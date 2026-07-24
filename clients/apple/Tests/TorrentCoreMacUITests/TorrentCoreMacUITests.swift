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
            app.descendants(matching: .any)["toolbar.refresh"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["toolbar.addMagnet"].exists
        )
        XCTAssertFalse(
            app.descendants(matching: .any)["toolbar.connectionStatus"].isHittable
        )

        app.staticTexts["Preview Torrent"].click()

        let inspectorContent = app.descendants(matching: .any)[
            "torrents.inspector.content"
        ]
        XCTAssertTrue(inspectorContent.waitForExistence(timeout: 5))
        let inspectorToggle = app.descendants(matching: .any)["toolbar.inspector"]
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        let refreshButton = app.descendants(matching: .any)["toolbar.refresh"]
        XCTAssertLessThanOrEqual(
            refreshButton.frame.maxX,
            inspectorContent.frame.minX,
            "Refresh should remain in the main-content toolbar, outside the inspector."
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["inspector.remove"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["inspector.deleteData"].exists
        )

        inspectorToggle.click()
        XCTAssertTrue(inspectorContent.waitForNonExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(inspectorContent.waitForExistence(timeout: 5))
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
        XCTAssertTrue(
            app.descendants(matching: .any)["history.row"]
                .firstMatch
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(app.descendants(matching: .any)["toolbar.addMagnet"].exists)
        app.descendants(matching: .any)["history.row"].firstMatch.click()
        let historyInspector = app.descendants(matching: .any)[
            "history.inspector.content"
        ]
        XCTAssertTrue(historyInspector.waitForExistence(timeout: 5))
        let inspectorToggle = app.descendants(matching: .any)["toolbar.inspector"]
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(historyInspector.waitForNonExistence(timeout: 5))

        let logsNavigation = app.descendants(matching: .any)["navigation.logs"]
        logsNavigation.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.row"]
                .firstMatch
                .waitForExistence(timeout: 10)
        )
        app.descendants(matching: .any)["logs.row"].firstMatch.click()
        let logsInspector = app.descendants(matching: .any)[
            "logs.inspector.content"
        ]
        XCTAssertTrue(logsInspector.waitForExistence(timeout: 5))
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(logsInspector.waitForNonExistence(timeout: 5))

        let settingsNavigation = app.descendants(matching: .any)[
            "navigation.serviceSettings"
        ]
        settingsNavigation.click()
        XCTAssertTrue(app.staticTexts["Downloads"].waitForExistence(timeout: 10))
    }

    func testServiceSettingsExposeHelpAndConstrainedModeChoices() {
        let app = launchApp()
        let settingsNavigation = app.descendants(matching: .any)[
            "navigation.serviceSettings"
        ]
        XCTAssertTrue(settingsNavigation.waitForExistence(timeout: 10))
        settingsNavigation.click()

        let activeDownloads = app.descendants(matching: .any)[
            "serviceSettings.maxActiveDownloads"
        ]
        XCTAssertTrue(activeDownloads.waitForExistence(timeout: 5))
        XCTAssertEqual(activeDownloads.value as? String, "4")

        let activeMetadataResolutions = app.descendants(matching: .any)[
            "serviceSettings.maxActiveMetadataResolutions"
        ]
        XCTAssertTrue(activeMetadataResolutions.exists)
        XCTAssertEqual(activeMetadataResolutions.value as? String, "4")

        let seedingGroup = app.staticTexts["Seeding & Cleanup"].firstMatch
        XCTAssertTrue(seedingGroup.waitForExistence(timeout: 10))
        seedingGroup.click()

        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.seedingStopMode"]
                .waitForExistence(timeout: 5)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)[
                "serviceSettings.completedTorrentCleanupMode"
            ].exists
        )

        let helpButton = app.descendants(matching: .any)["help.seeding-stop-mode"]
        XCTAssertTrue(helpButton.waitForExistence(timeout: 5))
        helpButton.click()
        XCTAssertTrue(
            app.staticTexts["Controls when a completed torrent stops seeding."]
                .waitForExistence(timeout: 5)
        )

        app.staticTexts["Engine"].firstMatch.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.engineEncryptionMode"]
                .waitForExistence(timeout: 5)
        )

        app.staticTexts["Categories"].firstMatch.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.categories.grid"]
                .waitForExistence(timeout: 5)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.category.tv.displayName"]
                .waitForExistence(timeout: 5)
        )
    }

    private func launchApp() -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchArguments = ["--torrentcore-ui-fixtures"]
        app.launch()
        let dashboardNavigation = app.descendants(matching: .any)[
            "navigation.dashboard"
        ]
        if !dashboardNavigation.waitForExistence(timeout: 3) {
            app.typeKey("n", modifierFlags: .command)
            _ = dashboardNavigation.waitForExistence(timeout: 5)
        }
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
        XCTAssertTrue(
            app.descendants(matching: .any)["torrents.table"]
                .waitForExistence(timeout: 10)
        )
    }
}
