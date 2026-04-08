#!/usr/bin/env bash
# cctokmon v0.1.0 — Claude Code Token Monitor
# Live token usage, cost, and context window data for Claude Code sessions.

set -uo pipefail

readonly VERSION="0.1.0"

# Resolve the real directory of this script (works through symlinks)
SCRIPT_DIR="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}" 2>/dev/null || echo "${BASH_SOURCE[0]}")")" && pwd)"
readonly SCRIPT_DIR

# ── Pricing table (USD per 1M tokens) ─────────────────────────────────────────
declare -A PRICE_INPUT PRICE_OUTPUT

PRICE_INPUT["claude-opus-4-6"]=15.00;            PRICE_OUTPUT["claude-opus-4-6"]=75.00
PRICE_INPUT["claude-opus-4-1-20250805"]=15.00;   PRICE_OUTPUT["claude-opus-4-1-20250805"]=75.00
PRICE_INPUT["claude-sonnet-4-6"]=3.00;           PRICE_OUTPUT["claude-sonnet-4-6"]=15.00
PRICE_INPUT["claude-sonnet-4-5-20250514"]=3.00;  PRICE_OUTPUT["claude-sonnet-4-5-20250514"]=15.00
PRICE_INPUT["claude-sonnet-4-20250514"]=3.00;    PRICE_OUTPUT["claude-sonnet-4-20250514"]=15.00
PRICE_INPUT["claude-haiku-4-5-20241022"]=0.80;   PRICE_OUTPUT["claude-haiku-4-5-20241022"]=4.00

# ── Defaults ───────────────────────────────────────────────────────────────────
REFRESH_INTERVAL=10
MODE="bridge"
STATE_FILE="$HOME/.claude/cctokmon-state.json"
STATS_CACHE="$HOME/.claude/stats-cache.json"
PROJECTS_DIR="$HOME/.claude/projects"
COST_SCOPE="session"
THEME="default"
CONFIG_FILE="${CCTOKMON_CONFIG:-$HOME/.config/cctokmon/config}"

# ── Runtime state ──────────────────────────────────────────────────────────────
TERM_COLS=80
TERM_ROWS=24
FRAME_WIDTH=58
NEEDS_CLEAR=1
COMPACT=0
LAST_UPDATE=0
DATA_STALE=0
SPIN_IDX=0

# ── Bridge mode data vars ──────────────────────────────────────────────────────
MODEL_NAME="unknown"
MODEL_ID="unknown"
SESSION_ID="unknown"
CWD="unknown"
COST_USD=0
DURATION_MS=0
LINES_ADDED=0
LINES_REMOVED=0
TOTAL_INPUT=0
TOTAL_OUTPUT=0
CTX_SIZE=200000
USED_PCT=0
REMAINING_PCT=100
CUR_INPUT=0
CUR_OUTPUT=0
CUR_CACHE_W=0
CUR_CACHE_R=0
RATE_LIMIT_PCT=0
CTX_AWAITING=1

# ── Polling mode data vars ─────────────────────────────────────────────────────
DAILY_MESSAGES=0
DAILY_SESSIONS=0
DAILY_TOOL_CALLS=0
DAILY_DATE=""
POLLING_MODEL_USAGE=""
CACHE_COMPUTED="unknown"

# ── Colors ─────────────────────────────────────────────────────────────────────
C_RESET="" C_BOLD="" C_DIM=""
C_GREEN="" C_YELLOW="" C_RED="" C_CYAN="" C_GRAY=""
C_HEADER="" C_BORDER="" C_LABEL="" C_VALUE="" C_WARN=""

# ── Box/bar chars ──────────────────────────────────────────────────────────────
BX_TL="┌" BX_TR="┐" BX_BL="└" BX_BR="┘"
BX_H="─" BX_V="│" BX_ML="├" BX_MR="┤"
BAR_FULL="█" BAR_EMPTY="░"

# ─────────────────────────────── INIT ─────────────────────────────────────────

init_theme() {
    [[ "$THEME" == "none" ]] && return
    local nc=0
    nc=$(tput colors 2>/dev/null || echo 0)
    [[ "$nc" -lt 8 ]] && return

    C_RESET=$(tput sgr0)
    C_BOLD=$(tput bold)
    C_GREEN=$(tput setaf 2)
    C_YELLOW=$(tput setaf 3)
    C_RED=$(tput setaf 1)
    C_CYAN=$(tput setaf 6)
    C_GRAY=$(tput setaf 8)

    if [[ "$THEME" == "default" ]]; then
        C_HEADER="${C_BOLD}${C_CYAN}"
        C_BORDER="${C_GRAY}"
        C_LABEL="${C_GRAY}"
        C_VALUE="${C_BOLD}"
        C_WARN="${C_YELLOW}"
    else  # minimal
        C_HEADER="${C_BOLD}"
        C_BORDER=""
        C_LABEL="${C_GRAY}"
        C_VALUE=""
        C_WARN="${C_YELLOW}"
    fi
}

