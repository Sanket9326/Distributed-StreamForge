#!/bin/sh
set -eu
# Trust only the actual edge container, not the entire Docker subnet.
attempt=0
while :; do
  TrustedProxies__0="$(getent hosts web | awk 'NR == 1 { print $1 }')"
  test -z "$TrustedProxies__0" || break
  attempt=$((attempt + 1))
  test "$attempt" -lt 60 || exit 1
  sleep 1
done
export TrustedProxies__0
exec dotnet StreamForge.Gateway.Api.dll
