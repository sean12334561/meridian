# Decisions

Rules the assignment left open and choices made in the design, with their reasoning. The design itself is in [docs/HLD.md](docs/HLD.md) and [docs/LLD.md](docs/LLD.md).

## Scoring and ranking

### Abstention counts as half a match; absent is not counted

Score = 100 x (sum of matched weights + 0.5 x sum of abstention weights) / (sum of weights where ballot is Y, N or A).

**Why:** TODO

### Minimum counted votes is a snapshot setting, default 1

The counted-vote number is shown with every score. Countries below the minimum show "insufficient data" and get no rank.

**Why:** TODO

### Ties share a rank

`RANK()` over unrounded scores, so 1, 2, 2, 4.

**Why:** TODO

## Imports

### Already-decided values don't reopen conflicts

A value already rejected, or replaced by an accepted decision, is reported as `PreviouslyDecided` and opens no conflict, unless the row's `published_at` is newer than the publication the decision was based on.

**Why:** TODO

### Normalize only trim and uppercase country code

Both are flagged in the report with the original value. Everything else invalid is rejected with a reason.

**Why:** TODO

### Membership dates are inclusive on both ends

**Why:** TODO

### Membership overlaps are checked in code

**Why:** `btree_gist` can't be enabled on the Azure server, so an exclusion constraint isn't available.

## Snapshots

### A second publication for a quarter supersedes the first

The earlier publication gets `withdrawn_at` and `superseded_by`; both snapshots stay viewable.

**Why:** TODO

## Infrastructure

### Background jobs use a Postgres table, not Service Bus

`FOR UPDATE SKIP LOCKED`, lease and heartbeat, 3 attempts with backoff.

**Why:** Service Bus wasn't provided, and the Postgres queue is sufficient.

### The worker is its own container, from the same image as the API

**Why:** TODO

### Azure storage is reached through a container SAS URL

`Storage__ContainerSasUrl` takes priority when set; `Storage__ConnectionString` remains for Azurite.

**Why:** The author has no permission to get a connection string for the storage account.

### Key Vault is read by a script before startup, not by the app

`scripts/pull-secrets.ps1` uses the operator's `az login` to build the git-ignored `.env` from `.env.example` and the vault. The app only ever sees environment variables.

**Why:** Containers can't use `az login`, and a service principal isn't possible because the author can't assign it a role. Trade-off: secrets sit in plain text in `.env` on the local machine. In production: managed identity, read directly by the app.

### Cosmos DB not used

**Why:** The data is relational and transactional.

## Deviations from the LLD

| Area | LLD says | Implemented | Why |
| --- | --- | --- | --- |
| `Storage__ConnectionString` | `UseDevelopmentStorage=true` for Azurite | That value from the host; inside containers, a full connection string with `BlobEndpoint=http://azurite:10000/devstoreaccount1` | `UseDevelopmentStorage=true` resolves to `127.0.0.1`, which inside the `api` and `worker` containers is the container itself |
| Azure storage access | Azure Blob connection string | `Storage__ContainerSasUrl`, a container SAS URL | No permission to get a connection string. The container must already exist, because a container SAS can't create it |
| Key Vault | Not used (HLD, `CLAUDE.md`) | Optional source for `.env`, through `scripts/pull-secrets.ps1` | Author wants settings kept in Key Vault; no service principal is available, so the app can't read it directly |
| Health endpoint | Not in the API surface | Unauthenticated `GET /health`, outside `/api` so nginx doesn't expose it | Gives Compose a readiness check for `api` and an end-to-end smoke test |
| Foreign key indexes | Not mentioned | EF's automatic foreign key indexes are turned off; only the indexes in the HLD exist | The HLD adds indexes only when `EXPLAIN ANALYZE` shows a need, and extra ones would slow the bulk inserts |
| `votes.position` | CHECK constraints named for ballots and weights only | Added `ck_position` (null, `Y` or `N`) | Same protection for the other scoring input |
| CSV columns | Uses `vote_ref` and `title`; lists 7 of the 8 columns | The file header is `assembly_vote_id,session,vote_date,resolution_title,country_code,country_name,ballot,published_at`. `assembly_vote_id` maps to `vote_ref`, `resolution_title` to `title`. `country_name` is kept on `import_rows` only | The LLD's names are the domain's; the file's names stay at the parsing boundary. Approved data gets country names from `member_states` |
| C# style | Code samples use `var`, `sealed` and `record` | Explicit types and plain `public class` everywhere. DTOs and requests are classes with `required` `init` properties | Author's preference |
| `country_code` columns | Tables reference a country | No foreign key to `member_states` | Its key is `(country_code, member_from)` because Iceland has two periods, so `country_code` alone isn't unique. Import validation checks the code |
| CsvHelper version | Not specified | Pinned to 33.1.0 | 34.0.0 replaces the API with a source-generated one and removes `ClassMap`. 33.x is stable and also covers the import step's streaming parse |
| 409 and other extension fields | `extensions.current`, `extensions.missingPositions` | Top-level JSON members: `current`, `missingPositions`, `correlationId` | RFC 9457 puts extension members at the top level of the object, and that's how ASP.NET Core serializes `ProblemDetails.Extensions`. The C# side still uses `Extensions` |
| Bodyless errors | Not mentioned | `UseStatusCodePages()` in the pipeline | The assignment asks for one error format across the API, and 401, 403 and unknown routes otherwise return an empty body |
| Supplied `X-Correlation-ID` | Accepted as given | Accepted only if 1 to 64 letters, digits, `-`, `_` or `.`; otherwise replaced | The client's value goes into logs and job payloads, so it must not be able to inject content |
| `GET /api/users` | Role "Any" | Anonymous | The UI needs the list to fill the switcher before anyone is picked. It exposes only names and roles of three demo users. `GET /api/me` needs a user |
| Users slice files | Only `UsersController.cs` | Also `GetUsers.cs`, `GetMe.cs`, `UserDto.cs` | Keeps the rule that controllers only map HTTP to a handler |
| JSON | Not mentioned | Enums as strings, strict numbers, for controllers and for the OpenAPI document | The contract generates the Angular client, so it must match what's on the wire. Without this, roles were documented as integers and every number as `integer | string`. Strict numbers also mean `"5"` is rejected where a number is expected |
| API image | Not specified | The final image also installs `libgssapi-krb5-2` | Npgsql looks for this library on connect, and without it every run logs an error even though the connection works |
