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

NS=hydra-system

# Auto-detect the Traefik LoadBalancer address and ingress class when not given.
if [ -z "${INGRESS_DOMAIN:-}" ]; then
  TRAEFIK_IP=$(kubectl get svc -A -o json 2>/dev/null | python3 -c '
import json,sys
d=json.load(sys.stdin)
for s in d["items"]:
    if "traefik" in s["metadata"]["name"] and s["spec"].get("type")=="LoadBalancer":
        ing=s["status"].get("loadBalancer",{}).get("ingress",[])
        if ing: print(ing[0].get("ip") or ing[0].get("hostname")); break
' 2>/dev/null || true)
  [ -n "$TRAEFIK_IP" ] || { echo "Could not detect the Traefik LoadBalancer IP. Set INGRESS_DOMAIN=<ip>.sslip.io and re-run."; exit 1; }
  INGRESS_DOMAIN="${TRAEFIK_IP}.sslip.io"
  echo "== Detected Traefik at $TRAEFIK_IP, using INGRESS_DOMAIN=$INGRESS_DOMAIN"
fi
if [ -z "${INGRESS_CLASS:-}" ]; then
  INGRESS_CLASS=$(kubectl get ingressclass -o jsonpath='{.items[?(@.spec.controller=="traefik.io/ingress-controller")].metadata.name}' 2>/dev/null | awk '{print $1}')
  INGRESS_CLASS="${INGRESS_CLASS:-kommander-traefik}"
  echo "== Using ingress class $INGRESS_CLASS"
fi

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
    SQL_SA_PASSWORD=$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Hy"+"".join(secrets.choice(a) for _ in range(18))+"!9")')
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
