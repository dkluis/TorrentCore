import Foundation
import HTTPTypes
import OpenAPIRuntime
import Testing
@testable import TorrentCoreAPI
@testable import TorrentCoreFeatures
@testable import TorrentCoreSupport

@Test
func sharedTargetsExposeTheSameProductIdentity() {
    #expect(TorrentCoreProduct.displayName == "TorrentCore")
    #expect(TorrentCoreAPIFoundation.productName == TorrentCoreProduct.displayName)
    #expect(TorrentCoreFeatureFoundation.productName == TorrentCoreProduct.displayName)
}

@Test
func torrentListPresentationShowsWaitReasonAndQueuePosition() {
    var summary = TorrentCorePreviewFixtures.downloadingTorrent
    summary.waitReason = TorrentCoreWaitReason(rawValue: "WaitingForMetadataSlot")
    summary.queuePosition = 27

    let item = TorrentCoreTorrentListItem(summary: summary)

    #expect(item.wait == "Waiting For Metadata Slot · #27")

    summary.waitReason = .automaticallyYieldedDownload
    summary.queuePosition = 8
    #expect(
        TorrentCoreTorrentListItem(summary: summary).wait
            == "Automatically Yielded · #8"
    )
}

@Test
func torrentListPresentationShowsPriorityOrdinaryAndHeldPositions() {
    var priority = TorrentCorePreviewFixtures.downloadingTorrent
    priority.waitReason = .waitingForDownloadSlot
    priority.priorityQueuePosition = 2
    priority.queuePosition = 19
    #expect(
        TorrentCoreTorrentListItem(summary: priority).wait
            == "Waiting For Download Slot · Priority #2 · Queue #19"
    )

    var held = priority
    held.waitReason = .heldByOperator
    held.priorityQueuePosition = nil
    held.queuePosition = nil
    held.heldQueuePosition = 3
    #expect(TorrentCoreTorrentListItem(summary: held).wait == "Held By Operator · Held #3")
}

@Test
func sharedHelpCatalogCoversEveryServiceSetting() {
    #expect(TorrentCoreHelpCatalog.Settings.all.count == 53)
    #expect(
        Set(TorrentCoreHelpCatalog.Settings.all.map(\.label)).count
            == TorrentCoreHelpCatalog.Settings.all.count
    )
    #expect(TorrentCoreHelpCatalog.Settings.seedingStopMode.detail.contains("live"))
    #expect(TorrentCoreHelpCatalog.Settings.engineEncryptionMode.detail.contains("restart"))
    #expect(TorrentCoreHelpCatalog.Settings.metadataResolutionTimeSliceMinutes.detail.contains("lone resolver"))
    #expect(TorrentCoreHelpCatalog.Settings.downloadNoProgressTimeSliceMinutes.detail.contains("completed-piece"))
    #expect(TorrentCoreHelpCatalog.Settings.automaticMetadataResetStuckThresholdSeconds.detail.contains("quarantines"))
    #expect(TorrentCoreHelpCatalog.Settings.categoryDownloadRootPath.detail.contains("Existing"))
}

