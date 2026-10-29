# Artifact delivery

GitLab Runner verifies the monorepo and publishes an immutable clean image to `$CI_REGISTRY_IMAGE/platform:$CI_COMMIT_SHA`. The root `.gitlab-ci.yml` owns verification and publication; GitHub is a public source mirror with Actions disabled. The `publish-platform-image` job records the image reference in its dotenv artifact.

Production configuration, resources and promotion policy remain in the deployment manifests repository. Image publication does not promote or restart production. Any existing consumer of GitHub `publish-image.yml` runs and GHCR must be switched to the GitLab pipeline and Registry before the next deployment. No deployment host, SSH key or production configuration is available to the service pipeline.

## Internal mobile transport source

The platform contract references `Quantum/quantum/src/Quantum.Mobile.Internal` in the same monorepo revision. This source module remains non-packable and has no dependency on the public plugin SDK. There is no secondary framework checkout or independent revision pin.

Build the container from the monorepo root:

```sh
docker build -f Quantum.Platform/Dockerfile -t quantum-platform:local .
```

No credentials or deployment configuration belong in this context. Configuration is supplied at runtime; the image does not contain `appsettings*.json` files.
