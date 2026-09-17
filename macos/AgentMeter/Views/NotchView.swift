import SwiftUI

struct NotchView: View {
  let model: NotchPresentation
  var body: some View {
    Group {
      if model.state.phase == .expanded {
        TimelineView(.periodic(from: .now, by: 30)) { context in
          HStack(alignment: .top, spacing: 18) {
            ForEach(model.rows) { row in
              if row.id != model.rows.first?.id { Divider().overlay(.white.opacity(0.15)) }
              VStack(alignment: .leading, spacing: 12) {
                HStack(spacing: 8) {
                  NotchProviderMark(provider: row.id, size: 23)
                  Text(row.percentage).font(.system(size: 17, weight: .semibold)).monospacedDigit()
                    .foregroundStyle(row.id.notchAccent)
                  if model.rows.count == 1 {
                    Text("remaining").font(.caption).foregroundStyle(.white.opacity(0.8))
                  }
                }
                AllowanceBar(remaining: row.snapshot.primary?.remaining, color: row.id.notchAccent)
                VStack(alignment: .leading, spacing: 4) {
                  if let primary = row.snapshot.primary {
                    Text(
                      (row.id == .codex ? "Main · " : "")
                        + (UsageCopy.duration(primary.durationMinutes) ?? primary.label)
                    )
                    .font(.caption2).foregroundStyle(.white.opacity(0.8))
                  }
                  Text(
                    row.snapshot.primary?.resetText(at: context.date) ?? row.snapshot.state.rawValue
                  ).font(.caption)
                  if let reset = row.snapshot.primary?.reset, reset > context.date {
                    Text(reset, format: .dateTime.month(.abbreviated).day().hour().minute()).font(
                      .caption2
                    ).foregroundStyle(.white.opacity(0.8))
                  }
                  if row.snapshot.state == .stale {
                    Text("Stale · last verified allowance").font(.caption2).foregroundStyle(
                      .secondary)
                  }
                }
              }.frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine).accessibilityLabel(row.accessibility)
            }
          }.padding(18)
        }
      } else {
        HStack(spacing: 12) {
          ForEach(model.rows) { row in
            if row.id != model.rows.first?.id {
              Rectangle().fill(.white.opacity(0.22)).frame(width: 1, height: 16)
            }
            HStack(spacing: 7) {
              NotchProviderMark(provider: row.id, size: 19)
              Text(row.percentage).font(.system(size: 13, weight: .semibold)).monospacedDigit()
                .foregroundStyle(row.id.notchAccent)
              if row.snapshot.state == .stale {
                Circle().fill(.secondary).frame(width: 4, height: 4).accessibilityLabel("Stale")
              }
            }.accessibilityElement(children: .ignore).accessibilityLabel(row.accessibility)
          }
        }.padding(.horizontal, 16)
      }
    }.frame(maxWidth: .infinity, maxHeight: .infinity)
      .foregroundStyle(.white).environment(\.colorScheme, .dark)
  }
}
