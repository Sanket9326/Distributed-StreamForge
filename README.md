# StreamForge

StreamForge is a learning-focused distributed video platform built with .NET
microservices and an Angular web client.

## Implemented slice

The platform accepts a source video, transcodes progressive MP4 renditions, and
projects completed videos into a pageable home feed:

```text
Angular Web / Nginx -> .NET Gateway / YARP -> .NET Upload Service
                                                    |-> private MinIO object
                                                    |-> PostgreSQL video + outbox
                                                    `-> Kafka video-processing
                                                               |
                                                               v
                                                   .NET Transcoding Worker
                                                     |-> PostgreSQL jobs + outbox
                                                     |-> FFmpeg MP4 renditions in MinIO
                                                     `-> completed / failed Kafka topics
                                                                  |
                                                                  v
                                                    .NET Feed API -> PostgreSQL read model
                                                                  -> signed rendition URLs
```

The Upload service streams MP4, MOV, WebM, and MKV files up to 1 GB directly to
MinIO, commits metadata and an outbox event to PostgreSQL, and publishes the
event to Kafka asynchronously. The independently scalable Transcoding worker
durably accepts those events, generates non-upscaled H.264/AAC MP4 renditions,
and publishes outcomes to dedicated Kafka topics. Feed independently joins the
upload and completion events, returns only ready videos, and signs every
available rendition so Angular can stream directly from private object storage.
No backend service proxies playback bytes.

## Quick start with Docker

Start Docker Desktop, copy the credential template, replace its placeholders,
then run:

```powershell
Copy-Item .env.example .env
docker compose -f infra/docker/compose.yml up --build
```

The first build compiles the pinned MinIO Community release from its official
source tag. The local interfaces are:

- StreamForge: `https://localhost:8443`
- Signed MinIO media API: `https://localhost:9443` (signed URLs only)
- pgAdmin: `http://localhost:5050`
- MinIO Console: `http://localhost:9001`

pgAdmin uses `STREAMFORGE_PGADMIN_EMAIL` for its login email and the local
`STREAMFORGE_POSTGRES_PASSWORD` for its login password. In pgAdmin, register a
server with host `postgres`, port `5432`, and the database/user/password values
from `.env`. Sign in to MinIO with `STREAMFORGE_MINIO_ACCESS_KEY` and
`STREAMFORGE_MINIO_SECRET_KEY` from `.env`.

Stop the services without deleting stored objects or database/Kafka data:

```powershell
docker compose -f infra/docker/compose.yml down
```

See [development setup](docs/development/setup.md) for configuration and cleanup,
[API documentation](docs/api/README.md) for HTTP and event contracts, the
[architecture](docs/architecture/README.md) for ownership, and the
[ingestion runbook](docs/operations/runbooks/README.md) for dependency failures.

User registration, login and logout are implemented with PostgreSQL accounts and
24-hour Redis sessions. Browsing/playback stay public; uploads require login.
See [HTTPS setup and authentication operations](docs/operations/runbooks/authentication.md).
