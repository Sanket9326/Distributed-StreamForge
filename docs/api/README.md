# API and Event Contracts

The Gateway is StreamForge's public HTTP boundary. The initial API is unversioned
while it remains a local MVP. Gateway and Upload communicate over HTTP and share
no implementation assemblies.

## Upload a video

`POST /api/uploads`

- Content type: `multipart/form-data`
- Required field: `file` (exactly one)
- Maximum file size: 1,073,741,824 bytes
- Extensions: `.mp4`, `.mov`, `.webm`, `.mkv`
- File content type: must begin with `video/`

Example through the public Web/Gateway endpoint:

```powershell
curl.exe -F "file=@C:\path\source.mp4;type=video/mp4" http://localhost:8080/api/uploads
```

Successful response: `201 Created`

```json
{
  "id": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "fileName": "source.mp4",
  "contentType": "video/mp4",
  "sizeBytes": 1048576,
  "uploadedAtUtc": "2026-08-27T00:00:00+00:00",
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01"
}
```

Errors use `application/problem+json` and include `status`, `title`, `detail`,
`instance`, and `correlationId`. Expected statuses are `400`, `413`, `415`, and
`500`.

## Correlation IDs

Clients may send `X-Correlation-ID` with 1–128 printable ASCII characters. The
Gateway generates a GUID-form identifier when the header is absent or invalid,
forwards it to Upload, and returns the same value in the response header. The
JSON success response and Problem Details body also contain that identifier.

## Health

- Gateway: `GET /health`
- Upload service: `GET /health`, healthy only when its storage is writable
- Web container: `GET /health`

Gateway and Upload health endpoints are internal-only in the Compose topology.
