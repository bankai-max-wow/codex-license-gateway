# Analysis

The original request mixes two different models:

1. A legitimate customer application built on the OpenAI API.
2. Shared access to your personal or business Codex/OpenAI account as if each customer had a native Codex subscription.

Only the first model is workable here.

## Why the original design should be changed

- OpenAI API keys are not meant to be shared with end users.
- API keys should not be shipped in desktop clients.
- Account credentials should not be made available to other people.
- The right way to serve end users is to keep the OpenAI key on your backend and expose your own application with your own limits.

## Resulting architecture

- Backend service
  - stores encrypted upstream keys
  - issues one-time redeemable license codes
  - binds redeemed licenses to customer identities and optional devices
  - meters usage by plan
  - exposes admin APIs and dashboard
- Windows launcher
  - redeems a license code once
  - keeps only your application session token locally
  - fetches current plan and quota from your backend
  - uses refresh to get updated backend configuration
- OpenAI proxy route
  - receives customer prompts on your backend
  - applies your quota logic
  - calls OpenAI Responses API using the encrypted server-side key

## Plan semantics in this starter

- `5x` -> backend-enforced 5-hour and weekly caps derived from the public Plus floor values multiplied by 5
- `20x` -> backend-enforced 5-hour and weekly caps derived from the public Plus floor values multiplied by 20
- `unlimited` -> bypasses the local limiter

You can change the policy at runtime from the admin panel or by updating the JSON returned by `GET /api/admin/rate-limits`.
