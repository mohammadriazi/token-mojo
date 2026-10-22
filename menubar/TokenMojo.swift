// TokenMojo — macOS menu bar companion for cctokmon.
// Reads the statusline bridge file (~/.claude/cctokmon-state.json) and shows
// rate-limit gauges in the menu bar, with a session dashboard in the dropdown.
import AppKit
import ServiceManagement
import SwiftUI

struct BridgeState: Decodable {
    struct Model: Decodable { let displayName: String? }
    struct Workspace: Decodable { let currentDir: String? }
    struct Cost: Decodable {
        let totalCostUsd: Double?
        let totalLinesAdded: Int?
        let totalLinesRemoved: Int?
    }
    struct ContextWindow: Decodable {
        let totalInputTokens: Int?
        let totalOutputTokens: Int?
        let contextWindowSize: Int?
        let usedPercentage: Double?
    }
    struct Limit: Decodable { let usedPercentage: Double?; let resetsAt: Double? }
    struct RateLimits: Decodable { let fiveHour: Limit?; let sevenDay: Limit? }

    let sessionId: String?
    let sessionName: String?
    let model: Model?
    let workspace: Workspace?
    let cost: Cost?
    let contextWindow: ContextWindow?
    let rateLimits: RateLimits?
}

struct GlanceGauge {
    let label: String
    let percent: Double?
}

struct SessionSnapshot: Identifiable {
    let id: String
    let state: BridgeState
    let updated: Date
}

final class MenuBarController: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let stateURL = URL(fileURLWithPath: ProcessInfo.processInfo.environment["CCTOKMON_STATE_FILE"]
        ?? NSHomeDirectory() + "/.claude/cctokmon-state.json")
    private let sessionsURL = URL(fileURLWithPath: ProcessInfo.processInfo.environment["CCTOKMON_SESSIONS_DIR"]
        ?? NSHomeDirectory() + "/.claude/cctokmon-sessions")
    private var timer: Timer?
    private var popOut: NSPanel?
    private var popOutView: NSHostingView<DashboardPanel>?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        refreshGlance()
        // The bridge replaces the file via rename, so poll rather than watch the inode.
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in self?.refreshGlance() }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        switch loadState() {
        case .failure(let error):
            menu.addItem(NSMenuItem(title: error.localizedDescription, action: nil, keyEquivalent: ""))
        case .success(let (state, updated)):
            let panel = NSHostingView(rootView: DashboardPanel(state: state, updated: updated, sessions: loadSessions()))
            panel.frame.size = panel.fittingSize
            let item = NSMenuItem()
            item.view = panel
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let popOutItem = NSMenuItem(title: "Pop Out Window", action: #selector(showPopOut), keyEquivalent: "p")
        popOutItem.target = self
        menu.addItem(popOutItem)
        let login = NSMenuItem(title: "Launch at Login", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
        login.target = self
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    private func refreshGlance() {
        guard let button = statusItem.button else { return }
        guard case .success(let (state, updated)) = loadState() else {
            button.image = nil
            button.title = "Mojo –"
            button.toolTip = "TokenMojo: no bridge data yet"
            return
        }
        let cost = String(format: "$%.2f", state.cost?.totalCostUsd ?? 0)
        var gauges: [GlanceGauge] = []
        // API-key users get no rate limits, so only the context gauge shows for them.
        if let limits = state.rateLimits {
            gauges = [GlanceGauge(label: "5h", percent: livePercent(limits.fiveHour)),
                      GlanceGauge(label: "7d", percent: livePercent(limits.sevenDay))]
        }
        gauges.append(GlanceGauge(label: "ctx", percent: state.contextWindow?.usedPercentage))
        let summary = (gauges.map { "\($0.label) \(percentText($0.percent))" } + [cost]).joined(separator: " · ")
        button.title = ""
        button.image = glanceImage(gauges: gauges, trailing: cost)
        button.image?.accessibilityDescription = "Claude Code usage: " + summary
        button.toolTip = summary
        popOutView?.rootView = DashboardPanel(state: state, updated: updated, sessions: loadSessions())
    }

    @objc private func showPopOut() {
        if popOut == nil {
            let state: BridgeState, updated: Date
            switch loadState() {
            case .failure(let error):
                NSAlert(error: error).runModal()
                return
            case .success(let loaded):
                (state, updated) = loaded
            }
            let view = NSHostingView(rootView: DashboardPanel(state: state, updated: updated, sessions: loadSessions()))
            let panel = NSPanel(contentRect: NSRect(origin: .zero, size: view.fittingSize),
                                styleMask: [.titled, .closable, .utilityWindow, .nonactivatingPanel],
                                backing: .buffered, defer: false)
            panel.title = "TokenMojo"
            panel.contentView = view
            panel.level = .floating
            panel.isFloatingPanel = true
            // Accessory apps are never "active", so the default would hide the panel immediately.
            panel.hidesOnDeactivate = false
            panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            panel.isReleasedWhenClosed = false
            panel.center()
            panel.setFrameAutosaveName("TokenMojoPopOut")
            popOut = panel
            popOutView = view
        }
        popOut?.orderFrontRegardless()
    }

    private func loadState() -> Result<(BridgeState, Date), Error> {
        Result {
            let data = try Data(contentsOf: stateURL)
            let decoder = JSONDecoder()
            decoder.keyDecodingStrategy = .convertFromSnakeCase
            let state = try decoder.decode(BridgeState.self, from: data)
            let updated = try FileManager.default.attributesOfItem(atPath: stateURL.path)[.modificationDate] as? Date
            return (state, updated ?? Date())
        }
    }

    // Claude Code signals no session end, so "recent" is the best proxy for "open".
    private func loadSessions() -> [SessionSnapshot] {
        let files: [URL]
        do {
            files = try FileManager.default.contentsOfDirectory(at: sessionsURL,
                                                                includingPropertiesForKeys: [.contentModificationDateKey])
        } catch let error as CocoaError where error.code == .fileReadNoSuchFile {
            return []  // Directory appears after the first bridge write.
        } catch {
            NSLog("TokenMojo: cannot list %@: %@", sessionsURL.path, error.localizedDescription)
            return []
        }
        let cutoff = Date().addingTimeInterval(-12 * 3600)
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .convertFromSnakeCase
        let sessions: [SessionSnapshot] = files.compactMap { file in
            guard file.pathExtension == "json",
                  let updated = try? file.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate,
                  updated > cutoff,
                  // Writes are atomic renames, so an undecodable file is foreign and safe to skip.
                  let data = try? Data(contentsOf: file),
                  let state = try? decoder.decode(BridgeState.self, from: data) else { return nil }
            return SessionSnapshot(id: file.deletingPathExtension().lastPathComponent, state: state, updated: updated)
        }
        return sessions.sorted { $0.updated > $1.updated }
    }

    @objc private func toggleLaunchAtLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSAlert(error: error).runModal()
        }
    }
}

