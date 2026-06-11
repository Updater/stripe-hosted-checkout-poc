---
name: onboard
description: Verify and set up a local development environment for the checkout-session API. Use when a developer is new to the repo, asks how to get set up, or hits SDK, HTTPS-certificate, secrets, or first-run problems. Checks the .NET 10 SDK, dev certs, user-secrets, and a build/test smoke run.
compatibility: Requires the .NET 10 SDK
---

# Onboard a developer

Walk through the checks below in order, running each command and fixing problems before moving on. Conventions and commands in `AGENTS.md` are the source of truth; `README.md` covers the API and configuration; `docs/HANDOFF.md` covers deployment. Don't duplicate their content — point the developer at them.

## 1. SDK

Run `dotnet --version`. It must report a 10.x SDK. If missing or older, direct the developer to install the .NET 10 SDK before continuing.

## 2. HTTPS dev certificate

The API is HTTPS-only, including local development. Run `dotnet dev-certs https --check --trust`. If the certificate is missing or untrusted, run `dotnet dev-certs https --trust` (may prompt for the OS keychain).

## 3. Secrets

Real keys never go in the repo — `appsettings.json` holds placeholders only. For local development use user-secrets (loaded automatically in Development).

**Secret values must never appear in this conversation.** Do not run any `dotnet user-secrets` command (project settings deny them) and never read the user-secrets store (`~/.microsoft/usersecrets/`). Instead, ask the developer to check and set their secrets in their **own terminal, outside this session**:

```bash
dotnet user-secrets list --project src/Checkout.Api          # check what's set
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/Checkout.Api
dotnet user-secrets set "Api:Key" "some-shared-secret" --project src/Checkout.Api
```

If a key is missing, do not ask the developer for the value — only for confirmation that they have set it. A Stripe **test** key (`sk_test_...`) is enough; see the Configuration table in `README.md` for all settings and their env-var forms (`Stripe__SecretKey`, `Api__Key`, ...).

Secrets are NOT needed for building or testing — only for running the API against Stripe.

## 4. Build and test smoke run

```bash
dotnet build CheckoutPoc.sln
dotnet test CheckoutPoc.sln
```

Both must pass with zero errors. Tests are fully offline (all Stripe touchpoints faked) — failures here indicate a real problem, not missing setup.

## 5. Run the API

```bash
dotnet run --project src/Checkout.Api   # serves https://localhost:7210
```

Verify with `curl -s https://localhost:7210/healthz` — expect `{"status":"ok"}`. The demo front end is at `https://localhost:7210/` (unauthenticated); the checkout endpoint requires the `X-Api-Key` header.

## Wrap-up

Tell the developer which checks passed, what was fixed, and anything left for them to do (e.g. obtaining a Stripe test key). Point them to `AGENTS.md` for the enforced conventions before their first change.
