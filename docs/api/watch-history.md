# Watch history

All endpoints are under `/api/engagement/watch-history` through Gateway and use
the authenticated user exclusively. Caller-supplied identity headers are replaced.
A live session is required for reads and writes; writes also require
`X-XSRF-TOKEN`. Responses use `Cache-Control: no-store`.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/` | `{ items, nextCursor }`; default limit 20, valid limits 1–50 |
| GET | `/{videoId}` | Progress item, or `204 No Content` if absent |
| PUT | `/{videoId}` | `202 Accepted` after Kafka acknowledgement |

PUT requires all three fields, including an explicit boolean:

```json
{ "positionMs": 120000, "durationMs": 600000, "isCompleted": false }
```

The video UUID must be nonempty and the video available. Require
`0 <= positionMs <= durationMs <= 9007199254740991` and `durationMs > 0`.
Positions are integer milliseconds. `isCompleted` is set on playback completion;
opening a completed video starts at zero, while its history retains final progress.

Example accepted response:

```json
{
  "progress": {
    "videoId": "40000000-0000-0000-0000-000000000004",
    "positionMs": 120000,
    "durationMs": 600000,
    "isCompleted": false,
    "createdAtUtc": "2026-09-15T12:00:00Z",
    "updatedAtUtc": "2026-09-15T12:05:00Z",
    "sourcePartition": 1,
    "sourceOffset": "9007199254740993"
  },
  "cachePending": false
}
```

Single-video reads and list items use the same `progress` shape without the
wrapper. `202` confirms Kafka acceptance, not PostgreSQL completion.
`cachePending: true` means Redis could not be updated after publication. The
consumer will reconcile it. A response can contain newer cached progress than
the caller's event; clients compare offsets only within the same partition.
Offsets are decimal strings and require exact integer comparison.

Lists order by `updatedAtUtc DESC, videoId DESC`. Pass the returned opaque
`cursor` unchanged to fetch another page. Cursors are bound to the current user
and history namespace; malformed or cross-user cursors return `400`. Concurrent
progress changes can move entries between pages. An empty history is
`{ "items": [], "nextCursor": null }`. Resolve video metadata through Feed,
preserving unavailable entries rather than silently removing their history.

`401` requires login, `403` means antiforgery failed, and `400` means invalid
input/cursor. An unavailable video returns `404`; dependency failures return
`503`. Network/publication timeouts are uncertain outcomes. Background save or
resume failures must not interrupt public playback. Redis session failure can
prevent authentication even when PostgreSQL is available.

## Kafka contracts

`UserWatchProgressSavedV1` contains `eventId`, `eventType`
`user.watch-progress.saved.v1`, `eventVersion: 1`, `occurredAtUtc`, `userId`,
`videoId`, `positionMs`, `durationMs`, `isCompleted`, `correlationId`, and the
canonical lower-case `key`. The broker key is `userUuid:videoUuid`.
The consumer group is `streamforge-engagement-watch-history-v1`.

Retries publish the unchanged payload to the same topic and partition, with
`watch-history-retry-v1` containing the full retry envelope. Its original offset
is a decimal string and must precede the new transport offset. The event ID,
key, payload, destination, and partition must agree with the record. Original
headers are preserved without nesting prior retry headers.

Dead-letter events use `user.watch-history.dead-letter.v1`, version 1, and
include their own event ID/time, source topic/partition/offset, source key,
reason, raw payload, and optional retry envelope. Reasons are
`invalid_watch_history_event` or `processing_retries_exhausted`.

The [ADR](../architecture/decisions/0010-watch-history-and-resume.md) defines
ordering, scheduler limits, and the accepted Redis restart-loss limitation.
