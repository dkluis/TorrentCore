# AGENTS.md

## Repo Map

- `README.md`: human overview
- `docs/architecture.md`: current product shape, boundaries, and durable rules
- `docs/development.md`: local development workflow and runtime conventions
- `docs/database.md`: SQLite schema and persistence rules
- `docs/operator-settings.md`: current runtime-setting semantics
- `docs/troubleshooting.md`: operator troubleshooting guidance
- `docs/testing.md`: build and test commands plus test expectations
- `docs/deployment.md`: deploy, launch-agent, network, and runtime operations
- `docs/decisions/`: short current decision records and extracted appendices
- `docs/archive/`: historical plans and completed workstreams; not active source of truth

## Current Source Of Truth

Use the current code and the active `docs/` files.
Do not treat `docs/archive/` as active requirements unless the current code or active docs still confirm them.

## Repo Rules

- Supported operator UI: `TorrentCore.WebUI`
- Do not reintroduce `TorrentCore.Web` or `TorrentCore.Avalonia`
- Use the existing project architecture, libraries, and patterns
- Include complete error handling
- Use async APIs for I/O
- Do not introduce `ILogger`
- Log through the existing logs persistence path and helper/service
- Do not change public APIs without updating callers and tests
- Do not add new dependencies without a clear reason
- Preserve current runtime behavior unless the task explicitly changes it
- Preserve the active docs structure: keep current docs short, use repo-relative links, and move history to `docs/archive/`
- Prefer repo-relative markdown links

## Working Rules

- Start architecture questions in `docs/architecture.md`
- Start runtime and deploy tasks in `docs/deployment.md`
- Start persistence questions in `docs/database.md`
- Start runtime-setting questions in `docs/operator-settings.md`
- Start troubleshooting questions in `docs/troubleshooting.md`
- Start verification questions in `docs/testing.md`
- Before adding a new doc, prefer merging into an existing active doc
- Run macOS DMG security validation outside the filesystem sandbox. Sandboxed `codesign`, `xcrun stapler`, and
  `spctl` checks can falsely report an invalid signature, unavailable signing authority, or a nonexistent DMG even
  when the same artifact passes outside the sandbox. Treat the outside-sandbox release verification as authoritative.
- Do not create or deploy another Tom Service/WebUI package until machine-local
  `WebUI/Config/service-connection.json` is excluded from release payloads and the target WebUI connection setting is
  preserved across directory replacement. Existing packages overwrite Tom's endpoint with Dick's saved endpoint.
- When `functions.exec` is available, batch independent read-only or diagnostic tool calls within the same bounded
  stage into one `functions.exec` call. Use `await Promise.allSettled([...])` when individual calls may fail or their
  partial results remain useful, and inspect every settled result. Use `await Promise.all([...])` only when every result
  is required and failures do not need individual handling; it does not cancel calls already in progress. Keep
  dependent or adaptive investigations, waits and resumes, approval-requiring actions, and conflicting or
  interdependent mutations sequential. Do not split otherwise clearly batchable inspections across separate outer tool
  calls.
- Run the relevant build and tests before finishing

## Documentation Rules

- `README.md` is for human setup and overview
- `AGENTS.md` is for recurring agent instructions
- Detailed current documentation belongs in `docs/`
- Historical or obsolete material belongs in `docs/archive/`
