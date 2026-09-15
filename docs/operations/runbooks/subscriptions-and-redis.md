# Subscriptions and Lua Redis operations

## Rollout

1. Deploy the Lua conversion to Identity and Gateway and verify session lookup,
   rotation, fixed expiry, logout and throttling. Existing session keys/JSON and
   cookies require no flush or migration.
2. Drain and stop all legacy Engagement writers before deploying the guarded
   Redis implementation. Do not overlap old rebuilding code with new writers.
   Deploy Engagement, applying migration `20260914120000_AddSubscriptions`.
   No subscription backfill is needed. Existing reaction/count keys retain their
   formats; rebuild leases and generation keys are additive.
3. Verify Engagement readiness, the new topics, and the consumer. Deploy Web
   with the guarded `/subscriptions` route and watch-page button.

Engagement configuration adds `Identity:BaseUrl` (local default
`http://localhost:5084`; Compose `http://identity-service:8080`),
`Kafka:SubscriptionTopic`, `Kafka:SubscriptionDeadLetterTopic`, and
`Kafka:SubscriptionConsumerGroupId`. Defaults are
`user-engagement-subscriptions`, `user-engagement-subscriptions-dead-letter`,
and `streamforge-engagement-subscriptions-v1`.
Existing `Kafka:PartitionCount` determines both new topics. The subscription
topic must keep that count for its lifetime; readiness rejects a mismatch.
Do not recreate Kafka topics while retaining PostgreSQL/Redis offset fences.

The existing standalone Redis deployment and ACL remain in use. Its application
principal must support EVAL/EVALSHA and the commands used by packaged scripts.
Readiness runs a read-only script. A Redis Cluster rollout is out of scope.

## Diagnosis and recovery

- Inspect `redis.script.duration` (milliseconds) in meters
  `StreamForge.Identity.Redis`, `StreamForge.Gateway.Redis`,
  `StreamForge.Engagement.Redis`, tagged with script and outcome. Executors
  also log safe script name, elapsed time and exception category. Do not enable
  logging of arguments, session records or full Redis exceptions containing
  credentials/keys when collecting diagnostics.
- Monitor subscription consumer lag, PostgreSQL receipts, dead-letter traffic,
  accepted-with-cache-pending warnings and Redis memory. Pending state and
  inactive version fences have no TTL. Standalone noeviction memory exhaustion
  needs capacity recovery; it must not cause pending state to be discarded.
- After Kafka acceptance, a Redis failure is repaired by the consumer before
  offset commit. A PostgreSQL commit followed by Redis failure is safe to replay:
  duplicate delivery loads durable state and repairs both relationship directions.
- Script flush or restart requires no application restart. The Redis client
  recovers its script cache. Mutations are not blindly retried on ambiguous
  script timeouts. Logout can execute under memory pressure; authentication
  reads retain read-only flags.
- A failed rebuild cannot mark its list complete. Abandoned leases expire after
  30 seconds. A read can reattempt hydration; newer accepted state remains.
  Do not manually delete a user hash/index while retaining its ready marker.
- If Redis is unavailable after successful authentication, reads can use the
  durable PostgreSQL snapshot. When Gateway cannot authenticate against Redis,
  protected requests remain 503; this intentionally preserves the session boundary.
- Dead-letter acknowledgement occurs only after Kafka publication. Failed DLQ
  publication leaves the source offset uncommitted. Diagnose the producer,
  correct the event and explicitly republish with a new event ID and the same
  canonical directed pair key. V1 has no automatic DLQ replay.
- Avoid rolling back to legacy Engagement writers after guarded rebuilding starts.
  Retain the additive subscriptions table on application rollback. Stop new
  subscription mutations before disabling their consumer.

## Verification

Standard checks remain:

```powershell
dotnet restore StreamForge.slnx
dotnet build StreamForge.slnx --no-restore
dotnet test StreamForge.slnx --no-build --no-restore
```

The integration suites run isolated PostgreSQL, Redis and Kafka containers.
Identity verifies script-cache recovery, fixed windows and logout under memory
pressure. Engagement covers delayed callbacks, duplicate repair, tombstones,
pagination, concurrent rebuilding, wrong key types, bounded batches, Redis
restart, post-ack cache failure and dead-letter publication.

The Angular tests exercise optimistic state, uncertain/definite outcomes, stale
responses and account changes. `tests/web/e2e/subscriptions.spec.ts` uses
controlled HTTP responses in Chromium for lists, buttons, responsive layout and
reduced motion. Other browser suites exercise the running HTTPS topology.

This workspace was verified using an installed compatible .NET 10 SDK and a
local Node 24.15 runtime without changing the repository SDK pin. Docker and
browser/compiler child processes require normal local process permissions.

### Verification recorded on 2026-09-15

- Backend build: zero warnings and errors.
- Latest results across 14 backend suites: 184 passed, zero failed. Engagement
  integration and unit suites were rerun after the final embedded-script change.
- Angular production build passed; all 49 tests across 17 test files passed.
- All three Chromium subscription checks passed with controlled API responses.
  Desktop and mobile screenshots are in `artifacts/screenshots/subscriptions-*.png`.
- Backend integration tests used real PostgreSQL, Redis, Kafka and other
  service dependencies in isolated containers. HTTP subscription tests paused
  consumers and stubbed Identity profiles; Identity's profile API has its own
  real service integration coverage.
- Redis packaging and the source audit passed. TRX results and an aggregate
  JSON summary are under `artifacts/verification/` (ignored local artifacts).
- Compose syntax was validated with temporary placeholder environment values.
  The deployment topology and the existing HTTPS authentication/media browser
  suites were not started as part of this verification.

The installed SDK used here was `10.0.401`, invoked explicitly via its
`dotnet.dll`; ordinary CLI commands still require an SDK satisfying
`global.json` (`10.0.303`, latest patch). The compatible Node executable is
`artifacts/tooling/node.exe` (24.15.0). No machine-wide SDK, Node or deployment
configuration was changed.
