# cctokmon

**Live token usage, cost, and context window monitor for Claude Code.**

Runs in a dedicated terminal pane alongside your Claude Code session. Data comes directly from Claude Code's billing engine — not from JSONL logs, which are known to undercount tokens by up to 174×.

```
┌──────────────────────────────────────────────────────┐
│  CCTOKMON · Live                    ↻ 10s  04/08 09:14 │
├──────────────────────────────────────────────────────┤
│  Model: Sonnet 4.6                  Session: 56m 12s │
│  Project: ~/Documents/myapp                          │
├──────────────────────────────────────────────────────┤
│  CONTEXT WINDOW                                      │
│  █████████████████████████░░░░░░░░░░░░░░░░  63%     │
│  Input:   8,500 tok       Output:  1,200 tok         │
│  Cache R: 2,000 tok       Cache W: 5,000 tok         │
│  Total:   126,700 / 200,000 tok                      │
├──────────────────────────────────────────────────────┤
│  COST & USAGE                                        │
│  Session cost:    $5.3621                            │
│  Lines: +1,842 / -437                               │
│  Burn rate:       $5.66/hr                           │
├──────────────────────────────────────────────────────┤
│  RATE LIMIT (5hr window)                             │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  0.0%   │
├──────────────────────────────────────────────────────┤
│  ▲ Updated 3s ago                                    │
└──────────────────────────────────────────────────────┘
```

---

## Why cctokmon

Most Claude Code usage monitors (ccusage, Claude-Code-Usage-Monitor) parse the JSONL session logs in `~/.claude/projects/`. This works for session history and activity counts, but the token fields in those logs are **unreliable**:

- `usage.input_tokens` in JSONL is a streaming placeholder — roughly 75% of entries contain `0` or `1`, never updated after streaming completes
- Input tokens are undercounted by **100–174×**
- Output tokens are undercounted by **10–17×**
- The `costUSD` field was removed from JSONL entirely as of Claude Code v1.0.9 for Max plan users

cctokmon avoids this by hooking into Claude Code's **statusline API** — a feature that sends a full billing-accurate JSON payload to a companion script on every interaction. Cost and token counts come from the same source Claude Code uses for its own billing display.

**Polling mode** (no setup required) reads `~/.claude/stats-cache.json`, which contains accurate aggregated totals that Claude Code maintains separately from the JSONL logs.

---

## Requirements

| Dependency | Version | Notes |
|---|---|---|
| Bash | 4.0+ | macOS ships with Bash 3 — see below |
| jq | any | JSON parsing |
| tput | any | Terminal control (ncurses) — standard on Linux/macOS |
| awk | any | Float arithmetic |

**macOS note:** The system Bash on macOS is 3.2 (GPLv2 licensing restriction). Install a current version:
```bash
brew install bash
```
jq and tput are available via Homebrew if missing:
```bash
brew install jq ncurses
```

**Linux:** jq may not be pre-installed:
```bash
sudo apt install jq        # Debian/Ubuntu
sudo dnf install jq        # Fedora/RHEL
```

---

## Installation

```bash
git clone https://github.com/ramon-webdevpro-nl/cctokmon.git
cd cctokmon
bash install.sh
```

The installer will:

1. Check all dependencies and offer to install any that are missing via your system package manager (apt / dnf / brew)
2. Copy your config to `~/.config/cctokmon/config`
3. Symlink `cctokmon` to `~/.local/bin/cctokmon`
4. Copy the statusline companion script to `~/.claude/cctokmon-statusline.sh`
5. Optionally configure the statusline bridge in `~/.claude/settings.json`

If `~/.local/bin` is not in your PATH, add this to your `~/.bashrc` or `~/.zshrc`:
```bash
export PATH="$HOME/.local/bin:$PATH"
```

---

## Usage

Open cctokmon in a separate terminal pane while Claude Code runs in another:

```bash
cctokmon                      # bridge mode (default, billing-accurate)
cctokmon --mode polling       # polling mode (stats-cache.json, no setup needed)
cctokmon --refresh 5          # refresh every 5 seconds
cctokmon --theme minimal      # reduced color output
cctokmon --check              # verify setup and data sources
cctokmon --setup              # re-run the installer
```

Press `Ctrl+C` to exit cleanly.

### Options

