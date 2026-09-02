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

        let torrentTable = app.descendants(matching: .any)["torrents.table"]
        XCTAssertTrue(torrentTable.waitForExistence(timeout: 10))
        let torrentTableFrameWithoutInspector = torrentTable.frame
        XCTAssertTrue(
            app.descendants(matching: .any)["toolbar.refresh"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["toolbar.addMagnet"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["status.connection"]
                .waitForExistence(timeout: 5)
        )
        XCTAssertFalse(app.descendants(matching: .any)["toolbar.connectionStatus"].exists)

        app.staticTexts["Preview Torrent"].click()

        let inspectorContent = app.descendants(matching: .any)[
            "torrents.inspector.content"
        ]
        XCTAssertTrue(inspectorContent.waitForExistence(timeout: 5))
        assertUnchanged(
            torrentTable.frame,
            from: torrentTableFrameWithoutInspector,
            message: "The torrent inspector must overlay without resizing the table."
        )
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
        let copyTorrentID = app.descendants(matching: .any)["torrents.copyTorrentID"]
        XCTAssertTrue(copyTorrentID.exists)
        copyTorrentID.click()
        XCTAssertEqual(
            NSPasteboard.general.string(forType: .string),
            "11111111-2222-3333-4444-555555555555"
        )

        inspectorToggle.click()
        XCTAssertTrue(inspectorContent.waitForNonExistence(timeout: 5))
        assertUnchanged(
            torrentTable.frame,
            from: torrentTableFrameWithoutInspector,
            message: "Hiding the torrent inspector must not resize the table."
        )
        inspectorToggle.click()
        XCTAssertTrue(inspectorContent.waitForExistence(timeout: 5))
        assertUnchanged(
            torrentTable.frame,
            from: torrentTableFrameWithoutInspector,
            message: "Reopening the torrent inspector must not resize the table."
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

    func testHistoryAndLogsInspectorsOverlayWithoutResizingTables() {
        let app = launchApp()
        let inspectorToggle = app.descendants(matching: .any)["toolbar.inspector"]

        let historyNavigation = app.descendants(matching: .any)["navigation.history"]
        XCTAssertTrue(historyNavigation.waitForExistence(timeout: 10))
        historyNavigation.click()
        let historyTable = app.descendants(matching: .any)["history.table"]
        XCTAssertTrue(historyTable.waitForExistence(timeout: 10))
        let historyFrame = historyTable.frame
        let historyRow = app.descendants(matching: .any)["history.row"].firstMatch
        XCTAssertTrue(historyRow.waitForExistence(timeout: 5))
        historyRow.click()
        let historyInspector = app.descendants(matching: .any)[
            "history.inspector.content"
        ]
        XCTAssertTrue(historyInspector.waitForExistence(timeout: 5))
        assertUnchanged(
            historyTable.frame,
            from: historyFrame,
            message: "The history inspector must overlay without resizing the table."
        )
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(historyInspector.waitForNonExistence(timeout: 5))
        assertUnchanged(
            historyTable.frame,
            from: historyFrame,
            message: "Hiding the history inspector must not resize the table."
        )

        let logsNavigation = app.descendants(matching: .any)["navigation.logs"]
        logsNavigation.click()
        let logsTable = app.descendants(matching: .any)["logs.table"]
        XCTAssertTrue(logsTable.waitForExistence(timeout: 10))
        let logsFrame = logsTable.frame
        let logRow = app.descendants(matching: .any)["logs.row"].firstMatch
        XCTAssertTrue(logRow.waitForExistence(timeout: 5))
        logRow.click()
        let logsInspector = app.descendants(matching: .any)[
            "logs.inspector.content"
        ]
        XCTAssertTrue(logsInspector.waitForExistence(timeout: 5))
        assertUnchanged(
            logsTable.frame,
            from: logsFrame,
            message: "The logs inspector must overlay without resizing the table."
        )
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(logsInspector.waitForNonExistence(timeout: 5))
        assertUnchanged(
            logsTable.frame,
            from: logsFrame,
            message: "Hiding the logs inspector must not resize the table."
        )
    }

    func testOperationalDestinationsLoadFixtureContent() {
        let app = launchApp()

        let historyNavigation = app.descendants(matching: .any)["navigation.history"]
        XCTAssertTrue(historyNavigation.waitForExistence(timeout: 10))
        historyNavigation.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["history.table"]
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["history.row"].firstMatch
                .waitForExistence(timeout: 5)
        )
        XCTAssertTrue(app.descendants(matching: .any)["toolbar.addMagnet"].exists)
        app.descendants(matching: .any)["history.row"].firstMatch.click()
        let historyInspector = app.descendants(matching: .any)[
            "history.inspector.content"
        ]
        XCTAssertTrue(historyInspector.waitForExistence(timeout: 5))
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

        historyInspector.scroll(byDeltaX: 0, deltaY: -500)

        let copyMagnetButton = app.descendants(matching: .any)["history.copyMagnet"]
        XCTAssertTrue(copyMagnetButton.waitForExistence(timeout: 5))
        copyMagnetButton.click()
        XCTAssertEqual(
            NSPasteboard.general.string(forType: .string),
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"
        )

        let copyTorrentID = app.descendants(matching: .any)["history.copyTorrentID"]
        XCTAssertTrue(copyTorrentID.waitForExistence(timeout: 5))
        copyTorrentID.click()
        XCTAssertEqual(
            NSPasteboard.general.string(forType: .string),
            "11111111-2222-3333-4444-555555555555"
        )

        historyInspector.scroll(byDeltaX: 0, deltaY: -500)

        let copyServiceInstanceID = app.descendants(matching: .any)[
            "history.copyServiceInstanceID"
        ]
        XCTAssertTrue(copyServiceInstanceID.waitForExistence(timeout: 5))
        copyServiceInstanceID.click()
        XCTAssertEqual(
            NSPasteboard.general.string(forType: .string),
            "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"
        )

        let inspectorToggle = app.descendants(matching: .any)["toolbar.inspector"]
        XCTAssertTrue(inspectorToggle.waitForExistence(timeout: 5))
        inspectorToggle.click()
        XCTAssertTrue(historyInspector.waitForNonExistence(timeout: 5))

        let logsNavigation = app.descendants(matching: .any)["navigation.logs"]
        logsNavigation.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.table"]
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.row"].firstMatch
                .waitForExistence(timeout: 5)
        )
        app.descendants(matching: .any)["logs.row"].firstMatch.click()
        let logsInspector = app.descendants(matching: .any)[
            "logs.inspector.content"
        ]
        XCTAssertTrue(logsInspector.waitForExistence(timeout: 5))
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.copyTorrentID"].exists
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.copyServiceInstanceID"].exists
        )
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

        app.staticTexts["Cleanup"].firstMatch.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.cleanup.logs.date"]
                .waitForExistence(timeout: 5)
        )
        XCTAssertTrue(
            app.descendants(matching: .any)["serviceSettings.cleanup.history.date"].exists
        )
        let deleteLogs = app.descendants(matching: .any)[
            "serviceSettings.cleanup.logs.delete"
        ]
        let deleteHistory = app.descendants(matching: .any)[
            "serviceSettings.cleanup.history.delete"
        ]
        let deleteOrphanedLogs = app.descendants(matching: .any)[
            "serviceSettings.cleanup.orphanedLogs.delete"
        ]
        XCTAssertTrue(deleteLogs.exists)
        XCTAssertTrue(deleteHistory.exists)
        XCTAssertTrue(deleteOrphanedLogs.exists)

        deleteLogs.click()
        XCTAssertTrue(app.staticTexts["Delete Log Entries?"].waitForExistence(timeout: 5))
        app.sheets.buttons["Cancel"].firstMatch.click()

        deleteHistory.click()
        XCTAssertTrue(app.staticTexts["Delete History Records?"].waitForExistence(timeout: 5))
        app.sheets.buttons["Cancel"].firstMatch.click()

        deleteOrphanedLogs.click()
        XCTAssertTrue(app.staticTexts["Delete Orphan Logs?"].waitForExistence(timeout: 5))
        app.sheets.buttons["Cancel"].firstMatch.click()
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

        let categoryPicker = app.descendants(matching: .any)["addMagnet.category"]
        XCTAssertTrue(categoryPicker.waitForExistence(timeout: 5))
        categoryPicker.click()
        XCTAssertTrue(app.menuItems["TV (Show)"].waitForExistence(timeout: 5))
        app.typeKey(.escape, modifierFlags: [])
        XCTAssertFalse(
            app.descendants(matching: .any)["addMagnet.categories.loading"].exists
        )
        app.typeKey(.escape, modifierFlags: [])
        XCTAssertTrue(magnetField.waitForNonExistence(timeout: 5))

        app.typeKey("3", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["history.table"]
                .waitForExistence(timeout: 10)
        )

        app.typeKey("4", modifierFlags: .command)
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.table"]
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

    func testSavedConnectionStartsAtMinimumWindowSizeWithVisibleMaintenanceActions() {
        let app = launchApp(additionalArguments: [
            "--torrentcore-ui-connection-start",
            "--torrentcore-ui-compact-window",
        ])

        let connectionList = app.descendants(matching: .any)["connection.list"]
        XCTAssertTrue(connectionList.waitForExistence(timeout: 10))

        let newConnection = app.descendants(matching: .any)["connection.new"]
        let deleteConnection = app.descendants(matching: .any)["connection.delete"]
        let statusBar = app.descendants(matching: .any)["status.connection"].firstMatch
        XCTAssertTrue(newConnection.waitForExistence(timeout: 5))
        XCTAssertTrue(deleteConnection.exists)
        XCTAssertTrue(statusBar.waitForExistence(timeout: 5))
        XCTAssertLessThanOrEqual(
            newConnection.frame.maxY,
            statusBar.frame.minY,
            "Connection maintenance controls must remain above the global status bar."
        )
        XCTAssertLessThanOrEqual(
            deleteConnection.frame.maxY,
            statusBar.frame.minY,
            "Connection maintenance controls must remain above the global status bar."
        )

        let sidebarToggle = app.descendants(matching: .any)["toolbar.sidebar"]
        XCTAssertTrue(sidebarToggle.waitForExistence(timeout: 5))
        sidebarToggle.click()
        XCTAssertTrue(
            app.descendants(matching: .any)["navigation.sidebar"]
                .waitForNonExistence(timeout: 5)
        )
        XCTAssertTrue(connectionList.exists)
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
            app.descendants(matching: .any)["logs.table"]
                .waitForExistence(timeout: 10)
        )
        XCTAssertTrue(app.staticTexts["1–50 of 1,000"].waitForExistence(timeout: 5))
        let nextLogPage = app.descendants(matching: .any)["logs.nextPage"]
        XCTAssertTrue(nextLogPage.isEnabled)
        nextLogPage.click()
        XCTAssertTrue(app.staticTexts["51–100 of 1,000"].waitForExistence(timeout: 5))
        XCTAssertTrue(
            app.descendants(matching: .any)["logs.limitNotice"]
                .waitForExistence(timeout: 5)
        )
    }

    private func launchApp(
        largeCollections: Bool = false,
        additionalArguments: [String] = []
    ) -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchArguments = [
            largeCollections
                ? "--torrentcore-ui-large-fixtures"
                : "--torrentcore-ui-fixtures",
        ] + additionalArguments
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

        let resetFiltersButton = app.buttons["torrents.resetFilters"]
        if resetFiltersButton.waitForExistence(timeout: 5), resetFiltersButton.isEnabled {
            resetFiltersButton.click()
        }
        XCTAssertTrue(
            app.descendants(matching: .any)["torrents.table"]
                .waitForExistence(timeout: 10)
        )
    }

    private func assertUnchanged(
        _ frame: CGRect,
        from expected: CGRect,
        message: String,
        accuracy: CGFloat = 1
    ) {
        XCTAssertEqual(frame.minX, expected.minX, accuracy: accuracy, message)
        XCTAssertEqual(frame.minY, expected.minY, accuracy: accuracy, message)
        XCTAssertEqual(frame.width, expected.width, accuracy: accuracy, message)
        XCTAssertEqual(frame.height, expected.height, accuracy: accuracy, message)
    }
}
