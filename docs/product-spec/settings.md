# Settings and lifecycle
Keep settings limited to launch at login/startup, compact monitor, background menu/tray icon and Appearance: System, Light, Dark. Preferences persist without credentials, account data or usage history. Show authoritative startup registration state; failed persistence must not claim success.

Normal launch opens Usage. Login/startup launch stays quiet when the background control surface is enabled. Close continues monitoring; Open restores/focuses one window. If the background icon is hidden, retain a discoverable native application entry so closing cannot strand the app. Quit cancels refreshes, removes background and compact surfaces, stops activity monitoring and terminates owned helpers.

Background menu: Open AgentMeter, Refresh, Settings, Quit. No allowance dashboard in that menu. No refresh sliders, colors, opacity controls, animation-speed controls, history, notifications, account management or API keys.