@Test
func runtimeSettingsDraftAndRequestPreserveAdditiveSettings() throws {
    var settings = TorrentCorePreviewFixtures.runtimeSettings
    settings.metadataResolutionTimeSliceMinutes = 21
    settings.priorityMetadataAttempts = 6
    settings.downloadNoProgressTimeSliceMinutes = 17
    settings.automaticMetadataResetStuckThresholdSeconds = 45
    settings.vpnEgressValidationEnabled = true
    settings.vpnEgressValidationEndpoint = "https://vpn-check.example.test/ip"
    settings.vpnEgressDirectIspCidrs = ["198.51.100.0/24"]
    settings.vpnEgressDegradedCheckIntervalSeconds = 30
    settings.vpnEgressReadyCheckIntervalSeconds = 120
    settings.vpnEgressRequestTimeoutSeconds = 5
    settings.vpnEgressEngineSuspensionTimeoutSeconds = 7
    settings.expressVPNAutomaticRecoveryMode = "AnyValidationFailure"
    settings.expressVPNRecoveryDelaySeconds = 181
    settings.expressVPNUnavailableLaunchDelaySeconds = 301
    settings.runtimeTickDurationSummaryEnabled = true

    let update = TorrentCoreRuntimeSettingsUpdate(settings: settings)
    #expect(update.metadataResolutionTimeSliceMinutes == 21)
    #expect(update.priorityMetadataAttempts == 6)
    #expect(update.downloadNoProgressTimeSliceMinutes == 17)
    #expect(update.automaticMetadataResetStuckThresholdSeconds == 45)
    #expect(update.vpnEgressValidationEnabled)
    #expect(update.vpnEgressDirectIspCidrs == ["198.51.100.0/24"])
    #expect(update.vpnEgressEngineSuspensionTimeoutSeconds == 7)
    #expect(update.expressVPNAutomaticRecoveryMode == "AnyValidationFailure")
    #expect(update.expressVPNRecoveryDelaySeconds == 181)
    #expect(update.expressVPNUnavailableLaunchDelaySeconds == 301)
    #expect(update.runtimeTickDurationSummaryEnabled)

    let request = Components.Schemas.UpdateRuntimeSettingsRequest(update)
    let data = try JSONEncoder().encode(request)
    let body = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
    #expect(body["metadataResolutionTimeSliceMinutes"] as? Int == 21)
    #expect(body["priorityMetadataAttempts"] as? Int == 6)
    #expect(body["downloadNoProgressTimeSliceMinutes"] as? Int == 17)
    #expect(body["automaticMetadataResetStuckThresholdSeconds"] as? Int == 45)
    #expect(body["vpnEgressValidationEnabled"] as? Bool == true)
    #expect(body["vpnEgressValidationEndpoint"] as? String == "https://vpn-check.example.test/ip")
    #expect(body["vpnEgressDirectIspCidrs"] as? [String] == ["198.51.100.0/24"])
    #expect(body["vpnEgressDegradedCheckIntervalSeconds"] as? Int == 30)
    #expect(body["vpnEgressReadyCheckIntervalSeconds"] as? Int == 120)
    #expect(body["vpnEgressRequestTimeoutSeconds"] as? Int == 5)
    #expect(body["vpnEgressEngineSuspensionTimeoutSeconds"] as? Int == 7)
    #expect(body["expressVpnAutomaticRecoveryMode"] as? String == "AnyValidationFailure")
    #expect(body["expressVpnRecoveryDelaySeconds"] as? Int == 181)
    #expect(body["expressVpnUnavailableLaunchDelaySeconds"] as? Int == 301)
    #expect(body["runtimeTickDurationSummaryEnabled"] as? Bool == true)
}

@Test
func callbackFeedbackSummaryUsesDisplayMessageThenFinalResult() {
    var feedback = TorrentCorePreviewFixtures.completionCallbackFeedback

    #expect(
        TorrentCoreCompletionCallbackPresentation.feedbackSummary(feedback)
            == "TVMaze accepted the completed download."
    )

    feedback.displayMessage = " "
    #expect(TorrentCoreCompletionCallbackPresentation.feedbackSummary(feedback) == "Success")

    feedback.finalState = " "
    #expect(TorrentCoreCompletionCallbackPresentation.feedbackSummary(feedback) == nil)
    #expect(TorrentCoreCompletionCallbackPresentation.feedbackSummary(nil) == nil)
}

@Test
func operatorPresentationMatchesWebUITerms() {
    #expect(
        TorrentCoreCompletionCallbackPresentation.state("PendingFinalization")
            == "Waiting For Final Payload"
    )
    #expect(
        TorrentCoreCompletionCallbackPresentation.state("WaitingForFeedback")
            == "Waiting For TVMaze"
    )
    #expect(
        TorrentCoreCompletionCallbackPresentation.state("Invoked")
            == "Final Feedback Received"
    )
    #expect(
        TorrentCoreCompletionCallbackPresentation.state("Failed")
            == "Callback Failed"
    )
    #expect(
        TorrentCoreCompletionCallbackPresentation.state("TimedOut")
            == "Callback Timed Out"
    )
    #expect(TorrentCoreCompletionCallbackPresentation.state("FutureState") == "FutureState")
    #expect(TorrentCoreCompletionCallbackPresentation.state("Unknown") == "--")
    #expect(TorrentCoreCompletionCallbackPresentation.state(nil) == "--")
    #expect(TorrentCoreDisplayFormatter.operatorValue("Unknown") == "--")
    #expect(TorrentCoreDisplayFormatter.operatorValue(" ") == "--")
    #expect(TorrentCoreDisplayFormatter.operatorValue(nil) == "--")
    #expect(TorrentCoreDisplayFormatter.operatorValue("Success") == "Success")
}

@Test
func timestampPresentationMatchesWebUIFormat() throws {
    var components = DateComponents()
    components.calendar = Calendar.current
    components.year = 2026
    components.month = 7
    components.day = 27
    components.hour = 4
    components.minute = 29

    let date = try #require(components.date)
    #expect(TorrentCoreDisplayFormatter.timestamp(date) == "7/27/2026 4:29 AM")
    #expect(TorrentCoreDisplayFormatter.timestamp(nil) == "--")
}

