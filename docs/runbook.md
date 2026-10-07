# Hydra-lite demo runbook

Session: Thu Oct 8, 2026, 11:00 AM Pacific. 30 min demo, hour blocked. Recording on.

Fill in before the session:

| Item | Value |
|---|---|
| Workload cluster | `stizlab` (workspace `stizlab-w966n`, 3 control plane + 4 workers, Ubuntu 24.04, K8s 1.34, Cilium) |
| Traefik LB / INGRESS_DOMAIN | `10.38.207.251.sslip.io` |
| Prod URL (platform GitOps, ns `hydra`) | http://hydra.10.38.207.251.sslip.io/ |
| Project URL (Kommander project CD, ns `hydra-lite-6mg6s`) | http://hydra-project.10.38.207.251.sslip.io/ |
| Kommander project | https://nkp-ultimate.nkpdemo.com/dkp/kommander/dashboard/workspace/stizlab-w966n/projects/hydra-lite-6mg6s/cd |
| Grafana / OpenCost | Kommander → Workspace `StizLab` → Applications → open Grafana and OpenCost dashboards (fill exact links the morning of) |
| Repo | https://github.com/mikestizza/ccu-hydra-lite-demo |
| Actions | https://github.com/mikestizza/ccu-hydra-lite-demo/actions |
| Branches (for the live delete) | https://github.com/mikestizza/ccu-hydra-lite-demo/branches |

Jumphost: `nkp-boot`, `export KUBECONFIG=~/stizlab.conf`, repo cloned at `~/ccu-hydra-lite-demo` (run `git pull` the morning of).

Note: https works too (Traefik default cert, browser will warn). If a browser auto-upgrades to https and shows a cert warning, click through or type http:// explicitly.

Have open in tabs before you start: prod URL (Dashboard page), project URL, the repo, Actions, the Kommander project page, Grafana, and a terminal on the jumphost with `KUBECONFIG` set and `watch -n 2 'kubectl get pods -n hydra; echo; kubectl get ns | grep hydra-'` running.

Rehearsed 10/7: every beat below was run against stizlab and worked. Timings are from that run.

---

## 0. Pre-flight (morning of)

```bash
cd ~/ccu-hydra-lite-demo && git pull
./bootstrap/status.sh            # everything Ready, pods Running, URLs answer
kubectl get ns | grep hydra-     # expect only hydra-lite-6mg6s and hydra-system
git branch -r | grep -v main     # no leftover branches
kubectl -n hydra-lite-6mg6s get pods   # project path healthy too
```

If an ephemeral env is left over: `git push origin --delete <branch>` and wait a minute.

---

## 1. Recap and framing (3 min)

Their diagram on screen. Talk track:

- Circle the two web servers and the Hangfire box: "This is Phase 1. The web tier."
- Circle the Colby API / UKG arrows: "These are the in-house hops Jesse mentioned that today bounce through the F5."
- "I built a small app with the same shape as Hydra. .NET 10, SQL Server, calls an in-house API, runs background jobs in-process. It is not Hydra, but everything I show you today works the same way for Hydra."

## 2. The app, running on NKP (4 min)

Browser: prod URL.

- Dashboard: point at the KPI tiles and the "served by pod" callout. Refresh twice: pod name alternates. "Two replicas, like WEB01 and WEB02."
- Platform page: Framework = .NET 10, OS = Linux, web server = Kestrel, process user = non-root. "Same code. No IIS. No Windows license on this box."
- Terminal: `kubectl -n hydra get pods -o wide` and `kubectl -n hydra get ingress`
- Repo: open `src/HydraWeb/Dockerfile`. "Fifteen lines. This is the whole containerization of the web tier. Casey has already done a version of this."

Talk track on their real dependencies: Entra ID is HTTPS calls, the Oracle managed driver and SqlClient are NuGet packages, no native client in the image.

## 3. East-west traffic (4 min)

Browser: Members, search "Okafor", click a member.

- Accounts panel: "Fetched from `http://colby-api/...` in N ms. Colby pod saw the call arrive from the web pod's cluster IP. This hop never touched the load balancer."
- Terminal: `kubectl -n hydra get svc colby-api` (ClusterIP, no external address)
- Terminal: `kubectl -n hydra get networkpolicy colby-api-ingress -o yaml | head -30`
- Terminal:
  ```bash
  kubectl apply -f deploy/demo/netpol-probes.yaml
  kubectl -n hydra logs -f probe-allowed       # RESULT: HTTP 200 in 0.004s  (rehearsal: 4 ms)
  kubectl -n intruder logs -f probe-denied     # RESULT: BLOCKED by NetworkPolicy (timeout)  (takes ~6 s)
  kubectl delete -f deploy/demo/netpol-probes.yaml
  ```
  Same image, same command. Only the label and namespace differ. Troy's moment.

## 4. Ephemeral environment from a branch (6 min)

Do this from the laptop, in the repo:

```bash
git checkout -b feature/member-alerts
# edit src/HydraWeb/Pages/Index.cshtml: change the h1 text to "Good day, Member Services (member alerts preview)"
git commit -am "Member alerts preview" && git push -u origin feature/member-alerts
```

