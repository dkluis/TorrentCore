import AppKit
import Foundation
import SwiftUI

enum TorrentCoreMacTableSupport {
    static let pageSizes = [25, 50, 100, 250]
    static let defaultPageSize = 25
    static let minimumOverlayWidth = 340.0
    static let maximumOverlayWidth = 720.0

    static func page<Element>(_ values: [Element], index: Int, size: Int) -> [Element] {
        let safeSize = max(1, size)
        let safeIndex = clampedPageIndex(index, count: values.count, size: safeSize)
        let start = safeIndex * safeSize
        guard start < values.count else { return [] }
        return Array(values[start..<min(values.count, start + safeSize)])
    }

    static func maximumPageIndex(count: Int, size: Int) -> Int {
        max(0, (count - 1) / max(1, size))
    }

    static func clampedPageIndex(_ index: Int, count: Int, size: Int) -> Int {
        min(max(0, index), maximumPageIndex(count: count, size: size))
    }

    static func resultRangeLabel(count: Int, pageIndex: Int, pageSize: Int) -> String {
        guard count > 0 else { return "0 results" }
        let safeSize = max(1, pageSize)
        let safeIndex = clampedPageIndex(pageIndex, count: count, size: safeSize)
        let start = safeIndex * safeSize + 1
        let end = min(count, start + safeSize - 1)
        return "\(start.formatted())–\(end.formatted()) of \(count.formatted())"
    }

    static func contentWidth(
        header: String,
        values: some Sequence<String>,
        minimum: CGFloat,
        maximum: CGFloat
    ) -> CGFloat {
        let font = NSFont.systemFont(ofSize: NSFont.systemFontSize)
        let attributes: [NSAttributedString.Key: Any] = [.font: font]
        let widest = values.reduce(CGFloat.zero) { width, value in
            max(width, (value as NSString).size(withAttributes: attributes).width)
        }
        let headerWidth = (header as NSString).size(withAttributes: attributes).width
        return min(maximum, max(minimum, ceil(max(widest, headerWidth) + 28)))
    }

    static func clampedOverlayWidth(_ width: Double) -> Double {
        min(maximumOverlayWidth, max(minimumOverlayWidth, width))
    }
}

struct TorrentCoreMacSortDescriptor<Field: Codable & Hashable>: Codable, Equatable, Identifiable {
    var field: Field
    var descending: Bool

    var id: Field { field }
}

enum TorrentCoreMacSortStorage {
    static func encode<Field>(_ descriptors: [TorrentCoreMacSortDescriptor<Field>]) -> String
    where Field: Codable & Hashable {
        guard let data = try? JSONEncoder().encode(descriptors) else { return "" }
        return data.base64EncodedString()
    }

    static func decode<Field>(
        _ storedValue: String,
        as fieldType: Field.Type
    ) -> [TorrentCoreMacSortDescriptor<Field>]?
    where Field: Codable & Hashable {
        guard let data = Data(base64Encoded: storedValue) else { return nil }
        return try? JSONDecoder().decode(
            [TorrentCoreMacSortDescriptor<Field>].self,
            from: data
        )
    }
}

