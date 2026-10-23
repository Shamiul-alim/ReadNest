// ReadNest client helpers. Kept small: Blazor Server does the heavy lifting. The shell (sidebar, More sheet,
// account menu, theme switch, recent searches) is rendered statically, so its behaviour lives here.
(function () {
    "use strict";

    var THEME_KEY = "rn-theme";
    var RECENT_KEY = "rn-recent-searches";
    var root = document.documentElement;

    function store(key, value) {
        try { if (value === null) { localStorage.removeItem(key); } else { localStorage.setItem(key, value); } } catch (e) { /* storage blocked */ }
    }
    function read(key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    }

    // ----- Theme: "light" (Paper), "dark" (Lamplight) or system default -----
    function systemDark() {
        return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
    }
    function effectiveTheme() {
        var explicit = root.getAttribute("data-theme");
        if (explicit === "light" || explicit === "dark") { return explicit; }
        return systemDark() ? "dark" : "light";
    }
    function applyTheme(choice) {
        if (choice === "light" || choice === "dark") {
            root.setAttribute("data-theme", choice);
            store(THEME_KEY, choice);
        } else {
            root.removeAttribute("data-theme");
            store(THEME_KEY, null);
        }
        syncThemeControls();
    }
    function syncThemeControls() {
        var dark = effectiveTheme() === "dark";
        document.querySelectorAll("[data-theme-label]").forEach(function (el) {
            el.textContent = dark ? "Paper theme" : "Lamplight theme";
        });
        document.querySelectorAll("[data-theme-toggle]").forEach(function (el) {
            if (!el.querySelector("[data-theme-label]")) {
                el.setAttribute("aria-label", dark ? "Switch to the Paper theme" : "Switch to the Lamplight theme");
            }
        });
        document.querySelectorAll("[data-theme-icon]").forEach(function (el) {
            // Show the moon while the page is light (switch to dark), the sun while it is dark.
            el.hidden = (el.getAttribute("data-theme-icon") === "dark") === dark;
        });
    }
    if (window.matchMedia) {
        try { window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", syncThemeControls); } catch (e) { /* old Safari */ }
    }

    // ----- More sheet -----
    var lastSheetTrigger = null;
    function sheetParts() {
        return { sheet: document.querySelector("[data-sheet]"), backdrop: document.querySelector(".rn-sheet-backdrop") };
    }
    function openSheet(trigger) {
        var parts = sheetParts();
        if (!parts.sheet) { return; }
        lastSheetTrigger = trigger || null;
        parts.sheet.hidden = false;
        if (parts.backdrop) { parts.backdrop.hidden = false; }
        document.querySelectorAll("[data-sheet-open]").forEach(function (b) { b.setAttribute("aria-expanded", "true"); });
        var first = parts.sheet.querySelector("a, button:not([data-sheet-close])");
        if (first) { first.focus(); }
    }
    function closeSheet(restoreFocus) {
        var parts = sheetParts();
        if (!parts.sheet || parts.sheet.hidden) { return; }
        parts.sheet.hidden = true;
        if (parts.backdrop) { parts.backdrop.hidden = true; }
        document.querySelectorAll("[data-sheet-open]").forEach(function (b) { b.setAttribute("aria-expanded", "false"); });
        if (restoreFocus && lastSheetTrigger && document.contains(lastSheetTrigger)) { lastSheetTrigger.focus(); }
    }

    // ----- Account menu -----
    function closeMenus(except) {
        document.querySelectorAll("[data-menu]").forEach(function (menu) {
            if (menu !== except && !menu.hidden) {
                menu.hidden = true;
                var toggle = menu.parentElement && menu.parentElement.querySelector("[data-menu-toggle]");
                if (toggle) { toggle.setAttribute("aria-expanded", "false"); }
            }
        });
    }
    function toggleMenu(button) {
        var menu = button.parentElement && button.parentElement.querySelector("[data-menu]");
        if (!menu) { return; }
        var open = menu.hidden;
        closeMenus(menu);
        menu.hidden = !open;
        button.setAttribute("aria-expanded", open ? "true" : "false");
        if (open) {
            var first = menu.querySelector("a, button");
            if (first) { first.focus(); }
        }
    }

    // ----- Recent searches (this device only; rendered as text, never HTML) -----
    function recentSearches() {
        try {
            var parsed = JSON.parse(read(RECENT_KEY) || "[]");
            return Array.isArray(parsed) ? parsed.filter(function (q) { return typeof q === "string" && q.length > 0; }).slice(0, 5) : [];
        } catch (e) { return []; }
    }
    function rememberSearch(query) {
        var q = (query || "").trim().slice(0, 200);
        if (!q) { return; }
        var list = recentSearches().filter(function (x) { return x.toLowerCase() !== q.toLowerCase(); });
        list.unshift(q);
        store(RECENT_KEY, JSON.stringify(list.slice(0, 5)));
    }
    function renderSuggestions(input) {
        var form = input.closest("[data-search-form]");
        var panel = form && form.querySelector("[data-suggest]");
        var list = panel && panel.querySelector("[data-suggest-list]");
        if (!panel || !list) { return; }
        var items = input.value.trim() ? [] : recentSearches();
        list.textContent = "";
        items.forEach(function (q) {
            var a = document.createElement("a");
            a.href = "explore?q=" + encodeURIComponent(q);
            a.setAttribute("data-recent", "");
            var label = document.createElement("span");
            label.textContent = q;
            a.appendChild(label);
            list.appendChild(a);
        });
        panel.hidden = items.length === 0;
        input.setAttribute("aria-expanded", items.length === 0 ? "false" : "true");
    }
    function hideSuggestions() {
        document.querySelectorAll("[data-suggest]").forEach(function (p) { p.hidden = true; });
    }

    // ----- Book covers: fade in when loaded, fall back to the typographic cover when missing -----
    function settleCover(img) {
        if (!img || !img.classList || !img.classList.contains("rn-cover-img")) { return; }
        if (img.complete) {
            if (img.naturalWidth > 0 && img.naturalWidth < 20) { img.classList.add("is-broken"); }
            else if (img.naturalWidth >= 20) { img.classList.add("is-loaded"); }
        }
    }
    function settleCovers() { document.querySelectorAll("img.rn-cover-img").forEach(settleCover); }
    document.addEventListener("load", function (event) { settleCover(event.target); }, true);
    document.addEventListener("error", function (event) {
        var target = event.target;
        if (target && target.tagName === "IMG" && target.classList.contains("rn-cover-img")) { target.classList.add("is-broken"); }
        // A profile photo that can't be shown (removed, private, offline) falls back to the initials beneath it.
        if (target && target.tagName === "IMG" && target.closest && target.closest(".rn-avatar, .rn-avatar-button")) { target.hidden = true; }
    }, true);

    // ----- Google Books embedded viewer (/reader/google/...) -----
    // Google's script is only allowed by that page's CSP, so this does nothing elsewhere.
    function initGoogleViewer() {
        var el = document.querySelector("[data-google-viewer]:not([data-started])");
        if (!el) { return; }
        el.setAttribute("data-started", "");
        var wrap = el.closest(".rn-google-viewer-wrap");
        var volume = el.getAttribute("data-google-viewer");
        var failed = function () { if (wrap) { wrap.classList.remove("is-ready"); wrap.classList.add("is-failed"); } };
        var ready = function () { if (wrap) { wrap.classList.add("is-ready"); } };
        var timer = setTimeout(failed, 20000);
        var start = function () {
            try {
                window.google.books.load();
                window.google.books.setOnLoadCallback(function () {
                    var viewer = new window.google.books.DefaultViewer(el);
                    viewer.load(volume, function () { clearTimeout(timer); failed(); }, function () { clearTimeout(timer); ready(); });
                });
            } catch (e) { clearTimeout(timer); failed(); }
        };
        if (window.google && window.google.books) { start(); return; }
        var script = document.createElement("script");
        script.src = "https://www.google.com/books/jsapi.js";
        script.onload = start;
        script.onerror = function () { clearTimeout(timer); failed(); };
        document.head.appendChild(script);
    }

    // ----- Password fields: show/hide toggle and a live checklist of the server's rules -----
    function updatePasswordRules(input) {
        var list = document.getElementById(input.getAttribute("data-pw-rules-for"));
        if (!list) { return; }
        var v = input.value || "";
        var checks = {
            length: v.length >= 8,
            "case": /[a-z]/.test(v) && /[A-Z]/.test(v),
            digit: /[0-9]/.test(v),
            symbol: /[^A-Za-z0-9]/.test(v)
        };
        list.querySelectorAll("[data-rule]").forEach(function (li) {
            var ok = !!checks[li.getAttribute("data-rule")];
            li.style.color = ok ? "var(--color-accent-2-800)" : "";
            li.style.fontWeight = ok ? "600" : "";
            li.setAttribute("data-met", ok ? "true" : "false");
        });
    }

    function fillTimeZoneInputs() {
        try {
            var zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
            document.querySelectorAll("input[data-timezone]").forEach(function (input) {
                if (!input.value) { input.value = zone || ""; }
            });
        } catch (e) { /* Older browsers: the server falls back to UTC. */ }
    }

    function afterRender() {
        fillTimeZoneInputs();
        syncThemeControls();
        settleCovers();
        closeSheet(false);
        closeMenus(null);
        hideSuggestions();
        initGoogleViewer();
    }

    document.addEventListener("DOMContentLoaded", afterRender);
    if (document.readyState !== "loading") { afterRender(); }

    document.addEventListener("click", function (event) {
        var t = event.target;
        if (!t || !t.closest) { return; }
        var pwToggle = t.closest("[data-pw-toggle]");
        if (pwToggle) {
            var field = document.getElementById(pwToggle.getAttribute("data-pw-toggle"));
            if (field) {
                var show = field.type === "password";
                field.type = show ? "text" : "password";
                pwToggle.setAttribute("aria-pressed", show ? "true" : "false");
                pwToggle.setAttribute("aria-label", show ? "Hide password" : "Show password");
            }
            return;
        }
        var themeButton = t.closest("[data-theme-toggle]");
        if (themeButton) {
            applyTheme(effectiveTheme() === "dark" ? "light" : "dark");
            return;
        }
        var opener = t.closest("[data-sheet-open]");
        if (opener) {
            var sheet = sheetParts().sheet;
            if (sheet && sheet.hidden) { openSheet(opener); } else { closeSheet(true); }
            return;
        }
        if (t.closest("[data-sheet-close]")) { closeSheet(true); return; }
        if (t.closest("[data-sheet] a")) { closeSheet(false); }

        var menuToggle = t.closest("[data-menu-toggle]");
        if (menuToggle) { toggleMenu(menuToggle); return; }
        if (!t.closest("[data-menu]")) { closeMenus(null); }
        if (t.closest("[data-menu] a")) { closeMenus(null); }

        if (!t.closest("[data-search-form]")) { hideSuggestions(); }
        if (t.closest("[data-recent]")) { hideSuggestions(); }
    });

    document.addEventListener("focusin", function (event) {
        var t = event.target;
        if (t && t.matches && t.matches("[data-search-input]")) { renderSuggestions(t); }
    });
    document.addEventListener("input", function (event) {
        var t = event.target;
        if (t && t.matches && t.matches("[data-search-input]")) { renderSuggestions(t); }
        if (t && t.matches && t.matches("[data-pw-rules-for]")) { updatePasswordRules(t); }
    });
    // External sign-in buttons show progress while the browser goes to the provider. The button is disabled after
    // the submit so its name/value is still sent, and re-enabled if the page is restored from the back cache.
    function resetExternalButtons() {
        document.querySelectorAll("[data-external-login] .is-loading").forEach(function (button) {
            button.classList.remove("is-loading");
            button.disabled = false;
            button.removeAttribute("aria-busy");
            var label = button.querySelector(".rn-google-label");
            if (label && button.dataset.originalLabel) { label.textContent = button.dataset.originalLabel; }
        });
    }
    window.addEventListener("pageshow", resetExternalButtons);
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form && form.matches && form.matches("[data-external-login]")) {
            var button = event.submitter || form.querySelector("button[type=submit]");
            if (button) {
                if (button.classList.contains("is-loading")) { event.preventDefault(); return; }
                button.classList.add("is-loading");
                button.setAttribute("aria-busy", "true");
                var label = button.querySelector(".rn-google-label");
                if (label && button.dataset.loadingLabel) {
                    button.dataset.originalLabel = label.textContent;
                    label.textContent = button.dataset.loadingLabel;
                }
                setTimeout(function () { button.disabled = true; }, 0);
            }
        }
    }, true);
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form && form.matches && form.matches("[data-search-form]")) {
            var input = form.querySelector("[data-search-input]");
            if (input) { rememberSearch(input.value); }
            hideSuggestions();
        }
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeSheet(true);
            var openMenu = document.querySelector("[data-menu]:not([hidden])");
            if (openMenu) {
                closeMenus(null);
                var toggle = openMenu.parentElement && openMenu.parentElement.querySelector("[data-menu-toggle]");
                if (toggle) { toggle.focus(); }
            }
            hideSuggestions();
        }
    });

    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", function () {
            var saved = read(THEME_KEY);
            if (saved === "light" || saved === "dark") { root.setAttribute("data-theme", saved); }
            afterRender();
        });
    }

    window.readnest = {
        showDialog: function (dialog, dotNetRef) {
            if (!dialog || typeof dialog.showModal !== "function") { return; }
            if (!dialog.open) { dialog.showModal(); }
            dialog.addEventListener("cancel", function (e) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("OnDialogCancelled");
            }, { once: true });
            // Backdrop click closes the dialog.
            dialog.addEventListener("click", function (e) {
                if (e.target === dialog) { dotNetRef.invokeMethodAsync("OnDialogCancelled"); }
            });
            var focusTarget = dialog.querySelector("[data-autofocus], [autofocus], .rn-dialog-body input:not([type=hidden]), .rn-dialog-body .rn-select-trigger, .rn-dialog-body select, .rn-dialog-body textarea, .rn-dialog-footer button.rn-btn-primary");
            if (focusTarget) {
                focusTarget.focus();
                if (typeof focusTarget.select === "function" && focusTarget.type === "number") { focusTarget.select(); }
            }
        },
        closeDialog: function (dialog) {
            if (dialog && dialog.open) { dialog.close(); }
        },
        setTitle: function (title) { document.title = title; },
        timeZone: function () {
            try { return Intl.DateTimeFormat().resolvedOptions().timeZone || null; } catch (e) { return null; }
        },
        copyText: async function (text) {
            try { await navigator.clipboard.writeText(text); return true; } catch (e) { return false; }
        },
        getTheme: function () { var t = read(THEME_KEY); return t === "light" || t === "dark" ? t : "system"; },
        setTheme: function (choice) { applyTheme(choice); },
        rememberSearch: rememberSearch,
        recentSearches: recentSearches,
        settleCovers: settleCovers,
        // The account button lives in the statically rendered layout; interactive pages update it in place.
        setAccountAvatar: function (url, initials) {
            document.querySelectorAll(".rn-avatar-button").forEach(function (button) {
                var img = button.querySelector("img");
                var label = button.querySelector(".rn-avatar-initials");
                if (label) { label.textContent = initials || ""; }
                if (!url) { if (img) { img.remove(); } return; }
                if (!img) {
                    img = document.createElement("img");
                    img.alt = "";
                    img.referrerPolicy = "no-referrer";
                    button.appendChild(img);
                }
                img.hidden = false;
                img.src = url;
            });
        }
    };
})();
