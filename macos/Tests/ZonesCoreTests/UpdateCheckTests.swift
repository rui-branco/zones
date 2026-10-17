import Foundation
import XCTest
import ZonesCore

final class UpdateCheckTests: XCTestCase {
    // MARK: - Versions

    func testVersionFromTag() {
        XCTAssertEqual(UpdateCheck.version(fromTag: "v1.2.3"), [1, 2, 3])
        XCTAssertEqual(UpdateCheck.version(fromTag: "V1.2.3"), [1, 2, 3])
        XCTAssertEqual(UpdateCheck.version(fromTag: "1.2.3"), [1, 2, 3])
        XCTAssertEqual(UpdateCheck.version(fromTag: "v0.0.0"), [0, 0, 0])
        XCTAssertEqual(UpdateCheck.version(fromTag: "v10.20.300"), [10, 20, 300])
    }

    func testVersionFromTagNeedsThreeNumbers() {
        for tag in ["", "v", "v1", "v1.2", "v1.2.3.4", "v1..3", "v1.2.", ".1.2", "vv1.2.3", "v1.2.x",
                    "v1.2.3-beta", "v+1.2.3", "v-1.2.3", "v 1.2.3", "v1.2.3 ", "release-1.2.3",
                    "v99999999999999999999.0.0"] {
            XCTAssertNil(UpdateCheck.version(fromTag: tag), tag)
        }
    }

    func testIsNewer() {
        XCTAssertTrue(UpdateCheck.isNewer("1.0.1", than: "1.0.0"))
        XCTAssertTrue(UpdateCheck.isNewer("1.1.0", than: "1.0.9"))
        XCTAssertTrue(UpdateCheck.isNewer("2.0.0", than: "1.99.99"))
        // Numerically, not as text.
        XCTAssertTrue(UpdateCheck.isNewer("1.0.10", than: "1.0.9"))
        XCTAssertFalse(UpdateCheck.isNewer("1.0.9", than: "1.0.10"))
        // A tag against a version.
        XCTAssertTrue(UpdateCheck.isNewer("v1.0.1", than: "1.0.0"))

        XCTAssertFalse(UpdateCheck.isNewer("1.0.0", than: "1.0.0"))
        XCTAssertFalse(UpdateCheck.isNewer("v1.0.0", than: "1.0.0"))
        XCTAssertFalse(UpdateCheck.isNewer("0.9.9", than: "1.0.0"))
    }

    func testIsNewerNeverForWhatIsNotAVersion() {
        XCTAssertFalse(UpdateCheck.isNewer("2.0", than: "1.0.0"))
        XCTAssertFalse(UpdateCheck.isNewer("2.0.0-beta", than: "1.0.0"))
        XCTAssertFalse(UpdateCheck.isNewer("2.0.0", than: "dev"))
        XCTAssertFalse(UpdateCheck.isNewer("garbage", than: "1.0.0"))
    }

    // MARK: - SHA256SUMS.txt

    private let zipHash = String(repeating: "ab", count: 32)
    private let exeHash = String(repeating: "0123456789abcdef", count: 4)

    func testChecksum() {
        let sums = "\(exeHash)  zones.exe\n\(zipHash)  Zones-mac.zip\n"
        XCTAssertEqual(UpdateCheck.checksum(for: "Zones-mac.zip", in: sums), zipHash)
        XCTAssertEqual(UpdateCheck.checksum(for: "zones.exe", in: sums), exeHash)
    }

