---
name: run
description: Start the API locally and smoke-check it. Use when asked to run the app, try an endpoint, or confirm a change works in the running service.
compatibility: Requires the .NET 10 SDK, a trusted HTTPS dev cert, and user-secrets configured (run /onboard if unsure)
---

# Run the API

Start the server in the background (it serves `https://localhost:7210`, HTTPS-only):

```bash
dotnet run --project src/Checkout.Api
```

Then verify it is up:

```bash
curl -s https://localhost:7210/healthz   # expect {"status":"ok"}
```

- The demo front end is at `https://localhost:7210/` (unauthenticated).
- The checkout endpoint requires the `X-Api-Key` header. **Never print the developer's secrets** — do not run `dotnet user-secrets` commands (denied by project settings) or read `~/.microsoft/usersecrets/`; if a request needs the key, ask the developer to supply or invoke it themselves.
- Talking to Stripe requires a configured test key; if checkout calls fail with a gateway error, point the developer at `/onboard` step 3 rather than debugging Stripe.
- Stop the background server when finished unless the developer asked to keep it running.
