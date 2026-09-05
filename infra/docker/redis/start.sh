#!/bin/sh
set -eu
# Read credentials without putting them in process arguments.
test -n "$STREAMFORGE_REDIS_PASSWORD"
umask 077
password_hash="$(printf '%s' "$STREAMFORGE_REDIS_PASSWORD" | sha256sum | cut -d ' ' -f 1)"
printf 'user default on #%s ~streamforge:* +@all\n' "$password_hash" > /tmp/streamforge-redis.acl
exec redis-server --aclfile /tmp/streamforge-redis.acl --save '' --appendonly no --maxmemory 256mb --maxmemory-policy noeviction
