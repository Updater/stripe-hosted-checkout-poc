# Stripe Hosted Checkout Session API

A minimal ASP.NET Core service that creates [Stripe Hosted Checkout](https://docs.stripe.com/checkout/quickstart) sessions. The merchant hosts this service with their Stripe secret key; a partner application calls the API with a customer and an offer, and redirects the end-user to the returned checkout URL.

> **Hosting this service?** See [`docs/HANDOFF.md`](docs/HANDOFF.md) for a deployment-focused guide written for the party that runs it with their own Stripe key (they are the merchant of record).

## How it works

1. The partner application calls `POST /api/checkout-sessions` with the customer's details and the offer (either inline pricing, or a pre-configured Stripe Price ID).
2. The service finds-or-creates the Stripe Customer (by email) and creates a Checkout Session using the merchant's secret key.
3. The partner redirects the end-user to the returned `checkoutUrl` — a Stripe-hosted payment page.
4. After the customer pays (or cancels), Stripe redirects them back to the `successUrl` / `cancelUrl` supplied in the request, returning them to the partner's app.

Offers can be sent **inline** (product name, amount, billing interval) so promotions and A/B price tests require zero configuration on the merchant's side, or referenced by **Stripe Price ID** if the merchant prefers to control pricing in their Stripe Dashboard.

## Architecture

The solution follows clean/onion architecture — dependencies point inward, and the core never references Stripe directly:

```
src/
  Checkout.Domain                 Core model: CheckoutOrder, Customer, Pricing
                                  (ProviderPrice | AdHocOffer). No dependencies.
  Checkout.Application            Use case (CreateCheckoutSession) and the outbound
                                  port (ICheckoutGateway). Depends only on Domain.
  Checkout.Infrastructure.Stripe  Stripe adapter implementing ICheckoutGateway
                                  with Stripe.net. Swappable for another provider.
  Checkout.Api                    ASP.NET Core host: endpoint, API-key filter,
                                  configuration, DI composition root.
```

Input validation lives in the domain (`Customer.Create`, `Pricing.FromOffer`, …) as enforced invariants; the API layer only translates `DomainValidationException` to `400` and `CheckoutGatewayException` to `502`.

## Configuration

Set via `appsettings.json`, environment variables, or any standard ASP.NET Core config source:

| Setting | Env var | Description |
|---|---|---|
| `Stripe:SecretKey` | `Stripe__SecretKey` | The merchant's Stripe secret key (`sk_live_...` / `sk_test_...`) |
| `Api:Key` | `Api__Key` | Shared secret the partner sends in the `X-Api-Key` header |
| `Stripe:DefaultSuccessUrl` | `Stripe__DefaultSuccessUrl` | Default redirect after successful payment when the request doesn't specify one (`{CHECKOUT_SESSION_ID}` is substituted by Stripe; must be `https://`) |
| `Stripe:DefaultCancelUrl` | `Stripe__DefaultCancelUrl` | Default redirect if the user abandons checkout and the request doesn't specify one (must be `https://`) |

### Where the keys come from

- **`Stripe:SecretKey`** comes from the Stripe Dashboard: [Developers → API keys](https://dashboard.stripe.com/apikeys). Use the **secret** key (`sk_test_…` with the test-mode toggle on, `sk_live_…` for real charges) — not the publishable `pk_…` key, which cannot create sessions. For production, prefer a [restricted key](https://docs.stripe.com/keys#limit-access) granting only what this service uses: **Customers (write)** and **Checkout Sessions (write)**.
- **`Api:Key`** is not a Stripe value — it's a shared secret you invent. Generate a long random string (e.g. `openssl rand -hex 32`), configure it here, and give it to the calling application over a secure channel; callers send it in the `X-Api-Key` header.

For local development, store the secrets with [user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) — they live under your user profile, never in the repo or your shell environment, and are loaded automatically when the app runs in Development:

```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/Checkout.Api
dotnet user-secrets set "Api:Key" "some-shared-secret" --project src/Checkout.Api
```

In production, supply the secret key and API key via a secrets manager or environment variables — do not commit real keys.

## Run

The API is HTTPS-only, including local development — plain-HTTP requests get a 307 redirect, per-request `successUrl`/`cancelUrl` must be `https://`, and the configured defaults are validated at startup. Trust the local dev certificate once, then run:

```bash
dotnet dev-certs https --trust
dotnet run --project src/Checkout.Api   # serves https://localhost:7210
```

In Development, each request is logged (method, path, status code, duration —
never headers or bodies, so the API key stays out of the logs); other
environments stay quiet unless `Microsoft.AspNetCore.HttpLogging` is raised to
`Information` in their logging config.

### Demo front end

In Development only, `https://localhost:7210/` serves a small demo page
(`src/Checkout.Api/wwwroot/`) that acts as a sample consumer of the API: pick a
service plan and hardware option (a Starlink-style order — monthly plan plus
one-time kit, shipping, and tax, sent as a `lineItems` bundle), and it redirects
you to the provider's hosted payment page (test card: `4242 4242 4242 4242`).
The tax line mimics Starlink's address-based tax step: only the one-time goods
are taxed (internet service is exempt), so the demo applies a flat 7% to
hardware plus shipping.
Success and cancel redirects land on `/success.html` and `/cancel.html`.

The page never handles the API key: it posts to `POST /demo/checkout-sessions`,
a Development-only twin of the real endpoint that stands in for the partner
backend (which is where the key lives in a real integration). Outside
Development neither the demo pages nor the demo endpoint exist.

A step-by-step walkthrough with screenshots is in [docs/DEMO.md](docs/DEMO.md).

Run the tests (domain invariants and the use case against a fake gateway — no Stripe key needed):

```bash
dotnet test
```

## API

### `POST /api/checkout-sessions`

Headers: `X-Api-Key: <shared secret>`, `Content-Type: application/json`

```json
{
  "customer": {
    "email": "jane@example.com",
    "name": "Jane Doe",
    "phone": "+15555550123",
    "externalId": "partner-user-12345"
  },
  "offer": {
    "name": "Residential Internet",
    "description": "Unlimited high-speed internet",
    "amountCents": 12000,
    "currency": "usd",
    "interval": "month"
  },
  "quantity": 1,
  "mode": "subscription",
  "successUrl": "https://partner.example.com/checkout/success?session_id={CHECKOUT_SESSION_ID}",
  "cancelUrl": "https://partner.example.com/checkout/cancel",
  "metadata": { "campaign": "spring-promo" }
}
```

- `customer.email` is required, plus exactly one of `offer` (inline pricing, shown above), `priceId` (a pre-configured Stripe Price, e.g. `"priceId": "price_1ABC..."`), or `lineItems` (a multi-item bundle, below).
- Within `offer`, only `name` and `amountCents` are required; `currency` defaults to `usd`, `interval` to `month`. `intervalCount` supports e.g. quarterly billing (`3` + `month`). `interval` applies to subscriptions only.
- `mode` defaults to `subscription`; use `payment` for one-time charges. In `payment` mode offers default to one-time, and recurring fields (`interval`, `intervalCount`, `"recurring": true`) are rejected.
- `successUrl` / `cancelUrl` send the customer back to the partner's app after checkout; if omitted, the configured defaults are used.
- `customer.externalId` is stored on the Stripe Customer (`metadata.external_id`) and on the session (`client_reference_id`) so completed checkouts can be correlated back to the partner's user.
- **Tax** — amounts are tax-exclusive; there are two ways to collect tax:
  - *Caller-computed (recommended when the caller already knows the tax)*: send it as one more one-time line, e.g. `{ "offer": { "name": "Tax", "amountCents": 1533, "recurring": false } }`. This fits flows where tax is computed upstream from the service address before checkout (as Starlink's `/sign-up/tax` step does) and only one-time goods are taxed — US internet *service* is tax-exempt, so there is no recurring tax to track. It appears on the Stripe page as a line named "Tax" rather than in Stripe's native tax field.
  - *Stripe-computed*: `"automaticTax": true` has Stripe compute and collect tax at checkout from the address the customer enters there — including on subscription renewals, so use this if the recurring service itself is ever taxable. Requires [Stripe Tax](https://docs.stripe.com/tax) to be enabled on the merchant account — without it Stripe rejects the session (returned as `502`). Defaults to `false`.

#### Bundle orders (`lineItems`)

For orders with multiple items — e.g. a recurring service plus a recurring hardware rental plus one-time shipping — send `lineItems` instead of a single `offer`/`priceId`. Each item takes exactly one of `offer` or `priceId`, plus an optional `quantity`. An offer with `"recurring": false` is charged once on the first invoice:

```json
{
  "customer": { "email": "jane@example.com", "externalId": "partner-user-12345" },
  "lineItems": [
    { "offer": { "name": "Residential 100 Mbps", "amountCents": 5500, "interval": "month" } },
    { "offer": { "name": "Hardware Rental", "amountCents": 1000, "interval": "month" } },
    { "offer": { "name": "Shipping & Handling", "amountCents": 2000, "recurring": false } }
  ]
}
```

In `subscription` mode at least one line must be recurring; `interval` and `intervalCount` are rejected on non-recurring offers.

Response `200`:

```json
{
  "sessionId": "cs_test_...",
  "checkoutUrl": "https://checkout.stripe.com/c/pay/cs_test_...",
  "customerId": "cus_...",
  "expiresAt": "2026-06-10T16:00:00Z"
}
```

Errors: `401` bad/missing API key, `400` missing required fields, `502` with `{ "error": "..." }` when Stripe rejects the request.

### `GET /healthz`

Returns `{ "status": "ok" }` for load-balancer health checks.

## Example

```bash
curl -s -X POST https://localhost:7210/api/checkout-sessions \
  -H "X-Api-Key: REPLACE_WITH_SHARED_SECRET" \
  -H "Content-Type: application/json" \
  -d '{
    "customer": { "email": "jane@example.com", "name": "Jane Doe", "externalId": "u-12345" },
    "offer": { "name": "Residential Internet", "amountCents": 12000 }
  }'
```

## Notes for production hardening

- Add a Stripe [webhook handler](https://docs.stripe.com/checkout/fulfillment) for `checkout.session.completed` to confirm fulfillment server-side.
- Restrict inbound network access to the partner's IPs in addition to the API key.
- The customer lookup lists Stripe Customers by exact email (read-after-write consistent, unlike Customer Search); if the merchant already has its own customer records in Stripe, replace this with their canonical mapping. Two concurrent first-time checkouts for the same email can still race and create duplicate customers — Stripe does not enforce email uniqueness, so a canonical mapping (or idempotency keys) is the real fix.
