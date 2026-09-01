# 0004: Event-built home feed and signed progressive playback

- Status: Accepted
- Date: 2026-09-01

## Context

Completed MP4 renditions are private objects owned by Transcoding. The browser
needs a pageable home feed with upload metadata and every available rendition,
but Upload and Transcoding must not expose their databases or become synchronous
request dependencies. The current completion event contains rendition coordinates
while the upload event contains descriptive metadata.

## Decision

- Deploy Feed as an independent .NET API behind Gateway.
- Let Feed consume `video-processing` and `video-transcoding-completed` with its
  own consumer group and join both immutable contracts by video ID in a Feed-owned
  PostgreSQL schema.
- Commit Kafka offsets only after an idempotent projection transaction. Feed can
  receive either event first and publishes a video only after both halves exist.
- Page ready videos newest-first with an opaque keyset cursor and a maximum page
  size of ten.
- Keep the rendition bucket private. Feed returns one-hour signed URLs for all
  completed MP4 renditions and never proxies video bytes.
- Use a per-video server-sent event for the local browser completion notice.
  Browser storage, rather than server identity, temporarily scopes "your upload."

## Consequences

Feed remains available without synchronous calls to Upload or Transcoding and can
rebuild from retained events. S3-only objects whose events have expired cannot be
reconstructed because object metadata intentionally omits descriptive fields.
Signed URLs make the object-storage API browser reachable while retaining private
bucket policy. Feed temporarily performs playback URL signing; authorization,
CDN policy, durable notifications, and adaptive manifests remain responsibilities
for future Identity, Playback, and Notification boundaries.
