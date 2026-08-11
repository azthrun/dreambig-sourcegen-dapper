# TODOs

Suggested next steps to make `DreamBig.SourceGen.Dapper` more useful, roughly ordered by impact. Grouped by theme.

## Close the documented gaps (Known Limitations in README)

- [x] **MySQL / MariaDB provider (`DreamBig.SourceGen.Dapper.MySql`) — implemented.** Spec, agreed via grilling session:
  - Driver: **MySqlConnector** (async-first, Apache-2.0, matches the ecosystem's default choice; avoids `MySql.Data`'s GPL-with-exceptions licensing friction).
  - **One package** covers both MySQL 5.7+ and MariaDB 10.3+ — no split package, no runtime server-version branching. Dialect differences between the two are treated as prose documentation ("MySQL 5.7+ / MariaDB 10.3+"), not code paths, consistent with how SQLite's version floor (3.35+ for `RETURNING`) is called out.
  - **`ReturnIdentity = true`** emits a single batched command — `INSERT ...; SELECT LAST_INSERT_ID();` (`ExecuteScalarAsync`) — since plain MySQL has no `OUTPUT`/`RETURNING`. Restricted at **compile time** to auto-increment integer key columns; any other key shape is a new diagnostic, **`DBSGD029`**, rather than silently generating wrong data. MariaDB's native `RETURNING` (10.5+) is deliberately *not* used, to keep one uniform code path — branching on a runtime-detected server version would be the first non-compile-time SQL decision in the generator and conflicts with its compile-time-generation premise.
  - **Identifiers**: always backtick-quoted, unconditionally. No `CaseSensitive` toggle like PostgreSQL's — PostgreSQL's flag exists because quoting there changes case-folding semantics; MySQL's identifier case-sensitivity is a server/filesystem config concern (`lower_case_table_names`), not something backtick-quoting toggles, so the flag wouldn't map to a real behavior difference.
  - **`[DbTable(Schema = ...)]`**: ignored for MySQL, same treatment as SQLite. Not reinterpreted as a database-qualifier — that's a connection-level concern, and implicit reinterpretation would let a copy-pasted SQL Server/PostgreSQL entity silently generate unintended cross-database queries. Cross-database access, if ever needed, is a `[DbQuery]`-with-explicit-`From`-override case.
  - **Testing**: standard unit/generator tests only (DI extension tests, attribute contract tests, generator output tests) — matches current SQL Server/PostgreSQL precedent. **No integration tests** for MySQL specifically; only SQLite has integration tests today, so requiring them uniquely for MySQL would be inconsistent scope creep.
  - Upsert (`ON DUPLICATE KEY UPDATE`) is explicitly **out of scope** — no provider has upsert support today, so MySQL shouldn't be first.
- [ ] **Testcontainers-based integration tests for SQL Server / PostgreSQL / MySQL** — separate, unfolded from the MySQL provider item above. Today only SQLite has integration tests (in-memory, no infra needed); the other providers have none. Worth doing eventually, but scoped as its own cross-cutting effort, not bundled into any single provider's TODO.
- [ ] **Multiple result sets from stored procedures** — currently one mapped result set + output params (`GeneratedProcedureResult<T>`). Extend to `(IEnumerable<T1>, IEnumerable<T2>, ...)` tuples for stored procs that return multiple result sets.
- [ ] **Multi-mapping / complex projections** — support Dapper's multi-map (`splitOn`) so `[DbJoin]` queries can materialize into `Customer` + nested `Order` graphs instead of only flat DTOs.

## Developer experience

- [ ] **Roslyn code fixes for diagnostics** — 28 `DBSGDxxx` diagnostics exist but (unverified — check `Diagnostics/` and `Generation/`) none appear to ship `CodeFixProvider`s. Adding fixes for the common ones (missing `[DbKey]`, missing `CancellationToken`, bad `OrderBy`/`By` property names) would turn compile errors into one-click fixes.
- [ ] **Analyzer/generator unit test coverage per diagnostic** — verify each `DBSGD0xx` has a dedicated test in `RepositorySourceGeneratorTests.cs` triggering it; add any missing ones so future refactors don't silently drop a check.
- [ ] **IntelliSense / XML doc coverage on public attributes** — ensure every public attribute in `Attributes/` has full XML doc comments (property-level, not just class-level) so consumers get inline guidance without leaving the IDE.
- [ ] **Sample/starter project** — a minimal runnable console or ASP.NET Core sample (SQLite-backed, zero external deps) under `samples/` that exercises CRUD, paging, joins, and a stored procedure, so new users can `dotnet run` instead of assembling snippets from the README.

## Generated code quality

- [ ] **Batch/bulk insert via provider-native APIs** — current bulk inserts execute per-item via Dapper multi-execute; investigate `SqlBulkCopy` (SQL Server) / `COPY` (PostgreSQL) fast paths for large collections, opt-in via `[DbOperation]` or a threshold.
- [ ] **Compiled/cached SQL text** — confirm generated `Sql` constants are truly `const string` (zero runtime cost) everywhere, including dynamically composed `[DbQuery]`/`[DbJoin]` SQL; document this guarantee explicitly since it's a selling point vs. hand-written Dapper.
- [ ] **Nullable reference type audit** — verify generated code is clean under `<Nullable>enable</Nullable>` with no generator-suppressed warnings, and add a test project that consumes the generator with NRTs + warnings-as-errors.

## Observability & extensibility

- [ ] **Interceptor/hook points** — allow consumers to plug in logging, metrics, or `DbCommand` interception (e.g., an `IDbCommandInterceptor` invoked before/after execution) for tracing generated SQL in production without hand-rolling Dapper wrappers.
- [ ] **OpenTelemetry integration** — emit `Activity`/spans around generated repository calls, gated behind an opt-in DI flag, so generated code participates in existing tracing pipelines.

## Packaging & release hygiene

- [ ] **CHANGELOG.md** — the repo is at `0.1` with active `fix:`-only commit history; a changelog would help consumers track breaking changes across provider packages as the API stabilizes toward 1.0.
- [ ] **CONTRIBUTING.md** — document the solution layout (`Abstractions` / `Generator` / per-provider `.Package` projects), how to add a diagnostic, and how to run generator tests, to lower the bar for external contributors.
- [ ] **Public API baseline (`Microsoft.CodeAnalysis.PublicApiAnalyzers` or `PublicAPI.Shipped.txt`)** — lock down the public surface now, before 1.0, so accidental breaking changes to attributes/generated signatures are caught in CI.

## Verification note

Items above are inferred from `README.md`'s "Known Limitations" section, the diagnostics list in `Diagnostics/DiagnosticDescriptors.cs`, and the project layout — not from exhaustive code reading. Before starting any item, confirm the current behavior in code (e.g., check for existing code fixes, existing OTel hooks) since some may already be partially implemented.