@Test
func initialSliceBuildsDeterministicRequestsAndDecodesFixtures() async throws {
    let recorder = RequestRecorder()
    let transport = FixtureTransport(recorder: recorder)
    let client = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: transport,
        readTransport: transport,
        mutationTransport: transport
    )
    let torrentID = try #require(UUID(uuidString: FixturePayloads.torrentID))

    let health = try await client.probe()
    let host = try await client.hostStatus()
    let lifecycle = try await client.dashboardLifecycle()
    let torrents = try await client.torrents()
    let detail = try await client.torrent(id: torrentID)
    let categories = try await client.categories()
    let added = try await client.addMagnet("magnet:?xt=urn:btih:ABC", categoryKey: "tv")
    let paused = try await client.pause(id: torrentID)
    let resumed = try await client.resume(id: torrentID)
    let removed = try await client.remove(id: torrentID, deleteData: true)

    #expect(health.apiVersion == 1)
    #expect(host.serviceInstanceID?.uuidString == FixturePayloads.serviceInstanceID.uppercased())
    #expect(host.serviceBuild == "0123456789abcdef0123456789abcdef01234567")
    #expect(lifecycle.recentEvents.count == 1)
    #expect(torrents.first?.state.rawValue == "FutureTorrentState")
    #expect(torrents.first?.isDownloadYielded == true)
    #expect(torrents.first?.downloadNoProgressStartedAt != nil)
    #expect(torrents.first?.downloadLastYieldedAt != nil)
    #expect(detail.torrentID == torrentID)
    #expect(detail.isDownloadYielded == true)
    #expect(categories.first?.key == "tv")
    #expect(added.magnetURI == "magnet:?xt=urn:btih:ABC")
    #expect(paused.state == .paused)
    #expect(resumed.state == .downloading)
    #expect(removed.dataDeleted == true)

    let requests = await recorder.entries
    #expect(requests.map(\.operationID) == [
        "Health_Get",
        "Host_GetStatus",
        "Host_GetDashboardLifecycle",
        "Torrents_GetAll",
        "Torrents_GetById",
        "Categories_GetAll",
        "Torrents_Add",
        "Torrents_Pause",
        "Torrents_Resume",
        "Torrents_Remove",
    ])
    #expect(requests.map(\.method) == [
        "GET", "GET", "GET", "GET", "GET", "GET", "POST", "POST", "POST", "POST",
    ])
    #expect(requests[4].path == "/api/torrents/\(torrentID.uuidString)")
    #expect(requests[7].path == "/api/torrents/\(torrentID.uuidString)/pause")
    #expect(requests[8].path == "/api/torrents/\(torrentID.uuidString)/resume")
    #expect(requests[9].path == "/api/torrents/\(torrentID.uuidString)/remove")

    let addBody = try #require(requests[6].body).jsonObject
    #expect(addBody["magnetUri"] as? String == "magnet:?xt=urn:btih:ABC")
    #expect(addBody["categoryKey"] as? String == "tv")
    let removeBody = try #require(requests[9].body).jsonObject
    #expect(removeBody["deleteData"] as? Bool == true)
}

@Test
func logLevelQueriesMatchServiceContract() async throws {
    let recorder = RequestRecorder()
    let transport = FixtureTransport(recorder: recorder)
    let client = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: transport,
        readTransport: transport,
        mutationTransport: transport
    )
    let levels: [(TorrentCoreActivityLogLevel, String)] = [
        (.debug, "0"),
        (.information, "1"),
        (.warning, "2"),
        (.error, "3"),
        (.critical, "4"),
    ]

    for (level, _) in levels {
        _ = try await client.logs(query: .init(take: 100, level: level))
    }

    let requests = await recorder.entries
    #expect(requests.map(\.operationID) == Array(repeating: "Logs_GetRecent", count: levels.count))
    let sentLevels: [String] = requests.compactMap { request -> String? in
        guard let path = request.path,
              let components = URLComponents(string: "http://torrentcore.test\(path)"),
              let value = components.queryItems?.first(where: { $0.name == "level" })?.value
        else {
            return nil
        }
        return value
    }
    #expect(sentLevels == levels.map { $0.1 })
}

@Test
func historyTorrentIDQueryMatchesServiceContract() async throws {
    let recorder = RequestRecorder()
    let transport = FixtureTransport(recorder: recorder)
    let client = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: transport,
        readTransport: transport,
        mutationTransport: transport
    )
    let torrentID = try #require(UUID(uuidString: FixturePayloads.torrentID))

    _ = try await client.history(query: .init(torrentID: torrentID, take: 500))

    let requests = await recorder.entries
    let request = try #require(requests.only)
    #expect(request.operationID == "History_GetAll")
    let path = try #require(request.path)
    let components = try #require(URLComponents(string: "http://torrentcore.test\(path)"))
    let queryItems = components.queryItems ?? []
    #expect(queryItems.first(where: { $0.name == "TorrentId" })?.value == torrentID.uuidString)
    #expect(queryItems.first(where: { $0.name == "Take" })?.value == "500")
}

