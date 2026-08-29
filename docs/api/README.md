# API and Event Contracts

The Gateway is StreamForge's public HTTP boundary. The initial API is unversioned
while it remains a local MVP. Gateway and Upload communicate over HTTP and share
no implementation assemblies.

## Upload a video

`POST /api/uploads`

- Content type: `multipart/form-data`
- `title`: required string, trimmed, 1–200 characters
- `description`: optional string, trimmed, at most 5,000 characters
- `hashtags`: optional repeated string field, at most 10 normalized values
- `file`: required, exactly one file
- Maximum file size: 1,073,741,824 bytes
- Extensions: `.mp4`, `.mov`, `.webm`, `.mkv`
- File content type: must begin with `video/`

Hashtags are trimmed, stripped of one leading `#`, lowercased, de-duplicated in
submission order, and limited to 1–50 letters, numbers, underscores, or hyphens.
The Web UI accepts comma-separated values and sends one `hashtags` part per value.

Example through the public Web/Gateway endpoint:

```powershell
curl.exe `
  -F "title=Example title" `
  -F "description=Example description" `
  -F "hashtags=dotnet" `
  -F "hashtags=video" `
  -F "file=@C:\path\source.mp4;type=video/mp4" `
  http://localhost:8080/api/uploads
```

Successful response: `201 Created`

```json
{
  "id": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "title": "Example title",
  "description": "Example description",
  "hashtags": ["dotnet", "video"],
  "status": "queued",
  "fileName": "source.mp4",
  "contentType": "video/mp4",
  "sizeBytes": 1048576,
  "uploadedAtUtc": "2026-08-29T10:30:00+00:00",
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01"
}
```

Errors use `application/problem+json` and include `status`, `title`, `detail`,
`instance`, and `correlationId`. Expected statuses are `400`, `413`, `415`, and
`500`, and `503`. A `201` response means MinIO and the PostgreSQL video/outbox
transaction are durable; it does not wait for Kafka publication.

## Video uploaded event

The outbox publisher sends `VideoUploadedV1` to `video-processing` with the
canonical video UUID as the Kafka key. The JSON payload uses camel case:

```json
{
  "eventId": "5adbaf16-45de-46bc-b499-24be0414125d",
  "eventType": "video.uploaded",
  "eventVersion": 1,
  "occurredAtUtc": "2026-08-29T10:30:00Z",
  "videoId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "bucket": "streamforge-videos",
  "objectKey": "sources/2026/08/29/20260829T103000000Z-e2c1bb104340452f9fc6a68cf4b12457.mp4",
  "etag": "object-etag",
  "originalFileName": "source.mp4",
  "contentType": "video/mp4",
  "sizeBytes": 1048576,
  "title": "Example title",
  "description": "Example description",
  "hashtags": ["dotnet", "video"],
  "ownerId": null,
  "uploadedAtUtc": "2026-08-29T10:30:00Z",
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01"
}
```

Delivery is at-least-once. Consumers must persist and deduplicate `eventId`
before applying non-idempotent work.

## Correlation IDs

Clients may send `X-Correlation-ID` with 1–128 printable ASCII characters. The
Gateway generates a GUID-form identifier when the header is absent or invalid,
forwards it to Upload, and returns the same value in the response header. The
JSON success response and Problem Details body also contain that identifier.

## Health

- Gateway: `GET /health`
- Upload service: `GET /health`, covering PostgreSQL, MinIO, Kafka, and outbox age
- Web container: `GET /health`

Gateway and Upload health endpoints are internal-only in the Compose topology.
