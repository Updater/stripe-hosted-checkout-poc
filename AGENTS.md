# Checkout Session API — project guide

ASP.NET Core (.NET 10) service that creates Stripe Hosted Checkout sessions. Solution: `CheckoutPoc.sln`.

## Commands

- Build: `dotnet build CheckoutPoc.sln`
- Test: `dotnet test CheckoutPoc.sln` (xUnit, `tests/Checkout.Tests`, no network or Stripe key needed)
- Run: `dotnet run --project src/Checkout.Api` (serves `https://localhost:7210`)

## Conventions (enforced — do not weaken)

- **Clean architecture, dependencies point inward**: `Checkout.Domain` (no deps) ← `Checkout.Application` (use cases + `ICheckoutGateway` port) ← `Checkout.Infrastructure.Stripe` (adapter) ← `Checkout.Api` (composition root). The core never references Stripe.
- **Validation lives in the domain** as invariants throwing `DomainValidationException`; the API layer only maps exceptions (400 domain, 502 gateway).
- **HTTPS-only**, including development; redirect URLs must be absolute `https://`.
- **No secrets in the repo**: `appsettings.json` holds placeholders only; real keys come from env vars.
- **Generic naming**: no partner or company names in code, comments, or docs.
- Every behavior change ships with tests (`FakeCheckoutGateway` pattern) and README updates when the API surface or configuration changes.

## Skills

Skills live in `.claude/skills/`:

- `/onboard` — verifies a local dev setup: .NET 10 SDK, trusted HTTPS dev cert, user-secrets, and an offline build/test smoke run. Run it when setting up the project for the first time.
- `/build` — builds the solution; zero errors and zero warnings expected.
- `/test` — runs the offline xUnit suite; failures are real problems, never missing setup.
- `/run` — starts the API on `https://localhost:7210` and smoke-checks `/healthz`.