@Test
func filterOptionsUseDedicatedUnfilteredEndpoints() async throws {
    let recorder = RequestRecorder()
    let transport = FixtureTransport(recorder: recorder)
    let client = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: transport,
        readTransport: transport,
        mutationTransport: transport
    )

    let historyOptions = try await client.historyFilterOptions()
    let logOptions = try await client.activityLogFilterOptions()

    #expect(historyOptions.categoryKeys == ["Movies", "TV"])
    #expect(historyOptions.states == ["Completed", "Downloading"])
    #expect(logOptions.categories == ["runtime", "torrent"])
    #expect(logOptions.eventTypes == ["runtime.operation.slow", "torrent.added"])

    let requests = await recorder.entries
    #expect(requests.map(\.operationID) == [
        "History_GetFilterOptions",
        "Logs_GetFilterOptions",
    ])
    #expect(requests.map(\.path) == [
        "/api/history/filter-options",
        "/api/logs/filter-options",
    ])
}

@Test
func problemDetailsAndNetworkFailuresKeepTheirMeaning() async throws {
    let serviceClient = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: FailureTransport(failure: .problem),
        readTransport: FailureTransport(failure: .problem),
        mutationTransport: FailureTransport(failure: .problem)
    )

    do {
        _ = try await serviceClient.addMagnet("not-a-magnet")
        Issue.record("Expected a typed service error")
    } catch let TorrentCoreClientError.service(problem) {
        #expect(problem.code == "torrent.invalid_magnet")
        #expect(problem.target == "magnetUri")
        #expect(problem.traceID == "trace-123")
        #expect(problem.errors["magnetUri"] == ["A valid magnet URI is required."])
    }

    for (failure, expected) in [
        (FailureTransport.Failure.offline, "offline"),
        (.interrupted, "offline"),
        (.denied, "offline"),
        (.timeout, "timeout"),
        (.cancelled, "cancelled"),
    ] {
        let client = try TorrentCoreClient(
            baseURL: #require(URL(string: "http://torrentcore.test:7033")),
            healthTransport: FailureTransport(failure: failure),
            readTransport: FailureTransport(failure: failure),
            mutationTransport: FailureTransport(failure: failure)
        )
        do {
            _ = try await client.addMagnet("magnet:?xt=urn:btih:ABC")
            Issue.record("Expected \(expected) error")
        } catch TorrentCoreClientError.offline {
            #expect(expected == "offline")
        } catch let TorrentCoreClientError.timedOut(operation, outcomeUncertain) {
            #expect(expected == "timeout")
            #expect(operation == .addMagnet)
            #expect(outcomeUncertain)
        } catch TorrentCoreClientError.cancelled {
            #expect(expected == "cancelled")
        }
    }

    let readTimeoutClient = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: FailureTransport(failure: .timeout),
        readTransport: FailureTransport(failure: .timeout),
        mutationTransport: FailureTransport(failure: .timeout)
    )
    do {
        _ = try await readTimeoutClient.torrents()
        Issue.record("Expected a read timeout")
    } catch let TorrentCoreClientError.timedOut(operation, outcomeUncertain) {
        #expect(operation == .torrentList)
        #expect(!outcomeUncertain)
    }

    let invalidPayloadClient = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: InvalidPayloadTransport(),
        readTransport: InvalidPayloadTransport(),
        mutationTransport: InvalidPayloadTransport()
    )
    do {
        _ = try await invalidPayloadClient.probe()
        Issue.record("Expected an invalid-payload error")
    } catch TorrentCoreClientError.invalidPayload {
        // Expected.
    }
}

@Test
func baseURLValidationIsNarrowAndPredictable() throws {
    #expect(
        try TorrentCoreClient.normalizedBaseURL(
            #require(URL(string: "http://ca-server.local:7033"))
        ).absoluteString == "http://ca-server.local:7033"
    )
    #expect(throws: TorrentCoreClientError.self) {
        try TorrentCoreClient.normalizedBaseURL(
            #require(URL(string: "ftp://ca-server.local:7033"))
        )
    }
}

@Test
func liveReadOnlyIntegrationProbe() async throws {
    guard let value = ProcessInfo.processInfo.environment["TORRENTCORE_INTEGRATION_BASE_URL"],
          let baseURL = URL(string: value)
    else {
        return
    }

    let client = try TorrentCoreClient(baseURL: baseURL)
    _ = try await liveStep("health") { try await client.probe() }
    _ = try await liveStep("host status") { try await client.hostStatus() }
    _ = try await liveStep("dashboard lifecycle") { try await client.dashboardLifecycle() }
    let torrents = try await liveStep("torrent list") { try await client.torrents() }
    _ = try await liveStep("categories") { try await client.categories() }
    let history = try await liveStep("history") {
        try await client.history(query: .init(take: 100))
    }
    _ = try await liveStep("logs") {
        try await client.logs(query: .init(take: 100))
    }
    _ = try await liveStep("runtime settings") {
        try await client.runtimeSettings()
    }
    if let torrentID = torrents.first?.torrentID {
        _ = try await liveStep("torrent detail") { try await client.torrent(id: torrentID) }
        _ = try await liveStep("peers") { try await client.peers(torrentID: torrentID) }
        _ = try await liveStep("trackers") {
            try await client.trackers(torrentID: torrentID)
        }
    }
    if let historyID = history.compactMap(\.torrentID).first {
        _ = try await liveStep("history detail") {
            try await client.historyDetail(torrentID: historyID)
        }
    }
}

