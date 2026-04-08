#!/usr/bin/env bash
# cctokmon installer
# Installs cctokmon and optionally configures the Claude Code statusline bridge.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN_DIR="$HOME/.local/bin"
CLAUDE_DIR="$HOME/.claude"
CONFIG_DIR="$HOME/.config/cctokmon"
SETTINGS_FILE="$CLAUDE_DIR/settings.json"
STATUSLINE_DEST="$CLAUDE_DIR/cctokmon-statusline.sh"

# ── Colors ─────────────────────────────────────────────────────────────────────
if tput colors &>/dev/null 2>&1 && [[ "$(tput colors)" -ge 8 ]]; then
    R=$(tput sgr0)
    BOLD=$(tput bold)
    GREEN=$(tput setaf 2)
    YELLOW=$(tput setaf 3)
    RED=$(tput setaf 1)
    CYAN=$(tput setaf 6)
else
    R="" BOLD="" GREEN="" YELLOW="" RED="" CYAN=""
fi

ok()   { printf '%s✓%s %s\n' "${GREEN}" "${R}" "$*"; }
miss() { printf '%s✗%s %s\n' "${RED}" "${R}" "$*"; }
warn() { printf '%s⚠%s  %s\n' "${YELLOW}" "${R}" "$*"; }
err()  { printf '%s✗%s %s\n' "${RED}" "${R}" "$*" >&2; }
info() { printf '  %s\n' "$*"; }

# ── Detect OS and package manager ──────────────────────────────────────────────
OS="$(uname -s)"
PKG_MGR=""
if   [[ "$OS" == "Darwin" ]] && command -v brew    >/dev/null 2>&1; then PKG_MGR="brew"
elif command -v apt-get >/dev/null 2>&1;                               then PKG_MGR="apt"
elif command -v dnf     >/dev/null 2>&1;                               then PKG_MGR="dnf"
elif command -v yum     >/dev/null 2>&1;                               then PKG_MGR="yum"
fi

# Package name map: tool → apt/dnf/brew package
declare -A PKG_APT PKG_BREW PKG_DNF
PKG_APT["jq"]="jq"
PKG_APT["tput"]="ncurses-bin"
PKG_APT["awk"]="gawk"
PKG_BREW["jq"]="jq"
PKG_BREW["tput"]="ncurses"
PKG_BREW["awk"]="gawk"
PKG_DNF["jq"]="jq"
PKG_DNF["tput"]="ncurses"
PKG_DNF["awk"]="gawk"

pkg_name() {
    local tool="$1"
    case "$PKG_MGR" in
        brew) echo "${PKG_BREW[$tool]:-$tool}" ;;
        dnf|yum) echo "${PKG_DNF[$tool]:-$tool}" ;;
        *) echo "${PKG_APT[$tool]:-$tool}" ;;
    esac
}

# ── Dependency checks ──────────────────────────────────────────────────────────

printf '\n%scctokmon installer%s\n' "${BOLD}${CYAN}" "${R}"
printf '%s\n\n' "──────────────────"

# Bash 4+ — can't auto-install, must exit immediately
if [[ "${BASH_VERSINFO[0]}" -lt 4 ]]; then
    err "Bash 4.0+ required. You have ${BASH_VERSION}."
    if [[ "$OS" == "Darwin" ]]; then
        info "Install: brew install bash"
    fi
    exit 1
fi
ok "Bash ${BASH_VERSION}"

# Check remaining deps, collecting any that are missing
MISSING_TOOLS=()

for tool in jq tput awk; do
    if command -v "$tool" >/dev/null 2>&1; then
        case "$tool" in
            jq)   ok "jq $(jq --version)" ;;
            tput) ok "tput (ncurses)" ;;
            awk)  ok "awk" ;;
        esac
    else
        miss "${tool} — not found"
        MISSING_TOOLS+=("$tool")
    fi
done

# ── Offer to install missing deps ──────────────────────────────────────────────

if [[ ${#MISSING_TOOLS[@]} -gt 0 ]]; then
    printf '\n%sMissing: %s%s\n' "${BOLD}" "${MISSING_TOOLS[*]}" "${R}"

    if [[ -z "$PKG_MGR" ]]; then
        err "No supported package manager found (apt, dnf, yum, brew)."
        info "Install manually and re-run:"
        for tool in "${MISSING_TOOLS[@]}"; do
            info "  ${tool}"
        done
        exit 1
    fi

    # Build the list of package names for the detected manager
    PKG_LIST=()
    for tool in "${MISSING_TOOLS[@]}"; do
        PKG_LIST+=("$(pkg_name "$tool")")
    done

    case "$PKG_MGR" in
        brew)
            printf '  Install with Homebrew (%s)? [Y/n] ' "${PKG_LIST[*]}"
            read -r ans
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: brew install ${PKG_LIST[*]}"; exit 1
            fi
            brew install "${PKG_LIST[@]}"
            ;;
        apt)
            printf '  Install with apt (%s)? [Y/n] ' "${PKG_LIST[*]}"
            read -r ans
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: sudo apt install ${PKG_LIST[*]}"; exit 1
            fi
            sudo apt-get install -y "${PKG_LIST[@]}"
            ;;
        dnf|yum)
            printf '  Install with %s (%s)? [Y/n] ' "$PKG_MGR" "${PKG_LIST[*]}"
            read -r ans
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: sudo $PKG_MGR install ${PKG_LIST[*]}"; exit 1
            fi
            sudo "$PKG_MGR" install -y "${PKG_LIST[@]}"
            ;;
    esac

    # Re-verify after install
    printf '\n'
    for tool in "${MISSING_TOOLS[@]}"; do
        if command -v "$tool" >/dev/null 2>&1; then
            ok "${tool} installed"
        else
            err "${tool} still not found — install manually and re-run."
            exit 1
        fi
    done
