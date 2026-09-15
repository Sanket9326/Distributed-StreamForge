# Subscriptions

All paths below are under `/api/engagement/subscriptions` through Gateway.
Every endpoint requires a live session; all mutations require `X-XSRF-TOKEN`.
Gateway replaces caller-supplied identity headers. Lists and status responses
use `Cache-Control: no-store` and are private to the authenticated user.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | / | My subscriptions |
| GET | /subscribers | My subscribers |
| GET | /status?creatorIds=UUID&creatorIds=UUID | Outgoing state for at most 50 IDs |
| PUT | /{creatorId} | Subscribe; validates the account through Identity |
| DELETE | /{creatorId} | Unsubscribe |
| DELETE | /subscribers/{subscriberId} | Remove this user's incoming subscription |

Mutations have no request body requirement. A caller cannot supply the acting
user or select someone else's list. Self-subscription returns 400. Missing
creator returns 404 on Subscribe. Absence on removal/unsubscribe is idempotent.

Lists accept `limit` (default 20, range 1–50) and an opaque `cursor`. They use
newest subscription date then counterpart UUID descending:

```json
{
  "items": [{
    "userId": "20000000-0000-0000-0000-000000000002",
    "createdAtUtc": "2026-09-14T12:00:00Z",
    "sourcePartition": 1,
    "sourceOffset": "9007199254740993"
  }],
  "nextCursor": null
}
```

Resolve usernames with `GET /api/users?ids=UUID`. A page's userId is the creator
for My subscriptions and the subscriber for My subscribers. Pagination is live:
concurrent subscription changes can move a relationship between pages. Invalid
or cross-user/direction cursors return 400.

Status returns an array of `{ creatorId, isActive, sourcePartition,
sourceOffset }`. Unknown relationships have false state and null position.
Offsets are decimal strings; compare only within the same partition.

An acknowledged mutation returns **202 Accepted**:

```json
{
  "subscriberId": "10000000-0000-0000-0000-000000000001",
  "creatorId": "20000000-0000-0000-0000-000000000002",
  "isActive": true,
  "cachePending": false,
  "sourcePartition": 1,
  "sourceOffset": "9007199254740993"
}
```

202 confirms Kafka acceptance, not PostgreSQL completion. `cachePending: true`
means the consumer must repair Redis. A delayed API callback may return a newer
cached relationship position than its own event. Clients retain accepted state
and ignore older fallback reads. Network failures and publication timeouts are
uncertain; display confirmation as unavailable and re-fetch.

401 means sign-in is required; 403 means antiforgery failed; 400 and 404 are
definite validation rejections. Dependency failures return 503. Authentication
cannot fall back to PostgreSQL when Redis sessions are unavailable.

## Event

`UserSubscriptionChangedV1` has eventId, eventType
`user.subscription.changed.v1`, eventVersion 1, occurredAtUtc, subscriberId,
creatorId, actorId, isActive, correlationId and a canonical pair key. Actor must
be the subscriber, or the creator when removing an incoming relationship.
The broker record key always uses subscriber first, including creator-initiated
removal. An incoming removal never publishes for the reverse pair.

