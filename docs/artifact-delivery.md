# Artifact delivery

GitHub Actions verifies and publishes an immutable clean image only. Production configuration, resources and promotion policy belong to `depart-software/deploy-manifests`. The resource pipeline reads successful public `publish-image.yml` runs and resolves the corresponding GHCR digest; Axiops executes deployment. No deployment host, SSH key or production configuration is available to the service workflow.

## Internal mobile transport source

The platform contract references Quantum's framework-internal transport project. `.framework/quantum.ref` pins the exact upstream commit used by CI. The workflow checks out only `quantum/src/Quantum.Mobile.Internal`; the Docker build consumes it through the named `quantum-framework` build context. This source module remains non-packable and has no dependency on the public plugin SDK.

Local builds use the sibling Quantum checkout by default. To reproduce the pinned build, set the MSBuild property `QuantumMobileInternalProject` to the checked-out internal project. Docker builds require `--build-context quantum-framework=<Quantum checkout>`; no credentials or deployment configuration belong in this context.
