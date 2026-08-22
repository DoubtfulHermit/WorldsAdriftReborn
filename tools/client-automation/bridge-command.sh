#!/bin/sh
set -eu

port=${WAREBORN_TEST_BRIDGE_PORT:-47631}
token=${WAREBORN_TEST_BRIDGE_TOKEN:-}

if [ -z "$token" ]; then
  echo "WAREBORN_TEST_BRIDGE_TOKEN is required" >&2
  exit 2
fi
if [ "$#" -eq 0 ]; then
  echo "usage: bridge-command.sh COMMAND" >&2
  exit 2
fi

printf '%s %s\n' "$token" "$*" | timeout 12 socat - "TCP:127.0.0.1:$port"
