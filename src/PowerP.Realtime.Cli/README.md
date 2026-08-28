# powerp — command line

A shell around the same client library the SDK and the MCP server use. Same limits, same
refusals, no second implementation with its own opinions.

```bash
dotnet publish src/PowerP.Realtime.Cli -c Release -r linux-x64
# osx-arm64, win-x64 — a single self-contained binary
```

## Credentials

Read from the environment, or given as options:

```bash
export POWERP_BASE_URL=https://acme.powerp.app/rt-api/api
export POWERP_CLIENT_ID=…
export POWERP_CLIENT_SECRET_FILE=~/.config/powerp/secret   # preferred
```

`--secret` exists but prefer `--secret-file`: an argument is visible in the process list
and in shell history.

## Commands

```bash
powerp buckets                      # what can this credential read?
powerp vocabulary -b 32             # what tags does this bucket have?

# price it first when the selection is broad
powerp explain -b 32 -s level=inverter --last 24h --every 5m

# raw points — the instants values were recorded at
powerp query -b 32 -s level=inverter -s signal=active_power --last 30m

# aggregated, either by naming the window or by asking for a point budget
powerp query -b 32 -s level=inverter --last 7d --every 15m
powerp query -b 32 -s level=inverter --last 7d --points 500

# an exact, reproducible set that re-tagging cannot move
powerp query -b 32 --keys 4115008 4115109 --last 1h

powerp latest -b 32 -s level=inverter        # what is happening now
```

`--last 30m` is the short form; `--from`/`--to` the precise one. Omit both and you get the
last hour.

## Output

`--format table` (default), `json`, or `csv`.

**Table and CSV write only data to stdout**; the context line — how many points, how many
series, and whether they are raw or aggregated — goes to stderr. So this yields a clean
file:

```bash
powerp query -b 32 -s level=inverter --last 6h --every 1m -f csv > inverters.csv
```

and this still tells you what you got:

```bash
powerp query -b 32 -s level=inverter --last 6h -f csv > out.csv
# 2160 points, 6 series, raw — timestamps are the instants the values were recorded at
```

Read that line. Aggregated points sit on window boundaries, so a state change reads as
having happened at the edge of its window rather than when it did.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Fine |
| `1` | The API refused the request; the message names the limit and the remedy |
| `2` | Missing credentials |
| `3` | Anything else |

A refusal is a `1` with the explanation on stderr, so a script can branch on it:

```bash
if ! powerp query -b 32 --last 30d > out.csv; then
  powerp query -b 32 --last 30d --every 1h > out.csv     # the message told you to
fi
```

## Limits

The same ones the API enforces: raw is capped at 3 hours and 500 signals, aggregated
queries by a point budget, and four requests in flight per credential. `powerp explain`
reports what a query would cost before you pay for it.
