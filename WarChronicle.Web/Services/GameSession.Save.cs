using System.Text.Json;
using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

public sealed partial class GameSession
{
    private const int CurrentSaveVersion = 1;

    private sealed class GameSaveSnapshot
    {
        public GameSaveSnapshot() { }

        public int SaveVersion { get; set; } = CurrentSaveVersion;
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

        public int Year { get; set; }
        public string Season { get; set; } = "Spring";
        public int Time { get; set; }
        public int Morale { get; set; }
        public int Leadership { get; set; }
        public GamePhase Phase { get; set; }
        public MobilizationStep MobilizationStep { get; set; }
        public TerrainType CurrentExploreTerrain { get; set; }
        public CampStep CampStep { get; set; }
        public EndOfSeasonStep EndOfSeasonStep { get; set; }
        public WinterStep WinterStep { get; set; }

        public int TerrainSerial { get; set; }
        public int ClearingSerial { get; set; }
        public int NextMapSequence { get; set; }
        public bool HasPriest { get; set; }
        public string? PriestSource { get; set; }
        public string? CurrentArrivalTribe { get; set; }
        public bool ArrivalCombatResolved { get; set; }
        public bool ArrivalCardRevealed { get; set; }
        public bool CampInitialized { get; set; }
        public bool CampPhaseEnding { get; set; }
        public bool CampCardResolvedSuccessfully { get; set; }
        public bool EndOfSeasonPending { get; set; }
        public GamePhase EndOfSeasonTriggerPhase { get; set; }
        public string? EndOfSeasonName { get; set; }
        public int HarvestMarketSalesRemaining { get; set; }
        public bool CombatDisengagedPending { get; set; }
        public bool SkipNextDisengagePenaltyAction { get; set; }
        public string? CampaignOutcome { get; set; }
        public string? CampaignEndReason { get; set; }
        public Guid GameLogId { get; set; }
        public DateTime GameStartedUtc { get; set; }
        public DateTime? GameEndedUtc { get; set; }
        public List<CompletedBattleLog> BattleHistory { get; set; } = [];

        public List<ResourceState> Resources { get; set; } = [];
        public List<MarketState> Market { get; set; } = [];
        public List<HostUnit> Host { get; set; } = [];
        public List<AdvancementOfferState> AdvancementOffer { get; set; } = [];
        public List<AdvancementOfferState> AcquiredAdvancements { get; set; } = [];
        public List<AvailableForceState> AvailableForces { get; set; } = [];
        public List<PersistentState> PersistentItems { get; set; } = [];
        public List<string> ActiveTokens { get; set; } = [];
        public List<BaggageSlot> Baggage { get; set; } = [];
        public List<TribeState> Tribes { get; set; } = [];
        public List<PlayAreaElement> PlayArea { get; set; } = [];
        public List<ChronicleEntry> Chronicle { get; set; } = [];
        public List<TerrainType> PendingTerrainChoices { get; set; } = [];
        public List<CampCardState> CampHand { get; set; } = [];

