# PowerP Real-Time MCP server

Connects the PowerP Real-Time API to an AI client — Claude Desktop, Claude Code, or
anything else that speaks the [Model Context Protocol](https://modelcontextprotocol.io) —
so a model can read a plant's historian through four typed operations instead of composing
HTTP by hand.

- `powerp_buckets` — the buckets your credential can read, with their ids
- `powerp_vocabulary` — the tag dimensions of a bucket and the values each takes
- `powerp_explain` — price a query without running it
- `powerp_query` — read measurements over a range, raw or aggregated
- `powerp_latest` — the current value of every signal a selection resolves to

---

## Security

The short version: **the server runs on your machine, holds your own tenant credential,
can only read, and cannot reach anything that credential cannot already reach.**

### Where it runs

It is a **stdio** server: your AI client launches it as a child process and talks to it
over standard input and output. Nothing listens on a port; nothing new is exposed to the
network; we host no additional service. The only outbound traffic is HTTPS to your own
PowerP host, exactly as your own code would make it.

### What it can do

The server has **no privileges of its own**. It authenticates with the tenant credential
you configure and is bounded by everything that credential is bounded by:

| Control | Effect |
|---|---|
| Tenant scope | Only your tenant's buckets resolve. Another tenant's data is a `403`, not a filtered result. |
| Host binding | The credential works only against your own hostname. Presented anywhere else it is rejected. |
| Rate limit | Your credential's requests-per-second, unchanged. |
| Concurrency | Four requests in flight per credential; a fifth is a `429`. |
| Size limits | Raw is capped at 3 h and 500 signals; aggregated queries are capped by a point budget. |

**Every tool is read-only.** There is no tool that writes, provisions, rotates a secret or
deletes anything — not because the model is asked not to, but because no such operation is
exposed. Even configured with an operator credential, this server can only read.

### What this means for prompt injection

If a model is manipulated — by a document it reads, a comment in a dataset, anything — the
worst it can do through this server is *read your own data*, which it was given access to
anyway. It cannot reach another tenant, cannot change a configuration, and cannot exfiltrate
a credential it never sees in a tool result. That bound is enforced by the API, not by the
tool descriptions, so it holds whether or not the model cooperates.

The limits work the same way. A tool description tells the model that raw is capped at
three hours; if the model ignores it, the server refuses the call regardless. The
description is there to save a round trip, not to be the control.

### Your credential

You need a **tenant client id and secret** — the same pair you would use from your own
code. Ask us for one, or reuse what you have.

- It is a **secret**. Prefer `POWERP_CLIENT_SECRET_FILE`, pointing at a file the operating
  system protects, over putting the value in your AI client's configuration: that file sits
  unencrypted on disk, is often synced between machines, and is occasionally committed by
  accident.
- Prefer a **dedicated credential** for AI use, so you can rate-limit it separately and
  revoke it without disturbing your production integration.
- Prefer a **read-only** credential. This server never needs more.
- Rotating is a call to us; tokens already issued stay valid until they expire (one hour).

The credential is read from the environment and never appears in a tool result, a log line,
or anything returned to the model.

### Sessions and tokens

The server exchanges your credential for a bearer token, refreshes it two minutes before it
expires, and mints a fresh one if a call is ever rejected as unauthorised. It is meant to be
left running for days; nothing needs restarting when a token ages out.

Every tool declares itself read-only and idempotent in its annotations, and publishes an
output schema, so a client can show you what a tool does before it runs and validate what
comes back.

### Human in the loop

The MCP specification asks clients to let a person see and approve tool calls. Claude
Desktop and Claude Code both do. Keep that on: it is what turns "the model can read your
plant" into "the model can read your plant while you watch".

---

## Configuration

Three environment variables:

| Variable | Example |
|---|---|
| `POWERP_BASE_URL` | `https://acme.powerp.app/rt-api/api` |
| `POWERP_CLIENT_ID` | `f2dc2c97-…` |
| `POWERP_CLIENT_SECRET` | your secret — or `POWERP_CLIENT_SECRET_FILE` pointing at a file holding it |

### Claude Desktop / Claude Code

```json
{
  "mcpServers": {
    "powerp": {
      "command": "/path/to/powerp-mcp",
      "env": {
        "POWERP_BASE_URL": "https://acme.powerp.app/rt-api/api",
        "POWERP_CLIENT_ID": "…",
        "POWERP_CLIENT_SECRET": "…"
      }
    }
  }
}
```

With a secret file instead:

```json
"env": {
  "POWERP_BASE_URL": "https://acme.powerp.app/rt-api/api",
  "POWERP_CLIENT_ID": "…",
  "POWERP_CLIENT_SECRET_FILE": "/home/you/.config/powerp/secret"
}
```

```bash
install -m 600 /dev/null ~/.config/powerp/secret
printf '%s' 'your-secret' > ~/.config/powerp/secret
```

The server refuses to start if the base URL, the client id or a secret is missing, rather than starting and
failing inside the first tool call — where a model reads the failure as the plant being
unreachable instead of the server being misconfigured.

---

## Build

```bash
dotnet publish src/PowerP.Realtime.MCP -c Release -r linux-x64
# or osx-arm64, win-x64 — a single self-contained binary, no runtime to install
```

---

## How a model should use it

The tool descriptions carry this, but for a reader:

1. **`powerp_buckets` first** if you do not know a bucket id — every other tool needs one
   and there is no way to guess it.
2. **`powerp_vocabulary` next.** Selectors are built from a bucket's tag dimensions, and a
   guessed dimension returns nothing rather than an error.
3. **`powerp_explain` before anything broad.** It reports how many signals resolve and how
   many points the query would move, and reads nothing.
4. **`powerp_query` with the window you mean.** No window gives raw points at their real
   instants — right for state changes, alarms and setpoints, where an aggregated timestamp
   sits on a window boundary and so reads as earlier than it happened. Give
   `resampleEvery` or `maxDataPoints` for anything long.
5. **Read `aggregated` in the result** before treating a timestamp as an instant, rather
   than assuming from what was asked.

When a call is refused, the error names the limit, the numbers and the remedy. It is meant
to be acted on, not retried unchanged.