| Flag | Default | Description |
|---|---|---|
| `-m, --mode MODE` | `bridge` | `bridge` or `polling` |
| `-r, --refresh SECS` | `10` | Seconds between refreshes |
| `-t, --theme THEME` | `default` | `default`, `minimal`, or `none` |
| `-s, --scope SCOPE` | `session` | `session`, `daily`, or `cumulative` |
| `-c, --config FILE` | `~/.config/cctokmon/config` | Path to config file |
| `--check` | | Validate dependencies and data sources |
| `--setup` | | Run the installer |

---

## Modes

### Bridge mode (recommended)

The installer configures `cctokmon-statusline.sh` as your Claude Code statusline script by setting `statusLine` in `~/.claude/settings.json`. Claude Code calls this script on every interaction, passing a JSON payload via stdin. The script writes it atomically to `~/.claude/cctokmon-state.json`. cctokmon reads that file every `REFRESH_INTERVAL` seconds.

Data displayed in bridge mode:
- Session cost (exact, from billing engine)
- Context window usage with per-request token breakdown
- Input / output / cache read / cache write tokens for the current request
- Session duration and burn rate ($/hr)
- Rate limit window usage (5-hour rolling)
- Lines added / removed

The statusline script also outputs a compact one-line summary to Claude Code's own status bar: `$0.053 | ctx 63%`.

### Polling mode

Reads `~/.claude/stats-cache.json` directly. No bridge configuration needed — works out of the box. Useful if you prefer not to configure a statusline, or as a fallback.

Data displayed in polling mode:
- Today's message count, session count, and tool call count
- All-time token totals per model (input, output, cache read, cache write)
- Estimated cost (calculated from the embedded pricing table)

Polling mode shows aggregated history, not per-request live data.

---

## Configuration

Edit `~/.config/cctokmon/config`:

```bash
# Refresh interval in seconds
REFRESH_INTERVAL=10

# Mode: bridge or polling
MODE=bridge

# Color theme: default, minimal, none
THEME=default

# Cost display scope: session, daily, cumulative
COST_SCOPE=session
```

CLI flags take precedence over the config file. The config path can be overridden with the `CCTOKMON_CONFIG` environment variable.

---

## How the statusline bridge works

```
Claude Code
    │  (on every interaction)
    │  pipes JSON billing payload to stdin
    ▼
cctokmon-statusline.sh
    │  writes atomically to
    ▼
~/.claude/cctokmon-state.json
    │  read every REFRESH_INTERVAL seconds by
    ▼
cctokmon.sh  →  terminal dashboard
```

The statusline script must complete in under 100ms (Claude Code requirement). It reads stdin, writes to a temp file, and atomically renames it — the entire script is under 20 lines.

---

## Uninstall

```bash
bash uninstall.sh
```

Removes the symlink at `~/.local/bin/cctokmon`, the statusline script at `~/.claude/cctokmon-statusline.sh`, the state file at `~/.claude/cctokmon-state.json`, and the `statusLine` entry from `~/.claude/settings.json`. Optionally removes `~/.config/cctokmon`.

---

## Comparison with other tools

| | cctokmon | ccusage | Claude-Code-Usage-Monitor |
|---|---|---|---|
| Data source | Statusline API / stats-cache | JSONL logs | JSONL logs |
| Token accuracy | Exact (billing engine) | ⚠ Unreliable | ⚠ Unreliable |
| Cost accuracy | Exact | ⚠ Estimated | ⚠ Estimated |
| Real-time | Yes (bridge mode) | No (historical) | Yes (polling JSONL) |
| Context window | Yes | No | Yes |
| Rate limit | Yes | No | Yes |
| Runtime | Bash + jq | Node.js | Python |
| Install | `bash install.sh` | `npm install -g` | `pip install` |

---

## Pricing table

The pricing table in `cctokmon.sh` is used in polling mode only. In bridge mode, `cost.total_cost_usd` from the statusline JSON is authoritative and no calculation is needed. Update the table near the top of `cctokmon.sh` when Anthropic changes pricing:

```bash
PRICE_INPUT["claude-sonnet-4-6"]=3.00   # USD per 1M input tokens
PRICE_OUTPUT["claude-sonnet-4-6"]=15.00  # USD per 1M output tokens
```

---

## License

MIT
