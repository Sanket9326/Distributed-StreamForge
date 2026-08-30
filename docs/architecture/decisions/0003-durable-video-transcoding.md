# 0003: Durable horizontally scaled video transcoding

- Status: Accepted
- Date: 2026-08-30

## Context

Upload publishes `VideoUploadedV1` at least once after retaining an immutable
source in MinIO. Encoding directly inside a Kafka poll loop would make lengthy
FFmpeg work vulnerable to consumer rebalances, while a stateless worker could
repeat side effects after a restart. Generated media must remain private,
reproducible from the original, and independently scalable from ingestion.

## Decision

- Deploy Transcoding as an independent .NET worker with Kafka intake, durable
  job scheduling, FFmpeg execution, and outcome publication as separate roles.
- Consume `video-processing` only. Persist the topic/partition/offset and a job
  before manually committing Kafka, and deduplicate jobs by upload `eventId`.
- Coordinate replicas through expiring PostgreSQL leases in the
  Transcoding-owned `transcoding` schema. Local Compose shares the PostgreSQL
  server; the service never reads Upload tables.
- Retain the original only in Upload's source bucket. Write deterministic MP4
  objects to the private `streamforge-renditions` bucket under
  `videos/{videoId}/{height}p/`.
- Use FFmpeg/libx264 and AAC to create non-upscaled 480p, 720p, and 1080p
  renditions. A source below 480p receives one normalized rendition at its
  original even dimensions.
- Commit job completion and a `VideoTranscodingCompletedV1` outbox message in
  one transaction. Commit terminal failure and `VideoTranscodingFailedV1` the
  same way.
- Publish completion to `video-transcoding-completed`, failure to
  `video-transcoding-failed`, and invalid input envelopes to
  `video-processing-dead-letter`. Transcoding never publishes to
  `video-processing`.

## Consequences

Kafka polling stays responsive while encoding can take hours. A crashed worker
may repeat an attempt after its lease expires, but event deduplication,
deterministic object keys, and terminal outbox transactions make repetition
safe. PostgreSQL and local scratch space become runtime dependencies. The first
version uses CPU software encoding and private progressive MP4 files; HLS,
thumbnails, GPU profiles, HDR conversion, and a separately deployed Processing
orchestrator remain future decisions.

The container's Alpine FFmpeg package includes GPL components such as libx264.
Its exact package version, source provenance, and license links are recorded in
the worker's `THIRD_PARTY_NOTICES.md`.
