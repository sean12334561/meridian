# Meridian Vote Tracker — Low-Level Design

Oct 30, 2026 · @Sem

One ASP.NET Core 10 project organized by feature slice, with thin controllers calling HTTP-free handlers, EF Core on PostgreSQL, set-based SQL for imports and scoring, and an Angular client generated from the OpenAPI contract.

## Solution structure

One backend project holds every feature slice plus a small `Shared` folder; two test projects and the Angular app sit beside it, and the OpenAPI contract is checked in so the frontend can generate its client without a running API.

```
meridian/
  docker-compose.yml  .env.example  README.md  DECISIONS.md  AI_USAGE.md
  contracts/openapi.json            ← emitted on backend build, input for the Angular client
  data/member_states.csv            ← seed file (large CSVs git-ignored)
  src/Meridian.Api/
    Program.cs
    Features/
      Imports/      ImportsController.cs  UploadImport.cs  GetImports.cs  GetImport.cs
                    GetImportReport.cs  GetImportRows.cs  ImportJobHandler.cs
                    Parsing/CsvRowReader.cs  Parsing/RowValidator.cs
                    Classification/InFileChecks.cs  Classification/ApprovedDataClassifier.cs
      Conflicts/    ConflictsController.cs  GetConflicts.cs  GetConflict.cs  DecideConflict.cs
      Votes/        VotesController.cs  GetVotes.cs  GetVote.cs  GetVoteTimeline.cs
                    SetPosition.cs  SetPositionsBulk.cs
      Snapshots/    SnapshotsController.cs  CreateSnapshot.cs  GetSnapshots.cs  GetSnapshot.cs
                    GetCountryBreakdown.cs  PublishSnapshot.cs  SnapshotJobHandler.cs
      Users/        UsersController.cs
    Shared/
      Data/         AppDbContext.cs  Entities/  Configurations/  Migrations/
      History/      HistoryWriter.cs
      Jobs/         JobQueue.cs  JobWorker.cs  IJobHandler.cs
      Storage/      IFileStore.cs  BlobFileStore.cs
      Auth/         HeaderAuthenticationHandler.cs  CurrentUser.cs  Policies.cs
      Errors/       AppExceptions.cs  ProblemDetailsExceptionHandler.cs
      Observability/CorrelationIdMiddleware.cs
      Seeding/      Seeder.cs
  tests/Meridian.UnitTests/         ← row validation, scoring rules
  tests/Meridian.IntegrationTests/  ← Testcontainers PostgreSQL, full scenarios
  web/                              ← Angular app
```

**Conventions**

- One file per use case, named as a verb phrase (`DecideConflict.cs`). It holds the request and response records, the validator and the handler.
- One controller per feature; it only maps HTTP to a handler call and back.
- Handlers never touch `HttpContext`, so the same handler could later be called from an Azure Function.
- A slice may read any table; every write to `votes`, `country_votes` or `conflicts` goes through `HistoryWriter`.
- Each feature registers its own services through one extension method, e.g. `services.AddImportsFeature()`.

## Slice anatomy

Every use case follows the same shape: a request record, a FluentValidation validator, a handler that does the work, and a one-line controller action. Setting a vote's position is the reference example.

```csharp
// Features/Votes/SetPosition.cs
public static class SetPosition
{
    public sealed record Request(string DesiredOutcome, int Weight);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.DesiredOutcome).Must(v => v is "Y" or "N")
                .WithMessage("Position must be For (Y) or Against (N).");
            RuleFor(r => r.Weight).InclusiveBetween(1, 5)
                .WithMessage("Weight must be between 1 (marginal) and 5 (critical).");
        }
    }

    public sealed class Handler(AppDbContext db, HistoryWriter history,
                                ICurrentUser user, IValidator<Request> validator)
    {
        public async Task<VoteDto> Handle(long voteId, Request req, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(req, ct);           // → 400, field-keyed
            var vote = await db.Votes.FindAsync([voteId], ct)
                       ?? throw new NotFoundException("Vote", voteId); // → 404

            var old = (vote.Position, vote.Weight);
            vote.Position = req.DesiredOutcome; vote.Weight = req.Weight;
            history.PositionChanged(vote.Id, old, (req.DesiredOutcome, req.Weight), user.Id);
            await db.SaveChangesAsync(ct);                            // one transaction
            return VoteDto.From(vote);
        }
    }
}

// Features/Votes/VotesController.cs
[ApiController, Route("api/votes"), Authorize(Policy = Policies.Analyst)]
public sealed class VotesController : ControllerBase
{
    [HttpPut("{id:long}/position"), Authorize(Policy = Policies.Admin)]
    public Task<VoteDto> SetPosition(long id, SetPosition.Request req,
        [FromServices] SetPosition.Handler handler, CancellationToken ct)
        => handler.Handle(id, req, ct);
}
```

