# Repository Guidelines

## Project Structure & Module Organization

StreamForge is a structure-only .NET 10 and Angular video-platform repository.
Backend boundaries live in `src/backend/`: `gateway/`, domain services under
`services/`, asynchronous workloads under `workers/`, and reusable primitives
under `shared/`. The Angular application belongs in `src/web/`. Tests mirror
those boundaries under `tests/backend/` and `tests/web/`. Infrastructure belongs
in `infra/`; design notes, ADRs, setup guides, and runbooks belong in `docs/`.

These directories are placeholders, not accepted architecture decisions. Do not
add generated projects or select infrastructure without an explicit task.

## Build, Test, and Development Commands

There are currently no build, test, Docker, or development commands. Do not add
placeholder commands that cannot run. Document commands here and in `README.md`
only after the corresponding tooling has been selected and initialized.

## Coding Style & Naming Conventions

Honor `.editorconfig`. C# uses four-space indentation, `PascalCase` public
members, and `camelCase` locals. TypeScript, JSON, and YAML use two spaces. Use
`kebab-case` for directories and web files. Final framework conventions and
project naming will be documented when the first applications are generated.

## Testing Guidelines

Testing frameworks are not selected yet. Reserve `tests/backend/unit/` for unit
tests, `tests/backend/integration/` for integration tests, `tests/web/e2e/` for
browser tests, and `tests/performance/` for load testing. Document naming and
coverage rules when those test projects are initialized.

## Commit & Pull Request Guidelines

Use imperative, single-purpose commits such as `Add upload session contract`.
Pull requests must describe behavior and architectural impact, link issues, list
verification commands, and include screenshots for UI changes. Call out schema,
contract, configuration, or deployment changes explicitly.

## Security & Agent Context

Never commit secrets or media artifacts. Before making architectural changes,
read `docs/codex/README.md` and relevant ADRs. Update documentation whenever a
boundary, command, or operational assumption changes.
