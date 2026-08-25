# Repository Guidelines

## Project Structure & Module Organization

StreamForge is a structure-first .NET 10 and Angular video-platform repository.
Backend boundaries live in `src/backend/`: `gateway/`, domain services under
`services/`, asynchronous workloads under `workers/`, and reusable primitives
under `shared/`. The Angular application belongs in `src/web/`. Tests mirror
those boundaries under `tests/backend/` and `tests/web/`. Infrastructure belongs
in `infra/`; design notes, ADRs, setup guides, and runbooks belong in `docs/`.

Do not share service databases or place domain logic in `shared/`. Communicate
across boundaries through explicit APIs or versioned contracts.

## Build, Test, and Development Commands

The repository currently contains boundaries and configuration, not generated
applications. Use these commands as implementation is added:

- `docker compose up -d`: start local PostgreSQL, Redis, and RabbitMQ.
- `dotnet restore StreamForge.slnx`: restore backend dependencies.
- `dotnet build StreamForge.slnx`: compile all .NET projects.
- `dotnet test StreamForge.slnx`: run backend tests.
- From `src/web/`, use `npm ci`, `npm start`, and `npm test` after Angular is generated.

## Coding Style & Naming Conventions

Honor `.editorconfig`. C# uses four-space indentation, nullable reference types,
file-scoped namespaces, `PascalCase` public members, and `camelCase` locals.
TypeScript, JSON, and YAML use two spaces. Use `kebab-case` for directories and
web files. Name .NET projects `StreamForge.<Domain>.<Role>` and tests
`StreamForge.<Domain>.<Role>.Tests`.

## Testing Guidelines

Keep unit tests fast and isolated; place cross-service and infrastructure checks
in `tests/backend/integration/`. Put Angular unit tests beside features and
browser journeys in `tests/web/e2e/`. Name C# test methods
`Method_Scenario_ExpectedResult`. Add regression tests for every bug fix.

## Commit & Pull Request Guidelines

Use imperative, single-purpose commits such as `Add upload session contract`.
Pull requests must describe behavior and architectural impact, link issues, list
verification commands, and include screenshots for UI changes. Call out schema,
contract, configuration, or deployment changes explicitly.

## Security & Agent Context

Copy `.env.example` to `.env`; never commit secrets or media artifacts. Before
making architectural changes, read `docs/codex/README.md` and relevant ADRs.
Update documentation whenever a boundary, command, or operational assumption changes.
