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

`video-processing` is an input-only topic for Transcoding. The Transcoding
publisher rejects attempts to publish any outcome back to this topic.

## Video transcoding completed event

After every selected rendition is durable and verified, Transcoding publishes
`VideoTranscodingCompletedV1` to `video-transcoding-completed`, keyed by video
ID. The payload uses camel case:

```json
{
  "eventId": "654c9e39-eece-48a7-a597-f2107bd06f14",
  "eventType": "video.transcoding.completed",
  "eventVersion": 1,
  "occurredAtUtc": "2026-08-30T10:30:00Z",
  "causationEventId": "5adbaf16-45de-46bc-b499-24be0414125d",
  "videoId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "sourceBucket": "streamforge-videos",
  "sourceObjectKey": "sources/2026/08/30/source.mp4",
  "sourceEtag": "source-etag",
  "renditions": [
    {
      "tier": "480p",
      "width": 854,
      "height": 480,
      "videoCodec": "h264",
      "audioCodec": "aac",
      "contentType": "video/mp4",
      "bucket": "streamforge-renditions",
      "objectKey": "videos/e2c1bb104340452f9fc6a68cf4b12457/480p/e2c1bb104340452f9fc6a68cf4b12457-480p.mp4",
      "etag": "rendition-etag",
      "sizeBytes": 5242880
    }
  ],
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01"
}
```

## Video transcoding failed event

A valid job that cannot complete publishes `VideoTranscodingFailedV1` to
`video-transcoding-failed`, keyed by video ID. `failureReason` is intentionally
sanitized and never contains credentials or raw process output.

```json
{
  "eventId": "09642f4a-ec3c-41b3-8155-982a31f05a82",
  "eventType": "video.transcoding.failed",
  "eventVersion": 1,
  "occurredAtUtc": "2026-08-30T10:31:00Z",
  "causationEventId": "5adbaf16-45de-46bc-b499-24be0414125d",
  "videoId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "failureCode": "source_media_invalid",
  "failureReason": "The media file could not be probed.",
  "attemptCount": 1,
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01"
}
```

Malformed envelopes are published to `video-processing-dead-letter` with their
source topic, partition, offset, key, rejection code, and original payload.

## Home feed

`GET /api/feed/videos?limit={1..10}&cursor={opaque}` returns completed videos in
newest-available order. `limit` defaults to 10 and cannot exceed 10. Omit
`cursor` for the first page; pass `nextCursor` unchanged for the next page. A
null `nextCursor` means there are no older ready videos.

```json
{
  "items": [
    {
      "id": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
      "title": "Example title",
      "description": "Example description",
      "hashtags": ["dotnet", "video"],
      "uploadedAtUtc": "2026-08-31T10:30:00Z",
      "availableAtUtc": "2026-08-31T10:35:00Z",
      "renditions": [
        {
          "tier": "1080p",
          "width": 1920,
          "height": 1080,
          "videoCodec": "h264",
          "audioCodec": "aac",
          "contentType": "video/mp4",
          "sizeBytes": 5242880,
          "playbackUrl": "http://localhost:9000/streamforge-renditions/...?signed-query",
          "playbackUrlExpiresAtUtc": "2026-08-31T11:35:00Z"
        }
      ]
    }
  ],
  "nextCursor": null
}
```

Feed returns every completed rendition but no raw source coordinates. Playback
URLs expire after one hour and read directly from the private S3-compatible
bucket. The Web client selects the greatest height and width. If a URL is near
expiry, `GET /api/feed/videos/{videoId}/renditions` returns a fresh signed set.

`GET /api/feed/videos/{videoId}/completion-events` is a server-sent event stream.
It emits one `completed` event and closes when that video is complete. If the
completion was already projected, the event is emitted immediately. The local
Web client opens this stream only for upload IDs retained in that browser.

Invalid limits and cursors return `400`; a rendition refresh for a video that is
not ready returns `404`. Other errors follow the repository Problem Details and
correlation-ID conventions.

## Correlation IDs

Clients may send `X-Correlation-ID` with 1–128 printable ASCII characters. The
Gateway generates a GUID-form identifier when the header is absent or invalid,
forwards it to Upload, and returns the same value in the response header. The
JSON success response and Problem Details body also contain that identifier.

## Health

- Gateway: `GET /health`
- Upload service: `GET /health`, covering PostgreSQL, MinIO, Kafka, and outbox age
- Transcoding worker: `GET /health/live`, `GET /health/ready`, and `GET /health`
- Feed service: `GET /health/live`, `GET /health/ready`, and `GET /health`
- Web container: `GET /health`

Gateway and Upload health endpoints are internal-only in the Compose topology.
