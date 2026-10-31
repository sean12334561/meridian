# Meridian Vote Tracker — High-Level Design

Oct 30, 2026 · @Sem

A modular monolith: one ASP.NET Core image run as API and worker, PostgreSQL as the single source of truth with append-only history, Blob Storage for original files, and an Angular client generated from OpenAPI.

## Architecture

&#91;embedded content: runtime architecture · 4 app containers, 3 Azure services\]

The browser reaches only nginx; the API writes uploads to Blob Storage and jobs to PostgreSQL, and the worker picks them up from there. The dashed line is telemetry from every app container.

## Components

Four long-running app containers come from two images built in the repo; everything Azure is reached through configuration, with a local fallback.

| Component | Runs as | Responsibility |
| --- | --- | --- |
| `web` | Container, image `meridian-web` (nginx) | Serves the built Angular app; proxies `/api` to `api` so the browser sees one origin (no CORS) |
| `api` | Container, image `meridian-api`, `Worker__Enabled=false` | Controllers, auth handler and policies, OpenAPI, ProblemDetails, correlation ID. Accepts uploads, enqueues jobs, serves reads |
| `worker` | Container, same image, `Worker__Enabled=true` | `BackgroundService` loops that claim jobs from Postgres: imports and snapshot calculations. Scalable with `--scale worker=N` |
| `migrator` | One-shot container, same image | Applies EF migrations, seeds countries, memberships and the 3 users idempotently, then exits |
| PostgreSQL | Azure Database for PostgreSQL (final run) or local `postgres` container (fallback, tests via Testcontainers) | Single source of truth: approved data with full change history, imports, conflicts, positions, snapshots, job queue |
| Blob Storage | Azure Blob Storage, or `azurite` locally | Original uploaded files, immutable, keyed by import ID |
| Application Insights | Azure | Centralized logs and request traces, correlated by correlation ID (OpenTelemetry exporter) |
| Key Vault | Azure, not used | Not used: inside Docker it would need a client secret in `.env` anyway. Production: managed identity + Key Vault |
| Not used | — | Cosmos DB (data is relational and transactional), Service Bus (not provided; Postgres queue suffices). AI services only for the bonus |

The backend is a modular monolith: one project, `Meridian.Api`, organized by feature slices (Imports, Conflicts, Votes, Snapshots) with a small `Shared` folder; see the LLD.

## Data model

Two tables hold the approved data, `votes` and `country_votes`, and every change to them writes a `vote_history` row in the same transaction; everything else is reference data, import records, the job queue or frozen snapshot copies.

&#91;embedded content: data model · 13 tables, arrows point to the referenced table\]

Arrows point from a table to the table it references. The dashed line is a job's payload pointing at an import or snapshot by ID, not a foreign key; `conflicts` and `vote_history` also reference `imports` and `users`, left out to keep the picture readable.

### Tables

| Table | Holds | Why it exists |
| --- | --- | --- |
| `users` | id, name, role | The 3 seeded users; every who-column points here |
| `member_states` | country\_code, country\_name, member\_from, member\_to | The CSV as-is (Iceland on two rows); validates codes and membership dates at import |
| `imports` | file name, blob key, SHA-256, uploader, status, processed / total rows, `report` (jsonb: totals and per-vote results) | Import history and live progress (ING-02, ING-15) |
| `import_rows` | every line: raw text, parsed fields, outcome (incl. PreviouslyDecided), reason, `conflict_id` | Staging for classification and the permanent per-row report, linked to any conflict it opened or matched (ING-14, HIS-01) |
| `jobs` | type, payload, status, attempts, locked\_until, `run_after`, last\_error | Background queue for imports and snapshots; `run_after` delays retries |
| `votes` | vote\_ref, session, date, title, `title_source_import_id`, `vote_date_source_import_id`, `session_source_import_id`, position, weight | Current approved vote details and where they came from (HIS-02); position and weight are null until the admin sets them (POS-04) |
| `country_votes` | vote\_id, country\_code, ballot, source import and line | Current approved ballot per vote × country, with its origin |
| `conflicts` | vote\_id, target (country code or field name), approved value with `approved_source_import_id` and line, proposed value with its source and published\_at, status, decider, note | Every disagreement with approved data, with the source of **both** values (CON-02); resolved ones stay (CON-06) |
| `vote_history` | vote\_id, type, `target`, `old_value` / `new_value` (jsonb), actor, import\_id, conflict\_id, created\_at | Append-only trail of changes, decisions and failed decision attempts, plus one "imported N ballots" row per vote per import (HIS-02–04) |
| `snapshots` | name, filter (incl. resolved vote IDs), rules (abstention 0.5, minimum votes, excluded votes), status, creator | Definition and rules of each ranking |
| `snapshot_scores` | country, score, rank, counted votes and weights | Ranking screen |
| `snapshot_contributions` | country, vote, copied ballot, position, weight, points, `source_import_id` | Frozen inputs plus lineage: which file each counted ballot came from (SNP-04) |
| `snapshot_publications` | snapshot, quarter, who, when, reason, withdrawn\_at, superseded\_by | Publication pointer per quarter, with full history |

