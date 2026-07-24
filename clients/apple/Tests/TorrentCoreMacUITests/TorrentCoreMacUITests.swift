import XCTest

@MainActor
final class TorrentCoreMacUITests: XCTestCase {
    func testCoreNavigationAndTorrentInspectorAreAccessible() {
        let app = launchApp()
        XCTAssertTrue(
            app.descendants(matching: .any)["navigation.dashboard"]
                .waitForExistence(timeout: 10)
        )

        openFixtureTorrents(in: app)

        XCTAssertTrue(
            app.descendants(matching: .any)["torrents.table"]
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["toolbar.connectionStatus"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["toolbar.refresh"].exists
        )

        app.staticTexts["Preview Torrent"].click()

        XCTAssertTrue(
            app.descendants(matching: .any)["torrents.inspector.content"]
                .waitForExistence(timeout: 5)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["inspector.remove"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["inspector.deleteData"].exists
        )
    }

    func testRemoveRequiresConfirmationAndCanBeCancelled() {
        let app = launchApp()
        openFixtureTorrents(in: app)
        XCTAssertTrue(
            app.staticTexts["Preview Torrent"].waitForExistence(timeout: 10)
        )
        app.staticTexts["Preview Torrent"].click()

        let removeButton = app.descendants(matching: .any)["inspector.remove"]
        XCTAssertTrue(removeButton.waitForExistence(timeout: 5))
        removeButton.click()

        let confirmationDialog = app.sheets.firstMatch
        XCTAssertTrue(confirmationDialog.waitForExistence(timeout: 5))
        let confirmButton = confirmationDialog.buttons["Remove Torrent"]
        XCTAssertTrue(confirmButton.waitForExistence(timeout: 5))
        let cancelButton = confirmationDialog.buttons["Cancel"]
        XCTAssertTrue(cancelButton.exists)
        cancelButton.click()

        XCTAssertTrue(app.staticTexts["Preview Torrent"].exists)
    }

    func testOperationalDestinationsLoadFixtureContent() {
        let app = launchApp()

        let historyNavigation = app.descendants(matching: .any)["navigation.history"]
        XCTAssertTrue(historyNavigation.waitForExistence(timeout: 10))
        historyNavigation.click()
        app.descendants(matching: .any)["toolbar.refresh"].click()
        XCTAssertTrue(
            app.descendants(matching: .any)["history.row"]
                .firstMatch
                .waitForExistence(timeout: 10)
        )

        let logsNavigation = app.descendants(matching: .any)["navigation.logs"]
        logsNavigation.click()
        app.descendants(matching: .any)["toolbar.refresh"].click()
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.row"]
                .firstMatch
                .waitForExistence(timeout: 10)
        )

        let settingsNavigation = app.descendants(matching: .any)[
            "navigation.serviceSettings"
        ]
        settingsNavigation.click()
        app.descendants(matching: .any)["toolbar.refresh"].click()
        XCTAssertTrue(app.staticTexts["Downloads"].waitForExistence(timeout: 10))
    }

    private func launchApp() -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchArguments = ["--torrentcore-ui-fixtures"]
        app.launch()
        return app
    }

    private func openFixtureTorrents(in app: XCUIApplication) {
        let torrentsNavigation = app.descendants(matching: .any)["navigation.torrents"]
        XCTAssertTrue(torrentsNavigation.waitForExistence(timeout: 10))
        torrentsNavigation.click()

        let clearFiltersButton = app.buttons["Clear"]
        if clearFiltersButton.waitForExistence(timeout: 5), clearFiltersButton.isEnabled {
            clearFiltersButton.click()
        }

        let refreshButton = app.descendants(matching: .any)["toolbar.refresh"]
        XCTAssertTrue(refreshButton.waitForExistence(timeout: 5))
        refreshButton.click()
    }
}