- **Errors are exceptions** mapped once to ProblemDetails: `ValidationException` → 400, `NotFoundException` → 404, `ConcurrencyConflictException` → 409, auth → 401/403.
- **History in the same save:** `HistoryWriter` adds rows to the same `DbContext`, so `SaveChangesAsync` commits the change and its trail together.
- **Handlers are scoped services**, injected per action with `[FromServices]` so a controller doesn't need a constructor listing every handler.
- **DTOs are records** with a static `From` mapper; no AutoMapper.

## Shared building blocks

Only seven things are shared across slices; everything else lives in its feature folder.

| Component | Responsibility | Key API |
| --- | --- | --- |
| `AppDbContext` | EF Core context over all tables; scoped per request, created from `IDbContextFactory` in the worker | `DbSet<Vote> Votes` etc. |
| `HistoryWriter` | The only way to record changes to approved data; adds `vote_history` rows to the current context | `BallotsImported`, `ConflictOpened`, `ConflictDecided`, `ValueChanged`, `DetailsChanged`, `PositionChanged` |
| `JobQueue` | Enqueue in the caller's transaction; claim, heartbeat, complete, fail or retry | `Enqueue(type, payload)`, `TryClaim()`, `Heartbeat(id)`, `Complete(id)`, `FailOrRetry(id, ex)` |
| `IFileStore` | Store and stream original files; one implementation for Azure Blob, which also serves Azurite | `SaveAsync(key, stream)`, `OpenReadAsync(key)` |
| `HeaderAuthenticationHandler` + `ICurrentUser` | Reads `X-User-Id`, loads the user, builds a ClaimsPrincipal with the role; `ICurrentUser` exposes `Id` and `Role` to handlers | Policies `Analyst` (Analyst or Admin) and `Admin` |
| `ProblemDetailsExceptionHandler` | `IExceptionHandler` that turns app exceptions into RFC 9457 responses with the correlation ID in `extensions` | Exceptions in `AppExceptions.cs` |
| `CorrelationIdMiddleware` | Accepts or creates `X-Correlation-ID`, adds it to the log scope and response, exposes it for job payloads | `ICorrelationContext.Id` |

**Advisory lock constant:** `Locks.ApprovedData = 1001`, taken with `SELECT pg_advisory_xact_lock(1001)` by import apply batches and conflict decisions. It is released automatically at commit or rollback.

## Persistence

Entities map one-to-one to the 13 HLD tables, with snake\_case names and enums stored as text so the database stays readable and the CHECK constraints are explicit.

**Enums (stored as text)**

| Enum | Values |
| --- | --- |
| `Role` | Analyst, Admin |
| `ImportStatus` / `ImportPhase` | Pending, Running, Completed, Failed / Queued, Parsing, Checking, Applying, Done |
| `RowOutcome` | New, Unchanged, Duplicate, Invalid, Contradiction, Conflict, PreviouslyDecided |
| `ConflictStatus` | Pending, Accepted, Rejected, Superseded |
| `HistoryType` | VoteCreated, BallotsImported, ConflictOpened, ConflictAccepted, ConflictRejected, ValueChanged, DetailsChanged, PositionChanged, DecisionNotApplied |
| `SnapshotStatus` / `JobStatus` | Pending, Running, Completed, Failed / Pending, Running, Succeeded, Failed |

Ballots stay a `char` (`Y`, `N`, `A`, `X`) with a CHECK constraint, matching the file format.

**EF Core configuration highlights**

