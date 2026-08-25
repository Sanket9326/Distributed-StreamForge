# Architecture

This is a placeholder service map derived from the project goals. It does not
select databases, messaging, storage, communication patterns, deployment units,
or cloud services. Record those decisions in `decisions/` before implementation.

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

These boundaries may change as the architecture is designed. Keep their folders
empty until the corresponding implementation is explicitly planned.