        public List<TerrainType> TerrainDeck { get; set; } = [];
        public List<ExploreCardState> ExploreDeck { get; set; } = [];
        public List<ArrivalCardState> ArrivalDeck { get; set; } = [];
        public List<CampCardState> CampDeck { get; set; } = [];
        public List<CampCardState> CampDiscard { get; set; } = [];
        public List<CampCardState> UnseededScenes { get; set; } = [];
        public List<CampCardState> UnseededEchoes { get; set; } = [];
        public List<CampCardState> RemovedCampCards { get; set; } = [];
        public List<bool> ClearingDeck { get; set; } = [];
        public List<AdvancementOfferState> AdvancementDeck { get; set; } = [];
        public List<AdvancementOfferState> ArmorSupply { get; set; } = [];
        public CampCardState? WorkCard { get; set; }
        public List<string> SeededEchoes { get; set; } = [];
        public List<string> EnabledContentSets { get; set; } = [];
        public Dictionary<string, string> EchoOrigins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> HarvestStaging { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HarvestPacked { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HarvestPurged { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HarvestSold { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions SaveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public bool CanSaveGame
    {
        get
        {
            if (Combat is not null || Finale is not null)
                return false;
            if (_activeFlow is not null || FlowPrompt is not null || !string.IsNullOrWhiteSpace(_specialPrompt))
                return false;
            if (PendingTerrainChoices.Count > 0 || PendingExploreCard is not null || PendingArrivalCard is not null || PendingCampCard is not null)
                return false;
            if (_pendingResourceGains.Count > 0 || _pendingWinterAttritionLosses.Count > 0)
                return false;
            if (EndOfSeasonStep is EndOfSeasonStep.HarvestPreparing or EndOfSeasonStep.HarvestPacking)
                return false;
            if (Phase == GamePhase.Winter && WinterStep == WinterStep.Attrition)
                return false;
            return true;
        }
    }

    public string SaveUnavailableReason
    {
        get
        {
            if (Combat is not null || Finale is not null)
                return "Finish the current battle or Finale step before saving.";
            if (EndOfSeasonStep is EndOfSeasonStep.HarvestPreparing or EndOfSeasonStep.HarvestPacking)
                return "Finish the current Harvest step before saving.";
            if (FlowPrompt is not null || _activeFlow is not null || PendingExploreCard is not null || PendingArrivalCard is not null || PendingCampCard is not null)
                return "Finish the current card or decision before saving.";
            if (PendingTerrainChoices.Count > 0)
                return "Choose the current Terrain before saving.";
            if (Phase == GamePhase.Winter && WinterStep == WinterStep.Attrition)
                return "Finish Winter Attrition before saving.";
            return "Finish the current resolution before saving.";
        }
    }

    public string CreateSaveJson()
    {
        if (!CanSaveGame)
            throw new InvalidOperationException(SaveUnavailableReason);

        var save = new GameSaveSnapshot
        {
            Year = Year,
            Season = Season,
            Time = Time,
            Morale = Morale,
            Leadership = Leadership,
            Phase = Phase,
            MobilizationStep = MobilizationStep,
            CurrentExploreTerrain = CurrentExploreTerrain,
            CampStep = CampStep,
            EndOfSeasonStep = EndOfSeasonStep,
            WinterStep = WinterStep,
            TerrainSerial = _terrainSerial,
            ClearingSerial = _clearingSerial,
            NextMapSequence = _nextMapSequence,
            HasPriest = _hasPriest,
            PriestSource = _priestSource,
            CurrentArrivalTribe = _currentArrivalTribe,
            ArrivalCombatResolved = _arrivalCombatResolved,
            ArrivalCardRevealed = _arrivalCardRevealed,
            CampInitialized = _campInitialized,
            CampPhaseEnding = _campPhaseEnding,
            CampCardResolvedSuccessfully = _campCardResolvedSuccessfully,
            EndOfSeasonPending = _endOfSeasonPending,
            EndOfSeasonTriggerPhase = _endOfSeasonTriggerPhase,
            EndOfSeasonName = _endOfSeasonName,
            HarvestMarketSalesRemaining = _harvestMarketSalesRemaining,
            CombatDisengagedPending = _combatDisengagedPending,
            SkipNextDisengagePenaltyAction = _skipNextDisengagePenaltyAction,
            CampaignOutcome = CampaignOutcome,
            CampaignEndReason = CampaignEndReason,
            GameLogId = GameLogId,
            GameStartedUtc = GameStartedUtc,
            GameEndedUtc = GameEndedUtc,
            BattleHistory = BattleHistory.ToList(),
            Resources = Resources.ToList(),
            Market = Market.ToList(),
            Host = Host.ToList(),
            AdvancementOffer = AdvancementOffer.ToList(),
            AcquiredAdvancements = AcquiredAdvancements.ToList(),
            AvailableForces = AvailableForces.ToList(),
            PersistentItems = PersistentItems.ToList(),
            ActiveTokens = ActiveTokens.ToList(),
            Baggage = Baggage.ToList(),
            Tribes = Tribes.ToList(),
            PlayArea = PlayArea.ToList(),
            Chronicle = Chronicle.ToList(),
            PendingTerrainChoices = PendingTerrainChoices.ToList(),
            CampHand = CampHand.ToList(),
            TerrainDeck = _terrainDeck.ToList(),
            ExploreDeck = _exploreDeck.ToList(),
            ArrivalDeck = _arrivalDeck.ToList(),
            CampDeck = _campDeck.ToList(),
            CampDiscard = _campDiscard.ToList(),
            UnseededScenes = _unseededScenes.ToList(),
            UnseededEchoes = _unseededEchoes.ToList(),
            RemovedCampCards = _removedCampCards.ToList(),
            ClearingDeck = _clearingDeck.ToList(),
            AdvancementDeck = _advancementDeck.ToList(),
            ArmorSupply = _armorSupply.ToList(),
            WorkCard = _workCard,
            SeededEchoes = _seededEchoes.ToList(),
            EnabledContentSets = _enabledContentSets.ToList(),
            EchoOrigins = new(_echoOrigins, StringComparer.OrdinalIgnoreCase),
            HarvestStaging = new(_harvestStaging, StringComparer.OrdinalIgnoreCase),
            HarvestPacked = new(_harvestPacked, StringComparer.OrdinalIgnoreCase),
            HarvestPurged = new(_harvestPurged, StringComparer.OrdinalIgnoreCase),
            HarvestSold = new(_harvestSold, StringComparer.OrdinalIgnoreCase)
        };

        return JsonSerializer.Serialize(save, SaveJsonOptions);
    }

    public string CreateGameLogJson(string appVersion)
    {
        var finalHost = Host
            .ToDictionary(h => h.Type, h => h.Count, StringComparer.OrdinalIgnoreCase);
        var finalResources = Resources
            .ToDictionary(r => r.Name, r => r.Value, StringComparer.OrdinalIgnoreCase);

        object? activeBattle = null;
        if (Combat is { IsTest: false } activeCombat)
        {
            activeBattle = new
            {
                startedUtc = activeCombat.StartedUtc,
                sourceLabel = activeCombat.SourceLabel,
                enemyName = activeCombat.EnemyName,
                round = activeCombat.Round,
                step = activeCombat.Step.ToString(),
                playerUnits = activeCombat.PlayerUnits,
                enemyUnits = activeCombat.EnemyUnits,
                entries = activeCombat.Log.ToArray()
            };
        }

        var document = new
        {
            schemaVersion = 1,
            logType = "War Chronicle Game Log",
            gameId = GameLogId,
            appVersion,
            startedUtc = GameStartedUtc,
            endedUtc = GameEndedUtc,
            status = GameEndedUtc is null ? "InProgress" : "Completed",
            outcome = CampaignOutcome,
            endReason = CampaignEndReason,
            contentSets = _enabledContentSets.OrderBy(x => x).ToArray(),
            finalState = new
            {
                year = Year,
                season = Season,
                time = Time,
                morale = Morale,
                leadership = Leadership,
                host = finalHost,
                resources = finalResources,
                activeTokens = ActiveTokens.OrderBy(x => x).ToArray(),
                baggage = Baggage.ToArray()
            },
            chronicle = Chronicle.ToArray(),
            battles = BattleHistory.ToArray(),
            activeBattle,
            finaleLog = Finale?.Log.ToArray() ?? Array.Empty<string>()
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    public bool TryRestoreSaveJson(string json, WcDataCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var save = JsonSerializer.Deserialize<GameSaveSnapshot>(json, SaveJsonOptions);
            if (save is null || save.SaveVersion != CurrentSaveVersion)
                return false;

            ResetNewGameState();
            _catalog = catalog;
            _catalogInitialized = true;
            _enabledContentSets.Clear();
            _enabledContentSets.Add("Base");
            foreach (var set in save.EnabledContentSets ?? [])
            {
                if (string.Equals(set, "SitA", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(set, "SiaSL", StringComparison.OrdinalIgnoreCase))
                    _enabledContentSets.Add(set);
            }

            Year = save.Year;
            Season = save.Season;
            Time = save.Time;
            Morale = save.Morale;
            Leadership = save.Leadership;
            Phase = save.Phase;
            MobilizationStep = save.MobilizationStep;
            CurrentExploreTerrain = save.CurrentExploreTerrain;
            CampStep = save.CampStep;
            EndOfSeasonStep = save.EndOfSeasonStep;
            WinterStep = save.WinterStep;
            _terrainSerial = save.TerrainSerial;
            _clearingSerial = save.ClearingSerial;
            _nextMapSequence = save.NextMapSequence;
            _hasPriest = save.HasPriest;
            _priestSource = save.PriestSource;
            _currentArrivalTribe = save.CurrentArrivalTribe;
            _arrivalCombatResolved = save.ArrivalCombatResolved;
            _arrivalCardRevealed = save.ArrivalCardRevealed;
            _campInitialized = save.CampInitialized;
            _campPhaseEnding = save.CampPhaseEnding;
            _campCardResolvedSuccessfully = save.CampCardResolvedSuccessfully;
            _endOfSeasonPending = save.EndOfSeasonPending;
            _endOfSeasonTriggerPhase = save.EndOfSeasonTriggerPhase;
            _endOfSeasonName = save.EndOfSeasonName;
            _harvestMarketSalesRemaining = save.HarvestMarketSalesRemaining;
            _combatDisengagedPending = save.CombatDisengagedPending;
            _skipNextDisengagePenaltyAction = save.SkipNextDisengagePenaltyAction;
            CampaignOutcome = save.CampaignOutcome;
            CampaignEndReason = save.CampaignEndReason;
            if (save.GameLogId != Guid.Empty)
                GameLogId = save.GameLogId;
            if (save.GameStartedUtc != default)
                GameStartedUtc = save.GameStartedUtc;
            GameEndedUtc = save.GameEndedUtc;
            CopyList(BattleHistory, save.BattleHistory);

            CopyList(Resources, save.Resources);
            CopyList(Market, save.Market);
            CopyList(Host, save.Host);
            CopyList(AdvancementOffer, save.AdvancementOffer);
            CopyList(AcquiredAdvancements, save.AcquiredAdvancements);
            CopyList(AvailableForces, save.AvailableForces);
            CopyList(PersistentItems, save.PersistentItems);
            ActiveTokens.Clear();
            foreach (var token in save.ActiveTokens)
                ActiveTokens.Add(token);
            CopyList(Baggage, save.Baggage);
            CopyList(Tribes, save.Tribes);
            CopyList(PlayArea, save.PlayArea);
            CopyList(Chronicle, save.Chronicle);
            CopyList(PendingTerrainChoices, save.PendingTerrainChoices);
            CopyList(CampHand, save.CampHand);
            CopyList(_terrainDeck, save.TerrainDeck);
            CopyList(_exploreDeck, save.ExploreDeck);
            CopyList(_arrivalDeck, save.ArrivalDeck);
            CopyList(_campDeck, save.CampDeck);
            CopyList(_campDiscard, save.CampDiscard);
            CopyList(_unseededScenes, save.UnseededScenes);
            CopyList(_unseededEchoes, save.UnseededEchoes);
            CopyList(_removedCampCards, save.RemovedCampCards);
            CopyList(_clearingDeck, save.ClearingDeck);
            CopyList(_advancementDeck, save.AdvancementDeck);
            CopyList(_armorSupply, save.ArmorSupply);
            _workCard = save.WorkCard ?? catalog.GetCampCards(includeOptional: false).FirstOrDefault(c => c.Id == "CAMP-001");
            CopyList(_seededEchoes, save.SeededEchoes);
            CopyDictionary(_echoOrigins, save.EchoOrigins);
            CopyDictionary(_harvestStaging, save.HarvestStaging);
            CopyDictionary(_harvestPacked, save.HarvestPacked);
            CopyDictionary(_harvestPurged, save.HarvestPurged);
            CopyDictionary(_harvestSold, save.HarvestSold);

            // Saves are intentionally made only at stable checkpoints. Any
            // transient flow/combat machinery must therefore resume clean.
            PendingExploreCard = null;
            PendingArrivalCard = null;
            PendingCampCard = null;
            _flowPrompt = null;
            _activeFlow = null;
            _activeNodeId = null;
            _activeFlowContext = FlowContext.None;
            _flowVars.Clear();
            _lastRollResults.Clear();
            _lastUnitTypeRolls.Clear();
            _pendingSelectedUnits.Clear();
            _pendingResourceGains.Clear();
            _pendingSelectVariable = null;
            _pendingSelectNextNode = null;
            _pendingMultiSelectRemaining = 0;
            _pendingMultiSelectVariable = null;
            _pendingMultiSelectNextNode = null;
            _pendingCombatNextNode = null;
            _pendingTestNarrativeLabel = null;
            _specialPrompt = null;
            _activeChronicleEntryIndex = null;
            _activeChronicleDecisionIndex = null;
            _pendingTributeFallbackCost = 0;
            _pendingTributeFallbackResource = null;
            _pendingCampActionNextNode = null;
            _pendingResourceGainResume = ResourceGainResume.None;
            _pendingResourceGainNextNode = null;
            _winterAttritionRolls.Clear();
            _pendingWinterAttritionLosses.Clear();
            _refitUndo.Clear();
            _harvestPreparationUndo.Clear();
            _harvestPackingUndo.Clear();
            _viewedCampCards.Clear();
            Combat = null;
            CombatVisible = false;
            Finale = null;
            _combatResumeMode = CombatResumeMode.None;
            _combatResumeNextNode = null;
            _combatSourceTribe = null;

            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to restore War Chronicle browser save.");
            return false;
        }
    }

    public void StartNewGame(WcDataCatalog catalog, IEnumerable<string>? enabledSets = null)
    {
        ResetNewGameState();
        InitializeCatalogState(catalog, enabledSets);
    }

    private static void CopyList<T>(List<T> target, IEnumerable<T>? source)
    {
        target.Clear();
        if (source is not null)
            target.AddRange(source);
    }

    private static void CopyDictionary(Dictionary<string, int> target, IReadOnlyDictionary<string, int>? source)
    {
        target.Clear();
        if (source is null)
            return;
        foreach (var pair in source)
            target[pair.Key] = pair.Value;
    }

    private static void CopyDictionary(Dictionary<string, string> target, IReadOnlyDictionary<string, string>? source)
    {
        target.Clear();
        if (source is null)
            return;
        foreach (var pair in source)
            target[pair.Key] = pair.Value;
    }
}