struct DashboardPanel: View {
    let state: BridgeState
    let updated: Date
    let sessions: [SessionSnapshot]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            if let limits = state.rateLimits {
                HStack(spacing: 10) {
                    LimitRing(title: "5-hour limit", limit: limits.fiveHour)
                    LimitRing(title: "7-day limit", limit: limits.sevenDay)
                }
            }
            if let context = state.contextWindow {
                ContextCard(context: context)
            }
            statTiles
            if !sessions.isEmpty {
                sessionList
            }
            Label("Updated \(RelativeDateTimeFormatter().localizedString(for: updated, relativeTo: Date()))",
                  systemImage: "clock")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(14)
        .frame(width: 340)
    }

    private var header: some View {
        HStack(spacing: 10) {
            Image(systemName: "sparkles")
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 36, height: 36)
                .background(LinearGradient(colors: [.orange, .pink], startPoint: .topLeading, endPoint: .bottomTrailing),
                            in: RoundedRectangle(cornerRadius: 9))
            VStack(alignment: .leading, spacing: 2) {
                Text(state.model?.displayName ?? "Claude Code").font(.headline)
                if let name = state.sessionName {
                    Text(name).font(.subheadline).lineLimit(1)
                }
                if let dir = state.workspace?.currentDir {
                    Label(dir.replacingOccurrences(of: NSHomeDirectory(), with: "~"), systemImage: "folder")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            }
        }
    }

    private var sessionList: some View {
        let totalCost = sessions.reduce(0) { $0 + ($1.state.cost?.totalCostUsd ?? 0) }
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Label("Sessions · last 12h", systemImage: "rectangle.stack")
                    .font(.subheadline.weight(.semibold))
                Spacer()
                Text("\(sessions.count) · \(String(format: "$%.2f", totalCost)) total")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }
            .padding(.bottom, 4)
            ForEach(sessions.prefix(10)) { session in
                SessionRow(session: session, isCurrent: session.id == state.sessionId)
            }
        }
        .padding(12)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
    }

    private var statTiles: some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible())], spacing: 10) {
            StatTile(icon: "dollarsign.circle.fill", tint: .green, label: "Session cost",
                     value: String(format: "$%.4f", state.cost?.totalCostUsd ?? 0))
            StatTile(icon: "plus.forwardslash.minus", tint: .orange, label: "Lines changed",
                     value: "+\((state.cost?.totalLinesAdded ?? 0).formatted()) / −\((state.cost?.totalLinesRemoved ?? 0).formatted())")
            StatTile(icon: "arrow.down.circle.fill", tint: .blue, label: "Input tokens",
                     value: (state.contextWindow?.totalInputTokens ?? 0).formatted())
            StatTile(icon: "arrow.up.circle.fill", tint: .purple, label: "Output tokens",
                     value: (state.contextWindow?.totalOutputTokens ?? 0).formatted())
        }
    }
}