```csharp
// Program.cs
builder.Services.AddDbContextFactory<AppDbContext>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());             // EFCore.NamingConventions

// Configurations/ConflictConfiguration.cs
b.Property(c => c.Status).HasConversion<string>();
b.HasIndex(c => new { c.VoteId, c.Target, c.ProposedValue })
 .IsUnique().HasFilter("status = 'Pending'");    // ING-12

// Configurations/CountryVoteConfiguration.cs
b.HasIndex(cv => new { cv.VoteId, cv.CountryCode }).IsUnique();
b.ToTable(t => t.HasCheckConstraint("ck_ballot", "ballot IN ('Y','N','A','X')"));

// Configurations/VoteConfiguration.cs
b.HasIndex(v => v.VoteRef).IsUnique();
b.ToTable(t => t.HasCheckConstraint("ck_weight", "weight IS NULL OR weight BETWEEN 1 AND 5"));

// Configurations/SnapshotPublicationConfiguration.cs
b.HasIndex(p => p.Quarter).IsUnique().HasFilter("withdrawn_at IS NULL");
```

- **Request handlers** use the scoped `AppDbContext`; **worker handlers** create one context per batch from the factory and use `AsNoTracking()` for reads.
- **Set-based statements** (classification, scoring) use `ExecuteSqlInterpolatedAsync` or `ExecuteUpdateAsync`; parameters are always interpolated, never concatenated.
- **Bulk load** of `import_rows` uses Npgsql binary `COPY` on the context's connection, inside the import's transaction scope.
- **Migrations** live in `Shared/Data/Migrations`; the `migrator` container runs `MigrateAsync()` and the idempotent seeder.

