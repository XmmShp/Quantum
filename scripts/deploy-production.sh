#!/usr/bin/env bash
set -euo pipefail

readonly stack_name="quantum-platform"
readonly config_dir="${QUANTUM_PLATFORM_CONFIG_DIR:-$HOME/.config/quantum-platform}"
readonly environment_file="${config_dir}/production.env"
readonly email_file="${config_dir}/quantum-platform-email.json"
readonly review_jwks_file="${config_dir}/quantum-platform-review.private.jwks.json"

: "${QUANTUM_PLATFORM_IMAGE:?set QUANTUM_PLATFORM_IMAGE}"
: "${QUANTUM_PLATFORM_DEPLOYMENT_ID:?set QUANTUM_PLATFORM_DEPLOYMENT_ID}"

# Download all image layers while the healthy service is still running. A slow
# registry must not take down the existing stop-first deployment.
docker pull "$QUANTUM_PLATFORM_IMAGE"

for required_file in "$environment_file" "$email_file" "$review_jwks_file"; do
  if [[ ! -r "$required_file" ]]; then
    echo "Required production configuration is unavailable." >&2
    exit 2
  fi
  permission="$(stat --format '%a' "$required_file")"
  if (( (8#$permission & 077) != 0 )); then
    echo "Production configuration permissions are too broad." >&2
    exit 2
  fi
done

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required on the deployment runner." >&2
  exit 2
fi

if [[ "$(docker info --format '{{.Swarm.LocalNodeState}}')" != "active" ]] ||
  [[ "$(docker info --format '{{.Swarm.ControlAvailable}}')" != "true" ]]; then
  echo "Deployment runner must execute on a Swarm manager." >&2
  exit 3
fi

for network in agentfn-overlay-net koala-pp-overlay-net; do
  if [[ "$(docker network inspect "$network" --format '{{.Scope}} {{.Driver}}')" != "swarm overlay" ]]; then
    echo "Required Swarm overlay network is unavailable." >&2
    exit 4
  fi
done

set -a
# The file is owned by the production operator and contains only deployment variables.
# shellcheck disable=SC1090
source "$environment_file"
set +a

: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
: "${QUANTUM_PLATFORM_JWT_SIGNING_KEY:?QUANTUM_PLATFORM_JWT_SIGNING_KEY is required}"
: "${QUANTUM_PLATFORM_OIDC_SIGNING_KEY_ENCRYPTION_KEY:?QUANTUM_PLATFORM_OIDC_SIGNING_KEY_ENCRYPTION_KEY is required}"

QUANTUM_PLATFORM_STORAGE_NODE="$(docker info --format '{{.Name}}')"
export QUANTUM_PLATFORM_STORAGE_NODE
export QUANTUM_PLATFORM_APPSETTINGS_SECRET="quantum-platform-appsettings-${QUANTUM_PLATFORM_DEPLOYMENT_ID}"
export QUANTUM_PLATFORM_EMAIL_SECRET="quantum-platform-email-${QUANTUM_PLATFORM_DEPLOYMENT_ID}"
export QUANTUM_PLATFORM_REVIEW_JWKS_SECRET="quantum-platform-review-jwks-${QUANTUM_PLATFORM_DEPLOYMENT_ID}"

temporary_dir="$(mktemp -d /tmp/quantum-platform-deploy-XXXXXX)"
readonly temporary_dir
trap 'rm -rf -- "$temporary_dir"' EXIT
chmod 700 "$temporary_dir"

export QUANTUM_PLATFORM_AGENTFN_MODEL="${QUANTUM_PLATFORM_AGENTFN_MODEL:-deepseek/deepseek-v4-pro}"
jq -n '
  {
    ConnectionStrings: {
      postgres: ("Host=koala-pp-postgresql;Port=5432;Database=quantum_platform;Username=quantum_platform;Password=" + env.POSTGRES_PASSWORD)
    },
    QuantumPlatform: {
      Jwt: { SigningKey: env.QUANTUM_PLATFORM_JWT_SIGNING_KEY },
      OidcServer: { SigningKeyEncryptionKey: env.QUANTUM_PLATFORM_OIDC_SIGNING_KEY_ENCRYPTION_KEY },
      Storage: { BasePath: "/app/Files" },
      AutomatedReview: {
        Enabled: true,
        Model: env.QUANTUM_PLATFORM_AGENTFN_MODEL,
        PrivateJwksPath: "/run/secrets/agentfn-review-private-jwks"
      },
      Email: { ConfigurationPath: "/run/secrets/quantum-platform-email" }
    }
  }
' >"${temporary_dir}/appsettings.Production.json"
chmod 600 "${temporary_dir}/appsettings.Production.json"

create_secret() {
  local name="$1"
  local source_file="$2"
  if ! docker secret inspect "$name" >/dev/null 2>&1; then
    docker secret create "$name" "$source_file" >/dev/null
  fi
}

create_secret "$QUANTUM_PLATFORM_APPSETTINGS_SECRET" "${temporary_dir}/appsettings.Production.json"
create_secret "$QUANTUM_PLATFORM_EMAIL_SECRET" "$email_file"
create_secret "$QUANTUM_PLATFORM_REVIEW_JWKS_SECRET" "$review_jwks_file"

docker volume inspect quantum-platform_platform-files >/dev/null 2>&1 ||
  docker volume create quantum-platform_platform-files >/dev/null

# Retire the legacy Compose task before claiming the same network alias and volume.
mapfile -t legacy_containers < <(
  docker ps --all --quiet \
    --filter label=com.docker.compose.project=quantum-platform \
    --filter label=com.docker.compose.service=platform
)
if (( ${#legacy_containers[@]} > 0 )); then
  docker stop "${legacy_containers[@]}" >/dev/null 2>&1 || true
fi

docker stack deploy \
  --with-registry-auth \
  --prune \
  --resolve-image always \
  --detach=false \
  --compose-file stack.production.yaml \
  "$stack_name"
