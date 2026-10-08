# Plutonium moderation: deployment and owner setup

## Status

The production API is deployed at **https://plutonium-moderation.vercel.app/api/v1** with a dedicated Neon PostgreSQL database. `src/main/resources/plutonium-service.json` now embeds that endpoint for newly built launchers and clients. Previously downloaded builds are unchanged. The appeal page is live at **https://plutoniumclient.vercel.app/appeal** with production Turnstile keys. Production configuration, unsigned-admin rejection, invalid Minecraft tokens and invalid CAPTCHA rejection have been checked. The owner confirmed successful human CAPTCHA submission on 2026-10-08. This generic acknowledgment does not create an inbox item for an unrestricted account. Complete the remaining official-client acceptance checks below before publishing an enabled release.

The API uses the separate Vercel project **ymca22/plutonium-moderation**. The existing website remains in **ymca22/plutonium**; its source is not in this repository. The existing Microsoft identity helper successfully verified the intended owner account on 2026-10-08. Its verified object ID, registration ID, Minecraft UUID and appeal origin/hostname are configured in the API project's Production environment. No authentication implementation was replaced or modified during provisioning.

## Security model and limits

- The sole owner is the Microsoft account **justquirk.business@gmail.com**. Its **immutable Microsoft object ID** and **Minecraft UUID** must be verified and pinned in server environment variables. An email string, gamertag, local file, hidden button or request-supplied role never grants authority.
- Microsoft says email/preferred_username are mutable and must not be used for authorization: [ID token claims](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference). The configured object ID is the authority after signature, issuer, audience, expiry and tenant verification. An email alias change does not transfer ownership to whoever later obtains that address.
- Owner verification uses Microsoft device-code sign-in; its ID token never comes from a launcher-supplied email. Owner privileges expire in 15 minutes. Minecraft sessions are verified against the official Minecraft profile API, then exchanged for opaque 30-minute service sessions. Only their SHA-256 hashes are stored.
- Owner alone can enable maintenance/change configuration, grant/revoke admins and read the audit log. Admins and owners can stop maintenance through a narrow audited recovery endpoint. Admins may restrict normal users and review appeals; they cannot restrict the owner, themselves or another admin. Disabling an admin removes its authority on the next request. Revocation is checked against the database, not a cached role claim.
- Username and an explanation are enough to submit an appeal with CAPTCHA. A submission does not prove ownership and never automatically restores access. An admin reviews it and may request further evidence outside this system before accepting.
- Configuration is a typed allow-list of switches. There is no remote shell, arbitrary download URL, executable script or master override key.
- All privileged writes are transactional and audited. Session tokens, Minecraft tokens, Microsoft device codes, passwords and database credentials must never be logged.
- An owner of their PC can patch a client binary or run an older build. **Unbypassable downloaded software is not possible.** These controls protect the hosted API and enforce policy in official enabled builds. They do not ban a Microsoft/Minecraft account from Minecraft itself, remove files, revoke a game purchase or govern unrelated clients.

## 1. Microsoft registration (one time)

1. Sign in to the Microsoft Entra admin center with an account allowed to create app registrations. Register **Plutonium Moderation**, supporting **personal Microsoft accounts** (or organizational and personal accounts). You may need an Entra tenant to own the registration even though the account being authenticated is personal.
2. Under Authentication → Advanced settings, enable **Allow public client flows** for device-code login. Use delegated OpenID scopes `openid profile email`. This design requires **no client secret**, no mail permissions, no tenant administrator permissions in the launcher and no client-credentials flow.
3. Copy Application (client) ID into `MICROSOFT_CLIENT_ID` on the API project, never a website textbox.
4. From `moderation/`, set the public `MICROSOFT_CLIENT_ID` environment variable and run `node scripts/identify-owner.js`. Complete Microsoft sign-in specifically as **justquirk.business@gmail.com**. This local helper validates Microsoft's signature, consumer issuer, audience and expiry, then prints the public immutable `oid` (never the token). Confirm the intended account and set that value as `OWNER_MICROSOFT_OID` on the backend. Do not paste a live token into a website, a chat or a public JWT decoder. Do not use a tenant object for a different organizational account that merely has a similar email alias.
5. Set `OWNER_MINECRAFT_UUID` to the 32-character lowercase Minecraft UUID of the Minecraft profile owned by that same Microsoft account. Verify it using the authenticated Minecraft profile. This prevents elevating a different selected launcher account with an owner Microsoft login.
6. Protect the owner's Microsoft account with MFA and secure recovery methods. The launcher cannot enforce the personal Microsoft account's MFA policy itself.

