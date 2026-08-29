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
