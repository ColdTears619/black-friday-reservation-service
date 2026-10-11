# Testing

Use these checks after [setup](setup-and-seed.md). Swagger is the easiest way to inspect individual API responses. Automated tests cover validation and SQL behavior; the script exercises the running API with concurrent HTTP requests.

## Automated tests

Build the solution:

```sh
dotnet build BlackFridaySale.sln
```

Run unit tests without SQL:

```sh
dotnet test tests/Sale.IntegrationTests/Sale.IntegrationTests.csproj --filter 'Category=Unit'
```

The following commands require SQL and **create and delete temporary databases** named `BlackFridaySale_Tests_<random-id>` on the configured server. The SQL account needs database create/drop permissions. The fixture replaces the connection's database name, so these tests do not use `BlackFridaySale` as their test database. Normal cleanup deletes each temporary database; an interrupted process may leave one behind.

Run all SQL tests:

```sh
dotnet test tests/Sale.IntegrationTests/Sale.IntegrationTests.csproj --filter 'Category=Database'
```

Run only reservation concurrency tests:

```sh
dotnet test tests/Sale.IntegrationTests/Sale.IntegrationTests.csproj --filter 'FullyQualifiedName~ReservationConcurrencyTests'
```

Run the complete suite:

```sh
dotnet test tests/Sale.IntegrationTests/Sale.IntegrationTests.csproj
```

Coverage includes competing holds, duplicate creation keys, repeated confirmation, rollback on insert failure, tenant separation, seed repeatability, competing expiry workers, and expiry racing with confirmation. For example, one test sends 100 requests against a quota of 30 and checks that only 30 holds succeed.

These tests do not require a running API or worker process. They exercise services and persistence directly, including worker behavior. Use Swagger or the script for the running HTTP host.

## Swagger walkthrough

Open `http://localhost:5080/swagger` while the API runs in Development mode.

1. List sales for `tenant-electronics`, or create a new sale with a start time before now and an end time after now. Use actual current UTC values, not dates copied from an old example. A valid seeded product is `TV-65-4K`; `280000000` is below its regular price.
2. Copy that sale's ID into the reservation endpoint's `saleEventId` route field. Set `tenantId` to `tenant-electronics`.
3. Set `Idempotency-Key` to a new value, such as a newly generated UUID, and submit this body:

```json
{
  "sku": "TV-65-4K",
  "quantity": 1
}
```

4. Expect `201`. Save the returned reservation `id` and inspect `expiresAt`.
5. Repeat the exact request with the same key. Expect `200` and the same reservation ID. The deadline must not move.
6. Before the deadline, call the confirmation endpoint with that reservation ID. Expect `202` and `Confirmed`. Repeat it to check that confirmation is safe to retry.
7. For a separate expiry check, create another hold with a new key and leave it unconfirmed. After its deadline, confirmation returns `410`. The running worker also releases expired holds automatically.

Confirmation creates a pending processing job, but no order-processing worker consumes it yet. The quota used by a confirmed reservation will not return through expiry.

## Capacity demo

Start both the API and worker. Consider using a 600-second hold override from the setup guide to leave enough time for manual checks.

This script **writes demo data to the database used by the running API**. It creates a new sale and leaves its records in place. It does not confirm any purchase.

```sh
python3 scripts/check_reservation_capacity.py
```

An explicit address and capacity can also be supplied:

```sh
python3 scripts/check_reservation_capacity.py --base-url http://localhost:5080 --capacity 100
```

The script uses the seeded electronics tenant and TV SKU. It creates a fresh sale, sends 100 independent one-unit hold requests, checks distinct reservation IDs and zero capacity, then sends request 101 with a new key. The last request should return `409 No reservable capacity.` It retries network errors and `503` responses with the same key, so the number of HTTP attempts can be greater than the number of independent requests.

The output includes the exact tenant, sale ID, SKU, first expiry time, and a request to try in Swagger or with curl. These are independent requests, not 100 authenticated user accounts. The elapsed time is useful context, not a formal load-test result.

To test release, keep using the **same sale ID** after its holds expire and the worker releases them. Do not run the script again for this check: each script run creates a different sale and fills its capacity again.

## Troubleshooting the demo

| Result | What to check |
| --- | --- |
| Swagger returns `201` when capacity should be full | Copy the latest script's sale ID into the route. The same SKU can belong to different sales. Also check whether holds already expired and capacity returned. |
| `409 No reservable capacity.` after the deadline | Check that the worker is running against the same database. Read its logs for sweep failures or backlog. Expiry release may lag behind the deadline. |
| `409 Idempotency key conflict.` | That key already belongs to a different successful request in this tenant. Use a fresh key for a new sale, SKU, or quantity. |
| `409 Sale is not active.` | The sale window has not started or has ended. Released capacity does not reopen a closed sale. |
| `200` with an expired reservation | The key belongs to an earlier successful hold. Use a new key to create another hold. |
| `503 Reservation service is busy.` | Retry after the indicated delay, keeping the same key and body. |
| `500` during setup or a request | Inspect the structured exception log. Check the SQL connection and that migrations were applied; do not assume every `500` has the same cause. |

If a request only failed for lack of capacity, its key was not stored. After release, the same unchanged request can receive `201`. This differs from reusing a key that already created a reservation successfully.

For larger load and recovery scenarios, see [future testing work](05-improvements.md#load-and-failure-testing).
