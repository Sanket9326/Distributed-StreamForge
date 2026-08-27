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

Restore and build the backend with `dotnet restore StreamForge.slnx` and
`dotnet build StreamForge.slnx --no-restore`. Run backend tests with
`dotnet test StreamForge.slnx --no-build --no-restore`. In `src/web/`, use
`npm ci`, `npm run build`, and `npm test -- --watch=false`. Start the local
container topology with `docker compose -f infra/docker/compose.yml up --build`.
Only document additional commands after their tooling has been initialized and
verified.

## Coding Style & Naming Conventions

Honor `.editorconfig`. C# uses four-space indentation, `PascalCase` public
members, and `camelCase` locals. TypeScript, JSON, and YAML use two spaces. Use
`kebab-case` for directories and web files. Final framework conventions and
project naming will be documented when the first applications are generated.

## Testing Guidelines

Backend tests use xUnit and WebApplicationFactory; Angular unit tests use Vitest.
Keep unit tests under `tests/backend/unit/`, service-boundary tests under
`tests/backend/integration/`, browser tests under `tests/web/e2e/`, and load tests
under `tests/performance/`. Name test projects after the component and test level,
and name test methods for the behavior and expected outcome.

## Commit & Pull Request Guidelines

Use imperative, single-purpose commits such as `Add upload session contract`.
Pull requests must describe behavior and architectural impact, link issues, list
verification commands, and include screenshots for UI changes. Call out schema,
contract, configuration, or deployment changes explicitly.

## Security & Agent Context

Never commit secrets or media artifacts. Before making architectural changes,
read `docs/codex/README.md` and relevant ADRs. Update documentation whenever a
boundary, command, or operational assumption changes.