struct TorrentCoreMacSortEditor<Field>: View
where Field: CaseIterable & Codable & Hashable & Identifiable,
      Field.AllCases: RandomAccessCollection {
    @Binding var descriptors: [TorrentCoreMacSortDescriptor<Field>]
    let defaultDescriptors: [TorrentCoreMacSortDescriptor<Field>]
    let fieldTitle: (Field) -> String
    let done: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Sort Order")
                .font(.headline)
            Text("The first field has the highest priority.")
                .font(.caption)
                .foregroundStyle(.secondary)

            if descriptors.isEmpty {
                Text("No sort fields")
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                ForEach(Array(descriptors.indices), id: \.self) { index in
                    HStack(spacing: 8) {
                        Text("\(index + 1)")
                            .monospacedDigit()
                            .frame(width: 18, alignment: .trailing)

                        Picker("Field", selection: fieldBinding(at: index)) {
                            ForEach(Field.allCases) { field in
                                Text(fieldTitle(field)).tag(field)
                                    .disabled(isFieldUsed(field, excluding: index))
                            }
                        }
                        .labelsHidden()
                        .frame(minWidth: 155)

                        Picker("Direction", selection: directionBinding(at: index)) {
                            Text("Ascending").tag(false)
                            Text("Descending").tag(true)
                        }
                        .labelsHidden()
                        .pickerStyle(.segmented)
                        .frame(width: 170)

                        Button("Move Earlier", systemImage: "arrow.up") {
                            move(at: index, by: -1)
                        }
                        .labelStyle(.iconOnly)
                        .disabled(index == 0)

                        Button("Move Later", systemImage: "arrow.down") {
                            move(at: index, by: 1)
                        }
                        .labelStyle(.iconOnly)
                        .disabled(index == descriptors.count - 1)

                        Button("Remove Sort Field", systemImage: "minus.circle") {
                            descriptors.remove(at: index)
                        }
                        .labelStyle(.iconOnly)
                    }
                }
            }

            Divider()
            HStack {
                Menu("Add Sort Field", systemImage: "plus") {
                    ForEach(Field.allCases) { field in
                        Button(fieldTitle(field)) {
                            descriptors.append(.init(field: field, descending: false))
                        }
                        .disabled(isFieldUsed(field, excluding: nil))
                    }
                }

                Button("Restore Default Sort") {
                    descriptors = defaultDescriptors
                }

                Spacer()
                Button("Done", action: done)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(14)
        .frame(minWidth: 520)
    }

    private func fieldBinding(at index: Int) -> Binding<Field> {
        Binding(
            get: { descriptors[index].field },
            set: { field in
                guard descriptors.indices.contains(index),
                      !isFieldUsed(field, excluding: index)
                else { return }
                descriptors[index].field = field
            }
        )
    }

    private func directionBinding(at index: Int) -> Binding<Bool> {
        Binding(
            get: { descriptors[index].descending },
            set: { descending in
                guard descriptors.indices.contains(index) else { return }
                descriptors[index].descending = descending
            }
        )
    }

    private func isFieldUsed(_ field: Field, excluding excludedIndex: Int?) -> Bool {
        descriptors.enumerated().contains { index, descriptor in
            index != excludedIndex && descriptor.field == field
        }
    }

    private func move(at index: Int, by offset: Int) {
        let destination = index + offset
        guard descriptors.indices.contains(index),
              descriptors.indices.contains(destination)
        else { return }
        descriptors.swapAt(index, destination)
    }
}

struct TorrentCoreMacPaginationBar: View {
    let resultCount: Int
    @Binding var pageIndex: Int
    @Binding var pageSize: Int
    let accessibilityPrefix: String

    private var maximumPageIndex: Int {
        TorrentCoreMacTableSupport.maximumPageIndex(
            count: resultCount,
            size: pageSize
        )
    }

    var body: some View {
        HStack {
            Text(TorrentCoreMacTableSupport.resultRangeLabel(
                count: resultCount,
                pageIndex: pageIndex,
                pageSize: pageSize
            ))
            .foregroundStyle(.secondary)
            .accessibilityIdentifier("\(accessibilityPrefix).resultRange")

            Spacer()

            Picker("Rows", selection: $pageSize) {
                ForEach(TorrentCoreMacTableSupport.pageSizes, id: \.self) { size in
                    Text(size.formatted()).tag(size)
                }
            }
            .frame(width: 120)
            .accessibilityIdentifier("\(accessibilityPrefix).pageSize")

            Button {
                pageIndex = max(0, pageIndex - 1)
            } label: {
                Label("Previous", systemImage: "chevron.left")
            }
            .disabled(pageIndex == 0)
            .accessibilityIdentifier("\(accessibilityPrefix).previousPage")

            Button {
                pageIndex = min(maximumPageIndex, pageIndex + 1)
            } label: {
                Label("Next", systemImage: "chevron.right")
            }
            .disabled(pageIndex >= maximumPageIndex)
            .accessibilityIdentifier("\(accessibilityPrefix).nextPage")

            Text("Page \(min(pageIndex, maximumPageIndex) + 1) of \(maximumPageIndex + 1)")
                .foregroundStyle(.secondary)
                .monospacedDigit()
        }
        .padding(10)
    }
}

enum TorrentCoreMacExportScope: String {
    case selected
    case all

    func rows<Element>(selected: Element?, all: [Element]) -> [Element] {
        switch self {
        case .selected:
            selected.map { [$0] } ?? []
        case .all:
            all
        }
    }
}

enum TorrentCoreMacTableExport {
    static func write(
        headers: [String],
        rows: [[String]],
        fileName: String,
        fileManager: FileManager = .default
    ) throws -> URL {
        guard !headers.isEmpty else { throw TorrentCoreMacTableExportError.missingHeaders }
        guard rows.allSatisfy({ $0.count == headers.count }) else {
            throw TorrentCoreMacTableExportError.fieldCountMismatch
        }
        guard let downloadsDirectory = fileManager.urls(
            for: .downloadsDirectory,
            in: .userDomainMask
        ).first else {
            throw TorrentCoreMacTableExportError.downloadsDirectoryUnavailable
        }

        let safeFileName = sanitizedFileName(fileName)
        let fileURL = downloadsDirectory.appendingPathComponent(
            safeFileName,
            isDirectory: false
        )
        try delimitedContent(headers: headers, rows: rows).write(
            to: fileURL,
            atomically: true,
            encoding: .utf8
        )
        return fileURL
    }

