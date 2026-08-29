# Architecture

This service map describes the intended platform boundaries. The Gateway and
Upload boundaries and the asynchronous ingestion path are implemented; the
remaining boundaries are proposals and must stay empty until explicitly selected.

## Implemented deployment flow

```text
Web / Nginx -> Gateway / YARP -> Upload -> private MinIO source object
                                      -> PostgreSQL video + outbox transaction
                                      -> background publisher -> Kafka
```

The Web application, Gateway, and Upload service are separate build and container
units. Only the Gateway exposes backend contracts to the browser. Upload owns the
private source bucket and temporarily owns ingestion metadata. Future catalog or
search metadata remains a separate boundary and must integrate through contracts,
not by reading Upload's database or mounting its storage.

An upload is accepted only after MinIO and the PostgreSQL video/outbox transaction
are durable. Kafka publication happens later and is at-least-once. The publisher
uses the video ID as the Kafka key; future consumers must deduplicate by event ID.

## Boundaries

| Boundary | Responsibility |
| --- | --- |
| Gateway | Routing, edge authentication, rate limiting, correlation IDs |
| Identity | Users, authentication, authorization |
| Catalog | Video metadata, publication state, search-facing metadata |
| Upload | Source ingestion, private object storage, ingestion metadata, and event outbox |
| Processing | Durable workflow state, retries, job scheduling |
| Transcoder | FFmpeg probing, renditions, thumbnails, HLS packaging |
| Playback | Manifests, playback authorization, delivery metadata |
| Live streaming | Ingest sessions, live packaging, stream lifecycle |
| Analytics | Playback events and aggregated viewing metrics |

These boundaries may change as the architecture is designed. Keep their folders
empty until the corresponding implementation is explicitly planned.

See [ADR 0002](decisions/0002-async-video-ingestion.md) for durability and
ownership decisions.
