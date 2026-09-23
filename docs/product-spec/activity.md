# Contextual activity
Each provider has independent CLI and desktop contributions. Either contribution makes that provider active; multiple surfaces never duplicate it. Both providers active shows both; neither active hides the monitor.

Desktop activity requires the verified provider application to be frontmost with a visible, nonminimized window. Switching away or minimizing removes its desktop contribution; restoring/frontmost restores it. A CLI remains independent of desktop focus.

Verified interactive CLI sessions count while open, including idle. Known interactive options are accepted; help, version, status, noninteractive/helper modes, unknown modes and Llumi-owned queries do not count. A terminal/console must exist. Invocation classification is bounded and rejects unknown input before collecting possible prompt arguments. Activity does not mean model inference.

Only executable identity, process lifetime, bounded invocation metadata and frontmost/visibility metadata are used. Do not inspect window titles, conversation content, source files, terminal output, keystrokes or screen pixels.
