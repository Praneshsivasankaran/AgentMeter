# Set up AgentMeter

Fresh installations open native setup automatically. Existing installations with valid migrated/current appearance or notch/menu preferences continue straight to Usage. You can reopen setup from **Help → Setup AgentMeter…**. Only a local completion flag is stored; provider choices are not persisted as monitoring restrictions.

Choose Codex, Claude Code, both, or continue without installing either. Setup displays instructions and Copy buttons. AgentMeter never installs providers, runs login commands, reads Terminal content or asks for credentials. **Check Again** uses the same bounded, coalesced provider refresh as Usage. Ready means a current authenticated allowance reading succeeded; stale or unavailable data is not considered Ready.

## Verified provider instructions

Copy checked against official documentation on 2026-09-23:

- Codex installation: `brew install --cask codex` ([official CLI guide](https://learn.chatgpt.com/docs/codex/cli)). This matches the supported Homebrew installation/discovery path. Homebrew must already be installed; the linked official guide also offers a standalone installer. AgentMeter does not install Homebrew.
- Codex authentication: `codex login` ([official authentication guide](https://learn.chatgpt.com/docs/auth)). Choose the ChatGPT subscription account used in other Codex clients. API-key billing is not subscription allowance.
- Claude Code installation: `curl -fsSL https://claude.ai/install.sh | bash` ([official quickstart](https://code.claude.com/docs/en/quickstart)). This is Anthropic’s native installation path, supported by AgentMeter discovery.
- Claude Code authentication: `claude auth login` ([official CLI reference](https://code.claude.com/docs/en/cli-reference)). Choose a Claude subscription account, not Console/API billing. Credentials stay with Claude Code.

Commands run only when the user chooses to paste and execute them in Terminal. No prompt or model inference is needed to check allowance. One working provider is enough. If verification is unavailable, Finish Anyway continues to preferences and the normal app without inventing readiness.

Setup preferences are the same objects used by Settings. Launch at Login uses actual ServiceManagement registration; it is not copied from beta preferences. System/Light/Dark applies to the entire app, including setup and compact/expanded notch. The normal Dock entry remains available even if notch and menu icon are off.
