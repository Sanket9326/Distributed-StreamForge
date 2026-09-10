# Search contracts

Search reads are public and are exposed only through Gateway. Elasticsearch has
no browser-accessible host port.

## Video suggestions

`GET /api/search/videos/suggestions?q={text}&limit={1..20}`

`q` is trimmed and must contain 1–200 characters. `limit` defaults to 8. The Web
client waits for two characters, debounces input for 250 ms, and cancels an
in-flight request when a newer query is ready.

```json
{
  "items": [
    {
      "videoId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
      "title": "Example title",
      "description": "Example description",
      "hashtags": ["dotnet", "video"]
    }
  ]
}
```

Suggestions rank title matches above hashtag matches and description matches.
Search uses a boosted `multi_match` `bool_prefix` query over the title and
hashtag `search_as_you_type` fields and their generated shingle fields. A search
dependency outage returns `503` Problem Details.

## Search indexing event

Feed publishes `VideoSearchIndexRequestedV1` to `video-search-index`, keyed by
`videoId`, only when both metadata and successful transcoding exist:

```json
{
  "eventId": "24eb75d0-6b40-42dc-84ce-efc75c5186fc",
  "eventType": "video.search.index-requested",
  "eventVersion": 1,
  "occurredAtUtc": "2026-09-10T10:35:00Z",
  "causationEventId": "654c9e39-eece-48a7-a597-f2107bd06f14",
  "correlationId": "43e738f2cbd446f093d5f64a5b01dc01",
  "videoId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "revision": 1,
  "ownerId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "title": "Example title",
  "description": "Example description",
  "hashtags": ["dotnet", "video"],
  "uploadedAtUtc": "2026-09-10T10:30:00Z",
  "availableAtUtc": "2026-09-10T10:34:00Z"
}
```

`videoId` is the Elasticsearch `_id`; `eventId` is trace identity. Search uses
`revision` to accept equal or newer writes and reject older writes. Malformed
contracts and permanent mapping failures are wrapped with their source
topic/partition/offset, reason, and original payload, then published to
`video-search-index-dead-letter` before the source offset is committed.
