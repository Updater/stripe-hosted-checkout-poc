# Hosting guide — Checkout Session API

This service creates [Stripe Hosted Checkout](https://docs.stripe.com/checkout/quickstart)
sessions. **You host it on your infrastructure with your own Stripe secret key**, so
every charge settles on **your** Stripe account — you are the merchant of record. The
calling application only sends a customer and an offer and receives back a checkout URL;
it never sees your secret key and never touches funds.

```
  Caller (partner backend)                 This service (you host)            Stripe
  ─────────────────────────                ───────────────────────           ──────
  POST /api/checkout-sessions  ─────────▶  validate + find/create  ────────▶  create
   { customer, offer }                      customer, create session          session
                              ◀─────────  { checkoutUrl, ... }    ◀────────  (your account)
  redirect end-user to checkoutUrl ─────────────────────────────────────────▶ hosted page
```

The end-user pays on Stripe's hosted page and is redirected back to the caller's
`successUrl` / `cancelUrl`.

## What you need

- **.NET 10 SDK** (the service targets `net10.0`).
- **A Stripe account** and its **secret key** (`sk_live_…` for production, `sk_test_…`
  to validate first). This is the only Stripe credential the service uses.
- **HTTPS termination** in front of the service (load balancer or reverse proxy). The
  service is HTTPS-only and will redirect plain HTTP.

You do **not** need any code from the caller, and you do **not** share your Stripe
secret key with anyone — it stays in your environment.

## Build and run

```bash
dotnet build CheckoutPoc.sln
dotnet run --project src/Checkout.Api      # listens on https://localhost:7210
```

For a production deployment, publish a self-contained build and run it behind your
HTTPS proxy:

```bash
dotnet publish src/Checkout.Api -c Release -o ./publish
# then run ./publish/Checkout.Api with the configuration below
```

## Configuration

Supply these via environment variables (recommended for servers), a secrets manager,
or any standard ASP.NET Core configuration source. **Do not commit real keys to the
repository** — `appsettings.json` holds placeholders only.

| Setting | Environment variable | Required | Description |
|---|---|---|---|
| `Stripe:SecretKey` | `Stripe__SecretKey` | yes | **Your** Stripe secret key (`sk_live_…` / `sk_test_…`). Charges settle on this account. |
| `Api:Key` | `Api__Key` | yes | Shared secret the caller must send in the `X-Api-Key` header. Pick a long random value and share it with the caller over a secure channel. |
| `Stripe:DefaultSuccessUrl` | `Stripe__DefaultSuccessUrl` | yes | Fallback redirect after a successful payment when a request omits `successUrl`. Must be `https://`. Stripe substitutes the `{CHECKOUT_SESSION_ID}` placeholder. |
| `Stripe:DefaultCancelUrl` | `Stripe__DefaultCancelUrl` | yes | Fallback redirect when the user abandons checkout and a request omits `cancelUrl`. Must be `https://`. |

All four are validated at startup — the service will refuse to start if any is missing
or if a URL is not `https://`.

### Where the keys come from

- **`Stripe:SecretKey`** — in your Stripe Dashboard, open
  [Developers → API keys](https://dashboard.stripe.com/apikeys) and reveal the
  **secret** key: `sk_test_…` with the test-mode toggle on (use this to validate
  first), `sk_live_…` in live mode for real charges. Do not use the publishable
  `pk_…` key — it cannot create sessions. For production, prefer a
  [restricted key](https://docs.stripe.com/keys#limit-access) granting only what
  this service uses: **Customers (write)** and **Checkout Sessions (write)** — it
  limits the blast radius if the key ever leaks.
- **`Api:Key`** — not a Stripe value; it's a shared secret you create yourself.
  Generate a long random string (the `openssl rand -hex 32` example below works
  well), configure it on the service, and hand it to the calling application over
  a secure channel (a password manager share or secrets exchange — not email or
  chat). The caller sends it back in the `X-Api-Key` header on every request.

Example (Linux/container environment):

```bash
export Stripe__SecretKey="sk_live_…"
export Api__Key="$(openssl rand -hex 32)"
export Stripe__DefaultSuccessUrl="https://your-app.example.com/checkout/success?session_id={CHECKOUT_SESSION_ID}"
export Stripe__DefaultCancelUrl="https://your-app.example.com/checkout/cancel"
```

For local validation against a test key, you can keep the secrets out of your shell and
the repo with user-secrets instead:

```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_…" --project src/Checkout.Api
dotnet user-secrets set "Api:Key" "some-shared-secret" --project src/Checkout.Api
```

## The endpoint the caller uses

`POST /api/checkout-sessions` — headers `X-Api-Key: <shared secret>` and
`Content-Type: application/json`. Callers should also send an
`Idempotency-Key` header (a unique key per logical request, resent verbatim on
retries) so a retry after a timeout cannot create a duplicate customer or
session. Minimal body:

```json
{
  "customer": { "email": "jane@example.com", "externalId": "caller-user-12345" },
  "offer": { "name": "Residential Internet", "amountCents": 12000, "interval": "month" }
}
```

Response `200`:

```json
{
  "sessionId": "cs_test_…",
  "checkoutUrl": "https://checkout.stripe.com/c/pay/cs_test_…",
  "customerId": "cus_…",
  "expiresAt": "2026-06-10T16:00:00Z"
}
```

The caller redirects the end-user to `checkoutUrl`. Errors: `401` (bad/missing API key),
`400` (validation), `502` (Stripe rejected the request), `500` (unexpected failure,
generic message — details stay in the server log); all but `401` carry
`{ "error": "…" }`.

`GET /healthz` returns `{ "status": "ok" }` for load-balancer health checks and requires
no API key.

The full request schema — pre-configured Stripe Price IDs, multi-line bundle orders,
one-time vs. subscription mode, trials, and metadata — is documented in
[`README.md`](../README.md#api).

## Validate before go-live

1. Configure the service with your **test** key (`sk_test_…`).
2. Call the endpoint and complete a payment on the returned URL with Stripe's test card
   `4242 4242 4242 4242` (any future expiry, any CVC).
3. Confirm the session appears in your Stripe Dashboard (test mode) and the redirect
   lands on your `successUrl`.
4. Swap in the live key (`sk_live_…`) only once the test flow is verified.

## Production hardening

- **Fulfillment:** add a Stripe [webhook handler](https://docs.stripe.com/checkout/fulfillment)
  for `checkout.session.completed` so you confirm and fulfill server-side rather than
  trusting the browser redirect. (Not included here — it belongs in your environment
  where your account's webhook signing secret lives.)
- **Network:** restrict inbound access to the caller's IP ranges in addition to the
  `X-Api-Key` check.
- **Tax:** line amounts are tax-exclusive. Callers that compute tax upstream include it
  as a one-time `"Tax"` line item — no Stripe setup needed. To have Stripe compute it at
  checkout instead (`"automaticTax": true`), enable
  [Stripe Tax](https://docs.stripe.com/tax) on your account; sessions requesting it
  without Stripe Tax enabled are rejected with a `400`.
- **Key rotation:** rotate `Api:Key` periodically and coordinate the change with the
  caller; rotate the Stripe key through your Stripe Dashboard.
- **Customer mapping:** the service finds-or-creates the Stripe Customer by email, and
  two concurrent first-time checkouts for the same email can race and create duplicates.
  If the caller already knows the customer's Stripe ID, it should send it as
  `customer.providerCustomerId` — that bypasses the email lookup (and the race) entirely.
- **Rate limiting:** nothing in-process bounds request volume; throttle at the reverse
  proxy or API gateway in front of this service.
- **Logging:** request logging records method, path, status, and duration only — never
  headers or bodies — so the API key and customer data stay out of the logs. Keep it that
  way if you extend logging.
