# 0010: Per-user watch history and resume playback

- Status: Accepted
- Date: 2026-09-15

## Decision

Engagement owns watch history in `engagement.watch_history`, with a composite
`(user_id, video_id)` primary key. Rewatch updates the same row. Progress uses
integer milliseconds, a positive duration, and an explicit completion flag.
Creation time is preserved after insertion; update time is the original event's
server timestamp. No foreign key crosses a service boundary.

History requires Gateway authentication. Gateway replaces supplied identity
headers, validates antiforgery on writes, and prevents HTTP caching. Guests can
still browse and play videos. See the [API contract](../../api/watch-history.md).

The API validates video availability, publishes to `user-watch-history`, and
acknowledges with `202` only after Kafka acceptance. It then updates Redis; a
cache failure returns `cachePending: true`. PostgreSQL persistence is asynchronous.
Publication timeouts have uncertain outcomes and do not confirm a saved position.

Each Kafka record uses canonical `userUuid:videoUuid` as its key. The topic has
three partitions by default; its partition count must remain fixed after creation.
The latest original Kafka offset wins, including backward seeks and rewatch.
Offsets remain decimal strings outside PostgreSQL/C# to avoid precision loss.
Neither HTTP completion order nor the browser clock determines the version.

The consumer locks the pair with a transaction-scoped PostgreSQL advisory lock,
updates progress and records the receipt in one transaction, then confirms Redis.
Duplicate delivery still repairs the cache. Old offsets cannot replace newer
progress; a partition mismatch is rejected. Restarted/rebalanced consumers resume
from committed offsets, with local backoff when a failed record cannot be handed off.

## Redis reads and retries

History uses per-user state hashes and lexicographic sorted indexes ordered by
19-digit UTC ticks and video UUID, both descending. Reads use Redis first and
hydrate misses from PostgreSQL. Lists reuse the bounded, generation/lease-checked
rebuild protocol from ADR 0009 with batches of 500 rows. Newer accepted progress
survives hydration. Negative single-video lookups expire after 30 seconds and are
invalidated by accepted saves. Authenticated reads can fall back to PostgreSQL
on cache failure; a shared Redis outage can still prevent Gateway authentication.

Only watch history uses the new jitter scheduler. Failed processing is enqueued
as a complete envelope in a Redis sorted set, scored by due Unix milliseconds.
The envelope retains payload, headers, key, destination, original partition and
offset, event ID, attempt, first failure time, and failure category. A retry's new
Kafka transport offset never becomes its progress version.

The scheduler polls every second, claims at most 50 due entries in ascending
order, and uses 30-second leases renewed every 10 seconds. Expired claims can be
reclaimed. `(eventId, attempt)` identifies an attempt; cleanup cannot remove a
subsequent attempt. Successful attempt markers are retained for 24 hours.
Publication failures release or expire the claim, without discarding its payload.

There are eight processing retries, with full jitter between zero and
`min(60 seconds, 2 seconds * 2^(attempt - 1))`. The consumer commits a failed
record only after retry enqueue succeeds or dead-letter publication is acknowledged.
If enqueue fails, the source remains uncommitted. Malformed or exhausted events
go to `user-watch-history-dead-letter`; exhaustion removes only the matching
unconfirmed cache state, preserving newer or confirmed state.

## Browser behavior

Authenticated `/watch-history` lists recent videos, saved position, duration,
completion, and last-watched time. Metadata comes from Feed with at most four
concurrent requests. Unavailable videos remain visible. Pagination is live, not
a frozen snapshot. Individual removal and clear-all are deferred.

Opening a video from any route looks up progress before playback, with a
two-second bound. Lookup failure starts at zero with a nonblocking notice.
Completed videos restart at zero. The initial seek is clamped to actual media
duration and preserved through HLS recovery, quality selection, refreshed MP4
links, and fallback. Source loading occurs after the rendered source is applied.

Only authenticated playback creates history; thumbnail previews do not. Save on
pause, completion, navigation, teardown, page hiding/exit, and explicit logout.
There are no periodic saves. Overlapping identical saves are coalesced. A failed
save permits another attempt at the next lifecycle event, without a retry loop.
Newer saves and account changes fence delayed callbacks. Exit uses authenticated
keepalive requests with antiforgery; logout waits at most two seconds for flushing.
Background `401` responses clear only the matching account, without redirecting
away from public playback. Foreground history requests retain normal redirects.

## Accepted limitations and rollout

Redis persistence stays unchanged for v1. Browser crashes or missed exit events
can lose unsaved progress. Redis restarts can lose scheduled retries whose source
offsets were already committed, and cache-only progress before PostgreSQL catches
up. Improving durability is explicitly deferred; do not describe Redis as a
durable retry store or promise every acknowledged save survives these failures.

Retained history, version fences, and receipts grow over time. Cleanup/retention
needs a separate design. The scheduler and multi-key scripts assume standalone
Redis. There is no automatic dead-letter replay.

Engagement applies the additive migration and initializes/verifies topics before
readiness. Deploy Engagement and Gateway before exposing the Web feature. Application
rollback retains the table, topics, and offsets; do not run the destructive Down
migration or change the partition count. See the [runbook](../../operations/runbooks/watch-history.md).