**Vote timeline (HIS-04)** = `vote_history` for the vote, merged at read time with the publications whose snapshot includes the vote, so a publication doesn't need thousands of history rows. A losing concurrent decision is recorded as a `DecisionNotApplied` event after its transaction rolls back.

### Indexes

Unique indexes enforce rules; other indexes exist only on large tables searched by a narrow filter. Small tables need nothing beyond their primary key, and more are added only when `EXPLAIN ANALYZE` shows a slow query.

| Table | Index | Why |
| --- | --- | --- |
| `votes` | unique `(vote_ref)` | Rule: one vote per Assembly ID |
| `country_votes` | unique `(vote_id, country_code)` | Rule: one approved ballot per vote × country; also the classification join |
| `conflicts` | unique `(vote_id, target, proposed_value) WHERE status = 'Pending'` | Rule: no duplicate or reopened pending conflict (ING-12); `target` is never null, so NULLs can't slip past it |
| `snapshot_publications` | unique `(quarter) WHERE withdrawn_at IS NULL` | Rule: one current publication per quarter |
| `vote_history` | `(vote_id)` | Timeline lookup on a large table (HIS-04) |
| `import_rows` | `(import_id, vote_ref)` | Each apply batch reads one batch of votes; without it every statement scans the import, and a decision waits on the lock for longer |
| Primary keys | `import_rows (import_id, line_no)`, `snapshot_scores (snapshot_id, country_code)`, `snapshot_contributions (snapshot_id, country_code, vote_id)` | Automatic; also serve the report, ranking and breakdown queries |

### Growth and retention

Nothing in the domain is ever deleted (ING-09, HIS-02).

| Behaviour | Tables | Notes |
| --- | --- | --- |
| Written once at seed | `users`, `member_states` | Never change |
| Append-only | `import_rows`, `vote_history`, `snapshot_scores`, `snapshot_contributions` | Fastest growth: \~962k `import_rows` per historic upload, \~975k contributions per full-history snapshot |
| Updated in place, never deleted | `imports`, `jobs`, `votes`, `country_votes`, `conflicts`, `snapshots`, `snapshot_publications` | Changes to `votes`, `country_votes` and `conflicts` write `vote_history` in the same transaction |
| Could be cleaned up | `jobs` | Finished jobs purgeable after N days; kept for the assignment |

Before production, `import_rows` and `snapshot_contributions` would be partitioned by import or snapshot, with old partitions archived to Blob Storage; blob files are never deleted. A database trigger on `votes` and `country_votes` would also reject any change that doesn't come with a `vote_history` row, so a code path that skips `HistoryWriter` can't change data silently.

## Import pipeline

&#91;embedded content: import pipeline · 4 request steps, 5 worker steps\]

The request only stores the file and enqueues a job (ING-01); the dashed line is the browser reading progress that the worker writes to the import row (ING-02). A retry after a crash restarts from the immutable blob, and the report is kept with the import (ING-15).

## Classification rules

Every row ends with exactly one outcome (ING-06), decided in this order; invalid rows are removed before consistency checks so a bad row can't poison its whole vote.

1. **File-level checks** — readable UTF-8, BOM stripped, required header columns present (matched by name). Failure marks the import Failed; nothing is applied.
2. **Row validation (ING-04) → Invalid** — column count, required fields, known country, ballot in Y/N/A/X, `yyyy-mm-dd` real date, integer session, vote-ref format, membership on the vote date. Allowed normalizations (trim, uppercase country code) are flagged with the original value (ING-13).
3. **Exact duplicates in the file → Duplicate** — same vote, country and ballot; first occurrence continues, the rest are counted.
4. **Same vote and country, different ballots → Contradiction** — none of the values is stored.
5. **Vote details differ between rows → Contradiction** on the vote details only; its country rows continue.
6. **Compare with approved data:**
   - vote or (vote, country) not yet approved → **New**, inserted into country\_votes with its source import and line (also covers partial votes, ING-10);
   - equal to the current approved value → **Unchanged**;
   - different → **Conflict**, unless an identical conflict is already pending (counted, not reopened — ING-12) or the value was already rejected or replaced by a decision (Previously decided, unless the row's published\_at is newer than the decision's source).