There is deliberately no “first user becomes owner” endpoint, default administrator, development master token or email-only fallback. If either immutable ID is missing, owner sign-in fails closed. Changing owner identity requires access to the protected server deployment configuration.

Reference: [Microsoft device-code flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).

### Running the existing identity helper on Windows

After registering the app, open PowerShell and run:

```powershell
Set-Location 'C:\Users\papro\Plutonium\moderation'
npm ci
$env:MICROSOFT_CLIENT_ID = 'YOUR-APPLICATION-CLIENT-ID'
node scripts/identify-owner.js
```

For this personal-account registration, open **https://www.microsoft.com/link**, enter the helper's current one-time code and sign in as the intended personal account. The generic `microsoft.com/devicelogin` URL printed by the existing helper rejected the consumer code during setup; the personal-account link accepted it. Use a private browser window and choose another account if a different Microsoft account is selected. Keep the helper running until it prints `OWNER_MICROSOFT_OID`, and check the verified account label before configuring ownership. Only a successful helper run establishes the verified owner ID. Do not substitute an ID copied from an unverified token.

For a personal Microsoft account, this existing helper verifies the following values in the signed token:

- Tenant (`tid`): `9188040d-6c67-4c5b-b112-36a304b66dad`
- Issuer (`iss`): `https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0`

The **Directory (tenant) ID** shown on the app registration identifies the directory hosting the application; it is not the personal-account consumer tenant above. This implementation already pins the consumer tenant and issuer in `moderation/lib/security.js`; do not add a different tenant environment variable or replace the authentication code.

If the helper cannot start, confirm that the application supports personal accounts and that public-client flows are enabled. If sign-in expires, run the same helper again for a fresh code. Do not create a client secret to work around a device-code configuration error.

## 2. API and database

