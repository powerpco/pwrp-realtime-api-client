#!/usr/bin/env python3
"""PowerP Real-Time API — v2 example (Python).

Walks the whole v2 flow: authenticate, discover the selector vocabulary, run a range
query, and poll the latest values. Only `requests` is needed (`pip install requests`).

    export POWERP_API_BASE_URL="https://<tenant>.powerp.app/rt-api/api/"
    export POWERP_CLIENT_ID="<your-client-id>"
    export POWERP_CLIENT_SECRET="<your-client-secret>"
    python3 query_v2.py

Set DATABASE_ID and the SELECTOR to your bucket. Run with no changes and it prints the
vocabulary so you can see what selectors your bucket supports.
"""
import os
import time
from datetime import datetime, timedelta, timezone

import requests

BASE = os.environ.get("POWERP_API_BASE_URL", "http://localhost:5000/api/").rstrip("/")
CLIENT_ID = os.environ["POWERP_CLIENT_ID"]
CLIENT_SECRET = os.environ["POWERP_CLIENT_SECRET"]

DATABASE_ID = int(os.environ.get("POWERP_DATABASE_ID", "1"))
# Placeholder selector — replace with your bucket's vocabulary (printed below).
SELECTOR = {"level": "inverter", "signal": "active_power"}

session = requests.Session()
_token = {"value": None, "exp": 0.0}


def token():
    """Fetch and cache a bearer token; refresh a minute before it expires."""
    if _token["value"] and time.monotonic() < _token["exp"]:
        return _token["value"]
    r = session.post(f"{BASE}/v1/auth/token", data={
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "grant_type": "client_credentials",
    })
    r.raise_for_status()
    body = r.json()
    _token["value"] = body["access_token"]
    _token["exp"] = time.monotonic() + body.get("expires_in", 3600) - 60
    return _token["value"]


def auth_headers():
    return {"Authorization": f"Bearer {token()}"}


def get(path):
    r = session.get(f"{BASE}{path}", headers=auth_headers(), timeout=60)
    r.raise_for_status()
    return r.json()


def post(path, body):
    r = session.post(f"{BASE}{path}", json=body, headers=auth_headers(), timeout=60)
    r.raise_for_status()
    return r.json()


def iso(dt):
    return dt.isoformat().replace("+00:00", "Z")


def main():
    # 1. Discover: what can this bucket be queried by?
    vocab = get(f"/v2/databases/{DATABASE_ID}/vocabulary")
    print(f"bucket '{vocab.get('bucket')}' — {vocab.get('signals')} signals")
    for dim, values in vocab.get("dimensions", {}).items():
        shown = values[:8] + (["..."] if len(values) > 8 else [])
        print(f"  {dim}: {shown}")

    # 2. Size the query first (explain moves no data).
    end = datetime.now(timezone.utc)
    start = end - timedelta(hours=1)
    plan = post("/v2/query", {
        "databaseId": DATABASE_ID, "selector": SELECTOR,
        "startTime": iso(start), "endTime": iso(end), "explain": True,
    })["query"]
    print(f"\nselector {SELECTOR} -> plan={plan['plan']} "
          f"series={plan['seriesRequested']} roundtrips={plan['roundtrips']}")

    # 3. Range query, resampled to 5-minute windows.
    ranged = post("/v2/query", {
        "databaseId": DATABASE_ID, "selector": SELECTOR,
        "startTime": iso(start), "endTime": iso(end), "resampleEvery": "5m",
    })
    q = ranged["query"]
    print(f"range: {len(ranged['points'])} points "
          f"(aggregated={q['aggregated']} every={q['resampleEvery']} via {q['windowSource']})")

    # 4. Raw points: no window at all, so every timestamp is the instant the value was
    #    recorded. This is what you want for states, alarms and setpoints, where a value
    #    moved to a window boundary is reported earlier than it happened.
    #    Raw is bounded: too wide a range or too many signals is refused with 400 rather
    #    than quietly aggregated, so keep the window narrow.
    raw = post("/v2/query", {
        "databaseId": DATABASE_ID, "selector": SELECTOR,
        "startTime": iso(end - timedelta(minutes=5)), "endTime": iso(end),
    })
    print(f"raw: {len(raw['points'])} points (aggregated={raw['query']['aggregated']})")

    # 5. Let the server size the window: ask for a point budget instead of guessing.
    budgeted = post("/v2/query", {
        "databaseId": DATABASE_ID, "selector": SELECTOR,
        "startTime": iso(start), "endTime": iso(end), "maxDataPoints": 200,
    })
    print(f"budgeted: {len(budgeted['points'])} points "
          f"at every={budgeted['query']['resampleEvery']}")

    # 6. Pin an exact set by key. Prefer this over a selector for a scheduled ingest:
    #    a selector follows the catalogue, so re-tagging a signal changes what you
    #    collect without anything failing. Keys are reproducible and diffable.
    #    Keys the catalogue does not have come back in query.unresolvedStreamKeys.
    keys = sorted({p["streamKey"] for p in ranged["points"]})[:3]
    if keys:
        pinned = post("/v2/query", {
            "databaseId": DATABASE_ID, "streamKeys": keys,
            "startTime": iso(end - timedelta(minutes=5)), "endTime": iso(end),
        })
        missing = pinned["query"].get("unresolvedStreamKeys")
        print(f"pinned {keys}: {len(pinned['points'])} points, unresolved={missing}")

    # 7. Latest value per series — the polling pattern, one call.
    latest = post("/v2/query/latest", {"databaseId": DATABASE_ID, "selector": SELECTOR})
    print(f"latest: {len(latest['points'])} series")
    for p in latest["points"][:8]:
        print(f"  {p['tag']}  {p['value']}  @ {p['timestamp']}")


if __name__ == "__main__":
    main()
