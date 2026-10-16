// UI smoke test for ReadNest: drives the real app in headless Chrome.
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
page.on("console", m => { if (m.type() === "error") problems.push("console: " + m.text().slice(0, 200)); });

async function shot(name, fullPage = true) { await page.screenshot({ path: SHOTS + name + ".png", fullPage }); }
async function noBlazorError(step) {
    const visible = await page.locator("#blazor-error-ui").isVisible().catch(() => false);
    log(step + " (no Blazor error UI)", !visible);
}
async function step(name, fn) {
    try { await fn(); log(name, true); }
    catch (e) { log(name, false, e.message.split("\n")[0]); await shot("fail-" + name.replace(/\W+/g, "-")).catch(() => {}); }
}

await step("landing", async () => {
    await page.goto(BASE + "/");
    await page.getByRole("heading", { level: 1 }).waitFor();
    await shot("01-landing");
});

await step("register", async () => {
    await page.goto(BASE + "/Account/Register");
    await page.fill("#Input\\.DisplayName", "Ada Reader");
    await page.fill("#Input\\.Email", email);
    await page.fill("#Input\\.Password", "Sup3rSecret!");
    await page.fill("#Input\\.ConfirmPassword", "Sup3rSecret!");
    await shot("02-register", false);
    await page.click("button[type=submit]");
    await page.waitForURL(/dashboard/);
    await page.getByText("Your library is empty").waitFor({ timeout: 15000 });
    await shot("03-dashboard-empty");
});
await noBlazorError("dashboard empty");

await step("explore search + add", async () => {
    await page.goto(BASE + "/explore?q=the%20pragmatic%20programmer&field=Title");
    await page.locator(".rn-book-card").first().waitFor({ timeout: 30000 });
    await shot("04-explore-results");
    await page.getByRole("button", { name: /Want to read/ }).first().click();
    await page.locator(".rn-toast").first().waitFor({ timeout: 20000 });
    await page.locator(".rn-book-card .rn-status").first().waitFor({ timeout: 10000 });
});

