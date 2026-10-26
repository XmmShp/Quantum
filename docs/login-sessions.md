# Native-client login persistence

Desktop and Quantum Mobile use the same internal `PlatformSession` implementation. After a password login against a platform that supports `RefreshLogin`, the complete session is saved in OS secure storage under a key scoped to the platform URL. Passwords are never persisted. Restarting restores the session; returning to the foreground reconnects using fresh credentials.

Access tokens refresh shortly before expiry, with one refresh shared by concurrent calls. A definitive HTTP 401 or platform `authentication_required` response can trigger one refresh and one retry. Network timeouts and unknown business outcomes are never retried automatically. Temporary network failures retain the login, whereas invalid/revoked/expired refresh credentials clear it and prompt for sign-in.

Refresh credentials rotate on use and have a 30-day absolute session lifetime. The client securely persists a refresh request ID before sending, allowing the latest rotation to be recovered after a dropped response within the platform's two-minute retry window. After a longer interruption during an uncertain rotation, sign in again. Logout clears local credentials and attempts server revocation with a five-second timeout; an offline logout cannot revoke a remote credential until the server is reachable. Password reset and session revocation invalidate new session-bound access tokens as well as refresh credentials.

Updating an older client does not create a refresh credential for an existing token-only login. The desktop migrates a still-valid official-platform token for its remaining lifetime; after it expires, one password login is required to establish a persistent session. The mobile preview previously kept access tokens only in memory and also needs one new login after upgrading.

Platform account ownership authorizes mobile access; there is no separate device approval. Device registration credentials retain their 24-hour lifetime. Expired/revoked registration produces a reconnect prompt rather than an endless retry loop, and replacement devices can reconnect using the same account. Reconnecting does not replay a pending action whose outcome is unknown.
