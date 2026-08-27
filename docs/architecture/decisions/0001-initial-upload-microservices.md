# 0001: Initial upload microservices

- Status: Accepted
- Date: 2026-08-27

## Context

StreamForge needs its first executable slice without coupling future catalog,
processing, or playback capabilities to file ingestion. The repository already
proposes Gateway and Upload boundaries but had not selected deployment, storage,
or communication patterns.

## Decision

- Deploy the Angular Web application, .NET Gateway, and .NET Upload service as
  independent containers.
- Route public backend traffic through the Gateway using YARP and communicate
  from Gateway to Upload over HTTP.
- Propagate `X-Correlation-ID` through the complete request path.
- Let Upload exclusively own a local Docker volume and stream uploads into it.
- Limit the MVP to one MP4, MOV, WebM, or MKV file up to 1 GB per request.
- Do not share implementation projects or storage mounts between services.

## Consequences

The initial topology establishes independent service ownership and allows new
services to be introduced without expanding Upload's responsibility. Local volume
storage is intentionally not suitable for horizontally scaled or cloud production
deployment; a future decision must introduce object storage and durable metadata.
Authentication, resumable upload sessions, media inspection, processing, and
playback remain outside this decision.