**Traceability columns** (added after tracing the assignment's history questions)

- `conflicts.approved_source_import_id` + `approved_source_line`: copied at detection, so both sides of a conflict keep their origin after the approved value is replaced (CON-02).
- `votes.details_source_import_id`: origin of the current title, date and session (HIS-02).
- `import_rows.conflict_id`: the conflict a row opened or matched.
- `vote_history.target` (country code, `title`, `vote_date`, `session` or `position`) and `old_value` / `new_value` as jsonb, so ballots, details and position + weight share one shape.
- `snapshot_contributions.source_import_id`: the file each counted ballot came from at snapshot time.

## Imports slice

The upload request stores the file and enqueues a job; `ImportJobHandler` then runs four phases — Parsing, Checking, Applying, Done — writing every row's outcome to `import_rows` and the report to `imports.report`.

### Upload (request side)

```csharp
[HttpPost, RequestSizeLimit(200_000_000), RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
public Task<ActionResult<ImportCreatedDto>> Upload(IFormFile file, [FromServices] UploadImport.Handler h, CancellationToken ct)

// UploadImport.Handler
var import = new Import { Id = Guid.NewGuid(), FileName = file.FileName, UploadedBy = user.Id, Status = Pending };
await using var hashing = new HashingStream(file.OpenReadStream());       // SHA-256 while streaming
await files.SaveAsync($"imports/{import.Id}.csv", hashing, ct);           // blob first
import.Sha256 = hashing.Hash; import.BlobKey = ...; import.SizeBytes = file.Length;
db.Imports.Add(import);
jobs.Enqueue("import", new { importId = import.Id, correlationId });      // same SaveChanges
await db.SaveChangesAsync(ct);                                             // → 202 { importId }
```

Rejected before any work (400): no file, extension not `.csv`, empty file. An earlier import with the same SHA-256 is noted in the report by its file name and upload time, because import IDs are GUIDs, but still processed.

### Phase 1 — Parsing (C#)

- Stream the blob through CsvHelper's low-level parser (`csv.Parser.Read()`, `Parser.Record`, `Parser.RawRecord`, `Parser.RawRow`) so a malformed row becomes data, never an exception.
- **File-level failure** (import Failed, nothing applied): not valid UTF-8, header missing any of the 8 required columns after BOM strip (matched by name, case-insensitive), or no data rows.
- Each row goes through `RowValidator` → parsed values plus either `Invalid` with a reason or no outcome yet.
- Rows are written with binary `COPY` in batches of 10,000. The whole phase runs in one transaction, so a crash leaves no partial rows and a retry starts clean. Progress (`processed_rows`, `total_rows` from a quick line count) is written by a separate short-lived context after each batch, so it is visible while the transaction is open.

| `RowValidator` check | Invalid reason example | Normalization (flagged, original kept) |
| --- | --- | --- |
| Exactly 8 columns | "Expected 8 columns, found 5" | — |
| Required fields non-empty | "Missing ballot" | Trim surrounding whitespace |
| `vote_ref` matches `MA/\d{4}/\d{3}` | "Malformed vote id 'MA/2024/3'" | — |
| `session` is an integer | "Session 'abc' is not a number" | — |
| `vote_date` is a real `yyyy-MM-dd` date | "Date '21/02/2024' is not yyyy-mm-dd" | — |
| `country_code` exists in `member_states` | "Unknown country code 'FRN'" | Uppercase (`deu` → `DEU`) |
| Member on `vote_date` (both ends inclusive) | "NZL was not a member on 2024-02-06" | — |
| `ballot` in Y, N, A, X | "Invalid ballot 'Yes'" | — |
| `published_at` is an ISO timestamp | "Invalid published\_at" | — |

### Phase 2 — Checking (SQL, no lock)

Only this import's rows with no outcome yet are considered, so the phase can run alongside other imports.

```sql
-- Exact duplicates: keep the first line, mark the rest
UPDATE import_rows r SET outcome = 'Duplicate', reason = 'Same as line ' || d.first_line
FROM (SELECT line_no, MIN(line_no) OVER (PARTITION BY vote_ref, country_code, ballot) AS first_line
      FROM import_rows WHERE import_id = @import AND outcome IS NULL) d
WHERE r.import_id = @import AND r.line_no = d.line_no AND d.line_no <> d.first_line;

-- Contradictions: same vote and country, different ballots → none stored
UPDATE import_rows r SET outcome = 'Contradiction', reason = 'Different ballots for this country in the file'
WHERE r.import_id = @import AND r.outcome IS NULL AND (r.vote_ref, r.country_code) IN (
  SELECT vote_ref, country_code FROM import_rows
  WHERE import_id = @import AND outcome IS NULL
  GROUP BY vote_ref, country_code HAVING COUNT(DISTINCT ballot) > 1);
```

Vote details (session, date, title) that differ between rows of one vote are recorded per vote in the report. If the vote is **new**, all its rows become Contradiction because the vote can't be created without agreed details; if it **exists**, no details conflict is opened and its ballot rows continue.

### Phase 3 — Applying (SQL, per batch of 500 votes, under the lock)

Each batch is one transaction that starts with `pg_advisory_xact_lock(1001)`, so it always classifies against everything committed before it.

1. **Create missing votes** from the file's details and write `VoteCreated` history.
2. **Details conflicts** for existing votes whose file details differ from approved: one conflict per field (`target` = `title`, `vote_date` or `session`).
3. **Classify ballot rows** in one `UPDATE … FROM` joined to `country_votes`:
   - no approved row → **New**;
   - same ballot → **Unchanged**;
   - different and previously decided → **PreviouslyDecided** (rule below);
   - otherwise → **Conflict**.
4. **Insert** New rows into `country_votes` with `source_import_id` and `source_line`; write one `BallotsImported` history row per vote.
5. **Open conflicts** with `INSERT … ON CONFLICT (vote_id, target, proposed_value) WHERE status = 'Pending' DO NOTHING`; every Conflict row gets conflict\_id (new or existing), and rows whose conflict already existed get the reason "already pending". Write `ConflictOpened` history for new ones.
6. Update `processed_rows` and commit, releasing the lock.

```sql
-- Previously decided: the incoming ballot was already ruled on, by a publication at least as new
EXISTS (SELECT 1 FROM conflicts c
        WHERE c.vote_id = v.id AND c.target = r.country_code
          AND c.published_at >= r.published_at
          AND (   (c.status = 'Rejected' AND c.proposed_value = r.ballot)    -- value was rejected
               OR (c.status = 'Accepted' AND c.approved_value = r.ballot))) -- value was replaced
```

### Phase 4 — Report

Totals per outcome and a per-vote list (new / unchanged / conflicts / invalid counts, details status) are aggregated from `import_rows` into `imports.report` (jsonb); status becomes Completed. The rows endpoint pages through `import_rows` filtered by outcome.

## Conflicts slice

The decision is a single conditional update inside the approved-data lock: whoever flips the status from Pending wins, and the loser gets a 409 that carries the winning decision.

| Use case | Notes |
| --- | --- |
| `GetConflicts` | Filters: `status` (default Pending), `voteId`; returns vote ref, target, approved and proposed values, source file, uploader, upload time; paged |
| `GetConflict` | Same fields plus both sides' source (file name, uploader, `uploaded_at`, `published_at`) for the side-by-side view (CON-02) and the decision if any |
| `DecideConflict` | Request: `Decision` (Accept or Reject), optional `Note` (max 1,000 chars) |

```csharp
// DecideConflict.Handler
await using var tx = await db.Database.BeginTransactionAsync(ct);
await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1001)", ct);

var status = req.Decision == Decision.Accept ? ConflictStatus.Accepted : ConflictStatus.Rejected;
var won = await db.Conflicts
    .Where(c => c.Id == id && c.Status == ConflictStatus.Pending)
    .ExecuteUpdateAsync(s => s
        .SetProperty(c => c.Status, status)
        .SetProperty(c => c.DecidedBy, user.Id)
        .SetProperty(c => c.DecidedAt, DateTimeOffset.UtcNow)
        .SetProperty(c => c.Note, req.Note), ct);

if (won == 0)                                   // someone else decided first, or it doesn't exist
{
    var current = await GetConflict.Load(db, id, ct) ?? throw new NotFoundException("Conflict", id);
    throw new ConcurrencyConflictException("This conflict was already decided.", current);
}

var conflict = await db.Conflicts.SingleAsync(c => c.Id == id, ct);
if (status == ConflictStatus.Accepted)
{
    await ApplyProposedValue(conflict, ct);     // country_votes.ballot or votes.<field>, new source
    await SupersedeOtherPending(conflict, ct);  // other pending proposals for the same target
}
history.ConflictDecided(conflict, user.Id);     // + ValueChanged / DetailsChanged on accept
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

- **409 body:** ProblemDetails with `type = "/problems/conflict-already-decided"` and `extensions.current` = the conflict as it now stands (decision, decider, time, resulting value). The UI shows "Dana already accepted this at 14:02" and refreshes the card (CON-05).
- **Accept on a ballot** sets `country_votes.ballot`, `source_import_id` and `source_line` to the proposal's source; **accept on details** sets the field on `votes`.
- **Reject** changes no approved data; the rejection plus its `published_at` is what later marks re-uploads as PreviouslyDecided.
- Resolved conflicts stay queryable with their decision (CON-06). A losing attempt is rolled back, then a DecisionNotApplied history row (who, what, when) is saved in its own short transaction, so the vote timeline shows it as in the mockup.

## Votes slice

Reads are plain EF queries over \~6,500 votes; the only writes are the admin's position changes, each logged through `HistoryWriter`.

| Use case | Request | Behaviour |
| --- | --- | --- |
| `GetVotes` | `session`, `from`, `to`, `q` (ref or title contains), `hasPosition`, `page`, `pageSize` (max 100) | Each item: ref, date, title, position, weight, open-conflict count, `missingPosition` flag (POS-04). Sorted by date descending |
| `GetVote` | `id` | Details, position and weight, and every country vote with its source file; ballots changed by a decision are flagged |
| `GetVoteTimeline` | `id` | `vote_history` for the vote, newest first, with actor name, related import file and conflict, merged with the publications whose snapshot includes the vote (checked against each snapshot's resolved vote-ID list) |
| `SetPosition` | `DesiredOutcome` (Y/N), `Weight` (1–5) | Admin only. Validation errors return `errors.weight` etc. (POS-02); writes `PositionChanged` with old and new values (HIS-03) |
| `SetPositionsBulk` | The same filter as `GetVotes`, plus `DesiredOutcome`, `Weight`, `OnlyMissing` (default true) | Admin only. Applies to every matching vote in one transaction and writes one `PositionChanged` row per vote; returns the count |

- Routes use the numeric `id`; `GetVotes` accepts `q = MA/2024/003` so users can still search by Assembly reference.
- `OnlyMissing = true` stops a bulk action from silently overwriting positions Lena set by hand.

## Snapshots slice

Creating a snapshot validates the selection synchronously and returns 202; `SnapshotJobHandler` computes everything in two set-based statements inside one REPEATABLE READ transaction.

### CreateSnapshot (request side)

- Request: `Name`, optional `From`/`To`, `Session`, `VoteIds`, `VoteRefs` (Assembly references, the form the UI sends), `ExcludeMissingPositions` (default false), `MinCountedVotes` (default 1).
- At least one selector is required; a selection that matches zero votes → 400.
- Votes in the selection without a position → 400 with `extensions.missingPositions` listing them, unless `ExcludeMissingPositions` is true; then they're stored in `rules.excludedVotes`.
- Saves the snapshot (status Pending, `rules` = `{ abstentionFactor: 0.5, minCountedVotes, excludedVotes }`, the resolved vote ID list in `filter`) and enqueues a `snapshot` job in the same save.

### SnapshotJobHandler (worker)

```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

-- 1. Frozen contributions: one row per country × selected vote, values copied
INSERT INTO snapshot_contributions
  (snapshot_id, country_code, vote_id, ballot, position, weight, source_import_id, counted, points, reason)
SELECT @sid, cv.country_code, v.id, cv.ballot, v.position, v.weight, cv.source_import_id,
       cv.ballot IN ('Y','N','A'),
       CASE WHEN cv.ballot = v.position THEN v.weight
            WHEN cv.ballot = 'A'        THEN v.weight * @abstentionFactor
            ELSE 0 END,
       CASE cv.ballot WHEN 'X' THEN 'Absent' WHEN 'A' THEN 'Abstained (half)'
            ELSE CASE WHEN cv.ballot = v.position THEN 'Matched' ELSE 'Opposed' END END
FROM country_votes cv JOIN votes v ON v.id = cv.vote_id
WHERE v.id = ANY(@voteIds) AND v.position IS NOT NULL;

-- 2. Scores and ranks
INSERT INTO snapshot_scores
  (snapshot_id, country_code, counted_votes, counted_weight, earned_weight, score, rank)
SELECT @sid, country_code, n, w, e,
       CASE WHEN n >= @minCounted THEN ROUND(100 * e / w, 4) END,
       CASE WHEN n >= @minCounted THEN RANK() OVER (ORDER BY
            CASE WHEN n >= @minCounted THEN e / w END DESC NULLS LAST) END
FROM (SELECT country_code,
             COUNT(*) FILTER (WHERE counted)       AS n,
             SUM(weight) FILTER (WHERE counted)    AS w,
             SUM(points)                           AS e
      FROM snapshot_contributions WHERE snapshot_id = @sid
      GROUP BY country_code) s;
```

- Then the handler records `pendingConflictsAtCreation` on the snapshot and sets status Completed. A failure rolls back both inserts; a retry starts clean.
- Ties share a rank because of `RANK()`; scores keep 4 decimals and display 1.
- After completion no code path updates `snapshot_contributions` or `snapshot_scores` (SNP-04).

### Reads

- `GetSnapshot`: status, rules (shown on screen as the scoring rules), the ranking from `snapshot_scores`, and the publication state.
- `GetCountryBreakdown`: the country's `snapshot_contributions` with vote ref, title, ballot, position, weight, points and reason — the per-vote contribution table (SNP-03).

### PublishSnapshot (Admin)

- Request: `Quarter` (`yyyy-Qn`), `Reason` (required when the quarter already has a publication).
- Only Completed snapshots can be published.
- In one transaction: set `withdrawn_at` and `superseded_by` on the current publication for that quarter, if any, then insert the new one. The partial unique index rejects a concurrent double publish, which is returned as 409.

## Background jobs

One `JobWorker` hosts N claim loops; each claimed job is dispatched by type to a keyed `IJobHandler` (`import` → `ImportJobHandler`, `snapshot` → `SnapshotJobHandler`), with a heartbeat keeping its lease alive.

&#91;embedded content: job lifecycle · 4 states\]

A crashed worker stops renewing `locked_until`, so the claim query's second condition hands the job to another loop; handlers are written to be re-run safely. After the third failure the job and its import or snapshot are marked Failed with the error message.

```sql
-- JobQueue.TryClaimAsync
UPDATE jobs SET status = 'Running', attempts = attempts + 1,
                locked_until = now() + interval '2 minutes'
WHERE id = (SELECT id FROM jobs
            WHERE (status = 'Pending' AND run_after <= now())
               OR (status = 'Running' AND locked_until < now())
            ORDER BY created_at
            FOR UPDATE SKIP LOCKED LIMIT 1)
RETURNING id, type, payload, attempts;
```

```csharp
public sealed class JobWorker(IServiceScopeFactory scopes, IOptions<WorkerOptions> opt,
                              ILogger<JobWorker> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(Enumerable.Range(0, opt.Value.Concurrency).Select(_ => LoopAsync(ct)));

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var job = await queue.TryClaimAsync(ct);
            if (job is null) { await Task.Delay(opt.Value.PollIntervalMs, ct); continue; }

            using var logScope = log.BeginScope(new { JobId = job.Id, job.CorrelationId });
            await using var heartbeat = queue.StartHeartbeat(job.Id);   // every 30 s: locked_until = now() + 2 min
            try
            {
                var handler = scope.ServiceProvider.GetRequiredKeyedService<IJobHandler>(job.Type);
                await handler.RunAsync(job, ct);
                await queue.CompleteAsync(job.Id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await queue.FailOrRetryAsync(job, ex, ct);   // < 3 attempts: Pending, run_after = now() + 30 s × attempts
            }                                                // else: Failed + last_error, handler.OnFailedAsync
        }
    }
}
```

- `jobs` gets a `run_after` column for the retry backoff (default `now()`).
- Handlers are registered with `AddKeyedScoped<IJobHandler, ImportJobHandler>("import")`; adding a job type means adding one handler and one registration.

## Request pipeline and configuration

`Program.cs` builds one host for all three roles; command-line and configuration flags decide whether it serves HTTP, runs the worker, or migrates and exits.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddShared(builder.Configuration)          // DbContext factory, HistoryWriter, JobQueue, IFileStore, Seeder
    .AddImportsFeature().AddConflictsFeature().AddVotesFeature().AddSnapshotsFeature()
    .AddValidatorsFromAssemblyContaining<Program>()
    .AddProblemDetails().AddExceptionHandler<ProblemDetailsExceptionHandler>()
    .AddAuthentication(HeaderAuthenticationHandler.Scheme)
        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.Scheme, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Analyst, p => p.RequireRole("Analyst", "Admin"))
    .AddPolicy(Policies.Admin,   p => p.RequireRole("Admin"));
builder.Services.AddControllers();
builder.Services.AddOpenApi();                 // build-time document → contracts/openapi.json
if (builder.Configuration.GetValue<bool>("Worker:Enabled"))
    builder.Services.AddHostedService<JobWorker>();
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();
if (args.Contains("--migrate-and-seed")) { await app.MigrateAndSeedAsync(); return; }

app.UseMiddleware<CorrelationIdMiddleware>();  // first: every log line and error carries the ID
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapControllers();
app.Run();
```

