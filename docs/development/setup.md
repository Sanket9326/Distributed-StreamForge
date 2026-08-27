# Development Setup

## Prerequisites

- .NET SDK 10.0.303 or a later 10.0 patch in the same feature band.
- Node.js 24.15.0 or newer and npm 11 for Angular 22.
- Docker Desktop with Docker Compose for the container workflow.

`global.json`, `package-lock.json`, and the container base images define the
repeatable toolchain. Never configure upload storage inside the Git workspace.

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

Start each command in its own terminal from the repository root. The Upload
service uses the operating-system temporary directory when no storage root is
configured.

```powershell
dotnet run --project src/backend/services/upload/StreamForge.Upload.Api.csproj
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
docker compose -f infra/docker/compose.yml up --build
docker compose -f infra/docker/compose.yml ps
```

Open `http://localhost:8080`. Only the Web container publishes a host port. The
Gateway and Upload service remain on the private Compose network.

Stop containers while preserving uploads:

```powershell
docker compose -f infra/docker/compose.yml down
```

To permanently delete the local upload volume, explicitly include `--volumes`.
This cannot be undone:

```powershell
docker compose -f infra/docker/compose.yml down --volumes
```

## Configuration

| Component | Key | Default |
| --- | --- | --- |
| Gateway | `ReverseProxy:Clusters:upload-cluster:Destinations:upload-service:Address` | `http://localhost:5081/` |
| Upload | `UploadStorage:RootPath` | OS temp directory under `streamforge/uploads` |
| Upload | `UploadStorage:MaxFileSizeBytes` | `1073741824` |

Use double underscores for environment-variable configuration segments. Do not
commit secrets, local paths, or uploaded media.
