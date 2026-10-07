#!/usr/bin/env bash
# Read-only preflight. Run against the NKP workload cluster kubeconfig and
# paste the output back. Nothing here changes the cluster.
set -uo pipefail

hr() { printf '\n== %s\n' "$1"; }

hr "cluster"
kubectl version 2>/dev/null | sed 's/^/   /'
kubectl get nodes -o wide 2>/dev/null | sed 's/^/   /'

hr "storage classes (need a default for the SQL Server PVC)"
kubectl get storageclass 2>/dev/null | sed 's/^/   /'

hr "flux (CRDs + controllers)"
kubectl get crd 2>/dev/null | grep -E 'toolkit.fluxcd.io' | awk '{print "   "$1}'
kubectl get deploy -A 2>/dev/null | grep -Ei 'source-controller|kustomize-controller|helm-controller' | sed 's/^/   /'
echo "   kustomize-controller args:"
kubectl get deploy -A -o json 2>/dev/null | python3 -c '
import json,sys
d=json.load(sys.stdin)
for i in d["items"]:
    if i["metadata"]["name"]=="kustomize-controller":
        for c in i["spec"]["template"]["spec"]["containers"]:
            print("     ns="+i["metadata"]["namespace"], " ".join(c.get("args",[])))
' 2>/dev/null

hr "ingress (Traefik) and its LoadBalancer IP"
kubectl get ingressclass 2>/dev/null | sed 's/^/   /'
kubectl get svc -A 2>/dev/null | grep -Ei 'traefik' | sed 's/^/   /'
TRAEFIK_IP=$(kubectl get svc -A -o json 2>/dev/null | python3 -c '
import json,sys
d=json.load(sys.stdin)
for s in d["items"]:
    if "traefik" in s["metadata"]["name"] and s["spec"].get("type")=="LoadBalancer":
        ing=s["status"].get("loadBalancer",{}).get("ingress",[])
        if ing: print(ing[0].get("ip") or ing[0].get("hostname")); break
' 2>/dev/null)
echo "   detected Traefik LB address: ${TRAEFIK_IP:-NOT FOUND}"
[ -n "${TRAEFIK_IP:-}" ] && echo "   suggested INGRESS_DOMAIN=${TRAEFIK_IP}.sslip.io"

hr "metallb / load balancer pools"
kubectl get ipaddresspools.metallb.io -A 2>/dev/null | sed 's/^/   /' || echo "   (no MetalLB IPAddressPool CRD)"

hr "gatekeeper"
kubectl get crd constrainttemplates.templates.gatekeeper.sh 2>/dev/null | sed 's/^/   /' || echo "   NOT installed (policy demo will be skipped unless enabled in Kommander)"
kubectl get pods -A 2>/dev/null | grep -i gatekeeper | sed 's/^/   /'

hr "observability / cost (Kommander platform apps)"
kubectl get pods -A 2>/dev/null | grep -Ei 'grafana|prometheus|kubecost|cost-analyzer' | awk '{print "   "$1"/"$2"  "$4}'

hr "cni (NetworkPolicy enforcement)"
kubectl get pods -n kube-system 2>/dev/null | grep -Ei 'cilium|calico' | awk '{print "   "$1"  "$3}'

hr "can the cluster reach GHCR? (one-off pull test, 60s max)"
kubectl run ghcr-pull-test --rm -i --restart=Never --image=ghcr.io/mikestizza/colby-api:main \
  --overrides='{"spec":{"containers":[{"name":"t","image":"ghcr.io/mikestizza/colby-api:main","command":["dotnet","--info"],"resources":{"limits":{"cpu":"200m","memory":"128Mi"}}}]}}' \
  --pod-running-timeout=60s 2>&1 | head -5 | sed 's/^/   /'

hr "existing hydra objects (should be empty before bootstrap)"
kubectl get ns 2>/dev/null | grep -E '^hydra' | sed 's/^/   /' || echo "   none"

echo
echo "Preflight done. Paste everything above back to Claude."
