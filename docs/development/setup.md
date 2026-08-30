# Development Setup

## Prerequisites

- .NET SDK 10.0.303 or a later 10.0 patch in the same feature band.
- Node.js 24.15.0 or newer and npm 11 for Angular 22.
- Docker Desktop with Docker Compose for the container workflow.
- FFmpeg 8 with `ffprobe` and the `libx264` encoder when running Transcoding
  directly outside Docker. The worker container already includes these tools.

`global.json`, `package-lock.json`, NuGet lock resolution, and the container base
images define the toolchain. Integration tests require Docker because they build
the pinned MinIO source release and run real PostgreSQL, MinIO, and Kafka containers.
The first integration run retains the locally built
`streamforge/minio-test:release-2025-10-15` image so later runs do not recompile
MinIO. Remove that image explicitly with `docker image rm
streamforge/minio-test:release-2025-10-15` when it is no longer needed.

## Restore, build, and test

```powershell
dotnet restore StreamForge.slnx
dotnet build StreamForge.slnx --no-restore
dotnet test StreamForge.slnx --no-build --no-restore

Set-Location src/web
npm ci
npm run build
npm test -- --watch=false
```

## Run services locally

Start each command in its own terminal from the repository root. Before starting
Upload outside Compose, supply `ConnectionStrings__UploadDatabase`,
`ObjectStorage__Endpoint`, `ObjectStorage__AccessKey`,
`ObjectStorage__SecretKey`, and `Kafka__BootstrapServers` as environment
variables. PostgreSQL, MinIO, and Kafka must already be reachable.

Before starting Transcoding outside Compose, also supply
`ConnectionStrings__TranscodingDatabase`, its `ObjectStorage__*` credentials,
and `Kafka__BootstrapServers`. The `video-processing` input topic must already
exist; Transcoding creates only its own output topics.

```powershell
dotnet run --project src/backend/services/upload/StreamForge.Upload.Api.csproj
```

```powershell
dotnet run --project src/backend/workers/transcoding/StreamForge.Transcoding.Worker.csproj
```

```powershell
dotnet run --project src/backend/gateway/StreamForge.Gateway.Api.csproj
```

```powershell
Set-Location src/web
npm start
```

Open `http://localhost:4200`. The Angular development proxy sends `/api` requests
to the Gateway on port 5080; the Gateway sends upload requests to the Upload
service on port 5081.

## Run with Docker

```powershell
Copy-Item .env.example .env
# Replace every placeholder in .env before continuing.
docker compose -f infra/docker/compose.yml up --build
docker compose -f infra/docker/compose.yml ps
```

Open the local interfaces:

| Interface | URL | Login |
| --- | --- | --- |
| StreamForge | `http://localhost:8080` | None |
| pgAdmin | `http://localhost:5050` | Email from `STREAMFORGE_PGADMIN_EMAIL`; password from `STREAMFORGE_POSTGRES_PASSWORD` |
| MinIO Console | `http://localhost:9001` | Access key and secret key from `.env` |

To view Upload metadata in pgAdmin, choose **Add New Server** and use:

| Field | Value |
| --- | --- |
| Name | `StreamForge Upload` (or any name) |
| Host name/address | `postgres` |
| Port | `5432` |
| Maintenance database | Value of `STREAMFORGE_POSTGRES_DATABASE` |
| Username | Value of `STREAMFORGE_POSTGRES_USER` |
| Password | Value of `STREAMFORGE_POSTGRES_PASSWORD` |

Use `postgres`, not `localhost`, because pgAdmin runs inside the Compose network.
The Gateway, Upload service, Transcoding worker, database port, MinIO API, and Kafka remain private;
only the Web UI and local administration consoles publish host ports. Upload
applies committed EF Core migrations, creates the private MinIO bucket if absent,
and creates `video-processing` only if absent before becoming ready.
Transcoding then creates `streamforge-renditions` and its completed, failed, and
dead-letter topics if absent.

Stop containers while preserving objects, database rows, and Kafka logs:

```powershell
docker compose -f infra/docker/compose.yml down
```

To permanently delete all local MinIO, PostgreSQL, Kafka, and pgAdmin data,
explicitly include `--volumes`. This cannot be undone:

```powershell
docker compose -f infra/docker/compose.yml down --volumes
```

## Configuration

| Component | Key | Default |
| --- | --- | --- |
| pgAdmin | `STREAMFORGE_PGADMIN_EMAIL` | `admin@streamforge.dev` |
| pgAdmin | Login password | Value of `STREAMFORGE_POSTGRES_PASSWORD` |
| Gateway | `ReverseProxy:Clusters:upload-cluster:Destinations:upload-service:Address` | `http://localhost:5081/` |
| Upload | `ConnectionStrings:UploadDatabase` | Required |
| Upload | `Upload:MaxFileSizeBytes` | `1073741824` |
| Upload | `ObjectStorage:Endpoint` | Required |
| Upload | `ObjectStorage:AccessKey` / `SecretKey` | Required secret values |
| Upload | `ObjectStorage:Bucket` | `streamforge-videos` |
| Upload | `ObjectStorage:UseSsl` | `false` |
| Upload | `Kafka:BootstrapServers` | Required |
| Upload | `Kafka:TopicName` | `video-processing` |
| Upload | `Kafka:PartitionCount` / `ReplicationFactor` | `1` / `1` |
| Upload | `Outbox:PollIntervalMilliseconds` | `1000` |
| Upload | `Outbox:BatchSize` | `20` |
| Upload | `Outbox:MaximumRetryDelaySeconds` | `60` |
| Upload | `Outbox:DegradedAfterSeconds` | `300` |
| Transcoding | `ConnectionStrings:TranscodingDatabase` | Required; local Compose uses the Upload database with an isolated schema |
| Transcoding | `Kafka:ConsumerGroupId` | `streamforge-transcoding-v1` |
| Transcoding | `Kafka:InputTopic` | `video-processing` |
| Transcoding | `Kafka:CompletedTopic` | `video-transcoding-completed` |
| Transcoding | `Kafka:FailedTopic` | `video-transcoding-failed` |
| Transcoding | `Kafka:DeadLetterTopic` | `video-processing-dead-letter` |
| Transcoding | `ObjectStorage:RenditionsBucket` | `streamforge-renditions` |
| Transcoding | `Transcoding:MaxConcurrentJobs` / `MaxAttempts` | `1` / `5` |
| Transcoding | `Transcoding:LeaseDurationSeconds` / `LeaseHeartbeatSeconds` | `120` / `30` |
| Transcoding | `Transcoding:JobTimeoutSeconds` | `21600` |
| Transcoding | `Transcoding:ScratchPath` | `/tmp/streamforge-transcoding` |

Use double underscores for environment-variable configuration segments. Do not
commit `.env`, secrets, local paths, or uploaded media. `.env.example` contains
placeholders only.