**Configuration (environment variables, nothing secret in `appsettings.json`)**

| Variable | Example | Purpose |
| --- | --- | --- |
| `ConnectionStrings__Default` | Azure: `Host=…postgres.database.azure.com;…;SslMode=Require` · local: `Host=postgres;…` | Database |
| `Storage__ConnectionString` | Azure Blob connection string, or `UseDevelopmentStorage=true` for Azurite | Original files |
| `Storage__Container` | `imports` | Blob container |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | from the Azure resource | Telemetry; omitted → console logs only |
| `Worker__Enabled` / `Worker__Concurrency` / `Worker__PollIntervalMs` | `true` / `2` / `2000` | Worker role |
| `Seed__MemberStatesPath` | `/seed/member_states.csv` | Seeder input |
| `Upload__MaxBytes` | `200000000` | Upload limit |

- **Users** are seeded with fixed IDs (Dana, Omer, Lena) so the frontend switcher and tests can rely on them.
- **Correlation ID** comes from `X-Correlation-ID` or `Activity.Current.TraceId`; it's copied into job payloads, and the worker opens a log scope and an `Activity` with it, so a request and its background job share one trace.

## Angular client

The frontend mirrors the backend slices, talks to the API only through the generated client, and handles identity, correlation, validation and 409s once in interceptors and shared helpers.

