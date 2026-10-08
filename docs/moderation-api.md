# Plutonium API / Vercel website handoff

Production base URL: `https://plutonium-moderation.vercel.app/api/v1`.
All responses are JSON with `Cache-Control: no-store`. Errors have `{ "error": "safe message", "requestId": "..." }`.

## Public website appeal

`POST /appeals` from the exact configured website origin:

```json
{
  "username": "MinecraftUsername",
  "explanation": "What happened? Please describe why the restriction should be reviewed.",
  "captcha": "Turnstile response token"
}
```

Visible form fields: **Username**, **What happened?**. Explanation: 10–2000 characters. Username: 3–16 letters/digits/underscores. Render returned messages as text, not HTML. Use Turnstile with `data-action="appeal"`, the site's public site key and a hostname matching `APPEAL_HOSTNAME`. The secret stays on the API server.

HTTP 200: a generic acknowledgment whether or not a matching restricted account exists. One pending appeal per UUID; resubmitting never overwrites it. HTTP 400: invalid form/CAPTCHA. HTTP 429: rate limit; wait before retrying. HTTP 503: service/setup issue; retain the explanation and let the user retry. Never show a success message for a failed request. The API does not send email and does not require opening the launcher to submit.

An appeal alone **does not verify account ownership**. It enters the review queue; accepting it requires an authenticated authorized reviewer. There is no public unban or admin-grant route.

## Launcher/client routes

| Method / path | Purpose | Required authority |
|---|---|---|
| `GET /config` | Maintenance message, disabled features, revision | Public |
| `POST /session` | Exchange `{minecraftToken}` after official Minecraft profile verification | Valid Minecraft session |
| `DELETE /session` | Revoke current service session | Service bearer |
| `GET /me` | Username, UUID, role, restriction, owner candidate | Service bearer |
| `POST /heartbeat` | `{kind:"launcher"|"client",version,installationId?}`; returns allowed/reason/config/120s lease | Service bearer |
| `POST /owner/start` | Begin Microsoft device-code verification | Pinned owner Minecraft UUID |
| `POST /owner/poll` | Poll no faster than returned interval | Same owner session |
| `GET /admin/stats` | Active accounts/clients, installs, weekly activity, restrictions, pending appeals | Owner |
| `GET /admin/users?after=UUID&search=NAME` | Accounts in stable UUID order, 100 per page; optional username search, response `nextCursor` | Admin/owner |
| `POST /admin/restriction` | `{uuid,disabled,reason}` | Admin/owner; protected targets denied |
| `GET /admin/appeals` | Latest 200 appeals, pending first | Admin/owner |
| `POST /admin/review` | `{id,decision:"accepted"|"rejected"}`; accepted restores access atomically | Admin/owner |
| `GET /admin/config` | Configuration and allowed feature IDs | Owner |
| `PUT /admin/config` | `{maintenance,message,disabledFeatures,revision}` | Owner; stale revision → 409 |
| `POST /admin/role` | `{uuid,role:"user"|"admin"}` | Owner |
| `GET /admin/audit` | Latest 100 privileged actions | Owner |

Service authorization: `Authorization: Bearer <opaque session token>`. Never put it in a URL or browser bundle. There are no shared admin API keys. The appeal website's browser origin can call only `/appeals`; it cannot call the administrative routes via CORS. Non-browser clients still require server-verified authorization.

## Metrics definitions / privacy

- Active launcher/account: last service heartbeat within 5 minutes (an account can run more than one launcher, but is counted once).
- Running client: valid client heartbeat within 2 minutes. Disabled accounts are not counted as playing.
- Total accounts: distinct verified Minecraft UUIDs that have connected since service deployment.
- Installs: distinct hashed, randomly generated launcher installation IDs reported by authenticated accounts. Reinstallation/reset can create a new ID; this is not a verified count of people or devices.
- Weekly activity and new installations: latest seven UTC calendar days including today, compared with the preceding seven. Today is partial. When the previous count is zero, display “no previous baseline” rather than an infinite percentage.
- `admin/stats` includes `daily`: 14 ascending UTC dates with `active` and `installs` counts; missing days are zero-filled. `versions` counts recently active accounts by last reported launcher version. `reviews` contains accepted/rejected appeal counts for the latest seven UTC calendar days. None of these counts invent historical activity before deployment.
- No email addresses, passwords, hardware fingerprints, world coordinates, server addresses or chat messages are collected for these metrics. Minecraft access tokens are used transiently to verify profiles, never persisted. Publish this disclosure in your site privacy notice before activation.
