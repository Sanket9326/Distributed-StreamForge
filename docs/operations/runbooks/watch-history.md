# Watch history and resume operations

See the [API contract](../../api/watch-history.md) and
[ADR 0010](../../architecture/decisions/0010-watch-history-and-resume.md).

## Configuration and deployment

Engagement uses its existing PostgreSQL, Redis, Kafka, and Feed connections.
New settings are:

| Setting | Default |
| --- | --- |
| `Kafka:WatchHistoryTopic` | `user-watch-history` |
| `Kafka:WatchHistoryDeadLetterTopic` | `user-watch-history-dead-letter` |
| `Kafka:WatchHistoryConsumerGroupId` | `streamforge-engagement-watch-history-v1` |
| `Kafka:WatchHistoryPartitionCount` | `3` |

Compose uses double underscores for these environment variables. Both new topics
inherit `Kafka:ReplicationFactor`; only the history partition setting controls
their initial partition count. Never increase an existing history topic's partition
count: key reassignment would break the original-offset ordering fence.

Deploy Engagement first. Its startup lock protects the additive
`20260915120733_AddWatchHistory` migration. It creates missing topics, verifies
the history topic's fixed partition count, and gates consumers on readiness.
Deploy the Gateway protection before the Web navigation/player changes.
Existing session formats and Redis persistence settings are unchanged.

For an application rollback, retain `engagement.watch_history`, Kafka topics,
receipts, and consumer offsets. Do not run migration Down, reset Kafka storage,
or remove version fences while old messages/retries may still exist.

## Missing history or resume

Confirm the user is signed in. Guests and thumbnail previews create no history.
Progress saves on pause, completion, leaving a video/page, and logout, without
periodic checkpoints. A completed video intentionally restarts at zero.

A resume lookup waits at most two seconds; a failed/slow lookup starts playback
at zero with a notice. An unavailable Feed video remains in history with an
unavailable label. `401` indicates an expired session; `403` indicates antiforgery
failure. Background history failures do not redirect public playback.

`202` means Kafka accepted the save. When `cachePending` is true or the consumer
lags, a cache rebuild/database fallback may temporarily show older durable data.
Check Engagement health and Kafka lag. Gateway authentication still depends on
Redis, so a Redis-wide outage can reject the request before database fallback.

## Cache recovery

Under `streamforge:engagement:history:v1:user:<userUuid>`, `state` is the per-video
hash and `order` is the zero-score lexicographic index. `ready`, `ready:lease`,
and `ready:generation` coordinate complete hydration. `absent:<videoUuid>` stores
a 30-second negative lookup. Application access uses embedded Lua scripts.

Cache misses rebuild from PostgreSQL in batches of at most 500. A 30-second lease
is renewed every 10 seconds; all merges and completion check the generation.
Contending requests wait at most 35 seconds to acquire the rebuild before the
durable fallback. Accepted newer writes survive snapshot merging.

Investigate Redis errors and key types before clearing anything. Do not flush
shared Redis: it also owns login sessions and pending retries. Do not delete only
one history key or mark a list ready manually. Restore Redis health and allow
the generation-checked rebuild to finish.

## Retry backlog and dead letters

The prefix `streamforge:engagement:history:retry:v1` has `due` and `inflight`
sorted sets, a `members` hash, a `leases` hash, and a `completed` sorted set.
Due scores are Unix milliseconds. Claims contain the entire original message
and source position. Failed republication releases a claim; expired 30-second
leases allow another worker to recover it. Do not remove members before Kafka
acknowledges republication or replay retries as new progress events.

Monitor the emitted .NET meters (an exporter is not configured by this feature):

| Meter / instrument | Meaning |
| --- | --- |
| `StreamForge.Engagement.WatchHistory.Consumer` / `watch_history.consumer.lag` | Remaining records after each consumed record, tagged by partition |
| `StreamForge.Engagement.WatchHistory.Consumer` / `watch_history.dead_letters` | Dead letters, tagged by reason |
| `StreamForge.Engagement.WatchHistory` / `watch_history.retry.depth` | Due plus in-flight entries sampled each scheduler cycle |
| `StreamForge.Engagement.WatchHistory` / `watch_history.retry.overdue` | Seconds overdue for the earliest due entry |
| `StreamForge.Engagement.Redis` / `redis.script.duration` | Script timings and counts, tagged by script name and success/error outcome |

If retry enqueue itself fails, the original Kafka offset remains uncommitted and
the consumer retries locally with capped backoff. Restore PostgreSQL/Redis/Kafka
availability. Eight failed processing retries lead to the dead-letter topic.
Read its reason, original source position, payload, failure category, and attempt
to investigate; payloads contain personal viewing data and must not be posted
to public logs or committed to Git. There is no automatic DLQ replay tool.
Any future replay must retain original identity and ordering information.

## Known v1 loss cases

Redis runs without persistence by explicit decision. Restarting it loses queued
retries even if Kafka has already committed their source offsets. Those messages
will not automatically reappear in this consumer group. Durable PostgreSQL rows
can rebuild their caches; unpersisted queued progress cannot be recovered by cache
hydration. Preserve retained Kafka data for any deliberate recovery investigation.

Browser crashes, missed exit events, an unavailable antiforgery token, or logout
outlasting the two-second flush bound can lose the latest unsaved position.
Keepalive is best effort. A failed save can be attempted again at a later lifecycle
event; there is no automatic browser retry loop or periodic checkpoint.
Durable retry storage and progress checkpoints remain deferred.
