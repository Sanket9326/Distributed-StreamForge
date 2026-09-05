# 0006: Identity-owned accounts and Redis browser sessions

- Status: Accepted
- Date: 2026-09-05
- Supersedes: browser-facing HTTP in ADR 0001 and HTTP media exposure in ADR 0005

## Decision

Identity is a separate service owning `identity.users` and its EF migrations in
the existing PostgreSQL instance. Credentials use ASP.NET Core Identity V3 hashing
with 600,000 PBKDF2-HMAC-SHA512 iterations and per-password salts. Normalized email
and username have unique indexes. Date of birth and address are optional and
never included in session values or account responses.

Registration and login generate a 256-bit random secret delivered only through
`__Host-streamforge-session` with Secure, HttpOnly, SameSite=Strict, Path=/ and no
Domain. The browser cookie persists for a fixed 24 hours. Redis stores the hashed
identifier and only user ID/UTC times. Creation, TTL assignment and revocation of
the browser's previous session are one atomic operation. Other devices remain
logged in. Logout succeeds only after confirmed revocation.

Gateway reads Redis for every protected request without caching or renewal,
removes supplied `X-StreamForge-*` headers, and forwards verified identity to
Upload. Upload rejects missing identity before reading media. Existing OwnerId
fields in the database, object metadata and version-one event are populated;
old records remain nullable without cross-service foreign keys or backfill.

Feed, playback and completion events stay public and do not resolve Redis.
Missing/invalid/expired sessions return 401 and cookie deletion; dependency
failure returns 503 without deleting the cookie. Accepted uploads can finish
after expiry. ASP.NET Core antiforgery tokens protect all API mutations, including
credentials and uploads, exclusively through a header before body streaming.
Gateway Data Protection keys persist in a private volume. Redis counters limit
login and registration attempts across Identity replicas.

Nginx terminates TLS for app/API and signed media. The media listener preserves
signature coordinates and strips cookies. Private services retain HTTP. Only
the actual Web proxy IP and loopback development proxies are trusted for scheme
and client-IP headers. Backend and Redis ports are not published. This assumes
a trusted service network; untrusted-network deployments need authenticated
service transport before exposure.

## Consequences

Redis is reusable, namespaced infrastructure with authentication, noeviction,
256 MB initial capacity and no persistence. Redis restarts sign out users;
future workloads must revisit capacity and durability deliberately.

There is no distributed PostgreSQL/Redis transaction. If an account commits
before session creation fails, it remains usable through later login. The client
receives `account_created_session_unavailable` and no newly issued cookie.

Session secrets never enter JavaScript storage or media services. Upload
notifications are scoped per user and their watchers close on logout.
Production supplies certificates, protects/shares Gateway Data Protection keys,
and should apply committed migrations during deployment. Local startup applies
Identity migrations before accepting requests.

Recovery, email verification, MFA, social login, roles, profile editing, account
deletion and logout-all-devices are deferred. Forgot password is visibly disabled.

See [contracts](../../api/authentication.md) and
[operations](../../operations/runbooks/authentication.md).
