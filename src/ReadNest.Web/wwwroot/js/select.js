// Keyboard, focus, positioning and dismissal for <AppSelect>. The component (in .NET) owns the value and the
// open/closed state; this module only handles what Blazor can't do well: per-key preventDefault, highlighting
// without a round trip, placing the popup in the viewport, and closing on outside clicks.
// Pattern: WAI-ARIA APG "select-only combobox" (focus stays on the trigger, aria-activedescendant points at the
// highlighted option).

const states = new WeakMap();
const supportsPopover = typeof HTMLElement !== "undefined" && HTMLElement.prototype.hasOwnProperty("popover");

function options(root) {
    return Array.from(root.querySelectorAll("[role=option]"));
}

function enabled(option) {
    return option.getAttribute("aria-disabled") !== "true";
}

function focusTarget(root) {
    return root.querySelector(".rn-select-search") || root.querySelector(".rn-select-trigger");
}

function setActive(root, index, scroll) {
    const list = options(root);
    list.forEach((option, i) => {
        if (i === index) { option.setAttribute("data-active", ""); } else { option.removeAttribute("data-active"); }
    });
    const state = states.get(root);
    if (state) { state.active = index; }
    const active = list[index];
    const owner = focusTarget(root);
    if (owner) {
        if (active) { owner.setAttribute("aria-activedescendant", active.id); } else { owner.removeAttribute("aria-activedescendant"); }
    }
    if (active && scroll) { active.scrollIntoView({ block: "nearest" }); }
}

function step(root, from, delta) {
    const list = options(root);
    if (list.length === 0) { return -1; }
    let i = from;
    for (let n = 0; n < list.length; n++) {
        i = Math.min(list.length - 1, Math.max(0, i + delta));
        if (enabled(list[i])) { return i; }
        if (i === 0 || i === list.length - 1) { break; }
    }
    return enabled(list[from] || {}) ? from : -1;
}

function firstEnabled(root, fromEnd) {
    const list = options(root);
    const order = fromEnd ? list.map((_, i) => list.length - 1 - i) : list.map((_, i) => i);
    for (const i of order) { if (enabled(list[i])) { return i; } }
    return -1;
}

function selectedIndex(root) {
    return options(root).findIndex(o => o.getAttribute("aria-selected") === "true");
}

function place(root) {
    const popup = root.querySelector(".rn-select-popup");
    const trigger = root.querySelector(".rn-select-trigger");
    if (!popup || !trigger) { return; }
    const rect = trigger.getBoundingClientRect();
    const viewport = window.visualViewport;
    const vh = viewport ? viewport.height : window.innerHeight;
    const vw = viewport ? viewport.width : window.innerWidth;
    const gap = 6;
    const margin = 12;
    const below = vh - rect.bottom - gap - margin;
    const above = rect.top - gap - margin;
    const natural = Math.min(popup.scrollHeight || 320, 320);
    const openUp = below < Math.min(natural, 200) && above > below;
    const maxHeight = Math.max(120, Math.min(320, openUp ? above : below));
    const width = Math.min(Math.max(rect.width, 200), vw - margin * 2);
    let left = Math.min(Math.max(margin, rect.left), vw - width - margin);
    popup.style.setProperty("--rn-select-max", maxHeight + "px");
    popup.dataset.placement = openUp ? "top" : "bottom";
    if (popup.matches(":popover-open") || popup.dataset.layer === "fixed") {
        popup.style.position = "fixed";
        popup.style.width = width + "px";
        popup.style.left = left + "px";
        if (openUp) {
            popup.style.top = "auto";
            popup.style.bottom = (vh - rect.top + gap) + "px";
        } else {
            popup.style.bottom = "auto";
            popup.style.top = (rect.bottom + gap) + "px";
        }
    }
}

function typeahead(root, key) {
    const state = states.get(root);
    if (!state) { return; }
    clearTimeout(state.typeTimer);
    state.typed = (state.typed || "") + key.toLowerCase();
    state.typeTimer = setTimeout(() => { state.typed = ""; }, 600);
    const list = options(root);
    const start = state.typed.length === 1 ? (state.active ?? -1) + 1 : Math.max(0, state.active ?? 0);
    for (let n = 0; n < list.length; n++) {
        const i = (start + n) % list.length;
        const text = (list[i].dataset.label || list[i].textContent || "").trim().toLowerCase();
        if (enabled(list[i]) && text.startsWith(state.typed)) { setActive(root, i, true); return; }
    }
}

// Sends the highlighted option's stable index (its position in the full list, which filtering doesn't change).
function choose(root, state, active) {
    const option = options(root)[active];
    if (option && enabled(option)) { state.dotnet.invokeMethodAsync("SelectIndex", Number(option.dataset.index)); }
}

