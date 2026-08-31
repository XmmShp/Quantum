# Upstream migration record

- Repository: `https://github.com/XmmShp/Quantum-ExtensionMarket`
- Imported commit: `2c5c4b1fdeaadf75f2057be57594f67467f4de8d`
- Upstream commit date: 2025-03-16
- License: MIT

## Functional mapping

| Upstream area | Quantum implementation |
| --- | --- |
| `UsersController`, `UserService` | `MarketUser` aggregate and user JSON-RPC handlers |
| `ExtensionsController`, `ExtensionService` | `PluginListing`, `PluginRelease` and plugin/release handlers |
| `AuditLogsController`, `AuditLogService` | `AuditEntry`, `AuditWriter`, `ListAuditEntries` |
| `FileStorageService` | `IPluginPackageStore`, `PhysicalPluginPackageStore` |
| `JwtService`, `AuthorizeRolesAttribute` | `IMarketTokenIssuer`, JWT bearer caller context and handler authorization |
| `ApplicationDbContext`, old migrations | `ExtensionMarketDbContext` and `InitialNofArchitecture` migration |
| REST controllers | `IExtensionMarketService` JSON-RPC Contract at `/rpc` |

The original debug token endpoint, source-controlled JWT secret and hard-coded administrator password were intentionally removed. Administrator bootstrap is opt-in and accepts secrets only through configuration. The upstream README mentioned ratings/comments, but the imported source did not implement those models or endpoints, so they are not represented as migrated functionality.
