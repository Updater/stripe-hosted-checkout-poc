---
name: test
description: Run the test suite and report results. Use when asked to run tests, verify changes, or investigate test failures.
compatibility: Requires the .NET 10 SDK
---

# Run the tests

```bash
dotnet test CheckoutPoc.sln
```

The suite (xUnit, `tests/Checkout.Tests`) is **fully offline** — every Stripe touchpoint is faked (`FakeCheckoutGateway`), no network or Stripe key needed. A failure is always a real problem, never missing setup.

- To run a subset: `dotnet test CheckoutPoc.sln --filter "FullyQualifiedName~<NameFragment>"`.
- On failures: show the failing test names and assertion output, diagnose, and fix. If the failure reveals a behavior change, remember the convention in `AGENTS.md`: every behavior change ships with tests.
- Report the final count (passed/failed/skipped) when done.
