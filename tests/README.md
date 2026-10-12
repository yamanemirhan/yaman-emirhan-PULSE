# Focused automated tests

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet test Pulse.slnx
```

xUnit tests exercise the real services, EF Core migrations and SQLite. Each test
uses its own temporary database, removed afterwards; application data is untouched.
No running web server is required. The first run restores NuGet packages.

Covered rules:
- Latest vote per participant, historical boundaries, confidence weighting and zero confidence.
- Request retries, unchanged votes and concurrent writes to the global chain.
- Append-only application guards, altered payloads and missing middle events.
- Legacy v1 and current v2 result replay, presentation reuse and altered result detection.
- Evidence review permissions, self-review prevention, historical visibility and stale reviews.
- Administrator-only operations and persisted Room selection.

These are focused correctness tests, not browser, load or full security tests.
Small concurrent-write checks do not demonstrate production capacity. Tampering
checks bypass the application only in disposable test databases; they do not prove
protection against an administrator rewriting the entire chain or truncating its end.

The tests were written with AI assistance.