```
web/src/app/
  core/
    api/                    ← generated by ng-openapi-gen from contracts/openapi.json (never edited)
    interceptors/           user-id.interceptor.ts  correlation-id.interceptor.ts  problem.interceptor.ts
    user/                   current-user.service.ts (signal + localStorage)  user-switcher.component.ts
  shared/                   problem-details.ts (field-error mapping)  status-badge  confirm-dialog
  features/
    imports/                import-upload  import-list  import-report
    review/                 conflict-inbox  conflict-detail
    votes/                  vote-list  vote-detail (position form + timeline)
    snapshots/              snapshot-create  snapshot-list  snapshot-detail  country-breakdown
  app.routes.ts             lazy routes per feature
```

- **Client generation:** `npm run generate:api` runs ng-openapi-gen on `contracts/openapi.json`; the generated code is committed so the Docker build needs no running API.
- **Interceptors:** add `X-User-Id` from the switcher, add a new `X-Correlation-ID` per request, and turn error responses into a typed `Problem`.
- **Field errors (POS-02):** `applyServerErrors(form, problem)` maps `problem.errors.weight` to `form.controls.weight.setErrors({ server: … })`, so the message shows under the field.
- **409 (CON-05):** the conflict detail reads `problem.extensions.current`, shows "already decided by … at …" in a banner and re-renders the card with the current state; it doesn't show a generic error toast.
- **Progress (ING-02, SNP-02):** `shared/poll.ts`: `timer(0, 1000).pipe(exhaustMap(() => load()), takeWhile(r => !isFinal(r), true))`, stopped by `takeUntilDestroyed`. `exhaustMap` so a slow call is not cancelled, and a failed call is reported without ending the polling unless it is a 4xx. The import ID is in the route (`/imports/:id`), so a refresh resumes polling.
- **Role-aware UI:** admin-only controls are hidden for analysts as a convenience; the API still enforces the rule.
- **UI kit:** Angular Material for tables, forms, dialogs and the progress bar; standalone components with signals, no NgRx.

