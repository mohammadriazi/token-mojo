# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`cctokmon` — a zero-dependency Bash terminal dashboard showing live Claude Code token usage, cost, and context window data. Full specification is in `claude-token-monitor-spec.md`.

**Stack:** Pure Bash 4.0+ · `jq` (required) · `tput` · `awk` · no other dependencies.

TokenMojo is the GUI on top of the same bridge files: a macOS menu bar app (`menubar/`, Swift) and a Windows tray app (`windows/`, C# / .NET 10 WPF).

## File structure

```
cctokmon.sh              # Main monitor — entry point
cctokmon-statusline.sh   # Companion statusline script (called by Claude Code on every interaction)
install.sh               # Installer: deps check, symlink, statusline config
uninstall.sh             # Clean removal
config.example           # Config template copied to ~/.config/cctokmon/config on install
CLAUDE.md                # This file
claude-token-monitor-spec.md  # Original build spec (source of truth for design decisions)
menubar/
  TokenMojo.swift        # macOS menu bar app (AppKit + SwiftUI, single file)
  build.sh               # swiftc → ~/Applications/TokenMojo.app
windows/
  build.sh               # tests, then dotnet publish → %LOCALAPPDATA%\Programs\TokenMojo\TokenMojo.exe
  TokenMojo.Windows.slnx
  src/TokenMojo.Core/    # net10.0, no UI: bridge parsing, display rules, session loading, source discovery
  src/TokenMojo/         # net10.0-windows WPF tray app: icon, flyout, pop-out, Launch at Login
  tests/TokenMojo.Core.Tests/  # xUnit tests for Core (run on any OS)
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

Windows app (needs the .NET 10 SDK; Core tests run on Linux/macOS too, the app builds from any OS):

```bash
dotnet test windows/tests/TokenMojo.Core.Tests
bash windows/build.sh [target-dir]
```

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

### Windows tray app (`windows/`)

- Shares everything outside the UI with the Mac app: the same bridge script, installer and file contract (`cctokmon-state.json`, `cctokmon-sessions/`, `CCTOKMON_STATE_FILE` / `CCTOKMON_SESSIONS_DIR`). Do not add a Windows-specific bridge; under native Windows, Claude Code runs the bash bridge through Git Bash.
- Display rules mirror `menubar/TokenMojo.swift` and must stay in step with it: `livePercent` (null once `resets_at` has passed), severity thresholds 50/80, the 12-hour session window, "active" within 10 minutes, at most 10 session rows, reset/percent/cost wording. They live in `TokenMojo.Core` (`Gauges`, `Dashboard`, `SessionStore`) so they are unit-tested; the WPF project only binds to `DashboardModel`.
- Sources: `ClaudeDirLocator` returns `%USERPROFILE%\.claude` plus `\\wsl.localhost\<distro>\home\*\.claude` (and `/root`) for distros listed by `wsl.exe --list --running --quiet` (UTF-16LE output). Only running distros are read, because touching a stopped distro's share boots it. Discovery reruns every minute; the bridge files are polled every 5 seconds off the UI thread.
- The current state is the most recently written `cctokmon-state.json` across sources (rate limits are per account). Sessions from every source are merged into one list, newest first.
- Failures stay per source: a distro that stops or denies access mid-read is skipped, never allowed to blank the others or throw out of the timer tick. Bridge files are opened with `FileShare.Delete` so the bridge's `mv` can rename over a file TokenMojo is reading on Windows.
- Tray icon: one ring for the worst gauge, drawn by the same `RingGauge.Draw` as the dashboard rings and wrapped as a PNG `.ico`; taskbar light/dark comes from `SystemUsesLightTheme`, app light/dark from `AppsUseLightTheme`.
- `build.sh` publishes self-contained single-file without compression: compression halves the file size but roughly doubles memory, and the app runs all day.
- C# style is enforced by `windows/.editorconfig` with `EnforceCodeStyleInBuild` and warnings as errors: explicit types, never `var` (`IDE0008`); `new()` when the type is already written (`IDE0090`).
