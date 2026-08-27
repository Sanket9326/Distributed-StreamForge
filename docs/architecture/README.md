# Architecture

This service map describes the intended platform boundaries. The Gateway and
Upload boundaries are implemented by the initial upload slice; the remaining
boundaries are proposals and must stay empty until explicitly selected.

## Implemented deployment flow

```text
Web / Nginx -> Gateway / YARP -> Upload -> Upload-owned local volume
```

The Web application, Gateway, and Upload service are separate build and container
units. Only the Gateway exposes backend contracts to the browser. Upload owns its
storage exclusively; other services must use contracts rather than mounting its
volume.

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
