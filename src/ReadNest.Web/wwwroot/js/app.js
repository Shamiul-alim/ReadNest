// ReadNest client helpers. Kept intentionally small: Blazor Server does the heavy lifting.
(function () {
    "use strict";

    // Hide broken cover images so the generated cover underneath shows instead.
    document.addEventListener("error", function (event) {
        var target = event.target;
        if (target && target.tagName === "IMG" && target.classList.contains("rn-cover-img")) {
            target.classList.add("is-broken");
        }
    }, true);

    function fillTimeZoneInputs() {
        try {
            var zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
            document.querySelectorAll("input[data-timezone]").forEach(function (input) {
                if (!input.value) { input.value = zone || ""; }
            });
        } catch (e) { /* Older browsers: the server falls back to UTC. */ }
    }

    function closeMobileNav() {
        var toggle = document.getElementById("rn-nav-toggle");
        if (toggle) { toggle.checked = false; }
        document.querySelectorAll("details.rn-account[open]").forEach(function (d) { d.removeAttribute("open"); });
    }

    document.addEventListener("DOMContentLoaded", fillTimeZoneInputs);
    document.addEventListener("click", function (event) {
        var toggleButton = event.target.closest && event.target.closest("[data-nav-toggle]");
        if (toggleButton) {
            var toggle = document.getElementById("rn-nav-toggle");
            if (toggle) {
                toggle.checked = !toggle.checked;
                toggleButton.setAttribute("aria-expanded", toggle.checked ? "true" : "false");
                if (toggle.checked) {
                    var firstLink = document.querySelector(".rn-sidebar a");
                    if (firstLink) { firstLink.focus(); }
                }
            }
            return;
        }

        // Close the account menu when clicking outside it.
        document.querySelectorAll("details.rn-account[open]").forEach(function (d) {
            if (!d.contains(event.target)) { d.removeAttribute("open"); }
        });
        if (event.target.closest && event.target.closest(".rn-sidebar a")) { closeMobileNav(); }
    });
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") { closeMobileNav(); }
    });

    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", function () { fillTimeZoneInputs(); closeMobileNav(); });
    }

    window.readnest = {
        showDialog: function (dialog, dotNetRef) {
            if (!dialog || typeof dialog.showModal !== "function") { return; }
            if (!dialog.open) { dialog.showModal(); }
            dialog.addEventListener("cancel", function (e) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("OnDialogCancelled");
            }, { once: true });
            var focusTarget = dialog.querySelector("[autofocus], input, select, textarea, button.rn-btn-primary");
            if (focusTarget) { focusTarget.focus(); }
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
        }
    };
})();
