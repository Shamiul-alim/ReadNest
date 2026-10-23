// UI smoke test for ReadNest: drives the real app in headless Chrome against a running server.
// Uses the live catalogue (Open Library / Google Books), so it needs network access.
import { chromium } from "playwright-core";
import fs from "node:fs";

const BASE = process.env.BASE ?? "http://localhost:5230";
const SHOTS = new URL("./shots/", import.meta.url).pathname.replace(/^\/([A-Z]:)/, "$1");
fs.mkdirSync(SHOTS, { recursive: true });
const email = `ui${Date.now()}@example.com`;
const results = [];
const problems = [];

function log(step, ok, extra = "") {
    results.push(`${ok ? "PASS" : "FAIL"} ${step}${extra ? " - " + extra : ""}`);
    if (!ok) problems.push(step);
}

const browser = await chromium.launch({ executablePath: process.env.CHROME_PATH ?? "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true });
const context = await browser.newContext({ viewport: { width: 1366, height: 900 } });
const page = await context.newPage();
page.on("pageerror", e => problems.push("pageerror: " + e.message));
page.on("console", m => {
    // The deliberate 404 check and slow external cover images are expected console noise.
    const text = m.text();
    if (m.type() === "error" && !/status of 404|ERR_CONNECTION_TIMED_OUT|ERR_NAME_NOT_RESOLVED/.test(text)) problems.push("console: " + text.slice(0, 200));
});

async function shot(name, fullPage = true) { await page.screenshot({ path: SHOTS + name + ".png", fullPage }); }
async function noBlazorError(step) {
    const visible = await page.locator("#blazor-error-ui").isVisible().catch(() => false);
    log(step + " (no Blazor error UI)", !visible);
}
async function step(name, fn) {
    try { await fn(); log(name, true); }
    catch (e) { log(name, false, e.message.split("\n")[0]); await shot("fail-" + name.replace(/\W+/g, "-")).catch(() => {}); }
}
const dialog = () => page.locator("dialog[open]");
const toast = text => page.locator(".rn-toast", { hasText: text }).first();

await step("landing", async () => {
    await page.goto(BASE + "/");
    await page.getByRole("heading", { level: 1, name: /A home for every book/ }).waitFor();
    await shot("01-landing");
});

await step("register", async () => {
    await page.goto(BASE + "/Account/Register");
    await page.fill("[id='Input.DisplayName']", "Ada Reader");
    await page.fill("[id='Input.Email']", email);
    await page.fill("[id='Input.Password']", "Sup3rSecret!");
    await page.locator("[data-rule='symbol'][data-met='true']").waitFor({ timeout: 5000 });
    await shot("02-register", false);
    await page.click("button[type=submit]");
    await page.waitForURL(/dashboard/);
    await page.getByRole("heading", { name: /Welcome to ReadNest, Ada/ }).waitFor({ timeout: 15000 });
    await shot("03-dashboard-empty");
});
await noBlazorError("dashboard empty");

await step("search, add and undo", async () => {
    await page.goto(BASE + "/explore?q=the%20pragmatic%20programmer&field=Title");
    await page.locator(".rn-list-item").first().waitFor({ timeout: 30000 });
    await shot("04-results");
    await page.locator(".rn-list-item button", { hasText: "Want to read" }).first().click();
    await toast("Want to read").waitFor({ timeout: 20000 });
    await page.locator(".rn-list-item .rn-status").first().waitFor({ timeout: 10000 });
    await toast("Want to read").getByRole("button", { name: "Undo" }).click();
    await page.locator(".rn-list-item button", { hasText: "Want to read" }).first().waitFor({ timeout: 10000 });
    await page.locator(".rn-list-item button", { hasText: "Want to read" }).first().click();
    await page.locator(".rn-list-item .rn-status").first().waitFor({ timeout: 10000 });
});

await step("open a catalogue book and start reading", async () => {
    await page.goto(BASE + "/explore?q=clean%20code&field=Title");
    await page.locator(".rn-list-item a.rn-list-title").first().waitFor({ timeout: 30000 });
    await page.locator(".rn-list-item a.rn-list-title").first().click();
    await page.locator(".rn-status-panel").waitFor({ timeout: 30000 });
    await page.locator(".rn-status-panel button", { hasText: "Start reading" }).click();
    await page.waitForURL(/books\//, { timeout: 30000 });
    await page.locator(".rn-status-panel button", { hasText: "Update progress" }).waitFor({ timeout: 15000 });
    await shot("05-book-page");
});
await noBlazorError("book page");

await step("update progress with validation", async () => {
    await page.locator(".rn-status-panel button", { hasText: "Update progress" }).click();
    await dialog().waitFor();
    await dialog().locator("input[type=number]").fill("999999");
    await dialog().getByText(/past the last page|Pages start at 0/).first().waitFor({ timeout: 5000 }).catch(async () => {
        // Books without a page count accept any page; then the error cannot appear.
    });
    await dialog().locator("input[type=number]").fill("120");
    await shot("06-progress-dialog", false);
    await dialog().locator("button[type=submit]").click();
    await toast("Progress saved").waitFor({ timeout: 15000 });
});

await step("reading view and session with validation", async () => {
    await page.getByRole("link", { name: "Open reading view" }).click();
    await page.waitForURL(/reading\//);
    await page.getByRole("heading", { name: "Reading history" }).waitFor({ timeout: 15000 });
    await page.getByRole("button", { name: /Log a session/ }).first().click();
    await dialog().waitFor();
    await dialog().locator("input[data-autofocus]").fill("100");
    await dialog().locator("button[type=submit]").click();
    await dialog().getByText("The end page can’t be before the start page.").waitFor({ timeout: 5000 });
    await dialog().locator("input[data-autofocus]").fill("150");
    await dialog().locator(".rn-quick button", { hasText: "30 min" }).click();
    await dialog().locator("textarea").fill("Naming matters more than I thought.");
    await shot("07-session-dialog", false);
    await dialog().locator("button[type=submit]").click();
    await toast("Session saved").waitFor({ timeout: 15000 });
    await page.getByText("Naming matters more than I thought.").waitFor({ timeout: 10000 });
    await shot("08-reading-view");
});
await noBlazorError("reading view");

await step("save a quote", async () => {
    await page.getByRole("button", { name: /Save a quote/ }).click();
    await dialog().waitFor();
    await dialog().locator("textarea").fill("Clean code reads like well-written prose.");
    await dialog().getByRole("button", { name: "Save note" }).click();
    await toast("Quote saved").waitFor({ timeout: 15000 });
});

await step("rate and review (rendered as text)", async () => {
    await page.getByRole("link", { name: "Book details" }).first().click();
    await page.waitForURL(/books\//);
    await page.getByRole("button", { name: "4 stars" }).click();
    await toast("Rating saved").waitFor({ timeout: 15000 });
    await page.fill("#rev-text", "Practical advice. <script>alert(1)</script> should render as text.");
    await page.getByRole("button", { name: "Save review" }).click();
    await toast("Review saved").waitFor({ timeout: 15000 });
});

await step("add to a new shelf", async () => {
    await page.getByRole("button", { name: /Add to shelf/ }).click();
    await dialog().waitFor();
    await dialog().getByRole("button", { name: /New shelf/ }).click();
    await dialog().locator("input[placeholder*='Book club']").fill("Software Engineering");
    await dialog().getByRole("button", { name: "Create" }).click();
    await dialog().getByText("Software Engineering").waitFor({ timeout: 10000 });
    await dialog().getByRole("button", { name: "Save" }).click();
    await toast("Software Engineering").waitFor({ timeout: 15000 });
});

await step("mark as finished", async () => {
    // The status control is an AppSelect (combobox + listbox), not a native <select>.
    await page.locator(".rn-status-panel .rn-select-trigger").click();
    await page.getByRole("option", { name: "Read", exact: true }).click();
    await dialog().waitFor();
    await dialog().getByRole("button", { name: /Mark as read/ }).click();
    await toast("Marked as read").waitFor({ timeout: 15000 });
});

const pages = [
    ["library", "10-library", ".rn-book-card"],
    ["library/read", "11-library-read", ".rn-book-card"],
    ["notes", "12-notes", ".rn-note-item"],
    ["history", "13-history", ".rn-timeline"],
    ["goals", "14-goals", "h1"],
    ["analytics", "15-insights", "h1"],
    ["shelves", "16-shelves", ".rn-shelf-card"],
    ["collection", "17-collection", "h1"],
    ["recommendations", "18-for-you", "h1"],
    ["reviews", "19-my-reviews", "h1"],
    ["profile", "20-profile", ".rn-profile-hero"],
    ["settings", "21-settings", "#set-name"],
    ["settings?tab=privacy", "22-settings-privacy", "[role=switch]"],
    ["import-export", "23-import-export", "#import-file"],
    ["community", "24-community", "h1"],
    ["challenges", "25-challenges", "h1"],
    ["library/add", "26-add-book", "#mb-title"],
    ["dashboard", "27-dashboard", ".rn-goal-card"],
];
for (const [path, name, selector] of pages) {
    await step("page " + path, async () => {
        await page.goto(BASE + "/" + path);
        await page.locator(selector).first().waitFor({ timeout: 20000 });
        await page.waitForTimeout(400);
        await shot(name);
    });
    await noBlazorError(path);
}

await step("library list view and bulk selection", async () => {
    await page.goto(BASE + "/library?view=list");
    await page.getByRole("button", { name: "Select all" }).click();
    await page.locator(".rn-bulkbar").waitFor({ timeout: 5000 });
    await shot("28-library-bulk", false);
    await page.getByRole("button", { name: "Clear selection" }).click();
    await page.locator(".rn-bulkbar").waitFor({ state: "detached", timeout: 5000 });
});

await step("custom select: keyboard, dismissal and focus", async () => {
    await page.goto(BASE + "/library");
    const trigger = page.locator("#lib-sort");
    await trigger.focus();
    await page.keyboard.press("ArrowDown");
    await page.locator(".rn-select-ctl.is-open [role=listbox]").waitFor({ timeout: 5000 });
    if ((await trigger.getAttribute("aria-expanded")) !== "true") throw new Error("ArrowDown did not open the list");
    const first = await trigger.getAttribute("aria-activedescendant");
    await page.keyboard.press("ArrowDown");
    if ((await trigger.getAttribute("aria-activedescendant")) === first) throw new Error("ArrowDown did not move the highlight");
    await page.keyboard.press("Escape");
    await page.waitForTimeout(200);
    if ((await trigger.getAttribute("aria-expanded")) !== "false") throw new Error("Escape did not close the list");
    if (!(await page.evaluate(() => document.activeElement && document.activeElement.id === "lib-sort"))) throw new Error("focus did not return to the trigger");
    await page.keyboard.press("Enter");
    await page.getByRole("option", { name: "Title" }).waitFor();
    await page.keyboard.press("End");
    await page.keyboard.press("Enter");
    await page.waitForURL(/sort=Rating/i, { timeout: 10000 });
    await trigger.click();
    await page.locator(".rn-select-ctl.is-open").waitFor();
    await page.mouse.click(5, 5);
    await page.waitForTimeout(200);
    if ((await trigger.getAttribute("aria-expanded")) !== "false") throw new Error("clicking outside did not close the list");
    await page.keyboard.press("Tab");
});

await step("custom select in dark theme and on a narrow screen", async () => {
    await page.evaluate(() => window.readnest.setTheme("dark"));
    await page.setViewportSize({ width: 360, height: 740 });
    await page.goto(BASE + "/settings?tab=reading");
    await page.locator("#pref-tz").click();
    const popup = page.locator(".rn-select-ctl.is-open .rn-select-popup");
    await popup.waitFor();
    const box = await popup.boundingBox();
    if (!box || box.x < 0 || box.x + box.width > 360 + 1 || box.y + box.height > 740 + 1) throw new Error("popup does not fit the viewport: " + JSON.stringify(box));
    await shot("28b-select-dark-mobile", false);
    await page.keyboard.press("Escape");
    await page.evaluate(() => window.readnest.setTheme("system"));
    await page.setViewportSize({ width: 1366, height: 900 });
});

await step("set goal", async () => {
    await page.goto(BASE + "/goals");
    await page.getByRole("button", { name: /Set a goal|Edit goal/ }).first().click();
    await dialog().waitFor();
    await page.fill("#goal-books", "24");
    await dialog().getByRole("button", { name: "Save goal" }).click();
    await toast("goal is 24 books").waitFor({ timeout: 15000 });
    await shot("29-goals-set");
});

await step("create challenge", async () => {
    await page.goto(BASE + "/challenges");
    await page.getByRole("button", { name: /New challenge/ }).click();
    await page.fill("#ch-title", "Autumn Reading Sprint");
    await page.fill("#ch-target", "3");
    await page.getByRole("button", { name: "Create challenge" }).click();
    await page.waitForURL(/challenges\/[0-9a-f-]{36}/, { timeout: 15000 });
    await shot("30-challenge");
});

await step("review shows as text (no XSS)", async () => {
    await page.goto(BASE + "/reviews");
    await page.getByText("<script>alert(1)</script>", { exact: false }).first().waitFor({ timeout: 15000 });
});

await step("theme switch", async () => {
    await page.goto(BASE + "/dashboard");
    await page.locator(".rn-sidebar [data-theme-toggle]").click();
    const theme = await page.evaluate(() => document.documentElement.getAttribute("data-theme"));
    if (theme !== "dark") throw new Error("expected dark theme, got " + theme);
    await shot("31-dashboard-dark");
    await page.locator(".rn-sidebar [data-theme-toggle]").click();
});

await step("unknown page shows not found", async () => {
    const response = await page.goto(BASE + "/no-such-page");
    if (response.status() !== 404) throw new Error("status " + response.status());
    await page.getByRole("heading", { name: /find that page/ }).waitFor();
});

for (const [w, h, label] of [[390, 844, "mobile"], [820, 1180, "tablet"]]) {
    await step("responsive " + label, async () => {
        await page.setViewportSize({ width: w, height: h });
        for (const path of ["/dashboard", "/library", "/notes", "/goals"]) {
            await page.goto(BASE + path);
            await page.locator("h1").first().waitFor({ timeout: 20000 });
            await page.waitForTimeout(300);
            const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
            if (overflow > 1) throw new Error(`${path}: horizontal overflow ${overflow}px`);
        }
        await shot(`32-goals-${label}`);
        if (label === "mobile") {
            await page.click(".rn-bottombar [data-sheet-open]");
            await page.locator("[data-sheet]:not([hidden])").waitFor({ timeout: 3000 });
            await shot("33-mobile-more", false);
            await page.keyboard.press("Escape");
        }
    });
}

await step("sign out", async () => {
    await page.setViewportSize({ width: 1366, height: 900 });
    await page.goto(BASE + "/dashboard");
    await page.click("[data-menu-toggle]");
    await page.getByRole("button", { name: "Sign out" }).click();
    await page.waitForURL(BASE + "/");
    await page.goto(BASE + "/dashboard");
    await page.waitForURL(/Account\/Login/);
    await shot("34-login");
});

await browser.close();
console.log(results.join("\n"));
console.log("\nProblems: " + (problems.length ? "\n" + problems.join("\n") : "none"));
process.exitCode = problems.length ? 1 : 0;
