# Authentication operations

Identity owns the identity schema in existing PostgreSQL and requires
ConnectionStrings__IdentityDatabase and ConnectionStrings__Redis. Gateway also
requires ConnectionStrings__Redis. Compose obtains credentials from the ignored
root .env. Use a long random alphanumeric STREAMFORGE_REDIS_PASSWORD; connection
string punctuation must be escaped if used. AuthThrottle__LoginPerEmail,
AuthThrottle__LoginPerIp and AuthThrottle__RegisterPerIp override positive 10/60/20
defaults. Rate-limit windows are 15 minutes for login and one hour for registration.

## Local HTTPS

From the repository root:

```powershell
./infra/docker/setup-local-https.ps1 -Trust
# Copy .env.example to .env only if absent; replace placeholders before starting.
docker compose --env-file .env -f infra/docker/compose.yml up -d --build
docker compose --env-file .env -f infra/docker/compose.yml ps
```

The script exports localhost.pem and localhost.key into ignored .certs, mounted
read-only by Nginx. The browser must trust the certificate. Production supplies
its own certificate/key and public hostname/port configuration.
HSTS is emitted for production hostnames and omitted for localhost, where its
automatic upgrade would bypass the redirect between the distinct local ports.

- App/API: https://localhost:8443; HTTP 8080 redirects there.
- Signed media: https://localhost:9443; HTTP MinIO 9000 is not published.
- Angular development: npm start in src/web serves https://localhost:4200.
- pgAdmin and MinIO Console retain local administration ports, separate from
  user-facing traffic; do not expose those administration endpoints publicly.

Nginx preserves signed Host/path/query coordinates and strips Cookie/Authorization
on the media listener. Feed and Playback use PublicUseSsl=true and public endpoint
localhost:9443. Internal MinIO access remains HTTP. The public endpoint is used for URL signing; browser requests reach it through
the separate Nginx TLS listener.

Gateway DataProtection__KeysPath uses the gateway-keys volume. Protect it and share
it across replicas. The Gateway startup script resolves only the Web container's
IP into TrustedProxies__0. Restart Gateway if recreating Web changes its address.
Never trust all networks or arbitrary forwarded headers. Backend services remain
private; their user headers assume a trusted service network.

## Troubleshooting

- 401 after expiry or Redis restart: sign in again; the fixed 24 hours never renews.
- 503 on auth/upload: check Identity and Gateway /health/ready, Redis password and
  availability, and PostgreSQL. Liveness /health is independent. Browsing stays public.
- Account created but sign-in unavailable: recover Redis, then log in. Do not
  delete the committed account as compensation.
- Logout failure: revocation was not confirmed; recover Redis and retry.
- 403: obtain a new antiforgery token, especially after login/logout in another
  tab. Check persistent Gateway key permissions. Never replay an upload automatically.
- 429: respect Retry-After. Check trusted proxy/IP configuration before raising limits.
- Redis memory exhaustion: noeviction rejects writes; increase capacity or fix
  workload growth. Do not FLUSHALL, which would affect future namespaces too.
- Media signature or mixed-content failure: check HTTPS signing endpoints, unchanged
  Host including port, CORS origins, and certificate trust.

Redis starts with 256 MB, noeviction and no persistence. Restarts invalidate all
sessions/counters while PostgreSQL accounts remain. Monitor readiness, auth status
and error-code counts, dependency latency, rate-limit responses and Redis memory.
Never log passwords, hashes, raw session IDs, cookies, DOB or address payloads.
