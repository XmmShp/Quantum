# Mobile attachments

Attachments use the authenticated `ExchangeMobileBridge` control plane, not public download URLs. `attachment/create` binds immutable metadata to an authenticated mobile device, its selected desktop and a declared schema-2 attachment input. `attachment/write` accepts aligned 128 KiB chunks; `attachment/complete` verifies length and SHA-256 before making the file readable. Only the owning desktop may call `attachment/read`. All operations remain account-scoped and serialized by the existing registration lock.

Queued actions contain only file references, keeping mailbox database writes and polling small. The gateway verifies references, bytes and the current action before materializing the public SDK attachment value for plugin RPC. A failed draft can rebind the same immutable file to a fresh valid view without uploading it again. Retried chunks and upload completion are idempotent. Missing or corrupt completed files become uploadable again.

The private file store lives below `QuantumPlatform:Storage:BasePath/mobile-attachments`, outside public static assets, using hashed account directories and random attachment IDs. Original filenames never select a server path. Metadata and file contents expire after 24 hours; the store removes expired files during periodic-on-access cleanup. This uses the platform's existing persistent storage volume and needs no schema migration. Preserve that volume when moving the service.

Limits are four files per action, 8 MiB per file, 16 MiB per action and 64 MiB of attachment storage per account. Page/action expiry and runtime ownership checks remain in force. No attachment contents, credentials or signed download URLs are logged.
