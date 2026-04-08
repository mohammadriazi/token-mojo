#!/usr/bin/env bash
# cctokmon-statusline.sh — Companion statusline script for cctokmon
#
# Claude Code pipes a JSON payload to this script's stdin on every interaction.
# We write it atomically to a shared state file for cctokmon.sh to read.
#
# Install: set "statusLine": "/path/to/cctokmon-statusline.sh" in ~/.claude/settings.json
# (Run: cctokmon --setup  to configure this automatically)

STATE_FILE="${CCTOKMON_STATE_FILE:-$HOME/.claude/cctokmon-state.json}"
TMP_FILE="${STATE_FILE}.tmp"

# Read all of stdin into a variable
input=$(cat)

# Atomically write to state file
printf '%s\n' "$input" > "$TMP_FILE" && mv "$TMP_FILE" "$STATE_FILE"

# Output a compact one-line status for Claude Code's own statusbar
if command -v jq >/dev/null 2>&1; then
    read -r cost pct <<< "$(printf '%s' "$input" | jq -r '
        [
            (.cost.total_cost_usd          // 0 | tostring),
            (.context_window.used_percentage // 0 | tostring)
        ] | @tsv
    ' 2>/dev/null)"
    LC_NUMERIC=C printf '$%.3f | ctx %s%%' "${cost:-0}" "${pct:-0}"
else
    printf 'cctokmon'
fi