struct LimitRing: View {
    let title: String
    let limit: BridgeState.Limit?

    var body: some View {
        let percent = livePercent(limit)
        let color = Color(nsColor: severity(percent ?? 0))
        VStack(spacing: 6) {
            ZStack {
                Circle().stroke(Color.primary.opacity(0.1), lineWidth: 9)
                Circle()
                    .trim(from: 0, to: min(percent ?? 0, 100) / 100)
                    .stroke(LinearGradient(colors: [color.opacity(0.7), color], startPoint: .top, endPoint: .bottom),
                            style: StrokeStyle(lineWidth: 9, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                Text(percentText(percent))
                    .font(.system(size: 20, weight: .bold, design: .rounded))
                    .monospacedDigit()
            }
            .frame(width: 76, height: 76)
            Text(title).font(.subheadline.weight(.semibold))
            Text(resetText(limit)).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 12)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
    }
}

struct ContextCard: View {
    let context: BridgeState.ContextWindow

    var body: some View {
        let percent = context.usedPercentage ?? 0
        let size = context.contextWindowSize.map { $0 >= 1_000_000 ? "\($0 / 1_000_000)M" : "\($0 / 1000)k" } ?? "?"
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Label("Context window", systemImage: "brain.head.profile")
                    .font(.subheadline.weight(.semibold))
                Spacer()
                Text("\(percentText(percent)) of \(size)").font(.subheadline).monospacedDigit()
            }
            CapsuleBar(percent: percent).frame(height: 8)
        }
        .padding(12)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
    }
}

struct SessionRow: View {
    let session: SessionSnapshot
    let isCurrent: Bool

    var body: some View {
        let state = session.state
        let folder = state.workspace?.currentDir.map { URL(fileURLWithPath: $0).lastPathComponent }
        let isActive = session.updated.timeIntervalSinceNow > -600
        let context = state.contextWindow?.usedPercentage ?? 0
        let ageFormatter = RelativeDateTimeFormatter()
        ageFormatter.unitsStyle = .abbreviated
        return HStack(spacing: 10) {
            Circle()
                .fill(isActive ? Color.green : Color.secondary.opacity(0.4))
                .frame(width: 8, height: 8)
                .help(isActive ? "Active in the last 10 minutes" : "Idle")
            VStack(alignment: .leading, spacing: 2) {
                Text(state.sessionName ?? folder ?? "Session")
                    .font(.subheadline.weight(.semibold))
                    .lineLimit(1)
                Text([folder, state.model?.displayName,
                      ageFormatter.localizedString(for: session.updated, relativeTo: Date())]
                        .compactMap { $0 }.joined(separator: " · "))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 6)
            VStack(alignment: .trailing, spacing: 4) {
                Text(String(format: "$%.2f", state.cost?.totalCostUsd ?? 0))
                    .font(.subheadline.weight(.semibold))
                    .monospacedDigit()
                HStack(spacing: 4) {
                    CapsuleBar(percent: context).frame(width: 36, height: 5)
                    Text(percentText(context)).font(.caption2).monospacedDigit()
                }
            }
        }
        .padding(.vertical, 6)
        .padding(.horizontal, 8)
        .background(isCurrent ? Color.accentColor.opacity(0.15) : Color.clear, in: RoundedRectangle(cornerRadius: 8))
    }
}

struct CapsuleBar: View {
    let percent: Double

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.1))
                Capsule()
                    .fill(LinearGradient(colors: [.teal, Color(nsColor: severity(percent))],
                                         startPoint: .leading, endPoint: .trailing))
                    .frame(width: geometry.size.width * min(percent, 100) / 100)
            }
        }
    }
}

