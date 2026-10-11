# 4. Trade-offs

These are the current design decisions. Each choice solves one problem and leaves a cost or limit.

## 1. Minimal APIs with separate layers

**Decision:** Use small HTTP endpoints, Application services, Domain entities, and Infrastructure storage. Keep the expiry implementation in Infrastructure and run it through a separate host.

**Reason:** The project needs clear responsibilities without many extra patterns or libraries.

**Cost:** Some rules, such as atomic quota updates, live close to SQL. This is a practical layered design, not a complete Clean Architecture implementation. More abstractions should be added only when they make a real change easier.

## 2. SQL locking instead of optimistic version retries

**Decision:** Use short transactions, update locks, conditional quantity updates, and database constraints.

**Reason:** Requests for the same popular sale item must agree on its available quantity. SQL can protect the quantity and reservation insert together. A version column could detect competing updates, but would still need retry and conflict handling.

**Cost:** A popular item becomes a busy database row. Requests can wait, and some may receive `503`. We still need measured load tests before claiming a throughput limit. Redis locks would introduce another coordination system and would not replace SQL consistency checks.

## 3. Hold quota before confirmation

**Decision:** A pending reservation reduces available sale quota immediately. Confirmation keeps it allocated; expiry returns it.

**Reason:** A customer gets a short, clear decision window without another customer taking the same allocation.

**Cost:** Abandoned holds temporarily reduce availability. There is no authenticated customer limit yet, so one caller can hold many units. This is not a warehouse stock ledger, and overlapping sales need a separate stock allocation policy.

## 4. Store idempotency in SQL

**Decision:** Lock each tenant/key pair during creation and keep the key on the saved reservation, with a unique constraint.

**Reason:** A client can retry after losing the response without creating a second hold. A failed capacity request does not permanently block that key.

**Cost:** Key checks add database work. Successful keys remain with their reservation records, so retention needs a future policy. The same key cannot represent two different requests within one tenant.

## 5. Save processing work with confirmation

**Decision:** Insert the pending processing job in the same SQL transaction as confirmation.

**Reason:** We avoid a gap where confirmation succeeds but its work item is lost. SQL is already required, so this first step needs no extra message broker.

**Cost:** The job table alone is not a complete queue system. Claiming jobs, retry delays, leases, failed-job review, and dead-letter handling still need implementation. A future broker would also need a safe way to publish committed jobs without losing or duplicating work.

## 6. Poll for expiry with equal worker instances

**Decision:** Scan persisted holds in bounded batches. Let SQL protect each release. Keep all expiry workers equal instead of assigning masters and slaves.

**Reason:** State survives process restarts, and another instance can release a hold without recovering a private in-memory timer or worker group.

**Cost:** Release is delayed by polling and backlog. Multiple instances may scan the same candidates, which adds database work even though the quantity is protected. The current scan does not guarantee a fair share of work for each tenant. See the [worker group proposal](05-improvements.md#proposed-master-slave-worker-groups) before adding more coordination.

## 7. Shared tables for tenants

**Decision:** Use tenant filters and composite keys in a shared SQL database.

**Reason:** One schema is simple to migrate and supports data relationships within each tenant.

**Cost:** This does not provide authentication, separate database resources, or protection from a noisy tenant. Access checks, request limits, and fair background scheduling are still needed.

## 8. Read directly from SQL for now

**Decision:** Sale reads currently use SQL with no cache.

**Reason:** The first working slice focuses on reservation correctness and avoids cache invalidation problems while the state flow is changing.

**Cost:** This does not yet meet the goal of cached reads under heavy traffic. A cached quantity would also be only a hint; SQL must still decide whether a hold can succeed.

## 9. Add missing seed data without resetting existing rows

**Decision:** Use stable IDs and an explicit seed initializer. Existing quantities and sale dates are preserved.

**Reason:** Running setup again should not erase reservations or refill a used sale.

**Cost:** A seeded sale that was active yesterday can be expired today. Re-running the seed does not restart it. Create a fresh demo sale for later tests. Set `Seed:ReferenceTimeUtc` when repeatable initial timestamps are required.

Next: [Ideas for improvement](05-improvements.md).
