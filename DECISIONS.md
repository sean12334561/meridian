# Decisions

## Storage and data

**Blob Storage or Cosmos DB.** Blob Storage is used for files, and PostgreSQL for structured records. Blob Storage is designed for unstructured text and binary objects, such as uploaded CSVs and PDFs. Cosmos DB would add a second database without a clear benefit here.

**Membership overlap check in code, not in the database.** Postgres can enforce non-overlapping membership periods with an exclusion constraint, but that requires the `btree_gist` extension, which can't be enabled on the provided Azure server. The risk is low because membership is static seed data.

**Indexes.** Unique indexes are used to enforce rules. Otherwise, only big tables that are searched by a narrow filter are indexed.

## Infrastructure

**Background jobs in a PostgreSQL queue.** Background imports and snapshot calculations use a durable PostgreSQL job queue processed by a background worker. This reuses required infrastructure and keeps job creation transactional with application records. Azure Service Bus was unavailable in the supplied environment. Jobs retain progress, bounded retries and recovery information. For production, we would evaluate Service Bus when workload bursts or independently scaled workers justify a dedicated broker.

**Claim-check.** For the workers, I went with the claim-check pattern: the file is saved to Azure Blob Storage on import, and the worker reads the file when the job starts running.

**Runtime topology: modular monolith, not microservices.** Why not microservices: all features share one transactional model. Splitting into services would mean a database per service, messaging between them, and eventual consistency, which adds risk to the system's main guarantee (approved data is never silently changed) for no benefit at this size.

**Separate worker container, not a worker inside the API.**
- The worker can be scaled independently of the API with `--scale worker=N`.
- Restarting the API doesn't interrupt running jobs.
- It matches how it would run in Azure if decided in production (a Container Apps worker).

**Secrets.** Secrets are stored in Key Vault and loaded before the run through the Azure CLI, using the developer's own identity, because the containers can't use interactive login and I don't have permission to assign roles to a service principal. The script writes the secrets into the git-ignored `.env`, so they sit in plain text on the developer's machine. In production, I'd use a managed identity with the Key Vault configuration provider.

## Concurrency

**Concurrent decisions.** A decision is a conditional update `WHERE status = 'Pending'`, so if two analysts decide the same conflict, only the first one applies. The second gets a 409 with the current state, and a `DecisionNotApplied` history row records the attempt. Import batches and decisions take the same advisory lock, so a decision can never interleave with an import changing the same approved data. The cost is that a decision may wait for the running batch to finish.

**Import order not guaranteed.** The worker runs two jobs at once (`Worker__Concurrency=2`), so files uploaded back to back can apply in either order. If a revised file overtakes its original, the same conflicts open with the two sides swapped. Approved data still never changes without a decision, and each conflict shows both publication dates, so I accepted this instead of forcing imports to run one at a time.

## Security

**Header auth.** Authentication is an `X-User-Id` header with three demo users and a user switcher in the UI. This is a deliberate simplification, since production auth was out of scope, and anyone who can reach the API can fake the header. The `Analyst` and `Admin` policies are enforced on the API, and the acting user always comes from the authenticated principal, never from the request body.

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
