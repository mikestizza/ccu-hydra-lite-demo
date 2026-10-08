#!/usr/bin/env bash
# Erase everything Hydra-lite put on the cluster. Back to "cluster just deployed".
# Safe to re-run. Does not touch the Kommander project namespace (hydra-lite-*);
# remove its GitOps source in the Kommander UI if you want that empty too.
set -uo pipefail

echo "== 1. Flux objects (deleting a Kustomization with prune:true garbage-collects what it applied)"
kubectl -n hydra-system delete kustomization hydra-policy-constraints hydra-policy hydra-prod hydra-envs --ignore-not-found --wait=true --timeout=120s
kubectl -n hydra-system delete gitrepository hydra-lite --ignore-not-found

echo "== 2. Namespaces"
kubectl delete ns hydra-system hydra intruder --ignore-not-found --wait=true --timeout=180s
for ns in $(kubectl get ns -l hydra.ccu/managed=true -o jsonpath='{.items[*].metadata.name}' 2>/dev/null); do
  kubectl delete ns "$ns" --ignore-not-found --wait=true --timeout=180s
done

echo "== 3. Cluster-scoped policy"
kubectl delete constrainttemplates hydrarequiredlimits hydradisallowlatest --ignore-not-found

echo "== 4. Anything left?"
kubectl get ns | grep -E '^hydra' || echo "   no hydra namespaces"
kubectl get constrainttemplates 2>/dev/null | grep -i hydra || echo "   no hydra policy"
kubectl get kustomizations -A 2>/dev/null | grep -i hydra || echo "   no hydra Flux objects (project ones excepted)"
echo
echo "Reset complete. Cluster is back to just-deployed for Hydra-lite."