fi

# ── Install files ──────────────────────────────────────────────────────────────

# Config dir
mkdir -p "$CONFIG_DIR"
if [[ ! -f "${CONFIG_DIR}/config" ]]; then
    cp "${SCRIPT_DIR}/config.example" "${CONFIG_DIR}/config"
    ok "Config created: ${CONFIG_DIR}/config"
else
    ok "Config exists:  ${CONFIG_DIR}/config  (not overwritten)"
fi

# Bin dir
mkdir -p "$BIN_DIR"

# Symlink main script
chmod +x "${SCRIPT_DIR}/cctokmon.sh"
ln -sf "${SCRIPT_DIR}/cctokmon.sh" "${BIN_DIR}/cctokmon"
ok "Symlinked: ${BIN_DIR}/cctokmon → ${SCRIPT_DIR}/cctokmon.sh"

# Copy statusline companion
chmod +x "${SCRIPT_DIR}/cctokmon-statusline.sh"
cp "${SCRIPT_DIR}/cctokmon-statusline.sh" "$STATUSLINE_DEST"
chmod +x "$STATUSLINE_DEST"
ok "Statusline: ${STATUSLINE_DEST}"

# PATH notice
if ! command -v cctokmon >/dev/null 2>&1; then
    warn "${BIN_DIR} is not in your PATH."
    info "Add this to your ~/.bashrc or ~/.zshrc:"
    info "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi

# ── Configure Claude Code statusline ──────────────────────────────────────────

printf '\n%sConfigure Claude Code statusline bridge?%s\n' "${BOLD}" "${R}"
printf 'This lets cctokmon receive real-time data from Claude Code.\n'
printf 'It sets "statusLine" in ~/.claude/settings.json.\n\n'

# Check if already configured
if [[ -f "$SETTINGS_FILE" ]] && command -v jq >/dev/null 2>&1; then
    existing_sl=$(jq -r '.statusLine // empty' "$SETTINGS_FILE" 2>/dev/null || true)
    if [[ -n "$existing_sl" ]]; then
        warn "A statusLine is already configured: ${existing_sl}"
        printf '  Overwrite with cctokmon? [y/N] '
        read -r ans
        if [[ "${ans,,}" != "y" ]]; then
            info "Skipped. To configure manually:"
            info "  Set \"statusLine\": \"${STATUSLINE_DEST}\" in ~/.claude/settings.json"
            configure_sl=0
        else
            configure_sl=1
        fi
    else
        printf '  Configure now? [Y/n] '
        read -r ans
        configure_sl=1
        [[ "${ans,,}" == "n" ]] && configure_sl=0
    fi
else
    printf '  Configure now? [Y/n] '
    read -r ans
    configure_sl=1
    [[ "${ans,,}" == "n" ]] && configure_sl=0
fi

if [[ "${configure_sl:-0}" -eq 1 ]]; then
    # Create settings.json if it doesn't exist
    if [[ ! -f "$SETTINGS_FILE" ]]; then
        printf '{}' > "$SETTINGS_FILE"
    fi

    # Update with jq
    local_tmp=$(mktemp)
    jq --arg sl "$STATUSLINE_DEST" '.statusLine = {"type": "command", "command": $sl}' "$SETTINGS_FILE" > "$local_tmp"
    mv "$local_tmp" "$SETTINGS_FILE"
    ok "statusLine set in ${SETTINGS_FILE}"
fi

# ── Done ───────────────────────────────────────────────────────────────────────

printf '\n%sInstallation complete!%s\n\n' "${BOLD}${GREEN}" "${R}"
printf 'Run the monitor:\n'
printf '  %scctokmon%s              # bridge mode (real-time, requires statusline)\n' "${BOLD}" "${R}"
printf '  %scctokmon --mode polling%s   # polling mode (stats-cache.json)\n' "${BOLD}" "${R}"
printf '  %scctokmon --check%s       # verify setup\n\n' "${BOLD}" "${R}"

if [[ "${configure_sl:-0}" -eq 1 ]]; then
    printf 'The statusline bridge is active. Claude Code will update cctokmon\n'
    printf 'on every interaction. Open cctokmon in a separate terminal pane.\n\n'
fi
