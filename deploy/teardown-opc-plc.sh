#!/usr/bin/env bash

set -euo pipefail

. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/opc-plc-common.sh"

require_manifest
copy_manifest

echo "==> Inspecting existing opc-plc resources"
remote_sh "microk8s kubectl -n '$KUBE_NAMESPACE' get deploy,po,svc,endpoints -l '$APP_LABEL' -o wide || true"

echo "==> Deleting manifest resources"
remote_sh "microk8s kubectl delete -f '$REMOTE_MANIFEST' --ignore-not-found"

echo "==> Waiting for opc-plc resources to terminate"
remote_sh <<EOF
set -euo pipefail
timeout 180 bash -lc '
  while microk8s kubectl -n "$KUBE_NAMESPACE" get deploy,po,svc,endpoints -l "$APP_LABEL" --ignore-not-found | grep -q "$DEPLOYMENT_NAME"; do
    sleep 2
  done
'
microk8s kubectl -n "$KUBE_NAMESPACE" get deploy,po,svc,endpoints -l "$APP_LABEL" -o wide || true
rm -f "$REMOTE_MANIFEST"
EOF
