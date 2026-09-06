# Engagement contracts

All IDs are UUIDs. Read endpoints and view submission are public. Reaction and
comment mutations require a live session; every mutation, including views,
requires the Gateway antiforgery header.

## Profiles and videos

- `GET /api/users?ids={id}&ids={id}` returns at most 50 `{ id, username }` rows.
- `GET /api/feed/videos/{videoId}` returns one available video with fresh signed
  playback URLs. Feed video responses include nullable `ownerId`.

## Summaries and reactions

- `GET /api/engagement/videos/summaries?ids=...` accepts at most 50 video IDs and
  returns `videoId`, `likeCount`, `dislikeCount`, `viewCount`, and `commentCount`.
- `GET /api/engagement/videos/{videoId}/reaction` returns the authenticated
  user's `like`, `dislike`, or `none`.
- `PUT /api/engagement/videos/{videoId}/reaction` accepts
  `{ "reaction": "like" | "dislike" | "none" }` and returns `202 Accepted`.
  Its response contains the accepted reaction, nullable counts, and
  `countsPending`.

Reaction events use `video-engagement-reactions` and event type
`video.reaction.changed.v1`. Publication failure returns `503` and leaves Redis
unchanged.

## Qualified views

`POST /api/engagement/videos/{videoId}/views` accepts a client-generated UUID in
`{ "viewSessionId": "..." }`. It publishes `video.view.qualified.v1` to
`video-engagement-views`. Duplicate session IDs are counted once. The response is
`202 Accepted` with `counted`, nullable `viewCount`, and `countsPending`.

## Comments

- `GET /api/engagement/videos/{videoId}/comments?limit=20&cursor=...` is public,
  newest-first, and caps `limit` at 50. It returns `items`, `totalCount`, and an
  opaque `nextCursor`.
- `POST /api/engagement/videos/{videoId}/comments` accepts `{ "body": "..." }`.
- `PATCH /api/engagement/comments/{commentId}` accepts `{ "body": "..." }`.
- `DELETE /api/engagement/comments/{commentId}` permanently deletes the comment.

Bodies are trimmed plain text with preserved line breaks and must contain 1–2,000
characters. Only the author may edit or delete a comment.
