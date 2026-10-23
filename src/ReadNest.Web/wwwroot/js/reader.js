// In-browser reader for a library entry's PDF or EPUB. The file is fetched from ReadNest's own content endpoint
// (owner-checked, same origin). PDF.js runs without eval; epub.js renders each chapter in a sandboxed iframe that
// is not allowed to run scripts. Position, active reading time and errors are reported to .NET, which saves them.

const readers = new WeakMap();
const IDLE_LIMIT_MS = 3 * 60 * 1000;
const TICK_MS = 15 * 1000;

let pdfjsPromise = null;
let epubPromise = null;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[data-reader-lib="${src}"]`);
        if (existing) {
            if (existing.dataset.loaded === "true") { resolve(); return; }
            existing.addEventListener("load", () => resolve());
            existing.addEventListener("error", () => reject(new Error("Could not load " + src)));
            return;
        }
        const script = document.createElement("script");
        script.src = src;
        script.async = false;
        script.dataset.readerLib = src;
        script.addEventListener("load", () => { script.dataset.loaded = "true"; resolve(); });
        script.addEventListener("error", () => reject(new Error("Could not load " + src)));
        document.head.appendChild(script);
    });
}

function loadPdfJs() {
    if (!pdfjsPromise) {
        pdfjsPromise = import("../lib/pdfjs/pdf.min.mjs").then(pdfjs => {
            pdfjs.GlobalWorkerOptions.workerSrc = new URL("../lib/pdfjs/pdf.worker.min.mjs", import.meta.url).href;
            return pdfjs;
        });
    }
    return pdfjsPromise;
}

function loadEpubJs() {
    if (!epubPromise) {
        const base = new URL("../lib/", import.meta.url).href;
        epubPromise = loadScript(base + "jszip/jszip.min.js")
            .then(() => loadScript(base + "epubjs/epub.min.js"))
            .then(() => window.ePub);
    }
    return epubPromise;
}

async function fetchBook(url, onProgress, signal) {
    const response = await fetch(url, { credentials: "same-origin", cache: "no-store", signal });
    if (response.status === 404) { throw Object.assign(new Error("missing"), { code: "missing" }); }
    if (response.status === 401 || response.status === 403) { throw Object.assign(new Error("denied"), { code: "denied" }); }
    if (!response.ok) { throw Object.assign(new Error("HTTP " + response.status), { code: "network" }); }
    const total = Number(response.headers.get("Content-Length")) || 0;
    if (!response.body || !total) { return await response.arrayBuffer(); }
    const reader = response.body.getReader();
    const buffer = new Uint8Array(total);
    let received = 0;
    for (;;) {
        const { done, value } = await reader.read();
        if (done) { break; }
        if (received + value.length > total) { throw Object.assign(new Error("size"), { code: "corrupt" }); }
        buffer.set(value, received);
        received += value.length;
        onProgress(Math.round(received * 100 / total));
    }
    return buffer.buffer;
}

// ---------- shared plumbing ----------

function trackActivity(state) {
    const touch = () => { state.lastInteraction = Date.now(); };
    const onVisibility = () => {
        if (document.visibilityState === "hidden") {
            state.dotnet.invokeMethodAsync("OnHidden", state.activeSeconds).catch(() => { });
        } else {
            touch();
        }
    };
    const onKey = event => {
        touch();
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) { return; }
        const tag = (event.target && event.target.tagName) || "";
        if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (event.target && event.target.isContentEditable)) { return; }
        if (event.target && event.target.closest && event.target.closest("[role=listbox], [role=combobox], dialog, .rn-reader-panel")) { return; }
        if (event.key === "ArrowRight" || event.key === "PageDown") { event.preventDefault(); state.next(); }
        else if (event.key === "ArrowLeft" || event.key === "PageUp") { event.preventDefault(); state.prev(); }
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", touch, true);
    document.addEventListener("wheel", touch, { passive: true, capture: true });
    document.addEventListener("visibilitychange", onVisibility);
    const timer = setInterval(() => {
        if (document.visibilityState === "visible" && Date.now() - state.lastInteraction < IDLE_LIMIT_MS) {
            state.activeSeconds += TICK_MS / 1000;
        }
        state.dotnet.invokeMethodAsync("OnHeartbeat", state.activeSeconds).catch(() => { });
    }, TICK_MS);
    state.cleanup.push(() => {
        clearInterval(timer);
        document.removeEventListener("keydown", onKey);
        document.removeEventListener("pointerdown", touch, true);
        document.removeEventListener("wheel", touch, { capture: true });
        document.removeEventListener("visibilitychange", onVisibility);
    });
    state.onKey = onKey;
}

function report(state, position) {
    state.lastInteraction = Date.now();
    state.dotnet.invokeMethodAsync("OnPosition", Object.assign({ activeSeconds: state.activeSeconds }, position)).catch(() => { });
}

function fail(state, code, error) {
    if (error && error.name === "AbortError") { return; }
    console.warn("ReadNest reader:", code, error && error.message);
    state.dotnet.invokeMethodAsync("OnError", code).catch(() => { });
}

// ---------- PDF ----------

async function openPdf(host, state, options) {
    const pdfjs = await loadPdfJs();
    const libBase = new URL("../lib/pdfjs/", import.meta.url).href;
    const task = pdfjs.getDocument({
        url: options.contentUrl,
        withCredentials: false,
        isEvalSupported: false,
        enableXfa: false,
        cMapUrl: libBase + "cmaps/",
        cMapPacked: true,
        standardFontDataUrl: libBase + "standard_fonts/",
    });
    task.onProgress = p => { if (p.total) { state.dotnet.invokeMethodAsync("OnLoading", Math.round(p.loaded * 100 / p.total)).catch(() => { }); } };
    state.cleanup.push(() => task.destroy());
    let doc;
    try {
        doc = await task.promise;
    } catch (error) {
        const name = error && error.name;
        if (name === "MissingPDFException" || (error && error.status === 404)) { fail(state, "missing", error); }
        else if (name === "PasswordException") { fail(state, "password", error); }
        else if (name === "InvalidPDFException") { fail(state, "corrupt", error); }
        else { fail(state, "network", error); }
        return;
    }
    state.doc = doc;
    state.pageCount = doc.numPages;
    state.page = Math.min(Math.max(1, options.page || 1), doc.numPages);
    state.zoom = 1;
    state.fitWidth = true;

    const page = document.createElement("div");
    page.className = "rn-pdf-page";
    const canvas = document.createElement("canvas");
    canvas.setAttribute("aria-hidden", "true");
    const text = document.createElement("div");
    text.className = "textLayer";
    page.append(canvas, text);
    host.replaceChildren(page);
    state.pageEl = page;
    state.canvas = canvas;
    state.textEl = text;

    let rendering = null;
    state.render = async () => {
        if (rendering) { rendering.cancel(); }
        const pdfPage = await doc.getPage(state.page);
        const base = pdfPage.getViewport({ scale: 1 });
        const available = Math.max(200, host.clientWidth - 16);
        const fit = Math.min(available / base.width, 2.5);
        const scale = (state.fitWidth ? fit : 1) * state.zoom;
        const viewport = pdfPage.getViewport({ scale });
        const ratio = window.devicePixelRatio || 1;
        canvas.width = Math.floor(viewport.width * ratio);
        canvas.height = Math.floor(viewport.height * ratio);
        canvas.style.width = Math.floor(viewport.width) + "px";
        canvas.style.height = Math.floor(viewport.height) + "px";
        page.style.width = Math.floor(viewport.width) + "px";
        page.style.height = Math.floor(viewport.height) + "px";
        page.style.setProperty("--scale-factor", String(scale));
        const context = canvas.getContext("2d");
        rendering = pdfPage.render({ canvasContext: context, viewport, transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : null });
        try {
            await rendering.promise;
        } catch (error) {
            if (error && error.name === "RenderingCancelledException") { return; }
            throw error;
        } finally {
            rendering = null;
        }
        text.replaceChildren();
        try {
            const layer = new pdfjs.TextLayer({ textContentSource: pdfPage.streamTextContent(), container: text, viewport });
            await layer.render();
        } catch (e) {
            // The text layer only adds selection and screen-reader text; the page is already drawn.
        }
        host.scrollTop = 0;
        report(state, {
            location: String(state.page),
            percent: Math.round(state.page * 10000 / state.pageCount) / 100,
            page: state.page,
            pageCount: state.pageCount,
            atEnd: state.page >= state.pageCount,
            chapter: null,
        });
    };
    state.go = async pageNumber => {
        const target = Math.min(Math.max(1, pageNumber | 0), state.pageCount);
        if (target === state.page && state.rendered) { return; }
        state.page = target;
        await state.render();
        state.rendered = true;
    };
    state.next = () => state.go(state.page + 1);
    state.prev = () => state.go(state.page - 1);
    state.zoomBy = async delta => {
        state.fitWidth = state.fitWidth && delta === 0;
        state.zoom = Math.min(4, Math.max(0.4, Math.round((state.zoom + delta) * 100) / 100));
        await state.render();
        return Math.round(state.zoom * 100);
    };
    state.fit = async () => { state.fitWidth = true; state.zoom = 1; await state.render(); return 100; };
    const resize = () => { clearTimeout(state.resizeTimer); state.resizeTimer = setTimeout(() => state.render(), 150); };
    window.addEventListener("resize", resize);
    state.cleanup.push(() => window.removeEventListener("resize", resize));

    state.dotnet.invokeMethodAsync("OnReady", { pageCount: state.pageCount, toc: [] }).catch(() => { });
    await state.render();
    state.rendered = true;
}

// ---------- EPUB ----------

const THEMES = {
    paper: { body: { color: "#201e1d", background: "#fbf7f0" }, a: { color: "#8c491a" } },
    sepia: { body: { color: "#3b2f20", background: "#f1e3c6" }, a: { color: "#7a4a1b" } },
    night: { body: { color: "#e6dfd2", background: "#1d1b17" }, a: { color: "#f6a06b" } },
};

function applyEpubPrefs(state, prefs) {
    const rendition = state.rendition;
    if (!rendition) { return; }
    const theme = (prefs.theme || "paper").toLowerCase();
    rendition.themes.select(THEMES[theme] ? theme : "paper");
    rendition.themes.fontSize((prefs.fontScale || 100) + "%");
    rendition.themes.override("line-height", String((prefs.lineHeight || 160) / 100), true);
}

function flattenToc(items, level, out) {
    (items || []).forEach(item => {
        out.push({ label: (item.label || "").trim() || "Untitled", href: item.href, level });
        flattenToc(item.subitems, level + 1, out);
    });
    return out;
}

async function openEpub(host, state, options) {
    let ePub;
    let data;
    try {
        [ePub, data] = await Promise.all([
            loadEpubJs(),
            fetchBook(options.contentUrl, p => state.dotnet.invokeMethodAsync("OnLoading", p).catch(() => { }), state.abort.signal),
        ]);
    } catch (error) {
        fail(state, error.code || "network", error);
        return;
    }

    let book;
    try {
        book = ePub(data);
        await book.ready;
    } catch (error) {
        fail(state, "corrupt", error);
        return;
    }
    state.book = book;
    state.cleanup.push(() => { try { book.destroy(); } catch (e) { } });

    const flow = (options.prefs.flow || "Paginated").toLowerCase() === "scrolled" ? "scrolled-doc" : "paginated";
    host.replaceChildren();
    const rendition = book.renderTo(host, {
        width: "100%",
        height: "100%",
        flow,
        spread: "none",
        allowScriptedContent: false,
    });
    state.rendition = rendition;
    Object.keys(THEMES).forEach(name => rendition.themes.register(name, THEMES[name]));
    applyEpubPrefs(state, options.prefs);

    // Keys pressed while the book's iframe has focus.
    rendition.on("keydown", event => state.onKey && state.onKey(event));
    rendition.on("click", () => { state.lastInteraction = Date.now(); });

    let locationsReady = false;
    rendition.on("relocated", location => {
        const start = location && location.start;
        if (!start) { return; }
        let percent = null;
        if (locationsReady && start.cfi) {
            try { percent = Math.round(book.locations.percentageFromCfi(start.cfi) * 10000) / 100; } catch (e) { percent = null; }
        }
        let chapter = null;
        try {
            const item = book.navigation && book.navigation.get(start.href);
            chapter = item ? (item.label || "").trim() : null;
        } catch (e) { chapter = null; }
        state.cfi = start.cfi;
        report(state, { location: start.cfi, percent, page: null, pageCount: null, atEnd: !!location.atEnd, chapter });
    });
    rendition.on("displayError", error => fail(state, "corrupt", error));

    state.next = () => rendition.next();
    state.prev = () => rendition.prev();
    state.goHref = href => rendition.display(href);

    const toc = flattenToc(book.navigation && book.navigation.toc, 0, []);
    state.dotnet.invokeMethodAsync("OnReady", { pageCount: null, toc }).catch(() => { });
    try {
        await rendition.display(options.location || undefined);
    } catch (error) {
        // A saved position that no longer fits the file: start from the beginning.
        try { await rendition.display(); } catch (e) { fail(state, "corrupt", e); return; }
    }

    // Percentages need the book's location map; build it in the background (it can take a moment).
    book.locations.generate(1600).then(() => {
        locationsReady = true;
        if (state.cfi) {
            const percent = Math.round(book.locations.percentageFromCfi(state.cfi) * 10000) / 100;
            report(state, { location: state.cfi, percent, page: null, pageCount: null, atEnd: false, chapter: null });
        }
    }).catch(() => { });

    const resize = () => { try { rendition.resize(); } catch (e) { } };
    window.addEventListener("resize", resize);
    state.cleanup.push(() => window.removeEventListener("resize", resize));
}

// ---------- API used by Reader.razor ----------

export async function open(host, options, dotnet) {
    dispose(host);
    // Resolve against <base href>; PDF.js would otherwise resolve relative URLs against the reader's own path.
    options.contentUrl = new URL(options.contentUrl, document.baseURI).href;
    const state = {
        dotnet,
        cleanup: [],
        abort: new AbortController(),
        activeSeconds: 0,
        lastInteraction: Date.now(),
        next: () => { },
        prev: () => { },
    };
    readers.set(host, state);
    state.cleanup.push(() => state.abort.abort());
    trackActivity(state);
    try {
        if (options.format === "pdf") { await openPdf(host, state, options); }
        else { await openEpub(host, state, options); }
    } catch (error) {
        fail(state, "corrupt", error);
    }
}

export function next(host) { const s = readers.get(host); if (s) { s.next(); } }
export function prev(host) { const s = readers.get(host); if (s) { s.prev(); } }
export function goToPage(host, page) { const s = readers.get(host); if (s && s.go) { s.go(page); } }
export function goToHref(host, href) { const s = readers.get(host); if (s && s.goHref) { s.goHref(href); } }
export async function zoom(host, delta) { const s = readers.get(host); return s && s.zoomBy ? await s.zoomBy(delta) : 100; }
export async function fitWidth(host) { const s = readers.get(host); return s && s.fit ? await s.fit() : 100; }

export function setPrefs(host, prefs) {
    const s = readers.get(host);
    if (s && s.rendition) {
        applyEpubPrefs(s, prefs);
        const flow = (prefs.flow || "Paginated").toLowerCase() === "scrolled" ? "scrolled-doc" : "paginated";
        if (s.flow !== flow) {
            s.flow = flow;
            try { s.rendition.flow(flow); s.rendition.display(s.cfi || undefined); } catch (e) { }
        }
    }
}

export function activeSeconds(host) { const s = readers.get(host); return s ? s.activeSeconds : 0; }

export async function toggleFullscreen(element) {
    try {
        if (document.fullscreenElement) { await document.exitFullscreen(); return false; }
        if (element.requestFullscreen) { await element.requestFullscreen(); return true; }
    } catch (e) { }
    return !!document.fullscreenElement;
}

export function dispose(host) {
    const s = readers.get(host);
    if (!s) { return; }
    readers.delete(host);
    s.cleanup.forEach(fn => { try { fn(); } catch (e) { } });
    s.cleanup = [];
}
