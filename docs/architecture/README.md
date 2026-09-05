# Architecture

This service map describes the intended platform boundaries. The Gateway,
Identity, Upload, Transcoding, Feed, and Playback boundaries and their asynchronous handoffs are
implemented; the remaining boundaries are proposals and must stay empty until
explicitly selected.

## Implemented deployment flow

```text
Web / Nginx -> Gateway / YARP -> Upload -> private MinIO source object
                                      -> PostgreSQL video + outbox transaction
                                      -> background publisher -> Kafka video-processing
                                                                  |
                                                                  v
                                              Transcoding intake -> PostgreSQL jobs/outbox
                                                                  -> FFmpeg MP4 + CMAF HLS in MinIO
                                                                  -> completed/failed Kafka topics
                                                                               |
                                      Feed PostgreSQL projection <--------------'
                                                |
                         Feed metadata + stable HLS URL through Gateway -> Web
                         Playback signed HLS manifests through Gateway -> Web
                         Browser -> signed private segments directly from MinIO
```

The Web application, Gateway, Upload service, and Feed service are separate build
and container units. Only the Gateway exposes backend API contracts to the browser.
Feed returns signed URLs that let the browser read private renditions directly
from object storage without proxying media bytes. Upload owns the
private source bucket and temporarily owns ingestion metadata. Future catalog or
search metadata remains a separate boundary and must integrate through contracts,
not by reading Upload's database or mounting its storage.

Transcoding reads source coordinates from the versioned event, stores its own
durable job state in the `transcoding` PostgreSQL schema, and writes only to its
private renditions bucket. Long-running FFmpeg work is claimed through expiring
leases, so multiple replicas can share the queue without running media work in
the Kafka poll loop. Downstream consumers use the dedicated completed or failed
topics and never read Transcoding's tables.

Feed consumes the upload and completed topics with its own consumer group, joins
descriptive metadata to rendition coordinates in the `feed` PostgreSQL schema,
and exposes only complete projections. Either event may arrive first. Retained
Kafka history bootstraps existing videos; Feed never scans object storage or reads
another service's schema.

An upload is accepted only after MinIO and the PostgreSQL video/outbox transaction
are durable. Kafka publication happens later and is at-least-once. The publisher
uses the video ID as the Kafka key; future consumers must deduplicate by event ID.

## Boundaries

| Boundary | Responsibility |
| --- | --- |
| Gateway | Routing, edge authentication, rate limiting, correlation IDs |
| Identity | Users, authentication, authorization |
| Catalog | Video metadata, publication state, search-facing metadata |
| Feed | Home-feed projection, cursor pagination, rendition options, and temporary signed playback URLs |
| Upload | Source ingestion, private object storage, ingestion metadata, and event outbox |
| Processing | Proposed future cross-service workflow orchestration |
| Transcoding | Durable job state, retries, FFmpeg probing, and MP4 renditions |
| Playback | V2 HLS projection, strict manifest rewriting, and signed private segment delivery |
| Live streaming | Ingest sessions, live packaging, stream lifecycle |
| Analytics | Playback events and aggregated viewing metrics |

The remaining proposed boundaries may change as the architecture is designed.
Keep their folders empty until the corresponding implementation is explicitly planned.

See [ADR 0002](decisions/0002-async-video-ingestion.md) for durability and
ownership decisions and [ADR 0003](decisions/0003-durable-video-transcoding.md)
for rendition processing and outcome topics. See [ADR 0004](decisions/0004-feed-read-model-and-progressive-playback.md)
for the Feed projection and signed playback decision.
See [ADR 0005](decisions/0005-adaptive-hls-playback.md) for adaptive HLS and the
implemented Playback boundary.

Authentication uses a separate Identity service, its PostgreSQL schema and a
shared Redis instance. Gateway validates opaque cookies for uploads while feed
and playback remain public. Nginx terminates HTTPS for app/API and signed media.
See [ADR 0006](decisions/0006-session-authentication.md).