7. **Votes missing from the file** — no action (ING-09).

All steps after parsing run as set-based SQL over `import_rows` (`GROUP BY` / joins), so unsorted files need no special handling (ING-03).

## Conflicts and concurrency

Only a human decision changes approved data (ING-08), and only one decision per conflict can ever win (CON-05).

- **Decision transaction:** `UPDATE conflicts SET status=@s, decided_by=@u, decided_at=now(), note=@n WHERE id=@id AND status='Pending'`. On Accept, the same transaction updates the approved value and writes a `vote_history` row (old → new, conflict, decider).
- **Losing a race:** zero rows affected → HTTP 409 ProblemDetails carrying the current state (who decided, when, the resulting value). The UI shows it as a message, not a generic error.
- **Several pending proposals for one value** (a third revision arrives): allowed; accepting one marks the others Superseded.
- **Concurrent imports:** parsing and validation run in parallel; classify-and-apply is serialized with `pg_advisory_xact_lock`, so each import classifies against the previous import's results.
- **Decisions vs imports:** decisions take the same advisory lock because they write approved data. Imports classify and apply one batch of votes at a time, each batch inside the lock, and release it between batches. A decision never waits long, and every batch still sees all writes committed before it.
- **Crash safety:** each apply batch is one transaction; a crashed job is reclaimed after its `locked_until` lease expires and re-run safely, because classification is idempotent.

## Positions, weights and snapshots

Each vote has one position (For/Against) and one weight (1–5), set only by the admin; a snapshot freezes those inputs and every country's result at creation time, so it never changes afterwards (SNP-04).

**Positions (POS-01–04)**

- Set per vote by Admin; 403 for analysts. Validation errors return field-keyed ProblemDetails (weight 7 → error on `weight`).
- Each change updates position and weight on `votes` and writes a `vote_history` row (who, when, old, new).
- Admin-only bulk endpoint sets position and weight for all votes matching a filter, logged per vote, so the 6,480 historic votes can be scored.

**Snapshot creation (SNP-01–03)**

1. Analyst selects votes by date range, session and/or explicit list.
2. Votes without a position block creation and are listed; the user may explicitly exclude them, and the exclusion is stored on the snapshot (POS-04).
3. A `snapshot` job runs in the worker inside one REPEATABLE READ transaction: compute contributions from current approved ballots, positions and weights, copied into `snapshot_contributions`; aggregate into `snapshot_scores`; assign ranks.
4. The snapshot stores its scoring rules and the count of votes that had pending conflicts at creation.

**Scoring rule**

```latex
\text{score} = 100 \times \frac{\sum w_{\text{match}} + 0.5 \times \sum w_{\text{abstain}}}{\sum w_{\text{Y, N or A}}}
```

A Y or N that matches the position earns the full weight, an abstention earns half, and absent (X) is not counted; non-member votes cannot exist as approved data. Each snapshot stores its rules: abstention factor 0.5 and minimum counted votes (default 1). Countries below the minimum show "insufficient data" and get no rank; every score shows its counted-vote number. Ties share a rank (1, 2, 2, 4); ranking uses unrounded scores.

**Publishing (SNP-05)** — Admin publishes a completed snapshot for a quarter (e.g. `2024-Q1`), recorded in `snapshot_publications` (snapshot, quarter, who, when, reason). Publishing another snapshot for the same quarter needs confirmation and a reason: the earlier publication gets `withdrawn_at` and `superseded_by`, while its snapshot stays unchanged, viewable and labelled "previously published, superseded on …". A partial unique index keeps one current publication per quarter.

## API surface

Five thin controllers map HTTP to application services; long operations return 202 and are tracked through their own resource, so no separate jobs endpoint is exposed.