init_charset() {
    local locale="${LANG:-}${LC_ALL:-}${LC_CTYPE:-}"
    if [[ "$locale" != *UTF-8* && "$locale" != *utf-8* && "$locale" != *utf8* ]]; then
        BX_TL="+" BX_TR="+" BX_BL="+" BX_BR="+"
        BX_H="-" BX_V="|" BX_ML="+" BX_MR="+"
        BAR_FULL="#" BAR_EMPTY="."
    fi
}

update_term_size() {
    local new_cols new_rows
    new_cols=$(tput cols 2>/dev/null || echo 80)
    new_rows=$(tput lines 2>/dev/null || echo 24)

    if [[ "$new_cols" -lt 30 ]]; then
        tput cnorm 2>/dev/null || true
        printf 'Terminal too narrow (%d cols). Need at least 30.\n' "$new_cols" >&2
        exit 1
    fi

    if [[ "$new_cols" != "$TERM_COLS" || "$new_rows" != "$TERM_ROWS" ]]; then
        TERM_COLS=$new_cols
        TERM_ROWS=$new_rows
        NEEDS_CLEAR=1
    fi

    if   [[ "$new_cols" -ge 60 ]]; then FRAME_WIDTH=58
    elif [[ "$new_cols" -ge 50 ]]; then FRAME_WIDTH=$(( new_cols - 2 ))
    else                                FRAME_WIDTH=$(( new_cols - 2 ))
    fi

    COMPACT=0
    [[ "$FRAME_WIDTH" -lt 50 ]] && COMPACT=1
}

cleanup() {
    tput cnorm 2>/dev/null || true
    tput sgr0  2>/dev/null || true
    tput clear 2>/dev/null || true
    echo "cctokmon stopped."
    exit 0
}

# ─────────────────────────────── UTILITIES ────────────────────────────────────

file_mtime() {
    if [[ "$(uname -s)" == "Darwin" ]]; then
        stat -f %m "$1" 2>/dev/null || echo 0
    else
        stat -c %Y "$1" 2>/dev/null || echo 0
    fi
}

fmt_num() {
    printf "%'d" "${1:-0}"
}

fmt_cost() {
    LC_NUMERIC=C printf '$%.4f' "${1:-0}"
}

format_duration() {
    local ms="${1:-0}"
    local s=$(( ms / 1000 ))
    local h=$(( s / 3600 ))
    local m=$(( (s % 3600) / 60 ))
    local sec=$(( s % 60 ))
    if   [[ $h -gt 0 ]]; then printf '%dh %dm'  "$h" "$m"
    elif [[ $m -gt 0 ]]; then printf '%dm %ds'  "$m" "$sec"
    else                       printf '%ds'      "$sec"
    fi
}

calc_burn_rate() {
    local dur="${DURATION_MS:-0}"
    if [[ "$dur" -gt 0 ]]; then
        awk -v cost="${COST_USD:-0}" -v dur="$dur" 'BEGIN {printf "%.2f", (cost * 3600000) / dur}'
    else
        echo "0.00"
    fi
}

