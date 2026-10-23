# Artifact delivery

GitHub Actions verifies and publishes an immutable clean image only. Production configuration, resources and promotion policy belong to `depart-software/deploy-manifests`. The resource pipeline reads successful public `publish-image.yml` runs and resolves the corresponding GHCR digest; Axiops executes deployment. No deployment host, SSH key or production configuration is available to the service workflow.