await step("explore second book", async () => {
    await page.goto(BASE + "/explore?q=clean%20code&field=Title");
    await page.locator(".rn-book-card").first().waitFor({ timeout: 30000 });
    await page.locator(".rn-book-card-link").first().click();
    await page.waitForURL(/explore\/|books\//);
    await page.getByRole("button", { name: /Add to library/ }).waitFor({ timeout: 30000 });
    await page.selectOption("#add-status", "CurrentlyReading");
    await page.getByRole("button", { name: /Add to library/ }).click();
    await page.waitForURL(/books\//, { timeout: 30000 });
    await page.getByRole("heading", { name: "Your reading" }).waitFor({ timeout: 15000 });
    await shot("05-book-details");
});
await noBlazorError("book details");

await step("update progress", async () => {
    await page.fill("#progress-page", "120");
    await page.fill("#progress-minutes", "40");
    await page.getByRole("button", { name: "Update progress" }).click();
    await page.getByText("Progress updated.").waitFor({ timeout: 15000 });
});

await step("log session via dialog", async () => {
    await page.getByRole("button", { name: /Log a session/ }).click();
    await page.locator("dialog.rn-dialog").waitFor();
    await shot("06-session-dialog", false);
    await page.fill("#session-start", "120");
    await page.fill("#session-end", "150");
    await page.fill("#session-minutes", "25");
    await page.fill("#session-reflection", "Naming matters more than I thought.");
    await page.getByRole("button", { name: "Save session" }).click();
    await page.getByText("Session recorded.").waitFor({ timeout: 15000 });
});

await step("invalid session shows validation", async () => {
    await page.getByRole("button", { name: /Log a session/ }).click();
    await page.fill("#session-start", "200");
    await page.fill("#session-end", "100");
    await page.getByRole("button", { name: "Save session" }).click();
    await page.getByText("The ending page must be greater than or equal to the starting page.").first().waitFor({ timeout: 5000 });
    await page.keyboard.press("Escape");
    await page.locator("dialog.rn-dialog").waitFor({ state: "detached", timeout: 5000 });
});

await step("add quote note", async () => {
    await page.selectOption("#new-note-type", "Quote");
    await page.fill("#new-note-page", "42");
    await page.fill("#new-note-content", "Clean code reads like well-written prose.");
    await page.getByRole("button", { name: "Save note" }).click();
    await page.getByText("Note saved.").waitFor({ timeout: 15000 });
});

await step("rate and review", async () => {
    await page.getByRole("radio", { name: "4 stars" }).click();
    await page.getByText("Rating saved.").waitFor({ timeout: 15000 });
    await page.fill("#review-text", "Practical advice. <script>alert(1)</script> should render as text.");
    await page.getByRole("button", { name: "Save review" }).click();
    await page.getByText("Review saved.").waitFor({ timeout: 15000 });
    await shot("07-book-after-actions");
});

await step("shelf from book page", async () => {
    await page.fill("#new-shelf-name", "Software Engineering");
    await page.locator("#new-shelf-name").press("Enter");
    await page.getByText(/Created “Software Engineering”/).waitFor({ timeout: 15000 });
});

await step("mark as finished", async () => {
    await page.getByRole("button", { name: /Mark as finished/ }).click();
    await page.locator("dialog.rn-dialog").waitFor();
    await page.getByRole("button", { name: "Mark as read" }).click();
    await page.getByText(/Marked as read/).waitFor({ timeout: 15000 });
});

const pages = [
    ["library", "08-library", ".rn-book-card"],
    ["library/read", "09-library-read", ".rn-book-card"],
    ["notes", "10-notes", ".rn-note"],
    ["history", "11-history", ".rn-day"],
    ["goals", "12-goals", "#goal-books"],
    ["analytics", "13-analytics", ".rn-stats"],
    ["shelves", "14-shelves", ".rn-card"],
    ["collection", "15-collection", ".rn-tabs"],
    ["recommendations", "16-recommendations", "h1"],
    ["reviews", "17-my-reviews", ".rn-card"],
    ["profile", "18-profile", "#profile-name"],
    ["settings", "19-settings", "#pref-tz"],
    ["import-export", "20-import-export", "#import-file"],
    ["community", "21-community", "h1"],
    ["challenges", "22-challenges", "h1"],
    ["library/add", "23-add-book", "#mb-title"],
    ["dashboard", "24-dashboard", ".rn-stats"],
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

await step("set goal", async () => {
    await page.goto(BASE + "/goals");
    await page.fill("#goal-books", "24");
    await page.getByRole("button", { name: "Save goal" }).click();
    await page.getByText("Goal saved.").waitFor({ timeout: 15000 });
    await shot("25-goals-set");
});

await step("create challenge", async () => {
    await page.goto(BASE + "/challenges");
    await page.getByRole("button", { name: /New challenge/ }).click();
    await page.fill("#ch-title", "Autumn Reading Sprint");
    await page.fill("#ch-target", "3");
    await page.getByRole("button", { name: "Create challenge" }).click();
    await page.waitForURL(/challenges\/[0-9a-f-]{36}/, { timeout: 15000 });
    await page.getByText("Leaderboard").waitFor();
    await shot("26-challenge");
});

await step("review rendered as text (no XSS)", async () => {
    await page.goto(BASE + "/reviews");
    await page.getByText("<script>alert(1)</script>", { exact: false }).first().waitFor({ timeout: 15000 });
});

for (const [w, h, label] of [[390, 844, "mobile"], [820, 1180, "tablet"]]) {
    await step("responsive " + label, async () => {
        await page.setViewportSize({ width: w, height: h });
        await page.goto(BASE + "/dashboard");
        await page.locator(".rn-stats").waitFor({ timeout: 20000 });
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        if (overflow > 1) throw new Error("horizontal overflow " + overflow + "px");
        await shot(`27-dashboard-${label}`);
        await page.goto(BASE + "/library");
        await page.locator(".rn-book-card").first().waitFor({ timeout: 20000 });
        await shot(`28-library-${label}`);
        if (label === "mobile") {
            await page.click("button[data-nav-toggle]");
            await page.waitForTimeout(300);
            await shot("29-mobile-nav-open", false);
        }
    });
}

await step("logout", async () => {
    await page.setViewportSize({ width: 1366, height: 900 });
    await page.goto(BASE + "/dashboard");
    await page.locator("details.rn-account > summary").click();
    await page.getByRole("button", { name: /Log out/ }).click();
    await page.waitForURL(BASE + "/");
    await page.goto(BASE + "/dashboard");
    await page.waitForURL(/Account\/Login/);
    await shot("30-login");
});

await browser.close();
console.log(results.join("\n"));
console.log("\nProblems: " + (problems.length ? "\n" + problems.join("\n") : "none"));
