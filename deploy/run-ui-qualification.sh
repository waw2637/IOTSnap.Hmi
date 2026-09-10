#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT_DIR/deploy/opc-plc-common.sh"

TEARDOWN_ON_EXIT="${TEARDOWN_ON_EXIT:-1}"
KEEP_DEPLOYMENT_ON_FAILURE="${KEEP_DEPLOYMENT_ON_FAILURE:-1}"
BUILD_BEFORE_TEST="${BUILD_BEFORE_TEST:-1}"
TEST_PROJECT="${TEST_PROJECT:-$ROOT_DIR/tests/IOTSnap.Hmi.Tests/IOTSnap.Hmi.Tests.csproj}"
TEST_FILTER="${TEST_FILTER:-Category=UiPlaywright}"

test_exit_code=0

cleanup() {
  if [[ "$TEARDOWN_ON_EXIT" == "1" && ( "$test_exit_code" == "0" || "$KEEP_DEPLOYMENT_ON_FAILURE" != "1" ) ]]; then
    "$ROOT_DIR/deploy/teardown-opc-plc.sh" >/dev/null 2>&1 || true
  fi
}

trap cleanup EXIT

"$ROOT_DIR/deploy/deploy-opc-plc.sh"

host="${LIVE_OPC_ENDPOINT#opc.tcp://}"
host="${host%%:*}"
port="${LIVE_OPC_ENDPOINT##*:}"
for _ in {1..30}; do
  if bash -lc "echo > /dev/tcp/${host}/${port}" 2>/dev/null; then
    break
  fi
  sleep 1
done

if [[ "$BUILD_BEFORE_TEST" == "1" ]]; then
  dotnet build "$TEST_PROJECT"
fi

IOTSNAP_LIVE_OPC_ENDPOINT="$LIVE_OPC_ENDPOINT" \
  dotnet test "$TEST_PROJECT" --no-build --filter "$TEST_FILTER" || test_exit_code=$?

exit "$test_exit_code"
