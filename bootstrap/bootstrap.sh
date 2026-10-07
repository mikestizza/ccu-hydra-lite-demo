#!/usr/bin/env bash
# One-time cluster bootstrap for the Hydra-lite demo.
#
# Creates:
#   - namespace hydra-system
#   - Secret hydra-secrets       (SQL SA password; never stored in Git)
#   - ConfigMap hydra-cluster-vars (INGRESS_DOMAIN, INGRESS_CLASS)
#   - Flux GitRepository + root Kustomizations pointing at this repo
#
# After this, Flux owns the rest. Re-running is safe (idempotent).
#
# Usage:
#   INGRESS_DOMAIN=10.38.207.150.sslip.io ./bootstrap/bootstrap.sh
#   INGRESS_DOMAIN=... INGRESS_CLASS=kommander-traefik SQL_SA_PASSWORD='...' ./bootstrap/bootstrap.sh
set -euo pipefail
cd "$(dirname "$0")/.."

: "${INGRESS_DOMAIN:?Set INGRESS_DOMAIN, e.g. INGRESS_DOMAIN=10.38.207.150.sslip.io (the Traefik LoadBalancer IP + .sslip.io)}"
INGRESS_CLASS="${INGRESS_CLASS:-kommander-traefik}"
NS=hydra-system

echo "== Checking cluster and Flux CRDs"
kubectl cluster-info | head -1
kubectl get crd kustomizations.kustomize.toolkit.fluxcd.io gitrepositories.source.toolkit.fluxcd.io >/dev/null \
  || { echo "Flux CRDs not found. On NKP, Flux ships with Kommander (namespace kommander-flux). Is this the right kubeconfig?"; exit 1; }

echo "== Namespace $NS"
kubectl create namespace "$NS" --dry-run=client -o yaml | kubectl apply -f -

echo "== SQL SA password"
if kubectl -n "$NS" get secret hydra-secrets >/dev/null 2>&1 && [ -z "${SQL_SA_PASSWORD:-}" ]; then
  echo "   hydra-secrets already exists, keeping it"
else
  if [ -z "${SQL_SA_PASSWORD:-}" ]; then
    # SQL Server wants 8+ chars with upper, lower, digit and symbol.
    SQL_SA_PASSWORD="Hy$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 18)!9"
    echo "   generated a random SA password (stored only in the cluster Secret)"
  fi
  kubectl -n "$NS" create secret generic hydra-secrets \
    --from-literal=SQL_SA_PASSWORD="$SQL_SA_PASSWORD" \
    --dry-run=client -o yaml | kubectl apply -f -
fi

echo "== Cluster variables"
kubectl -n "$NS" create configmap hydra-cluster-vars \
  --from-literal=INGRESS_DOMAIN="$INGRESS_DOMAIN" \
  --from-literal=INGRESS_CLASS="$INGRESS_CLASS" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "== Flux sync objects"
kubectl apply -f bootstrap/flux-sync.yaml

echo
echo "Bootstrap complete. Flux will reconcile within about a minute."
echo "Watch:   kubectl -n $NS get gitrepositories,kustomizations"
echo "App:     kubectl -n hydra get pods,svc,ingress"
echo "URL:     http://hydra.${INGRESS_DOMAIN}/"
