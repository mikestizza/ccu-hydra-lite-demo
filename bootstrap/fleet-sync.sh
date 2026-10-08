#!/usr/bin/env bash
# Point one more cluster at the same folder in Git. Run once per cluster.
#
#   KUBECONFIG=~/stizlab.conf  ./bootstrap/fleet-sync.sh
#   KUBECONFIG=~/stizlab2.conf ./bootstrap/fleet-sync.sh
#
# What it creates, in the NKP project namespace (already present on every cluster
# in the project, along with hydra-secret, because Kommander federates them):
#   - a Flux GitRepository pointing at this repo, branch main
#   - a Flux Kustomization applying ./deploy/envs/project into that namespace
# This is exactly what the Project's Continuous Deployment tab creates for you;
# here it is by hand so you can see it.
set -euo pipefail
NS="${NS:-hydra-lite-6mg6s}"

kubectl get ns "$NS" >/dev/null                      # project namespace must exist (Kommander federates it)
kubectl -n "$NS" get secret hydra-secret >/dev/null  # and the project secret

cat <<EOF | kubectl apply -f -
apiVersion: source.toolkit.fluxcd.io/v1
kind: GitRepository
metadata:
  name: hydra-lite-fleet               # distinct from the Project-UI source name, so the two never collide
  namespace: $NS
spec:
  interval: 30s                        # check the repo every 30 s
  url: https://github.com/mikestizza/ccu-hydra-lite-demo
  ref:
    branch: main
---
apiVersion: kustomize.toolkit.fluxcd.io/v1
kind: Kustomization
metadata:
  name: hydra-lite-fleet
  namespace: $NS
spec:
  interval: 1m                         # re-apply every minute (undoes drift)
  prune: true                          # remove what Git no longer has
  wait: false
  sourceRef:
    kind: GitRepository
    name: hydra-lite-fleet
  path: ./deploy/envs/project          # the same folder on every cluster
  targetNamespace: $NS
EOF

echo
echo "Cluster: $(kubectl config view --minify -o jsonpath='{.clusters[0].name}')  now pulls ./deploy/envs/project into $NS"
echo "Watch:   kubectl -n $NS get kustomization hydra-lite-fleet; kubectl -n $NS get pods"
