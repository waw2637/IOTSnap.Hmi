#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG_PATH="${CONFIG_PATH:-$ROOT_DIR/deploy/opc-plc.env}"
if [[ -f "$CONFIG_PATH" ]]; then
  # Shared shell/MCP-facing configuration surface for the harness.
  # shellcheck disable=SC1090
  source "$CONFIG_PATH"
fi

MANIFEST_PATH="${MANIFEST_PATH:-$ROOT_DIR/deploy/opc-plc-test.yaml}"
CP1_HOST="${CP1_HOST:-192.168.1.26}"
CP1_USER="${CP1_USER:-williamwatson}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/cp1_ed25519}"
REMOTE_MANIFEST="${REMOTE_MANIFEST:-/tmp/opc-plc-test.yaml}"
KUBE_NAMESPACE="${KUBE_NAMESPACE:-workloads}"
APP_LABEL="${APP_LABEL:-app.kubernetes.io/name=opc-plc}"
DEPLOYMENT_NAME="${DEPLOYMENT_NAME:-opc-plc}"
SERVICE_NAME="${SERVICE_NAME:-opc-plc}"
LIVE_OPC_ENDPOINT="${LIVE_OPC_ENDPOINT:-opc.tcp://192.168.1.26:50000}"
ROLL_OUT_TIMEOUT="${ROLL_OUT_TIMEOUT:-180s}"
SSH_OPTS=(
  -o BatchMode=yes
  -o ConnectTimeout=10
  -o IdentitiesOnly=yes
  -o StrictHostKeyChecking=accept-new
  -i "$SSH_KEY"
)
REMOTE_TARGET="$CP1_USER@$CP1_HOST"

require_manifest() {
  if [[ ! -f "$MANIFEST_PATH" ]]; then
    echo "Manifest not found: $MANIFEST_PATH" >&2
    exit 1
  fi
}

copy_manifest() {
  echo "==> Copying manifest to $REMOTE_TARGET:$REMOTE_MANIFEST"
  scp "${SSH_OPTS[@]}" "$MANIFEST_PATH" "$REMOTE_TARGET:$REMOTE_MANIFEST"
}

remote_sh() {
  ssh "${SSH_OPTS[@]}" "$REMOTE_TARGET" "$@"
}

cleanup_remote_manifest() {
  remote_sh "rm -f '$REMOTE_MANIFEST'"
}
