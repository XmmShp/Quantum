# Quantum Mobile development preview

0.1.6 connects the native pull-to-refresh gesture to a real refresh: release past the platform threshold to reload the device/page catalog and current desktop snapshot. The indicator remains active until the fresh page renders, then retracts; short pulls rebound without starting a refresh. Connection-status and refresh buttons are removed from Settings, and successful refreshes leave no persistent status panel. Draft inputs are retained. Pending or uncertain business operations remain protected and are never resubmitted by refresh.

0.1.5 adds a compact 44-high title bar with an avatar opening a left account drawer. The drawer presents user information and the current desktop, offers device switching, and pins Settings at the bottom. Login is required before account navigation becomes available. After login or saved-session restoration, the app returns to the last desktop for that platform/account; first use enters the first online desktop in menu order. An offline remembered desktop remains selected until it returns or the user switches. A uniquely named same-account desktop can recover from a rotated registration; ambiguous names require an explicit selection. The bottom plugin navigation and live page updates remain available.

0.1.4 gives plugin pages the full content viewport. Settings move into a dedicated page in the bottom menu; pull down the content area to inspect connection and synchronization status. There is no persistent app header, duplicate page heading, or manual prepare-data/refresh flow. Updated official plugins publish changes automatically, and desktop subscriptions forward them to the current phone view. Existing controls update in place, preserving unsubmitted inputs and scrolling. Switching pages displays a session-local cached preview while a fresh actionable view arrives. Desktop results upload in the same processing turn, foreground polling is 500 ms, and desktop-presence checks are throttled separately. Platform coalescing prevents live snapshots from exhausting mailbox storage; business request deduplication remains intact.

0.1.3 connects directly to online desktops signed in to the same account. Desktop Quantum automatically publishes every available mobile plugin page after sign-in; plugin selection and per-device approval are no longer required. After choosing a desktop, the first plugin opens immediately. A persistent bottom navigation bar offers quick switches, and its pull-up menu lists all pages, refreshes the catalog and switches desktops. Draft confirmation and pending-action protections remain in place.

0.1.2 improves feedback after real device testing: page loading and device selection clear outdated status messages, action results use Chinese/English UI messages, and plugin result JSON is no longer displayed. Validation errors offer input guidance. Pending and uncertain actions retain their duplicate-submission warnings.

The existing secure login restoration and token renewal remain available. Updating retains the saved login and device registration. Account ownership, device credentials, view expiry and execution validation continue to protect access.

Android companion client and portable Windows client. Sign in with the same Quantum Platform email and password as the desktop, then choose an online computer. No separate sharing selection or device approval is required.

- Native rendering of declared plugin pages; business operations continue to run on the desktop.
- Calendar, Ledger and Codex mobile pages are provided by the matching updated official plugins.
- Chinese/English selection and persistent advanced connection settings.
- Release builds omit Mock and UI JSON developer tools.
- Automatic page updates; expired and uncertain operations must be checked before retrying.

This is an Android/Windows development preview. iOS and store distribution are not included. Windows: extract the full archive and run Quantum.Mobile.exe. Android: install the signed APK. Local debug-signed builds may need removal before installing this first release because the signing identity differs. Future published updates use the persistent release signing identity.

See the mobile usage guide and official plugin mobile documentation for setup, supported workflows and limitations.
