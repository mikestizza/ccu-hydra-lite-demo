# Bottom-up walkthrough: from an empty cluster to Hydra-lite, one layer at a time

For the infrastructure and platform team. Every layer is one file, applied by hand, so you can see exactly what lands on the cluster and why. At the end, Git takes over (layer 10) and nobody applies by hand again.

Framing: the developers have containerized the app and tested it in Docker. The two images are in the registry. Our job is the plumbing underneath them.

Have two terminals on the jumphost: one to apply, one running

```bash
watch -n 2 'kubectl -n hydra get pods,svc,pvc,ingress 2>/dev/null'
```

Start state: `./bootstrap/reset.sh` has run. `kubectl get ns | grep hydra` shows nothing but the Kommander project namespace.

| Layer | What lands | Command | What to watch for |
| --- | --- | --- | --- |
| 1 | Namespace | `kubectl apply -f walkthrough/01-namespace.yaml` | `kubectl get ns hydra --show-labels` |
| 2 | Secret | `./walkthrough/02-secret.sh` | `kubectl -n hydra get secret hydra-secret -o yaml` (base64, one key) |
| 3 | ConfigMap | `kubectl apply -f walkthrough/03-config.yaml` | `kubectl -n hydra get cm hydra-config -o yaml` |
| 4 | ServiceAccounts | `kubectl apply -f walkthrough/04-identities.yaml` | `kubectl -n hydra get sa` |
| 5 | Database | `kubectl apply -f walkthrough/05-database.yaml` | PVC Pending then Bound; pod ContainerCreating then Running (30 to 60 s) |
| 6 | API + NetworkPolicy | `kubectl apply -f walkthrough/06-colby-api.yaml` | 2 pods Running in seconds; `kubectl -n hydra get netpol` |
| 7 | Web tier | `kubectl apply -f walkthrough/07-hydra-web.yaml` | pods Running but 0/1 until SQL answers, then 1/1 |
| 8 | Ingress | `kubectl apply -f walkthrough/08-ingress.yaml` | ADDRESS 10.38.207.251 appears; open http://hydra.10.38.207.251.sslip.io/ |
| 9 | Policy | `kubectl apply -f walkthrough/09-policy/a-template-required-limits.yaml -f walkthrough/09-policy/b-template-disallow-latest.yaml`, wait 10 s, `kubectl apply -f walkthrough/09-policy/c-constraints.yaml` | `kubectl get crd \| grep hydra`; then `kubectl apply -f deploy/demo/bad-deployment.yaml` is denied |
| 10 | Flux handoff | `./bootstrap/bootstrap.sh` | Kustomizations Ready; `hydra-config-<hash>`; one rollout |

## Lines per layer

**Layer 1, namespace.** "A namespace is the boundary. Quotas, RBAC, network policy and admission policy all scope to it. Two labels: one opts it into our Gatekeeper rules, the other enforces the baseline pod security standard, so nothing privileged, no host networking, no host paths. Hydra prod, Hydra dev and a feature branch are each one of these."

**Layer 2, secret.** "The only secret this app needs is the SQL password. I'm creating it by hand once, here. It is never in Git, never in an image. In production this is where External Secrets Operator writes it from Parameter Store or Key Vault. The script also puts the same value in hydra-system for Flux, so the handoff in layer 10 is seamless."

**Layer 3, config.** "Everything that is not a secret: environment name, API URL, database host, the banner text. The image is identical in every environment; this ConfigMap is what makes it production. Paul, this is your Parameter Store config side; same shape."

**Layer 4, identities.** "Each workload gets its own ServiceAccount. It is an identity, not a login. Nothing here calls the Kubernetes API so no token is mounted. This is what policies, RBAC and later workload identity attach to. Troy, this is the equivalent of each app having its own service account on a Windows box, except it cannot be used to log in anywhere."

**Layer 5, database.** Apply, then narrate the watch pane: "A StatefulSet, because a database wants a stable name and a stable disk. The PVC goes Pending, then Bound: that is the Nutanix CSI driver carving a volume on the same storage your VMs use. The pod pulls the SQL Server 2022 Linux image from Microsoft and starts; about 40 seconds. Matthew, from the storage side this is a Nutanix volume like any other. For Hydra itself, your SQL and Oracle stay where they are; this is here so the demo is self-contained."

**Layer 6, API and the fence.** "The in-house API. Its Service is ClusterIP: it has a name, colby-api, and an address inside the cluster, and nothing else. No external IP exists. And the NetworkPolicy at the bottom: only pods labeled hydra-web may talk to it, on 8080. Cilium enforces that on every node at the kernel. Jesse, this is the hairpin through the F5 going away. Troy, this is the east-west control you don't have today."

**Layer 7, web tier.** "Two replicas, like WEB01 and WEB02. Config and the secret arrive as environment variables. Watch the pane: the pods go Running but stay 0 of 1 ready until /readyz says SQL is reachable. The Service only sends traffic to ready pods. That is why a rollout never drops a request. Also note: non-root, no capabilities, seccomp. The image runs as user 1654 and cannot escalate."

**Layer 8, front door.** "An Ingress: hostname in, Service out. Traefik, which NKP ships, owns it and terminates TLS. Traefik sits behind a MetalLB address, 10.38.207.251, from the pool the cluster got at creation. That address is what the F5 or the Fortinet VIP forwards to. Watch the ADDRESS column fill in." Open the URL. "There's the app. Eight files, no server was logged into."

**Layer 9, policy.** "Two ConstraintTemplates: each one becomes a new kind on the cluster, and the Rego inside is the rule. Then two Constraints that say: enforce these, deny on violation, in every namespace carrying our label." Apply the bad deployment. "Rejected at the door, three reasons in plain English. Jesse asked who writes these and who reviews them: platform writes them, in Git, through a PR. The app team sees them only when they break one."

**Layer 10, handoff.** "Everything so far was me with kubectl. That proves each layer; it is not how you run production. Now Git becomes the source of truth." Run bootstrap. "A GitRepository pointing at the repo, polled every 30 seconds, and Kustomizations that render the same manifests you just saw, through kustomize, and apply them. Flux takes ownership of what already exists. One visible change: the ConfigMap gets a content hash in its name, so the Deployment rolls once. From here on nobody runs kubectl apply for Hydra. A change is a commit, and the Git log is the change record."

Then the developer half of the story: push a branch (ephemeral environment), change the banner (config-only roll), the NKP project for the app team.