    func testChecksumMissingFile() {
        let sums = "\(exeHash)  zones.exe\n"
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: sums))
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: ""))
        // The name whole, not a part of it.
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(zipHash)  Zones-mac.zip.old\n"))
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(zipHash)  old-Zones-mac.zip\n"))
        XCTAssertNil(UpdateCheck.checksum(for: "zones-mac.zip", in: "\(zipHash)  Zones-mac.zip\n"))
    }

    func testChecksumWindowsLineEndsAndUppercase() {
        // As a release script on Windows may write it.
        let sums = "\(exeHash.uppercased())  zones.exe\r\n\(zipHash.uppercased())  Zones-mac.zip\r\n"
        XCTAssertEqual(UpdateCheck.checksum(for: "Zones-mac.zip", in: sums), zipHash)
        XCTAssertEqual(UpdateCheck.checksum(for: "zones.exe", in: sums), exeHash)
    }

    func testChecksumBinaryMarkAndNoFinalNewline() {
        XCTAssertEqual(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(zipHash) *Zones-mac.zip"), zipHash)
    }

    func testChecksumAfterAByteOrderMark() {
        let sums = "\u{FEFF}\(zipHash)  Zones-mac.zip\r\n\(exeHash)  zones.exe\r\n"
        XCTAssertEqual(UpdateCheck.checksum(for: "Zones-mac.zip", in: sums), zipHash)
    }

    func testChecksumSkipsBadHashes() {
        let short = String(repeating: "a", count: 63)
        let notHex = String(repeating: "g", count: 64)
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(short)  Zones-mac.zip\n"))
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(notHex)  Zones-mac.zip\n"))
        XCTAssertNil(UpdateCheck.checksum(for: "Zones-mac.zip", in: "Zones-mac.zip\n"))
        // A bad line does not hide a good one after it.
        XCTAssertEqual(UpdateCheck.checksum(for: "Zones-mac.zip", in: "\(short)  Zones-mac.zip\n\(zipHash)  Zones-mac.zip\n"),
                       zipHash)
    }

    // MARK: - GitHub's answer

    private func json(tag: Any = "v1.2.3", draft: Any = false, prerelease: Any = false,
                      page: Any = "https://github.com/rui-branco/zones/releases/tag/v1.2.3",
                      assets: [String] = ["zones.exe", "Zones-mac.zip", "SHA256SUMS.txt"]) -> Data {
        let object: [String: Any] = [
            "tag_name": tag,
            "name": "Zones 1.2.3",
            "draft": draft,
            "prerelease": prerelease,
            "html_url": page,
            "assets": assets.map { name in
                [
                    "name": name,
                    "size": 1234,
                    "browser_download_url": "https://github.com/rui-branco/zones/releases/download/v1.2.3/\(name)",
                ] as [String: Any]
            },
        ]
        return try! JSONSerialization.data(withJSONObject: object)
    }

    func testRelease() throws {
        let release = try XCTUnwrap(UpdateCheck.release(from: json()))
        XCTAssertEqual(release.tag, "v1.2.3")
        XCTAssertEqual(release.version, "1.2.3")
        XCTAssertEqual(release.zipURL.absoluteString,
                       "https://github.com/rui-branco/zones/releases/download/v1.2.3/Zones-mac.zip")
        XCTAssertEqual(release.sumsURL.absoluteString,
                       "https://github.com/rui-branco/zones/releases/download/v1.2.3/SHA256SUMS.txt")
        XCTAssertEqual(release.pageURL.absoluteString, "https://github.com/rui-branco/zones/releases/tag/v1.2.3")
    }

    func testReleaseWithoutV() throws {
        let release = try XCTUnwrap(UpdateCheck.release(from: json(tag: "1.2.3")))
        XCTAssertEqual(release.tag, "1.2.3")
        XCTAssertEqual(release.version, "1.2.3")
    }

    func testReleaseNotDraftOrPrerelease() {
        XCTAssertNil(UpdateCheck.release(from: json(draft: true)))
        XCTAssertNil(UpdateCheck.release(from: json(prerelease: true)))
        XCTAssertNil(UpdateCheck.release(from: json(draft: true, prerelease: true)))
    }

    func testReleaseNeedsBothMacAssets() {
        XCTAssertNil(UpdateCheck.release(from: json(assets: ["zones.exe", "SHA256SUMS.txt"])))
        XCTAssertNil(UpdateCheck.release(from: json(assets: ["zones.exe", "Zones-mac.zip"])))
        XCTAssertNil(UpdateCheck.release(from: json(assets: [])))
        XCTAssertNotNil(UpdateCheck.release(from: json(assets: ["Zones-mac.zip", "SHA256SUMS.txt"])))
    }

    func testReleaseNeedsAVersionTag() {
        XCTAssertNil(UpdateCheck.release(from: json(tag: "latest")))
        XCTAssertNil(UpdateCheck.release(from: json(tag: "v1.2")))
        XCTAssertNil(UpdateCheck.release(from: json(tag: 123)))
    }

    func testReleaseNeedsHTTPS() {
        XCTAssertNil(UpdateCheck.release(from: json(page: "http://github.com/rui-branco/zones")))
        let plain = """
            {"tag_name": "v1.2.3", "draft": false, "prerelease": false,
             "html_url": "https://github.com/rui-branco/zones/releases/tag/v1.2.3",
             "assets": [
               {"name": "Zones-mac.zip", "browser_download_url": "http://example.com/Zones-mac.zip"},
               {"name": "SHA256SUMS.txt", "browser_download_url": "https://example.com/SHA256SUMS.txt"}]}
            """
        XCTAssertNil(UpdateCheck.release(from: Data(plain.utf8)))
    }

    func testReleaseNotFoundOrGarbage() {
        // What GitHub answers while nothing has been released.
        let notFound = #"{"message":"Not Found","documentation_url":"https://docs.github.com/rest/releases/releases#get-the-latest-release","status":"404"}"#
        XCTAssertNil(UpdateCheck.release(from: Data(notFound.utf8)))
        XCTAssertNil(UpdateCheck.release(from: Data("not json".utf8)))
        XCTAssertNil(UpdateCheck.release(from: Data("[]".utf8)))
        XCTAssertNil(UpdateCheck.release(from: Data()))
    }
}
