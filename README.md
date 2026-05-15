# Codex License Gateway

This project is a compliant alternative to "sharing a Codex/OpenAI account or API key with customers".

What it does:

- Generates one-time redeemable license codes.
- Assigns plans such as `5x`, `20x`, and `unlimited`.
- Tracks sessions, devices, and backend-enforced rate limits.
- Supports license duration in months and admin disable/re-enable controls.
- Keeps the upstream OpenAI key server-side.
- Lets an admin rotate the upstream key without shipping a new client.
- Proxies OpenAI Responses API calls from your backend.
- Includes a Windows customer app that authenticates against this backend.

What it does **not** do:

- It does not inject your OpenAI key into customer devices.
- It does not log customers into the official Codex dashboard or account.
- It does not attempt to mimic or bypass OpenAI-native subscription or rate-limit systems.

## Quick start

1. Copy `.env.example` to `.env` and set strong secrets.
2. Install dependencies:

```bash
npm install
```

3. Run the server:

```bash
npm start
```

4. Open the admin panel:

```text
http://localhost:3000/admin
```

Default admin credentials come from `.env`.

## Windows customer app

The `launcher/` project is a .NET Windows application. It handles:

- first-run key activation
- local session persistence
- refresh against the backend
- displaying plan, expiry, and remaining limits
- sending prompts through the backend proxy

The backend also exposes:

- `POST /api/client/responses`

That endpoint accepts `model`, `input`, and optional `instructions`, decrements one unit from the active license plan, and forwards the request to OpenAI from the server.

## Rate limits

This project now enforces:

- model-specific 5-hour request caps
- optional weekly caps
- shared 5-hour accounting for `local_messages` and `cloud_tasks`
- editable rate-limit config through the admin panel and `GET/PUT /api/admin/rate-limits`

Default values are conservative derived defaults for your custom `5x` and `20x` plans, based on the publicly visible Plus baseline ranges on the Codex pricing page. You should treat them as your own product policy, not as a guaranteed mirror of OpenAI's private internal counters.

## Render deployment

This repo includes a `render.yaml` blueprint for a Node web service with a persistent disk and `DATA_DIR` configured to keep the SQLite database across restarts and deploys.

If Render source builds are blocked by exhausted pipeline minutes, the repo also includes a `Dockerfile` and a GitHub Actions workflow that publishes `ghcr.io/bankai-max-wow/codex-license-gateway:latest`. Render can deploy a prebuilt image instead of building from source.

Build it with:

```bash
dotnet build launcher/CodexLauncher/CodexLauncher.csproj
```

Publish a Windows EXE with:

```bash
npm run publish:customer
```

The publish output is written to `dist/customer/CodexGateway`.
