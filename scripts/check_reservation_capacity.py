#!/usr/bin/env python3
"""Create a demo sale, fill its capacity with concurrent holds, and check rejection.

Uses Python's standard library. Running this script writes a new sale and pending
reservations through the API. It never confirms purchases or deletes data. Holds
are released by the expiration worker; the demo sale remains for inspection.
"""

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
import json
import shlex
import sys
from threading import Event
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen
from uuid import uuid4


def request(base_url, method, path, body=None, key=None):
    headers = {"Content-Type": "application/json"}
    if key is not None:
        headers["Idempotency-Key"] = key
    req = Request(
        base_url + path,
        method=method,
        data=None if body is None else json.dumps(body).encode("utf-8"),
        headers=headers,
    )
    try:
        try:
            response = urlopen(req, timeout=30)
        except HTTPError as error:
            response = error
        with response:
            text = response.read().decode("utf-8", errors="replace")
            try:
                data = json.loads(text)
            except json.JSONDecodeError:
                data = {"detail": text[:500]}
            return response.status, data
    except (URLError, TimeoutError, ConnectionError) as error:
        return 0, {"detail": str(error)}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5080")
    parser.add_argument("--capacity", type=int, default=100,
                        help="Capacity and number of independent requests (1-100; default 100).")
    args = parser.parse_args()
    if not 1 <= args.capacity <= 100:
        parser.error("--capacity must be between 1 and 100")
    args.base_url = args.base_url.rstrip("/")
    return args


def run(args):
    tenant = "tenant-electronics"
    sku = "TV-65-4K"
    run_id = uuid4().hex
    now = datetime.now(timezone.utc)
    sales_path = f"/api/tenants/{quote(tenant, safe='')}/sales"
    print(f"Target: {args.base_url}", flush=True)
    print(f"Creating a NEW demo sale and {args.capacity} pending holds; no purchase confirmation.", flush=True)
    status, sale = request(args.base_url, "POST", sales_path, {
        "name": f"Capacity demo {run_id[:8]}",
        "startsAt": (now - timedelta(minutes=1)).isoformat(),
        "endsAt": (now + timedelta(minutes=30)).isoformat(),
        "items": [{"sku": sku, "discountedPrice": 280000000, "initialQuantity": args.capacity}],
    })
    require(status == 201, f"Sale creation failed: HTTP {status}, {sale}. Check that the API and seed data are ready.")
    sale_id = sale["id"]
    print(f"tenantId: {tenant}\nsaleEventId: {sale_id}\nsku: {sku}", flush=True)
    path = f"{sales_path}/{sale_id}/reservations"
    body = {"sku": sku, "quantity": 1}
    gate = Event()

    def reserve(index):
        gate.wait()
        key = f"capacity-{run_id}-{index}"
        # A timeout can occur after commit; all retries keep the original key.
        for attempt in range(1, 4):
            status, result = request(args.base_url, "POST", path, body, key)
            if status not in (0, 503) or attempt == 3:
                return status, result, attempt
            time.sleep(0.25 * attempt)

    started = time.monotonic()
    with ThreadPoolExecutor(max_workers=args.capacity) as pool:
        futures = [pool.submit(reserve, index) for index in range(args.capacity)]
        gate.set()
        results = [future.result() for future in futures]

    counts = dict(sorted(Counter(status for status, _, _ in results).items()))
    print(f"Independent requests: {args.capacity}; HTTP attempts including retries: {sum(x[2] for x in results)}", flush=True)
    print(f"Response counts: {counts}; elapsed: {time.monotonic() - started:.2f}s", flush=True)
    failures = [(status, result) for status, result, _ in results if status not in (200, 201)]
    require(not failures, f"Some reservations failed: {failures[:3]}. The demo sale and any successful holds remain available for inspection.")
    holds = [result for _, result, _ in results]
    require(len({hold["id"] for hold in holds}) == args.capacity,
            "Independent requests did not return distinct reservation IDs.")
    require(all(hold["status"] == "Pending" and hold["quantity"] == 1 for hold in holds),
            "Expected one pending unit per reservation.")
    first_expiring = min(holds, key=lambda hold: datetime.fromisoformat(hold["expiresAt"].replace("Z", "+00:00")))
    print(f"First hold expires at: {first_expiring['expiresAt']}", flush=True)

    status, held = request(args.base_url, "GET", f"/api/tenants/{tenant}/reservations/{first_expiring['id']}")
    require(status == 200 and held.get("canConfirm") is True,
            "The earliest hold has already expired. Increase Reservations:HoldSeconds and run again.")
    status, current_sale = request(args.base_url, "GET", f"{sales_path}/{sale_id}")
    require(status == 200, f"Could not read sale: HTTP {status}, {current_sale}")
    remaining = next(item["remainingQuantity"] for item in current_sale["items"] if item["sku"] == sku)
    require(remaining == 0, f"Expected zero remaining capacity, got {remaining}. Holds may have expired during the run.")

    status, rejected = request(args.base_url, "POST", path, body, f"capacity-{run_id}-extra")
    print(f"Extra request #{args.capacity + 1}: HTTP {status}\n{json.dumps(rejected, indent=2)}", flush=True)
    require(status == 409 and rejected.get("title") == "No reservable capacity.",
            "Expected a capacity rejection. If holds expired during this check, rerun with a longer hold duration.")
    print(f"PASS: {args.capacity} distinct holds, zero remaining capacity, and the next request rejected.", flush=True)
    print("\nTry the same sale in Swagger BEFORE the first hold expires:", flush=True)
    print(f"POST {path}\nIdempotency-Key: manual-{run_id}\n{json.dumps(body)}", flush=True)
    print("\nOr copy this exact command while the holds are still active:", flush=True)
    print(shlex.join([
        "curl", "-i", "-X", "POST", args.base_url + path,
        "-H", "Content-Type: application/json",
        "-H", f"Idempotency-Key: manual-{run_id}",
        "--data", json.dumps(body),
    ]), flush=True)
    print("Expected: 409 No reservable capacity. After expiry and worker cleanup, a new key can reserve again.", flush=True)
    print("This simulates independent requests, not authenticated user accounts. The demo sale is retained.", flush=True)


if __name__ == "__main__":
    try:
        run(parse_args())
    except (RuntimeError, KeyError, TypeError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
