# 0005: Adaptive HLS packaging and private direct delivery

- Status: Accepted
- Date: 2026-09-03

## Context

Progressive MP4 cannot adapt to changing bandwidth. Adaptive delivery must keep
media private and preserve the event-built Feed and MP4 fallback during rollout.

## Decision

- Transcoding produces MP4 plus a non-upscaled 360p, 480p, 720p, and 1080p
  H.264 Main/AAC-LC CMAF HLS package with aligned four-second closed GOPs.
- It uploads deterministic private objects, verifies them, uploads the master
  last, and atomically records summaries with a version 2 completion event.
- Playback independently projects V2 summaries, ignores valid V1 events,
  rewrites manifests, and signs initialization/media objects for one hour.
- Browsers fetch signed media directly from private MinIO. Local Compose
  restricts MinIO's cluster-wide CORS configuration to the local Web origins;
  the rendition bucket remains private.
- Feed accepts completion V1 and V2, exposes HLS only for V2, and continues to
  sign progressive fallback renditions.
- The Web client pins hls.js 1.7.1, delays segment loading until play, supports
  Auto/manual quality, retries HLS once, and then falls back to MP4.

## Consequences

Transcoding uses more compute, scratch, and objects. Playback is in the manifest
path but not the media-byte path. Existing V1 events, rows, and MP4 objects stay
valid without backfill. Authentication, CDN tokenization, encryption/DRM,
subtitles, live HLS, and moving MP4 signing remain separate work.
