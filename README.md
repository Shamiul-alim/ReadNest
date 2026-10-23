# ReadNest — Personal Book Library & Reading Tracker

ReadNest is a full-stack .NET 10 web application for people who want to **discover** books, **track** their reading, and **learn** from what they read. It combines a searchable public catalogue (Google Books with Open Library fallback), a private personal library with progress tracking, a private knowledge journal, goals and analytics, a physical-collection and lending log, CSV/JSON import and export, and an opt-in social layer.

---

## Features

| Area | What you can do |
| --- | --- |
| **Discover** | Search by title, author, ISBN-10/13, keyword or subject; filter by language and source; paginated results with covers, authors, year and page count; book preview pages; automatic fallback to Open Library when Google Books fails or rate-limits. |
| **Library** | Add catalogue books or manual (private) books; statuses *Want to Read, Currently Reading, Read, Paused, Did Not Finish*; search, filter (status, genre, author, rating, favourites, shelf), sort (title, author, added, updated, finished, rating), paginate; grid and list views; favourites; remove with an explicit impact summary. |
| **Progress** | Current page / total pages, percentage and progress bar, start/finish/last-read dates; unknown page counts handled without fake percentages. An *Update progress* dialog (slider, +5/+10/+25, "I finished it", live preview) and a *Finish* dialog (rating, finish date) are available from the dashboard, the book page and the reading view, with **Undo**. |
| **Reading view** | `/reading/{entry}`: big percentage, quarter-ticked progress bar, pages left, time read, 14-day pace and a "finish by" estimate (all from logged sessions), pause/resume/stop, reading history and notes for the book. |
| **Reading sessions** | Date, start and end page (pages read is always derived), minutes, reflection; editing and deleting; duplicate protection; bookmark stays in sync. Quick progress updates can be recorded as today's session. |
| **Journal** | Notes, quotes, takeaways, lessons and action items with page/chapter references; search across all notes; filter by book and type. Always private. |
| **Ratings & reviews** | One 1–5 rating per book, written review, private or public visibility (feature-flagged), delete review, "My reviews" page, public reviews on book pages. |
| **Goals & analytics** | Yearly book (and optional page) goals with pace; dashboard with counts, pages, streaks, monthly activity, recent books and sessions, favourite genres/authors; yearly analytics with monthly charts and rating distribution. |
| **Recommendations** | Transparent rule-based scoring over your authors and genres, grouped by basis ("Authors you already love", "Subjects you keep returning to") and each with a reason, plus a few catalogue picks from your favourite author and subject. **Not for me** hides a suggestion for good (stored per reader, with Undo and "show dismissed suggestions"). Books you already own in another edition are left out. No AI/ML is claimed or used. |
| **Shelves** | Create/rename/delete custom shelves; a book can be on many shelves; optional public share link. |
| **Collection & loans** | Ownership, format, acquisition date, price, condition; lend/return with due dates and overdue flags; loan history. |
| **Import / export** | CSV export (formula-injection safe), full JSON export, validated CSV import with a per-row summary. |
| **Community** | Public profiles (opt-in), public reading list (opt-in), follow/unfollow, reader search, recent public reviews, shareable shelves, reading challenges with leaderboards. |
| **Read in the browser** | Attach your own PDF or EPUB to a library entry (private to you) and read it at `/reader/{entry}`: PDF.js (page input, zoom, fit width, text selection) or epub.js (table of contents, text size, line spacing, width, page-by-page or scrolling). Paper, sepia and night themes, full screen, keyboard page turns, your place saved per book and reader settings saved per account. Reading time and pages feed the normal session tracking. Book pages also list Google Books full view or *Read Preview* (embedded viewer), Open Library read/borrow links and public-domain Project Gutenberg EPUBs. |
| **Account** | ASP.NET Core Identity: register, login, logout, lockout, password change, 2FA, passkeys, optional **Continue with Google**, personal-data download and account deletion. Profile, display name, uploaded profile photo, preferred language, time zone, privacy settings. |
| **REST API** | Versioned `/api/v1` endpoints with OpenAPI document and Scalar UI in Development; bearer tokens via `/api/auth/login`. |

## Design

The interface implements the *ReadNest Prototype* from the Claude Design project (Organic design system). Everything lives in `src/ReadNest.Web/wwwroot/css/app.css`:

