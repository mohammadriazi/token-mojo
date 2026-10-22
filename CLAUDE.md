# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`cctokmon` — a zero-dependency Bash terminal dashboard showing live Claude Code token usage, cost, and context window data. Full specification is in `claude-token-monitor-spec.md`.

**Stack:** Pure Bash 4.0+ · `jq` (required) · `tput` · `awk` · no other dependencies.

## File structure

```
cctokmon.sh              # Main monitor — entry point
cctokmon-statusline.sh   # Companion statusline script (called by Claude Code on every interaction)
install.sh               # Installer: deps check, symlink, statusline config
uninstall.sh             # Clean removal
config.example           # Config template copied to ~/.config/cctokmon/config on install
CLAUDE.md                # This file
claude-token-monitor-spec.md  # Original build spec (source of truth for design decisions)
```

## Running / testing

```bash
bash cctokmon.sh --check     # validate deps and data sources
bash cctokmon.sh --help
bash cctokmon.sh             # bridge mode (default)
bash cctokmon.sh --mode polling
bash install.sh              # or: cctokmon --setup
```

No build step — scripts run directly with bash.

## Key architecture

### Two operating modes

- **Bridge mode (default):** Claude Code calls `cctokmon-statusline.sh` on every interaction → writes JSON atomically to `~/.claude/cctokmon-state.json` → `cctokmon.sh` polls that file every `REFRESH_INTERVAL` seconds. Billing-accurate data.
- **Polling mode:** Reads `~/.claude/stats-cache.json` directly. No setup beyond install; shows daily/all-time totals only.

### Data source rules (critical)

- **Statusline JSON** — billing-accurate. Use `cost.total_cost_usd` directly in bridge mode; do not recalculate from tokens.
- **`stats-cache.json`** — accurate cumulative/daily totals.
- **JSONL `usage.input_tokens` / `usage.output_tokens`** — unreliable streaming placeholders (off by 100–174×). Do not use.

### Rendering

- `tput cup 0 0` for in-place refresh — no `tput clear` per frame (causes flicker). Clear only on first run and SIGWINCH.
- `tput ed` after each frame to erase stale content below (e.g. after resize).
- All float math via `awk`. No `bc`.
- Single `jq` call per data read with `mapfile -t arr` into indexed array.
- `SCRIPT_DIR` resolved via `readlink -f` at startup so `--setup` works through symlinks.
- `stat` mtime: macOS uses `stat -f %m`, Linux uses `stat -c %Y` — detected at runtime via `uname -s`.
- Always prefix `printf` with `LC_NUMERIC=C` when formatting floats (e.g. `LC_NUMERIC=C printf '$%.4f'`). Without it, locales using `,` as the decimal separator cause `printf: invalid number` and crash the script under `set -u`.
- Variables used across conditional branches must be declared before the `if` block. Declaring inside only one branch and referencing outside it triggers `set -u` and kills the process. (Historical example: `bar_w` was declared inside an `else` branch but used afterwards for the rate limit bar.)

### Installer (`install.sh`)

- Checks all deps (bash 4+, jq, tput, awk) before touching anything.
- If deps are missing: detects package manager (brew / apt / dnf / yum / winget), shows what it will install, prompts once, then re-verifies.
- Symlinks `cctokmon.sh` to `~/.local/bin/cctokmon`. Under Git Bash on Windows (`uname -s` is `MINGW*`/`MSYS*`/`CYGWIN*`) it writes a small launcher script instead: `ln -s` there silently copies the file unless Developer Mode is on, and a copy cannot find `install.sh` for `--setup`. `uninstall.sh` removes either form.
- On Windows only jq needs installing (Git Bash ships tput and awk); winget's links dir is added to `PATH` for the re-verify, since the running shell predates it.
- Copies `cctokmon-statusline.sh` to `~/.claude/cctokmon-statusline.sh`.
- Optionally sets `statusLine` in `~/.claude/settings.json` via `jq`. The correct format is an object: `{"type": "command", "command": "/path/to/script"}` — not a plain string (Claude Code's `/doctor` will flag a plain string as invalid).

### Config file

`~/.config/cctokmon/config` (or `$CCTOKMON_CONFIG`). Sourced after CLI args are parsed, so CLI args take precedence. Key settings: `REFRESH_INTERVAL`, `MODE`, `STATE_FILE`, `STATS_CACHE`, `THEME`.

### Pricing table

`declare -A PRICE_INPUT PRICE_OUTPUT` near top of `cctokmon.sh`. Used **only** in polling mode for cost estimation. In bridge mode `cost.total_cost_usd` from the statusline JSON is authoritative.
