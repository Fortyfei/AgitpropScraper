# Content Parser Testcase Policy

This folder stores parser fixtures and testcase definitions used by:
- offline tests (fixture HTML)
- online tests (live URL fetch)

## RunOnline flag

Each testcase can set `RunOnline`:
- `true`: included in online tests via `TestCaseFactory.GetContentParserOnlineCases`
- `false`: excluded from online tests, still used by offline tests

Use `RunOnline=false` when any of these apply:
- live URL is unstable, redirected, or no longer serves the same article
- site anti-bot behavior causes non-deterministic responses
- fixture intentionally covers a historical layout not guaranteed online anymore

## Fixture splitting guidance

If one article has multiple fixture snapshots with materially different extracted output:
1. split into separate testcase entries (one HTML path per entry)
2. keep offline coverage for each snapshot
3. only enable online for the snapshot/url pair that is currently stable

## Online fetch behavior

`ContentParserOnlineTests` now uses an `HttpClientHandler` with automatic gzip/deflate/brotli decompression and browser-like headers.

Reason: some sites return compressed/challenge payloads with default client settings, which can look like garbled input and cause parser failures unrelated to selector logic.
