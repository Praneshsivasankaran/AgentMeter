# Settings and lifecycle
Keep settings limited to launch at login/startup, compact monitor, background menu/tray icon and Appearance: System, Light, Dark. Preferences persist without credentials, account data or usage history. Show authoritative startup registration state; failed persistence must not claim success.

Normal launch opens Usage. Login/startup launch stays quiet when the background control surface is enabled. Close continues monitoring; Open restores/focuses one window. If the background icon is hidden, retain a discoverable native application entry so closing cannot strand the app. Quit cancels refreshes, removes background and compact surfaces, stops activity monitoring and terminates owned helpers.

Background menu: Open AgentMeter, Refresh, Settings, Quit. No allowance dashboard in that menu. No refresh sliders, colors, opacity controls, animation-speed controls, history, notifications, account management or API keys.

macOS 1.0.0 candidate: one process per user owns the background services, including across copies of the app. A nonblocking kernel lock is acquired before services or diagnostics initialize. A second launch may request that the existing window open, but starts no services. The lock lasts through shutdown and is released by process exit; it is not a PID file and is never deleted on exit. Windows behavior is unchanged by this Mac remediation.

The new macOS preference domain imports only validated `notchEnabled`, `menuEnabled` and `appearance` values from the beta once. Existing new-domain values win. Login registration is never copied; ServiceManagement remains authoritative. The normal Dock entry remains available with both optional surfaces disabled.