* **Tokens** — Organic colour ramps, spacing, radii and shadows as CSS custom properties. Older `--rn-*` names are aliases.
* **Lamplight** — every ramp mirrored under `:root[data-theme=dark]` (and `prefers-color-scheme: dark` when no choice is saved). Components only use ramp tokens, so they need no dark-specific code. The choice is saved in the browser (`localStorage`) from the sidebar, account menu, More sheet or *Settings → Appearance*.
* **Layout** — a 264px sidebar from 1080px, a 96px icon rail from 700px and a sticky top bar with a bottom bar and More sheet below 700px. Dialogs dock as bottom sheets on phones.
* **Shared components** — `BookCover` (provider image or Open Library cover by ISBN over a typographic cover in one of six tones picked from the title), `StatusBadge` (colour + icon + label, Paused dashed), `ProgressBar`, `GoalRing`, `StarInput`, `Modal`/`ConfirmDialog`, `ToastHost` (5 s, optional Undo), `EmptyState`, `LoadingIndicator` (static skeletons) and `ErrorAlert` (reassure, explain, retry).
* **AppSelect** — every application select is a custom accessible combobox/listbox (`Components/Shared/AppSelect.razor` + `wwwroot/js/select.js`): generic typed values (enums, ids, strings, numbers), labels separate from values, placeholder, disabled options, optional search, clearable, loading and empty states, `EditForm` validation, arrow/Home/End/PageUp/PageDown/typeahead/Enter/Space/Escape/Tab keys, click-outside dismissal, and a popup in the top layer (Popover API) placed above or below to stay in the viewport. Native file inputs remain native.
* **Shell behaviour** — the shell is server-rendered. The More sheet, account menu, theme switch, recent searches (stored on the device only) and password helpers are small delegated handlers in `wwwroot/js/app.js`.

## Technology stack

* .NET 10 (SDK 10.0.402, runtime 10.0.13), C# with nullable reference types and warnings-as-errors
* ASP.NET Core Blazor Web App — Interactive Server rendering (per page), static SSR for Identity and public read-only pages
* ASP.NET Core Identity (cookie for the browser, bearer token for the API), optional Google sign-in (`Microsoft.AspNetCore.Authentication.Google`)
* SkiaSharp 3.119 for profile-photo validation and re-encoding; PDF.js 4.10.38, epub.js 0.3.93 and JSZip 3.10.1 (bundled in `wwwroot/lib`) for the in-browser reader
* Entity Framework Core 10 + Npgsql 10 on PostgreSQL (17 in Docker; any supported PostgreSQL works)
* Minimal APIs, `Microsoft.AspNetCore.OpenApi`, Scalar API reference (Development only)
* `Microsoft.Extensions.Http.Resilience` (timeouts, retries for transient errors) for the catalogue HTTP clients
* The **Organic** design system (cream and sand ground, terracotta actions, sage progress; Caprasimo and Figtree, self-hosted under the SIL Open Font License) with a dark **Lamplight** theme; Lucide icons inline; Bootstrap 5 (bundled locally) only for the account-management pages; no CDNs
* xUnit, NSubstitute, bUnit, `WebApplicationFactory`, Testcontainers for PostgreSQL

## Architecture

A modular monolith following a pragmatic clean architecture:

```
ReadNest.sln
src/
  ReadNest.Domain/          Entities, enums, value objects (ISBN, text keys), domain rules — no dependencies
  ReadNest.Application/     Use-case services, DTOs, calculators (streaks, goals, recommendations), CSV format,
                            catalogue orchestration, IReadNestDbContext / IBookCatalogProvider abstractions
  ReadNest.Infrastructure/  EF Core DbContext, Fluent configurations, migrations, Identity user,
                            Google Books & Open Library providers, DI and health checks
  ReadNest.Web/             Program.cs, REST API endpoints, Blazor components (layouts, pages, shared UI),
                            authorization, security headers, rate limiting, static assets
tests/
  ReadNest.UnitTests/        Domain rules, calculators, recommendations, CSV, catalogue fallback, provider parsing
  ReadNest.IntegrationTests/ Real app + disposable PostgreSQL container (migrations, API, authorization, import/export)
```

Key decisions:

