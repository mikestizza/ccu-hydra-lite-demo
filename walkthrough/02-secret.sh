#!/usr/bin/env bash
# Layer 2: the one secret the app needs, the SQL SA password.
# Created by hand here, never written to Git. The same value also goes into
# hydra-system/hydra-secrets so that when Flux takes over (step 10) it
# substitutes the identical password and nothing restarts with a mismatch.
set -euo pipefail
SQL_SA_PASSWORD="${SQL_SA_PASSWORD:-$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Hy"+"".join(secrets.choice(a) for _ in range(18))+"!9")')}"

kubectl -n hydra create secret generic hydra-secret \
  --from-literal=MSSQL_SA_PASSWORD="$SQL_SA_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl create namespace hydra-system --dry-run=client -o yaml | kubectl apply -f -
kubectl -n hydra-system create secret generic hydra-secrets \
  --from-literal=SQL_SA_PASSWORD="$SQL_SA_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "Secret hydra/hydra-secret created (and mirrored to hydra-system/hydra-secrets for Flux)."
kubectl -n hydra get secret hydra-secret
