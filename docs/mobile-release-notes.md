# Quantum Mobile development preview

0.1.3 connects directly to online desktops signed in to the same account. Desktop Quantum automatically publishes every available mobile plugin page after sign-in; plugin selection and per-device approval are no longer required. After choosing a desktop, the first plugin opens immediately. A persistent bottom navigation bar offers quick switches, and its pull-up menu lists all pages, refreshes the catalog and switches desktops. Draft confirmation and pending-action protections remain in place.

0.1.2 improves feedback after real device testing: page loading and device selection clear outdated status messages, action results use Chinese/English UI messages, and plugin result JSON is no longer displayed. Validation errors offer input guidance. Pending and uncertain actions retain their duplicate-submission warnings.

The existing secure login restoration and token renewal remain available. Updating retains the saved login and device registration. Account ownership, device credentials, view expiry and execution validation continue to protect access.

Android companion client and portable Windows client. Sign in with the same Quantum Platform email and password as the desktop, then choose an online computer. No separate sharing selection or device approval is required.

- Native rendering of declared plugin pages; business operations continue to run on the desktop.
- Calendar, Ledger and Codex mobile pages are provided by the matching updated official plugins.
- Chinese/English selection and persistent advanced connection settings.
- Release builds omit Mock and UI JSON developer tools.
- Explicit data refresh; expired and uncertain operations must be checked before retrying.

This is an Android/Windows development preview. iOS and store distribution are not included. Windows: extract the full archive and run Quantum.Mobile.exe. Android: install the signed APK. Local debug-signed builds may need removal before installing this first release because the signing identity differs. Future published updates use the persistent release signing identity.

See the mobile usage guide and official plugin mobile documentation for setup, supported workflows and limitations.