@Test
func liveDisposableMutationSequence() async throws {
    let environment = ProcessInfo.processInfo.environment
    guard environment["TORRENTCORE_ALLOW_DISPOSABLE_MUTATION"] == "1" else {
        return
    }
    guard let baseURLValue = environment["TORRENTCORE_INTEGRATION_BASE_URL"],
          let baseURL = URL(string: baseURLValue),
          let magnetURI = environment["TORRENTCORE_DISPOSABLE_MAGNET_URI"],
          let expectedInfoHashValue = environment["TORRENTCORE_DISPOSABLE_INFO_HASH"],
          let categoryDisplayName = environment["TORRENTCORE_DISPOSABLE_CATEGORY"]
    else {
        throw LiveMutationSafetyError(
            "The live disposable mutation gate requires the base URL, magnet URI, expected info hash, and category."
        )
    }

    let expectedInfoHash = expectedInfoHashValue.lowercased()
    guard expectedInfoHash.count == 40,
          expectedInfoHash.unicodeScalars.allSatisfy({
              CharacterSet(charactersIn: "0123456789abcdef").contains($0)
          }),
          magnetURI.lowercased().contains("xt=urn:btih:\(expectedInfoHash)")
    else {
        throw LiveMutationSafetyError(
            "The expected 40-character info hash does not match the disposable magnet URI."
        )
    }

    let client = try TorrentCoreClient(baseURL: baseURL)
    _ = try await liveStep("mutation preflight health") { try await client.probe() }
    let host = try await liveStep("mutation preflight host status") {
        try await client.hostStatus()
    }
    guard host.supportsMagnetAdds,
          host.supportsPause,
          host.supportsResume,
          host.supportsRemove
    else {
        throw LiveMutationSafetyError(
            "The live host does not report all capabilities required by the disposable sequence."
        )
    }

    let categories = try await liveStep("mutation preflight categories") {
        try await client.categories()
    }
    let matchingCategories = categories.filter {
        $0.enabled && $0.displayName == categoryDisplayName
    }
    guard matchingCategories.count == 1,
          let categoryKey = matchingCategories[0].key,
          !categoryKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    else {
        throw LiveMutationSafetyError(
            "The requested enabled category did not resolve to exactly one non-empty category key."
        )
    }

    let existingMatches = try await liveTorrents(
        matchingInfoHash: expectedInfoHash,
        client: client
    )
    guard existingMatches.isEmpty else {
        throw LiveMutationSafetyError(
            "The disposable info hash already exists on the host; no mutation was attempted."
        )
    }

    var createdTorrentID: UUID?
    do {
        let added = try await liveStep("add disposable magnet") {
            try await client.addMagnet(magnetURI, categoryKey: categoryKey)
        }
        let torrentID = try requireLiveTorrentID(added.torrentID, step: "add")
        guard added.infoHash?.lowercased() == expectedInfoHash,
              added.categoryKey == categoryKey
        else {
            createdTorrentID = torrentID
            throw LiveMutationSafetyError(
                "The add response did not match the approved info hash and resolved category."
            )
        }
        createdTorrentID = torrentID

        for observation in 1 ... 3 {
            let detail = try await liveStep("monitor disposable torrent \(observation)") {
                try await client.torrent(id: torrentID)
            }
            guard detail.infoHash?.lowercased() == expectedInfoHash else {
                throw LiveMutationSafetyError(
                    "The monitored torrent identity changed after add."
                )
            }
            print(
                "Disposable observation \(observation): "
                    + "state=\(detail.state.rawValue), "
                    + "progress=\(detail.progressPercent)"
            )
            if observation < 3 {
                try await Task.sleep(for: .seconds(2))
            }
        }

        _ = try await waitForLiveTorrent(
            client: client,
            id: torrentID,
            step: "wait for pause capability",
            accepting: \.canPause
        )
        let paused = try await liveStep("pause disposable torrent") {
            try await client.pause(id: torrentID)
        }
        guard paused.torrentID == torrentID else {
            throw LiveMutationSafetyError("The pause response returned a different torrent ID.")
        }
        _ = try await waitForLiveTorrent(
            client: client,
            id: torrentID,
            step: "confirm paused state"
        ) {
            $0.state == .paused && $0.canResume
        }

        let resumed = try await liveStep("resume disposable torrent") {
            try await client.resume(id: torrentID)
        }
        guard resumed.torrentID == torrentID else {
            throw LiveMutationSafetyError("The resume response returned a different torrent ID.")
        }
        _ = try await waitForLiveTorrent(
            client: client,
            id: torrentID,
            step: "confirm resumed state"
        ) {
            $0.state != .paused && $0.canPause
        }

        let removed = try await liveStep("remove disposable torrent and data") {
            try await client.remove(id: torrentID, deleteData: true)
        }
        guard removed.torrentID == torrentID,
              removed.dataDeleted == true
        else {
            throw LiveMutationSafetyError(
                "The remove response did not confirm the approved torrent ID and data deletion."
            )
        }
        let remaining = try await liveStep("confirm disposable torrent removal") {
            try await client.torrents()
        }
        guard !remaining.contains(where: { $0.torrentID == torrentID }) else {
            throw LiveMutationSafetyError(
                "The disposable torrent still appears in the authoritative list after removal."
            )
        }
        createdTorrentID = nil
    } catch {
        let primaryError = error
        let cleanupID: UUID?
        if let createdTorrentID {
            cleanupID = createdTorrentID
        } else {
            cleanupID = try await liveTorrents(
                matchingInfoHash: expectedInfoHash,
                client: client
            ).only
        }

        if let cleanupID {
            do {
                _ = try await client.remove(id: cleanupID, deleteData: true)
                let remaining = try await client.torrents()
                guard !remaining.contains(where: { $0.torrentID == cleanupID }) else {
                    throw LiveMutationSafetyError(
                        "The cleanup target remains in the authoritative torrent list."
                    )
                }
            } catch {
                throw LiveMutationSafetyError(
                    "The disposable sequence failed (\(primaryError)); cleanup also failed (\(error))."
                )
            }
        }
        throw primaryError
    }
}

