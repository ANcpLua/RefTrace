# RefTrace development

This is a private .NET 11 RC3 preview project. The owning SDK and MSTest SDK are
pinned in `global.json`; package versions belong in `Directory.Packages.props`.
The public API is intentionally not frozen before the first release.

Use `make build`, `make test` and `make audit-test`. `make inspect` runs the local
JetBrains tool when installed. Keep reports, downloaded packages and binlogs under
`artifacts/`; never commit credentials or copied package source.

For package reviews, read [.agents/skills/package-evidence/SKILL.md](.agents/skills/package-evidence/SKILL.md).
Use recorded package versions and source revisions, not the date or framework
selector of an aggregated documentation page. A successful inspection is evidence
about its input, not an execution, compatibility or security proof.

Retain the short `ref` command, readable help and explicit JSON/plain modes.
Do not publish a package unless the user requests that release. A private repository
and an existing NuGet trust policy do not by themselves authorize publication.
