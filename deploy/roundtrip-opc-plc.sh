#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CYCLES="${CYCLES:-2}"

if ! [[ "$CYCLES" =~ ^[0-9]+$ ]] || [[ "$CYCLES" -lt 1 ]]; then
  echo "CYCLES must be a positive integer." >&2
  exit 1
fi

for cycle in $(seq 1 "$CYCLES"); do
  echo "==== Round-trip cycle $cycle/$CYCLES: deploy ===="
  "$ROOT_DIR/deploy/deploy-opc-plc.sh"

  echo "==== Round-trip cycle $cycle/$CYCLES: teardown ===="
  "$ROOT_DIR/deploy/teardown-opc-plc.sh"
done

echo "Round-trip verification completed successfully across $CYCLES cycle(s)."
