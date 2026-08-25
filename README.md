# StreamForge

StreamForge is a learning-focused, production-shaped video platform built around
.NET 10 microservices, an Angular/TypeScript web client, FFmpeg workers, HLS,
live streaming, caching, observability, and fault-tolerant distributed workflows.

## Repository status

The repository currently defines architecture boundaries and local development
infrastructure. Application projects will be generated incrementally so each
service starts with an explicit responsibility and contract.

## Planned request flow

```text
Angular client -> Gateway -> Domain APIs -> PostgreSQL / Redis
                           -> RabbitMQ -> Processing workers -> HLS storage/CDN
Live ingest -> Live service -> Transcoding/packaging -> HLS delivery
```

## Local prerequisites

- .NET SDK 10 (pinned in `global.json`)
- Node.js and an Angular CLI version that supports it
- Docker Desktop with Compose
- FFmpeg for local worker development

Copy `.env.example` to `.env`, then run `docker compose up -d` to start the
local PostgreSQL, Redis, and RabbitMQ dependencies. See `docs/development/setup.md`
and `docs/codex/README.md` before implementing the first vertical slice.
