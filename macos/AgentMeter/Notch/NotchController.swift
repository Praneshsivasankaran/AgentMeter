import AppKit
import CoreGraphics
import QuartzCore
import SwiftUI

private final class MonitorPanel: NSPanel {
  override var canBecomeKey: Bool { false }
  override var canBecomeMain: Bool { false }
}
final class TrackingSurface: NSView {
  var appearanceChanged: () -> Void = {}
  override func viewDidChangeEffectiveAppearance() {
    super.viewDidChangeEffectiveAppearance()
    appearanceChanged()
  }
  override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
  var hover: (Bool) -> Void = { _ in }
  var clicked: () -> Void = {}
  private var tracking: NSTrackingArea?
  override func updateTrackingAreas() {
    super.updateTrackingAreas()
    if let tracking { removeTrackingArea(tracking) }
    tracking = NSTrackingArea(
      rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self)
    addTrackingArea(tracking!)
  }
  override func hitTest(_ point: NSPoint) -> NSView? {
    bounds.contains(convert(point, from: superview)) ? self : nil
  }
  override func mouseEntered(with event: NSEvent) { hover(true) }
  override func mouseExited(with event: NSEvent) { hover(false) }
  override func mouseDown(with event: NSEvent) { clicked() }
  override func accessibilityPerformPress() -> Bool {
    clicked()
    return true
  }
}
@MainActor final class NotchController {
  private let panel = MonitorPanel(
    contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered,
    defer: false)
  private let presentation = NotchPresentation()
  private let surface = TrackingSurface()
  private let material = NSVisualEffectView()
  private let tint = NSView()
  private var observers: [NSObjectProtocol] = []
  private var hoverWork: DispatchWorkItem?
  private var generation = 0
  private var click: () -> Void = {}
  private(set) var focusPreserved = true
  var text: String {
    presentation.rows.map { $0.id.title + " " + $0.percentage }.joined(separator: " | ")
  }
  var isVisible: Bool { panel.isVisible }
  var onActiveSpace: Bool { panel.isOnActiveSpace }
  var onScreen: Bool { panel.occlusionState.contains(.visible) }
  var frame: NSRect { panel.frame }
  var phase: String { presentation.state.phase.rawValue }
  var effectiveAppearance: NSAppearance { panel.effectiveAppearance }
  func applyAppearance(_ appearance: AppAppearance) {
    panel.appearance = appearance.native
    updateMaterial()
  }
  init(open: @escaping () -> Void = {}) {
    click = open
    panel.isOpaque = false
    panel.backgroundColor = .clear
    panel.hidesOnDeactivate = false
    panel.level = .statusBar
    panel.hasShadow = true
    panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    if #available(macOS 15.0, *) { panel.collectionBehavior.insert(.canJoinAllApplications) }
    surface.wantsLayer = true
    surface.layer?.masksToBounds = true
    surface.setAccessibilityElement(true)
    surface.setAccessibilityRole(.button)
    surface.clicked = { [weak self] in self?.openFromClick() }
    surface.hover = { [weak self] inside in self?.scheduleHover(inside) }
    material.material = .hudWindow
    material.blendingMode = .behindWindow
    material.state = .active
    material.autoresizingMask = [.width, .height]
    surface.addSubview(material)
    tint.wantsLayer = true
    tint.autoresizingMask = [.width, .height]
    surface.addSubview(tint)
    let host = NSHostingView(rootView: NotchView(model: presentation))
    host.autoresizingMask = [.width, .height]
    surface.addSubview(host)
    panel.contentView = surface
    surface.appearanceChanged = { [weak self] in self?.updateMaterial() }
    updateMaterial()
    observers.append(
      NotificationCenter.default.addObserver(
        forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
      ) { [weak self] _ in MainActor.assumeIsolated { self?.place() } })
    observers.append(
      NSWorkspace.shared.notificationCenter.addObserver(
        forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification, object: nil,
        queue: .main
      ) { [weak self] _ in MainActor.assumeIsolated { self?.updateMaterial() } })
  }
  private func openFromClick() {
    Diagnostics.shared.record("notch-click")
    click()
  }
  private func updateMaterial() {
    let opaque = NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency
    material.isHidden = opaque
    surface.effectiveAppearance.performAsCurrentDrawingAppearance {
      tint.layer?.backgroundColor = NSColor.windowBackgroundColor.withAlphaComponent(opaque ? 1 : 0.78).cgColor
      surface.layer?.backgroundColor = NSColor.windowBackgroundColor.withAlphaComponent(opaque ? 1 : 0.65).cgColor
      surface.layer?.borderColor = NSColor.separatorColor.cgColor
    }
    surface.layer?.borderWidth =
      NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast ? 1 : 0.5
  }
  func update(_ model: Presentation) {
    applyAppearance(model.preferences.appearance)
    let front = NSWorkspace.shared.frontmostApplication?.processIdentifier
    let old = presentation.state
    presentation.state.reconcile(activity: model.activity, enabled: model.preferences.notchEnabled)
    presentation.rows = presentation.state.providers.map {
      ProviderGlance(snapshot: model.usage[$0] ?? UsageSnapshot(provider: $0))
    }
    surface.setAccessibilityLabel(
      presentation.rows.map(\.accessibility).joined(separator: ", ") + ". Open AgentMeter")
    if old != presentation.state {
      transition(immediate: !model.preferences.notchEnabled)
    } else if presentation.state.phase != .hidden {
      place()
    }
    focusPreserved =
      focusPreserved && front == NSWorkspace.shared.frontmostApplication?.processIdentifier
      && !panel.isKeyWindow && !panel.isMainWindow
  }
  private func scheduleHover(_ inside: Bool) {
    hoverWork?.cancel()
    let work = DispatchWorkItem { [weak self] in
      guard let self, self.presentation.state.phase != .hidden else { return }
      if !inside && self.panel.frame.contains(NSEvent.mouseLocation) { return }
      self.setHover(inside)
    }
    hoverWork = work
    DispatchQueue.main.asyncAfter(deadline: .now() + (inside ? 0.12 : 0.10), execute: work)
  }
  func setHover(_ inside: Bool) {
    let old = presentation.state
    presentation.state.hover(inside)
    if old != presentation.state { transition() }
  }
  private func targetFrame() -> NSRect? {
    let screens = NSScreen.screens
    let builtIn = screens.first {
      CGDisplayIsBuiltin(
        ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? UInt32) ?? 0) != 0
    }
    guard let screen = builtIn ?? NSScreen.main ?? screens.first else { return nil }
    let expanded = presentation.state.phase == .expanded
    let measured = presentation.rows.reduce(CGFloat(0)) { total, row in
      total
        + (row.percentage as NSString).size(withAttributes: [
          .font: NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
        ]).width + 26 + (row.snapshot.state == .stale ? 11 : 0)
    }
    let width = min(
      screen.visibleFrame.width,
      expanded
        ? (presentation.rows.count > 1 ? 390 : 228)
        : max(112, measured + 40 + CGFloat(max(0, presentation.rows.count - 1)) * 25))
    let height: CGFloat = expanded ? (presentation.state.providers.contains(.claude) ? 174 : 144) : 34
    // Preserve Phase 1's screen/visible-area anchor. Presentation alone changes size.
    let anchor = MonitorGeometry.frame(
      screen: screen.frame, visible: screen.visibleFrame, safeTop: screen.safeAreaInsets.top,
      contentWidth: width)
    let top = anchor.maxY + 2
    return NSRect(
      x: min(
        max(screen.frame.midX - width / 2, screen.visibleFrame.minX),
        screen.visibleFrame.maxX - width), y: max(screen.visibleFrame.minY, top - height),
      width: width, height: height)
  }
  private func transition(immediate: Bool = false) {
    Diagnostics.shared.record("notch", code: presentation.state.phase.rawValue)
    generation += 1
    let current = generation
    let hidden = presentation.state.phase == .hidden
    hoverWork?.cancel()
    if hidden && immediate {
      panel.orderOut(nil)
      return
    }
    guard let target = targetFrame() else {
      panel.orderOut(nil)
      return
    }
    let reduce = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    if !hidden && !panel.isVisible {
      panel.alphaValue = 0
      panel.setFrame(
        reduce
          ? target
          : NSRect(
            x: target.midX - target.width * 0.45, y: target.maxY - 4, width: target.width * 0.9,
            height: 4), display: false)
      panel.orderFrontRegardless()
    }
    surface.layer?.cornerRadius = presentation.state.phase == .expanded ? 18 : 17
    NSAnimationContext.runAnimationGroup { context in
      context.duration = immediate ? 0 : (reduce ? 0.1 : (hidden ? 0.14 : 0.24))
      context.timingFunction = CAMediaTimingFunction(name: .easeOut)
      if hidden {
        panel.animator().alphaValue = 0
        if !reduce {
          panel.animator().setFrame(
            NSRect(
              x: panel.frame.midX - panel.frame.width * 0.45, y: panel.frame.maxY - 3,
              width: panel.frame.width * 0.9, height: 3), display: true)
        }
      } else {
        panel.animator().alphaValue = 1
        panel.animator().setFrame(target, display: true)
      }
    } completionHandler: { [weak self] in
      MainActor.assumeIsolated {
        guard let self, self.generation == current else { return }
        if hidden { self.panel.orderOut(nil) }
      }
    }
  }
  func place() {
    if presentation.state.phase != .hidden, let frame = targetFrame() {
      panel.setFrame(frame, display: true)
    }
  }
  func close() {
    hoverWork?.cancel()
    generation += 1
    for observer in observers {
      NotificationCenter.default.removeObserver(observer)
      NSWorkspace.shared.notificationCenter.removeObserver(observer)
    }
    panel.orderOut(nil)
    panel.close()
  }
  #if DEBUG
    func clickForValidation() { _ = surface.accessibilityPerformPress() }
    func capture(to url: URL) {
      guard let bitmap = surface.bitmapImageRepForCachingDisplay(in: surface.bounds) else { return }
      surface.cacheDisplay(in: surface.bounds, to: bitmap)
      try? bitmap.representation(using: .png, properties: [:])?.write(to: url)
    }
  #endif
}