private struct LiveProbeStepError: Error, CustomStringConvertible {
    let step: String
    let underlying: any Error

    var description: String {
        "\(step): \(underlying)"
    }
}

private struct LiveMutationSafetyError: Error, CustomStringConvertible {
    let description: String

    init(_ description: String) {
        self.description = description
    }
}

private func liveStep<Value>(
    _ step: String,
    operation: () async throws -> Value
) async throws -> Value {
    do {
        return try await operation()
    } catch {
        throw LiveProbeStepError(step: step, underlying: error)
    }
}

private func requireLiveTorrentID(_ id: UUID?, step: String) throws -> UUID {
    guard let id else {
        throw LiveMutationSafetyError(
            "The \(step) response did not include a torrent ID."
        )
    }
    return id
}

private func liveTorrents(
    matchingInfoHash expectedInfoHash: String,
    client: TorrentCoreClient
) async throws -> [UUID] {
    let summaries = try await liveStep("disposable target preflight list") {
        try await client.torrents()
    }
    var matches: [UUID] = []
    for torrentID in summaries.compactMap(\.torrentID) {
        let detail = try await liveStep("disposable target preflight detail") {
            try await client.torrent(id: torrentID)
        }
        if detail.infoHash?.lowercased() == expectedInfoHash {
            matches.append(torrentID)
        }
    }
    return matches
}

private func waitForLiveTorrent(
    client: TorrentCoreClient,
    id: UUID,
    step: String,
    accepting: (TorrentCoreTorrentDetail) -> Bool
) async throws -> TorrentCoreTorrentDetail {
    for attempt in 1 ... 15 {
        let detail = try await liveStep(step) {
            try await client.torrent(id: id)
        }
        if accepting(detail) {
            return detail
        }
        if attempt < 15 {
            try await Task.sleep(for: .seconds(2))
        }
    }
    throw LiveMutationSafetyError(
        "\(step) did not become true within 30 seconds."
    )
}

private extension Array {
    var only: Element? {
        count == 1 ? self[0] : nil
    }
}

private actor RequestRecorder {
    struct Entry: Sendable {
        var operationID: String
        var method: String
        var path: String?
        var body: String?
    }

    private(set) var entries: [Entry] = []

    func append(_ entry: Entry) {
        entries.append(entry)
    }
}

private struct FixtureTransport: ClientTransport {
    let recorder: RequestRecorder

    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        let bodyString: String?
        if let body {
            bodyString = try await String(collecting: body, upTo: 1_048_576)
        } else {
            bodyString = nil
        }
        await recorder.append(.init(
            operationID: operationID,
            method: request.method.rawValue,
            path: request.path,
            body: bodyString
        ))

        let fixture = FixturePayloads.response(for: operationID)
        let headers: HTTPFields = [.contentType: "application/json"]
        return (
            HTTPResponse(status: .init(code: fixture.statusCode), headerFields: headers),
            HTTPBody(fixture.body)
        )
    }
}

private struct FailureTransport: ClientTransport {
    enum Failure: Sendable {
        case problem
        case offline
        case interrupted
        case denied
        case timeout
        case cancelled
    }

    let failure: Failure

    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        switch failure {
        case .problem:
            let headers: HTTPFields = [.contentType: "application/problem+json"]
            return (
                HTTPResponse(status: .badRequest, headerFields: headers),
                HTTPBody(FixturePayloads.problem)
            )
        case .offline:
            throw URLError(.cannotConnectToHost)
        case .interrupted:
            throw URLError(.networkConnectionLost)
        case .denied:
            throw URLError(.dataNotAllowed)
        case .timeout:
            throw URLError(.timedOut)
        case .cancelled:
            throw CancellationError()
        }
    }
}