Deploy the **moderation/** folder as its own Vercel project with Node.js 22 or newer. Use the standard [Node.js Vercel Functions runtime](https://vercel.com/docs/functions/runtimes/node-js). Do not redeploy this folder over the existing website.

Create a PostgreSQL database with a TLS connection URL (for example a Vercel Marketplace PostgreSQL provider). Keep database credentials only on the backend. Use a dedicated database role, restrict external database access and enable backups.

Set these production environment variables from `moderation/.env.example`:

| Variable | Value |
|---|---|
| `DATABASE_URL` | Provider's TLS PostgreSQL connection string |
| `MICROSOFT_CLIENT_ID` | Public registration ID |
| `OWNER_MICROSOFT_OID` | Verified personal Microsoft immutable object ID |
| `OWNER_MINECRAFT_UUID` | Verified Minecraft UUID, no hyphens |
| `TURNSTILE_SECRET_KEY` | Cloudflare Turnstile secret for the appeal site |
| `APPEAL_ORIGIN` | `https://plutoniumclient.vercel.app` |
| `APPEAL_HOSTNAME` | `plutoniumclient.vercel.app` |

Put these values in the **moderation API project's Production environment** in Vercel. Do not prefix secrets with `NEXT_PUBLIC_` or `VITE_`, put them in Git, or paste them into chat. Environment-variable changes require a new deployment before they affect running Functions. The website gets only the API base URL and the **public Turnstile site key**; its matching secret belongs exclusively to the API. Use a separate database and separate owner setup for preview deployments, or leave previews unconfigured.

From `moderation/`, run `npm ci`, `npm test`, then `npm run migrate` with `DATABASE_URL` set securely. The migration creates tables and an inactive maintenance policy; it does not grant an owner or admin. Deploy the project and retain its HTTPS URL. The API uses PostgreSQL for persistent sessions, account state, appeals, limits, configuration and audit records; Vercel's local filesystem is not used for storage.

Configure database retention according to the published privacy policy. `activity_days` can be pruned after 35 days without affecting week-over-week metrics; expired sessions and rate-limit entries are removed on sign-in. Keep restricted-account records while restrictions/appeals are active. Audit data should be retained according to your moderation policy, and never exported publicly.

## 3. Activate official builds

Set the single bundled source file `src/main/resources/plutonium-service.json`:

```json
{"baseUrl":"https://YOUR-API-PROJECT.vercel.app/api/v1"}
```

Both the Java client and the Windows launcher embed this file at build time. It is not a launcher preference, command-line override or runtime environment flag. Build and test both profiles, then publish **only after** testing the live owner identity and API. A configured service failure blocks new launches; it does not silently permit them. Updated official clients recheck every 3 seconds, with at most a 120-second successful-response grace period. If the lease expires, modules are disabled and an access screen appears. Maintenance covers everyone, including owners/admins: launchers show MAINTENANCE MODE and its reason, and running clients show a blocking screen and disable modules. This does not terminate the game process. Updates normally arrive within approximately three seconds plus network latency; disconnected or older builds cannot be updated instantly. Disabled users retain launcher access to accounts, files and updates.

In the launcher, go to **Settings → Moderation & service status**. Connect the selected Minecraft account. The configured owner then selects **Verify owner with Microsoft** and completes the displayed device-code flow with the owner Microsoft account. All permissions still come from the API, regardless of what the UI displays.

For this personal account, manually enter that current code at **https://www.microsoft.com/link** if the launcher's generic device-login page rejects it. The existing authentication implementation remains unchanged.

During maintenance, **Open admin menu** remains available. Only a verified admin, owner, or pinned owner candidate can enter recovery; owner candidates must complete the existing Microsoft verification before changing anything. Owners and admins can select **Stop maintenance**. Navigation to normal launcher pages remains blocked until maintenance ends. In-game admins can open the maintenance admin menu and stop it; the owner uses the verified launcher session. There is no unauthenticated bypass button.

## 4. Website appeals handoff

See [the integration guide](moderation-api.md). The reusable `moderation/website/appeal-example.html` template needs only the public API URL and Turnstile site key; no admin token or API secret belongs in the webpage.

The current deployment serves `moderation/public/appeal.html` through a Vercel project routing rule named **Plutonium appeals** on **ymca22/plutonium**: exact path `/appeal` rewrites to `https://plutonium-moderation.vercel.app/appeal.html`. This preserves the website's other pages without needing its source. The rule is managed in Vercel and is not stored in the website's source. The form contains only the public site key; `TURNSTILE_SECRET_KEY` is stored in the API's Production environment. When migrating this form into the website source, remove the project routing rule after verifying the replacement.

### Turnstile setup and rotation

In the Cloudflare dashboard, open Turnstile and create a **Managed** widget for hostname `plutoniumclient.vercel.app`. Put the public site key in `moderation/public/appeal.html`. Put the matching secret into **ymca22/plutonium-moderation → Settings → Environment Variables → Production**, named `TURNSTILE_SECRET_KEY`. Redeploy the moderation project after updating its environment. Never put the secret in Git or a public page. See [Cloudflare's setup guide](https://developers.cloudflare.com/turnstile/get-started/).

## Required production checks

1. Normal Microsoft/Minecraft account can connect; a fabricated profile/token cannot.
2. A different Microsoft account using the owner's email text cannot elevate. Correct UUID with incorrect Microsoft object ID is denied. Incorrect UUID with owner Microsoft login is denied.
3. Owner elevation succeeds for the intended account and expires. A normal user receives HTTP 403 on every admin route; an admin receives 403 on owner routes.
4. Maintenance appears in another launcher within approximately 3 seconds plus network latency. Owner/admin can sign in and turn it off without redeploying.
5. A restricted account cannot launch the official client. A running official client receives its restriction; API outage fails closed after the lease expires. Launcher accounts/files remain available.
6. Public appeal form submits with valid CAPTCHA; invalid/missing CAPTCHA fails. Accepting an appeal restores the intended UUID atomically; reviewing the same appeal twice fails.
7. Admin revocation takes effect for already signed-in sessions. All mutations appear in the owner audit log.
8. Verify statistics against two real launcher/client sessions. Counts include only clients reporting to this service; no historical pre-deployment installs are invented.
9. Review hosted function logs and confirm no credentials/request bodies appear. Verify database backups and recovery before distributing enforced builds.

Local tests exercise identity validation and authorization with an in-memory PostgreSQL-compatible adapter. They do not substitute for real PostgreSQL transaction/concurrency tests or live Microsoft/Vercel acceptance tests.
