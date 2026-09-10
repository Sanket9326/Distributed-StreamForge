# 0008: Durable Elasticsearch video search

- Status: Accepted
- Date: 2026-09-10

## Context

Available videos need prefix-aware suggestions without coupling browser requests
or a new service to Feed's PostgreSQL schema. Upload and completion events can
arrive at Feed in either order, Kafka delivery is at least once, and an
Elasticsearch outage must not block ingestion, transcoding, feed availability,
or playback.

## Decision

- Feed creates a `VideoSearchIndexRequestedV1` outbox row in the same PostgreSQL
  transaction that first joins valid metadata and completed transcoding. The
  consumed-message receipt and Feed projection are committed with it.
- Feed publishes the outbox asynchronously to `video-search-index`, keyed by
  `videoId`. Each video starts at revision 1; future mutations increment the
  per-video revision.
- Search is an independent .NET service containing both the
  `streamforge-search-index-v1` Kafka consumer and the public suggestion API.
  It never reads another service's database.
- Search writes through the `streamforge-videos-write` alias and reads through
  `streamforge-videos-read`. Both initially target `streamforge-videos-v1`,
  which has one primary shard, zero replicas, and strict mappings.
- Elasticsearch uses `videoId` as `_id` and `external_gte` revisioning. Equal
  deliveries are harmless, higher revisions replace the document, and lower
  revisions return a version conflict that Search treats as successfully
  superseded.
- Connectivity errors, timeouts, `429`, and `5xx` responses retry with capped
  exponential backoff while the source offset remains uncommitted. Malformed
  contracts and permanent mapping errors are acknowledged to
  `video-search-index-dead-letter` before the source offset is committed.
- Local development pins Elasticsearch 9.5.3 and
  `Elastic.Clients.Elasticsearch` 9.5.2. The single node is private to the
  Compose network, persists data, has a 1 GB memory limit, and disables
  transport security only for local development.

## Consequences

Search freshness can lag while Elasticsearch or Kafka is unavailable without
affecting video publication or playback. The first version has no automatic DLQ
replay; operators use structured logs and the `search.indexing.failures` counter.
Versioned aliases permit a later reindex without changing clients, while future
metadata edits and deletion require new versioned events and revision handling.
