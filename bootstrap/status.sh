#!/usr/bin/env bash
# Snapshot of everything Hydra-lite on the cluster. Read-only. Paste the output back.
set -uo pipefail
hr() { printf '\n== %s\n' "$1"; }

hr "flux source + kustomizations (hydra-system)"
kubectl -n hydra-system get gitrepositories,kustomizations 2>&1 | sed 's/^/   /'

hr "flux conditions (why something is not Ready)"
kubectl -n hydra-system get kustomizations -o json 2>/dev/null | python3 -c '
import json,sys
for k in json.load(sys.stdin)["items"]:
    for c in k["status"].get("conditions",[]):
        if c["type"]=="Ready":
            print("   %-22s Ready=%-5s %s" % (k["metadata"]["name"], c["status"], c.get("message","")[:220]))
'

hr "hydra namespaces"
kubectl get ns -l hydra.ccu/managed=true 2>&1 | sed 's/^/   /'

for ns in $(kubectl get ns -l hydra.ccu/managed=true -o jsonpath='{.items[*].metadata.name}' 2>/dev/null); do
  hr "namespace $ns"
  kubectl -n "$ns" get pods,svc,ingress,pvc -o wide 2>&1 | sed 's/^/   /'
  echo "   -- recent events (warnings)"
  kubectl -n "$ns" get events --field-selector type=Warning --sort-by=.lastTimestamp 2>/dev/null | tail -8 | sed 's/^/   /'
  echo "   -- hydra-web log tail"
  kubectl -n "$ns" logs deploy/hydra-web --tail=6 2>/dev/null | sed 's/^/   /'
done

hr "gatekeeper constraints"
kubectl get constrainttemplates 2>/dev/null | sed 's/^/   /'
kubectl get hydrarequiredlimits,hydradisallowlatest 2>/dev/null | sed 's/^/   /'

hr "urls"
DOMAIN=$(kubectl -n hydra-system get cm hydra-cluster-vars -o jsonpath='{.data.INGRESS_DOMAIN}' 2>/dev/null)
for ns in $(kubectl get ns -l hydra.ccu/managed=true -o jsonpath='{.items[*].metadata.name}' 2>/dev/null); do
  H=$(kubectl -n "$ns" get ingress hydra-web -o jsonpath='{.spec.rules[0].host}' 2>/dev/null)
  [ -n "$H" ] && echo "   $ns  ->  http://$H/"
done
echo
echo "Status done. Paste everything above back to Claude."
