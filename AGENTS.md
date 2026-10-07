# Validation Policy

- Match local validation to the size and scope of the change. For a small, localized feature or fix, run focused tests for the affected behavior and build only the affected projects when needed.
- Do not run the full local test suite merely to commit, push, or prepare a release. Full application regression, exhaustive GUI and language coverage, and cross-platform package validation belong in CI. CI is the full regression gate.
- For save-data changes, local tests must cover the changed fields, input limits, serialization round trips, checksums, and preservation of unrelated data. Keep these checks focused on the change rather than running every editor feature.
- Reuse successful local validation results when the relevant code has not changed. Do not repeat a completed full suite as an additional release prerequisite.
- Run full local regression only when the user explicitly requests it or concrete evidence shows that focused checks are insufficient. Explain the reason before starting it.
- If an existing release script forces redundant full local testing, report that conflict before running it. Do not silently spend time on the full suite, bypass the designated release workflow, or manually replace its version commits, tags, or release artifacts.
- Report which focused checks ran and which full checks are left to CI. Do not describe unrun tests as passed.
