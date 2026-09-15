# 0009: Asynchronous subscriptions and Lua Redis operations

- Status: Accepted
- Date: 2026-09-14

## Decision

Engagement owns directed subscriptions. Identity remains the account owner and
is queried through its public profile contract before Subscribe. No database
foreign key crosses the service boundary. The composite key is
`(subscriber_id, creator_id)`; a separate check prohibits self-subscription.
Inactive rows retain their Kafka position. Repeated Subscribe preserves the
date; Subscribe after removal starts a new date.

The browser changes state immediately. Engagement acknowledges only after Kafka
acceptance, then applies the position-aware Redis projection. A cache failure
after publication returns `202` with `cachePending: true`. Kafka publication
timeouts are uncertain outcomes; clients display uncertainty and re-fetch rather
than automatically replaying a mutation.

All operations for a directed pair use the lower-case canonical key
`subscriberUuid:creatorUuid` on `user-engagement-subscriptions`. The producer
uses acknowledgements from all in-sync replicas and idempotence. V1 keeps the
partition count fixed. Concurrent API requests resolve in the broker's recorded
order, not browser timestamps or HTTP completion order.

Each consumer processes records sequentially. It commits the relationship and
receipt in one PostgreSQL transaction, reconciles Redis, then commits Kafka.
A transaction-scoped advisory lock serializes overlapping rebalanced consumers.
Old offsets cannot overwrite newer durable state. Duplicate deliveries still
repair the cache. Transient errors retry with exponential delays capped at
30 seconds; a failed seek recreates the consumer from committed offsets.
Malformed records require acknowledged publication to
`user-engagement-subscriptions-dead-letter` before their offsets can advance.

## Redis representation

All application Redis data access in Identity, Gateway and Engagement executes
embedded, version-controlled Lua scripts through each service's executor.
Authentication session keys, JSON values, cookie attributes and absolute
24-hour expiry remain unchanged. Gateway reads do not renew TTLs. Logout uses
`allow-oom`; read-only operations use `no-writes`. Script source is constant,
keys use KEYS, and values use ARGV. StackExchange.Redis handles script-cache
recovery; application code does not blindly repeat timed-out scripts.
See [Redis script flags](https://redis.io/docs/latest/develop/programmability/lua-api/#script-flags)
and [atomic scripting behavior](https://redis.io/docs/latest/develop/programmability/eval-intro/).

For a user UUID U, under `streamforge:engagement:subscriptions:v1:user:U`:

| Suffix | Type | Contents |
| --- | --- | --- |
| `following:state` | Hash | Creator UUID -> relationship JSON |
| `subscribers:state` | Hash | Subscriber UUID -> relationship JSON |
| `following:order`, `subscribers:order` | Sorted set | Score 0; member = 19-digit UTC ticks + ":" + counterpart UUID without hyphens |
| `following:ready`, `subscribers:ready` | String | "1" only after complete hydration |
| `*:ready:lease` | String, 30-second TTL | Random lease token |
| `*:ready:generation` | String | Current rebuild token |

Relationship JSON contains subscriberId, creatorId, isActive, createdAtUtc,
updatedAtUtc, sourcePartition, sourceOffset **as a decimal string**, sortTime,
and confirmed. Hash fields retain inactive state and offsets. Neither accepted
relationships nor membership expire before reconciliation. The same script
writes outgoing and incoming hash fields and ordered indexes atomically.
Confirmation of an equal version can repair dates/indexes; an older position
cannot overwrite either direction. Positions from different partitions are
rejected, not compared. Lua never converts Kafka offsets to floating point.

Lists use bounded lexicographic reads of at most 50 returned entries plus one
lookahead. This preserves exact timestamp/UUID ordering without floating-point
timestamp scores. Both Redis and PostgreSQL cursors use creation time and
counterpart ID descending. A cursor is bound to the current user and direction;
it is an opaque pagination position, not an authorization token.

Rebuilding uses a repeatable-read PostgreSQL snapshot and batches of at most
500 rows, including inactive rows. A 30-second lease is renewed every 10 seconds;
every merge and completion verifies both lease and generation. Newer accepted
changes win over snapshot rows. Redis loss, lease loss, or generation mismatch
prevents publishing completeness. Accepted writes never mark lists complete.
Reaction rebuilding uses the same bounded lease protocol. Comment mutations
invalidate counts with a generation token; initialization cannot publish a
database count read before a concurrent invalidation.

## Consequences

Redis remains standalone: updating two users atomically requires a separate
design before Redis Cluster. Redis scripting blocks other Redis work, so
operations remain bounded and perform no Kafka or PostgreSQL I/O. A cache miss
can rebuild a large user's list over multiple database batches; contending
requests wait at most 35 seconds for acquisition before a durable fallback.

PostgreSQL fallback is permitted only after Gateway authentication succeeds.
A shared Redis outage can therefore still prevent authentication. Following
Redis loss, reads can temporarily show the last durable state while Kafka
catches up. Relationship tombstones and cache hashes grow with historical pairs;
monitor capacity and design compaction separately before deleting version fences.

The UI has two private lists, Subscribe back, Unsubscribe, and Remove subscriber.
Removal does not block later resubscription or alter the reverse relationship.
Client state is cleared across account changes; requests retain their account
generation and relationship revision to reject stale callbacks.

