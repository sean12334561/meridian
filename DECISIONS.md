# Decisions

## Storage and data

**Blob Storage or Cosmos DB.** Blob Storage is used for files, and PostgreSQL for structured records. Blob Storage is designed for unstructured text and binary objects, such as uploaded CSVs and PDFs. Cosmos DB would add a second database without a clear benefit here.

## Infrastructure

**Background jobs in a PostgreSQL queue.** Imports and snapshot calculations run from a PostgreSQL job table processed by the worker. It reuses the database, and a job is saved in the same transaction as its import. Jobs keep progress, bounded retries and recovery information. Service Bus wasn't in the supplied environment; in production I'd adopt it only if bursts or independently scaled workers justify a dedicated broker.

**Separate worker container, with claim-check.** The file is saved to Blob Storage on upload, and the worker reads it when the job starts. The worker runs in its own container, so it scales on its own (`--scale worker=N`), API restarts don't interrupt jobs, and it maps directly to a Container Apps worker.

**Modular monolith, not microservices.** All features share one transactional model. Separate services would bring eventual consistency, which risks the main guarantee (approved data is never silently changed) for no benefit at this size.

**Secrets.** Secrets are kept in Key Vault and pulled into the git-ignored `.env` through the Azure CLI with the developer's own identity, because the containers can't sign in interactively and I can't assign roles to a service principal.

## Concurrency

**Concurrent decisions.** A decision is a conditional update `WHERE status = 'Pending'`: the second analyst gets a 409 with the current state, and a `DecisionNotApplied` history row. Import batches and decisions share an advisory lock, so a decision never interleaves with an import, at the cost of waiting for the running batch.

**Import order not guaranteed.** The worker runs two jobs at once, so files uploaded back to back can apply in either order, and a revised file that overtakes its original opens the same conflicts with the sides swapped. Approved data still never changes without a decision, and each conflict shows both publication dates, so I accepted this over running imports one at a time.

## Security

**Header auth.** An `X-User-Id` header with three demo users is a deliberate, spoofable simplification. The `Analyst` and `Admin` policies are enforced on the API, and the acting user always comes from the principal, never the request body.

## Domain rules

- **Normalization.** The only automatic fixes are trimming whitespace and upper-casing the country code, and both are flagged in the import report with the original value. Anything else invalid is rejected with a reason rather than guessed.
- **Abstentions** are counted as half a match. Score = 100 x (sum of matched weights + 0.5 x sum of abstention weights) / (sum of weights where ballot is Y, N or A).
- **Minimum counted votes.** For snapshots, the minimum number of counted votes defaults to 1, so a score is calculated, and the number of votes is shown for context. Countries below the minimum show "insufficient data" and get no rank.
- **Re-upload after a decision.** If a value that was already rejected, or that was replaced by an accepted decision, arrives again, it is marked as previously decided and no new conflict is opened, unless the incoming row's `published_at` is newer.
- **Second snapshot for the same quarter.** It never overwrites the first, and everything stays traceable. The earlier snapshot stays fully viewable and is labelled. Publishing a second one in the same quarter requires a reason.
- **Membership dates.** `member_from` and `member_to` are inclusive.

## History and traceability

- Approved data is kept as current values in `votes` and `country_votes`, each recording the import it came from.
- Every change goes through one `HistoryWriter` that writes a `vote_history` row (target, old → new, actor, import, conflict) in the same transaction, so no value changes without a trace.
- Conflicts copy the approved value and its source when detected, so both sides keep their origin after a decision.
- Every file is kept in Blob Storage, and every row's outcome in `import_rows`.
- Snapshots copy their inputs (ballot, position, weight, source file) and are never modified, so a published index can be reproduced exactly.

I chose this over full version tables (`valid_from`/`valid_to`) because it keeps reads simple, and the questions the institute asks are answered by the history log and the snapshot copies.

## Moving to production

- **Authentication.** Microsoft Entra ID tokens replace the `X-User-Id` header, with Analyst and Admin as app roles, and other systems calling the API get their own Entra application instead of a shared key. The policies stay, so the change is mainly the authentication handler and sign-in in Angular.
- **Managed identities** for `api` and `worker` to reach Blob Storage, Key Vault and PostgreSQL. That removes the SAS URL, the database password and `scripts/pull-secrets.ps1`.
- **Private endpoints** for PostgreSQL and storage, in place of firewall rules for individual IP addresses.
- **Hosting.** Images in Azure Container Registry. `web`, `api` and `worker` run as separate Container Apps so the worker scales on its own, and `migrator` runs as a one-off job before each release, with the only login allowed to change the schema. An Azure Function doesn't fit the worker: a large import runs far past the Consumption plan's 10-minute limit.
- **Database.** Move off `Standard_B1ms`, disk first: during the historic import IOPS sat at 100% of the limit with up to 85 queued operations, while CPU averaged 26%. Raise IOPS and memory, measure again, then revisit the batch size, since shorter batches mean shorter waits for a decision.

## Dividing the work between four developers

I would divide the work so each developer owns one area, and two people rarely change the same code at once.

- **Azure and running the system (one developer):** Container Apps, Container Registry, managed identities, private endpoints, the separate migrations login, alerts, and separate service names for `api` and `worker` in Application Insights.
- **Backend (two developers):**
  - **Authentication and the API:** JWT and Entra ID, app roles, application access for API callers, and removing the header authentication.
  - **Database and background work:** sizing the database by measurement, batch size and decision waits, and the Service Bus test.
- **Frontend and quality (one developer):** design improvements, Entra sign-in, tests for the screens that have none, and end-to-end tests of the main flows once the other developers' work is in.
