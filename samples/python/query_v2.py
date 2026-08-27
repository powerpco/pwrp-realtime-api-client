#!/usr/bin/env python3
"""PowerP Real-Time API — v2 selector query example.

The v1 path asks for explicit measurement indexes, up to 20 per call. The v2 path asks
by what a signal *means* — a set of semantic tags — and the server resolves it to series
and runs the cheapest query. A whole site is one request.

Credentials come from the environment so nothing is committed:

    export POWERP_CLIENT_ID="<your-client-id-guid>"
    export POWERP_CLIENT_SECRET="<your-client-secret>"
    export POWERP_API_BASE_URL="https://<tenant>.powerp.app/rt-api/api/"
    python3 query_v2.py
"""
import os
from datetime import datetime, timedelta, timezone

import requests

BASE = os.environ.get("POWERP_API_BASE_URL", "http://localhost:5000/api/").rstrip("/")
CLIENT_ID = os.environ["POWERP_CLIENT_ID"]
CLIENT_SECRET = os.environ["POWERP_CLIENT_SECRET"]

# The bucket to query and the selector that describes the signals you want. The tag
# dimensions (site, level, signal, ...) are the ones the catalogue exposes for your
# tenant; ask the PowerP team for your bucket's vocabulary. These are placeholders.
DATABASE_ID = 1
SELECTOR = {"site": "SITE01", "level": "inverter", "signal": "active_power"}

session = requests.Session()


def token():
    r = session.post(
        f"{BASE}/v1/auth/token",
        data={
            "client_id": CLIENT_ID,
            "client_secret": CLIENT_SECRET,
            "grant_type": "client_credentials",
        },
    )
    r.raise_for_status()
    return r.json()["access_token"]


def query(headers, selector, start, end, explain=False, resample_every=None):
    body = {
        "databaseId": DATABASE_ID,
        "selector": selector,
        "startTime": start.isoformat().replace("+00:00", "Z"),
        "endTime": end.isoformat().replace("+00:00", "Z"),
        "explain": explain,
    }
    if resample_every:
        body["resampleEvery"] = resample_every
    r = session.post(f"{BASE}/v2/query", json=body, headers=headers)
    r.raise_for_status()
    return r.json()


def main():
    headers = {"Authorization": f"Bearer {token()}"}
    end = datetime.now(timezone.utc)
    start = end - timedelta(hours=1)

    # 1. Size the query first: explain returns the plan without moving any data.
    plan = query(headers, SELECTOR, start, end, explain=True)["query"]
    print(f"plan={plan['plan']} series={plan['seriesRequested']} "
          f"roundtrips={plan['roundtrips']} elapsedMs={plan['elapsedMs']}")

    # 2. Run it. One request covers every series the selector resolved to.
    result = query(headers, SELECTOR, start, end)
    points = result["points"]
    print(f"got {len(points)} points")
    for p in points[:5]:
        print(f"  {p['tag']}  {p['timestamp']}  {p['value']}")


if __name__ == "__main__":
    main()
