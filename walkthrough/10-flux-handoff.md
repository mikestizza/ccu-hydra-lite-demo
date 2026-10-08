# Layer 10: hand it to Flux

Everything so far was applied by a person with kubectl. That proves each layer; it is not how you run production. Now Git becomes the source of truth and the cluster pulls from it.

```bash
./bootstrap/bootstrap.sh
```

bootstrap.sh keeps the `hydra-system/hydra-secrets` you created in step 02 (same password), adds the `hydra-cluster-vars` ConfigMap (Traefik IP, ingress class) and applies `bootstrap/flux-sync.yaml`:

- a `GitRepository` pointing at github.com/mikestizza/ccu-hydra-lite, branch main, polled every 30 s
- a root `Kustomization` that applies every file under `deploy/flux/envs/` (one per environment; CI adds and removes them)
- two `Kustomization`s for policy, templates first, then constraints

Flux renders `deploy/envs/prod` (the same manifests you just applied by hand, through kustomize) and applies it with server-side apply. It takes ownership of the objects that already exist. One visible change: the ConfigMap is now `hydra-config-<hash>`, so the Deployment rolls once. Nothing else moves.

```bash
kubectl -n hydra-system get gitrepositories,kustomizations
kubectl -n hydra get cm
kubectl -n hydra rollout status deploy/hydra-web
```

From here on, nobody runs kubectl apply for Hydra. A change is a commit.

Housekeeping: the hand-made `hydra-config` ConfigMap from layer 3 is not in Flux's inventory, so it stays behind unused. `kubectl -n hydra delete cm hydra-config` once the rollout finishes.