- Actions tab: build runs (rehearsal: about 90 s with cache, namespace Running ~2 min after push). Narrate while it builds: "CI builds the image, tags it with the branch and commit, then writes a tiny overlay into the repo. Nobody logs into a server."
- Repo: show the CI commit adding `deploy/envs/feature-member-alerts/` and `deploy/flux/envs/feature-member-alerts.yaml`.
- Terminal: `kubectl get ns | grep hydra-` and `kubectl -n hydra-feature-member-alerts get pods` (SQL Server takes ~40s).
- Browser: `http://hydra-feature-member-alerts.<domain>/` next to prod. Purple header, yellow banner, changed title. Own SQL Server, own data.
- "Delete the branch, the namespace goes away." Delete it on the Branches page (trash icon) or `git push origin --delete feature/member-alerts`. Show the `teardown` Action and `kubectl get ns | grep hydra-` a minute later (or come back to it at the end).

Jesse's moment. Tie back: "This is the PR preview you said you wanted and only have with Vercel today."

## 5. Patching and rollout (3 min)

Have a second trivial change ready on main (a comment, or the footer text). Merge or push it:

```bash
git checkout main && git pull
# edit, commit, push
```

- Actions builds, promote step bumps `deploy/envs/prod/kustomization.yaml`.
- Terminal: `kubectl -n hydra rollout status deploy/hydra-web` while refreshing the browser. Zero failed requests, `maxUnavailable: 0`.
- "Patching the OS is the same motion. Rebuild the image on a new base, push, roll. WEB01 and WEB02 are cattle."

If time is short, skip this and describe it; step 4 already proved the pipeline.

## 6. Day 2 in Kommander (7 min)

- **Grafana**: namespace `hydra` dashboard: CPU, memory, pod restarts, network. Point out the ephemeral namespace showed up with zero config.
- **OpenCost** (NKP's cost tool on this build): cost by namespace. "The throwaway branch environment cost cents. You can see what Hydra costs to run."
- **Gatekeeper**:
  ```bash
  kubectl apply -f deploy/demo/bad-deployment.yaml
  ```
  Expected (verified): `admission webhook "validation.gatekeeper.sh" denied the request` with three lines: must set `resources.limits.cpu`, must set `resources.limits.memory`, `:latest` not allowed. "Policy as code, in Git, reviewed like code. That answers 'who writes these and who reviews them'."
  Show `deploy/policy/templates` and `deploy/policy/constraints` in the repo.
- **The NKP Project** (`hydra-lite`): this is the app team's view. Walk the tabs:
  - Continuous Deployment: the `hydra-lite` GitOps source pointing at `./deploy/envs/project`. "The Hydra team added this themselves, in the UI, no cluster-admin."
  - Secrets: `hydra-secret`, one key, created in the UI. Git never saw it.
  - Network Policies: `colby-api-ingress` shows up here automatically.
  - Quotas & Limit Ranges: where the platform team caps what a project can consume.
  Two GitOps paths from one repo: platform team owns `hydra-system` (prod, ephemeral envs, policy); app team owns their project. Same images, same manifests.
- Edition call-out: Grafana/Prometheus, Flux, Kubecost and Gatekeeper are all **Pro**. Fleet/multi-cluster is Ultimate. "You'd be a Pro shop."

## 7. Config and secrets (2 min, talk track)

- Platform page: SQL target and connection string source. "The only secret the app needs is the SQL password. In prod Flux substitutes it from a cluster Secret at apply time; in the project it came from the Secrets tab. Either way Git only has a placeholder."
- Repo: `deploy/base/config.yaml` with `${SQL_SA_PASSWORD}`.
- "In production, External Secrets Operator fills that Secret from AWS Parameter Store today, or Azure Key Vault when you move. Your cloud direction doesn't block any of this."

## 8. Phase 1 proposal and Q&A

One slide:

1. Small NKP **Pro** cluster on their Nutanix (3 workers is plenty; licensed by worker vCPU only)
2. Casey containerizes the Hydra web tier (the Dockerfile you saw), Hangfire stays in-process
3. Flux wired to Azure DevOps (or GitHub). PR builds -> ephemeral namespaces. Merge -> prod with the manual approval gate they have today
4. Fortinet VIP points at Traefik. TLS terminates at ingress; pass-through to pod is possible if compliance requires
5. Datadog agent as a DaemonSet, existing APM tracer keeps working
6. Secrets via External Secrets Operator -> Parameter Store now, Key Vault later
7. Databases stay where they are. Oracle and SQL Server are reached over the network exactly as today

Open the floor.

---

## Recovery

| Problem | Do |
|---|---|
| Lab down | Play the backup recording. Walk the repo and the diagram. |
| Image pull error | Check GHCR packages are public. `kubectl -n hydra describe pod <pod>` |
| hydra-web not Ready | `kubectl -n hydra logs deploy/hydra-web`. Usually SQL still starting; readiness has 12 retries. |
| Flux not reconciling | `kubectl -n hydra-system get kustomizations`; `flux reconcile source git hydra-lite -n hydra-system` if flux CLI present, else `kubectl -n hydra-system annotate gitrepository hydra-lite reconcile.fluxcd.io/requestedAt="$(date +%s)" --overwrite` |
| Ephemeral env slow | SQL Server pull + start is ~60s first time per node. Narrate the Actions run meanwhile. |
| Gatekeeper not installed | Skip step 6c, show the policy files and describe. |
| Project CD source red | Check `kubectl -n hydra-lite-6mg6s get kustomizations hydra-lite -o yaml | grep -A3 message`. Prod path is independent, keep going. |
| Can't delete branch from jumphost | Use the GitHub Branches page trash icon. |
| Lost the SQL password for the project ns | Edit the secret in the project's Secrets tab and `kubectl -n hydra-lite-6mg6s delete pod hydra-db-0` so SQL Server picks it up (new DB, re-seeds). |
