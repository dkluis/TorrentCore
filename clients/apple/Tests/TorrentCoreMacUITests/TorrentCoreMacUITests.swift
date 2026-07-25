import AppKit
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
        XCTAssertTrue(
            app.staticTexts["Final Result: Success"].waitForExistence(timeout: 5)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["history.feedback.summary"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["history.feedback.received"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["history.feedback.finalResult"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["history.feedback.reason"].exists
        )

        let copyMagnetButton = app.descendants(matching: .any)["history.copyMagnet"]
        XCTAssertTrue(copyMagnetButton.waitForExistence(timeout: 5))
        copyMagnetButton.click()
        XCTAssertEqual(
            NSPasteboard.general.string(forType: .string),
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"
        )
        expectation(
            for: NSPredicate(format: "label == %@", "Copied"),
            evaluatedWith: copyMagnetButton
        )
        waitForExpectations(timeout: 1)

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

    func testKeyboardCommandsAndAddMagnetInitialFocus() {
        let app = launchApp()

        app.typeKey("2", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["torrents.table"]
                .waitForExistence(timeout: 10)
        )

        let addButton = app.descendants(matching: .any)["toolbar.addMagnet"]
        XCTAssertTrue(addButton.waitForExistence(timeout: 5))
        addButton.click()

        let magnetField = app.descendants(matching: .any)["addMagnet.uri"]
        XCTAssertTrue(magnetField.waitForExistence(timeout: 5))
        let magnet = "magnet:?xt=urn:btih:keyboardfocus"
        app.typeText(magnet)
        XCTAssertEqual(magnetField.value as? String, magnet)
        app.typeKey(.escape, modifierFlags: [])
        XCTAssertTrue(magnetField.waitForNonExistence(timeout: 5))

        app.typeKey("3", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["history.row"].firstMatch
                .waitForExistence(timeout: 10)
        )

        app.typeKey("4", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.row"].firstMatch
                .waitForExistence(timeout: 10)
        )

        app.typeKey("5", modifierFlags: .command)
        XCTAssertTrue(app.staticTexts["Downloads"].waitForExistence(timeout: 10))

        app.typeKey("6", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["connection.list"]
                .waitForExistence(timeout: 10)
        )

        app.typeKey("1", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["dashboard.content"]
                .waitForExistence(timeout: 10)
        )
    }

    func testAgreedLargeCollectionsRenderAndPaginate() {
        let app = launchApp(largeCollections: true)

        app.typeKey("2", modifierFlags: .command)
        XCTAssertTrue(
            app.staticTexts["1–25 of 100"].waitForExistence(timeout: 10)
        )
        XCTAssertTrue(app.staticTexts["Page 1 of 4"].exists)
        let nextTorrentPage = app.descendants(matching: .any)[
            "torrents.nextPage"
        ]
        XCTAssertTrue(nextTorrentPage.isEnabled)
        nextTorrentPage.click()
        XCTAssertTrue(
            app.staticTexts["26–50 of 100"].waitForExistence(timeout: 5)
        )

        app.typeKey("3", modifierFlags: .command)
        XCTAssertTrue(
            app.staticTexts["1–50 of 500"].waitForExistence(timeout: 10)
        )

        app.typeKey("4", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.row"].firstMatch
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.limitNotice"]
                .waitForExistence(timeout: 5)
        )
    }

    private func launchApp(
        largeCollections: Bool = false
    ) -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchArguments = [
            largeCollections
                ? "--torrentcore-ui-large-fixtures"
                : "--torrentcore-ui-fixtures",
        ]
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
