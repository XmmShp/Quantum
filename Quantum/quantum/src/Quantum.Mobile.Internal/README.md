# Quantum mobile internals

This framework-owned source project is shared by the desktop host, platform and mobile application. It is not a public plugin API or a NuGet package (`IsPackable=false`). It has no dependency on the plugin SDK, NOF, or desktop implementation.

Plugin page declarations live in `Quantum.Plugin.Abstraction` and the TypeScript SDK. The desktop gateway explicitly converts those declarations to this internal version-one wire representation, replacing RPC bindings with opaque action IDs. The declaration importer and builders here serve the standalone mobile simulator; they never invoke plugin RPC on mobile.

Changes to the wire contract must be validated across desktop, platform and mobile. Preserve version-one JSON compatibility independently from the public SDK namespaces.
