import Foundation

/// A published release that has the Mac app in it: what GitHub's "latest
/// release" answer says, once UpdateCheck.release(from:) has checked it.
public struct Release: Equatable, Sendable {
    /// "v1.2.3", as tagged.
    public let tag: String

    /// "1.2.3": the tag without its v, what the app in the zip has as its
    /// CFBundleShortVersionString.
    public let version: String

    /// Zones-mac.zip, Zones.app zipped with `ditto -c -k --keepParent`.
    public let zipURL: URL

    /// SHA256SUMS.txt, the SHA-256 of every asset of the release.
    public let sumsURL: URL

    /// The release's page on GitHub, for a copy that cannot install itself.
    public let pageURL: URL

    public init(tag: String, version: String, zipURL: URL, sumsURL: URL, pageURL: URL) {
        self.tag = tag
        self.version = version
        self.zipURL = zipURL
        self.sumsURL = sumsURL
        self.pageURL = pageURL
    }
}

/// Zones' updates, as the Windows app and the release script have them:
/// releases are published on the public rui-branco/zones repo, tagged
/// v<major>.<minor>.<patch>, with Zones-mac.zip and SHA256SUMS.txt among
/// their assets. Pure, so it can be tested; downloading and installing
/// are the app's (Updater).
public enum UpdateCheck {
    /// The newest release that is neither a draft nor a prerelease. Asked
    /// without authentication; 404 while nothing has been released.
    public static let latestURL = URL(string: "https://api.github.com/repos/rui-branco/zones/releases/latest")!

    /// The headers GitHub's API is asked with.
    public static let headers = [
        "User-Agent": "Zones-Updater",
        "Accept": "application/vnd.github+json",
    ]

    /// The Mac app's asset: Zones.app at the root of the zip.
    public static let zipName = "Zones-mac.zip"

    /// One line per asset: `<lowercase hex sha256>  <filename>`.
    public static let sumsName = "SHA256SUMS.txt"

    /// "v1.2.3" (or "V1.2.3", or "1.2.3") as [1, 2, 3]. Nil unless it is
    /// exactly three whole numbers, digits only.
    public static func version(fromTag tag: String) -> [Int]? {
        var text = Substring(tag)
        if text.first == "v" || text.first == "V" { text = text.dropFirst() }
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        var numbers: [Int] = []
        for part in parts {
            // Int() alone would take "+1" too.
            guard !part.isEmpty, part.allSatisfy({ ("0"..."9").contains($0) }), let n = Int(part) else { return nil }
            numbers.append(n)
        }
        return numbers
    }

    /// Numerically, part by part: 1.0.10 is newer than 1.0.9. A tag or a
    /// version alike; anything that does not read as one is never newer,
    /// nor is anything newer than it.
    public static func isNewer(_ a: String, than b: String) -> Bool {
        guard let x = version(fromTag: a), let y = version(fromTag: b) else { return false }
        return y.lexicographicallyPrecedes(x)
    }

    /// `file`'s SHA-256 in the text of a SHA256SUMS.txt, lowercase; nil when
    /// it has no line for it. Takes the line ends of either system, the byte
    /// order mark PowerShell may start a UTF-8 file with, and the `*`
    /// sha256sum marks a file read in binary mode with.
    public static func checksum(for file: String, in sums: String) -> String? {
        let text = sums.hasPrefix("\u{FEFF}") ? sums.dropFirst() : Substring(sums)
        for raw in text.split(whereSeparator: { $0 == "\n" || $0 == "\r\n" || $0 == "\r" }) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard let gap = line.firstIndex(where: { $0 == " " || $0 == "\t" }) else { continue }
            let hash = line[..<gap]
            var name = line[gap...].drop(while: { $0 == " " || $0 == "\t" })
            if name.first == "*" { name = name.dropFirst() }
            guard name == file, hash.count == 64, hash.allSatisfy({ $0.isASCII && $0.isHexDigit }) else { continue }
            return hash.lowercased()
        }
        return nil
    }

    /// GitHub's answer for a release, as a Release; nil for a draft, a
    /// prerelease, a tag that is not v<major>.<minor>.<patch>, or one
    /// without Zones-mac.zip and SHA256SUMS.txt (a Windows-only release).
    public static func release(from json: Data) -> Release? {
        guard let object = (try? JSONSerialization.jsonObject(with: json)) as? [String: Any] else { return nil }
        if JSON.bool(object["draft"]) == true || JSON.bool(object["prerelease"]) == true { return nil }

        guard let tag = object["tag_name"] as? String, version(fromTag: tag) != nil,
              let page = url(object["html_url"])
        else { return nil }

        var assets: [String: URL] = [:]
        for case let asset as [String: Any] in object["assets"] as? [Any] ?? [] {
            if let name = asset["name"] as? String, let link = url(asset["browser_download_url"]) {
                assets[name] = link
            }
        }
        guard let zip = assets[zipName], let sums = assets[sumsName] else { return nil }

        let first = tag.first
        let bare = first == "v" || first == "V" ? String(tag.dropFirst()) : tag
        return Release(tag: tag, version: bare, zipURL: zip, sumsURL: sums, pageURL: page)
    }

    /// An https address; nothing is fetched or opened over anything else.
    private static func url(_ value: Any?) -> URL? {
        guard let text = value as? String, let url = URL(string: text), url.scheme?.lowercased() == "https",
              url.host?.isEmpty == false
        else { return nil }
        return url
    }
}
