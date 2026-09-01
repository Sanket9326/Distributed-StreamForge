# Async Ingestion Runbook

## Startup does not become ready

Upload intentionally blocks readiness until PostgreSQL migrations, the private
MinIO bucket, and the Kafka topic check succeed. Inspect status and logs:

```powershell
docker compose -f infra/docker/compose.yml ps
docker compose -f infra/docker/compose.yml logs upload-service postgres pgadmin minio kafka
```

Correct credentials or dependency availability, then restart Upload. Startup is
idempotent: committed migrations and an existing bucket/topic are accepted. The
service never changes partition or replication settings on an existing topic.

## Uploads return 503

`Object storage unavailable` indicates MinIO failure. `Metadata storage
unavailable` indicates PostgreSQL failure. Search Upload logs by the returned
correlation ID. A database failure after an object upload triggers object
deletion; a compensation failure logs the exact bucket/key and correlation ID at
error level and requires manual orphan review.

Verify recovery through the internal topology:

```powershell
docker compose -f infra/docker/compose.yml exec gateway wget --quiet --spider http://upload-service:8080/health
```

## Kafka is unavailable after startup

New uploads remain available because the event is durable in
`outbox_messages`. Kafka and outbox health become unhealthy, and the publisher
retries indefinitely with bounded exponential backoff. Inspect pending rows:

```powershell
docker compose -f infra/docker/compose.yml exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select id, video_id, attempt_count, next_attempt_at_utc, last_error from outbox_messages where processed_at_utc is null order by next_attempt_at_utc;"'
```

Restore Kafka rather than editing or deleting outbox rows. Verify that
`processed_at_utc` becomes non-null and health recovers. Re-publication is
possible after ambiguous broker acknowledgement; consumers must deduplicate by
event ID.

## Local cleanup

Stop containers without data loss using `docker compose -f
infra/docker/compose.yml down`. To permanently remove all local source objects,
database rows, and Kafka logs, use `docker compose -f infra/docker/compose.yml
down --volumes`. Volume deletion cannot be recovered.

## Transcoding is not ready

Inspect the worker and its dependencies:

```powershell
docker compose -f infra/docker/compose.yml ps
docker compose -f infra/docker/compose.yml logs transcoding-service postgres minio kafka
```

Readiness requires the Transcoding schema, `streamforge-renditions`, all four
Kafka topics, FFmpeg, ffprobe, and the configured minimum scratch capacity. The
worker verifies but never creates the Upload-owned `video-processing` topic.

## A transcoding job is retrying or failed

Search logs by event, video, or correlation ID. Inspect service-owned state:

```powershell
docker compose -f infra/docker/compose.yml exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select event_id, video_id, status, attempt_count, next_attempt_at_utc, lease_owner, lease_expires_at_utc, last_error_code from transcoding.jobs order by created_at_utc;"'
```

Transient failures retry five times with bounded exponential backoff. A crashed
replica leaves a lease that another replica can claim after expiration. Invalid
media fails immediately. Do not edit job or outbox rows manually; restore the
dependency and use logs plus the failed/dead-letter topic for diagnosis.

## Outcomes are delayed

Successful MinIO objects and terminal job state remain durable while Kafka is
unavailable. Inspect pending outcome messages:

```powershell
docker compose -f infra/docker/compose.yml exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select id, type, topic, attempt_count, next_attempt_at_utc, last_error from transcoding.outbox_messages where processed_at_utc is null order by next_attempt_at_utc;"'
```

Completion is published only to `video-transcoding-completed`; failure is
published only to `video-transcoding-failed`. The worker has a code-level guard
against publishing to `video-processing`.

## Feed is empty or not ready

Feed readiness requires PostgreSQL, the private rendition bucket, and both
`video-processing` and `video-transcoding-completed`. Inspect the service and
its dependencies:

```powershell
docker compose -f infra/docker/compose.yml ps
docker compose -f infra/docker/compose.yml logs feed-service postgres minio kafka
```

Feed uses `streamforge-feed-v1` with the earliest reset policy. A new database
projection is rebuilt from events still retained by Kafka. An object that exists
only in MinIO cannot be listed because descriptive metadata is deliberately not
stored on rendition objects; restore/replay the matching Kafka events or upload
the video again instead of reading another service's schema.

Inspect projection state without modifying it:

```powershell
docker compose -f infra/docker/compose.yml exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select id, title, has_metadata, has_completion, available_at_utc from feed.videos order by available_at_utc desc nulls last;"'
```

Rows appear publicly only when metadata, completion, and at least one rendition
are present. Replayed offsets and event IDs are safe because Feed deduplicates
them transactionally.

## A signed video does not play

Confirm the browser can reach `http://localhost:9000` and that the Feed response
contains a non-expired `playbackUrl`. Do not make `streamforge-renditions`
public. Feed signs URLs using the browser-visible endpoint but verifies storage
through the private `minio:9000` endpoint. The Web client refreshes an expiring
URL once; persistent `403` or `404` responses indicate clock skew, mismatched
credentials/endpoints, or a rendition object removed after its completion event.