private struct InvalidPayloadTransport: ClientTransport {
    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        let headers: HTTPFields = [.contentType: "application/json"]
        return (HTTPResponse(status: .ok, headerFields: headers), HTTPBody("{}"))
    }
}

private enum FixturePayloads {
    static let torrentID = "11111111-2222-3333-4444-555555555555"
    static let serviceInstanceID = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

    static func response(for operationID: String) -> (statusCode: Int, body: String) {
        switch operationID {
        case "Health_Get":
            (200, health)
        case "Host_GetStatus":
            (200, host)
        case "Host_GetDashboardLifecycle":
            (200, lifecycle)
        case "Torrents_GetAll":
            (200, "[\(summary)]")
        case "Torrents_GetById":
            (200, detail)
        case "Categories_GetAll":
            (200, "[\(category)]")
        case "Logs_GetRecent":
            (200, "[]")
        case "History_GetAll":
            (200, "[]")
        case "History_GetFilterOptions":
            (200, historyFilterOptions)
        case "Logs_GetFilterOptions":
            (200, activityLogFilterOptions)
        case "Torrents_Add":
            (201, detail)
        case "Torrents_Pause":
            (200, action(state: "Paused", action: "pause", dataDeleted: false))
        case "Torrents_Resume":
            (200, action(state: "Downloading", action: "resume", dataDeleted: false))
        case "Torrents_Remove":
            (200, action(state: "Removed", action: "remove", dataDeleted: true))
        default:
            (500, "{}")
        }
    }

    static let health = """
    {
      "apiVersion": 1,
      "serviceName": "TorrentCore.Service",
      "status": "ok",
      "environmentName": "Integration",
      "checkedAtUtc": "2026-07-23T12:00:00Z"
    }
    """

    static let historyFilterOptions = """
    {
      "categoryKeys": ["Movies", "TV"],
      "states": ["Completed", "Downloading"]
    }
    """

    static let activityLogFilterOptions = """
    {
      "categories": ["runtime", "torrent"],
      "eventTypes": ["runtime.operation.slow", "torrent.added"]
    }
    """

    static let host = """
    {
      "apiVersion": 1,
      "serviceName": "TorrentCore.Service",
      "serviceVersion": "1.0.0",
      "serviceBuild": "0123456789abcdef0123456789abcdef01234567",
      "serviceInstanceId": "\(serviceInstanceID)",
      "engineRuntime": "MonoTorrent",
      "engineListenPort": 55123,
      "engineDhtPort": 55123,
      "enginePortForwardingEnabled": true,
      "engineLocalPeerDiscoveryEnabled": true,
      "engineAllowPeerExchange": false,
      "engineEncryptionMode": "EncryptedPreferred",
      "engineMaximumConnections": 200,
      "engineMaximumHalfOpenConnections": 20,
      "engineMaximumDownloadRateBytesPerSecond": 0,
      "engineMaximumUploadRateBytesPerSecond": 0,
      "engineConnectionFailureLogBurstLimit": 10,
      "engineConnectionFailureLogWindowSeconds": 60,
      "maxActiveMetadataResolutions": 2,
      "maxActiveDownloads": 3,
      "availableMetadataResolutionSlots": 1,
      "availableDownloadSlots": 2,
      "resolvingMetadataCount": 1,
      "metadataQueueCount": 0,
      "downloadingCount": 1,
      "downloadQueueCount": 0,
      "seedingCount": 1,
      "pausedCount": 0,
      "completedCount": 2,
      "errorCount": 0,
      "currentConnectedPeerCount": 8,
      "currentDownloadRateBytesPerSecond": 4096,
      "currentUploadRateBytesPerSecond": 1024,
      "partialFilesEnabled": true,
      "partialFileSuffix": ".!mt",
      "seedingStopMode": "Ratio",
      "seedingStopRatio": 2.0,
      "seedingStopMinutes": 0,
      "completedTorrentCleanupMode": "Never",
      "completedTorrentCleanupMinutes": 0,
      "deleteLogsForCompletedTorrents": false,
      "status": "Ready",
      "environmentName": "Integration",
      "downloadRootPath": "/downloads",
      "torrentCount": 5,
      "supportsMagnetAdds": true,
      "supportsPause": true,
      "supportsResume": true,
      "supportsRemove": true,
      "supportsPersistentStorage": true,
      "supportsMultiHost": false,
      "startupRecoveryCompleted": true,
      "startupRecoveredTorrentCount": 2,
      "startupNormalizedTorrentCount": 1,
      "startupRecoveryCompletedAtUtc": "2026-07-23T11:59:00Z",
      "checkedAtUtc": "2026-07-23T12:00:00Z"
    }
    """