| Controller | Endpoint | Purpose | Role |
| --- | --- | --- | --- |
| Imports | `POST /api/imports` | Multipart upload → blob → job; returns 202 with `importId` | Analyst+ |
| Imports | `GET /api/imports` | Import history | Analyst+ |
| Imports | `GET /api/imports/{id}` | Status, phase, processed / total rows (polled) | Analyst+ |
| Imports | `GET /api/imports/{id}/report` | Totals per outcome, result per vote | Analyst+ |
| Imports | `GET /api/imports/{id}/rows?outcome=` | Rejected and flagged rows: line, reason, original value | Analyst+ |
| Conflicts | `GET /api/conflicts?status=&voteId=` | Review inbox with vote filter | Analyst+ |
| Conflicts | `GET /api/conflicts/{id}` | Approved vs proposed, with source file, uploader, time | Analyst+ |
| Conflicts | `POST /api/conflicts/{id}/decision` | Accept or reject with note; 409 if already decided | Analyst+ |
| Votes | `GET /api/votes?session=&from=&to=&q=&hasPosition=` | Vote list with position, weight, open conflicts | Analyst+ |
| Votes | `GET /api/votes/{id}` | Details and country votes | Analyst+ |
| Votes | `GET /api/votes/{id}/timeline` | Full history of the vote | Analyst+ |
| Votes | `PUT /api/votes/{id}/position` | Set position and weight | Admin |
| Votes | `POST /api/votes/positions/bulk` | Set positions by filter | Admin |
| Snapshots | `POST /api/snapshots` | Create from filter or list; 202 | Analyst+ |
| Snapshots | `GET /api/snapshots` | List with status and published flag | Analyst+ |
| Snapshots | `GET /api/snapshots/{id}` | Rules, scores, ranking | Analyst+ |
| Snapshots | `GET /api/snapshots/{id}/countries/{code}` | Per-vote contribution breakdown | Analyst+ |
| Snapshots | `POST /api/snapshots/{id}/publish` | Publish for a quarter | Admin |
| Users | `GET /api/users`, `GET /api/me` | User switcher and current identity | Any |

The OpenAPI document generates the typed Angular client (ng-openapi-gen or NSwag); no hand-written HTTP calls.

## Cross-cutting concerns

Authorization, errors, tracing and secrets are handled once in the pipeline, not per endpoint.

| Concern | Approach |
| --- | --- |
| Identity | Seeded users (Dana, Omer: Analyst; Lena: Admin). The UI switcher sends `X-User-Id`; a custom `AuthenticationHandler` builds a ClaimsPrincipal with the role from the database. Spoofable by design — production auth is out of scope; replacing the handler with Entra ID is the upgrade path |
| Authorization | Policies `Analyst` (Analyst or Admin) and `Admin`, applied with `[Authorize]` on controllers and actions. Actor for every write comes from the principal, never the body |
| Errors | One format: ProblemDetails (RFC 9457) from a global exception handler; `ValidationProblemDetails` with field-keyed `errors`; 409 for concurrency with the current state in extensions |
| Correlation ID | Angular interceptor sends `X-Correlation-ID`; middleware accepts or creates it, adds it to the log scope and the response header, and stores it on imports and jobs so worker logs continue the same trace. Exported to Application Insights via OpenTelemetry |
| Secrets | No keys or connection strings in code or `appsettings.json`. `.env` (git-ignored, `.env.example` committed); in production, Key Vault through a managed identity |
| Upload limits | Raised body and multipart limits on the upload endpoint only (\~200 MB, `Upload__MaxBytes`); held in a temporary file on disk, never in memory, then streamed to Blob Storage |

## Environments

Development runs only infrastructure in Docker; the final run builds and starts everything with one command and uses Azure when configured.

|  | Development | Final run |
| --- | --- | --- |
| Images | Pulled: `postgres`, `azurite` | Pulled: `postgres`, `azurite`; built: `meridian-api`, `meridian-web` |
| Containers | `postgres`, `azurite` | `migrator` (one-shot), `api`, `worker`, `web`, plus `postgres` / `azurite` as fallback |
| Backend | `dotnet run` from the IDE, debugger attached | `api` and `worker` containers |
| Frontend | `ng serve` with `proxy.conf.json` | `web` container on `localhost:8080` |
| Database | Local container | Azure Database for PostgreSQL via `ConnectionStrings__Default` (firewall rule, `SslMode=Require`) |
| Files and telemetry | Azurite, console logs | Azure Blob Storage, Application Insights |
| Command | `docker compose up postgres azurite` | `docker compose up --build` |
| Tests | Test containers Postgres, throwaway | Same — never against the shared Azure database |

## Settled decisions

These rules were open in the assignment or the design and are now fixed; each goes into DECISIONS.md.

- **Abstention (A) counts as half a match;** absent (X) is not counted.
- **Minimum counted votes** is a snapshot setting, default 1; the counted-vote number is shown next to every score.
- **Re-upload after a decision:** a value already rejected, or replaced by an accepted decision, is reported as "previously decided" and opens no conflict — unless the row's `published_at` is newer than the publication the decision was based on.
- **Publishing never overwrites:** a new publication for a quarter supersedes the previous one, which stays viewable and labelled.
- **Membership dates are inclusive** on both ends.
- **Worker runs as its own container** from the same image as the API.
- **No exclusion constraint on memberships:** `btree_gist` can't be enabled on the Azure server, so overlapping periods are checked in code when seeding.