## Tests

Unit tests cover the pure C# rules; integration tests run the real SQL against a throwaway PostgreSQL from Testcontainers and replay the assignment's own section 7.2 scenario end to end.

**Unit (xUnit, no database)**

- `RowValidator`: one case per rule in the Imports table, including normalization flags (`deu` → `DEU`) and the inclusive membership boundaries (vote on `member_to` is valid, the day after is not).
- `SetPosition.Validator` and `CreateSnapshot` validation: weight 0, 6 and 7 rejected with the right field key.
- Scoring arithmetic: the worked examples (Brazil 80.0, France 60.0 with the abstention), ties sharing a rank, below-minimum countries unranked.

**Integration (xUnit + Testcontainers PostgreSQL, `WebApplicationFactory`, one migrated database per test class)**

| Scenario | Asserts |
| --- | --- |
| Section 7.2 original file | New, Duplicate, Contradiction and Invalid outcomes per row as in the assignment table |
| Section 7.2 revised file | BRA conflict, IND unchanged, FRA filled in as New (ING-10), vote 105 new, title conflict on 101, vote 102 untouched (ING-09) |
| Re-upload of the revised file | 0 New, 0 new conflicts; pending conflicts counted as already pending (ING-12) |
| Concurrent decisions | Two parallel `DecideConflict` calls → exactly one 200 and one 409 with `extensions.current` (CON-05) |
| Previously decided | Reject, re-upload → PreviouslyDecided; same value with a newer `published_at` → Conflict |
| Snapshot immutability | Create snapshot, accept a conflict, change a weight → the snapshot's scores and contributions are unchanged (SNP-04) |
| Authorization | Analyst on `PUT /votes/{id}/position` and `POST /snapshots/{id}/publish` → 403 |
| Publishing twice | Second publish for the same quarter supersedes the first; both remain readable |

The historic file is not part of the automated suite; its load time is measured manually and reported in the README.
