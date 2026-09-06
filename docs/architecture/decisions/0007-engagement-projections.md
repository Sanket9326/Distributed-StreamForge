# 0007: Engagement-owned reactions, comments, and visible views

- Status: Accepted
- Date: 2026-09-06

## Context

The watch experience needs durable comments, mutually exclusive user reactions,
and inexpensive visible counters. Reaction and qualified-view traffic can be
bursty, while comment authors must receive an immediate durable result. Feed and
Identity remain the owners of video metadata and user accounts respectively.

## Decision

One Engagement service owns the `engagement` PostgreSQL schema and its migrations.
It stores a local projection of available video IDs, unique `(video_id, user_id)`
reactions, flat comments, per-video view totals, and Kafka deduplication receipts.
It never creates cross-service foreign keys to Identity.

Reaction changes and client-qualified views are acknowledged by Kafka before the
API changes Redis. They use separate, video-keyed topics. Reaction events are
consumed individually and upsert or delete the durable row; source positions stop
an older delivery from replacing a newer cached state. View events use the client
watch-session ID as their event ID. The consumer groups new receipts by video and
increments PostgreSQL in one transaction, flushing after at most five minutes or
10,000 buffered events, then commits Kafka offsets. Replay is safe because receipt
IDs and topic positions are unique.

Comments are synchronous: PostgreSQL commits before Redis is updated. Author-only
editing and permanent deletion are enforced by Engagement. Pages are newest-first
and use opaque keyset cursors.

Redis stores namespaced like/dislike membership sets, visible counters,
short-lived view-session idempotency keys, and reaction positions. It is a
rebuildable projection, not durable truth. Cache misses hydrate from PostgreSQL.
After Redis loss, views can temporarily show the last committed aggregate.

Engagement learns newly available videos from `video-transcoding-completed`. A
local miss is checked once through Feed and then cached, allowing pre-existing
videos to work after Kafka retention has expired. Identity supplies only public
ID/username projections; Feed supplies the canonical single-video response.

## Consequences

A successful Kafka publish followed by a Redis outage returns an accepted result
with `countsPending`; clients retain their optimistic state and reconcile later.
A Kafka publish failure returns `503` without changing Redis. A Redis failure after
a comment commit does not undo the comment.

Visible views are an MVP metric: the browser emits one event after ten cumulative
seconds of forward playback in a newly created watch session. They are not
fraud-resistant. Other clients see changes on their next fetch; live push,
replies, moderation, sorting, comment reactions, and watch-later persistence are
deferred.
