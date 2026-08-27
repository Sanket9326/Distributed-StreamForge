# StreamForge

StreamForge is a learning-focused distributed video platform built with .NET
microservices and an Angular web client.

## Implemented slice

The first vertical slice accepts one source video through three independently
deployable components:

```text
Angular Web / Nginx -> .NET Gateway / YARP -> .NET Upload Service -> owned volume
```

The Upload service streams MP4, MOV, WebM, and MKV files up to 1 GB into its own
storage. It does not yet catalog, transcode, or serve those files.

## Quick start with Docker

Start Docker Desktop, then run:

```powershell
docker compose -f infra/docker/compose.yml up --build
```

Open `http://localhost:8080`. Stop the services without deleting uploaded files:

```powershell
docker compose -f infra/docker/compose.yml down
```

See [development setup](docs/development/setup.md) for local commands and volume
cleanup, [API documentation](docs/api/README.md) for the HTTP contract, and
[architecture](docs/architecture/README.md) for service ownership.
