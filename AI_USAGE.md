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

## Where the AI was wrong or needed correcting

TODO

## Deviations from the design

Recorded in [DECISIONS.md](DECISIONS.md#deviations-from-the-lld).
