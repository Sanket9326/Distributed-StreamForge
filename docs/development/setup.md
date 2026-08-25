# Development Setup

## Current bootstrap

1. Install the .NET SDK selected by `global.json`, Docker Desktop, Node.js, and FFmpeg.
2. Copy `.env.example` to `.env` and keep local values uncommitted.
3. Run `docker compose config` to validate configuration.
4. Run `docker compose up -d` to start PostgreSQL, Redis, and RabbitMQ.
5. Open RabbitMQ management at `http://localhost:15672` when troubleshooting queues.

The installed global Angular CLI 17 does not support the currently installed
Node.js 24. Before generating `src/web`, select a supported Angular/Node pairing
and pin it in the web workspace. Do not rely on the global CLI version.

## Project-generation rules

- Use `StreamForge.<Domain>.<Role>` for .NET project names.
- Add every generated .NET project to `StreamForge.slnx` immediately.
- Give each deployable component its own Dockerfile under its project directory.
- Add health, readiness, structured logging, and tracing before integrating a service.
- Never put uploaded or transcoded media inside the Git workspace.