* **Ownership is enforced in every service query.** Services receive the user id resolved from the validated authentication ticket (`ClaimsPrincipal`), never from URLs or request bodies. Another user's record behaves exactly like a missing one (404).
* **Short-lived DbContexts.** Services create a context per operation through a factory, which is the safe pattern for Blazor Server circuits and also used by the API.
* **Domain invariants live in entities** (page ranges, status/date consistency, rating bounds, loan rules) and are backed by PostgreSQL check constraints, unique indexes and deliberate delete behaviours.
* **Interactive pages render without prerendering** (`AppRenderMode.Interactive`) so data is loaded once per page visit.
* **No fake data.** Statistics are SQL aggregates of real records; providers return `null` instead of inventing metadata; imports never create reading sessions.

### Data rules worth knowing

* **Catalogue de-duplication:** provider + external id → normalised ISBN-13 (ISBN-10 is converted) → for records without any ISBN, identical normalised title and first author. A unique partial index guarantees one shared catalogue record per ISBN-13.
* **Manual books** are private to their creator; a manual entry whose ISBN matches a catalogue book reuses the catalogue record.
* **Counting rule for goals:** a book counts toward year *Y* when its library entry has status *Read* and its completion date (reader's local date) is in *Y*. Each entry counts once, so editing the date moves the book between years rather than double-counting. Books imported as *Read* without a date are not attributed to any year.
* **Pages read** = sum of `EndPage − StartPage` over reading sessions in the period (progress updates can be recorded as sessions).
* **Streaks** count consecutive local calendar days with at least one session; a streak stays alive until the end of the day after the last reading day. "Today" is computed in the time zone set in Settings (default UTC).
* **Deletion:** removing a library entry deletes its sessions, notes, review, shelf placements and loan history after an explicit confirmation listing what will be removed; it is refused while the book is lent out. Shared catalogue books are never deleted when a library entry or account is removed. Deleting an account cascades all personal data.

## Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or later)
* PostgreSQL 14+ — easiest via [Docker Desktop](https://www.docker.com/products/docker-desktop/)
* Docker is also required to run the integration tests (Testcontainers)

## Quick start (local development)

```bash
# 1. Start PostgreSQL (port 5433 on the host, user/password readnest/readnest_dev_password)
docker compose up -d db

# 2. Restore the local EF Core tool and packages
dotnet tool restore
dotnet restore

# 3. Run the app (Development applies migrations automatically on startup)
dotnet run --project src/ReadNest.Web --launch-profile http
```

Open <http://localhost:5230>, create an account, and start searching.

`src/ReadNest.Web/appsettings.Development.json` points at the Docker database. Its password is a **local development default only**; never reuse it elsewhere.

### Using an existing PostgreSQL instead of Docker

Create a database and user, then provide the connection string through user secrets (preferred) or an environment variable:

```bash
dotnet user-secrets --project src/ReadNest.Web set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=readnest;Username=readnest;Password=<your password>"
```

## Configuration

Settings use standard ASP.NET Core configuration (appsettings → user secrets → environment variables, where `:` becomes `__`).

| Setting | Required | Default | Purpose |
| --- | --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | **Yes** | Dev: Docker DB | PostgreSQL connection string |
| `Database__ApplyMigrationsOnStartup` | No | `false` (`true` in Development) | Apply EF Core migrations at startup |
| `Catalog__GoogleBooks__ApiKey` | No | empty | Raises the Google Books quota; works anonymously without it |
| `Catalog__GoogleBooks__Enabled` / `Catalog__OpenLibrary__Enabled` | No | `true` | Enable/disable providers |
| `Catalog__ProviderOrder` | No | `GoogleBooks, OpenLibrary` | Provider priority (later = fallback) |
| `Catalog__OpenLibrary__ContactEmail` | No | empty | Contact included in the User-Agent, as Open Library requests |
| `Catalog__SearchCacheMinutes` / `Catalog__BookCacheMinutes` | No | 15 / 120 | In-memory caching of catalogue results |
| `Features__PublicReviews` | No | `true` | Allow publishing reviews |
| `Identity__RequireConfirmedAccount` | No | `false` | Require email confirmation (needs a real `IEmailSender`, see limitations) |
| `ReverseProxy__ForwardHeaders` | No | `false` | Honour `X-Forwarded-For/Proto` behind a proxy |
| `Https__Redirect` | No | `true` | Disable when TLS terminates at a proxy |
| `RateLimiting__Enabled` | No | `true` | Only switched off by automated tests |
| `Authentication__Google__ClientId` / `Authentication__Google__ClientSecret` | No | empty | Enable "Continue with Google" (both required; see [Google sign-in](#google-sign-in)) |
| `Storage__RootPath` | No | `App_Data/storage` (container: `/data/storage`) | Folder for uploaded photos and books; relative paths are under the content root. Must be persistent. |
| `Uploads__ProfilePhotoMaxBytes` | No | `5242880` (5 MB) | Largest profile photo upload |
| `Uploads__ReadingFileMaxBytes` | No | `52428800` (50 MB) | Largest PDF/EPUB upload |
| `ReadingSources__GoogleBooksEnabled` / `__OpenLibraryEnabled` / `__GutenbergEnabled` | No | `true` | Turn individual "Ways to read" sources off |
| `ReadingSources__GutendexBaseUrl` | No | `https://gutendex.com/` | Project Gutenberg catalogue API |

`.env.example` lists the variables used by `docker-compose.yml`; copy it to `.env` (git-ignored) to override them.

## Google sign-in

"Continue with Google" uses ASP.NET Core Identity external logins. It is **off unless both settings are present**; without them the app runs normally with email and password (in Development the sign-in and register pages show a disabled Google button with setup instructions; elsewhere it is hidden).

**1. Create OAuth credentials in Google Cloud Console** (<https://console.cloud.google.com/>):

1. Select or create a project.
2. *APIs & Services → OAuth consent screen* (or *Google Auth Platform → Branding/Audience*): choose **External**, enter the app name, support email and developer contact, and add the scopes `openid`, `.../auth/userinfo.email` and `.../auth/userinfo.profile`. While the app is in *Testing*, add your Google account under *Test users*.
3. *APIs & Services → Credentials → Create credentials → OAuth client ID*, application type **Web application**.
4. Under **Authorized redirect URIs** add, exactly:
   * `http://localhost:5230/signin-google` (the `http` launch profile)
   * `https://localhost:7262/signin-google` (the `https` launch profile)
   * `https://<your-domain>/signin-google` for each deployed host (and `http://localhost:8080/signin-google` if you use the Docker profile locally)

   No *Authorized JavaScript origins* are needed. Save, then copy the client ID and client secret.

**2. Give the credentials to ReadNest** (never commit them):

```bash
# Development (stored outside the repository by the user-secrets tool)
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>.apps.googleusercontent.com" --project src/ReadNest.Web
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>" --project src/ReadNest.Web

# Production / containers (environment variables)
export Authentication__Google__ClientId="<client-id>.apps.googleusercontent.com"
export Authentication__Google__ClientSecret="<client-secret>"
# docker compose reads GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET from the environment or .env
```

Restart the app after setting them.

**How accounts are matched.** A Google identity is linked to a ReadNest account only by its Google subject id. A first Google sign-in with an unknown email creates a new account (the email comes from Google and must be verified; you choose a display name). If the email already belongs to a ReadNest account, it is **not** linked automatically: the page asks you to sign in with your password and connect Google from *Settings → Account → Connected sign-ins*, which links it to the signed-in account. Cancelling on Google's screen or a failed callback returns to the sign-in page with a message. Google's access tokens are not stored. Behind a TLS-terminating proxy set `ReverseProxy__ForwardHeaders=true` so the callback URL uses `https`.

## Uploaded files and storage

Profile photos and reading files are stored by `LocalFileStore` under `Storage__RootPath`, **outside `wwwroot`**, with server-generated names (`photos/2026/<random>.webp`, `books/2026/<random>.pdf`). They are never served as static files: photos go through `GET /api/v1/users/{id}/photo` (the profile's privacy setting applies; private profiles' photos are only shown to their owner) and books through `GET /api/v1/library/{entry}/reading-file/content` (owner only). The database stores only the key, size, SHA-256 and metadata.

* **Photos** (JPEG, PNG or WebP, 5 MB): the type is detected from the file signature and must match the decoder; SVG, GIF and anything else is rejected. Dimensions are checked from the header before decoding (decompression-bomb protection), then the image is cropped square, resized to 512 px and re-encoded as WebP, which drops EXIF/GPS metadata. The old photo is deleted only after the new one is saved.
* **Books** (PDF or EPUB, 50 MB): streamed to a staging file while hashing, then validated without extracting anything: PDFs must start with `%PDF-` and end with `%%EOF`; EPUBs must be ZIPs with an `application/epub+zip` `mimetype` entry and `META-INF/container.xml`, at most 10,000 entries and 500 MB uncompressed. The extension must match the content. A replaced file is deleted after the new one is stored; removing a book from your library or deleting your account deletes its files.
* **Persistence:** in Development files go to `src/ReadNest.Web/App_Data/storage` (git-ignored). The container image stores them in `/data/storage` and `docker-compose.yml` mounts the `readnest-files` volume at `/data`. **Without a persistent volume uploads are lost when the container is replaced** — back the volume up together with the database. Multiple instances need shared storage (a shared volume, or an `IFileStore` implementation for blob storage).

## Reading in the browser

| Source | What ReadNest offers | Full text? |
| --- | --- | --- |
| **Your own PDF/EPUB** | Read in ReadNest at `/reader/{entry}`; private to you | Yes (your copy) |
| **Project Gutenberg** (via the Gutendex catalogue API) | Only when Project Gutenberg marks the book as *not* under copyright: *Read in ReadNest* downloads its EPUB from gutenberg.org into your library entry, with attribution and a note that it is public domain **in the US** (check your own country). Otherwise only a link. | Yes, public domain (US) |
| **Google Books** | Embedded viewer at `/reader/google/{volume}` when Google says the volume is embeddable: *full view* for public-domain books, otherwise **Read Preview** (publisher-chosen pages). Not embeddable → link to Google Books. `NO_PAGES` → nothing shown. | Only for Google's full-view books |
| **Open Library** | Official links only: *Read free on Open Library* for public scans, *Borrow on Open Library* for lendable books (borrowing happens on Open Library). | On Open Library's site |

Nothing is scraped, no DRM is bypassed, and a book is never shown as readable unless the provider says so. Google availability uses the Books API `accessInfo`; when that is rate limited (anonymous quota) it falls back to Google's viewability endpoint used by its embedded viewer. Lookups are cached (12 h, failures 5 min). Gutendex can take up to a minute to answer, so the book page fills these options in as they arrive.

**Reader.** PDFs render with PDF.js 4.10.38 (`isEvalSupported: false`, no XFA, streamed with range requests); EPUBs with epub.js 0.3.93, whose chapter iframes are sandboxed **without `allow-scripts`**, so scripts in uploaded books never run. Both libraries (and JSZip 3.10.1) are served from `wwwroot/lib` (integrity checked against the npm registry; licences alongside: Apache-2.0, BSD-2-Clause, MIT). Your place (PDF page, or EPUB CFI and percentage) is saved a couple of seconds after you stop turning pages, not on every page. Reader settings (theme, text size, line spacing, width, page turning) are saved to your account.

**Progress and sessions.** Saving your place never creates a session. Reading time is counted only while the tab is visible and you have interacted in the last three minutes. When you leave (Done, back, closing or hiding the tab) ReadNest maps your position to a book page — page for page when a PDF has the same page count as the entry, otherwise by percentage of the entry's page count (EPUBs need a page count on the entry) — and, if you read forward for at least a minute at a plausible pace, records it through the normal progress workflow as a reading session. Jumps (for example to the last chapter) only move the file bookmark; *Done → Log this reading* records them explicitly. Reaching the end never marks a book as read: the reader offers **Mark as finished**, which opens the usual finish dialog.

## Database and migrations

Migrations live in `src/ReadNest.Infrastructure/Migrations` and are the only schema source of truth (no `EnsureCreated`). The initial migration also seeds a list of common genres.

```bash
# Apply migrations explicitly (uses the design-time factory; set the env var for non-default databases)
ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=readnest;Username=readnest;Password=readnest_dev_password" \
  dotnet ef database update --project src/ReadNest.Infrastructure --startup-project src/ReadNest.Infrastructure

# Add a migration after changing the model
dotnet ef migrations add <Name> --project src/ReadNest.Infrastructure --startup-project src/ReadNest.Infrastructure --output-dir Migrations

# Check that the model and migrations agree
dotnet ef migrations has-pending-model-changes --project src/ReadNest.Infrastructure --startup-project src/ReadNest.Infrastructure

# Produce an idempotent SQL script or a bundle for deployments
dotnet ef migrations script --idempotent --project src/ReadNest.Infrastructure --startup-project src/ReadNest.Infrastructure -o migrate.sql
```

To start over locally: `docker compose down -v && docker compose up -d db`.

## Running the tests

```bash
dotnet test ReadNest.sln
```

* **Unit tests** (no external dependencies): status/progress/completion rules, session validation, loans, ISBN normalisation, streaks across month boundaries, goal pace (incl. leap years), monthly aggregation, recommendation ranking, CSV parsing/validation, CSV formula-injection escaping, catalogue fallback/caching/rate-limit cooldown, Google Books / Open Library parsing and failure handling, URL sanitising.
* **Component tests** (bUnit) cover the `AppSelect` component: rendering and ARIA state, enum/int/Guid binding, labels vs values, disabled options and selects, validation inside `EditForm`, search filtering with stable indices, clearing, loading/empty states and the keyboard callbacks.
* **Integration tests** start a disposable `postgres:17-alpine` container (Docker must be running), boot the real application with its migrations, and replace the external catalogue with a deterministic fake. They cover startup/health, model–migration consistency, authentication, the full library workflow, validation and problem responses, cross-user access attempts on every private endpoint, shelves/profile/visibility rules, lending, account deletion cascades, CSV import/export, JSON export, recommendations (including "Not for me" isolation between readers and other editions), progress undo (including another reader trying to undo yours), weekly/daily/outcome statistics, taste statistics with enough data, CSV import keeping Paused status, 404 pages, challenges and provider outages; Google sign-in through a fake external provider registered as "Google" (new account, returning user, no automatic linking by email, explicit linking, unverified email, cancelled/forged callbacks, the real handler's redirect to Google with the `/signin-google` callback when dummy credentials are configured, and the app without credentials); profile photos (re-encoding, SVG/spoofed/oversized/bomb rejection, privacy, replacement and deletion); reading files (PDF/EPUB validation, ranges, cross-user access, positions without sessions, progress through sessions, replacement, deletion with the entry or account, missing files, Gutenberg import rules, reading-source labels). Uploads go to a temporary folder and every test database is a throwaway container — your development database is never touched.

### Optional browser smoke test

`tests/e2e/ui-smoke.mjs` drives a running instance in headless Chrome: it registers a user, searches the live catalogue, adds a book and undoes it, starts a book, updates progress and logs a session (including validation errors), saves a quote, rates and reviews (checking the review renders as text), shelves and finishes the book, visits every page, uses list-view bulk selection, drives the custom select with the keyboard (open, move, Escape, focus return, End/Enter, click outside) and checks it in the dark theme on a 360 px screen, sets a goal, creates a challenge, switches to Lamplight, checks the 404 page, checks for Blazor errors and horizontal overflow at phone/tablet widths, opens the More sheet and signs out. Screenshots are written to `tests/e2e/shots/`.

```bash
dotnet run --project src/ReadNest.Web --launch-profile http   # in another terminal
cd tests/e2e && npm install && CHROME_PATH="/path/to/chrome" npm run smoke
```

It uses the real Google Books / Open Library APIs, so it needs network access.

## REST API

* OpenAPI document (Development): <http://localhost:5230/openapi/v1.json>
* Interactive reference (Development): <http://localhost:5230/scalar/v1>
* Get a token: `POST /api/auth/register` then `POST /api/auth/login` with `{"email": "...", "password": "..."}`; send `Authorization: Bearer <accessToken>`. Browser sessions (cookie) are accepted as well.

| Group | Endpoints |
| --- | --- |
| Catalogue | `GET /api/v1/catalog/search`, `GET /api/v1/catalog/{provider}/{externalId}`, `POST …/import`, `GET /api/v1/books/{id}`, `GET /api/v1/books/{id}/related`, `GET /api/v1/books/{id}/reviews` |
| Library | `GET/POST /api/v1/library`, `POST /api/v1/library/manual`, `GET/DELETE /api/v1/library/{id}`, `PUT …/status`, `PUT …/progress`, `PUT …/details`, `PUT …/favorite`, `GET /api/v1/library/counts` |
| Reading files | `GET/POST/DELETE /api/v1/library/{id}/reading-file` (upload: multipart `file`, bearer token), `GET …/reading-file/content` (`?download=true` to save), `PUT …/reading-file/position`, `POST …/reading-file/gutenberg`, `GET /api/v1/books/{id}/reading-sources` |
| Profile | `GET /api/v1/profile`, `PUT/DELETE /api/v1/profile/photo` (upload: multipart `photo`, bearer token), `GET/PUT /api/v1/profile/reader-preferences`, `GET /api/v1/users/{id}/photo` |
| Reading | `POST /api/v1/library/{id}/sessions`, `GET /api/v1/sessions`, `PUT/DELETE /api/v1/sessions/{id}` |
| Notes | `GET /api/v1/notes`, `POST /api/v1/library/{id}/notes`, `PUT/DELETE /api/v1/notes/{id}` |
| Reviews | `PUT/DELETE /api/v1/library/{id}/review`, `GET /api/v1/reviews/mine`, `GET /api/v1/reviews/public` |
| Goals & stats | `GET /api/v1/dashboard`, `GET /api/v1/analytics/{year}`, `GET /api/v1/goals`, `GET/PUT/DELETE /api/v1/goals/{year}` |
| Shelves | `GET/POST /api/v1/shelves`, `PUT/DELETE /api/v1/shelves/{id}`, `PUT/DELETE /api/v1/shelves/{id}/entries/{entryId}` |
| Other | `GET /api/v1/recommendations`, `POST /api/v1/recommendations/{bookId}/dismiss`, `DELETE /api/v1/recommendations/dismissals`, `GET /api/v1/export/library.csv`, `GET /api/v1/export/library.json`, `POST /api/v1/import/library.csv` (multipart, bearer token) |
| Health | `GET /health/live`, `GET /health/ready` (includes the database) |

List endpoints return `{ items, page, pageSize, totalCount, totalPages, hasPrevious, hasNext }`. Errors use RFC 7807 problem details with a `traceId` (400 validation, 401, 404, 409 conflict, 429 rate limited, 503 catalogue unavailable, 500 without internal details).

## External book catalogues

* **Google Books** (`https://www.googleapis.com/books/v1/`) is tried first; an API key is optional.
* **Open Library** (`https://openlibrary.org/`) is the fallback, and can also be chosen explicitly on the Explore page.
* A failing, slow (8 s per attempt, 20 s total) or rate-limited provider falls through to the next one, and the page explains which source served the results. Providers answering `429` are skipped for two minutes and are not retried (to respect their limits). Searches are cached for 15 minutes and book details for 2 hours.
* When every provider is down, searching shows a clear error while everything already in your library keeps working.
* Attribution is shown on book pages and in the footer. Descriptions are converted to plain text and always HTML-encoded; cover and link URLs are restricted to https.

## CSV format

```
Title,Author,ISBN,Status,CurrentPage,TotalPages,Rating,StartedOn,CompletedOn,Favorite,Shelves
Clean Code,Robert C. Martin,9780132350884,Currently Reading,180,464,,2026-09-01,,false,Software Engineering
```

Only `Title` is required; header names are case-insensitive and columns may appear in any order. Multiple authors or shelves are separated by semicolons; dates are `YYYY-MM-DD`; status is one of *Want to Read, Currently Reading, Read, Paused, Did Not Finish*. Files are limited to 1 MB / 2,000 rows, must be UTF-8, and each row is validated and imported atomically. Rows already in the library or repeated in the file are skipped.

## Security and privacy

* Identity handles password hashing, lockout (5 failed attempts → 10 minutes), 2FA and passkeys.
* Every private query is scoped to the authenticated user; integration tests attempt cross-user reads, edits and deletes on every private endpoint.
* Notes, private reviews, lending details and account data are never exposed publicly; public profiles, reading lists, reviews and shelves are opt-in.
* Blazor forms are protected by antiforgery tokens; the API accepts JSON only (no simple cross-site form posts), the auth cookie is `HttpOnly` and `SameSite=Lax`, and the multipart import endpoint requires a bearer token.
* User content is always rendered encoded (never as raw HTML); external URLs are validated (https only, no credentials, no `javascript:`/`data:`), external links use `rel="noopener noreferrer nofollow"`, images use `referrerpolicy="no-referrer"`.
* Login redirects only to local URLs. Google sign-in never links an account by matching email; tokens from Google are not stored; the OAuth correlation cookie is `SameSite=Lax`.
* Uploaded files: type checked by content, size limits enforced while streaming, random storage names outside `wwwroot`, owner/visibility checks on every download, `Content-Security-Policy: sandbox` and `nosniff` on file responses, EPUB scripts never executed.
* Rate limits: API 300/min, catalogue 40/min, token endpoints 10/min, imports 5 per 10 min, Identity form posts 20/min per IP.
* Security headers: CSP (the readers need `blob:`/`data:` for images, fonts and styles of the book being read; only the Google viewer page allows Google's https script and frame sources), `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`; HSTS outside Development.
* Request logging records method, path, status and duration only — no bodies, query strings, cookies or tokens.

## Deployment guidance

1. Build the container: `docker build -t readnest .` (or `docker compose --profile app up --build` to run it with the database on <http://localhost:8080>).
2. Provide `ConnectionStrings__DefaultConnection` from a secret store; use a dedicated database user with a strong password.
3. Run migrations as a release step (`dotnet ef migrations bundle` or the idempotent SQL script), or set `Database__ApplyMigrationsOnStartup=true` for single-instance deployments.
4. Terminate TLS at a reverse proxy, set `ReverseProxy__ForwardHeaders=true` and keep HSTS on.
5. **Persist ASP.NET Core Data Protection keys** (e.g. to a mounted volume, Azure Blob/Key Vault or Redis) so login cookies and tokens survive restarts and work across instances.
6. **Persist uploaded files**: mount a volume at `Storage__RootPath` (`/data` in the image) and back it up with the database.
7. To enable Google sign-in, add `https://<your-domain>/signin-google` to the OAuth client and set the two `Authentication__Google__*` variables from a secret store.
8. Register a real `IEmailSender<ApplicationUser>` before enabling email confirmation or password-reset emails.
9. Consider tightening the CSP with nonces, adding a distributed cache for catalogue results in multi-instance deployments, and shipping logs to a central sink (JSON console logging is enabled outside Development).

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Connection string 'DefaultConnection' is not configured` | Run with the Development environment or set the connection string (see Configuration). |
| `Failed to connect to localhost:5433` | `docker compose up -d db` and wait until `docker compose ps` shows *healthy*. |
| Port 5433 already in use | Set `POSTGRES_PORT` in `.env` and update the connection string. |
| Searches always come from Open Library with "Google Books is rate limiting" | Anonymous Google Books quotas are shared per IP; add `Catalog__GoogleBooks__ApiKey`. |
| Integration tests fail to start | Docker must be running; the first run pulls `postgres:17-alpine`. |
| `dotnet ef` not found | `dotnet tool restore`. |
| Logged out after redeploying a container | Persist Data Protection keys (see Deployment). |
| Google button is greyed out | Set both `Authentication:Google:*` user secrets and restart (see Google sign-in). |
| Google says `redirect_uri_mismatch` | Add the exact URL shown in the error (scheme, host, port and `/signin-google`) to the OAuth client's authorized redirect URIs. Behind a proxy, enable `ReverseProxy__ForwardHeaders`. |
| Uploaded photos/books disappear after redeploying | Mount a persistent volume for `Storage__RootPath` (see Uploaded files and storage). |
| "Project Gutenberg couldn't be checked" | Gutendex is slow or down; the book page links to a gutenberg.org search instead. Try again later. |

## Known limitations

* No email delivery is configured (a no-op sender is registered), so email confirmation is off by default and "forgot password" emails are not sent.
* Re-reading a book is not tracked as a separate read; reopening a finished book clears its completion date (sessions are kept).
* Editing or deleting a reading session never moves the bookmark backwards.
* Catalogue caching is in-memory (per instance).
* Rate limiting of catalogue searches applies to the REST API; searches made from the Blazor UI rely on caching and provider cooldowns.
* The prototype shows a notifications bell; there is no notification system, so the bell is not shown.
* Discover shows search, subjects and your own recommendations. The prototype's hand-curated collections ("Short novels for long evenings", "New and notable") are not invented.
* Settings has no favourite-genre picker, default-status choice or pages/percentage switch, and goals have no "show on profile" switch. These need backend support that does not exist yet, so the controls are omitted rather than faked.
* The theme choice is per device (browser storage), not per account.
* Reading files and photos are stored on the local file system; multi-instance deployments need shared storage or a blob-storage `IFileStore`.
* EPUBs without a page count on the library entry can be read and resumed, but in-browser reading can't be converted to pages for sessions until the entry has a page count.
* The Google Books embedded viewer is Google's own (older) viewer; what it shows, and whether a book can be embedded, is decided by Google and can differ by country.
* Annotations and highlights inside the reader are not supported; use the notes journal (with page references).
* Public profiles are discoverable by name/handle only; there is no activity feed or messaging by design.
