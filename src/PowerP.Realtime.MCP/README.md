# PowerP Real-Time MCP server

Connects the PowerP Real-Time API to an AI client — Claude Desktop, Claude Code, or
anything else that speaks the [Model Context Protocol](https://modelcontextprotocol.io) —
so a model can read a plant's historian through four typed operations instead of composing
HTTP by hand.

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

- It is a **secret**. Put it in your AI client's configuration, not in a repository.
- Prefer a **dedicated credential** for AI use, so you can rate-limit it separately and
  revoke it without disturbing your production integration.
- Prefer a **read-only** credential. This server never needs more.
- Rotating is a call to us; tokens already issued stay valid until they expire (one hour).

The credential is read from the environment and never appears in a tool result, a log line,
or anything returned to the model.

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
| `POWERP_CLIENT_SECRET` | your secret |

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

The server refuses to start if any of the three is missing, rather than starting and
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

1. **`powerp_vocabulary` first.** Selectors are built from a bucket's tag dimensions, and a
   guessed dimension returns nothing rather than an error.
2. **`powerp_explain` before anything broad.** It reports how many signals resolve and how
   many points the query would move, and reads nothing.
3. **`powerp_query` with the window you mean.** No window gives raw points at their real
   instants — right for state changes, alarms and setpoints, where an aggregated timestamp
   sits on a window boundary and so reads as earlier than it happened. Give
   `resampleEvery` or `maxDataPoints` for anything long.
4. **Read `aggregated` in the result** before treating a timestamp as an instant, rather
   than assuming from what was asked.

When a call is refused, the error names the limit, the numbers and the remedy. It is meant
to be acted on, not retried unchanged.