# Shorten a path by replacing $HOME with ~ and truncating from left if needed
fmt_path() {
    local path="$1" max="$2"
    path="${path/#$HOME/\~}"
    if [[ ${#path} -gt $max ]]; then
        path="…${path: -$(( max - 1 ))}"
    fi
    echo "$path"
}

# Truncate a plain string
truncate_str() {
    local s="$1" max="$2"
    if [[ ${#s} -gt $max ]]; then
        echo "${s:0:$(( max - 1 ))}…"
    else
        echo "$s"
    fi
}

# Estimate cost from tokens (polling mode)
estimate_cost() {
    local model="$1" in_tok="${2:-0}" out_tok="${3:-0}"
    local pi="${PRICE_INPUT[$model]:-0}"
    local po="${PRICE_OUTPUT[$model]:-0}"
    awk -v in_tok="$in_tok" -v out_tok="$out_tok" -v pi="$pi" -v po="$po" \
        'BEGIN {printf "%.4f", (in_tok/1000000)*pi + (out_tok/1000000)*po}'
}

# ─────────────────────────────── DRAWING ──────────────────────────────────────
#
# Layout:  │  [content_area chars]  │
# content_area = FRAME_WIDTH - 6  (1+2 left, 2+1 right)

draw_hline() {
    local len=$1 s="" i
    for (( i=0; i<len; i++ )); do s+="$BX_H"; done
    printf '%s' "$s"
}

draw_bar() {
    local pct="${1:-0}" width="${2:-20}"
    local pct_int="${pct%.*}"
    (( pct_int < 0   )) && pct_int=0
    (( pct_int > 100 )) && pct_int=100

    local filled=$(( pct_int * width / 100 ))
    local empty=$(( width - filled ))

    local bar_color
    if   (( pct_int <= 50 )); then bar_color="$C_GREEN"
    elif (( pct_int <= 75 )); then bar_color="$C_YELLOW"
    elif (( pct_int <= 90 )); then bar_color="$C_RED"
    else                           bar_color="${C_BOLD}${C_RED}"
    fi

    local bar="" emp="" i
    for (( i=0; i<filled; i++ )); do bar+="$BAR_FULL"; done
    for (( i=0; i<empty;  i++ )); do emp+="$BAR_EMPTY"; done
    printf '%s%s%s%s%s' "$bar_color" "$bar" "${C_GRAY}" "$emp" "${C_RESET}"
}

print_top() {
    local inner=$(( FRAME_WIDTH - 2 ))
    printf '%s%s%s%s%s\n' "${C_BORDER}" "$BX_TL" "$(draw_hline $inner)" "$BX_TR" "${C_RESET}"
}

print_mid() {
    local inner=$(( FRAME_WIDTH - 2 ))
    printf '%s%s%s%s%s\n' "${C_BORDER}" "$BX_ML" "$(draw_hline $inner)" "$BX_MR" "${C_RESET}"
}

print_bot() {
    local inner=$(( FRAME_WIDTH - 2 ))
    printf '%s%s%s%s%s\n' "${C_BORDER}" "$BX_BL" "$(draw_hline $inner)" "$BX_BR" "${C_RESET}"
}

# Single content row. $1=plain text (for width calc), $2=colored text (optional)
print_row() {
    local vis="$1" col="${2:-$1}"
    local ca=$(( FRAME_WIDTH - 6 ))
    local pad=$(( ca - ${#vis} ))
    (( pad < 0 )) && pad=0
    printf '%s%s%s  %s%*s  %s%s%s\n' \
        "${C_BORDER}" "$BX_V" "${C_RESET}" \
        "$col" "$pad" "" \
        "${C_BORDER}" "$BX_V" "${C_RESET}"
}

print_empty() {
    local ca=$(( FRAME_WIDTH - 6 ))
    printf '%s%s%s  %*s  %s%s%s\n' \
        "${C_BORDER}" "$BX_V" "${C_RESET}" \
        "$ca" "" \
        "${C_BORDER}" "$BX_V" "${C_RESET}"
}

# Two-column row: left left-aligned, right right-aligned.
# $1=left_vis $2=left_col $3=right_vis $4=right_col
print_row2() {
    local lv="$1" lc="$2" rv="$3" rc="$4"
    local ca=$(( FRAME_WIDTH - 6 ))
    local pad=$(( ca - ${#lv} - ${#rv} ))
    (( pad < 1 )) && pad=1
    printf '%s%s%s  %s%*s%s  %s%s%s\n' \
        "${C_BORDER}" "$BX_V" "${C_RESET}" \
        "$lc" "$pad" "" "$rc" \
        "${C_BORDER}" "$BX_V" "${C_RESET}"
}

# ─────────────────────────────── DATA READING ─────────────────────────────────

read_bridge_state() {
    [[ -f "$STATE_FILE" ]] || return 1

    local json
    json=$(<"$STATE_FILE") || return 1

    local values
    values=$(printf '%s' "$json" | jq -r '[
        (.model.display_name                                        // "unknown"),
        (.model.id                                                  // "unknown"),
        (.session_id                                                // "unknown"),
        (.cwd                                                       // "unknown"),
        (.cost.total_cost_usd                           // 0 | tostring),
        (.cost.total_duration_ms                        // 0 | tostring),
        (.cost.total_lines_added                        // 0 | tostring),
        (.cost.total_lines_removed                      // 0 | tostring),
        (.context_window.total_input_tokens             // 0 | tostring),
        (.context_window.total_output_tokens            // 0 | tostring),
        (.context_window.context_window_size            // 200000 | tostring),
        (.context_window.used_percentage                // 0 | tostring),
        (.context_window.remaining_percentage           // 100 | tostring),
        (.context_window.current_usage.input_tokens             // 0 | tostring),
        (.context_window.current_usage.output_tokens            // 0 | tostring),
        (.context_window.current_usage.cache_creation_input_tokens // 0 | tostring),
        (.context_window.current_usage.cache_read_input_tokens     // 0 | tostring),
        (.rate_limits.five_hour.used_percentage         // 0 | tostring),
        (if .context_window.current_usage == null then "1" else "0" end)
    ] | .[]') 2>/dev/null || return 1

    local -a a
    mapfile -t a <<< "$values"
    [[ ${#a[@]} -lt 19 ]] && return 1

    MODEL_NAME="${a[0]}"
    MODEL_ID="${a[1]}"
    SESSION_ID="${a[2]}"
    CWD="${a[3]}"
    COST_USD="${a[4]}"
    DURATION_MS="${a[5]}"
    LINES_ADDED="${a[6]}"
    LINES_REMOVED="${a[7]}"
    TOTAL_INPUT="${a[8]}"
    TOTAL_OUTPUT="${a[9]}"
    CTX_SIZE="${a[10]}"
    USED_PCT="${a[11]}"
    REMAINING_PCT="${a[12]}"
    CUR_INPUT="${a[13]}"
    CUR_OUTPUT="${a[14]}"
    CUR_CACHE_W="${a[15]}"
    CUR_CACHE_R="${a[16]}"
    RATE_LIMIT_PCT="${a[17]}"
    CTX_AWAITING="${a[18]}"

    LAST_UPDATE=$(date +%s)
    local mtime; mtime=$(file_mtime "$STATE_FILE")
    DATA_STALE=$(( LAST_UPDATE - mtime ))

    return 0
}

read_polling_state() {
    [[ -f "$STATS_CACHE" ]] || return 1

    local json
    json=$(<"$STATS_CACHE") || return 1

    DAILY_DATE=$(date +%Y-%m-%d)

    local daily_vals
    daily_vals=$(printf '%s' "$json" | jq -r --arg d "$DAILY_DATE" '
        ((.dailyActivity // []) | map(select(.date == $d)) | .[0] // {}) |
        [(.messageCount // 0), (.sessionCount // 0), (.toolCallCount // 0)] | .[]
    ') 2>/dev/null || daily_vals=$'0\n0\n0'

    local -a da
    mapfile -t da <<< "$daily_vals"
    DAILY_MESSAGES="${da[0]:-0}"
    DAILY_SESSIONS="${da[1]:-0}"
    DAILY_TOOL_CALLS="${da[2]:-0}"

    POLLING_MODEL_USAGE=$(printf '%s' "$json" | jq -r '
        .modelUsage // {} | to_entries[] |
        [.key,
         (.value.inputTokens              // 0),
         (.value.outputTokens             // 0),
         (.value.cacheReadInputTokens     // 0),
         (.value.cacheCreationInputTokens // 0)] | @tsv
    ') 2>/dev/null || POLLING_MODEL_USAGE=""

    CACHE_COMPUTED=$(printf '%s' "$json" | jq -r '.lastComputedDate // "unknown"' 2>/dev/null \
        || echo "unknown")

    LAST_UPDATE=$(date +%s)
    return 0
}

# ─────────────────────────────── RENDERING ────────────────────────────────────

render_bridge() {
    local ca=$(( FRAME_WIDTH - 6 ))
    local now; now=$(date +%s)
    local ts; ts=$(date '+%m/%d %H:%M:%S')

    # Header
    local hdr_vis="CCTOKMON · Live"
    local hdr_col="${C_HEADER}CCTOKMON${C_RESET} ${C_GREEN}· Live${C_RESET}"
    local ref_vis="↻ ${REFRESH_INTERVAL}s  ${ts}"
    local ref_col="${C_GRAY}${ref_vis}${C_RESET}"

    print_top
    print_row2 "$hdr_vis" "$hdr_col" "$ref_vis" "$ref_col"
    print_mid

    # Model / session
    local dur; dur=$(format_duration "$DURATION_MS")
    local model_max=$(( ca / 2 - 12 ))
    (( model_max < 8 )) && model_max=8
    local model_s; model_s=$(truncate_str "$MODEL_NAME" $model_max)

    if [[ "$COMPACT" -eq 1 ]]; then
        print_row "Model: ${model_s}" \
                  "${C_LABEL}Model:${C_RESET} ${C_VALUE}${model_s}${C_RESET}"
        print_row "Session: ${dur}" \
                  "${C_LABEL}Session:${C_RESET} ${C_VALUE}${dur}${C_RESET}"
    else
        print_row2 "Model: ${model_s}" \
                   "${C_LABEL}Model:${C_RESET} ${C_VALUE}${model_s}${C_RESET}" \
                   "Session: ${dur}" \
                   "${C_LABEL}Session:${C_RESET} ${C_VALUE}${dur}${C_RESET}"
    fi

    local cwd_s; cwd_s=$(fmt_path "$CWD" "$ca")
    print_row "Project: ${cwd_s}" \
              "${C_LABEL}Project:${C_RESET} ${C_VALUE}${cwd_s}${C_RESET}"

    print_mid

    # Context window
    print_row "CONTEXT WINDOW" "${C_BOLD}CONTEXT WINDOW${C_RESET}"

    local bar_w=$(( ca - 8 ))
    (( bar_w < 10 )) && bar_w=10

    if [[ "${CTX_AWAITING:-1}" == "1" ]]; then
        print_row "  Awaiting first response…" \
                  "  ${C_GRAY}Awaiting first response…${C_RESET}"
    else
        local bar; bar=$(draw_bar "${USED_PCT:-0}" "$bar_w")
        local pct_s="${USED_PCT}%"

        # vis: 2 spaces + bar_w spaces + 2 spaces + pct
        local bar_vis_len=$(( 2 + bar_w + 2 + ${#pct_s} ))
        printf '%s%s%s  %s  %s%s  %s%s%s\n' \
            "${C_BORDER}" "$BX_V" "${C_RESET}" \
            "$bar" "${C_VALUE}${pct_s}${C_RESET}" \
            "$(printf '%*s' $(( ca - bar_vis_len )) '')" \
            "${C_BORDER}" "$BX_V" "${C_RESET}"

        local in_f; in_f=$(fmt_num "$CUR_INPUT")
        local out_f; out_f=$(fmt_num "$CUR_OUTPUT")
        print_row2 "Input:   ${in_f} tok" \
                   "${C_LABEL}Input:${C_RESET}   ${C_VALUE}${in_f} tok${C_RESET}" \
                   "Output:  ${out_f} tok" \
                   "${C_LABEL}Output:${C_RESET}  ${C_VALUE}${out_f} tok${C_RESET}"

        local cr_f; cr_f=$(fmt_num "$CUR_CACHE_R")
        local cw_f; cw_f=$(fmt_num "$CUR_CACHE_W")
        print_row2 "Cache R: ${cr_f} tok" \
                   "${C_LABEL}Cache R:${C_RESET} ${C_VALUE}${cr_f} tok${C_RESET}" \
                   "Cache W: ${cw_f} tok" \
                   "${C_LABEL}Cache W:${C_RESET} ${C_VALUE}${cw_f} tok${C_RESET}"

        local total=$(( CUR_INPUT + CUR_OUTPUT + CUR_CACHE_W + CUR_CACHE_R ))
        local tot_f ctx_f
        tot_f=$(fmt_num "$total")
        ctx_f=$(fmt_num "$CTX_SIZE")
        print_row "Total:   ${tot_f} / ${ctx_f} tok" \
                  "${C_LABEL}Total:${C_RESET}   ${C_VALUE}${tot_f} / ${ctx_f} tok${C_RESET}"
    fi

    print_mid

    # Cost & usage
    print_row "COST & USAGE" "${C_BOLD}COST & USAGE${C_RESET}"

    local cost_f; cost_f=$(fmt_cost "$COST_USD")
    print_row "Session cost:    ${cost_f}" \
              "${C_LABEL}Session cost:${C_RESET}    ${C_VALUE}${cost_f}${C_RESET}"

    print_row "Lines: +${LINES_ADDED} / -${LINES_REMOVED}" \
              "${C_LABEL}Lines:${C_RESET} ${C_GREEN}+${LINES_ADDED}${C_RESET} ${C_GRAY}/${C_RESET} ${C_RED}-${LINES_REMOVED}${C_RESET}"

    local burn; burn=$(calc_burn_rate)
    print_row "Burn rate:       \$${burn}/hr" \
              "${C_LABEL}Burn rate:${C_RESET}       ${C_VALUE}\$${burn}/hr${C_RESET}"

    print_mid

    # Rate limit
    print_row "RATE LIMIT (5hr window)" "${C_BOLD}RATE LIMIT (5hr window)${C_RESET}"

    local rl_int="${RATE_LIMIT_PCT%.*}"
    local rl_bar; rl_bar=$(draw_bar "$rl_int" "$bar_w")
    local rl_s; rl_s=$(LC_NUMERIC=C printf '%.1f%%' "${RATE_LIMIT_PCT:-0}")
    local rl_vis_len=$(( 2 + bar_w + 2 + ${#rl_s} ))
    printf '%s%s%s  %s  %s%s  %s%s%s\n' \
        "${C_BORDER}" "$BX_V" "${C_RESET}" \
        "$rl_bar" "${C_VALUE}${rl_s}${C_RESET}" \
        "$(printf '%*s' $(( ca - rl_vis_len )) '')" \
        "${C_BORDER}" "$BX_V" "${C_RESET}"

    print_mid

    # Footer
    local stale="${DATA_STALE:-0}"
    local age=$(( now - LAST_UPDATE ))
    if [[ "$stale" -gt 300 ]]; then
        local sv="⚠  Data stale — ${stale}s since last update"
        print_row "$sv" "${C_WARN}${sv}${C_RESET}"
    elif [[ "$stale" -gt 60 ]]; then
        local wv="▲ Last update: ${stale}s ago"
        print_row "$wv" "${C_WARN}${wv}${C_RESET}"
    else
        local ov="▲ Updated ${age}s ago"
        print_row "$ov" "${C_GREEN}${ov}${C_RESET}"
    fi

    print_bot
}

render_waiting() {
    local spin_chars=('-' '\\' '|' '/')
    local spin="${spin_chars[$SPIN_IDX]}"
    SPIN_IDX=$(( (SPIN_IDX + 1) % 4 ))

    local ca=$(( FRAME_WIDTH - 6 ))

    print_top
    print_row2 "CCTOKMON · Bridge" "${C_HEADER}CCTOKMON${C_RESET} ${C_GRAY}· Bridge${C_RESET}" \
               "$(date '+%m/%d %H:%M:%S')" "${C_GRAY}$(date '+%m/%d %H:%M:%S')${C_RESET}"
    print_mid
    print_empty
    local wv="${spin} Waiting for Claude Code session…"
    print_row "$wv" "${C_YELLOW}${wv}${C_RESET}"
    print_empty
    local hint; hint=$(fmt_path "$STATE_FILE" "$ca")
    local hv="State: ${hint}"
    print_row "$hv" "${C_GRAY}${hv}${C_RESET}"
    print_empty
    print_bot
}

render_polling() {
    local ca=$(( FRAME_WIDTH - 6 ))
    local ts; ts=$(date '+%m/%d %H:%M:%S')

    print_top
    print_row2 "CCTOKMON · Polling" \
               "${C_HEADER}CCTOKMON${C_RESET} ${C_GRAY}· Polling${C_RESET}" \
               "↻ ${REFRESH_INTERVAL}s  ${ts}" \
               "${C_GRAY}↻ ${REFRESH_INTERVAL}s  ${ts}${C_RESET}"
    print_mid

    # Today's activity
    print_row "TODAY'S USAGE (${DAILY_DATE})" \
              "${C_BOLD}TODAY'S USAGE${C_RESET} ${C_GRAY}(${DAILY_DATE})${C_RESET}"

    local msg_f tc_f
    msg_f=$(fmt_num "$DAILY_MESSAGES")
    tc_f=$(fmt_num "$DAILY_TOOL_CALLS")
    print_row "Msg: ${msg_f}   Sess: ${DAILY_SESSIONS}   Tools: ${tc_f}" \
        "${C_LABEL}Msg:${C_RESET} ${C_VALUE}${msg_f}${C_RESET}   ${C_LABEL}Sess:${C_RESET} ${C_VALUE}${DAILY_SESSIONS}${C_RESET}   ${C_LABEL}Tools:${C_RESET} ${C_VALUE}${tc_f}${C_RESET}"

    print_mid
    print_row "TOKEN TOTALS (all time)" "${C_BOLD}TOKEN TOTALS (all time)${C_RESET}"

    local total_cost=0

    if [[ -n "$POLLING_MODEL_USAGE" ]]; then
        while IFS=$'\t' read -r mid in_tok out_tok cache_r cache_w; do
            [[ -z "$mid" ]] && continue

            local short="${mid#claude-}"
            print_row "  ${short}" "  ${C_CYAN}${short}${C_RESET}"

            local in_f out_f cr_f cw_f
            in_f=$(fmt_num "$in_tok")
            out_f=$(fmt_num "$out_tok")
            cr_f=$(fmt_num "$cache_r")
            cw_f=$(fmt_num "$cache_w")

            if [[ "$COMPACT" -eq 0 ]]; then
                print_row2 "    In: ${in_f}" \
                           "${C_LABEL}    In:${C_RESET} ${C_VALUE}${in_f}${C_RESET}" \
                           "Out: ${out_f}" \
                           "${C_LABEL}Out:${C_RESET} ${C_VALUE}${out_f}${C_RESET}"
                print_row2 "    Cache R: ${cr_f}" \
                           "${C_LABEL}    Cache R:${C_RESET} ${C_VALUE}${cr_f}${C_RESET}" \
                           "Cache W: ${cw_f}" \
                           "${C_LABEL}Cache W:${C_RESET} ${C_VALUE}${cw_f}${C_RESET}"
            else
                print_row "    In: ${in_f}  Out: ${out_f}" \
                          "${C_LABEL}    In:${C_RESET} ${C_VALUE}${in_f}${C_RESET}  ${C_LABEL}Out:${C_RESET} ${C_VALUE}${out_f}${C_RESET}"
            fi

            local mc; mc=$(estimate_cost "$mid" "$in_tok" "$out_tok")
            total_cost=$(awk -v a="$total_cost" -v b="$mc" 'BEGIN {printf "%.4f", a + b}')
        done <<< "$POLLING_MODEL_USAGE"
    else
        print_row "  No model data available" "  ${C_GRAY}No model data available${C_RESET}"
    fi

    print_mid

    print_row "Estimated cost (all time)   \$${total_cost}" \
        "${C_LABEL}Estimated cost (all time)${C_RESET}   ${C_VALUE}\$${total_cost}${C_RESET}"

    print_mid

    # Cache freshness footer
    local cache_mtime; cache_mtime=$(file_mtime "$STATS_CACHE")
    local cache_time
    if [[ "$(uname -s)" == "Darwin" ]]; then
        cache_time=$(date -r "$cache_mtime" '+%H:%M:%S' 2>/dev/null || echo "unknown")
    else
        cache_time=$(date -d "@${cache_mtime}" '+%H:%M:%S' 2>/dev/null || echo "unknown")
    fi

    local fv="▲ Cache computed: ${CACHE_COMPUTED}   updated: ${cache_time}"
    print_row "$fv" "${C_GRAY}${fv}${C_RESET}"
    print_bot
}

# ─────────────────────────────── DIAGNOSTICS ──────────────────────────────────

cmd_check() {
    local ok="${C_GREEN}✓${C_RESET}" warn="${C_YELLOW}⚠${C_RESET}" fail="${C_RED}✗${C_RESET}"

    printf '\ncctokmon v%s — Diagnostics\n' "$VERSION"
    printf '%s\n' "────────────────────────────────"

    # Bash version
    if [[ "${BASH_VERSINFO[0]}" -ge 4 ]]; then
        printf '%b Bash %s\n' "$ok" "$BASH_VERSION"
    else
        printf '%b Bash %s — need 4.0+\n' "$fail" "$BASH_VERSION"
    fi

    # jq
    if command -v jq >/dev/null 2>&1; then
        printf '%b jq %s\n' "$ok" "$(jq --version)"
    else
        printf '%b jq not found\n     apt install jq  /  brew install jq\n' "$fail"
    fi

    # tput / colors
    local nc; nc=$(tput colors 2>/dev/null || echo 0)
    if [[ "$nc" -ge 8 ]]; then
        printf '%b Terminal: %dx%d, %d colors\n' "$ok" "$(tput cols)" "$(tput lines)" "$nc"
    else
        printf '%b Terminal: %dx%d, %d colors (limited display)\n' "$warn" \
            "$(tput cols)" "$(tput lines)" "$nc"
    fi

    # Stats cache
    if [[ -f "$STATS_CACHE" ]]; then
        local age=$(( $(date +%s) - $(file_mtime "$STATS_CACHE") ))
        printf '%b stats-cache.json  (%ds old)\n' "$ok" "$age"
    else
        printf '%b stats-cache.json not found: %s\n' "$warn" "$STATS_CACHE"
    fi

    # State file (bridge)
    if [[ -f "$STATE_FILE" ]]; then
        local sage=$(( $(date +%s) - $(file_mtime "$STATE_FILE") ))
        if [[ $sage -gt 300 ]]; then
            printf '%b State file stale: %ds old\n     %s\n' "$warn" "$sage" "$STATE_FILE"
        else
            printf '%b State file: %ds old\n' "$ok" "$sage"
        fi
    else
        # Check if statusline is configured — if so, the file will appear after the next Claude Code interaction
        local _sl=""
        local _settings="$HOME/.claude/settings.json"
        [[ -f "$_settings" ]] && command -v jq >/dev/null 2>&1 && \
            _sl=$(jq -r '.statusLine // empty' "$_settings" 2>/dev/null || true)
        if [[ -n "$_sl" ]]; then
            printf '%b State file not found yet\n     Statusline is configured — start a Claude Code session to populate it.\n' "$warn"
        else
            printf '%b State file not found: %s\n     Run: cctokmon --setup\n' "$warn" "$STATE_FILE"
        fi
    fi

    # Claude Code statusline
    local settings="$HOME/.claude/settings.json"
    if [[ -f "$settings" ]] && command -v jq >/dev/null 2>&1; then
        local sl; sl=$(jq -r '.statusLine // empty' "$settings" 2>/dev/null || true)
        if [[ -n "$sl" ]]; then
            printf '%b statusLine: %s\n' "$ok" "$sl"
        else
            printf '%b No statusLine in ~/.claude/settings.json\n     Run: cctokmon --setup\n' "$warn"
        fi
    else
        printf '%b ~/.claude/settings.json not found\n' "$warn"
    fi

    # JSONL projects dir
    if [[ -d "$PROJECTS_DIR" ]]; then
        local n; n=$(find "$PROJECTS_DIR" -maxdepth 1 -mindepth 1 -type d 2>/dev/null | wc -l)
        printf '%b Projects dir: %d project(s)\n' "$ok" "$n"
    else
        printf '%b Projects dir not found: %s\n' "$warn" "$PROJECTS_DIR"
    fi

    printf '\n'
}

# ─────────────────────────────── CLI & CONFIG ─────────────────────────────────

print_help() {
    cat <<EOF
Usage: cctokmon [OPTIONS]

Options:
  -m, --mode   MODE   Mode: bridge (default) or polling
  -r, --refresh SECS  Refresh interval in seconds (default: 10)
  -t, --theme  THEME  Theme: default, minimal, none
  -s, --scope  SCOPE  Cost scope: session, daily, cumulative
  -c, --config FILE   Config file path
  -h, --help          Show this help
  -v, --version       Show version
      --setup         Run installer
      --check         Run diagnostics

Files:
  State:  ${STATE_FILE}
  Cache:  ${STATS_CACHE}
  Config: ${CONFIG_FILE}
EOF
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            -m|--mode)    MODE="$2";             shift 2 ;;
            -r|--refresh) REFRESH_INTERVAL="$2"; shift 2 ;;
            -t|--theme)   THEME="$2";            shift 2 ;;
            -s|--scope)   COST_SCOPE="$2";       shift 2 ;;
            -c|--config)  CONFIG_FILE="$2";      shift 2 ;;
            -h|--help)    print_help; exit 0 ;;
            -v|--version) printf 'cctokmon v%s\n' "$VERSION"; exit 0 ;;
            --setup)
                exec "${SCRIPT_DIR}/install.sh"
                ;;
            --check)
                init_theme
                cmd_check
                exit 0
                ;;
            *)
                printf 'Unknown option: %s\n' "$1" >&2
                print_help >&2
                exit 1
                ;;
        esac
    done

    if [[ "$MODE" != "bridge" && "$MODE" != "polling" ]]; then
        printf 'Invalid mode: %s (use bridge or polling)\n' "$MODE" >&2; exit 1
    fi
}

load_config() {
    [[ -f "$CONFIG_FILE" ]] || return 0
    # shellcheck source=/dev/null
    source "$CONFIG_FILE"
}

check_deps() {
    if [[ "${BASH_VERSINFO[0]}" -lt 4 ]]; then
        printf 'cctokmon requires Bash 4.0+. You have %s.\n' "$BASH_VERSION" >&2
        printf 'macOS users: brew install bash\n' >&2
        exit 1
    fi
    if ! command -v jq >/dev/null 2>&1; then
        printf 'cctokmon requires jq.\n  Ubuntu/Debian: apt install jq\n  macOS: brew install jq\n' >&2
        exit 1
    fi
}

# ─────────────────────────────── MAIN LOOP ────────────────────────────────────

main_loop() {
    while true; do
        update_term_size

        if [[ "$NEEDS_CLEAR" -eq 1 ]]; then
            tput clear
            NEEDS_CLEAR=0
        fi

        tput cup 0 0

        if [[ "$MODE" == "bridge" ]]; then
            if read_bridge_state; then
                render_bridge
            else
                render_waiting
            fi
        else
            if read_polling_state; then
                render_polling
            else
                print_top
                local mv="Stats cache not found: ${STATS_CACHE}"
                print_row "$mv" "${C_WARN}${mv}${C_RESET}"
                print_bot
            fi
        fi

        # Clear any stale content below the frame (e.g. after resize)
        tput ed 2>/dev/null || true

        sleep "$REFRESH_INTERVAL"
    done
}

main() {
    parse_args "$@"
    load_config
    check_deps
    init_charset
    init_theme
    update_term_size

    trap cleanup SIGINT SIGTERM EXIT
    trap 'NEEDS_CLEAR=1' SIGWINCH

    tput civis 2>/dev/null || true

    main_loop
}

main "$@"
