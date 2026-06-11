---
name: build
description: Build the solution and report errors or warnings. Use when asked to build, compile, or check that the code compiles.
compatibility: Requires the .NET 10 SDK
---

# Build the solution

```bash
dotnet build CheckoutPoc.sln
```

The build must finish with **zero errors and zero warnings** — this repo builds clean today, so any new warning is a regression introduced by recent changes, not noise.

- On errors or warnings: report each one with its `file:line`, fix them if they come from the change being worked on, and rebuild until clean.

Conventions in `AGENTS.md` apply to any fix made here (clean architecture, dependencies point inward).
