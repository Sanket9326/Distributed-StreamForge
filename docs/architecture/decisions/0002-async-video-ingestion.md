# 0002: Durable asynchronous video ingestion

- Status: Accepted
- Date: 2026-08-29
- Supersedes: the local-volume portion of ADR 0001

## Context

The first upload slice stored source videos on one container volume and had no
durable metadata or processing handoff. That cannot support independent
processing, safe service restarts, or horizontal deployment. The HTTP request
must still stream up to 1 GB without buffering the full video or writing a local
temporary file.

## Decision

- Upload streams source bytes into a private MinIO bucket under a server-generated
  timestamp and video UUID key.
- Upload owns the authoritative ingestion record in PostgreSQL, including source
  object coordinates and descriptive metadata. This is temporary ingestion
  ownership; a future Catalog/Search boundary remains separate.
- The video row and a `VideoUploadedV1` outbox row are committed in one database
  transaction after MinIO succeeds. A failed validation or database commit is
  compensated by deleting the created object.
- A hosted publisher sends pending messages to the `video-processing` Kafka topic,
  keyed by video ID, and marks them processed after broker acknowledgement.
  Delivery is at-least-once, so consumers must deduplicate by event ID.
- Startup applies committed migrations, creates the private bucket if absent, and
  creates the topic only if absent. Existing topics are never modified.

## Consequences

HTTP success means the source object, video metadata, and eventual publication
intent are durable even when Kafka later becomes unavailable. MinIO and
PostgreSQL must both be available for new uploads; Kafka must be available during
initial startup but an outage after startup only delays outbox delivery. Holding
PostgreSQL row locks while publishing makes concurrent publishers safe, but
production scaling may later require leases or a dedicated relay. Production
rollouts should also move migrations from application startup into a controlled
deployment step.