    static func delimitedContent(headers: [String], rows: [[String]]) -> String {
        var lines = [headers.joined(separator: "##")]
        lines.reserveCapacity(rows.count + 1)
        lines.append(contentsOf: rows.map {
            $0.map(escapedField).joined(separator: "##")
        })
        return lines.joined(separator: "\n") + "\n"
    }

    static func timestamp(_ date: Date = Date()) -> String {
        formatter(dateFormat: "yyyyMMdd-HHmmss").string(from: date)
    }

    static func isoTimestamp(_ date: Date?) -> String {
        guard let date else { return "" }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        return formatter.string(from: date)
    }

    static func sanitizedFileName(_ value: String) -> String {
        var sanitized = String(value.map { character in
            character == "/" || character == ":" || character.isWhitespace
                ? "-"
                : character
        })
        while sanitized.contains("--") {
            sanitized = sanitized.replacingOccurrences(of: "--", with: "-")
        }
        sanitized = sanitized.trimmingCharacters(in: CharacterSet(charactersIn: "-"))
        return sanitized.isEmpty ? "table-export.csv" : sanitized
    }

    private static func escapedField(_ value: String) -> String {
        let sanitized = value
            .replacingOccurrences(of: "\r", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\"", with: "\"\"")
        return "\"\(sanitized)\""
    }

    private static func formatter(dateFormat: String) -> DateFormatter {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .current
        formatter.dateFormat = dateFormat
        return formatter
    }

}

enum TorrentCoreMacTableExportError: LocalizedError {
    case missingHeaders
    case fieldCountMismatch
    case downloadsDirectoryUnavailable

    var errorDescription: String? {
        switch self {
        case .missingHeaders: "The export has no column headers."
        case .fieldCountMismatch: "One or more export rows do not match the defined columns."
        case .downloadsDirectoryUnavailable: "The current user's Downloads directory is unavailable."
        }
    }
}

enum TorrentCoreMacNoticeKind: Equatable {
    case success
    case warning
    case error

    var title: String {
        switch self {
        case .success: "Success"
        case .warning: "Warning"
        case .error: "Error"
        }
    }

    var systemImage: String {
        switch self {
        case .success: "checkmark.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .error: "xmark.octagon.fill"
        }
    }

    var color: Color {
        switch self {
        case .success: .green
        case .warning: .orange
        case .error: .red
        }
    }

    var dismissesAutomatically: Bool { self != .error }
}

struct TorrentCoreMacNotice: Identifiable, Equatable {
    let id = UUID()
    let kind: TorrentCoreMacNoticeKind
    let message: String
}

private struct TorrentCoreMacToastModifier: ViewModifier {
    @Binding var notice: TorrentCoreMacNotice?

    func body(content: Content) -> some View {
        content
            .overlay(alignment: .topTrailing) {
                if let notice {
                    HStack(alignment: .top, spacing: 10) {
                        Image(systemName: notice.kind.systemImage)
                            .font(.title3)

                        VStack(alignment: .leading, spacing: 3) {
                            Text(notice.kind.title)
                                .font(.headline)
                            Text(notice.message)
                                .font(.callout)
                                .fixedSize(horizontal: false, vertical: true)
                        }

                        Spacer(minLength: 4)

                        Button("Dismiss", systemImage: "xmark") {
                            withAnimation(.easeInOut(duration: 0.18)) {
                                self.notice = nil
                            }
                        }
                        .labelStyle(.iconOnly)
                        .buttonStyle(.plain)
                    }
                    .foregroundStyle(.white)
                    .padding(14)
                    .frame(width: 390, alignment: .leading)
                    .background(
                        notice.kind.color.opacity(0.96),
                        in: RoundedRectangle(cornerRadius: 10)
                    )
                    .shadow(color: .black.opacity(0.28), radius: 12, y: 5)
                    .padding(.top, 16)
                    .padding(.trailing, 18)
                    .transition(.move(edge: .top).combined(with: .opacity))
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel("\(notice.kind.title): \(notice.message)")
                    .zIndex(100)
                    .task(id: notice.id) {
                        guard notice.kind.dismissesAutomatically else { return }
                        try? await Task.sleep(for: .seconds(5))
                        guard !Task.isCancelled, self.notice?.id == notice.id else { return }
                        withAnimation(.easeInOut(duration: 0.18)) {
                            self.notice = nil
                        }
                    }
                }
            }
    }
}

extension View {
    func torrentCoreToast(notice: Binding<TorrentCoreMacNotice?>) -> some View {
        modifier(TorrentCoreMacToastModifier(notice: notice))
    }
}