function onKeyDown(event) {
    const root = event.currentTarget;
    const state = states.get(root);
    if (!state || root.querySelector(".rn-select-trigger").disabled) { return; }
    const open = root.classList.contains("is-open");
    const inSearch = event.target.classList && event.target.classList.contains("rn-select-search");
    const key = event.key;

    if (!open) {
        if (key === "ArrowDown" || key === "ArrowUp" || key === "Enter" || key === " " || (key === "Home") || (key === "End")) {
            if (event.target.classList.contains("rn-select-clear")) { return; }
            event.preventDefault();
            state.pendingHighlight = key === "ArrowUp" || key === "End" ? "last" : (key === "Home" ? "first" : "selected");
            state.dotnet.invokeMethodAsync("OpenFromKeyboard");
        } else if (key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey && key !== " " && !event.target.classList.contains("rn-select-clear")) {
            // Keep everything typed before the list has opened.
            const opening = !!state.pendingType;
            state.pendingType = (state.pendingType || "") + key;
            if (!opening) { state.dotnet.invokeMethodAsync("OpenFromKeyboard"); }
        }
        return;
    }

    const active = state.active ?? -1;
    switch (key) {
        case "ArrowDown":
            event.preventDefault();
            if (event.altKey) { break; }
            setActive(root, active < 0 ? firstEnabled(root, false) : step(root, active, 1), true);
            break;
        case "ArrowUp":
            event.preventDefault();
            if (event.altKey) {
                choose(root, state, active);
                break;
            }
            setActive(root, active < 0 ? firstEnabled(root, true) : step(root, active, -1), true);
            break;
        case "PageDown":
            event.preventDefault();
            setActive(root, step(root, Math.max(active, 0), 8), true);
            break;
        case "PageUp":
            event.preventDefault();
            setActive(root, step(root, Math.max(active, 0), -8), true);
            break;
        case "Home":
            if (inSearch) { return; }
            event.preventDefault();
            setActive(root, firstEnabled(root, false), true);
            break;
        case "End":
            if (inSearch) { return; }
            event.preventDefault();
            setActive(root, firstEnabled(root, true), true);
            break;
        case "Enter":
            event.preventDefault();
            // While a new filter is being applied the highlight is out of date: choose once the list refreshes.
            if (state.stale) { state.enterPending = true; break; }
            choose(root, state, active);
            break;
        case " ":
            if (inSearch) { return; }
            event.preventDefault();
            choose(root, state, active);
            break;
        case "Escape":
            // Close only the list; a surrounding dialog stays open.
            event.preventDefault();
            event.stopPropagation();
            state.dotnet.invokeMethodAsync("CloseFromKeyboard", true);
            break;
        case "Tab":
            // Let focus move on; just close the list without changing the value.
            state.dotnet.invokeMethodAsync("CloseFromKeyboard", false);
            break;
        default:
            if (!inSearch && key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) { typeahead(root, key); }
    }
}

export function init(root, dotnet) {
    if (!root || states.has(root)) { return; }
    const state = { dotnet, active: -1, listeners: [] };
    states.set(root, state);
    root.addEventListener("keydown", onKeyDown);
    root.addEventListener("input", event => {
        if (event.target.classList && event.target.classList.contains("rn-select-search")) {
            state.stale = true;
            setActive(root, -1, false);
        }
    });
    // Hovering an option highlights it, like keyboard navigation does.
    root.addEventListener("pointermove", event => {
        const option = event.target.closest && event.target.closest("[role=option]");
        if (option && enabled(option)) {
            const index = options(root).indexOf(option);
            if (index !== state.active) { setActive(root, index, false); }
        }
    });
}

export function open(root) {
    const state = states.get(root);
    if (!state) { return; }
    const popup = root.querySelector(".rn-select-popup");
    if (popup) {
        if (supportsPopover && popup.hasAttribute("popover")) {
            // The popover sits in the top layer, so dialogs and scroll containers never clip it.
            if (!popup.matches(":popover-open")) { try { popup.showPopover(); } catch (e) { } }
        } else {
            popup.dataset.layer = "inline";
        }
    }
    place(root);

    let index = selectedIndex(root);
    if (state.pendingHighlight === "first" || index < 0) { index = firstEnabled(root, false); }
    if (state.pendingHighlight === "last") { index = firstEnabled(root, true); }
    state.pendingHighlight = null;
    setActive(root, index, true);
    const search = root.querySelector(".rn-select-search");
    if (state.pendingType) {
        if (search) {
            // Typing on a closed searchable select starts the search.
            search.value = state.pendingType;
            search.dispatchEvent(new Event("input", { bubbles: true }));
        } else {
            for (const ch of state.pendingType) { typeahead(root, ch); }
        }
        state.pendingType = null;
    }
    if (search) { search.focus({ preventScroll: true }); }

    if (!state.listeners.length) {
        const outside = event => {
            if (!root.contains(event.target)) { state.dotnet.invokeMethodAsync("CloseFromOutside"); }
        };
        const reposition = () => place(root);
        document.addEventListener("pointerdown", outside, true);
        window.addEventListener("resize", reposition);
        window.addEventListener("scroll", reposition, true);
        state.listeners = [
            () => document.removeEventListener("pointerdown", outside, true),
            () => window.removeEventListener("resize", reposition),
            () => window.removeEventListener("scroll", reposition, true),
        ];
    }
}

// Called after the option list changed while open (search filtering): keep a valid highlight.
export function refresh(root) {
    const state = states.get(root);
    if (!state || !root.classList.contains("is-open")) { return; }
    place(root);
    const list = options(root);
    const current = state.active ?? -1;
    setActive(root, current >= 0 && current < list.length && enabled(list[current]) ? current : firstEnabled(root, false), true);
    state.stale = false;
    if (state.enterPending) {
        state.enterPending = false;
        choose(root, state, state.active);
    }
}

export function close(root, restoreFocus) {
    const state = states.get(root);
    if (!state) { return; }
    state.listeners.forEach(remove => remove());
    state.listeners = [];
    const popup = root.querySelector(".rn-select-popup");
    if (popup && supportsPopover && popup.hasAttribute("popover") && popup.matches(":popover-open")) {
        try { popup.hidePopover(); } catch (e) { }
    }
    const owner = root.querySelector(".rn-select-trigger");
    if (owner) { owner.removeAttribute("aria-activedescendant"); }
    if (restoreFocus && owner) { owner.focus({ preventScroll: true }); }
}

export function dispose(root) {
    const state = states.get(root);
    if (!state) { return; }
    close(root, false);
    root.removeEventListener("keydown", onKeyDown);
    states.delete(root);
}
