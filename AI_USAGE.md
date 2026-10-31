# AI Usage

How AI tools were used on this project, what was checked by hand, and where the AI's output was changed or rejected.

## Tools

| Tool | Used for |
| --- | --- |
| Claude Code | Implementation, in small reviewed steps |
| TODO | Design documents (HLD, LLD) |

## How the work was run

`CLAUDE.md` holds the project rules: stack, conventions, domain rules that must never be broken, and the working agreement. Claude Code works one commit-sized step at a time, builds and runs tests, then stops. The author reviews each step and commits it.

## Step log

| Step | What the AI did | What was reviewed or changed |
| --- | --- | --- |
| 1. Repo setup | Extended `.gitignore` and `.gitattributes`, wrote `.env.example` and skeleton docs | TODO |
| 2. Solution skeleton | Replaced the ASP.NET template with `src/Meridian.Api` and two test projects, added `docker-compose.yml` (postgres, azurite), the API Dockerfile, OpenAPI generation on build and `/health` | TODO |
| 3. Data model | Added the 13 entities, EF configurations, `AppDbContext`, the initial migration, and Testcontainers tests for constraints, indexes and foreign keys | TODO |
| 4. Seeder and migrator | Added `--migrate-and-seed`, the idempotent `Seeder` with the membership overlap check, the `migrator` Compose service, and unit and integration tests. Ran the container twice against a temporary database | TODO |
| 5. Errors and correlation ID | Added app exceptions, `ProblemDetailsExceptionHandler`, `CorrelationIdMiddleware`, FluentValidation registration and tests for every error type. Running the real app showed the correlation ID missing from console logs, because a dictionary scope prints as its type name; fixed and covered with a test | TODO |
| 6. Auth and users | Added `HeaderAuthenticationHandler`, `ICurrentUser`, the two policies, the Users slice and tests against real seeded users. Reading the generated contract showed wrong types the tests had missed (roles as integers, no 200 on `/api/me`); fixed and covered with a contract test | TODO |
| 7. HistoryWriter and locks | Added `HistoryWriter` with per-row methods and set-based bulk methods for imports, the `Locks.ApprovedData` constant, and tests for row contents and for history committing and rolling back with its transaction. Bulk methods refuse to run outside a transaction | TODO |
| 8. Background jobs | Added `JobQueue`, `JobWorker`, `IJobHandler` and the worker settings, with tests against real PostgreSQL for concurrent claims, lease expiry, retries, the attempt limit and shutdown. The AI found that the LLD let a job that crashes its worker retry forever; the author chose to fail it after 3 attempts | TODO |
| 9. File store | Added `IFileStore` and `BlobFileStore` with write-once saves, SAS URL and connection string modes, and tests against a real Azurite container, including a generated SAS and a large non-seekable stream | TODO |
| 10. Imports endpoints | Added upload, list and status endpoints, with the size limit read from configuration, and tests against real PostgreSQL and Azurite. A test on real Kestrel showed that an oversized upload reset the connection before the client saw the 413, which the in-memory test server had hidden; fixed and covered | TODO |
| 11. Parsing | Added the import job handler, row reader and validator, binary `COPY` and progress, with tests and a timed run of the real files. A test showed one stray quote made CsvHelper merge later rows and lose them, so the reader now treats each line as one row. Reading the real output showed normalizations recorded on rejected rows; fixed and covered | TODO |
| 12. In-file checks | Added duplicate and contradiction checks in SQL, with tests and a timed run of the real files. The LLD's order could hide differing details, so they are checked first. A test timed out because the planner had no statistics on freshly loaded rows; fixed with `ANALYZE` and a longer timeout | TODO |
| 13. Applying | Added `ApprovedDataClassifier`: batches of 500 votes under the lock create votes, classify rows, insert ballots and open conflicts, with the section 7.2 scenario as tests. Done unattended; the choices are in `AUTONOMOUS_DECISIONS.md` | TODO |
| 14. Report and rows | Added `ImportReportWriter`, report and rows endpoints, paging helpers and `ApiTestBase`, with a timed run of the real files. Done unattended | TODO |
| 15. Conflicts | Added conflict reads and `DecideConflict` with the conditional update, supersede and `DecisionNotApplied`, with tests for races, previously-decided cases and details. Done unattended | TODO |
| 16. Vote reads and timeline | Added the vote list, detail and timeline, with a shared `VoteFilter`. Done unattended | TODO |
| 17. Positions | Added `SetPosition` and `SetPositionsBulk`, admin only, with field errors and one history row per vote. Done unattended | TODO |
| 18. Snapshot calculation | Added `CreateSnapshot` and `SnapshotJobHandler` with the set-based scoring SQL, with tests for the assignment example, abstention, ties, the minimum and immutability. Done unattended | TODO |

## Where the AI was wrong or needed correcting

TODO

## Deviations from the design

Recorded in [DECISIONS.md](DECISIONS.md#deviations-from-the-lld).
