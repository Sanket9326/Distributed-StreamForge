# Authentication contracts

Browsers call the HTTPS app origin: `https://localhost:8443` in Compose or
`https://localhost:4200` during Angular development. Identity's private local
port is 5084; browser clients always use Gateway through relative `/api` URLs.

| Endpoint | Request | Success |
| --- | --- | --- |
| GET `/api/auth/csrf` | None | 204; antiforgery cookie and readable XSRF-TOKEN |
| POST `/api/auth/register` | username, email, password, optional dob/address | 201; automatically signs in |
| POST `/api/auth/login` | email/password | 200; replaces current browser session |
| GET `/api/auth/me` | Session cookie | 200; current account state |
| POST `/api/auth/logout` | Session cookie if present | 204 after revocation, even if already absent |

Registration/login requests are JSON. Registration/login/me return:

```json
{
  "user": { "id": "e2c1bb10-4340-452f-9fc6-a68cf4b12457", "username": "creator", "email": "creator@example.test" },
  "expiresAtUtc": "2026-09-06T10:00:00Z"
}
```

Username accepts 3–50 letters, digits, dots, underscores or hyphens. Email must
have email format and be at most 254 characters. Passwords are 15–128 characters,
case-sensitive and never trimmed. Dob is optional ISO YYYY-MM-DD, no later than
today UTC; address is optional and at most 1,000 characters. Username/email lookup
values are trimmed, Unicode-normalized and uppercased invariantly, with database
uniqueness. Password confirmation is checked by the browser only.

Auth responses use Cache-Control: no-store. The HttpOnly session cookie is never
returned in JSON or copied into browser storage, URLs, custom headers, or logs.

## Antiforgery and ownership

Call GET `/api/auth/csrf` before a mutation. Send the readable XSRF-TOKEN cookie
value as X-XSRF-TOKEN together with browser-managed cookies. Angular's built-in
XSRF interceptor handles relative API URLs. Refresh after login/registration/logout
because identity changes. Missing/invalid headers return 403; Gateway never reads
a multipart form to find the token. Feed/playback/completion GETs stay public.

Uploads require a live session. Gateway overwrites X-StreamForge-User-Id for Upload
and X-StreamForge-Client-Ip for Identity and strips cookies before forwarding to
other services. These headers are private service contracts, not browser auth.
New uploads populate existing OwnerId fields in PostgreSQL, MinIO metadata and
video.uploaded.v1; Feed already projects this value. Old videos retain null owners.

## Redis contract v1

Key: `streamforge:identity:sessions:v1:<lowercase SHA-256 hex of UTF-8 session-id>`.
The ID is base64url encoding of 32 random bytes: 43 characters without padding.

```json
{
  "userId": "e2c1bb10-4340-452f-9fc6-a68cf4b12457",
  "createdAtUtc": "2026-09-05T10:00:00Z",
  "expiresAtUtc": "2026-09-06T10:00:00Z"
}
```

Creation assigns a TTL of exactly 86,400 seconds atomically. Reads never renew it
and validate absolute expiry, nonempty user ID, creation time and the 24-hour
interval. Malformed records fail authentication. No profile or credential data
is stored. Identity owns writes; Gateway has a separate reader tested against
Identity's real Redis records. No service implementation assembly is shared.
Rate counters use `streamforge:identity:limits:v1:` with hashed email/IP values.

## Errors

| Status | Code | Response |
| --- | --- | --- |
| 400 | validation_failed, invalid_dob | Correct the form |
| 401 | invalid_credentials | Generic email/password error |
| 401 | session_invalid | Clear local user state; protected operations return to login |
| 403 | csrf_invalid | Refresh the form token and let the user retry |
| 409 | account_exists | Username or email is already registered |
| 429 | rate_limited | Respect Retry-After seconds |
| 503 | session_unavailable, identity_unavailable | Preserve cookies and retry after recovery |
| 503 | account_created_session_unavailable | Account exists; use login after recovery |
| 500 | internal_error | Generic error with server correlation |

Validation errors include field errors; expected failures expose a safe title and
stable code. Login defaults to 10 attempts/email and 60/IP per 15 minutes;
registration defaults to 20/IP/hour. Counters include successful attempts. Logout
failure never claims revocation. Media requests are never automatically replayed.
