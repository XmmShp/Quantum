#!/usr/bin/env bash
set -euo pipefail

readonly stack_name="quantum-platform"
readonly service_name="${stack_name}_platform"
readonly deadline=$((SECONDS + 240))

while (( SECONDS < deadline )); do
  if docker service inspect "$service_name" >/dev/null 2>&1; then
    replicas="$(docker service ls --filter "name=${service_name}" --format '{{.Replicas}}')"
    container_id="$(
      docker ps --quiet \
        --filter "label=com.docker.swarm.service.name=${service_name}" \
        --filter status=running |
        head -n 1
    )"
    if [[ "$replicas" == "1/1" ]] && [[ -n "$container_id" ]]; then
      health="$(docker inspect "$container_id" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}')"
      if [[ "$health" == "healthy" ]]; then
        if [[ -n "${QUANTUM_PLATFORM_IMAGE:-}" ]]; then
          actual_image="$(docker inspect "$container_id" --format '{{.Config.Image}}')"
          if [[ "$actual_image" != "$QUANTUM_PLATFORM_IMAGE" && "$actual_image" != "$QUANTUM_PLATFORM_IMAGE@"* ]]; then
            echo "Healthy service is running a different image than this deployment." >&2
            exit 1
          fi
        fi
        echo "Production service is healthy."
        break
      fi
    fi
  fi
  sleep 5
done

if (( SECONDS >= deadline )); then
  echo "Production service did not become healthy; inspect it on the Swarm manager." >&2
  exit 1
fi

mapfile -t legacy_containers < <(
  docker ps --all --quiet \
    --filter label=com.docker.compose.project=quantum-platform \
    --filter label=com.docker.compose.service=platform
)
if (( ${#legacy_containers[@]} > 0 )); then
  docker rm "${legacy_containers[@]}" >/dev/null
fi

current_secrets="$(
  docker service inspect "$service_name" \
    --format '{{range .Spec.TaskTemplate.ContainerSpec.Secrets}}{{println .SecretName}}{{end}}'
)"

cleanup_secrets() {
  local prefix="$1"
  local retained
  retained="$(
    while IFS= read -r secret; do
      [[ -n "$secret" ]] || continue
      docker secret inspect "$secret" --format '{{.CreatedAt}} {{.Spec.Name}}'
    done < <(docker secret ls --format '{{.Name}}' | grep -F "$prefix" || true) |
      sort --reverse |
      head -n 3 |
      awk '{print $NF}'
  )"

  while IFS= read -r secret; do
    [[ -n "$secret" ]] || continue
    if grep -Fxq "$secret" <<<"$current_secrets" || grep -Fxq "$secret" <<<"$retained"; then
      continue
    fi
    docker secret rm "$secret" >/dev/null 2>&1 || true
  done < <(docker secret ls --format '{{.Name}}' | grep -F "$prefix" || true)
}

cleanup_secrets quantum-platform-appsettings-
cleanup_secrets quantum-platform-email-
cleanup_secrets quantum-platform-review-jwks-
