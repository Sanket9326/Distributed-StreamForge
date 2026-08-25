# Architecture

StreamForge uses independently deployable services and queue-backed workers.
Each service owns its persistence and publishes versioned integration events.
Start with a modular, observable local system; split and scale only from measured
pressure rather than assumed traffic.

## Boundaries

| Boundary | Responsibility |
| --- | --- |
| Gateway | Routing, edge authentication, rate limiting, correlation IDs |
| Identity | Users, authentication, authorization |
| Catalog | Video metadata, publication state, search-facing metadata |
| Upload | Resumable upload sessions and object-storage coordination |
| Processing | Durable workflow state, retries, job scheduling |
| Transcoder | FFmpeg probing, renditions, thumbnails, HLS packaging |
| Playback | Manifests, playback authorization, delivery metadata |
| Live streaming | Ingest sessions, live packaging, stream lifecycle |
| Analytics | Playback events and aggregated viewing metrics |

The first vertical slice should be upload -> queued job -> transcode -> publish
-> playback. Record decisions that change these boundaries in `decisions/`.
