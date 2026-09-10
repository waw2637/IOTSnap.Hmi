#!/usr/bin/env bash

set -euo pipefail

. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/opc-plc-common.sh"

require_manifest
copy_manifest

echo "==> Inspecting existing workloads state"
remote_sh "microk8s kubectl -n '$KUBE_NAMESPACE' get deploy,po,svc -o wide || true"

echo "==> Diffing manifest"
set +e
remote_sh "microk8s kubectl diff -f '$REMOTE_MANIFEST'"
DIFF_STATUS=$?
set -e

if [[ $DIFF_STATUS -gt 1 ]]; then
  echo "kubectl diff failed with exit code $DIFF_STATUS" >&2
  exit "$DIFF_STATUS"
fi

echo "==> Applying manifest"
remote_sh "microk8s kubectl apply -f '$REMOTE_MANIFEST'"

echo "==> Waiting for rollout"
remote_sh "microk8s kubectl -n '$KUBE_NAMESPACE' rollout status deploy/'$DEPLOYMENT_NAME' --timeout='$ROLL_OUT_TIMEOUT'"

echo "==> Collecting deployment evidence"
remote_sh <<EOF
set -euo pipefail
microk8s kubectl -n "$KUBE_NAMESPACE" get deploy "$DEPLOYMENT_NAME" -o wide
microk8s kubectl -n "$KUBE_NAMESPACE" get pod -l "$APP_LABEL" -o wide
microk8s kubectl -n "$KUBE_NAMESPACE" get svc "$SERVICE_NAME" -o wide
microk8s kubectl -n "$KUBE_NAMESPACE" get endpoints "$SERVICE_NAME" -o wide
microk8s kubectl -n "$KUBE_NAMESPACE" logs deploy/"$DEPLOYMENT_NAME" --tail=20
rm -f "$REMOTE_MANIFEST"
EOF