    static let lifecycle = """
    {
      "callbackFailedCount": 0,
      "callbackInvokedCount": 1,
      "callbackTimedOutCount": 0,
      "completedAutoRemovedCount": 0,
      "firstEventAtUtc": "2026-07-23T11:00:00Z",
      "lastEventAtUtc": "2026-07-23T12:00:00Z",
      "metadataRefreshRequestedCount": 1,
      "metadataResetRequestedCount": 0,
      "metadataResolvedCount": 1,
      "metadataRestartRequestedCount": 0,
      "orphanedTorrentLogsDeletedCount": 0,
      "recentEvents": [{
        "category": "Runtime",
        "eventType": "runtime.ready",
        "level": "Information",
        "message": "TorrentCore is ready.",
        "occurredAtUtc": "2026-07-23T12:00:00Z",
        "torrentId": "\(torrentID)"
      }],
      "recoveryCompletedAtUtc": "2026-07-23T11:59:00Z",
      "serviceInstanceId": "\(serviceInstanceID)",
      "startupNormalizedTorrentCount": 1,
      "startupReadyAtUtc": "2026-07-23T11:59:30Z",
      "startupRecoveredTorrentCount": 2,
      "torrentsAddedCount": 4,
      "torrentsRemovedCount": 1
    }
    """

    static let summary = """
    {
      "addedAtUtc": "2026-07-23T10:00:00Z",
      "canPause": true,
      "canMakeNext": false,
      "canHold": false,
      "canReleaseHold": false,
      "canResumeNext": false,
      "canResumeOnHold": false,
      "isQueueHeld": false,
      "isDownloadYielded": true,
      "downloadNoProgressStartedAtUtc": "2026-07-23T10:15:00Z",
      "downloadLastYieldedAtUtc": "2026-07-23T10:30:00Z",
      "canRefreshMetadata": false,
      "canRemove": true,
      "canResume": false,
      "canRetryCompletionCallback": false,
      "categoryKey": "tv",
      "connectedPeerCount": 5,
      "downloadRateBytesPerSecond": 4096,
      "downloadedBytes": 524288,
      "name": "Fixture Torrent",
      "progressPercent": 50.0,
      "state": "FutureTorrentState",
      "torrentId": "\(torrentID)",
      "totalBytes": 1048576,
      "trackerCount": 2,
      "uploadRateBytesPerSecond": 512,
      "waitReason": "FutureWaitReason"
    }
    """

    static let detail = """
    {
      "addedAtUtc": "2026-07-23T10:00:00Z",
      "canPause": true,
      "canMakeNext": false,
      "canHold": false,
      "canReleaseHold": false,
      "canResumeNext": false,
      "canResumeOnHold": false,
      "isQueueHeld": false,
      "isDownloadYielded": true,
      "downloadNoProgressStartedAtUtc": "2026-07-23T10:15:00Z",
      "downloadLastYieldedAtUtc": "2026-07-23T10:30:00Z",
      "canRefreshMetadata": false,
      "canRemove": true,
      "canResume": false,
      "canRetryCompletionCallback": false,
      "categoryKey": "tv",
      "connectedPeerCount": 5,
      "downloadRateBytesPerSecond": 4096,
      "downloadedBytes": 524288,
      "infoHash": "ABC",
      "magnetUri": "magnet:?xt=urn:btih:ABC",
      "name": "Fixture Torrent",
      "progressPercent": 50.0,
      "savePath": "/downloads/Fixture Torrent",
      "state": "Downloading",
      "torrentId": "\(torrentID)",
      "totalBytes": 1048576,
      "trackerCount": 2,
      "uploadRateBytesPerSecond": 512
    }
    """

    static let category = """
    {
      "callbackLabel": "TV",
      "displayName": "Television",
      "downloadRootPath": "/downloads/tv",
      "enabled": true,
      "invokeCompletionCallback": true,
      "key": "tv",
      "sortOrder": 10
    }
    """

    static func action(state: String, action: String, dataDeleted: Bool) -> String {
        """
        {
          "action": "\(action)",
          "dataDeleted": \(dataDeleted),
          "processedAtUtc": "2026-07-23T12:00:00Z",
          "state": "\(state)",
          "torrentId": "\(torrentID)"
        }
        """
    }

    static let problem = """
    {
      "type": "https://torrentcore.local/problems/invalid-magnet",
      "title": "Invalid magnet",
      "status": 400,
      "detail": "The magnet URI is invalid.",
      "code": "torrent.invalid_magnet",
      "target": "magnetUri",
      "traceId": "trace-123",
      "errors": {
        "magnetUri": ["A valid magnet URI is required."]
      }
    }
    """
}

private extension String {
    var jsonObject: [String: Any] {
        get throws {
            let object = try JSONSerialization.jsonObject(with: Data(utf8))
            return try #require(object as? [String: Any])
        }
    }
}