struct StatTile: View {
    let icon: String
    let tint: Color
    let label: String
    let value: String

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: icon)
                .font(.system(size: 18))
                .foregroundStyle(tint)
            VStack(alignment: .leading, spacing: 1) {
                Text(value).font(.system(.body, design: .rounded).weight(.semibold)).monospacedDigit()
                Text(label).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(10)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
    }
}

/// One-row menu bar glance: label, ring gauge and percentage per limit, then a trailing note.
func glanceImage(gauges: [GlanceGauge], trailing: String) -> NSImage {
    let labelFont = NSFont.systemFont(ofSize: 11, weight: .semibold)
    let valueFont = NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
    let ring: CGFloat = 15, lineWidth: CGFloat = 2.5, gap: CGFloat = 4, groupGap: CGFloat = 8
    let groupWidths = gauges.map {
        textWidth($0.label, labelFont) + gap + ring + gap + textWidth(percentText($0.percent), valueFont) + groupGap
    }
    let height = NSStatusBar.system.thickness
    let size = NSSize(width: groupWidths.reduce(0, +) + textWidth(trailing, valueFont), height: height)

    // Colors resolve at draw time, so the glance follows light/dark menu bars.
    return NSImage(size: size, flipped: false) { _ in
        let midY = height / 2
        var x: CGFloat = 0
        for gauge in gauges {
            drawText(gauge.label, font: labelFont, color: .labelColor, x: x, midY: midY)
            x += textWidth(gauge.label, labelFont) + gap
            let center = NSPoint(x: x + ring / 2, y: midY)
            let radius = (ring - lineWidth) / 2
            let track = NSBezierPath(ovalIn: NSRect(x: center.x - radius, y: center.y - radius,
                                                    width: radius * 2, height: radius * 2))
            track.lineWidth = lineWidth
            NSColor.tertiaryLabelColor.setStroke()
            track.stroke()
            if let pct = gauge.percent, pct > 0 {
                let arc = NSBezierPath()
                arc.appendArc(withCenter: center, radius: radius, startAngle: 90,
                              endAngle: 90 - 360 * min(pct, 100) / 100, clockwise: true)
                arc.lineWidth = lineWidth
                arc.lineCapStyle = .round
                severity(pct).setStroke()
                arc.stroke()
            }
            x += ring + gap
            let value = percentText(gauge.percent)
            drawText(value, font: valueFont, color: .labelColor, x: x, midY: midY)
            x += textWidth(value, valueFont) + groupGap
        }
        drawText(trailing, font: valueFont, color: .labelColor, x: x, midY: midY)
        return true
    }
}

// nil once the window has reset: the bridge only refreshes on the next Claude Code interaction.
func livePercent(_ limit: BridgeState.Limit?) -> Double? {
    guard let pct = limit?.usedPercentage else { return nil }
    if let resetsAt = limit?.resetsAt, Date(timeIntervalSince1970: resetsAt) < Date() { return nil }
    return pct
}

func resetText(_ limit: BridgeState.Limit?) -> String {
    guard let resetsAt = limit?.resetsAt else { return limit?.usedPercentage == nil ? "n/a" : " " }
    let reset = Date(timeIntervalSince1970: resetsAt)
    if reset < Date() { return "Reset — awaiting update" }
    let when = reset.timeIntervalSinceNow > 86_400
        ? reset.formatted(.dateTime.weekday().hour().minute())
        : reset.formatted(date: .omitted, time: .shortened)
    return "Resets \(when)"
}

func severity(_ pct: Double) -> NSColor {
    pct >= 80 ? .systemRed : pct >= 50 ? .systemOrange : .systemGreen
}

func percentText(_ pct: Double?) -> String {
    pct.map { "\(Int($0))%" } ?? "–"
}

private func textWidth(_ text: String, _ font: NSFont) -> CGFloat {
    ceil(NSAttributedString(string: text, attributes: [.font: font]).size().width)
}

private func drawText(_ text: String, font: NSFont, color: NSColor, x: CGFloat, midY: CGFloat) {
    let string = NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color])
    string.draw(at: NSPoint(x: x, y: midY - string.size().height / 2))
}

let app = NSApplication.shared
let controller = MenuBarController()
app.delegate = controller
app.setActivationPolicy(.accessory)
app.run()
