using System.Text.Json;
using System.Text.RegularExpressions;
using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

/// <summary>
/// Authoritative browser-facing game state. The implemented rules slice now
/// covers New Game setup, Mobilization / Explore / Arrival, Camp, End of
/// Season, Winter, and interactive Combat.
/// </summary>
public sealed partial class GameSession
{
    private enum FlowContext
    {
        None,
        Explore,
        Arrival,
        Camp
    }

    private enum ResourceGainResume
    {
        None,
        Flow,
        CompleteArrival
    }

    private enum HarvestUndoKind
    {
        Purge,
        Sell,
        Pack
    }

    private sealed record HarvestUndoAction(HarvestUndoKind Kind, int Position, string Resource, int CoinAmount = 0);

    private sealed record RefitUndoSnapshot(
        int ChronicleCount,
        int Morale,
        int Leadership,
        List<ResourceState> Resources,
        List<HostUnit> Host,
        List<AvailableForceState> AvailableForces,
        List<BaggageSlot> Baggage,
        List<AdvancementOfferState> AdvancementOffer,
        List<AdvancementOfferState> AcquiredAdvancements,
        List<AdvancementOfferState> AdvancementDeck,
        List<AdvancementOfferState> ArmorSupply,
        List<PersistentState> PersistentItems,
        HashSet<string> ActiveTokens);

    private readonly ILogger<GameSession> _logger;
    private readonly List<TerrainType> _terrainDeck = [];
    private readonly List<ExploreCardState> _exploreDeck = [];
    private readonly List<ArrivalCardState> _arrivalDeck = [];
    private readonly List<CampCardState> _campDeck = [];
    private readonly List<CampCardState> _campDiscard = [];
    private readonly List<CampCardState> _unseededScenes = [];
    private readonly List<CampCardState> _unseededEchoes = [];
    private readonly List<CampCardState> _removedCampCards = [];
    private readonly List<bool> _clearingDeck = [];
    private readonly List<AdvancementOfferState> _advancementDeck = [];
    private readonly List<AdvancementOfferState> _armorSupply = [];
    private readonly List<int> _lastRollResults = [];
    private readonly Dictionary<string, int> _lastUnitTypeRolls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _flowVars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pendingSelectedUnits = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _seededEchoes = [];
    private readonly Dictionary<string, string> _echoOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledContentSets = new(StringComparer.OrdinalIgnoreCase) { "Base" };
    private readonly List<CampCardState> _viewedCampCards = [];
    private readonly List<(string Resource, int Amount)> _pendingResourceGains = [];

    private WcDataCatalog? _catalog;
    private JsonElement? _activeFlow;
    private string? _activeNodeId;
    private string? _lastRollText;
    private bool _catalogInitialized;
    private int _terrainSerial;
    private int _clearingSerial;
    private int _nextMapSequence;
    private FlowContext _activeFlowContext;
    private string? _currentArrivalTribe;
    private bool _arrivalCombatResolved;
    private bool _hasPriest;
    private string? _priestSource;
    private int _pendingTributeFallbackCost;
    private string? _pendingTributeFallbackResource;

    private string? _pendingSelectVariable;
    private string? _pendingSelectNextNode;
    private int _pendingMultiSelectRemaining;
    private string? _pendingMultiSelectVariable;
    private string? _pendingMultiSelectNextNode;
    private string? _pendingCombatNextNode;
    private string? _pendingTestNarrativeLabel;
    private string? _pendingOutcomeText;
    private string? _pendingBrowserFlavor;
    private string? _specialPrompt;
    private int? _activeChronicleEntryIndex;
    private int? _activeChronicleDecisionIndex;
    private FlowPromptState? _flowPrompt;
    private bool _arrivalCardRevealed;
    private bool _campInitialized;
    private bool _campPhaseEnding;
    private bool _campCardResolvedSuccessfully;
    private CampCardState? _workCard;
    private int _campMarketTradesRemaining;
    private string? _pendingCampActionNextNode;
    private ResourceGainResume _pendingResourceGainResume;
    private string? _pendingResourceGainNextNode;
    private bool _pendingResourceGainOpenStaging;

    // Debug card simulation uses the real Camp EventFlow resolver, but the
    // selected card is not physically drawn from or disposed into the Camp
    // deck merely because it was launched from Debug.
    private bool _debugCampSimulation;
    private GamePhase _debugReturnPhase;
    private CampStep _debugReturnCampStep;
    private MobilizationStep _debugReturnMobilizationStep;

    private bool _endOfSeasonPending;
    private GamePhase _endOfSeasonTriggerPhase = GamePhase.Mobilization;
    private string? _endOfSeasonName;
    private readonly Dictionary<string, int> _harvestStaging = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _harvestPacked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _harvestPurged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _harvestSold = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HarvestUndoAction> _harvestPreparationUndo = [];
    private readonly List<HarvestUndoAction> _harvestPackingUndo = [];
    private int _harvestMarketSalesRemaining;
    private readonly List<int> _winterAttritionRolls = [];
    private readonly List<string> _pendingWinterAttritionLosses = [];
    private readonly List<RefitUndoSnapshot> _refitUndo = [];

    public GameSession(ILogger<GameSession> logger)
    {
        _logger = logger;
        ResetNewGameState();
    }

    public int Year { get; private set; } = 1;
    public string Season { get; private set; } = "Spring";
    public int Time { get; private set; }

    public int Morale { get; private set; } = 5;
    public int Leadership { get; private set; } = 3;

    public GamePhase Phase { get; private set; } = GamePhase.Mobilization;
    public MobilizationStep MobilizationStep { get; private set; } = MobilizationStep.ExploreReady;
    public TerrainType CurrentExploreTerrain { get; private set; } = TerrainType.None;
    public List<TerrainType> PendingTerrainChoices { get; } = [];
    public ExploreCardState? PendingExploreCard { get; private set; }
    public ArrivalCardState? PendingArrivalCard { get; private set; }
    public CampCardState? PendingCampCard { get; private set; }
    public CampStep CampStep { get; private set; } = CampStep.Ready;
    public EndOfSeasonStep EndOfSeasonStep { get; private set; } = EndOfSeasonStep.Ready;
    public WinterStep WinterStep { get; private set; } = WinterStep.Ready;
    public List<CampCardState> CampHand { get; } = [];
    public FlowPromptState? FlowPrompt
    {
        get => _flowPrompt;
        private set
        {
            _flowPrompt = value;
            if (value is null)
            {
                _activeChronicleDecisionIndex = null;
                return;
            }

            var choices = value.Options
                .Select(o => new ChronicleChoice(o.Id, o.Label, o.Detail, o.Disabled))
                .ToArray();
            Chronicle.Add(new(
                CalendarStamp,
                value.Prompt,
                ChronicleTone.Narrative,
                value.Heading,
                choices));
            _activeChronicleDecisionIndex = Chronicle.Count - 1;
        }
    }
    public bool ArrivalCardRevealed => _arrivalCardRevealed;
    public bool HasPriest => _hasPriest;
    public string? PriestSource => _priestSource;

    public List<ResourceState> Resources { get; } = [];
    public List<MarketState> Market { get; } = [];
    public List<HostUnit> Host { get; } = [];
    public List<AdvancementOfferState> AdvancementOffer { get; } = [];
    public List<AdvancementOfferState> AcquiredAdvancements { get; } = [];
    public List<AvailableForceState> AvailableForces { get; } = [];
    public List<PersistentState> PersistentItems { get; } = [];
    public HashSet<string> ActiveTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BaggageSlot> Baggage { get; } = [];
    public List<TribeState> Tribes { get; } = [];
    public List<PlayAreaElement> PlayArea { get; } = [];
    public List<ChronicleEntry> Chronicle { get; } = [];
    public List<CompletedBattleLog> BattleHistory { get; } = [];
    public Guid GameLogId { get; private set; }
    public DateTime GameStartedUtc { get; private set; }
    public DateTime? GameEndedUtc { get; private set; }
    public IReadOnlyCollection<string> EnabledContentSets => _enabledContentSets;

    public bool EndOfSeasonPending => _endOfSeasonPending;
    public string EndOfSeasonName => _endOfSeasonName ?? Season;
    public IReadOnlyDictionary<string, int> HarvestStaging => _harvestStaging;
    public int HarvestMarketSalesRemaining => _harvestMarketSalesRemaining;
    public bool CanUndoHarvestPreparation => Phase == GamePhase.EndOfSeason
        && EndOfSeasonStep == EndOfSeasonStep.HarvestPreparing
        && _harvestPreparationUndo.Count > 0;
    public bool CanUndoHarvestPacking => Phase == GamePhase.EndOfSeason
        && EndOfSeasonStep == EndOfSeasonStep.HarvestPacking
        && _harvestPackingUndo.Count > 0;
    public bool CanUndoRefitReform => IsRefitReformActive() && _refitUndo.Count > 0;
    public bool HasCampSteward => HasToken("CampSteward");

    public IReadOnlyList<string> VisibleReminders => ActiveTokens
        .Select(DisplayTokenName)
        .OrderBy(x => x)
        .ToArray();

    public event Action? Changed;

    public void InitializeCatalogState(WcDataCatalog catalog, IEnumerable<string>? enabledSets = null)
    {
        if (_catalogInitialized)
            return;

        _catalogInitialized = true;
        _catalog = catalog;
        _enabledContentSets.Clear();
        _enabledContentSets.Add("Base");
        foreach (var set in enabledSets ?? Enumerable.Empty<string>())
        {
            if (string.Equals(set, "SitA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(set, "SiaSL", StringComparison.OrdinalIgnoreCase))
                _enabledContentSets.Add(set);
        }

        var advancements = catalog.GetBaseAdvancements();
        _armorSupply.AddRange(advancements.Where(a => a.Id is "ADV-001" or "ADV-002" or "ADV-003"));
        _advancementDeck.AddRange(advancements.Where(a => a.Id is not "ADV-001" and not "ADV-002" and not "ADV-003"));
        Shuffle(_advancementDeck);
        RefreshAdvancementOffer();

        _exploreDeck.AddRange(catalog.GetExploreCards(_enabledContentSets));
        Shuffle(_exploreDeck);

        _arrivalDeck.AddRange(catalog.GetArrivalCards());
        Shuffle(_arrivalDeck);

        InitializeCampDeck(catalog);
        Changed?.Invoke();
    }

    private void InitializeCampDeck(WcDataCatalog catalog)
    {
        _campDeck.Clear();
        _campDiscard.Clear();
        _unseededScenes.Clear();
        _unseededEchoes.Clear();
        _removedCampCards.Clear();
        CampHand.Clear();

        var cards = catalog.GetCampCards(_enabledContentSets);
        _workCard = cards.FirstOrDefault(c => c.Id == "CAMP-001");

        var standard = cards.Where(c => c.CardType == "Camp" && c.Id != "CAMP-001").ToList();
        var scenes = cards.Where(c => c.CardType == "Scene").ToList();
        Shuffle(scenes);
        var seededScenes = scenes.Take(Math.Min(2, scenes.Count)).ToList();
        _unseededScenes.AddRange(scenes.Skip(seededScenes.Count));

        var echoes = cards.Where(c => c.CardType == "Echo").ToList();
        Shuffle(echoes);
        _unseededEchoes.AddRange(echoes);

        _campDeck.AddRange(standard);
        _campDeck.AddRange(seededScenes);
        Shuffle(_campDeck);

        // Setup burns 2 cards face down to the Camp discard pile.
        for (var i = 0; i < 2 && _campDeck.Count > 0; i++)
        {
            _campDiscard.Add(_campDeck[0]);
            _campDeck.RemoveAt(0);
        }

        _campInitialized = true;
        _logger.LogInformation("Camp deck initialized: {Draw} draw, {Discard} burned, {Scenes} unseeded Scenes, {Echoes} unseeded Echoes.",
            _campDeck.Count, _campDiscard.Count, _unseededScenes.Count, _unseededEchoes.Count);
    }

    public bool CanSimulateCampCard => _catalog is not null
        && !_debugCampSimulation
        && _activeFlow is null
        && FlowPrompt is null
        && Combat is null
        && Finale is null
        && Phase != GamePhase.GameOver;

    public void SimulateCampCard(string cardId)
    {
        if (!CanSimulateCampCard || string.IsNullOrWhiteSpace(cardId) || _catalog is null)
            return;

        var card = _catalog.GetCampCard(cardId, includeOptional: true);
        if (card is null || card.CardType is not ("Scene" or "Echo"))
            return;

        _debugCampSimulation = true;
        _debugReturnPhase = Phase;
        _debugReturnCampStep = CampStep;
        _debugReturnMobilizationStep = MobilizationStep;
        _campPhaseEnding = false;

        StartCampCard(card, special: true);

        // If the simulated card's Time advance itself triggers the Finale, the
        // normal Camp resolver intentionally stops immediately. Do not leave
        // Debug simulation state hanging behind that transition.
        if (Phase == GamePhase.Finale && _debugCampSimulation)
        {
            _debugCampSimulation = false;
            PendingCampCard = null;
            _activeChronicleEntryIndex = null;
        }

        Changed?.Invoke();
    }

    public void BeginCamp()
    {
        if (Phase != GamePhase.Camp || CampStep != CampStep.Ready || !_campInitialized)
            return;

        CampHand.Clear();
        PendingCampCard = null;
        FlowPrompt = null;
        _campPhaseEnding = false;

        for (var i = 0; i < 3; i++)
        {
            var card = DrawCampCard();
            if (card is not null)
                CampHand.Add(card);
        }

        // A Hostile Echo interrupts the whole hand. The first one drawn is
        // resolved and everything else drawn this Camp is discarded.
        var hostileEcho = CampHand.FirstOrDefault(IsHostileEcho);
        if (hostileEcho is not null)
        {
            foreach (var other in CampHand.Where(c => c.Id != hostileEcho.Id).ToArray())
                _campDiscard.Add(other);
            CampHand.Clear();
            CampHand.Add(hostileEcho);
            CampStep = CampStep.ResolvingSpecial;
            StartCampCard(hostileEcho, special: true);
        }
        else
        {
            ResolveNextCampSpecialOrOffer();
        }

        Changed?.Invoke();
    }

    public IEnumerable<CampCardState> CampActionChoices
    {
        get
        {
            if (CampStep != CampStep.ChoosingAction)
                return [];
            var choices = CampHand.Where(c => c.CardType == "Camp").ToList();
            if (_workCard is not null)
                choices.Add(_workCard);
            return choices;
        }
    }

    public bool CanChooseCampCard(CampCardState card)
    {
        if (CampStep != CampStep.ChoosingAction)
            return false;
        if (!CampLocationAllowed(card))
            return false;
        if (!CanPayFixedCampCost(card))
            return false;
        if (card.Id == "CAMP-011" && !Tribes.Any(t => t.Rapport == "Hostile"))
            return false;
        if (card.Id == "CAMP-013" && !Tribes.Any(t => t.Rapport is "Neutral" or "Friendly"))
            return false;
        if (card.Id == "CAMP-012" && HasScouting && HasToken("CampSteward"))
            return false;
        if (card.Id == "CAMP-003" && !((!HasBuilding("Chapel") && CanPayCostBundle("1W+1C")) || (!HasBuilding("Academy") && CanPayCostBundle("1S+1C"))))
            return false;
        if (card.Id == "CAMP-004" && !((!HasBuilding("Market") && CanPayCostBundle("1W+1C")) || (!HasBuilding("Baggage Cart") && CanPayCostBundle("1W"))))
            return false;
        if (card.Id == "CAMP-012" && !((!HasScouting && ResourceValue("Research") >= 1) || (!HasToken("CampSteward") && ResourceValue("Coin") >= 2)))
            return false;
        return true;
    }

    public string CampChoiceAvailability(CampCardState card)
    {
        if (!CampLocationAllowed(card))
            return "Not available at this location.";
        if (!CanPayFixedCampCost(card))
            return "You cannot pay the printed cost.";
        if (card.Id == "CAMP-011" && !Tribes.Any(t => t.Rapport == "Hostile"))
            return "No Hostile tribe is available.";
        if (card.Id == "CAMP-013" && !Tribes.Any(t => t.Rapport is "Neutral" or "Friendly"))
            return "No Neutral or Friendly tribe is available.";
        if (card.Id == "CAMP-012" && HasScouting && HasToken("CampSteward"))
            return "Both Field Staff roles are already in place.";
        if (card.Id == "CAMP-003" && !((!HasBuilding("Chapel") && CanPayCostBundle("1W+1C")) || (!HasBuilding("Academy") && CanPayCostBundle("1S+1C"))))
            return "No unbuilt option can be afforded.";
        if (card.Id == "CAMP-004" && !((!HasBuilding("Market") && CanPayCostBundle("1W+1C")) || (!HasBuilding("Baggage Cart") && CanPayCostBundle("1W"))))
            return "No unbuilt option can be afforded.";
        if (card.Id == "CAMP-012" && !((!HasScouting && ResourceValue("Research") >= 1) || (!HasToken("CampSteward") && ResourceValue("Coin") >= 2)))
            return "No available role can be afforded.";
        return string.Empty;
    }

    public void ChooseCampCard(string cardId)
    {
        if (CampStep != CampStep.ChoosingAction)
            return;

        var card = CampActionChoices.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase));
        if (card is null || !CanChooseCampCard(card))
            return;

        AddNarrativeText($"The Host turns its attention to {card.Title}.");
        CampStep = CampStep.ResolvingAction;
        StartCampCard(card, special: false);
        Changed?.Invoke();
    }

    private CampCardState? DrawCampCard()
    {
        if (_campDeck.Count == 0)
            RebuildCampDeckFromDiscard();
        if (_campDeck.Count == 0)
            return null;

        var card = _campDeck[0];
        _campDeck.RemoveAt(0);
        return card;
    }

    private void RebuildCampDeckFromDiscard()
    {
        if (_campDiscard.Count == 0)
            return;

        _campDeck.AddRange(_campDiscard);
        _campDiscard.Clear();
        Shuffle(_campDeck);

        // Each time the Camp deck is rebuilt, burn 2 cards to the new discard.
        for (var i = 0; i < 2 && _campDeck.Count > 0; i++)
        {
            _campDiscard.Add(_campDeck[0]);
            _campDeck.RemoveAt(0);
        }
        _logger.LogInformation("Camp draw deck rebuilt and 2 cards burned.");
    }

    private void ResolveNextCampSpecialOrOffer()
    {
        var special = CampHand.FirstOrDefault(c => c.CardType is "Scene" or "Echo");
        if (special is not null)
        {
            CampStep = CampStep.ResolvingSpecial;
            StartCampCard(special, special: true);
            return;
        }

        CampStep = CampStep.ChoosingAction;
        PendingCampCard = null;
        _activeChronicleEntryIndex = null;
    }

    private void StartCampCard(CampCardState card, bool special)
    {
        PendingCampCard = card;
        _campCardResolvedSuccessfully = true;

        var echoOrigin = string.Equals(card.CardType, "Echo", StringComparison.OrdinalIgnoreCase)
            && _echoOrigins.TryGetValue(card.Id, out var recordedOrigin)
            && !string.IsNullOrWhiteSpace(recordedOrigin)
                ? recordedOrigin
                : null;
        var chronicleStamp = card.CardType switch
        {
            "Scene" => $"{CalendarStamp} • Scene",
            "Echo" when echoOrigin is not null => $"{CalendarStamp} • Echo • Seeded by: {echoOrigin}",
            "Echo" => $"{CalendarStamp} • Echo",
            _ => CalendarPhaseStamp("Camp")
        };

        Chronicle.Add(new(chronicleStamp, card.Flavor, ChronicleTone.Narrative, card.Title));
        _activeChronicleEntryIndex = Chronicle.Count - 1;

        if (card.Time > 0)
        {
            AdvanceTime(card.Time);
            if (Phase == GamePhase.Finale)
            {
                Changed?.Invoke();
                return;
            }
        }

        AddCampPrintedNarrative(card);

        if (!special && string.Equals(card.CostType, "Fixed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(card.CostValue))
        {
            if (!SpendCostBundle(card.CostValue!, narrate: true))
            {
                _campCardResolvedSuccessfully = false;
                AddNarrativeText("The Host cannot meet the cost and abandons the effort.");
                CompleteCampCard();
                return;
            }
        }

        StartCampFlow(card);
    }

    private void AddCampPrintedNarrative(CampCardState card)
    {
        // Card prose that matters to the player's story belongs in the Chronicle.
        // Hidden deck plumbing, such as Echo seeding, stays internal.
        if (card.Id == "SCENE-019")
        {
            AddNarrativeText("When the Host stops nearby, a sudden uproar breaks out: silver is missing, and one of your men is being named as the thief. The charge cannot be proven, but the Host can feel how badly the locals want someone to answer for it.");
            AddNarrativeText("You leave the matter unsettled and march on rather than lose time to finding the truth.");
        }
    }

    private void StartCampFlow(CampCardState card)
    {
        if (_catalog is null)
        {
            CompleteCampCard();
            return;
        }

        var flow = _catalog.GetFlowForSource(card.Id, "OnResolve");
        if (flow is null)
        {
            AddNarrativeText("Nothing further comes of it.");
            CompleteCampCard();
            return;
        }

        _activeFlow = flow.Value;
        _activeNodeId = GetString(flow.Value, "start_node") ?? "N01";
        _activeFlowContext = FlowContext.Camp;
        _flowVars.Clear();
        _lastRollResults.Clear();
        _lastUnitTypeRolls.Clear();
        _lastRollText = null;
        _pendingSelectedUnits.Clear();
        FlowPrompt = null;
        AdvanceFlow();
    }

    private void CompleteCampCard()
    {
        var card = PendingCampCard;
        var debugSimulation = _debugCampSimulation;
        var afterPlayOverridden = _flowVars.TryGetValue("Camp.AfterPlayOverridden", out var overridden) && overridden is true;
        ResetActiveFlowState();
        if (card is null)
        {
            _debugCampSimulation = false;
            return;
        }

        if (debugSimulation)
        {
            // Debug launches a card through its real EventFlow, but does not
            // pretend the card was drawn from the Camp deck. Card effects such
            // as Echo seeding still happen because they are part of the flow.
            PendingCampCard = null;
            _activeChronicleEntryIndex = null;
            _campPhaseEnding = false;
            _debugCampSimulation = false;

            if (Phase != GamePhase.GameOver && Phase != GamePhase.Finale)
            {
                Phase = _debugReturnPhase;
                CampStep = _debugReturnCampStep;
                MobilizationStep = _debugReturnMobilizationStep;
            }
            return;
        }

        if (!afterPlayOverridden
            && string.Equals(card.AfterPlay, "RemoveOrDiscard", StringComparison.OrdinalIgnoreCase))
        {
            PresentCampAfterPlayDispositionPrompt(card);
            return;
        }

        FinalizeCampCard(card, afterPlayOverridden);
    }

    private void PresentCampAfterPlayDispositionPrompt(CampCardState card)
    {
        _specialPrompt = "CampAfterPlayDisposition";
        FlowPrompt = new(
            FlowPromptKind.Choice,
            card.Title,
            "Choose what to do with this card after play.",
            [
                new("camp-disposition:discard", "Discard", "Return this card to the Camp discard pile."),
                new("camp-disposition:remove", "Remove from game", "This card will not return this game.")
            ]);
    }

    private void ResolveCampAfterPlayDispositionPrompt(string optionId)
    {
        var card = PendingCampCard;
        if (card is null)
            return;

        if (string.Equals(optionId, "camp-disposition:discard", StringComparison.OrdinalIgnoreCase))
        {
            _campDiscard.Add(card);
            AddNarrativeText($"{card.Title} is discarded.");
        }
        else if (string.Equals(optionId, "camp-disposition:remove", StringComparison.OrdinalIgnoreCase))
        {
            _removedCampCards.Add(card);
            AddNarrativeText($"{card.Title} is removed from the game.");
        }
        else
        {
            return;
        }

        _specialPrompt = null;
        FlowPrompt = null;
        FinalizeCampCard(card, afterPlayAlreadyHandled: true);
        Changed?.Invoke();
    }

    private void FinalizeCampCard(CampCardState card, bool afterPlayAlreadyHandled)
    {
        CampHand.RemoveAll(c => c.Id == card.Id);
        if (!afterPlayAlreadyHandled)
            ApplyCampAfterPlay(card);
        PendingCampCard = null;
        _activeChronicleEntryIndex = null;

        if (_combatDisengagedPending)
        {
            _combatDisengagedPending = false;
            foreach (var remaining in CampHand.ToArray())
                _campDiscard.Add(remaining);
            CampHand.Clear();
            BeginForcedMobilization();
            return;
        }

        if (_campPhaseEnding || IsHostileEcho(card))
        {
            EndCampPhase();
            return;
        }

        if (CampStep == CampStep.ResolvingSpecial)
        {
            ResolveNextCampSpecialOrOffer();
            return;
        }

        // A normal Camp choice ends the phase. Unchosen drawn Camp cards go
        // to the discard pile; Work is never discarded because it is separate.
        foreach (var remaining in CampHand.ToArray())
            _campDiscard.Add(remaining);
        CampHand.Clear();
        EndCampPhase();
    }

    private void ApplyCampAfterPlay(CampCardState card)
    {
        if (_flowVars.TryGetValue("Camp.AfterPlayOverridden", out var overridden) && overridden is true)
            return;
        var disposition = card.AfterPlay ?? string.Empty;
        if (string.Equals(disposition, "Keep", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.Equals(disposition, "Remove", StringComparison.OrdinalIgnoreCase))
        {
            _removedCampCards.Add(card);
            return;
        }
        if (string.Equals(disposition, "Discard", StringComparison.OrdinalIgnoreCase))
        {
            _campDiscard.Add(card);
            return;
        }
        if (string.Equals(disposition, "RemoveOrDiscard", StringComparison.OrdinalIgnoreCase))
        {
            // Normal Camp resolution must present the explicit player choice
            // before this method is reached. Keep this branch side-effect free
            // so Remove-or-Discard can never be silently decided by success/failure.
            _logger.LogWarning("RemoveOrDiscard reached ApplyCampAfterPlay without a player disposition for {Card}.", card.Title);
            return;
        }
        if (string.Equals(disposition, "Special", StringComparison.OrdinalIgnoreCase))
        {
            // Special Echo flows explicitly issue Remove/Discard/Return actions.
            // If the data did not override it, discard as the safest persistent default.
            if (!_removedCampCards.Any(c => c.Id == card.Id) && !_campDiscard.Any(c => c.Id == card.Id) && !_unseededEchoes.Any(c => c.Id == card.Id))
                _campDiscard.Add(card);
        }
    }

    private void EndCampPhase()
    {
        foreach (var remaining in CampHand.ToArray())
            _campDiscard.Add(remaining);
        CampHand.Clear();
        PendingCampCard = null;
        FlowPrompt = null;
        _specialPrompt = null;
        _campPhaseEnding = false;
        CampStep = CampStep.Complete;
        _activeChronicleEntryIndex = null;

        if (_endOfSeasonPending)
        {
            EnterEndOfSeason();
            return;
        }

        Phase = GamePhase.Mobilization;
        MobilizationStep = MobilizationStep.ExploreReady;
    }

    private void EnterEndOfSeason()
    {
        if (!_endOfSeasonPending)
            return;

        if (Year >= 3 && string.Equals(EndOfSeasonName, "Autumn", StringComparison.OrdinalIgnoreCase))
        {
            EnterFinale();
            return;
        }

        Phase = GamePhase.EndOfSeason;
        EndOfSeasonStep = EndOfSeasonStep.Ready;
        FlowPrompt = null;
        _specialPrompt = null;
        _harvestStaging.Clear();
        _harvestPacked.Clear();
        _harvestPurged.Clear();
        _harvestSold.Clear();
        _harvestPreparationUndo.Clear();
        _harvestPackingUndo.Clear();
        _harvestMarketSalesRemaining = 0;

        Chronicle.Add(new(
            CalendarStamp,
            $"The {EndOfSeasonName.ToLowerInvariant()} campaign draws to a close. The Host pauses to reckon with the season behind it.",
            ChronicleTone.Narrative,
            $"End of {EndOfSeasonName}"));
        _activeChronicleEntryIndex = Chronicle.Count - 1;
    }

    public void BeginEndOfSeason()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.Ready)
            return;

        EndOfSeasonStep = EndOfSeasonStep.VulnerabilityReady;
        AddNarrativeText("Before the Host can settle its stores, the wider world shifts around it.");
        Changed?.Invoke();
    }

    public void ResolveEndOfSeasonVulnerability()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.VulnerabilityReady)
            return;

        ResolveMarketVulnerability();

        // The Barbarian Activation die has a Barbarian icon on one-third of
        // its faces. Raw die plumbing stays in the log; the Chronicle records
        // only whether the world actually reacts.
        var barbarianIcon = Random.Shared.Next(3) == 0;
        _logger.LogInformation("End of Season Barbarian Activation die: {Result}.", barbarianIcon ? "Barbarian icon" : "No icon");
        AddSystemText($"Barbarian Activation die: {(barbarianIcon ? "Barbarian icon" : "No icon")}.");
        ResolveBarbarianRaid(barbarianIcon);

        EndOfSeasonStep = EndOfSeasonStep.HarvestReady;
        Changed?.Invoke();
    }

    private void ResolveMarketVulnerability()
    {
        var resources = new[] { "Food", "Wood", "Stone" };
        var resource = resources[Random.Shared.Next(resources.Length)];
        var direction = Random.Shared.Next(2) == 0 ? -1 : 1;
        var index = Market.FindIndex(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        var current = Market[index];
        var nextBuy = Math.Clamp(current.BuyPrice + direction, 2, 4);
        Market[index] = current with { BuyPrice = nextBuy, SellPrice = nextBuy - 2 };

        _logger.LogInformation("Market Vulnerability: {Resource} {Direction}; {Old}->{New}.", resource, direction > 0 ? "+" : "-", current.BuyPrice, nextBuy);
        AddSystemText($"Market Vulnerability die: {resource} {(direction > 0 ? "+" : "-")}.");
        if (nextBuy == current.BuyPrice)
        {
            AddNarrativeText($"The Market presses against its limit, but {resource} remains at {nextBuy} Coin to buy.");
        }
        else
        {
            var verb = nextBuy > current.BuyPrice ? "rises" : "falls";
            AddNarrativeText($"The Market shifts. {resource} {verb} from {current.BuyPrice} to {nextBuy} Coin to buy.");
        }
    }

    private void ResolveBarbarianRaid(bool barbarianIcon)
    {
        if (!barbarianIcon)
        {
            AddNarrativeText("The tribes remain quiet. No raid follows the seasonal reckoning.");
            return;
        }

        var hostile = Tribes
            .Where(t => string.Equals(t.Rapport, "Hostile", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rearMostPlayerClearing = PlayArea.FindIndex(p =>
            p.Kind == PlayAreaKind.Clearing
            && string.Equals(p.Controller, "Player", StringComparison.OrdinalIgnoreCase));

        if (hostile.Count == 0 || rearMostPlayerClearing < 0)
        {
            AddNarrativeText(hostile.Count == 0
                ? "There are signs of movement beyond the road, but no Hostile tribe is able to raid the Host's holdings."
                : "A Hostile tribe stirs, but there is no Player Controlled Settlement for it to seize.");
            return;
        }

        var raider = hostile[Random.Shared.Next(hostile.Count)].Name;
        var clearing = PlayArea[rearMostPlayerClearing];
        PlayArea[rearMostPlayerClearing] = clearing with
        {
            Controller = raider,
            IsHostile = true
        };
        AddNarrativeText($"{raider} raiders strike the rear-most controlled settlement and seize it for themselves.");
    }

    public void BeginEndOfSeasonHarvest()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.HarvestReady)
            return;

        _harvestStaging.Clear();
        _harvestPacked.Clear();
        _harvestPurged.Clear();
        _harvestSold.Clear();
        _harvestPreparationUndo.Clear();
        _harvestPackingUndo.Clear();
        _harvestMarketSalesRemaining = HasBuilding("Market") ? 2 : 0;

        var harvestedTerrain = 0;
        var blockedTerrain = 0;
        var recoveredTerrain = 0;

        for (var i = 0; i < PlayArea.Count; i++)
        {
            var element = PlayArea[i];
            if (element.Kind != PlayAreaKind.Terrain)
                continue;

            if (element.IsDepleted)
            {
                PlayArea[i] = element with { IsDepleted = false };
                recoveredTerrain++;
                continue;
            }

            if (TerrainBlockedByHostile(i))
            {
                blockedTerrain++;
                continue;
            }

            var resource = element.Terrain switch
            {
                TerrainType.Plains => "Food",
                TerrainType.Forest => "Wood",
                TerrainType.Mountain => "Stone",
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(resource))
                continue;

            AddStagedHarvest(resource, 1);
            harvestedTerrain++;

            if (element.BonusFood > 0)
            {
                AddStagedHarvest("Food", element.BonusFood);
                PlayArea[i] = PlayArea[i] with { BonusFood = 0 };
            }
        }

        var yieldText = FormatResourceCounts(_harvestStaging);
        AddNarrativeText(_harvestStaging.Values.Sum() > 0
            ? $"Harvest brings in {yieldText}. The new supplies are staged beside the Baggage Train before anything is packed away."
            : "This Harvest produces no usable supplies.");

        if (blockedTerrain > 0)
            AddNarrativeText($"{blockedTerrain} Terrain {(blockedTerrain == 1 ? "is" : "are")} cut off by Hostile settlements and produce nothing.");
        if (recoveredTerrain > 0)
            AddNarrativeText($"{recoveredTerrain} depleted Terrain {(recoveredTerrain == 1 ? "recovers" : "recover")} instead of producing this season.");

        EndOfSeasonStep = EndOfSeasonStep.HarvestPreparing;
        Changed?.Invoke();
    }

    private bool TerrainBlockedByHostile(int index)
    {
        var hostileBefore = index > 0
            && PlayArea[index - 1].Kind == PlayAreaKind.Clearing
            && PlayArea[index - 1].IsHostile;
        var hostileAfter = index < PlayArea.Count - 1
            && PlayArea[index + 1].Kind == PlayAreaKind.Clearing
            && PlayArea[index + 1].IsHostile;
        return hostileBefore || hostileAfter;
    }

    private void AddStagedHarvest(string resource, int amount)
    {
        if (amount <= 0)
            return;
        _harvestStaging[resource] = _harvestStaging.TryGetValue(resource, out var current) ? current + amount : amount;
    }

    public bool CanPurgeHarvestBaggage(int position)
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.HarvestPreparing)
            return false;

        var slot = Baggage.FirstOrDefault(b => b.Position == position);
        if (slot is null || string.IsNullOrWhiteSpace(slot.Contents))
            return false;

        // Harvest normally allows the player to clear carried Resources before
        // repacking. Firepots also physically occupy a Baggage Train space, so
        // the player may voluntarily dump them here to free that space.
        return IsPhysicalResource(slot.Contents!)
            || string.Equals(slot.Contents, "Firepots", StringComparison.OrdinalIgnoreCase);
    }

    public void PurgeHarvestBaggage(int position)
    {
        if (!CanPurgeHarvestBaggage(position))
            return;

        var slot = Baggage.First(b => b.Position == position);
        var item = slot.Contents!;
        var removed = IsPhysicalResource(item)
            ? RemoveOneResourceFromBaggageSlot(position, item)
            : RemoveHarvestBaggageToken(position, item);

        if (removed)
        {
            IncrementCount(_harvestPurged, item, 1);
            _harvestPreparationUndo.Add(new(HarvestUndoKind.Purge, position, item));
        }
        Changed?.Invoke();
    }

    private bool RemoveHarvestBaggageToken(int position, string token)
    {
        if (!string.Equals(token, "Firepots", StringComparison.OrdinalIgnoreCase))
            return false;

        var index = Baggage.FindIndex(b => b.Position == position
            && string.Equals(b.Contents, token, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        Baggage[index] = Baggage[index] with { Contents = null, Quantity = 1 };
        ActiveTokens.Remove(token);
        return true;
    }

    public bool CanSellHarvestBaggage(int position)
    {
        if (!HasBuilding("Market") || _harvestMarketSalesRemaining <= 0 || !CanPurgeHarvestBaggage(position))
            return false;
        var slot = Baggage.First(b => b.Position == position);
        return Market.Any(m => string.Equals(m.Resource, slot.Contents, StringComparison.OrdinalIgnoreCase));
    }

    public void SellHarvestBaggage(int position)
    {
        if (!CanSellHarvestBaggage(position))
            return;
        var slot = Baggage.First(b => b.Position == position);
        var resource = slot.Contents!;
        var market = Market.First(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        if (!RemoveOneResourceFromBaggageSlot(position, resource))
            return;
        AdjustResource("Coin", market.SellPrice);
        _harvestMarketSalesRemaining--;
        IncrementCount(_harvestSold, resource, 1);
        _harvestPreparationUndo.Add(new(HarvestUndoKind.Sell, position, resource, market.SellPrice));
        Changed?.Invoke();
    }

    private bool RemoveOneResourceFromBaggageSlot(int position, string resource)
    {
        var index = Baggage.FindIndex(b => b.Position == position && string.Equals(b.Contents, resource, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        if (string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase) && Baggage[index].Quantity > 1)
            Baggage[index] = Baggage[index] with { Quantity = Baggage[index].Quantity - 1 };
        else
            Baggage[index] = Baggage[index] with { Contents = null, Quantity = 1 };

        AdjustResource(resource, -1);
        return true;
    }

    public void BeginEndOfSeasonPacking()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.HarvestPreparing)
            return;

        if (_harvestSold.Values.Sum() > 0)
            AddNarrativeText($"The Market buys {FormatResourceCounts(_harvestSold)} from the Host's carried stores.");
        var purgedResources = _harvestPurged
            .Where(kv => IsPhysicalResource(kv.Key))
            .ToArray();
        if (purgedResources.Sum(kv => kv.Value) > 0)
            AddNarrativeText($"The Host leaves {FormatResourceCounts(purgedResources)} behind to make room in the Baggage Train.");
        if (_harvestPurged.TryGetValue("Firepots", out var firepotsPurged) && firepotsPurged > 0)
            AddNarrativeText("The Host dumps the Firepots to make room in the Baggage Train.");

        _harvestPreparationUndo.Clear();
        _harvestPackingUndo.Clear();
        EndOfSeasonStep = EndOfSeasonStep.HarvestPacking;
        Changed?.Invoke();
    }

    public bool CanPackHarvestResource(string resource)
    {
        return Phase == GamePhase.EndOfSeason
            && EndOfSeasonStep == EndOfSeasonStep.HarvestPacking
            && _harvestStaging.TryGetValue(resource, out var amount)
            && amount > 0
            && CanAddPhysicalResource(resource, 1);
    }

    public void PackHarvestResource(string resource)
    {
        if (!CanPackHarvestResource(resource))
            return;

        var before = Baggage.ToDictionary(b => b.Position, b => (b.Contents, b.Quantity));
        if (AddPhysicalResource(resource, 1) != 1)
            return;

        var changedSlot = Baggage.FirstOrDefault(slot =>
            before.TryGetValue(slot.Position, out var prior)
            && (!string.Equals(prior.Contents, slot.Contents, StringComparison.OrdinalIgnoreCase) || prior.Quantity != slot.Quantity));

        _harvestStaging[resource]--;
        IncrementCount(_harvestPacked, resource, 1);
        if (changedSlot is not null)
            _harvestPackingUndo.Add(new(HarvestUndoKind.Pack, changedSlot.Position, resource));
        Changed?.Invoke();
    }

    public void UndoHarvestPreparation()
    {
        if (!CanUndoHarvestPreparation)
            return;

        var action = _harvestPreparationUndo[^1];
        if (!RestoreHarvestBaggageItem(action.Position, action.Resource))
            return;

        _harvestPreparationUndo.RemoveAt(_harvestPreparationUndo.Count - 1);
        if (action.Kind == HarvestUndoKind.Sell)
        {
            AdjustResource("Coin", -action.CoinAmount);
            _harvestMarketSalesRemaining++;
            DecrementCount(_harvestSold, action.Resource, 1);
        }
        else
        {
            DecrementCount(_harvestPurged, action.Resource, 1);
        }

        Changed?.Invoke();
    }

    public void UndoHarvestPacking()
    {
        if (!CanUndoHarvestPacking)
            return;

        var action = _harvestPackingUndo[^1];
        if (!RemoveOneResourceFromBaggageSlot(action.Position, action.Resource))
            return;

        _harvestPackingUndo.RemoveAt(_harvestPackingUndo.Count - 1);
        _harvestStaging[action.Resource] = _harvestStaging.TryGetValue(action.Resource, out var staged) ? staged + 1 : 1;
        DecrementCount(_harvestPacked, action.Resource, 1);
        Changed?.Invoke();
    }

    private bool RestoreHarvestBaggageItem(int position, string item)
    {
        if (IsPhysicalResource(item))
            return RestoreOneResourceToBaggageSlot(position, item);

        if (!string.Equals(item, "Firepots", StringComparison.OrdinalIgnoreCase))
            return false;

        var index = Baggage.FindIndex(b => b.Position == position);
        if (index < 0 || Baggage[index].Damaged || !string.IsNullOrWhiteSpace(Baggage[index].Contents))
            return false;

        Baggage[index] = Baggage[index] with { Contents = "Firepots", Quantity = 1 };
        ActiveTokens.Add("Firepots");
        return true;
    }

    private bool RestoreOneResourceToBaggageSlot(int position, string resource)
    {
        var index = Baggage.FindIndex(b => b.Position == position);
        if (index < 0 || Baggage[index].Damaged)
            return false;

        var slot = Baggage[index];
        if (string.IsNullOrWhiteSpace(slot.Contents))
        {
            Baggage[index] = slot with { Contents = resource, Quantity = 1 };
        }
        else if (string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase)
            && string.Equals(slot.Contents, "Food", StringComparison.OrdinalIgnoreCase)
            && slot.Quantity < 2)
        {
            Baggage[index] = slot with { Quantity = slot.Quantity + 1 };
        }
        else
        {
            return false;
        }

        AdjustResource(resource, 1);
        return true;
    }

    private static void DecrementCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (!counts.TryGetValue(key, out var current))
            return;
        var next = current - amount;
        if (next <= 0)
            counts.Remove(key);
        else
            counts[key] = next;
    }

    public void FinishEndOfSeasonHarvest()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.HarvestPacking)
            return;

        if (_harvestPacked.Values.Sum() > 0)
        {
            var packed = FormatResourceCounts(_harvestPacked);
            AddNarrativeText($"The Host packs {packed} from the new Harvest.");
        }

        var leftovers = _harvestStaging.Values.Sum();
        if (leftovers > 0)
            AddNarrativeText($"The remaining {FormatResourceCounts(_harvestStaging)} cannot be carried and are left behind.");

        _harvestStaging.Clear();
        _harvestPackingUndo.Clear();
        EndOfSeasonStep = EndOfSeasonStep.ConsolidationReady;
        Changed?.Invoke();
    }

    public void ResolveEndOfSeasonConsolidation()
    {
        if (Phase != GamePhase.EndOfSeason || EndOfSeasonStep != EndOfSeasonStep.ConsolidationReady)
            return;

        var beforeClearings = PlayArea.Count(p => p.Kind == PlayAreaKind.Clearing);
        var beforeTerrain = PlayArea.Count(p => p.Kind == PlayAreaKind.Terrain);
        ConsolidatePlayArea();
        var removedClearings = beforeClearings - PlayArea.Count(p => p.Kind == PlayAreaKind.Clearing);
        var removedTerrain = beforeTerrain - PlayArea.Count(p => p.Kind == PlayAreaKind.Terrain);

        if (removedClearings > 0 || removedTerrain > 0)
            AddNarrativeText($"The road behind the Host falls out of the Chronicle: {removedClearings} Clearing{(removedClearings == 1 ? string.Empty : "s")} and {removedTerrain} Terrain card{(removedTerrain == 1 ? string.Empty : "s")} leave the active route.");
        else
            AddNarrativeText("The explored route is already compact enough that no old ground is lost.");

        if (string.Equals(EndOfSeasonName, "Summer", StringComparison.OrdinalIgnoreCase))
        {
            _refitUndo.Clear();
            EndOfSeasonStep = EndOfSeasonStep.RefitReform;
            AddNarrativeText("With Summer ended, the Host has a chance to Refit & Reform before the march resumes.");
        }
        else
        {
            CompleteEndOfSeason();
        }
        Changed?.Invoke();
    }

    private void ConsolidatePlayArea()
    {
        var clearingIndices = PlayArea
            .Select((element, index) => (element, index))
            .Where(x => x.element.Kind == PlayAreaKind.Clearing)
            .Select(x => x.index)
            .ToList();
        var hostIndex = PlayArea.FindIndex(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        var hostClearingPosition = clearingIndices.IndexOf(hostIndex);
        if (hostClearingPosition < 0)
            return;

        var firstKeptPosition = Math.Max(0, hostClearingPosition - 2);
        var keptClearings = clearingIndices
            .Skip(firstKeptPosition)
            .Take(hostClearingPosition - firstKeptPosition + 1)
            .ToHashSet();

        var keep = new HashSet<int>(keptClearings);
        for (var i = 0; i < PlayArea.Count; i++)
        {
            if (PlayArea[i].Kind != PlayAreaKind.Terrain)
                continue;
            if ((i > 0 && keptClearings.Contains(i - 1))
                || (i < PlayArea.Count - 1 && keptClearings.Contains(i + 1)))
                keep.Add(i);
        }

        var rebuilt = PlayArea
            .Select((element, index) => (element, index))
            .Where(x => keep.Contains(x.index))
            .Select(x => x.element)
            .ToList();
        PlayArea.Clear();
        PlayArea.AddRange(rebuilt);
    }

    public void BeginWinter()
    {
        if (Phase != GamePhase.Winter || WinterStep != WinterStep.Ready)
            return;

        Chronicle.Add(new(
            CalendarStamp,
            "The campaigning year gives way to winter. The Host settles in, contracts expire, and the cold begins its own accounting.",
            ChronicleTone.Narrative,
            "Winter"));

        DisbandWinterMercenaries();
        WinterStep = WinterStep.Attrition;
        PresentWinterAttritionMethod();
        Changed?.Invoke();
    }

    private void DisbandWinterMercenaries()
    {
        var mercenaries = Host
            .Where(u => u.Count > 0 && (string.Equals(u.Id, "MER-INF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.Id, "MER-CAV", StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.Type, "Mercenaries", StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.Type, "Hedge Knights", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var total = mercenaries.Sum(u => u.Count);
        if (total <= 0)
            return;

        var details = mercenaries
            .Select(u => $"{u.Count} {SingularizeUnitLabel(u.Type, u.Count)}")
            .ToArray();
        Host.RemoveAll(u => string.Equals(u.Id, "MER-INF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Id, "MER-CAV", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Type, "Mercenaries", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Type, "Hedge Knights", StringComparison.OrdinalIgnoreCase));

        AddNarrativeText($"Winter ends the Host's mercenary contracts. {JoinNatural(details)} leave the Host.");
    }

    private void PresentWinterAttritionMethod()
    {
        _specialPrompt = "WinterAttritionMethod";
        var options = new List<FlowPromptOption>
        {
            new("winter-attrition:normal", "Roll 1d10", $"Lower is better. {Morale} or less: no losses. Above {Morale}: lose Units equal to the difference.")
        };
        if (Leadership > 0)
        {
            options.Add(new(
                "winter-attrition:leadership",
                "Commit 1 Leadership",
                $"Spend 1 Leadership and roll 2d10. Lower is better: choose one result; {Morale} or less causes no losses."));
        }

        FlowPrompt = new(
            FlowPromptKind.Choice,
            "Winter Attrition",
            $"Morale is {Morale}. This is a roll-under check: lower is better. A result of {Morale} or less causes no losses; above Morale loses Units equal to the difference.",
            options);
    }

    private void ResolveWinterAttritionMethodPrompt(string optionId)
    {
        if (Phase != GamePhase.Winter || WinterStep != WinterStep.Attrition)
            return;

        if (optionId == "winter-attrition:normal")
        {
            _specialPrompt = null;
            FlowPrompt = null;
            var result = Random.Shared.Next(1, 11);
            _winterAttritionRolls.Clear();
            _winterAttritionRolls.Add(result);
            AddSystemText($"Winter Attrition roll: {result} (Morale {Morale}; lower is better).");
            ResolveWinterAttritionResult(result);
            Changed?.Invoke();
            return;
        }

        if (optionId == "winter-attrition:leadership" && Leadership > 0)
        {
            Leadership--;
            AddSystemText("Spend 1 Leadership.");
            _winterAttritionRolls.Clear();
            _winterAttritionRolls.Add(Random.Shared.Next(1, 11));
            _winterAttritionRolls.Add(Random.Shared.Next(1, 11));
            AddSystemText($"Winter Attrition rolls: {_winterAttritionRolls[0]}, {_winterAttritionRolls[1]} (Morale {Morale}; lower is better).");

            _specialPrompt = "WinterAttritionChooseRoll";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "Winter Attrition",
                $"Choose 1 result. Lower is better: {Morale} or less causes no Attrition losses; above Morale loses Units equal to the difference.",
                [
                    new("winter-roll:0", $"Choose {_winterAttritionRolls[0]}", WinterAttritionChoiceDetail(_winterAttritionRolls[0])),
                    new("winter-roll:1", $"Choose {_winterAttritionRolls[1]}", WinterAttritionChoiceDetail(_winterAttritionRolls[1]))
                ]);
            Changed?.Invoke();
        }
    }

    private void ResolveWinterAttritionChooseRollPrompt(string optionId)
    {
        if (!optionId.StartsWith("winter-roll:", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(optionId[12..], out var index)
            || index < 0
            || index >= _winterAttritionRolls.Count)
            return;

        var result = _winterAttritionRolls[index];
        AddSystemText($"Choose {result} for Winter Attrition. {WinterAttritionChoiceDetail(result)}");
        _specialPrompt = null;
        FlowPrompt = null;
        ResolveWinterAttritionResult(result);
        Changed?.Invoke();
    }

    private string WinterAttritionChoiceDetail(int result)
        => result <= Morale
            ? "No Attrition losses."
            : $"Would lose {result - Morale} Unit{(result - Morale == 1 ? string.Empty : "s")} before the Priest modifier.";

    private void ResolveWinterAttritionResult(int result)
    {
        if (result <= Morale)
        {
            AddNarrativeText("The Host holds together through the winter. No units are lost to attrition.");
            CompleteWinterAttrition();
            return;
        }

        var requestedLosses = result - Morale;
        var losses = BuildWinterAttritionLosses(requestedLosses);
        if (losses.Count == 0)
        {
            AddNarrativeText("The winter should take its toll, but no Units remain to lose.");
            CompleteWinterAttrition();
            return;
        }

        AddNarrativeText($"The attrition check fails by {requestedLosses}. Winter threatens to take {losses.Count} Unit{(losses.Count == 1 ? string.Empty : "s")} from the Host.");

        if (_hasPriest)
        {
            var distinctTypes = losses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (distinctTypes.Length == 1)
            {
                var spared = distinctTypes[0];
                losses.Remove(spared);
                AddNarrativeText($"The Priest preserves 1 {SingularizeUnitLabel(spared, 1)} from the winter losses.");
                ApplyWinterAttritionLosses(losses);
                CompleteWinterAttrition();
                return;
            }

            _pendingWinterAttritionLosses.Clear();
            _pendingWinterAttritionLosses.AddRange(losses);
            _specialPrompt = "WinterPriestPreventLoss";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "Priest's Aid",
                "The Priest can prevent the loss of 1 Unit. Choose which threatened Unit is spared.",
                distinctTypes.Select(t => new FlowPromptOption(
                    $"winter-priest:{t}",
                    $"Spare 1 {SingularizeUnitLabel(t, 1)}")).ToArray());
            return;
        }

        ApplyWinterAttritionLosses(losses);
        CompleteWinterAttrition();
    }

    private List<string> BuildWinterAttritionLosses(int requestedLosses)
    {
        var remaining = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Levy"] = HostCount("Levy"),
            ["Infantry"] = HostCount("Infantry"),
            ["Archers"] = HostCount("Archers"),
            ["Cavalry"] = HostCount("Cavalry")
        };
        var order = new[] { "Levy", "Infantry", "Archers", "Cavalry" };
        var losses = new List<string>();

        while (losses.Count < requestedLosses && remaining.Values.Sum() > 0)
        {
            foreach (var type in order)
            {
                if (losses.Count >= requestedLosses)
                    break;
                if (remaining[type] <= 0)
                    continue;
                remaining[type]--;
                losses.Add(type);
            }
        }

        return losses;
    }

    private void ResolveWinterPriestPreventLossPrompt(string optionId)
    {
        const string prefix = "winter-priest:";
        if (!optionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        var spared = optionId[prefix.Length..];
        var index = _pendingWinterAttritionLosses.FindIndex(x => string.Equals(x, spared, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        _pendingWinterAttritionLosses.RemoveAt(index);
        AddNarrativeText($"The Priest preserves 1 {SingularizeUnitLabel(spared, 1)} from the winter losses.");
        var losses = _pendingWinterAttritionLosses.ToList();
        _pendingWinterAttritionLosses.Clear();
        _specialPrompt = null;
        FlowPrompt = null;
        ApplyWinterAttritionLosses(losses);
        CompleteWinterAttrition();
        Changed?.Invoke();
    }

    private void ApplyWinterAttritionLosses(IReadOnlyList<string> losses)
    {
        if (losses.Count == 0)
        {
            AddNarrativeText("The Priest's intervention leaves the Host with no Winter Attrition losses.");
            return;
        }

        var actualLosses = new List<string>();
        foreach (var type in losses)
        {
            if (LoseHostUnit(type, 1) > 0)
                actualLosses.Add(type);
        }

        if (actualLosses.Count == 0)
        {
            AddNarrativeText("No Units are lost to Winter Attrition.");
            return;
        }

        var summary = actualLosses
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Count()} {SingularizeUnitLabel(g.Key, g.Count())}")
            .ToArray();
        AddNarrativeText($"Winter Attrition removes {JoinNatural(summary)} from the Host.");
    }

    private void CompleteWinterAttrition()
    {
        var before = Morale;
        Morale = 5;
        AddNarrativeText(before == 5
            ? "Morale steadies at 5 for the new year."
            : $"With attrition resolved, Morale resets from {before} to 5.");

        _refitUndo.Clear();
        WinterStep = WinterStep.RefitReform;
        AddNarrativeText("The Host may now Refit & Reform before the new campaign year begins.");
    }

    private void CompleteWinterAfterRefit()
    {
        if (TotalHostUnits() <= 0)
        {
            WinterStep = WinterStep.Defeat;
            AddNarrativeText("No Units remain in the Host after Refit & Reform. The campaign ends in winter.");
            EndCampaign("Defeat", "No Units remain in the Host after Winter Refit & Reform. The campaign ends in defeat.");
            return;
        }

        ResetCampDeckForWinter();
        StabilizeMarketForWinter();

        WinterStep = WinterStep.Complete;
        Year++;
        Season = "Spring";
        Time = 0;
        Phase = GamePhase.Mobilization;
        MobilizationStep = MobilizationStep.ExploreReady;
        CampStep = CampStep.Ready;
        EndOfSeasonStep = EndOfSeasonStep.Ready;
        _endOfSeasonPending = false;
        _endOfSeasonName = null;
        _endOfSeasonTriggerPhase = GamePhase.Mobilization;
        FlowPrompt = null;
        _specialPrompt = null;
        _activeChronicleEntryIndex = null;

        Chronicle.Add(new(
            CalendarStamp,
            "Winter recedes. The Host takes the road again, carrying the scars and preparations of the year behind it.",
            ChronicleTone.Narrative,
            $"Year {Year} Begins"));
    }

    private void ResetCampDeckForWinter()
    {
        // The remaining draw pile joins the discard. Two previously unseeded
        // Scenes enter the ecosystem, then the whole pile is shuffled and two
        // cards are burned back to the face-down discard.
        _campDiscard.AddRange(_campDeck);
        _campDeck.Clear();

        var randomizedScenes = _unseededScenes.ToList();
        Shuffle(randomizedScenes);
        var scenesToSeed = randomizedScenes
            .Take(Math.Min(2, randomizedScenes.Count))
            .ToList();
        foreach (var scene in scenesToSeed)
        {
            _unseededScenes.Remove(scene);
            _campDiscard.Add(scene);
        }

        _campDeck.AddRange(_campDiscard);
        _campDiscard.Clear();
        Shuffle(_campDeck);
        for (var i = 0; i < 2 && _campDeck.Count > 0; i++)
        {
            _campDiscard.Add(_campDeck[0]);
            _campDeck.RemoveAt(0);
        }

        _logger.LogInformation("Winter Camp reset: {Seeded} new Scenes seeded; {Draw} draw; {Discard} discard after burn.", scenesToSeed.Count, _campDeck.Count, _campDiscard.Count);
        AddNarrativeText("The Camp is reorganized for the coming year, with new stories waiting somewhere in the deck.");
    }

    private void StabilizeMarketForWinter()
    {
        var starts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Food"] = 2,
            ["Wood"] = 3,
            ["Stone"] = 4
        };
        var changes = new List<string>();

        for (var i = 0; i < Market.Count; i++)
        {
            var current = Market[i];
            if (!starts.TryGetValue(current.Resource, out var start))
                continue;

            var next = current.BuyPrice == start
                ? start
                : current.BuyPrice + Math.Sign(start - current.BuyPrice);
            Market[i] = current with { BuyPrice = next, SellPrice = next - 2 };
            if (next != current.BuyPrice)
                changes.Add($"{current.Resource} {current.BuyPrice}→{next}");
        }

        if (changes.Count == 0)
            AddSystemText("Market stabilization: values are already at their starting levels.");
        else
            AddSystemText($"Market stabilization: {string.Join(" • ", changes)}.");
    }

    private static string SingularizeUnitLabel(string type, int count)
    {
        if (count != 1)
            return type;
        return type switch
        {
            "Archers" => "Archer",
            "Mercenaries" => "Mercenary",
            "Hedge Knights" => "Hedge Knight",
            _ => type
        };
    }

    private static string JoinNatural(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return string.Empty;
        if (items.Count == 1)
            return items[0];
        if (items.Count == 2)
            return $"{items[0]} and {items[1]}";
        return $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}";
    }

    private bool IsRefitReformActive()
        => (Phase == GamePhase.EndOfSeason && EndOfSeasonStep == EndOfSeasonStep.RefitReform)
            || (Phase == GamePhase.Winter && WinterStep == WinterStep.RefitReform);

    private void CaptureRefitUndo()
    {
        if (!IsRefitReformActive())
            return;

        _refitUndo.Add(new(
            Chronicle.Count,
            Morale,
            Leadership,
            Resources.ToList(),
            Host.ToList(),
            AvailableForces.ToList(),
            Baggage.ToList(),
            AdvancementOffer.ToList(),
            AcquiredAdvancements.ToList(),
            _advancementDeck.ToList(),
            _armorSupply.ToList(),
            PersistentItems.ToList(),
            new HashSet<string>(ActiveTokens, StringComparer.OrdinalIgnoreCase)));
    }

    public void UndoRefitReform()
    {
        if (!CanUndoRefitReform)
            return;

        var snapshot = _refitUndo[^1];
        _refitUndo.RemoveAt(_refitUndo.Count - 1);

        Morale = snapshot.Morale;
        Leadership = snapshot.Leadership;
        CopyList(Resources, snapshot.Resources);
        CopyList(Host, snapshot.Host);
        CopyList(AvailableForces, snapshot.AvailableForces);
        CopyList(Baggage, snapshot.Baggage);
        CopyList(AdvancementOffer, snapshot.AdvancementOffer);
        CopyList(AcquiredAdvancements, snapshot.AcquiredAdvancements);
        CopyList(_advancementDeck, snapshot.AdvancementDeck);
        CopyList(_armorSupply, snapshot.ArmorSupply);
        CopyList(PersistentItems, snapshot.PersistentItems);
        ActiveTokens.Clear();
        foreach (var token in snapshot.ActiveTokens)
            ActiveTokens.Add(token);

        if (Chronicle.Count > snapshot.ChronicleCount)
            Chronicle.RemoveRange(snapshot.ChronicleCount, Chronicle.Count - snapshot.ChronicleCount);

        Changed?.Invoke();
    }

    public bool CanRefitRecruit(string type)
    {
        if (!IsRefitReformActive())
            return false;

        var militaryCount = HostCount("Archers") + HostCount("Cavalry") + HostCount("Infantry");
        return type switch
        {
            "Archers" => militaryCount < 10 && AvailableForceCount("Archers") >= 1 && ResourceValue("Coin") >= 1,
            "Infantry" => militaryCount < 10 && AvailableForceCount("Infantry") >= 1 && ResourceValue("Coin") >= 2,
            "Cavalry" => militaryCount < 10 && AvailableForceCount("Cavalry") >= 1 && ResourceValue("Coin") >= 2 && ResourceValue("Food") >= 1,
            "Levy" => HostCount("Levy") < 6 && AvailableForceCount("Levy") >= 1 && ResourceValue("Food") >= 1,
            _ => false
        };
    }

    public string RefitRecruitLabel(string type) => type switch
    {
        "Archers" => "Recruit 1 Archer • 1 Coin",
        "Infantry" => "Recruit 1 Infantry • 2 Coin",
        "Cavalry" => "Recruit 1 Cavalry • 2 Coin + 1 Food",
        "Levy" => RefitLevyRecruitAmount() == 1 ? "Recruit 1 Levy • 1 Food" : "Recruit up to 2 Levy • 1 Food",
        _ => type
    };

    public void RefitRecruit(string type)
    {
        if (!CanRefitRecruit(type))
            return;

        CaptureRefitUndo();
        switch (type)
        {
            case "Archers":
                AdjustResource("Coin", -1);
                SetHostCount("Archers", HostCount("Archers") + 1);
                AdjustAvailableForce("Archers", -1);
                AddNarrativeText("Recruit 1 Archer for 1 Coin.");
                break;
            case "Infantry":
                AdjustResource("Coin", -2);
                SetHostCount("Infantry", HostCount("Infantry") + 1);
                AdjustAvailableForce("Infantry", -1);
                AddNarrativeText("Recruit 1 Infantry for 2 Coin.");
                break;
            case "Cavalry":
                AdjustResource("Coin", -2);
                RemovePhysicalResource("Food", 1);
                SetHostCount("Cavalry", HostCount("Cavalry") + 1);
                AdjustAvailableForce("Cavalry", -1);
                AddNarrativeText("Recruit 1 Cavalry for 2 Coin and 1 Food.");
                break;
            case "Levy":
                var levyRecruited = RefitLevyRecruitAmount();
                if (levyRecruited <= 0)
                {
                    _refitUndo.RemoveAt(_refitUndo.Count - 1);
                    return;
                }
                RemovePhysicalResource("Food", 1);
                SetHostCount("Levy", HostCount("Levy") + levyRecruited);
                AdjustAvailableForce("Levy", -levyRecruited);
                AddNarrativeText($"Recruit {levyRecruited} Levy for 1 Food.");
                break;
        }
        Changed?.Invoke();
    }

    private int RefitLevyRecruitAmount()
        => Math.Min(2, Math.Min(Math.Max(0, 6 - HostCount("Levy")), AvailableForceCount("Levy")));

    public bool CanRefitDismiss(string type)
        => IsRefitReformActive()
            && (type is "Archers" or "Infantry" or "Cavalry" or "Levy")
            && HostCount(type) > 0;

    public string RefitDismissLabel(string type)
        => string.Equals(type, "Levy", StringComparison.OrdinalIgnoreCase) ? "Levy" : type.TrimEnd('s');

    public void RefitDismiss(string type)
    {
        if (!CanRefitDismiss(type))
            return;

        CaptureRefitUndo();
        SetHostCount(type, HostCount(type) - 1);
        AdjustAvailableForce(type, 1);
        AddNarrativeText($"Dismiss 1 {RefitDismissLabel(type)} from the Host with no refund.");
        Changed?.Invoke();
    }

    public bool CanBuyRefitAdvancement(string id)
    {
        if (!IsRefitReformActive())
            return false;
        var advancement = AdvancementOffer.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        return advancement is not null && CanPayAdvancementCost(advancement.Cost);
    }

    public void BuyRefitAdvancement(string id)
    {
        if (!CanBuyRefitAdvancement(id))
            return;
        var advancement = AdvancementOffer.First(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        CaptureRefitUndo();
        if (!PayAdvancementCost(advancement.Cost))
        {
            _refitUndo.RemoveAt(_refitUndo.Count - 1);
            return;
        }

        AcquiredAdvancements.Add(advancement);
        _armorSupply.RemoveAll(a => string.Equals(a.Id, advancement.Id, StringComparison.OrdinalIgnoreCase));
        _advancementDeck.RemoveAll(a => string.Equals(a.Id, advancement.Id, StringComparison.OrdinalIgnoreCase));
        RefreshAdvancementOffer();
        AddNarrativeText($"Acquire {advancement.Name} for {advancement.Cost}.");
        Changed?.Invoke();
    }

    public bool CanCycleRefitAdvancement =>
        IsRefitReformActive()
        && _advancementDeck.Count > 0
        && ResourceValue("Coin") >= 1;

    public void CycleRefitAdvancement()
    {
        if (!CanCycleRefitAdvancement)
            return;
        CaptureRefitUndo();
        var discarded = _advancementDeck[0];
        AdjustResource("Coin", -1);
        _advancementDeck.RemoveAt(0);
        RefreshAdvancementOffer();
        AddNarrativeText($"Spend 1 Coin to pass over {discarded.Name} and reveal the next Advancement.");
        Changed?.Invoke();
    }

    public void FinishRefitReform()
    {
        if (!IsRefitReformActive())
            return;

        AddNarrativeText("Refit & Reform is complete.");
        _refitUndo.Clear();
        if (Phase == GamePhase.Winter)
            CompleteWinterAfterRefit();
        else
            CompleteEndOfSeason();
        Changed?.Invoke();
    }

    private bool CanPayAdvancementCost(string cost)
    {
        var coin = ParseCostAmount(cost, "C");
        var research = ParseCostAmount(cost, "R");
        return ResourceValue("Coin") >= coin && ResourceValue("Research") >= research;
    }

    private bool PayAdvancementCost(string cost)
    {
        if (!CanPayAdvancementCost(cost))
            return false;
        var coin = ParseCostAmount(cost, "C");
        var research = ParseCostAmount(cost, "R");
        if (coin > 0) AdjustResource("Coin", -coin);
        if (research > 0) AdjustResource("Research", -research);
        return true;
    }

    private static int ParseCostAmount(string cost, string code)
    {
        var match = Regex.Match(cost ?? string.Empty, $@"(\d+)\s*{Regex.Escape(code)}(?:T)?\b", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private void CompleteEndOfSeason()
    {
        var ended = EndOfSeasonName;
        var returnAfter = _endOfSeasonTriggerPhase;

        _endOfSeasonPending = false;
        _endOfSeasonName = null;
        EndOfSeasonStep = EndOfSeasonStep.Complete;
        _harvestStaging.Clear();
        _harvestPacked.Clear();
        _harvestPurged.Clear();
        _harvestSold.Clear();
        _harvestPreparationUndo.Clear();
        _harvestPackingUndo.Clear();
        _harvestMarketSalesRemaining = 0;
        AddNarrativeText($"End of {ended} is complete.");
        _activeChronicleEntryIndex = null;

        if (string.Equals(ended, "Autumn", StringComparison.OrdinalIgnoreCase))
        {
            if (Year >= 3)
            {
                EnterFinale();
            }
            else
            {
                Season = "Winter";
                WinterStep = WinterStep.Ready;
                _winterAttritionRolls.Clear();
                _pendingWinterAttritionLosses.Clear();
                Phase = GamePhase.Winter;
            }
            return;
        }

        Season = string.Equals(ended, "Spring", StringComparison.OrdinalIgnoreCase) ? "Summer" : "Autumn";

        // If an unusually long phase carried Time across the next threshold
        // before the prior End of Season could resolve, immediately queue the
        // newly crossed season rather than silently skipping it.
        if (Time > SeasonThreshold(Season))
        {
            _endOfSeasonPending = true;
            _endOfSeasonName = Season;
            _endOfSeasonTriggerPhase = returnAfter;
            EnterEndOfSeason();
            return;
        }

        if (returnAfter == GamePhase.Mobilization)
        {
            Phase = GamePhase.Camp;
            CampStep = CampStep.Ready;
            MobilizationStep = MobilizationStep.ArrivalComplete;
        }
        else
        {
            Phase = GamePhase.Mobilization;
            MobilizationStep = MobilizationStep.ExploreReady;
            CampStep = CampStep.Complete;
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (amount <= 0)
            return;
        counts[key] = counts.TryGetValue(key, out var current) ? current + amount : amount;
    }

    private static string FormatResourceCounts(IEnumerable<KeyValuePair<string, int>> counts)
    {
        var parts = counts
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key == "Food" ? 0 : kv.Key == "Wood" ? 1 : kv.Key == "Stone" ? 2 : 3)
            .Select(kv => $"{kv.Value} {kv.Key}")
            .ToArray();
        return parts.Length switch
        {
            0 => "no Resources",
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts[..^1])}, and {parts[^1]}"
        };
    }

    private bool CampLocationAllowed(CampCardState card)
    {
        var location = card.Location ?? "Any";
        if (string.Equals(location, "Any", StringComparison.OrdinalIgnoreCase))
            return true;

        var clearing = PlayArea.LastOrDefault(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        if (clearing is null)
            return false;

        var playerControlled = string.Equals(clearing.Controller, "Player", StringComparison.OrdinalIgnoreCase);
        var barbarian = !string.IsNullOrWhiteSpace(clearing.Controller) && !playerControlled;
        var allied = barbarian && Tribes.Any(t => NormalizeTribeName(t.Name) == NormalizeTribeName(clearing.Controller!) && t.Rapport == "Allied");

        if (string.Equals(location, "Controlled", StringComparison.OrdinalIgnoreCase))
            return playerControlled || allied;
        if (string.Equals(location, "Barbarian", StringComparison.OrdinalIgnoreCase))
        {
            if (barbarian)
                return true;
            return card.Id == "CAMP-002" && playerControlled && HasBuilding("Market");
        }
        return true;
    }

    private bool CanPayFixedCampCost(CampCardState card)
        => !string.Equals(card.CostType, "Fixed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(card.CostValue)
            || CanPayCostBundle(card.CostValue!);

    private bool CanPayCostBundle(string bundle)
    {
        foreach (Match match in Regex.Matches(bundle, @"(\d+)([CFWSRL])", RegexOptions.IgnoreCase))
        {
            var amount = int.Parse(match.Groups[1].Value);
            var target = CostCodeTarget(match.Groups[2].Value);
            var available = target switch
            {
                "Leadership" => Leadership,
                _ => ResourceValue(target)
            };
            if (available < amount)
                return false;
        }
        return true;
    }

    private bool SpendCostBundle(string bundle, bool narrate)
    {
        if (!CanPayCostBundle(bundle))
            return false;
        foreach (Match match in Regex.Matches(bundle, @"(\d+)([CFWSRL])", RegexOptions.IgnoreCase))
        {
            var amount = int.Parse(match.Groups[1].Value);
            var target = CostCodeTarget(match.Groups[2].Value);
            if (target == "Leadership")
                Leadership -= amount;
            else if (IsPhysicalResource(target))
                RemovePhysicalResource(target, amount);
            else
                AdjustResource(target, -amount);
            if (narrate)
                AddNarrativeText($"Spend {amount} {target}.");
        }
        return true;
    }

    private static string CostCodeTarget(string code) => code.ToUpperInvariant() switch
    {
        "C" => "Coin",
        "F" => "Food",
        "W" => "Wood",
        "S" => "Stone",
        "R" => "Research",
        "L" => "Leadership",
        _ => code
    };

    private bool IsHostileEcho(CampCardState card)
        => card.CardType == "Echo" && string.Equals(card.Keyword, "Hostile", StringComparison.OrdinalIgnoreCase);

    public void BeginExplore()
        => BeginExploreInternal(false);

    public void ProceedToArrival()
    {
        if (Phase != GamePhase.Mobilization || MobilizationStep != MobilizationStep.ArrivalReady)
            return;

        BeginArrival();
    }

    private void BeginExploreInternal(bool forced)
    {
        if (Phase != GamePhase.Mobilization)
            return;
        if (!forced && MobilizationStep != MobilizationStep.ExploreReady)
            return;

        var cost = HasToken("ForcedMarch") || HasToken("Forced March") ? 1 : 2;
        if (HasToken("HighOverlookClearRoad"))
            cost = Math.Max(1, cost - 1);

        AdvanceTime(cost);
        if (Phase == GamePhase.Finale)
        {
            Changed?.Invoke();
            return;
        }

        if (!forced && HasScouting)
        {
            PendingTerrainChoices.Clear();
            PendingTerrainChoices.Add(DrawTerrain());
            PendingTerrainChoices.Add(DrawTerrain());
            MobilizationStep = MobilizationStep.TerrainChoice;
            _logger.LogInformation("Explore started. Spent {Cost} Time. Scouting drew two Terrain cards.", cost);
        }
        else
        {
            var terrain = DrawTerrain();
            PlaceExploredTerrain(terrain, forced);
            _logger.LogInformation("{Kind} Explore started. Spent {Cost} Time. Drew {Terrain}.", forced ? "Forced" : "Normal", cost, terrain);
        }

        Changed?.Invoke();
    }

    public void ChooseTerrain(TerrainType terrain)
    {
        if (MobilizationStep != MobilizationStep.TerrainChoice || !PendingTerrainChoices.Contains(terrain))
            return;

        var discarded = PendingTerrainChoices.FirstOrDefault(t => t != terrain);
        PendingTerrainChoices.Clear();
        PlaceExploredTerrain(terrain);
        _logger.LogInformation("Scouting selected {Terrain}; discarded {Discarded}.", terrain, discarded);
        Changed?.Invoke();
    }

    public void ResolveFlowPrompt(string optionId)
    {
        if (FlowPrompt is null)
            return;

        var selectedOption = FlowPrompt.Options.FirstOrDefault(o => string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase));
        if (selectedOption is null || selectedOption.Disabled)
            return;

        RecordChronicleDecisionSelection(optionId);

        if (_specialPrompt == "WinterAttritionMethod")
        {
            ResolveWinterAttritionMethodPrompt(optionId);
            return;
        }

        if (_specialPrompt == "WinterAttritionChooseRoll")
        {
            ResolveWinterAttritionChooseRollPrompt(optionId);
            return;
        }

        if (_specialPrompt == "WinterPriestPreventLoss")
        {
            ResolveWinterPriestPreventLossPrompt(optionId);
            return;
        }

        if (_specialPrompt == "ResourceStaging")
        {
            ResolveResourceStagingPrompt(optionId);
            return;
        }

        if (_specialPrompt == "BaggageOverflow")
        {
            ResolveBaggageOverflowPrompt(optionId);
            return;
        }

        if (_specialPrompt == "HighOverlook")
        {
            ResolveHighOverlookPrompt(optionId);
            return;
        }

        if (_specialPrompt == "VacantArrival")
        {
            ResolveVacantArrivalPrompt(optionId);
            return;
        }

        if (_specialPrompt == "ArrivalTributeFallback")
        {
            ResolveTributeFallbackPrompt(optionId);
            return;
        }

        if (_specialPrompt == "ArrivalHostileGateTest")
        {
            ResolveArrivalGateTest(optionId, hostileGate: true);
            return;
        }

        if (_specialPrompt == "ArrivalPassageGateTest")
        {
            ResolveArrivalGateTest(optionId, hostileGate: false);
            return;
        }

        if (_specialPrompt == "CampMarketTrade")
        {
            ResolveCampMarketTradePrompt(optionId);
            return;
        }

        if (_specialPrompt == "CampStewardBuy")
        {
            ResolveCampStewardBuyPrompt(optionId);
            return;
        }

        if (_specialPrompt == "CampAfterPlayDisposition")
        {
            ResolveCampAfterPlayDispositionPrompt(optionId);
            return;
        }

        if (_specialPrompt == "NoArrivalFlow" && optionId == "no-arrival-flow:continue")
        {
            _specialPrompt = null;
            FlowPrompt = null;
            CompleteArrivalStep();
            Changed?.Invoke();
            return;
        }

        if (_specialPrompt == "NoFlow" && optionId == "no-flow:continue")
        {
            _specialPrompt = null;
            FlowPrompt = null;
            CompleteExploreEncounter();
            Changed?.Invoke();
            return;
        }

        if (FlowPrompt.Kind == FlowPromptKind.Combat)
        {
            ResolveCombatPrompt(optionId);
            return;
        }

        if (FlowPrompt.Kind == FlowPromptKind.Test)
        {
            ResolveTestPrompt(optionId);
            return;
        }

        if (_pendingMultiSelectRemaining > 0)
        {
            ResolveMultiSelectPrompt(optionId);
            return;
        }

        if (optionId.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(optionId[7..], out var branchIndex))
                return;

            var node = CurrentFlowNode();
            if (node is null || !node.Value.TryGetProperty("branches", out var branches))
                return;

            var branchArray = branches.EnumerateArray().ToArray();
            if (branchIndex < 0 || branchIndex >= branchArray.Length)
                return;

            var branch = branchArray[branchIndex];
            var next = GetString(branch, "next");
            PrepareTestNarrative(branch, next);
            RecordChoiceNarrative(branch);
            AddPlayerText(branch);
            FlowPrompt = null;
            _activeNodeId = next;
            AdvanceFlow();
            Changed?.Invoke();
            return;
        }

        if (optionId.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = optionId[7..];
            object? selected = raw;
            if (int.TryParse(raw, out var intValue))
                selected = intValue;

            if (!string.IsNullOrWhiteSpace(_pendingSelectVariable))
            {
                _flowVars[_pendingSelectVariable] = selected;
                if (_activeFlowContext == FlowContext.Camp)
                    AddNarrativeText($"You select {raw}.");
            }

            FlowPrompt = null;
            _activeNodeId = _pendingSelectNextNode;
            _pendingSelectVariable = null;
            _pendingSelectNextNode = null;
            AdvanceFlow();
            Changed?.Invoke();
        }
    }

    public string CalendarStamp => $"{Season} • Year {Year}";
    private string CalendarPhaseStamp(string phase) => $"{CalendarStamp} • {phase}";

    public int ResourceValue(string name) =>
        Resources.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

    public bool HasScouting => PersistentItems.Any(p => string.Equals(p.Name, "Scouting", StringComparison.OrdinalIgnoreCase));

    private void ResetNewGameState()
    {
        Year = 1;
        Season = "Spring";
        Time = 0;
        Morale = 5;
        Leadership = 3;
        Phase = GamePhase.Mobilization;
        MobilizationStep = MobilizationStep.ExploreReady;
        CurrentExploreTerrain = TerrainType.None;
        PendingExploreCard = null;
        PendingArrivalCard = null;
        PendingCampCard = null;
        CampStep = CampStep.Ready;
        FlowPrompt = null;
        _terrainSerial = 0;
        _clearingSerial = 0;
        _nextMapSequence = 1;
        _catalogInitialized = false;
        _catalog = null;
        _activeFlow = null;
        _activeNodeId = null;
        _activeFlowContext = FlowContext.None;
        _pendingOutcomeText = null;
        _pendingBrowserFlavor = null;
        _specialPrompt = null;
        _currentArrivalTribe = null;
        _arrivalCombatResolved = false;
        _arrivalCardRevealed = false;
        _activeChronicleDecisionIndex = null;
        _campInitialized = false;
        _campPhaseEnding = false;
        _campCardResolvedSuccessfully = false;
        _workCard = null;
        _campMarketTradesRemaining = 0;
        _pendingCampActionNextNode = null;
        _pendingResourceGains.Clear();
        _pendingResourceGainResume = ResourceGainResume.None;
        _pendingResourceGainNextNode = null;
        _pendingResourceGainOpenStaging = false;
        _debugCampSimulation = false;
        _debugReturnPhase = GamePhase.Mobilization;
        _debugReturnCampStep = CampStep.Ready;
        _debugReturnMobilizationStep = MobilizationStep.ExploreReady;
        _endOfSeasonPending = false;
        _endOfSeasonTriggerPhase = GamePhase.Mobilization;
        _endOfSeasonName = null;
        EndOfSeasonStep = EndOfSeasonStep.Ready;
        WinterStep = WinterStep.Ready;
        _winterAttritionRolls.Clear();
        _pendingWinterAttritionLosses.Clear();
        _refitUndo.Clear();
        _harvestStaging.Clear();
        _harvestPacked.Clear();
        _harvestPurged.Clear();
        _harvestSold.Clear();
        _harvestPreparationUndo.Clear();
        _harvestPackingUndo.Clear();
        _harvestMarketSalesRemaining = 0;
        _hasPriest = false;
        _priestSource = null;
        _pendingTributeFallbackCost = 0;
        _pendingTributeFallbackResource = null;
        _pendingTestNarrativeLabel = null;
        Combat = null;
        CombatVisible = false;
        Finale = null;
        CampaignOutcome = null;
        CampaignEndReason = null;
        GameLogId = Guid.NewGuid();
        GameStartedUtc = DateTime.UtcNow;
        GameEndedUtc = null;
        _combatResumeMode = CombatResumeMode.None;
        _combatResumeNextNode = null;
        _combatSourceTribe = null;
        _combatDisengagedPending = false;
        _skipNextDisengagePenaltyAction = false;

        Resources.Clear();
        Market.Clear();
        Host.Clear();
        AdvancementOffer.Clear();
        AcquiredAdvancements.Clear();
        AvailableForces.Clear();
        PersistentItems.Clear();
        ActiveTokens.Clear();
        Baggage.Clear();
        Tribes.Clear();
        PlayArea.Clear();
        Chronicle.Clear();
        BattleHistory.Clear();
        PendingTerrainChoices.Clear();
        _terrainDeck.Clear();
        _exploreDeck.Clear();
        _arrivalDeck.Clear();
        _campDeck.Clear();
        _campDiscard.Clear();
        _unseededScenes.Clear();
        _unseededEchoes.Clear();
        _removedCampCards.Clear();
        CampHand.Clear();
        _clearingDeck.Clear();
        _advancementDeck.Clear();
        _armorSupply.Clear();
        _flowVars.Clear();
        _lastRollResults.Clear();
        _lastUnitTypeRolls.Clear();
        _seededEchoes.Clear();
        _echoOrigins.Clear();
        _enabledContentSets.Clear();
        _enabledContentSets.Add("Base");
        _viewedCampCards.Clear();
        _pendingSelectedUnits.Clear();
        _activeChronicleEntryIndex = null;

        Resources.AddRange([
            new("Coin", 5),
            new("Food", 2),
            new("Wood", 1),
            new("Stone", 1),
            new("Research", 0)
        ]);

        Market.AddRange([
            new("Food", 2, 0),
            new("Wood", 3, 1),
            new("Stone", 4, 2)
        ]);

        Host.AddRange([
            new("ARC", "Archers", 0),
            new("CAV", "Cavalry", 0),
            new("INF", "Infantry", 0),
            new("LEV", "Levy", 2)
        ]);

        AvailableForces.AddRange([
            new("Archers", 8),
            new("Cavalry", 8),
            new("Infantry", 10),
            new("Levy", 4)
        ]);

        Baggage.AddRange([
            new(1, "Food", 2),
            new(2, "Wood"),
            new(3, "Stone"),
            new(4, null)
        ]);

        Tribes.AddRange([
            new("Sun-Touched", "Unmet"),
            new("Pale Hands", "Unmet"),
            new("Nightstone", "Unmet"),
            new("Quietus", "Unmet")
        ]);

        PlayArea.Add(new(
            "START",
            0,
            PlayAreaKind.Clearing,
            Controller: "Player",
            IsHostLocation: true));

        Chronicle.Add(new(
            CalendarStamp,
            "The Host is assembled. The road to Middelalderen begins here.",
            ChronicleTone.Narrative,
            "The March Begins"));

        _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Plains, 10));
        _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Forest, 8));
        _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Mountain, 6));
        Shuffle(_terrainDeck);

        // The Starting Clearing is already in play. The remaining Clearing
        // stack contains 9 Barbarian-icon and 5 vacant Clearings.
        _clearingDeck.AddRange(Enumerable.Repeat(true, 9));
        _clearingDeck.AddRange(Enumerable.Repeat(false, 5));
        Shuffle(_clearingDeck);
    }

    private void RefreshAdvancementOffer()
    {
        AdvancementOffer.Clear();
        if (_armorSupply.Count > 0)
            AdvancementOffer.Add(_armorSupply[0]);
        if (_advancementDeck.Count > 0)
            AdvancementOffer.Add(_advancementDeck[0]);
    }

    private void PlaceExploredTerrain(TerrainType terrain, bool forced = false)
    {
        var firstMarch = PlayArea.Count == 1;
        for (var i = 0; i < PlayArea.Count; i++)
        {
            if (PlayArea[i].IsHostLocation)
                PlayArea[i] = PlayArea[i] with { IsHostLocation = false };
        }

        _terrainSerial++;
        PlayArea.Add(new(
            $"TRN-{_terrainSerial:000}",
            _nextMapSequence++,
            PlayAreaKind.Terrain,
            terrain,
            IsHostLocation: true));

        CurrentExploreTerrain = terrain;
        Chronicle.Add(new(
            CalendarPhaseStamp("Explore"),
            ExploreTravelNarrative(terrain, firstMarch, forced),
            ChronicleTone.Result,
            terrain.ToString()));

        ResolveExploreDraw();
    }

    private void ResolveExploreDraw()
    {
        if (CurrentExploreTerrain == TerrainType.None)
            return;

        if (_exploreDeck.Count == 0)
        {
            _logger.LogWarning("Explore deck is empty.");
            AddUneventfulExploreNarrative();
            MobilizationStep = MobilizationStep.ArrivalReady;
            Changed?.Invoke();
            return;
        }

        var card = _exploreDeck[0];
        _exploreDeck.RemoveAt(0);
        var matches = card.TerrainKeywords.Any(k =>
            string.Equals(k, CurrentExploreTerrain.ToString(), StringComparison.OrdinalIgnoreCase));

        var clearRoadActive = HasToken("HighOverlookClearRoad");

        if (!matches)
        {
            if (clearRoadActive)
                ActiveTokens.Remove("HighOverlookClearRoad");

            PendingExploreCard = null;
            AddUneventfulExploreNarrative();
            MobilizationStep = MobilizationStep.ArrivalReady;
            _logger.LogInformation(
                "Explore card {CardId} ({Title}) did not match {Terrain}; discarded without effect.",
                card.Id, card.Title, CurrentExploreTerrain);
            Changed?.Invoke();
            return;
        }

        PendingExploreCard = card;
        MobilizationStep = MobilizationStep.ExploreEncounter;
        Chronicle.Add(new(
            CalendarPhaseStamp("Explore"),
            string.IsNullOrWhiteSpace(card.Flavor) ? "Something on the road demands attention." : card.Flavor,
            ChronicleTone.Narrative,
            card.Title));
        _activeChronicleEntryIndex = Chronicle.Count - 1;

        if (clearRoadActive)
        {
            _specialPrompt = "HighOverlook";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "High Overlook",
                "The marked clear road lets you ignore this Explore card. Resolve it or pass it by?",
                [
                    new("high-overlook:resolve", "Resolve the Explore card"),
                    new("high-overlook:ignore", "Ignore the Explore card")
                ]);
            Changed?.Invoke();
            return;
        }

        StartExploreFlow(card);
    }

    private void ResolveHighOverlookPrompt(string optionId)
    {
        ActiveTokens.Remove("HighOverlookClearRoad");
        _specialPrompt = null;
        FlowPrompt = null;

        if (optionId == "high-overlook:ignore")
        {
            _logger.LogInformation("High Overlook ignored Explore card {CardId}.", PendingExploreCard?.Id);
            AddNarrativeText("The Host follows the marked clear road and leaves the encounter behind.");
            CompleteExploreEncounter();
        }
        else if (PendingExploreCard is not null)
        {
            StartExploreFlow(PendingExploreCard);
        }

        Changed?.Invoke();
    }

    private void BeginArrival()
    {
        if (Phase != GamePhase.Mobilization)
            return;

        MobilizationStep = MobilizationStep.ArrivalEncounter;
        PendingArrivalCard = null;
        _currentArrivalTribe = null;
        _arrivalCombatResolved = false;
        _arrivalCardRevealed = false;
        FlowPrompt = null;
        _specialPrompt = null;

        for (var i = 0; i < PlayArea.Count; i++)
        {
            if (PlayArea[i].IsHostLocation)
                PlayArea[i] = PlayArea[i] with { IsHostLocation = false };
        }

        var barbarian = DrawClearing();
        _clearingSerial++;
        PlayArea.Add(new(
            $"CLR-{_clearingSerial:000}",
            _nextMapSequence++,
            PlayAreaKind.Clearing,
            HasBarbarianIcon: barbarian,
            Controller: barbarian ? null : "Player",
            IsHostLocation: true));

        if (!barbarian)
        {
            Chronicle.Add(new(
                CalendarPhaseStamp("Arrival"),
                "The road opens into an unclaimed clearing. The Host takes control without resistance.",
                ChronicleTone.Narrative,
                "A Vacant Clearing"));
            _activeChronicleEntryIndex = Chronicle.Count - 1;
            ResolveClearingRevealReminders(false);
            PresentVacantArrivalChoice();
            Changed?.Invoke();
            return;
        }

        if (_arrivalDeck.Count == 0 && _catalog is not null)
        {
            _arrivalDeck.AddRange(_catalog.GetArrivalCards());
            Shuffle(_arrivalDeck);
            _logger.LogInformation("Arrival deck reshuffled after exhaustion.");
        }

        if (_arrivalDeck.Count == 0)
        {
            _logger.LogError("Arrival deck is empty.");
            CompleteArrivalStep();
            Changed?.Invoke();
            return;
        }

        PendingArrivalCard = _arrivalDeck[0];
        _arrivalDeck.RemoveAt(0);
        _currentArrivalTribe = CanonicalTribeName(PendingArrivalCard.Tribe);

        var clearingIndex = PlayArea.Count - 1;
        PlayArea[clearingIndex] = PlayArea[clearingIndex] with { Controller = _currentArrivalTribe };

        Chronicle.Add(new(
            CalendarPhaseStamp("Arrival"),
            ArrivalSettlementNarrative(_currentArrivalTribe),
            ChronicleTone.Narrative,
            "Arrival"));
        _activeChronicleEntryIndex = Chronicle.Count - 1;

        ResolveClearingRevealReminders(true);
        ResolveFirstContactIfNeeded();
        SyncPlayAreaHostilityFromRapport();
        BeginTributeProcedure();
        Changed?.Invoke();
    }

    private bool DrawClearing()
    {
        if (_clearingDeck.Count == 0)
        {
            _clearingDeck.AddRange(Enumerable.Repeat(true, 9));
            _clearingDeck.AddRange(Enumerable.Repeat(false, 5));
            Shuffle(_clearingDeck);
            _logger.LogInformation("Clearing stack reshuffled after exhaustion.");
        }

        var result = _clearingDeck[0];
        _clearingDeck.RemoveAt(0);
        return result;
    }

    private void PresentVacantArrivalChoice()
    {
        _specialPrompt = "VacantArrival";
        var forageUnavailable = ForageUnavailableReason();
        FlowPrompt = new(
            FlowPromptKind.Choice,
            "Arrival",
            "The clearing is secure. Forage before making camp?",
            [
                new(
                    "arrival:forage",
                    "Forage",
                    forageUnavailable is null
                        ? "Advance 1 Time. On 4–6, gain 1 Resource matching the adjacent Terrain."
                        : $"Unavailable: {forageUnavailable}",
                    forageUnavailable is not null),
                new("arrival:continue", "Continue", "Proceed to Camp without foraging.")
            ]);
    }

    private string? ForageUnavailableReason()
    {
        var terrainIndex = CurrentForageTerrainIndex();
        if (terrainIndex < 0)
            return "there is no adjacent Terrain to forage";

        var terrain = PlayArea[terrainIndex];
        var reasons = new List<string>();
        if (terrain.IsDepleted)
            reasons.Add($"the adjacent {terrain.Terrain} is Depleted");

        var hostileSettlements = new[] { terrainIndex - 1, terrainIndex + 1 }
            .Where(i => i >= 0 && i < PlayArea.Count)
            .Select(i => PlayArea[i])
            .Where(p => p.Kind == PlayAreaKind.Clearing && p.IsHostile)
            .Select(p => p.Controller)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hostileSettlements.Length > 0)
            reasons.Add($"Hostile settlement adjacent: {string.Join(", ", hostileSettlements)}");

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    private int CurrentForageTerrainIndex()
    {
        var clearingIndex = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        if (clearingIndex < 0)
            clearingIndex = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Clearing);

        if (clearingIndex > 0 && PlayArea[clearingIndex - 1].Kind == PlayAreaKind.Terrain)
            return clearingIndex - 1;
        if (clearingIndex >= 0 && clearingIndex + 1 < PlayArea.Count && PlayArea[clearingIndex + 1].Kind == PlayAreaKind.Terrain)
            return clearingIndex + 1;
        return -1;
    }

    private void ResolveVacantArrivalPrompt(string optionId)
    {
        if (optionId == "arrival:forage" && ForageUnavailableReason() is not null)
            return;

        _specialPrompt = null;
        FlowPrompt = null;

        if (optionId == "arrival:forage")
        {
            AdvanceTime(1);
            if (Phase == GamePhase.Finale)
            {
                Changed?.Invoke();
                return;
            }
            var result = Random.Shared.Next(1, 7);
            _logger.LogInformation("Forage roll: {Result}.", result);
            AddSystemText($"Forage roll: {result}.");
            if (result >= 4)
            {
                var resource = CurrentExploreTerrain switch
                {
                    TerrainType.Plains => "Food",
                    TerrainType.Forest => "Wood",
                    TerrainType.Mountain => "Stone",
                    _ => string.Empty
                };
                if (!string.IsNullOrWhiteSpace(resource))
                {
                    AddNarrativeText("The Host finds useful supplies while foraging.");
                    if (!BeginPhysicalResourceGain(new[] { (resource, 1) }, ResourceGainResume.CompleteArrival))
                    {
                        Changed?.Invoke();
                        return;
                    }
                }
            }
            else
            {
                AddNarrativeText("The search yields nothing useful.");
            }
        }
        else
        {
            AddNarrativeText("The Host leaves the clearing undisturbed and makes camp.");
        }

        CompleteArrivalStep();
        Changed?.Invoke();
    }

    private void ResolveClearingRevealReminders(bool barbarian)
    {
        if (HasToken("AncientPathway"))
        {
            if (!barbarian)
            {
                AddNarrativeText("The ancient pathway leads true to a vacant settlement.");
                ApplyGain("Leadership", 1);
            }
            else
            {
                AddNarrativeText("The ancient pathway ends at an occupied settlement, and its advantage is lost.");
            }
            ActiveTokens.Remove("AncientPathway");
        }

        if (HasToken("HighOverlookScoutApproach"))
        {
            if (barbarian)
                ApplyGain("Leadership", 1);
            else
                ApplyGain("Morale", 1);
            ActiveTokens.Remove("HighOverlookScoutApproach");
        }
    }

    private void ResolveFirstContactIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_currentArrivalTribe)
            || !string.Equals(GetTribeRapport(_currentArrivalTribe), "Unmet", StringComparison.OrdinalIgnoreCase))
            return;

        var d1 = Random.Shared.Next(1, 7);
        var d2 = Random.Shared.Next(1, 7);
        var total = d1 + d2;
        var rapport = total switch
        {
            <= 5 => "Hostile",
            >= 11 => "Friendly",
            _ => "Neutral"
        };

        SetRapport(_currentArrivalTribe, rapport);
        _logger.LogInformation("First Contact with {Tribe}: {D1}+{D2}={Total}, starting {Rapport}.", _currentArrivalTribe, d1, d2, total, rapport);
        AddSystemText($"First Contact roll: {d1} + {d2} = {total}.");

        AddNarrativeText(FirstContactNarrative(_currentArrivalTribe, rapport));
    }

    private void BeginTributeProcedure()
    {
        if (PendingArrivalCard is null || string.IsNullOrWhiteSpace(_currentArrivalTribe))
        {
            CompleteArrivalStep();
            return;
        }

        if (!TryParseTribute(PendingArrivalCard.Tribute, out var resource, out var amount))
        {
            _logger.LogWarning("Could not parse Tribute '{Tribute}' for {CardId}; proceeding to options.", PendingArrivalCard.Tribute, PendingArrivalCard.Id);
            StartArrivalFlow();
            return;
        }

        var tribeName = CanonicalTribeName(_currentArrivalTribe);
        AddNarrativeText(TributeDemandNarrative(tribeName, amount, resource));

        if (CanPayExactTribute(resource, amount))
        {
            PayExactTribute(resource, amount);
            AddNarrativeText(TributePaidNarrative(tribeName, amount, resource));
            AfterTributePaid();
            return;
        }

        var fallback = TributeFallbackCoinCost(resource, amount);
        if (fallback is int fallbackCost && ResourceValue("Coin") >= fallbackCost)
        {
            _pendingTributeFallbackCost = fallbackCost;
            _pendingTributeFallbackResource = resource;
            _specialPrompt = "ArrivalTributeFallback";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "Tribute",
                $"The Host cannot provide {amount} {resource}. Pay Coin instead?",
                [
                    new("tribute:pay-fallback", $"Pay {fallbackCost} Coin", "Pay the full fallback and continue."),
                    new("tribute:decline", "Do not pay", "Attempt passage without the tribute.")
                ]);
            return;
        }

        AddNarrativeText($"The Host cannot provide the demanded {amount} {resource} in full.");
        ContinueAfterUnpaidTribute();
    }

    private void ResolveTributeFallbackPrompt(string optionId)
    {
        FlowPrompt = null;
        _specialPrompt = null;

        if (optionId == "tribute:pay-fallback" && ResourceValue("Coin") >= _pendingTributeFallbackCost)
        {
            AdjustResource("Coin", -_pendingTributeFallbackCost);
            AddNarrativeText($"With no {_pendingTributeFallbackResource} to offer, the Host pays {_pendingTributeFallbackCost} Coin instead.");
            _pendingTributeFallbackCost = 0;
            _pendingTributeFallbackResource = null;
            AfterTributePaid();
        }
        else
        {
            AddNarrativeText("The Host declines the Coin fallback.");
            _pendingTributeFallbackCost = 0;
            _pendingTributeFallbackResource = null;
            ContinueAfterUnpaidTribute();
        }

        Changed?.Invoke();
    }

    private void AfterTributePaid()
    {
        if (CurrentTribeIsHostile())
            PresentArrivalGateTest(hostileGate: true);
        else
            StartArrivalFlow();
    }

    private void ContinueAfterUnpaidTribute()
    {
        if (CurrentTribeIsHostile())
            PresentArrivalForcedCombat("The hostile tribe refuses passage without tribute and attacks.");
        else
            PresentArrivalGateTest(hostileGate: false);
    }

    private void PresentArrivalGateTest(bool hostileGate)
    {
        var drm = CurrentTribeRapportDrm();
        var detail = drm == 0 ? null : $"Rapport DRM: {(drm > 0 ? "+" : string.Empty)}{drm}";
        var options = new List<FlowPromptOption> { new("gate:normal", "Roll 2d6", detail) };
        if (Leadership > 0)
            options.Add(new("gate:leadership", "Spend 1 Leadership", "Roll 3d6 and keep the best 2"));

        _specialPrompt = hostileGate ? "ArrivalHostileGateTest" : "ArrivalPassageGateTest";
        FlowPrompt = new(
            FlowPromptKind.Test,
            "Passage",
            hostileGate
                ? "The tribute is accepted, but hostility remains. Test 8+ to secure passage."
                : "Without tribute, test 8+ to persuade the tribe to allow passage.",
            options);
    }

    private void ResolveArrivalGateTest(string optionId, bool hostileGate)
    {
        var useLeadership = optionId == "gate:leadership" && Leadership > 0;
        var drm = CurrentTribeRapportDrm();
        var (total, criticalFailure, criticalSuccess, diceText) = RollStandardTest(useLeadership, drm);
        _logger.LogInformation("Arrival passage test for {Tribe}: {Dice}; total {Total}.", _currentArrivalTribe, diceText, total);
        AddSystemText(useLeadership
            ? $"Roll: {diceText} (keep best 2){(drm == 0 ? string.Empty : $", DRM {(drm > 0 ? "+" : string.Empty)}{drm}")} = {total}."
            : $"Roll: {diceText.Replace(", ", " + ")}{(drm == 0 ? string.Empty : $" {(drm > 0 ? "+" : string.Empty)}{drm}")} = {total}.");
        FlowPrompt = null;
        _specialPrompt = null;

        if (hostileGate)
        {
            if (total >= 8 && !criticalFailure)
            {
                AddNarrativeText("The passage test succeeds.");
                AddNarrativeText("The tribute and appeal cool the immediate hostility.");
                AdjustRapport("CurrentTribe", 1);
                StartArrivalFlow();
            }
            else
            {
                AddNarrativeText("The passage test fails.");
                AddNarrativeText($"The {_currentArrivalTribe} reject the appeal and attack.");
                PresentArrivalForcedCombat();
            }
            Changed?.Invoke();
            return;
        }

        if (criticalSuccess)
            AdjustRapport("CurrentTribe", 1);

        if (total >= 8 && !criticalFailure)
        {
            AddNarrativeText("The passage test succeeds.");
            AddNarrativeText($"The {_currentArrivalTribe} relent and permit the Host to proceed.");
            StartArrivalFlow();
            Changed?.Invoke();
            return;
        }

        AddNarrativeText("The passage test fails.");
        AddNarrativeText($"The {_currentArrivalTribe} refuse passage.");
        AdjustRapport("CurrentTribe", -1);
        if (criticalFailure)
            AdjustRapport("CurrentTribe", -1);

        if (CurrentTribeIsHostile())
            PresentArrivalForcedCombat();
        else
            BeginForcedMobilization();

        Changed?.Invoke();
    }

    private (int Total, bool CriticalFailure, bool CriticalSuccess, string DiceText) RollStandardTest(bool useLeadership, int drm)
    {
        var primary1 = Random.Shared.Next(1, 7);
        var primary2 = Random.Shared.Next(1, 7);
        var dice = new List<int> { primary1, primary2 };
        if (useLeadership && Leadership > 0)
        {
            Leadership--;
            dice.Add(Random.Shared.Next(1, 7));
        }

        var kept = dice.OrderByDescending(x => x).Take(2).ToArray();
        return (
            kept.Sum() + drm,
            primary1 == 1 && primary2 == 1,
            primary1 == 6 && primary2 == 6,
            string.Join(", ", dice));
    }

    private void StartArrivalFlow()
    {
        if (_catalog is null || PendingArrivalCard is null)
        {
            CompleteArrivalStep();
            return;
        }

        RevealArrivalCard();

        var flow = _catalog.GetFlowForSource(PendingArrivalCard.Id, "OnResolve");
        if (flow is null)
        {
            _logger.LogWarning("No Arrival EventFlow found for {CardId}.", PendingArrivalCard.Id);
            _specialPrompt = "NoArrivalFlow";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                PendingArrivalCard.Title,
                "This Arrival card does not yet have compiled EventFlow.",
                [new("no-arrival-flow:continue", "Continue to Camp")]);
            return;
        }

        _activeFlow = flow.Value;
        _activeNodeId = GetString(flow.Value, "start_node") ?? "N01";
        _activeFlowContext = FlowContext.Arrival;
        _flowVars.Clear();
        _lastRollResults.Clear();
        _lastUnitTypeRolls.Clear();
        _lastRollText = null;
        _pendingSelectedUnits.Clear();
        FlowPrompt = null;
        MobilizationStep = MobilizationStep.ArrivalEncounter;

        _logger.LogInformation("Starting Arrival EventFlow for {CardId}.", PendingArrivalCard.Id);
        AdvanceFlow();
    }


    private void RevealArrivalCard()
    {
        if (_arrivalCardRevealed || PendingArrivalCard is null)
            return;

        Chronicle.Add(new(
            CalendarPhaseStamp("Arrival"),
            PendingArrivalCard.Setup,
            ChronicleTone.Narrative,
            PendingArrivalCard.Title));
        _activeChronicleEntryIndex = Chronicle.Count - 1;
        _arrivalCardRevealed = true;
    }

    private void BeginForcedMobilization()
    {
        AddNarrativeText("The Host is forced onward before it can make camp.");
        ResetActiveFlowState();
        PendingArrivalCard = null;
        _currentArrivalTribe = null;
        _arrivalCardRevealed = false;
        _activeChronicleEntryIndex = null;
        Phase = GamePhase.Mobilization;
        MobilizationStep = MobilizationStep.ExploreReady;
        BeginExploreInternal(true);
    }

    private static bool TryParseTribute(string text, out string resource, out int amount)
    {
        resource = string.Empty;
        amount = 0;
        var match = Regex.Match(text ?? string.Empty, @"(?i)(\d+)\s*([CFWSR])");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out amount))
            return false;

        resource = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "C" => "Coin",
            "F" => "Food",
            "W" => "Wood",
            "S" => "Stone",
            "R" => "Research",
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(resource);
    }

    private bool CanPayExactTribute(string resource, int amount)
        => ResourceValue(resource) >= amount;

    private void PayExactTribute(string resource, int amount)
    {
        if (IsPhysicalResource(resource))
            RemovePhysicalResource(resource, amount);
        else
            AdjustResource(resource, -amount);
    }

    private int? TributeFallbackCoinCost(string resource, int amount)
    {
        if (string.Equals(resource, "Coin", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(resource, "Research", StringComparison.OrdinalIgnoreCase))
            return 4;

        var market = Market.FirstOrDefault(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        return market is null ? null : amount * (market.BuyPrice + 1);
    }

    private string CanonicalTribeName(string name)
    {
        var normalized = NormalizeTribeName(name);
        return Tribes.FirstOrDefault(t => NormalizeTribeName(t.Name) == normalized)?.Name
            ?? name.Replace("The ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" Clan", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private void StartExploreFlow(ExploreCardState card)
    {
        if (_catalog is null)
        {
            _logger.LogWarning("Cannot resolve {CardId}: catalog is not initialized.", card.Id);
            return;
        }

        var flow = _catalog.GetFlowForSource(card.Id, "OnResolve");
        if (flow is null)
        {
            _logger.LogWarning("No EventFlow found for {CardId}.", card.Id);
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "Explore",
                "This Explore effect does not yet have compiled EventFlow.",
                [new("no-flow:continue", "Continue")]);
            _specialPrompt = "NoFlow";
            return;
        }

        _activeFlow = flow.Value;
        _activeNodeId = GetString(flow.Value, "start_node") ?? "N01";
        _activeFlowContext = FlowContext.Explore;
        _flowVars.Clear();
        _lastRollResults.Clear();
        _lastUnitTypeRolls.Clear();
        _lastRollText = null;
        _pendingSelectedUnits.Clear();
        FlowPrompt = null;

        _logger.LogInformation("Starting EventFlow for {CardId}.", card.Id);
        AdvanceFlow();
    }

    private void AdvanceFlow()
    {
        var guard = 0;
        while (_activeFlow is not null && !string.IsNullOrWhiteSpace(_activeNodeId) && FlowPrompt is null)
        {
            if (++guard > 250)
            {
                _logger.LogError("EventFlow runaway detected at node {NodeId}.", _activeNodeId);
                CompleteActiveFlow();
                return;
            }

            var node = CurrentFlowNode();
            if (node is null)
            {
                _logger.LogError("EventFlow node {NodeId} was not found.", _activeNodeId);
                CompleteActiveFlow();
                return;
            }

            var type = GetString(node.Value, "type") ?? string.Empty;
            switch (type)
            {
                case "End":
                    FlushPendingOutcomeNarrative();
                    CompleteActiveFlow();
                    return;

                case "Action":
                    if (!ExecuteActionNode(node.Value))
                        return;
                    break;

                case "Roll":
                    ExecuteRollNode(node.Value);
                    break;

                case "Check":
                    ExecuteCheckNode(node.Value);
                    break;

                case "Choice":
                    FlushPendingOutcomeNarrative();
                    PresentChoiceNode(node.Value);
                    return;

                case "Select":
                    FlushPendingOutcomeNarrative();
                    if (!PresentSelectNode(node.Value))
                        return;
                    break;

                case "Test":
                    FlushPendingOutcomeNarrative();
                    PresentTestNode(node.Value);
                    return;

                default:
                    _logger.LogWarning("Unsupported EventFlow node type {Type}.", type);
                    CompleteActiveFlow();
                    return;
            }
        }
    }

    private JsonElement? CurrentFlowNode()
    {
        if (_activeFlow is null || string.IsNullOrWhiteSpace(_activeNodeId))
            return null;

        if (!_activeFlow.Value.TryGetProperty("nodes", out var nodes)
            || !nodes.TryGetProperty(_activeNodeId, out var node))
            return null;

        return node;
    }

    private void PresentChoiceNode(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
        {
            CompleteActiveFlow();
            return;
        }

        var hostileEchoParley = _activeFlowContext == FlowContext.Camp
            && PendingCampCard is not null
            && IsHostileEcho(PendingCampCard)
            && (GetString(node, "prompt") ?? string.Empty).Contains("Parley", StringComparison.OrdinalIgnoreCase);
        var parleyResource = hostileEchoParley ? AdjacentTerrainResource() : string.Empty;

        var options = new List<FlowPromptOption>();
        var branchArray = branches.EnumerateArray().ToArray();
        for (var i = 0; i < branchArray.Length; i++)
        {
            var branch = branchArray[i];
            var label = ReplacePlaceholders(GetString(branch, "label") ?? "Choose");
            var firepotsBaggageChoice = _activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "SCENE-022", StringComparison.OrdinalIgnoreCase)
                && label.StartsWith("Take the Firepots", StringComparison.OrdinalIgnoreCase);
            var drillArmorUnavailable = _activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "CAMP-010", StringComparison.OrdinalIgnoreCase)
                && label.StartsWith("Armor Development", StringComparison.OrdinalIgnoreCase)
                && _armorSupply.Count == 0;
            var branchAllowed = BranchConditionAllowed(branch) && !drillArmorUnavailable;
            if (firepotsBaggageChoice)
                branchAllowed = branchAllowed && Baggage.Any(b => !b.Damaged && string.IsNullOrWhiteSpace(b.Contents));
            if (!branchAllowed && !hostileEchoParley && !firepotsBaggageChoice && !drillArmorUnavailable)
                continue;

            if (_activeFlowContext == FlowContext.Camp && !CampBranchAllowed(label) && !drillArmorUnavailable)
                continue;
            var detail = ReplacePlaceholders(GetString(branch, "notes") ?? string.Empty);
            if (drillArmorUnavailable)
                detail = "Unavailable: no Armor Developments remain in the R&R offer.";

            if (hostileEchoParley)
            {
                if (label.StartsWith("Pay 2 matching Resources", StringComparison.OrdinalIgnoreCase))
                {
                    var have = ResourceValue(parleyResource);
                    label = $"Pay 2 {parleyResource}";
                    detail = branchAllowed
                        ? $"You have {have} {parleyResource}. Then test Rapport 9+."
                        : $"Unavailable: requires 2 {parleyResource}; you have {have}.";
                }
                else if (label.StartsWith("Pay 5 Coin", StringComparison.OrdinalIgnoreCase))
                {
                    var have = ResourceValue("Coin");
                    detail = branchAllowed
                        ? $"You have {have} Coin. Then test Rapport 9+."
                        : $"Unavailable: requires 5 Coin; you have {have}.";
                }
                else if (string.Equals(label, "Decline", StringComparison.OrdinalIgnoreCase))
                {
                    detail = "Decline Parley. The tribe attacks.";
                }
            }
            else if (_activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "CAMP-012", StringComparison.OrdinalIgnoreCase))
            {
                detail = label switch
                {
                    "Train Scouts" => "Cost: 1 Research. Scouting: when Exploring, draw 2 Terrain cards and choose 1.",
                    "Appoint Camp Steward" => "Cost: 2 Coin. Camp Steward: during Work, you may instead gain 1 Resource matching the adjacent Terrain or buy 1 Resource for 1 Coin less than its Market Buy value.",
                    _ => detail
                };
            }
            else if (_activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "CAMP-003", StringComparison.OrdinalIgnoreCase))
            {
                detail = label switch
                {
                    "Chapel" => "Cost: 1 Wood + 1 Coin. Gain a Priest. Priest: optional 5–6 Proselytization before Combat, prevent 1 Winter Attrition loss, and improve the Finale Resolve Check. A Priest does not count against Host limits and cannot be a Combat casualty.",
                    "Academy" => "Cost: 1 Stone + 1 Coin. Whenever you gain Research, roll 1d6 for each Research gained; each 4–6 grants +1 additional Research.",
                    _ => detail
                };
            }
            else if (_activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "CAMP-004", StringComparison.OrdinalIgnoreCase))
            {
                detail = label switch
                {
                    "Market" => "Cost: 1 Wood + 1 Coin. During Harvest, you may sell up to 2 Resources purged from the Baggage Train. Market Day may also be used at a Player Controlled Settlement.",
                    "Baggage Cart" => "Cost: 1 Wood. Increase Baggage Train capacity by 2 slots.",
                    _ => detail
                };
            }
            else if (_activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "SCENE-023", StringComparison.OrdinalIgnoreCase))
            {
                detail = label switch
                {
                    "Take the Draught" => "Gain Painkiller. Painkiller: whenever a Military Unit or Levy would be lost, roll 1d6 for that loss; on 5–6, prevent it.",
                    "Refuse the Dose" => "Gain +1 Morale.",
                    _ => detail
                };
            }
            else if (_activeFlowContext == FlowContext.Camp
                && string.Equals(PendingCampCard?.Id, "SCENE-024", StringComparison.OrdinalIgnoreCase))
            {
                detail = label switch
                {
                    "Force the March" => "Gain Forced March, +1 Morale, and +1 Leadership. While Forced March is active, normal Explore costs 1 Time instead of 2.",
                    "Keep a Human Pace" => "No effect.",
                    _ => detail
                };
            }
            else if (firepotsBaggageChoice)
            {
                detail = branchAllowed
                    ? "Uses 1 empty Baggage Train space. Gain Firepots. Before the first Combat round, you may remove Firepots and roll 3d6; each 4+ inflicts 1 casualty as if from a Cavalry attack."
                    : "Unavailable: requires 1 empty, undamaged Baggage Train space.";
            }
            else if (string.Equals(label, "Attack", StringComparison.OrdinalIgnoreCase) && PendingArrivalCard is not null)
            {
                var enemy = CurrentArrivalEnemyHost();
                detail = string.IsNullOrWhiteSpace(detail) ? $"Enemy Host: {enemy}" : $"{detail} Enemy Host: {enemy}";
            }

            detail = DoNotChooseBlindDetail(label, detail);
            options.Add(new($"branch:{i}", label, string.IsNullOrWhiteSpace(detail) ? null : detail, !branchAllowed));
        }

        if (options.Count == 0)
        {
            _logger.LogWarning("Choice node {NodeId} has no legal branches.", _activeNodeId);
            CompleteActiveFlow();
            return;
        }

        var prompt = ReplacePlaceholders(GetString(node, "prompt") ?? "Choose");
        if (hostileEchoParley)
        {
            prompt = $"Parley? Tribute: 2 {parleyResource} or 5 Coin.";
            if (ResourceValue(parleyResource) < 2 && ResourceValue("Coin") < 5)
                prompt += " You cannot meet either demand.";
        }

        FlowPrompt = new(
            FlowPromptKind.Choice,
            ActiveFlowHeading(),
            prompt,
            options);
    }

    /// <summary>
    /// Supplies player-visible immediate consequences for story-labelled
    /// choices while deliberately omitting hidden downstream information.
    /// </summary>
    private string DoNotChooseBlindDetail(string label, string currentDetail)
    {
        if (currentDetail.StartsWith("Unavailable:", StringComparison.OrdinalIgnoreCase))
            return currentDetail;

        var sourceId = _activeFlowContext switch
        {
            FlowContext.Camp => PendingCampCard?.Id,
            FlowContext.Explore => PendingExploreCard?.Id,
            FlowContext.Arrival => PendingArrivalCard?.Id,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(sourceId))
            return currentDetail;

        // UX rule: choices should expose every immediate, player-knowable
        // mechanical consequence before selection. Hidden Echo seeding and
        // other concealed downstream information deliberately stay hidden.
        return (sourceId, label) switch
        {
            ("CAMP-001", "Gain 2 Coin") =>
                "Gain 2 Coin.",
            ("CAMP-001", "Gain 1 Coin and 1 Leadership") =>
                "Gain 1 Coin and 1 Leadership.",
            ("CAMP-001", "Gain adjacent Terrain Resource") =>
                $"Gain 1 {AdjacentTerrainResource()} matching the adjacent Terrain. Subject to Baggage Train capacity.",
            ("CAMP-001", "Buy 1 Resource at Market -1 Coin") =>
                "Buy 1 Resource for 1 Coin less than its current Market Buy value. Subject to Baggage Train capacity.",
            ("CAMP-001", "Repair 1 damaged Baggage Train space — Spend 1 Wood") =>
                "Spend 1 Wood. Repair 1 damaged Baggage Train space.",

            ("CAMP-002", "Market") =>
                "Buy and/or sell up to 2 Resources at current Market values. Purchases are subject to Baggage Train capacity.",
            ("CAMP-002", "Rest") =>
                "Gain 1 Morale.",

            ("CAMP-003", "Chapel") =>
                "Spend 1 Wood + 1 Coin. Gain a Priest. Priest: optional 5–6 Proselytization before Combat, prevent 1 Winter Attrition loss, and improve the Finale Resolve Check.",
            ("CAMP-003", "Academy") =>
                "Spend 1 Stone + 1 Coin. Whenever you gain Research, roll 1d6 for each Research gained; each 4–6 grants +1 additional Research.",

            ("CAMP-004", "Market") =>
                "Spend 1 Wood + 1 Coin. During Harvest, you may sell up to 2 Resources purged from the Baggage Train. Market Day may also be used at a Controlled Settlement.",
            ("CAMP-004", "Baggage Cart") =>
                "Spend 1 Wood. Increase Baggage Train capacity by 2 spaces.",

            ("CAMP-007", "Provision") =>
                "Spend 2 Coin to gain 1 Food, Wood, or Stone. You may spend 1 additional Coin to gain 1 additional Food, Wood, or Stone.",
            ("CAMP-007", "Rest") =>
                "Gain 1 Morale.",
            ("CAMP-007", "Buy 1 additional Resource") =>
                "Spend 1 additional Coin. Gain 1 additional Food, Wood, or Stone.",
            ("CAMP-007", "Done") =>
                "Finish Supply Convoy without buying another Resource.",

            ("CAMP-009", "Raise Levies") =>
                "Spend 1 Food. Add 2 Levy to your Host.",
            ("CAMP-009", "Rest") =>
                "Gain 1 Morale.",

            ("CAMP-010", "Purchase Advancement/Armor") =>
                "Purchase an Advancement or Armor Development for 1 less Research, to a minimum Research cost of 1. Pay all other printed costs. After Drill resolves, regain 1 Leadership.",
            ("CAMP-010", "Gain Research") =>
                "Gain 1 Research. You may then discard the top Tactics card for free. After Drill resolves, regain 1 Leadership.",
            ("CAMP-010", "Top Advancement card") =>
                "Purchase the top Advancement card for 1 less Research, to a minimum Research cost of 1. Pay all other printed costs.",
            ("CAMP-010", "Armor Development from R&R Offer") =>
                "Purchase an Armor Development from the R&R offer for 1 less Research, to a minimum Research cost of 1. Pay all other printed costs.",
            ("CAMP-010", "Yes") =>
                "Discard the top Tactics card for free, then reveal the next card.",
            ("CAMP-010", "No") =>
                "Keep the current top Tactics card.",

            ("CAMP-012", "Train Scouts") =>
                "Spend 1 Research. Gain Scouting: when Exploring, draw 2 Terrain cards and choose 1.",
            ("CAMP-012", "Appoint Camp Steward") =>
                "Spend 2 Coin. Gain Camp Steward: during Work, you may instead gain 1 Resource matching the adjacent Terrain or buy 1 Resource for 1 Coin less than its Market Buy value.",

            ("SCENE-020", "Send in a Fighter") =>
                "Choose 1 Military Unit, then roll 1d6. 1: lose that Unit; 2–3: lose 1 Morale; 4–5: gain 2 Coin; 6: gain 3 Coin and 1 Morale.",
            ("SCENE-020", "Refuse the Challenge") =>
                "Lose 1 Morale.",

            ("SCENE-022", "Take the Firepots") =>
                "Requires 1 empty, undamaged Baggage Train space. Gain Firepots. Before the first Combat round, you may remove it and roll 3d6; each 4+ assigns 1 casualty as if from a Cavalry attack.",
            ("SCENE-022", "Condemn the Stockpile") =>
                "Gain 1 Leadership and lose 1 Morale.",

            ("SCENE-023", "Take the Draught") =>
                "Gain Painkiller. Whenever a Military Unit or Levy would be lost from any game effect, roll 1d6 for that loss; on 5–6, prevent it.",
            ("SCENE-023", "Refuse the Dose") =>
                "Gain 1 Morale.",

            ("SCENE-024", "Force the March") =>
                "Gain Forced March, 1 Morale, and 1 Leadership. While Forced March is active, normal Explore costs 1 Time instead of 2.",
            ("SCENE-024", "Keep a Human Pace") =>
                "No immediate effect.",

            ("SCENE-026", "Let the Rite Continue") =>
                "Advance 1 Time.",
            ("SCENE-026", "Interrupt the Priest") =>
                "Gain 1 Leadership.",
            ("SCENE-026", "Let Your Priest Judge It") =>
                "Requires a Priest. Test 8+: pass, gain 1 Leadership and 1 Morale; fail, lose 1 Morale.",

            ("SCENE-027", "Laugh It Off") =>
                "Gain 1 Morale.",
            ("SCENE-027", "Set a Watch") =>
                "Gain 1 Leadership and lose 1 Morale.",
            ("SCENE-027", "Let Your Priest Name It") =>
                "Requires a Priest. Test 8+: pass, gain 1 Morale and 1 Leadership; fail, lose 1 Morale.",

            ("ECHO-U-02", "Dump the Firepots") =>
                "Remove Firepots from the Baggage Train.",
            ("ECHO-U-02", "Carry the Risk") =>
                "Keep Firepots. No immediate stat or Resource change.",

            ("ECHO-L-01", "Keep the Cadence") =>
                "Lose 1 Leadership and keep Forced March.",
            ("ECHO-L-01", "Call a Halt") =>
                "Advance 2 Time and remove Forced March.",

            ("ECHO-L-02", "Spend Discipline") =>
                "Lose 1 Leadership and keep Forced March.",
            ("ECHO-L-02", "Let Them Breathe") =>
                "Remove Forced March and gain 1 Morale.",

            ("ECHO-G-02", "Answer the Charge") =>
                "Lose 1 Leadership.",
            ("ECHO-G-02", "Make Restitution") =>
                "Pay 2 Coin.",
            ("ECHO-G-02", "Let Doctrine Speak") =>
                "Requires a Priest. Test 7+: pass, no further immediate effect; fail, lose 1 Morale.",

            ("EXPLORE-012", "Sample the fungi") =>
                "Choose 1 Unit, then roll 1d6. On 1, lose that Unit; otherwise gain 1 Food.",
            ("EXPLORE-012", "Leave them alone") =>
                "No effect.",

            ("EXPLORE-016", "Search") =>
                "Roll 1d6. On 6, lose 1 Infantry if able; otherwise gain Coin equal to the result.",
            ("EXPLORE-016", "Leave it") =>
                "No effect.",

            ("EXPLORE-017", "Defend in Combat") =>
                $"Fight a Hill Clan force of {Year} Infantry.",
            ("EXPLORE-017", "Flee") =>
                "Roll 1d6. On 5–6, negotiate safe passage. On 1–4, lose 1 Infantry or 1 Morale.",

            ("EXPLORE-020", "Loot") =>
                "Roll 1d6. 1: lose 1 Military Unit; 2: lose 2 Coin; 3: nothing; 4: gain 1 Food; 5: gain 1 Wood; 6: gain 1 Stone.",
            ("EXPLORE-020", "Leave it") =>
                "No effect.",

            ("EXPLORE-022", "Hear Petitions (6+)") =>
                "Test 6+ using the combined Rapport DRM from all tribes. Pass: gain 1 Leadership. Fail: no effect.",
            ("EXPLORE-022", "Broker Terms (7+)") =>
                "Test 7+ using the combined Rapport DRM from all tribes. Pass: gain 3 Coin. Fail: no effect.",
            ("EXPLORE-022", "Exploit the Discord (8+)") =>
                "Test 8+ using the combined Rapport DRM from all tribes. Pass: gain 6 Coin. Fail: lose 1 Rapport with every tribe; unmet tribes become Hostile.",

            ("EXPLORE-023", "Mark the Clear Road") =>
                "Your next Explore costs 1 less Time, to a minimum of 1. When its Explore card is revealed, you may ignore that card's effect; then remove this reminder.",
            ("EXPLORE-023", "Scout the Approach") =>
                "The next time you reveal a Clearing, gain 1 Leadership if it has a Barbarian icon; otherwise gain 1 Morale.",

            ("EXPLORE-028", "Dig") =>
                "Assign 1 Infantry and roll 1d6. On 4–6, gain Halberds or Longbows; on 1–3, lose the assigned Infantry.",
            ("EXPLORE-028", "Leave the dead alone") =>
                "No effect.",

            ("EXPLORE-029", "Search") =>
                "Roll 1d6. On 5–6, gain 1 Armor Advancement; on 1, lose 1d3 Units of your choice; on 2–4, no effect.",
            ("EXPLORE-029", "Leave it") =>
                "No effect.",

            ("EXPLORE-031", "Take What Remains") =>
                "Gain 3 Coin, 1 Leadership, and Reign in Blood. While Reign in Blood is active, your Host may not Disengage from Combat.",
            ("EXPLORE-031", "Bury the Dead") =>
                "Advance 1 Time.",

            _ => currentDetail
        };
    }

    /// <summary>
    /// Returns true when the Select node completed automatically and the flow
    /// can continue immediately. Returns false when player input is pending.
    /// </summary>
    private bool PresentSelectNode(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
            return true;

        var branch = branches.EnumerateArray().FirstOrDefault();
        if (branch.ValueKind == JsonValueKind.Undefined)
            return true;

        var input = GetString(branch, "input") ?? string.Empty;
        var variable = GetString(branch, "label") ?? "SelectedValue";
        var next = GetString(branch, "next");
        var prompt = GetString(node, "prompt") ?? "Choose";

        if (string.Equals(input, "HostUnits", StringComparison.OrdinalIgnoreCase)
            && TryGetMultiSelectCount(branch, out var count))
        {
            count = Math.Min(count, TotalHostUnits());
            if (count <= 0)
            {
                _flowVars[variable] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                AddNarrativeText("No Units are lost.");
                _activeNodeId = next;
                return true;
            }

            _pendingSelectedUnits.Clear();
            _pendingMultiSelectRemaining = count;
            _pendingMultiSelectVariable = variable;
            _pendingMultiSelectNextNode = next;
            BuildMultiSelectPrompt(prompt);
            return false;
        }

        var options = BuildSelectOptions(input);
        if (options.Count == 0)
        {
            _logger.LogInformation("Select node {NodeId} has no eligible choices.", _activeNodeId);
            _flowVars[variable] = null;
            _activeNodeId = next;
            return true;
        }

        _pendingSelectVariable = variable;
        _pendingSelectNextNode = next;
        FlowPrompt = new(
            FlowPromptKind.Select,
            ActiveFlowHeading(),
            ReplacePlaceholders(prompt),
            options);
        return false;
    }

    private List<FlowPromptOption> BuildSelectOptions(string input)
    {
        var options = new List<FlowPromptOption>();

        if (string.Equals(input, "Integer0..3", StringComparison.OrdinalIgnoreCase))
        {
            var max = Math.Min(3, ResourceValue("Coin"));
            for (var i = 0; i <= max; i++)
            {
                var detail = i == 0
                    ? "Recruit no Mercenaries."
                    : $"Spend {i} Coin. Add {i} Mercenar{(i == 1 ? "y" : "ies")} to your Host.";
                options.Add(new($"select:{i}", i == 0 ? "Recruit none" : $"Recruit {i}", detail));
            }
            return options;
        }

        if (string.Equals(input, "HostileTribes", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var tribe in Tribes.Where(t => t.Rapport == "Hostile"))
            {
                var detail = _activeFlowContext == FlowContext.Camp
                    && string.Equals(PendingCampCard?.Id, "CAMP-011", StringComparison.OrdinalIgnoreCase)
                        ? "Test 6+ with no Hostile DRM. Leadership may not be spent. Pass: gain 1 Rapport and 1 Morale."
                        : null;
                options.Add(new($"select:{tribe.Name}", tribe.Name, detail));
            }
            return options;
        }

        if (input.StartsWith("TribesByRapport:", StringComparison.OrdinalIgnoreCase))
        {
            var allowed = input[(input.IndexOf(':') + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tribe in Tribes.Where(t => allowed.Contains(t.Rapport, StringComparer.OrdinalIgnoreCase)))
            {
                var detail = tribe.Rapport;
                if (_activeFlowContext == FlowContext.Camp
                    && string.Equals(PendingCampCard?.Id, "CAMP-013", StringComparison.OrdinalIgnoreCase))
                {
                    detail = string.Equals(tribe.Rapport, "Friendly", StringComparison.OrdinalIgnoreCase)
                        ? "Friendly • +1 DRM. Test 6+; Leadership may not be spent. Pass: gain 2 Coin and 2 Leadership."
                        : "Neutral • +0 DRM. Test 6+; Leadership may not be spent. Pass: gain 2 Coin, 1 Leadership, and 1 Rapport.";
                }
                options.Add(new($"select:{tribe.Name}", tribe.Name, detail));
            }
            return options;
        }

        if (string.Equals(input, "Host.MilitaryUnits", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var unit in Host.Where(u => u.Count > 0 && u.Type is "Archers" or "Cavalry" or "Infantry"))
                options.Add(new($"select:{unit.Type}", unit.Type, $"{unit.Count} in Host"));
            return options;
        }

        if (string.Equals(input, "HostInfantryClassUnits", StringComparison.OrdinalIgnoreCase))
        {
            var infantry = HostCount("Infantry");
            if (infantry > 0)
                options.Add(new("select:Infantry", "Infantry", $"{infantry} in Host"));

            var mercenaries = HostCount("Mercenaries");
            if (mercenaries > 0)
                options.Add(new("select:Mercenaries", mercenaries == 1 ? "Mercenary" : "Mercenaries", $"{mercenaries} Infantry-class Mercenaries in Host"));

            return options;
        }

        if (input.StartsWith("AvailableTokens:", StringComparison.OrdinalIgnoreCase))
        {
            var names = input[(input.IndexOf(':') + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in names.Where(HasToken))
                options.Add(new($"select:{name}", DisplayTokenName(name)));
            return options;
        }

        if (input.StartsWith("AdjacentTo:FirepotsSlot", StringComparison.OrdinalIgnoreCase))
        {
            var firePosition = ToInt(_flowVars.TryGetValue("FirepotsSlot", out var storedFireSlot) ? storedFireSlot : 0);
            if (firePosition <= 0)
                firePosition = Baggage.FirstOrDefault(b => string.Equals(b.Contents, "Firepots", StringComparison.OrdinalIgnoreCase))?.Position ?? 0;

            if (firePosition > 0)
            {
                // The Baggage Train is displayed as a two-column grid:
                //   1 2
                //   3 4
                //   5 6
                // "Adjacent" means sharing an edge, not merely being next in list order.
                var fireRow = (firePosition - 1) / 2;
                var fireCol = (firePosition - 1) % 2;

                foreach (var slot in Baggage
                    .Where(b => b.Position != firePosition)
                    .Where(b =>
                    {
                        var row = (b.Position - 1) / 2;
                        var col = (b.Position - 1) % 2;
                        return Math.Abs(row - fireRow) + Math.Abs(col - fireCol) == 1;
                    })
                    .OrderBy(b => b.Position))
                {
                    options.Add(new($"select:{slot.Position}", $"Space {slot.Position}", BaggageContentsLabel(slot)));
                }
            }
            return options;
        }

        if (string.Equals(input, "ViewedCampCards", StringComparison.OrdinalIgnoreCase))
        {
            if (_viewedCampCards.Count == 0)
                return options;

            if (_viewedCampCards.Count == 1)
            {
                var card = _viewedCampCards[0];
                options.Add(new("select:__KEEP_ORIGINAL__", $"Keep {card.Title}", "Return it to the top of the Camp deck."));
                options.Add(new($"select:{card.Id}", $"Discard {card.Title}"));
                return options;
            }

            var first = _viewedCampCards[0];
            var second = _viewedCampCards[1];
            options.Add(new("select:__KEEP_ORIGINAL__", "Keep both, current order", $"{first.Title} → {second.Title}"));
            options.Add(new("select:__KEEP_REVERSED__", "Keep both, reverse order", $"{second.Title} → {first.Title}"));
            options.Add(new($"select:{first.Id}", $"Discard {first.Title}", $"Return {second.Title} to the top."));
            options.Add(new($"select:{second.Id}", $"Discard {second.Title}", $"Return {first.Title} to the top."));
            options.Add(new($"select:{first.Id}|{second.Id}", "Discard both", "Return neither card to the deck."));
            return options;
        }

        if (string.Equals(input, "MarketBuyableResources", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var market in Market.Where(m => CanMarketBuy(m.Resource, 1)))
                options.Add(new($"select:{market.Resource}", market.Resource, $"{market.BuyPrice} Coin"));
            return options;
        }

        if (string.Equals(input, "SupplyConvoyResources", StringComparison.OrdinalIgnoreCase))
        {
            var coinCost = string.Equals(_activeNodeId, "N20", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            foreach (var resource in new[] { "Food", "Wood", "Stone" })
            {
                if (CanAddPhysicalResource(resource, 1))
                    options.Add(new($"select:{resource}", resource, $"Spend {coinCost} Coin. Gain 1 {resource}."));
            }
            return options;
        }

        if (string.Equals(input, "MarketSellableResources", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var market in Market.Where(m => ResourceValue(m.Resource) > 0))
                options.Add(new($"select:{market.Resource}", market.Resource, $"Gain {market.SellPrice} Coin"));
            return options;
        }

        if (string.Equals(input, "HostUnitTypes", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var unit in Host.Where(u => u.Count > 0))
                options.Add(new($"select:{unit.Type}", unit.Type, $"{unit.Count} in Host"));
            return options;
        }

        if (string.Equals(input, "HostMilitaryUnitTypes", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var unit in Host.Where(u => u.Count > 0 && !string.Equals(u.Type, "Levy", StringComparison.OrdinalIgnoreCase)))
                options.Add(new($"select:{unit.Type}", unit.Type, $"{unit.Count} in Host"));
            return options;
        }

        if (string.Equals(input, "UndamagedBaggageSlots", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in Baggage.Where(b => !b.Damaged))
                options.Add(new($"select:{slot.Position}", $"Space {slot.Position}", string.IsNullOrWhiteSpace(slot.Contents) ? "Empty" : BaggageContentsLabel(slot)));
            return options;
        }

        if (string.Equals(input, "DamagedBaggageSlots", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in Baggage.Where(b => b.Damaged))
            {
                var detail = _activeFlowContext == FlowContext.Camp
                    && string.Equals(PendingCampCard?.Id, "CAMP-001", StringComparison.OrdinalIgnoreCase)
                        ? "Spend 1 Wood. Repair this damaged Baggage Train space."
                        : "Damaged";
                options.Add(new($"select:{slot.Position}", $"Space {slot.Position}", detail));
            }
            return options;
        }

        if (input.StartsWith("AvailableAdvancements:", StringComparison.OrdinalIgnoreCase))
        {
            var ids = input[(input.IndexOf(':') + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var id in ids.Where(IsAdvancementAvailable))
            {
                var advancement = _catalog?.GetAdvancement(id);
                if (advancement is not null)
                    options.Add(new($"select:{id}", advancement.Name, advancement.Effect));
            }
            return options;
        }

        if (input.StartsWith("AffordableCount0..2:CoinPer=", StringComparison.OrdinalIgnoreCase))
        {
            var raw = input[(input.LastIndexOf('=') + 1)..];
            var coinPer = int.TryParse(raw, out var parsed) ? parsed : 1;
            var max = Math.Min(2, coinPer <= 0 ? 2 : ResourceValue("Coin") / coinPer);
            for (var i = 0; i <= max; i++)
                options.Add(new($"select:{i}", i == 0 ? "Recruit none" : $"Recruit {i}", i == 0 ? null : $"{i * coinPer} Coin"));
            return options;
        }

        return options;
    }

    private void BuildMultiSelectPrompt(string basePrompt)
    {
        var options = Host
            .Where(u => u.Count - (_pendingSelectedUnits.TryGetValue(u.Type, out var selected) ? selected : 0) > 0)
            .Select(u => new FlowPromptOption(
                $"multi:{u.Type}",
                u.Type,
                $"{u.Count - (_pendingSelectedUnits.TryGetValue(u.Type, out var selected) ? selected : 0)} available"))
            .ToArray();

        if (options.Length == 0)
        {
            FinishMultiSelect();
            return;
        }

        FlowPrompt = new(
            FlowPromptKind.Select,
            ActiveFlowHeading(),
            $"{ReplacePlaceholders(basePrompt)} ({_pendingMultiSelectRemaining} remaining)",
            options);
    }

    private void ResolveMultiSelectPrompt(string optionId)
    {
        if (!optionId.StartsWith("multi:", StringComparison.OrdinalIgnoreCase))
            return;

        var type = optionId[6..];
        var unit = Host.FirstOrDefault(u => string.Equals(u.Type, type, StringComparison.OrdinalIgnoreCase));
        if (unit is null)
            return;

        var already = _pendingSelectedUnits.TryGetValue(type, out var selected) ? selected : 0;
        if (already >= unit.Count)
            return;

        _pendingSelectedUnits[type] = already + 1;
        _pendingMultiSelectRemaining--;

        if (_pendingMultiSelectRemaining <= 0)
            FinishMultiSelect();
        else
            BuildMultiSelectPrompt(FlowPrompt?.Prompt.Split('(')[0].Trim() ?? "Choose Units to lose");

        Changed?.Invoke();
    }

    private void FinishMultiSelect()
    {
        if (!string.IsNullOrWhiteSpace(_pendingMultiSelectVariable))
            _flowVars[_pendingMultiSelectVariable] = new Dictionary<string, int>(_pendingSelectedUnits, StringComparer.OrdinalIgnoreCase);

        FlowPrompt = null;
        _activeNodeId = _pendingMultiSelectNextNode;
        _pendingMultiSelectRemaining = 0;
        _pendingMultiSelectVariable = null;
        _pendingMultiSelectNextNode = null;
        AdvanceFlow();
    }

    private bool TryGetMultiSelectCount(JsonElement branch, out int count)
    {
        count = 0;
        foreach (var flag in GetFlags(branch))
        {
            var name = GetString(flag, "name");
            if (string.Equals(name, "CountFromRollResult", StringComparison.OrdinalIgnoreCase))
            {
                count = LastNumericRollResult();
                return true;
            }

            if (string.Equals(name, "CountFromRollRange", StringComparison.OrdinalIgnoreCase))
            {
                var range = ScalarText(flag, "value") ?? "1..2";
                count = CountRollsInRange(range);
                return true;
            }
        }
        return false;
    }

    private void ExecuteRollNode(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
        {
            CompleteActiveFlow();
            return;
        }

        var first = branches.EnumerateArray().FirstOrDefault();
        var input = first.ValueKind == JsonValueKind.Undefined ? "1d6" : GetString(first, "input") ?? "1d6";

        _lastRollResults.Clear();
        _lastUnitTypeRolls.Clear();
        _lastRollText = null;

        switch (input)
        {
            case "1d6":
                _lastRollResults.Add(Random.Shared.Next(1, 7));
                break;
            case "2d6":
                _lastRollResults.Add(Random.Shared.Next(1, 7));
                _lastRollResults.Add(Random.Shared.Next(1, 7));
                break;
            case "1d3":
                _lastRollResults.Add(Random.Shared.Next(1, 4));
                break;
            case "1d6PerHostInfantry":
                for (var i = 0; i < HostCount("Infantry"); i++)
                    _lastRollResults.Add(Random.Shared.Next(1, 7));
                break;
            case "1d6PerHostUnitType":
                foreach (var type in new[] { "Levy", "Archers", "Infantry", "Cavalry" })
                {
                    if (HostCount(type) <= 0)
                        continue;
                    var result = Random.Shared.Next(1, 7);
                    _lastUnitTypeRolls[type] = result;
                    _lastRollResults.Add(result);
                }
                break;
            case "ResourceVulnerabilityDie":
                // The Market Vulnerability die has one + and one - face for each
                // Resource. This card only cares which Resource icon was rolled,
                // so Food, Wood, and Stone are equally likely and the market is
                // deliberately left unchanged.
                _lastRollText = new[] { "Food", "Wood", "Stone" }[Random.Shared.Next(3)];
                break;
            default:
                _logger.LogWarning("Unsupported roll input {Input}; using 1d6 fallback.", input);
                _lastRollResults.Add(Random.Shared.Next(1, 7));
                break;
        }

        LogRoll(input);

        var matchedBranch = FindMatchingBranch(node, RollComparableValue()) ?? FindAlwaysBranch(node);
        if (matchedBranch is not null)
        {
            if (_activeFlowContext == FlowContext.Camp && string.IsNullOrWhiteSpace(GetString(matchedBranch.Value, "player_text")))
                AddCampRollNarrative(RollComparableValue());
            AddPlayerText(matchedBranch.Value);
            _activeNodeId = GetString(matchedBranch.Value, "next");
        }
        else
        {
            _activeNodeId = null;
        }
    }

    private void AddCampRollNarrative(object? result)
    {
        if (PendingCampCard is null)
            return;

        var n = ToInt(result);

        // Preserve the printed card outcome, then let the browser add a little
        // atmosphere after the mechanical actions have resolved.
        if (PendingCampCard.Id == "SCENE-014")
        {
            var canonical = n switch
            {
                1 => "Your host shakes their head in disapproval.",
                2 => "Leave the tavern with your reputation intact.",
                _ => null
            };

            var flavor = n switch
            {
                1 => PickNarrative(
                    "The evening turns sour. By the time the Host files out, even the mugs seem to be judging you.",
                    "Whatever seemed funny an hour ago no longer survives the walk back to camp. The Host leaves the tavern under a cloud.",
                    "A few bad throws become a very long night. No one volunteers to recount the details on the march tomorrow."),
                2 => PickNarrative(
                    "The night ends without profit, scandal, or anyone needing to be carried back to camp. That counts as a respectable result.",
                    "The dice are put away before fortune finds a reason to become cruel. The Host departs with dignity mostly intact."),
                3 => PickNarrative(
                    "A modest run of luck leaves a little silver on your side of the table.",
                    "For once, the dice pay for the drinks rather than the other way around."),
                4 => PickNarrative(
                    "The table begins to lean your way, and confidence returns with the coin.",
                    "A cheer goes up as the Host finally finds a streak worth remembering."),
                5 => PickNarrative(
                    "Now the room is watching. The purse grows heavier and the Host starts believing the night has chosen a favorite.",
                    "The dice keep landing kindly, which is exactly when sensible people consider leaving."),
                6 => PickNarrative(
                    "The table erupts. For a few glorious minutes the Host owns the room, the dice, and most of the loose coin in it.",
                    "Fortune arrives loudly and with witnesses. Even the barkeep seems impressed."),
                _ => null
            };

            QueueOutcomeNarrative(canonical, flavor);
            return;
        }

        var text = PendingCampCard.Id switch
        {
            "SCENE-015" when n == 1 => "You misjudge the mood of the fair.",
            "SCENE-015" when n == 2 => "No one is satisfied, but the dispute ends without further consequence.",
            "SCENE-015" when n >= 3 => "Your judgment wins some measure of approval.",
            "SCENE-016" when n >= 8 => "The fire is put out in time for grateful eyes to see it.",
            "SCENE-016" => "The flame is stamped out before anyone even notices.",
            "SCENE-018" when n >= 6 => "The cart is righted, and the family parts from the Host with gratitude plain on every face.",
            "SCENE-018" => "The cart is righted all the same, but the help is taken more as necessity than kindness.",
            "SCENE-020" when n == 1 => "The wager ends brutally for your fighter.",
            "SCENE-020" when n <= 3 => "Your fighter leaves the ring beaten and the Host feels the loss.",
            "SCENE-020" when n <= 5 => "Your fighter carries the wager and earns a purse for the Host.",
            "SCENE-020" => "Your fighter dominates the ring, and the camp erupts around the victory.",
            "ECHO-Q-03" when n <= 2 => "They catch your Host off guard and muddy the truth before witnesses. Lose 1 Leadership.",
            "ECHO-Q-03" when n <= 4 => "The accusation comes to nothing, but leaves a sour taste. No effect.",
            "ECHO-Q-03" => "Your Host turns the confrontation back on them and comes away looking sharper. Gain 1 Leadership.",
            "SCENE-025" when n == 1 => "’Orra goes red with fury and lands a big, meaty fist in your face. Lose 1 Morale.",
            "SCENE-025" when n == 2 => "’Orra spits and gestures sharply. You appear to owe him something. Lose 1 Coin.",
            "SCENE-025" when n == 3 => "No one understands anyone. No effect.",
            "SCENE-025" when n == 4 => "’Orra nods as if a serious matter has been settled. Gain 1 Leadership.",
            "SCENE-025" when n == 5 => "’Orra roars with laughter and claps you on the shoulder. Gain 1 Coin and 1 Leadership.",
            "SCENE-025" => "’Orra declares you a person of rare wisdom. Apparently. Gain 2 Coin and 1 Morale.",
            "ECHO-M-01" => $"The restitution is set at {n} Coin.",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(text))
            AddNarrativeText(text);
    }

    private void LogRoll(string input)
    {
        if (!string.IsNullOrWhiteSpace(_lastRollText))
        {
            _logger.LogInformation("EventFlow roll {Input}: {Result}.", input, _lastRollText);
            AddSystemText($"Roll: {_lastRollText}.");
            return;
        }

        if (input == "1d6PerHostUnitType" && _lastUnitTypeRolls.Count > 0)
        {
            var detail = string.Join(", ", _lastUnitTypeRolls.Select(kv => $"{kv.Key} {kv.Value}"));
            _logger.LogInformation("EventFlow rolls {Input}: {Results}.", input, detail);
            AddSystemText($"Rolls: {detail}.");
            return;
        }

        var resultText = _lastRollResults.Count == 0 ? "no dice" : string.Join(", ", _lastRollResults);
        _logger.LogInformation("EventFlow roll {Input}: {Result}.", input, resultText);

        if (_lastRollResults.Count == 0)
            return;

        if (input == "2d6" && _lastRollResults.Count >= 2)
        {
            var total = _lastRollResults.Take(2).Sum();
            AddSystemText($"Roll: {_lastRollResults[0]} + {_lastRollResults[1]} = {total}.");
        }
        else if (input == "1d3" && _lastRollResults.Count == 1)
        {
            AddSystemText($"D3 roll: {_lastRollResults[0]}.");
        }
        else if (_lastRollResults.Count == 1)
        {
            AddSystemText($"Roll: {_lastRollResults[0]}.");
        }
        else
        {
            AddSystemText($"Rolls: {string.Join(", ", _lastRollResults)}.");
        }
    }

    private void ExecuteCheckNode(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
        {
            CompleteActiveFlow();
            return;
        }

        foreach (var branch in branches.EnumerateArray())
        {
            if (BranchConditionAllowed(branch))
            {
                _activeNodeId = GetString(branch, "next");
                return;
            }
        }

        _logger.LogWarning("Check node {NodeId} had no matching branch.", _activeNodeId);
        CompleteActiveFlow();
    }

    private void PresentTestNode(JsonElement node)
    {
        var prompt = GetString(node, "prompt") ?? "Test";
        var drm = TestDrm(node);
        var detail = drm == 0 ? null : $"Rapport DRM: {(drm > 0 ? "+" : string.Empty)}{drm}";

        if (_activeFlowContext == FlowContext.Camp
            && string.Equals(PendingCampCard?.Id, "CAMP-011", StringComparison.OrdinalIgnoreCase))
        {
            detail = "Leadership may not be spent. Pass: gain 1 Rapport and 1 Morale.";
        }
        else if (_activeFlowContext == FlowContext.Camp
            && string.Equals(PendingCampCard?.Id, "CAMP-013", StringComparison.OrdinalIgnoreCase))
        {
            var drmText = drm >= 0 ? $"+{drm}" : drm.ToString();
            detail = $"Rapport DRM: {drmText}. Leadership may not be spent. Pass: gain 2 Coin and 1 Leadership; if Friendly, gain 1 additional Leadership, otherwise gain 1 Rapport.";
        }

        var options = new List<FlowPromptOption>
        {
            new("test:normal", "Roll 2d6", detail)
        };

        if (Leadership > 0 && !NodeHasFlag(node, "NoLeadership"))
            options.Add(new("test:leadership", "Spend 1 Leadership", "Roll 3d6 and keep the best 2"));

        FlowPrompt = new(
            FlowPromptKind.Test,
            ActiveFlowHeading(),
            prompt,
            options);
    }

    private void ResolveTestPrompt(string optionId)
    {
        var node = CurrentFlowNode();
        if (node is null)
            return;

        var useLeadership = optionId == "test:leadership"
            && Leadership > 0
            && !NodeHasFlag(node.Value, "NoLeadership");
        var primary1 = Random.Shared.Next(1, 7);
        var primary2 = Random.Shared.Next(1, 7);
        var dice = new List<int> { primary1, primary2 };
        if (useLeadership)
        {
            Leadership--;
            dice.Add(Random.Shared.Next(1, 7));
        }

        var kept = dice.OrderByDescending(x => x).Take(2).ToArray();
        var drm = TestDrm(node.Value);
        var total = kept.Sum() + drm;
        var criticalFailure = primary1 == 1 && primary2 == 1;
        var criticalSuccess = primary1 == 6 && primary2 == 6;

        var diceText = string.Join(", ", dice);
        var resultText = drm == 0 ? total.ToString() : $"{kept.Sum()} {(drm > 0 ? "+" : string.Empty)}{drm} = {total}";
        var criticalText = criticalFailure ? " Critical Failure." : criticalSuccess ? " Critical Success." : string.Empty;
        _logger.LogInformation("EventFlow test roll: {Dice}. Result {Result}.{Critical}", diceText, resultText, criticalText.Trim());
        AddSystemText(useLeadership
            ? $"Roll: {diceText} (keep best 2){(drm == 0 ? string.Empty : $", DRM {(drm > 0 ? "+" : string.Empty)}{drm}")} = {total}.{criticalText}"
            : $"Roll: {diceText.Replace(", ", " + ")}{(drm == 0 ? string.Empty : $" {(drm > 0 ? "+" : string.Empty)}{drm}")} = {total}.{criticalText}");

        _lastRollResults.Clear();
        _lastRollResults.AddRange(dice);
        _flowVars["Test.Total"] = total;
        _flowVars["Test.CriticalFailure"] = criticalFailure;
        _flowVars["Test.CriticalSuccess"] = criticalSuccess;

        string? next = null;
        JsonElement? matchedBranch = null;
        if (node.Value.TryGetProperty("branches", out var branches))
        {
            if (criticalFailure)
            {
                var branch = branches.EnumerateArray()
                    .FirstOrDefault(b => (GetString(b, "label") ?? string.Empty).Contains("Fail", StringComparison.OrdinalIgnoreCase));
                if (branch.ValueKind != JsonValueKind.Undefined)
                    matchedBranch = branch;
            }
            else if (criticalSuccess)
            {
                var branch = branches.EnumerateArray()
                    .FirstOrDefault(b => (GetString(b, "label") ?? string.Empty).Contains("Pass", StringComparison.OrdinalIgnoreCase));
                if (branch.ValueKind != JsonValueKind.Undefined)
                    matchedBranch = branch;
            }
            else
            {
                matchedBranch = FindMatchingBranch(node.Value, total);
            }

            if (matchedBranch is not null)
            {
                var branchLabel = GetString(matchedBranch.Value, "label") ?? string.Empty;
                var passed = branchLabel.Contains("Pass", StringComparison.OrdinalIgnoreCase);
                var failed = branchLabel.Contains("Fail", StringComparison.OrdinalIgnoreCase);
                var authoredPlayerText = GetString(matchedBranch.Value, "player_text");
                var authoredOutcome = AuthoredTestOutcomeNarrative(passed, failed, criticalSuccess, criticalFailure);
                var pendingTestLabel = _pendingTestNarrativeLabel;
                if (_activeFlowContext == FlowContext.Arrival && !string.IsNullOrWhiteSpace(authoredPlayerText))
                {
                    AddTestOutcomeNarrative(passed, failed, criticalSuccess, criticalFailure);
                    QueueOutcomeNarrative(null, ArrivalTestFlavor(PendingArrivalCard?.Id, pendingTestLabel, passed || criticalSuccess, authoredPlayerText));
                }
                else if (!string.IsNullOrWhiteSpace(authoredPlayerText))
                    AddPlayerText(matchedBranch.Value);
                else if (!string.IsNullOrWhiteSpace(authoredOutcome))
                    AddNarrativeText(authoredOutcome);
                else
                    AddTestOutcomeNarrative(passed, failed, criticalSuccess, criticalFailure);

                if (_activeFlowContext == FlowContext.Arrival && NodeHasFlag(node.Value, "ArrivalCriticalRapport"))
                {
                    if (criticalSuccess)
                        AdjustRapport("CurrentTribe", 1);
                    else if (criticalFailure)
                        AdjustRapport("CurrentTribe", -1);
                }
                next = GetString(matchedBranch.Value, "next");
            }
        }

        FlowPrompt = null;
        _activeNodeId = next;
        AdvanceFlow();
        Changed?.Invoke();
    }

    private void RecordChoiceNarrative(JsonElement branch)
    {
        if (!string.IsNullOrWhiteSpace(GetString(branch, "player_text")))
            return;

        var label = ReplacePlaceholders(GetString(branch, "label") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(label) || label is "Yes" or "No")
            return;

        label = Regex.Replace(label, @"\s*\(\d+\+\)\s*$", string.Empty).Trim();
        AddNarrativeText($"You choose: {label}.");
    }

    private void PrepareTestNarrative(JsonElement branch, string? nextNodeId)
    {
        _pendingTestNarrativeLabel = null;
        if (string.IsNullOrWhiteSpace(nextNodeId) || _activeFlow is null)
            return;

        if (!_activeFlow.Value.TryGetProperty("nodes", out var nodes)
            || !nodes.TryGetProperty(nextNodeId, out var nextNode)
            || !string.Equals(GetString(nextNode, "type"), "Test", StringComparison.OrdinalIgnoreCase))
            return;

        var label = ReplacePlaceholders(GetString(branch, "label") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(label))
            return;

        // Remove a trailing target number so the sentence reads naturally:
        // "Take the Seed Grain (7+)" -> "Take the Seed Grain".
        label = Regex.Replace(label, @"\s*\(\d+\+\)\s*$", string.Empty).Trim();
        _pendingTestNarrativeLabel = label;
    }

    private string? AuthoredTestOutcomeNarrative(bool passed, bool failed, bool criticalSuccess, bool criticalFailure)
    {
        var sourceId = _activeFlowContext switch
        {
            FlowContext.Camp => PendingCampCard?.Id,
            FlowContext.Explore => PendingExploreCard?.Id,
            FlowContext.Arrival => PendingArrivalCard?.Id,
            _ => null
        };

        var isPass = passed || criticalSuccess;
        var isFail = failed || criticalFailure;
        return (sourceId, isPass, isFail) switch
        {
            ("ECHO-B-02", true, _) => "The matter settles.",
            ("ECHO-U-01", _, true) => "BANG! BANG!",
            _ => null
        };
    }

    private void AddTestOutcomeNarrative(bool passed, bool failed, bool criticalSuccess, bool criticalFailure)
    {
        var resultWord = criticalSuccess ? "a critical success"
            : criticalFailure ? "a critical failure"
            : passed ? "success"
            : failed ? "failure"
            : "result";

        if (_activeFlowContext == FlowContext.Camp && PendingCampCard is not null && IsHostileEcho(PendingCampCard))
        {
            AddNarrativeText(passed || criticalSuccess ? "The parley succeeds." : "The parley fails.");
            _pendingTestNarrativeLabel = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingTestNarrativeLabel))
        {
            var label = _pendingTestNarrativeLabel;
            _pendingTestNarrativeLabel = null;
            var sentence = criticalSuccess
                ? $"You attempted to {label}, and achieved a critical success."
                : criticalFailure
                    ? $"You attempted to {label}, but suffered a critical failure."
                    : passed
                        ? $"You attempted to {label}, and succeeded."
                        : failed
                            ? $"You attempted to {label}, but failed."
                            : $"You attempted to {label}.";
            AddNarrativeText(sentence);
            return;
        }

        AddNarrativeText($"The test ends in {resultWord}.");
    }

    private int TestDrm(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
            return 0;

        var flags = branches.EnumerateArray().SelectMany(GetFlags).ToArray();
        if (flags.Any(f => string.Equals(GetString(f, "name"), "IgnoreHostileRapportDRM", StringComparison.OrdinalIgnoreCase)))
            return 0;
        if (flags.Any(f => string.Equals(GetString(f, "name"), "CurrentTribeRapportDRM", StringComparison.OrdinalIgnoreCase)))
            return CurrentTribeRapportDrm();
        if (flags.Any(f => string.Equals(GetString(f, "name"), "ApplyRapportDRM", StringComparison.OrdinalIgnoreCase)))
        {
            var selected = _flowVars.TryGetValue("SelectedTribe", out var tribe) ? Convert.ToString(tribe) : null;
            if (!string.IsNullOrWhiteSpace(selected))
                return RapportDrm(GetTribeRapport(selected!));
            return CurrentTribeRapportDrm();
        }

        return flags.Any(f => string.Equals(GetString(f, "name"), "CombinedRapportDRM", StringComparison.OrdinalIgnoreCase))
            ? Tribes.Sum(t => RapportDrm(t.Rapport))
            : 0;
    }

    private static bool NodeHasFlag(JsonElement node, string name)
        => node.TryGetProperty("branches", out var branches)
            && branches.EnumerateArray().SelectMany(GetFlags)
                .Any(f => string.Equals(GetString(f, "name"), name, StringComparison.OrdinalIgnoreCase));

    private bool ExecuteActionNode(JsonElement node)
    {
        if (!node.TryGetProperty("action", out var action))
        {
            _activeNodeId = GetString(node, "next");
            return true;
        }

        var type = GetString(action, "type") ?? string.Empty;
        var target = ResolveTarget(GetString(action, "target"));
        var value = ResolveActionValue(action.TryGetProperty("value", out var rawValue) ? rawValue : default);
        var flags = GetFlags(node).ToArray();
        var next = GetString(node, "next");

        if (_skipNextDisengagePenaltyAction && string.Equals(type, "Lose", StringComparison.OrdinalIgnoreCase))
        {
            _skipNextDisengagePenaltyAction = false;
            AddNarrativeText("Feigned Withdrawal avoids the normal Disengage penalty.");
            _activeNodeId = next;
            return true;
        }

        // Narrative framing belongs before the state mutation so the Chronicle
        // reads like an event rather than an engine trace followed by prose.
        AddPlayerText(node);

        switch (type)
        {
            case "Gain":
                if (string.Equals(target, "Resources", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BeginResourceBundleGain(Convert.ToString(value) ?? string.Empty, ResourceGainResume.Flow, next))
                        return false;
                }
                else if (IsPhysicalResource(target))
                {
                    var physicalGains = CollectConsecutivePhysicalResourceGains(target, ToInt(value), ref next);
                    if (!BeginPhysicalResourceGain(physicalGains, ResourceGainResume.Flow, next))
                        return false;
                }
                else
                {
                    ApplyGain(target, ToInt(value));
                }
                break;
            case "Lose":
                ApplyLose(target, value, flags);
                break;
            case "Spend":
                if (string.Equals(target, "Resources", StringComparison.OrdinalIgnoreCase))
                    ApplyResourceBundle(Convert.ToString(value) ?? string.Empty, gain: false, flags);
                else
                    ApplySpend(target, ToInt(value), flags);
                break;
            case "MarketBuy":
                if (_activeFlowContext == FlowContext.Camp && string.Equals(target, "Resource", StringComparison.OrdinalIgnoreCase))
                {
                    PresentCampStewardBuy(next);
                    return false;
                }
                MarketBuy(target, ToInt(value), FlagInt(flags, "CoinModifier", 0));
                break;
            case "MarketSell":
                MarketSell(target, ToInt(value));
                break;
            case "PlaceToken":
                PlaceTerrainToken(target, Convert.ToString(value) ?? string.Empty);
                break;
            case "Convert":
                ConvertUnits(target, ToInt(value));
                break;
            case "LoseByRollResults":
                LoseByRollResults(Convert.ToString(value) ?? "1..2");
                break;
            case "Recruit":
                Recruit(target, ToInt(value), flags);
                break;
            case "PurchaseResource":
                PurchaseResource(target, ToInt(value), flags);
                break;
            case "AdjustRapport":
                AdjustRapport(target, ToInt(value), flags);
                break;
            case "SetRapport":
                SetRapport(target, Convert.ToString(value) ?? string.Empty);
                break;
            case "Damage":
                DamageBaggageSlot(ToInt(value));
                break;
            case "Repair":
                RepairBaggageSlot(ToInt(value));
                break;
            case "Capture":
                CaptureCurrentSettlement();
                break;
            case "GainAdvancement":
                GainAdvancement(target, flags);
                break;
            case "GainToken":
                GainPersistentToken(target, flags);
                break;
            case "RemoveToken":
                RemovePersistentToken(target);
                break;
            case "AddBuilding":
                AddBuilding(target);
                break;
            case "AddUpgrade":
                AddUpgrade(target);
                break;
            case "MarketTrade":
                PresentCampMarketTrade(next);
                return false;
            case "Purchase":
                PurchaseAdvancement(target, flags);
                break;
            case "PayTribute":
                PayCampTribute(target, flags);
                break;
            case "RemoveUnitByPriority":
                RemoveUnitByPriority();
                break;
            case "DiscardDrawnCampCards":
                DiscardOtherCampCards();
                break;
            case "EndPhase":
                if (string.Equals(target, "Camp", StringComparison.OrdinalIgnoreCase))
                    _campPhaseEnding = true;
                break;
            case "Advance":
                if (string.Equals(target, "Time", StringComparison.OrdinalIgnoreCase))
                {
                    var amount = ToInt(value);
                    AdvanceTime(amount);
                    _logger.LogInformation("EventFlow advanced Time by {Amount}.", amount);
                    if (Phase == GamePhase.Finale)
                        return false;
                }
                break;
            case "Combat":
                PresentCombatPrompt(target, Convert.ToString(value) ?? string.Empty, flags, next);
                return false;
            case "SeedRandomEcho":
            case "SeedEcho":
                SeedEchoInternal(type, target, Convert.ToString(value));
                break;
            case "Discard":
                if (string.Equals(target, "DeckTop:Tactics", StringComparison.OrdinalIgnoreCase))
                    DiscardTopTacticsCard();
                else
                    HandleCampCardDisposition(target, "Discard", flags);
                break;
            case "Remove":
                HandleCampCardDisposition(target, "Remove", flags);
                break;
            case "Return":
                HandleCampCardDisposition(target, "Return", flags);
                break;
            case "SeedRemainingEcho":
                SeedRemainingEcho(target, Convert.ToString(value));
                break;
            case "InspectDeckTop":
                InspectCampDeckTop(ToInt(value));
                break;
            case "ReturnToDeckTop":
                ReturnViewedCampCardsToTop();
                break;
            case "Reveal":
                if (string.Equals(target, "DeckTop:Tactics", StringComparison.OrdinalIgnoreCase))
                    RevealTopTacticsCard(flags);
                else
                    _logger.LogInformation("EventFlow action {Type} {Target} handled as platform/no-op at this stage.", type, target);
                break;
            case "Ignore":
            case "ModifyCost":
            case "AddAllowedLocation":
                _logger.LogInformation("EventFlow action {Type} {Target} handled as platform/no-op at this stage.", type, target);
                break;
            default:
                _logger.LogWarning("Unsupported EventFlow action {Type} on {Target}.", type, target);
                break;
        }

        _activeNodeId = next;
        return true;
    }

    private void DiscardTopTacticsCard()
    {
        if (_advancementDeck.Count == 0)
        {
            AddNarrativeText("There is no Advancement card left to discard.");
            return;
        }

        // Drill the Host's data target is DeckTop:Tactics. In the physical game,
        // Tactics and Developments share the Advancement deck, so this action
        // discards the currently revealed top Advancement when it is a Tactic.
        var top = _advancementDeck[0];
        var isTactic = top.Id.StartsWith("ADV-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(top.Id.AsSpan(4), out var number)
            && number is >= 7 and <= 15;

        if (!isTactic)
        {
            AddNarrativeText($"{top.Name} is not a Tactic, so it is not discarded.");
            return;
        }

        _advancementDeck.RemoveAt(0);
        RefreshAdvancementOffer();
        _flowVars["Drill.DiscardedTactic"] = top.Name;
        AddNarrativeText($"Discard {top.Name} from the top of the Advancement deck for free.");
    }

    private void RevealTopTacticsCard(IReadOnlyList<JsonElement> flags)
    {
        RefreshAdvancementOffer();
        if (_advancementDeck.Count == 0)
        {
            AddNarrativeText("No Advancement remains to reveal.");
            return;
        }

        var top = _advancementDeck[0];
        var suffix = HasFlag(flags, "CannotPurchaseRevealedTacticThisPhase")
            ? " It cannot be purchased during this Camp phase."
            : string.Empty;
        AddNarrativeText($"Reveal {top.Name} as the next Advancement.{suffix}");
    }

    private void ApplyResourceBundle(string bundle, bool gain, IReadOnlyList<JsonElement>? flags = null)
    {
        foreach (Match match in Regex.Matches(bundle, @"(\d+)([CFWSR])", RegexOptions.IgnoreCase))
        {
            var amount = int.Parse(match.Groups[1].Value);
            var target = CostCodeTarget(match.Groups[2].Value);
            if (gain)
                ApplyGain(target, amount);
            else
                ApplySpend(target, amount, flags);
        }
    }

    private string AdjacentTerrainResource()
    {
        var clearingIndex = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        if (clearingIndex < 0)
            clearingIndex = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Clearing);

        TerrainType terrain = TerrainType.None;
        if (clearingIndex > 0 && PlayArea[clearingIndex - 1].Kind == PlayAreaKind.Terrain)
            terrain = PlayArea[clearingIndex - 1].Terrain;
        else if (clearingIndex >= 0 && clearingIndex + 1 < PlayArea.Count && PlayArea[clearingIndex + 1].Kind == PlayAreaKind.Terrain)
            terrain = PlayArea[clearingIndex + 1].Terrain;

        return terrain switch
        {
            TerrainType.Plains => "Food",
            TerrainType.Forest => "Wood",
            TerrainType.Mountain => "Stone",
            _ => "Food"
        };
    }

    private string NearestTribeRapport()
    {
        var clearing = PlayArea.LastOrDefault(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        if (clearing is not null && !string.IsNullOrWhiteSpace(clearing.Controller) && clearing.Controller != "Player")
            return GetTribeRapport(clearing.Controller);

        // On a player-controlled Clearing, use the nearest encountered tribe
        // represented on the explored road. Search backward first because that
        // is the closest known settlement on the current march.
        for (var i = PlayArea.Count - 1; i >= 0; i--)
        {
            var element = PlayArea[i];
            if (element.Kind != PlayAreaKind.Clearing || string.IsNullOrWhiteSpace(element.Controller) || element.Controller == "Player")
                continue;
            var rapport = GetTribeRapport(element.Controller);
            if (rapport != "Unmet")
                return rapport;
        }
        return "Neutral";
    }

    private void GainPersistentToken(string target, IReadOnlyList<JsonElement> flags)
    {
        if (string.Equals(target, "Priest", StringComparison.OrdinalIgnoreCase))
        {
            var hadPriest = _hasPriest;
            var replacedQuietusPriest = hadPriest
                && NormalizeTribeName(_priestSource ?? string.Empty) == NormalizeTribeName("Quietus");

            _hasPriest = true;
            _priestSource = "Chapel";
            if (!HasBuilding("Chapel"))
                PersistentItems.Add(new("Chapel"));

            AddNarrativeText(replacedQuietusPriest
                ? "A Chapel is established. The Quietus Priest is replaced by the Host's own Priest. Priest: may attempt Proselytization before Combat, prevents 1 Winter Attrition loss, and improves the Finale Resolve Check."
                : hadPriest
                    ? "A Chapel is established. Priest: may attempt Proselytization before Combat, prevents 1 Winter Attrition loss, and improves the Finale Resolve Check."
                    : "A Chapel is established, and a Priest joins the Host. Priest: may attempt Proselytization before Combat, prevents 1 Winter Attrition loss, and improves the Finale Resolve Check.");
            return;
        }

        var occupiesBaggage = flags.Any(f => string.Equals(GetString(f, "name"), "Location", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ScalarText(f, "value"), "BaggageTrain", StringComparison.OrdinalIgnoreCase));
        var baggageIndex = occupiesBaggage
            ? Baggage.FindIndex(b => string.IsNullOrWhiteSpace(b.Contents) && !b.Damaged)
            : -1;
        if (occupiesBaggage && baggageIndex < 0)
        {
            AddNarrativeText($"There is no empty, undamaged Baggage Train space for {DisplayTokenName(target)}.");
            _logger.LogWarning("Could not gain baggage token {Token}: no empty undamaged Baggage Train space.", target);
            return;
        }

        ActiveTokens.Add(target);
        if (string.Equals(target, "Scouting", StringComparison.OrdinalIgnoreCase) && !PersistentItems.Any(p => p.Name == "Scouting"))
            PersistentItems.Add(new("Scouting"));
        if (string.Equals(target, "CampSteward", StringComparison.OrdinalIgnoreCase) && !PersistentItems.Any(p => p.Name == "Camp Steward"))
            PersistentItems.Add(new("Camp Steward"));

        // Physical tokens that occupy Baggage Train space are represented in
        // the first available undamaged slot.
        if (baggageIndex >= 0)
            Baggage[baggageIndex] = Baggage[baggageIndex] with { Contents = target, Quantity = 1 };

        var tokenRules = PersistentTokenRulesText(target);
        var tokenNarrative = target switch
        {
            "AncientPathway" => "The ancient pathway points toward the next settlement. If the next Clearing is vacant, gain 1 Leadership.",
            "HighOverlookClearRoad" => "The scouts mark a clearer route ahead. The next march should be quicker and easier to navigate.",
            "HighOverlookScoutApproach" => "Scouts are sent ahead to study the next approach and prepare the Host for what waits there.",
            _ when !string.IsNullOrWhiteSpace(tokenRules) => $"Gain {DisplayTokenName(target)}. {DisplayTokenName(target)}: {tokenRules}",
            _ => $"Gain {DisplayTokenName(target)}."
        };
        AddNarrativeText(tokenNarrative);
        _logger.LogInformation("Token gained: {Token}", target);
    }

    private void RemovePersistentToken(string target)
    {
        if (string.Equals(target, "SelectedToken", StringComparison.OrdinalIgnoreCase)
            && _flowVars.TryGetValue("SelectedToken", out var selected))
            target = Convert.ToString(selected) ?? target;

        ActiveTokens.Remove(target);
        if (string.Equals(target, "Scouting", StringComparison.OrdinalIgnoreCase))
            PersistentItems.RemoveAll(p => p.Name == "Scouting");
        if (string.Equals(target, "CampSteward", StringComparison.OrdinalIgnoreCase))
            PersistentItems.RemoveAll(p => p.Name == "Camp Steward");

        var baggageIndex = Baggage.FindIndex(b => string.Equals(b.Contents, target, StringComparison.OrdinalIgnoreCase));
        if (baggageIndex >= 0)
        {
            if (string.Equals(target, "Firepots", StringComparison.OrdinalIgnoreCase))
                _flowVars["FirepotsSlot"] = Baggage[baggageIndex].Position;
            Baggage[baggageIndex] = Baggage[baggageIndex] with { Contents = null, Quantity = 1 };
        }

        AddNarrativeText($"Remove {DisplayTokenName(target)}.");
        _logger.LogInformation("Token removed: {Token}", target);
    }

    private void AddBuilding(string target)
    {
        if (!HasBuilding(target))
            PersistentItems.Add(new(target));

        AddNarrativeText(target switch
        {
            "Academy" => "Build Academy. Whenever you gain Research, roll 1d6 for each Research gained; each 4–6 grants +1 additional Research.",
            "Market" => "Build Market. During Harvest, sell up to 2 Resources purged from the Baggage Train; Market Day may also be used at a Player Controlled Settlement.",
            _ => $"Build {target}."
        });
    }

    private bool HasBuilding(string target)
        => PersistentItems.Any(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));

    private void AddUpgrade(string target)
    {
        if (string.Equals(target, "BaggageCart", StringComparison.OrdinalIgnoreCase))
        {
            if (!PersistentItems.Any(p => p.Name == "Baggage Cart"))
                PersistentItems.Add(new("Baggage Cart"));
            var start = Baggage.Count + 1;
            Baggage.Add(new(start, null));
            Baggage.Add(new(start + 1, null));
            AddNarrativeText("Upgrade the Baggage Cart. Gain 2 Baggage spaces.");
            return;
        }

        if (!PersistentItems.Any(p => p.Name == target))
            PersistentItems.Add(new(target));
        AddNarrativeText($"Gain {target}.");
    }

    private void PresentCampMarketTrade(string? next)
    {
        _pendingCampActionNextNode = next;
        _campMarketTradesRemaining = 2;
        _specialPrompt = "CampMarketTrade";
        BuildCampMarketTradePrompt();
    }

    private void BuildCampMarketTradePrompt()
    {
        var options = new List<FlowPromptOption>();
        if (_campMarketTradesRemaining > 0)
        {
            foreach (var market in Market)
            {
                if (CanMarketBuy(market.Resource, 1))
                    options.Add(new($"camptrade:buy:{market.Resource}", $"Buy {market.Resource}", $"Pay {market.BuyPrice} Coin"));
                if (ResourceValue(market.Resource) > 0)
                    options.Add(new($"camptrade:sell:{market.Resource}", $"Sell {market.Resource}", $"Gain {market.SellPrice} Coin"));
            }
        }
        options.Add(new("camptrade:done", "Done trading"));
        FlowPrompt = new(FlowPromptKind.Choice, "Market Day", _campMarketTradesRemaining == 2 ? "Buy and/or sell up to 2 Resources." : $"{_campMarketTradesRemaining} trade remaining.", options);
    }

    private void ResolveCampMarketTradePrompt(string optionId)
    {
        if (optionId == "camptrade:done")
        {
            _specialPrompt = null;
            FlowPrompt = null;
            _activeNodeId = _pendingCampActionNextNode;
            _pendingCampActionNextNode = null;
            AdvanceFlow();
            Changed?.Invoke();
            return;
        }

        var parts = optionId.Split(':');
        if (parts.Length != 3 || _campMarketTradesRemaining <= 0)
            return;
        var resource = parts[2];
        if (parts[1] == "buy")
            MarketBuy(resource, 1);
        else if (parts[1] == "sell")
            MarketSell(resource, 1);
        else
            return;

        _campMarketTradesRemaining--;
        if (_campMarketTradesRemaining <= 0)
        {
            _specialPrompt = null;
            FlowPrompt = null;
            _activeNodeId = _pendingCampActionNextNode;
            _pendingCampActionNextNode = null;
            AdvanceFlow();
        }
        else
        {
            BuildCampMarketTradePrompt();
        }
        Changed?.Invoke();
    }

    private void PresentCampStewardBuy(string? next)
    {
        _pendingCampActionNextNode = next;
        _specialPrompt = "CampStewardBuy";
        var options = new List<FlowPromptOption>();
        foreach (var market in Market)
        {
            var price = Math.Max(0, market.BuyPrice - 1);
            if (ResourceValue("Coin") >= price && CanAddPhysicalResource(market.Resource, 1))
                options.Add(new($"stewardbuy:{market.Resource}", market.Resource, $"{price} Coin"));
        }
        if (options.Count == 0)
        {
            AddNarrativeText("The Camp Steward finds no purchase the Baggage Train can take.");
            _specialPrompt = null;
            _activeNodeId = next;
            AdvanceFlow();
            return;
        }
        FlowPrompt = new(FlowPromptKind.Select, "Camp Steward", "Choose 1 Resource to buy for 1 Coin less than Market value.", options);
    }

    private void ResolveCampStewardBuyPrompt(string optionId)
    {
        if (!optionId.StartsWith("stewardbuy:", StringComparison.OrdinalIgnoreCase))
            return;
        var resource = optionId[11..];
        var market = Market.FirstOrDefault(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        if (market is null)
            return;
        var price = Math.Max(0, market.BuyPrice - 1);
        if (ResourceValue("Coin") < price || !CanAddPhysicalResource(resource, 1))
            return;
        AdjustResource("Coin", -price);
        var gained = AddPhysicalResource(resource, 1);
        AddNarrativeText($"The Camp Steward buys {gained} {resource} for {price} Coin.");
        _specialPrompt = null;
        FlowPrompt = null;
        _activeNodeId = _pendingCampActionNextNode;
        _pendingCampActionNextNode = null;
        AdvanceFlow();
        Changed?.Invoke();
    }

    private void PurchaseAdvancement(string target, IReadOnlyList<JsonElement> flags)
    {
        AdvancementOfferState? advancement = null;
        if (string.Equals(target, "TopAdvancementCard", StringComparison.OrdinalIgnoreCase))
            advancement = _advancementDeck.FirstOrDefault();
        else if (string.Equals(target, "ArmorDevelopmentFromRROffer", StringComparison.OrdinalIgnoreCase))
            advancement = _armorSupply.FirstOrDefault();
        if (advancement is null)
        {
            AddNarrativeText("No eligible Advancement is available.");
            _campCardResolvedSuccessfully = false;
            return;
        }

        var researchCost = ParseResearchCost(advancement.Cost);
        var coinCost = ParseCostAmount(advancement.Cost, "C");
        var discount = FlagInt(flags, "ResearchCostDiscount", 0);
        var minimum = FlagInt(flags, "MinimumResearchCost", 0);
        researchCost = Math.Max(minimum, researchCost - discount);
        if (ResourceValue("Research") < researchCost || ResourceValue("Coin") < coinCost)
        {
            AddNarrativeText($"The Host cannot afford {advancement.Name}.");
            _campCardResolvedSuccessfully = false;
            return;
        }

        if (researchCost > 0) AdjustResource("Research", -researchCost);
        if (coinCost > 0) AdjustResource("Coin", -coinCost);
        AcquiredAdvancements.Add(advancement);
        _advancementDeck.RemoveAll(a => a.Id == advancement.Id);
        _armorSupply.RemoveAll(a => a.Id == advancement.Id);
        RefreshAdvancementOffer();
        var paid = new List<string>();
        if (researchCost > 0) paid.Add($"{researchCost} Research");
        if (coinCost > 0) paid.Add($"{coinCost} Coin");
        var paidText = paid.Count > 0 ? $" for {string.Join(" and ", paid)}" : string.Empty;
        AddNarrativeText($"Acquire {advancement.Name}{paidText}.");
    }

    private static int ParseResearchCost(string cost)
    {
        var match = Regex.Match(cost ?? string.Empty, @"(\d+)\s*RT?", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private void PayCampTribute(string target, IReadOnlyList<JsonElement> flags)
    {
        var tribe = string.Equals(target, "SelectedTribe", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToString(_flowVars.TryGetValue("SelectedTribe", out var selected) ? selected : null)
            : target;
        if (string.IsNullOrWhiteSpace(tribe))
            return;

        string resource;
        if (NormalizeTribeName(tribe) == NormalizeTribeName("Sun-Touched")) resource = "Stone";
        else if (NormalizeTribeName(tribe) == NormalizeTribeName("Nightstone")) resource = "Food";
        else if (NormalizeTribeName(tribe) == NormalizeTribeName("Quietus")) resource = "Wood";
        else
        {
            resource = Market
                .Where(m => ResourceValue(m.Resource) > 0)
                .OrderByDescending(m => m.BuyPrice)
                .ThenBy(m => m.Resource)
                .Select(m => m.Resource)
                .FirstOrDefault() ?? "Food";
        }

        var removed = RemovePhysicalResource(resource, 1);
        if (removed == 1)
            AddNarrativeText($"Send {1} {resource} to {CanonicalTribeName(tribe)}.");
        else
        {
            AddNarrativeText($"The Host cannot assemble the tribute for {CanonicalTribeName(tribe)}.");
            _campCardResolvedSuccessfully = false;
        }
    }

    private void RemoveUnitByPriority()
    {
        foreach (var type in new[] { "Cavalry", "Infantry", "Archers", "Levy" })
        {
            if (HostCount(type) <= 0)
                continue;
            var lost = LoseHostUnit(type, 1);
            if (lost > 0)
                AddNarrativeText($"Lose 1 {type}.");
            return;
        }
    }

    private void DiscardOtherCampCards()
    {
        if (PendingCampCard is null)
            return;
        foreach (var card in CampHand.Where(c => c.Id != PendingCampCard.Id).ToArray())
        {
            CampHand.Remove(card);
            _campDiscard.Add(card);
        }
    }

    private void InspectCampDeckTop(int count)
    {
        _viewedCampCards.Clear();
        count = Math.Max(0, count);
        while (count-- > 0)
        {
            if (_campDeck.Count == 0)
                RebuildCampDeckFromDiscard();
            if (_campDeck.Count == 0)
                break;

            var card = _campDeck[0];
            _campDeck.RemoveAt(0);
            _viewedCampCards.Add(card);
        }

        if (_viewedCampCards.Count > 0)
            AddNarrativeText($"You look at the top {_viewedCampCards.Count} Camp card{(_viewedCampCards.Count == 1 ? string.Empty : "s")}: {string.Join("; ", _viewedCampCards.Select(c => c.Title))}.");
    }

    private void HandleViewedCampCardsDisposition(string disposition)
    {
        if (!string.Equals(disposition, "Discard", StringComparison.OrdinalIgnoreCase) || _viewedCampCards.Count == 0)
            return;

        var selection = Convert.ToString(_flowVars.TryGetValue("SelectedToDiscard", out var selected) ? selected : null) ?? string.Empty;
        if (selection.StartsWith("__KEEP_", StringComparison.OrdinalIgnoreCase))
            return;

        var ids = selection.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var id in ids)
        {
            var card = _viewedCampCards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (card is null)
                continue;
            _viewedCampCards.Remove(card);
            _campDiscard.Add(card);
            AddNarrativeText($"Discard {card.Title} from the cards you inspected.");
        }
    }

    private void ReturnViewedCampCardsToTop()
    {
        if (_viewedCampCards.Count == 0)
            return;

        var selection = Convert.ToString(_flowVars.TryGetValue("SelectedToDiscard", out var selected) ? selected : null) ?? string.Empty;
        if (string.Equals(selection, "__KEEP_REVERSED__", StringComparison.OrdinalIgnoreCase))
            _viewedCampCards.Reverse();

        // Insert in reverse so the first listed card remains the actual top card.
        for (var i = _viewedCampCards.Count - 1; i >= 0; i--)
            _campDeck.Insert(0, _viewedCampCards[i]);

        AddNarrativeText($"Return {string.Join(" then ", _viewedCampCards.Select(c => c.Title))} to the top of the Camp deck.");
        _viewedCampCards.Clear();
    }

    private void HandleCampCardDisposition(string target, string disposition, IReadOnlyList<JsonElement> flags)
    {
        if (string.Equals(target, "SelectedViewedCampCards", StringComparison.OrdinalIgnoreCase))
        {
            HandleViewedCampCardsDisposition(disposition);
            return;
        }

        if (_activeFlowContext != FlowContext.Camp || PendingCampCard is null)
        {
            _logger.LogInformation("EventFlow card disposition {Disposition} {Target} ignored outside Camp.", disposition, target);
            return;
        }

        if (!string.Equals(target, "Card", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, "Self", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("EventFlow card disposition {Disposition} {Target} retained as platform action.", disposition, target);
            return;
        }

        // A Debug simulation resolves the card's real effects, but launching it
        // from the Debug menu must not itself move that card between deck zones.
        if (_debugCampSimulation)
        {
            _flowVars["Camp.AfterPlayOverridden"] = true;
            return;
        }

        // Prevent ApplyCampAfterPlay from applying the printed default a second time.
        var card = PendingCampCard;
        if (disposition == "Discard")
        {
            if (!_campDiscard.Any(c => c.Id == card.Id))
                _campDiscard.Add(card);
        }
        else if (disposition == "Remove")
        {
            if (!_removedCampCards.Any(c => c.Id == card.Id))
                _removedCampCards.Add(card);
        }
        else if (disposition == "Return")
        {
            if (!_unseededEchoes.Any(c => c.Id == card.Id))
                _unseededEchoes.Add(card);
        }

        _flowVars["Camp.AfterPlayOverridden"] = true;
    }

    private void SeedRemainingEcho(string target, string? value)
    {
        var pool = string.Equals(target, "EchoPool", StringComparison.OrdinalIgnoreCase)
            ? value ?? Convert.ToString(_flowVars.TryGetValue("EchoPool", out var v) ? v : null)
            : target;
        if (string.IsNullOrWhiteSpace(pool) && PendingCampCard is not null)
            pool = PendingCampCard.EchoPool;
        SeedEchoByPool(pool, random: false);
    }

    private void PresentCombatPrompt(string target, string value, IReadOnlyList<JsonElement> flags, string? next)
    {
        _pendingCombatNextNode = next;
        _combatResumeNextNode = next;
        _combatResumeMode = CombatResumeMode.Flow;

        var count = value.Contains("GameYear+1", StringComparison.OrdinalIgnoreCase) ? Year + 1 : Year;
        string enemyName;
        string enemySpec;
        switch (target)
        {
            case "BanditHorde":
                enemyName = "Bandit Horde";
                enemySpec = $"{count}I";
                break;
            case "HillClan":
                enemyName = "Hill Clan";
                enemySpec = $"{count}I";
                break;
            case "CurrentArrivalHost":
                enemyName = string.IsNullOrWhiteSpace(_currentArrivalTribe) ? "Tribal Host" : _currentArrivalTribe;
                enemySpec = CurrentArrivalEnemyHost();
                break;
            case "CurrentEchoHost" when PendingCampCard is not null:
                enemyName = PendingCampCard.Title;
                enemySpec = CurrentCampEnemyHost(value);
                break;
            default:
                enemyName = target;
                enemySpec = _activeFlowContext == FlowContext.Camp && value.Contains("Y1=", StringComparison.OrdinalIgnoreCase)
                    ? CurrentCampEnemyHost(value)
                    : value;
                break;
        }

        var noDisengage = flags.Any(f => string.Equals(GetString(f, "name"), "NoDisengage", StringComparison.OrdinalIgnoreCase));
        var mayDisengage = !noDisengage;

        BeginInteractiveCombat(
            enemyName,
            ParseCombatHost(enemySpec),
            mayDisengage: mayDisengage,
            noDisengage: noDisengage,
            playerInitiated: flags.Any(f => string.Equals(GetString(f, "name"), "ArrivalAttack", StringComparison.OrdinalIgnoreCase)),
            isHostileEcho: _activeFlowContext == FlowContext.Camp && PendingCampCard is not null && IsHostileEcho(PendingCampCard),
            sourceLabel: ActiveFlowHeading());
    }

    private string CurrentCampEnemyHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Enemy Host";

        var match = Regex.Match(value, $@"(?:^|;)\s*Y{Year}\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : value;
    }

    private void ResolveCombatPrompt(string optionId)
    {
        if (!optionId.StartsWith("combat:", StringComparison.OrdinalIgnoreCase))
            return;

        if (_specialPrompt == "ArrivalForcedCombat")
        {
            ResolveArrivalForcedCombatPrompt(optionId);
            return;
        }

        var outcome = optionId[7..];
        if (_activeFlowContext == FlowContext.Arrival)
            _arrivalCombatResolved = true;
        _flowVars["Combat.Outcome"] = outcome;
        AddNarrativeText(
            outcome switch
            {
                "Won" => "The Host wins the battle.",
                "Disengaged" => "The Host disengages after the first round.",
                "Lost" => "The Host is defeated in battle.",
                _ => "The battle ends."
            });

        FlowPrompt = null;
        _activeNodeId = _pendingCombatNextNode;
        _pendingCombatNextNode = null;
        AdvanceFlow();
        Changed?.Invoke();
    }

    private List<(string Resource, int Amount)> CollectConsecutivePhysicalResourceGains(
        string firstResource,
        int firstAmount,
        ref string? nextNode)
    {
        var gains = new List<(string Resource, int Amount)> { (firstResource, firstAmount) };
        if (_activeFlow is null || string.IsNullOrWhiteSpace(nextNode)
            || !_activeFlow.Value.TryGetProperty("nodes", out var nodes))
            return gains;

        // Adjacent physical-resource Gain nodes are one simultaneous reward in
        // the authored card text. Batch them before touching the Baggage Train
        // so JSON node order never decides which offered resource gets packed.
        while (!string.IsNullOrWhiteSpace(nextNode) && nodes.TryGetProperty(nextNode, out var candidate))
        {
            if (!string.Equals(GetString(candidate, "type"), "Action", StringComparison.OrdinalIgnoreCase)
                || !candidate.TryGetProperty("action", out var action)
                || !string.Equals(GetString(action, "type"), "Gain", StringComparison.OrdinalIgnoreCase)
                || candidate.TryGetProperty("player_text", out _)
                || GetFlags(candidate).Any())
                break;

            var candidateTarget = ResolveTarget(GetString(action, "target"));
            if (!IsPhysicalResource(candidateTarget))
                break;

            var candidateValue = ResolveActionValue(action.TryGetProperty("value", out var rawValue) ? rawValue : default);
            var candidateAmount = ToInt(candidateValue);
            if (candidateAmount <= 0)
                break;

            gains.Add((candidateTarget, candidateAmount));
            nextNode = GetString(candidate, "next");
        }

        return gains;
    }

    private bool BeginResourceBundleGain(string bundle, ResourceGainResume resume, string? nextNode = null)
    {
        var gains = new List<(string Resource, int Amount)>();

        foreach (Match match in Regex.Matches(bundle, @"(\d+)([CFWSR])", RegexOptions.IgnoreCase))
        {
            var amount = int.Parse(match.Groups[1].Value);
            var target = CostCodeTarget(match.Groups[2].Value);

            if (IsPhysicalResource(target))
                gains.Add((target, amount));
            else
                ApplyGain(target, amount);
        }

        return BeginPhysicalResourceGain(gains, resume, nextNode);
    }

    private bool BeginPhysicalResourceGain(
        IEnumerable<(string Resource, int Amount)> gains,
        ResourceGainResume resume,
        string? nextNode = null)
    {
        if (_pendingResourceGains.Count > 0)
        {
            _logger.LogWarning("A new physical Resource gain was requested while another Baggage overflow choice was pending.");
            return false;
        }

        foreach (var (resource, amount) in gains)
        {
            if (amount > 0 && IsPhysicalResource(resource))
                _pendingResourceGains.Add((resource, amount));
        }

        if (_pendingResourceGains.Count == 0)
            return true;

        _pendingResourceGainResume = resume;
        _pendingResourceGainNextNode = nextNode;
        _pendingResourceGainOpenStaging = _pendingResourceGains
            .Select(g => g.Resource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();

        if (_pendingResourceGainOpenStaging)
        {
            PresentResourceStagingPrompt();
            return false;
        }

        var completed = ContinuePhysicalResourceGain(resume, nextNode);
        if (completed)
        {
            _pendingResourceGainResume = ResourceGainResume.None;
            _pendingResourceGainNextNode = null;
            _pendingResourceGainOpenStaging = false;
        }
        return completed;
    }

    private bool ContinuePhysicalResourceGain(ResourceGainResume resume, string? nextNode)
    {
        while (_pendingResourceGains.Count > 0)
        {
            var pending = _pendingResourceGains[0];
            var gained = AddPhysicalResource(pending.Resource, pending.Amount);

            if (gained > 0)
                AddNarrativeText($"Gain {gained} {pending.Resource}.");

            var remaining = pending.Amount - gained;
            if (remaining <= 0)
            {
                _pendingResourceGains.RemoveAt(0);
                continue;
            }

            _pendingResourceGains[0] = (pending.Resource, remaining);
            _pendingResourceGainResume = resume;
            _pendingResourceGainNextNode = nextNode;
            PresentBaggageOverflowPrompt();
            return false;
        }

        return true;
    }

    private void PresentResourceStagingPrompt()
    {
        if (_pendingResourceGains.Count == 0)
        {
            FinishPendingResourceGain();
            return;
        }

        var options = new List<FlowPromptOption>();
        foreach (var pending in _pendingResourceGains
                     .Where(g => g.Amount > 0)
                     .GroupBy(g => g.Resource, StringComparer.OrdinalIgnoreCase)
                     .Select(g => (Resource: g.Key, Amount: g.Sum(x => x.Amount))))
        {
            if (CanAddPhysicalResource(pending.Resource, 1))
            {
                options.Add(new(
                    $"resource-staging:pack:{pending.Resource}",
                    $"Pack 1 {pending.Resource}",
                    pending.Amount > 1 ? $"{pending.Amount} {pending.Resource} are waiting." : "Pack this offered Resource next."));
            }

            options.Add(new(
                $"resource-staging:leave:{pending.Resource}",
                $"Leave 1 {pending.Resource} behind",
                "Do not place this offered Resource in the Baggage Train."));
        }

        foreach (var slot in Baggage.Where(b =>
                     !b.Damaged
                     && !string.IsNullOrWhiteSpace(b.Contents)
                     && IsPhysicalResource(b.Contents!)))
        {
            var quantity = string.Equals(slot.Contents, "Food", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, slot.Quantity)
                : 1;
            options.Add(new(
                $"resource-staging:purge:{slot.Position}",
                $"Purge {quantity} {slot.Contents}",
                $"Free Baggage space {slot.Position}."));
        }

        _specialPrompt = "ResourceStaging";
        FlowPrompt = new(
            FlowPromptKind.Choice,
            "Baggage Train",
            "These Resources were gained together. Choose what to pack next, in any order. You may purge carried Resources to make room; anything you leave behind is lost.",
            options);
    }

    private void ResolveResourceStagingPrompt(string optionId)
    {
        if (_pendingResourceGains.Count == 0)
            return;

        if (optionId.StartsWith("resource-staging:pack:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = optionId[22..];
            var index = _pendingResourceGains.FindIndex(g =>
                g.Amount > 0 && string.Equals(g.Resource, resource, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || !CanAddPhysicalResource(resource, 1) || AddPhysicalResource(resource, 1) != 1)
                return;

            var pending = _pendingResourceGains[index];
            AddNarrativeText($"Gain 1 {pending.Resource}.");
            if (pending.Amount <= 1)
                _pendingResourceGains.RemoveAt(index);
            else
                _pendingResourceGains[index] = (pending.Resource, pending.Amount - 1);
        }
        else if (optionId.StartsWith("resource-staging:leave:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = optionId[23..];
            var index = _pendingResourceGains.FindIndex(g =>
                g.Amount > 0 && string.Equals(g.Resource, resource, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;

            var pending = _pendingResourceGains[index];
            AddNarrativeText($"The Host leaves 1 {pending.Resource} behind.");
            if (pending.Amount <= 1)
                _pendingResourceGains.RemoveAt(index);
            else
                _pendingResourceGains[index] = (pending.Resource, pending.Amount - 1);
        }
        else if (optionId.StartsWith("resource-staging:purge:", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(optionId[23..], out var position))
        {
            var index = Baggage.FindIndex(b =>
                b.Position == position
                && !b.Damaged
                && !string.IsNullOrWhiteSpace(b.Contents)
                && IsPhysicalResource(b.Contents!));
            if (index < 0)
                return;

            var resource = Baggage[index].Contents!;
            var quantity = string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, Baggage[index].Quantity)
                : 1;
            Baggage[index] = Baggage[index] with { Contents = null, Quantity = 1 };
            AdjustResource(resource, -quantity);
            AddNarrativeText($"The Host leaves {quantity} {resource} behind to make room in the Baggage Train.");
        }
        else
        {
            return;
        }

        if (_pendingResourceGains.Count == 0)
        {
            FinishPendingResourceGain();
            Changed?.Invoke();
            return;
        }

        PresentResourceStagingPrompt();
        Changed?.Invoke();
    }

    private void PresentBaggageOverflowPrompt()
    {
        if (_pendingResourceGains.Count == 0)
            return;

        var pending = _pendingResourceGains[0];
        var options = new List<FlowPromptOption>
        {
            new(
                "baggage-overflow:leave",
                $"Leave {pending.Amount} {pending.Resource} behind",
                "Keep the Baggage Train as it is.")
        };

        foreach (var slot in Baggage.Where(b =>
                     !b.Damaged
                     && !string.IsNullOrWhiteSpace(b.Contents)
                     && IsPhysicalResource(b.Contents!)))
        {
            var quantity = string.Equals(slot.Contents, "Food", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, slot.Quantity)
                : 1;
            options.Add(new(
                $"baggage-overflow:purge:{slot.Position}",
                $"Purge {quantity} {slot.Contents}",
                $"Free Baggage space {slot.Position}."));
        }

        _specialPrompt = "BaggageOverflow";
        FlowPrompt = new(
            FlowPromptKind.Choice,
            "Baggage Train",
            $"The Baggage Train has no room for {pending.Amount} {pending.Resource}. Purge a carried Resource to make room, or leave the new Resource behind.",
            options);
    }

    private void ResolveBaggageOverflowPrompt(string optionId)
    {
        if (_pendingResourceGains.Count == 0)
            return;

        if (string.Equals(optionId, "baggage-overflow:leave", StringComparison.OrdinalIgnoreCase))
        {
            var pending = _pendingResourceGains[0];
            AddNarrativeText($"The Host leaves {pending.Amount} {pending.Resource} behind because the Baggage Train cannot carry it.");
            _pendingResourceGains.RemoveAt(0);
        }
        else if (optionId.StartsWith("baggage-overflow:purge:", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(optionId[23..], out var position))
        {
            var index = Baggage.FindIndex(b =>
                b.Position == position
                && !b.Damaged
                && !string.IsNullOrWhiteSpace(b.Contents)
                && IsPhysicalResource(b.Contents!));
            if (index < 0)
                return;

            var resource = Baggage[index].Contents!;
            var quantity = string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, Baggage[index].Quantity)
                : 1;

            Baggage[index] = Baggage[index] with { Contents = null, Quantity = 1 };
            AdjustResource(resource, -quantity);
            AddNarrativeText($"The Host leaves {quantity} {resource} behind to make room in the Baggage Train.");
        }
        else
        {
            return;
        }

        _specialPrompt = null;
        FlowPrompt = null;

        if (_pendingResourceGainOpenStaging && _pendingResourceGains.Count > 0)
        {
            PresentResourceStagingPrompt();
            Changed?.Invoke();
            return;
        }

        if (!ContinuePhysicalResourceGain(_pendingResourceGainResume, _pendingResourceGainNextNode))
        {
            Changed?.Invoke();
            return;
        }

        FinishPendingResourceGain();
        Changed?.Invoke();
    }

    private void FinishPendingResourceGain()
    {
        var resume = _pendingResourceGainResume;
        var nextNode = _pendingResourceGainNextNode;

        _specialPrompt = null;
        FlowPrompt = null;
        _pendingResourceGainResume = ResourceGainResume.None;
        _pendingResourceGainNextNode = null;
        _pendingResourceGainOpenStaging = false;
        _pendingResourceGains.Clear();

        switch (resume)
        {
            case ResourceGainResume.Flow:
                _activeNodeId = nextNode;
                AdvanceFlow();
                break;
            case ResourceGainResume.CompleteArrival:
                CompleteArrivalStep();
                break;
        }
    }

    private void ApplyGain(string target, int amount)
    {
        if (string.IsNullOrWhiteSpace(target) || amount <= 0)
            return;

        if (target.StartsWith("Rapport:", StringComparison.OrdinalIgnoreCase))
        {
            AdjustRapport(target[8..], amount);
            return;
        }

        if (IsPhysicalResource(target))
        {
            var gained = AddPhysicalResource(target, amount);
            if (gained > 0)
                AddNarrativeText($"Gain {gained} {target}.");
            if (gained < amount)
                AddNarrativeText($"No Baggage space for {amount - gained} {target}.");
            return;
        }

        if (string.Equals(target, "Leadership", StringComparison.OrdinalIgnoreCase))
        {
            Leadership += amount;
            AddNarrativeText($"Gain {amount} Leadership.");
            return;
        }

        if (string.Equals(target, "Morale", StringComparison.OrdinalIgnoreCase))
        {
            Morale += amount;
            AddNarrativeText($"Gain {amount} Morale.");
            return;
        }

        if (string.Equals(target, "Priest", StringComparison.OrdinalIgnoreCase))
        {
            if (!_hasPriest)
            {
                _hasPriest = true;
                _priestSource = _currentArrivalTribe;
                AddNarrativeText("A Priest joins the Host.");
            }
            return;
        }

        AdjustResource(target, amount);
        AddNarrativeText($"Gain {amount} {target}.");
        if (string.Equals(target, "Research", StringComparison.OrdinalIgnoreCase))
            ResolveAcademyResearchBonus(amount);
    }

    private void ApplyLose(string target, object? value, IReadOnlyList<JsonElement> flags)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        if (string.Equals(target, "SelectedInfantryClass", StringComparison.OrdinalIgnoreCase))
        {
            var selectedType = _flowVars.TryGetValue("AssignedInfantryClass", out var selected)
                ? Convert.ToString(selected)
                : null;
            if (!string.IsNullOrWhiteSpace(selectedType))
            {
                var lost = LoseHostUnit(selectedType!, 1);
                if (lost > 0)
                    AddNarrativeText(selectedType!.Equals("Mercenaries", StringComparison.OrdinalIgnoreCase)
                        ? "The assigned Mercenary dies in the excavation."
                        : "The assigned Infantry dies in the excavation.");
            }
            return;
        }

        if (string.Equals(target, "SelectedUnits", StringComparison.OrdinalIgnoreCase))
        {
            if (_flowVars.TryGetValue("SelectedUnits", out var selected) && selected is Dictionary<string, int> dict)
            {
                foreach (var pair in dict)
                    LoseHostUnit(pair.Key, pair.Value);
            }
            return;
        }

        if (target.StartsWith("Rapport:", StringComparison.OrdinalIgnoreCase))
        {
            var rapportAmount = ToInt(value);
            if (rapportAmount > 0)
                AdjustRapport(target[8..], -rapportAmount, flags);
            return;
        }

        if (string.Equals(target, "Rapport", StringComparison.OrdinalIgnoreCase))
        {
            var rapportAmount = ToInt(value);
            if (rapportAmount > 0)
                AdjustRapport("CurrentTribe", -rapportAmount, flags);
            return;
        }

        if (string.Equals(target, "ResourceInSelectedAdjacentSlot", StringComparison.OrdinalIgnoreCase))
        {
            var position = ToInt(_flowVars.TryGetValue("SelectedAdjacentSlot", out var selectedSlot) ? selectedSlot : 0);
            var slotIndex = Baggage.FindIndex(b => b.Position == position);
            if (slotIndex >= 0 && IsPhysicalResource(Baggage[slotIndex].Contents ?? string.Empty) && Baggage[slotIndex].Quantity > 0)
            {
                var resource = Baggage[slotIndex].Contents!;
                if (string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase) && Baggage[slotIndex].Quantity > 1)
                    Baggage[slotIndex] = Baggage[slotIndex] with { Quantity = Baggage[slotIndex].Quantity - 1 };
                else
                    Baggage[slotIndex] = Baggage[slotIndex] with { Contents = null, Quantity = 1 };
                AdjustResource(resource, -1);
                AddNarrativeText($"Lose 1 {resource} from Baggage Train space {position}.");
            }
            return;
        }

        var amount = value is string all && string.Equals(all, "All", StringComparison.OrdinalIgnoreCase)
            ? (string.Equals(target, "Leadership", StringComparison.OrdinalIgnoreCase) ? Leadership : ResourceValue(target))
            : ToInt(value);
        if (amount <= 0)
            return;

        var ifAble = HasFlag(flags, "IfAble") || HasFlag(flags, "NoShortfall");
        var applyShortfall = !ifAble
            && (HasFlag(flags, "ApplyGlobalShortfall") || UsesGlobalShortfallByDefault(target));

        if (IsUnitType(target))
        {
            var lost = LoseHostUnit(target, amount);
            if (lost > 0)
                AddNarrativeText($"Lose {lost} {target}.");
            if (applyShortfall && lost < amount)
                ApplyShortfall(amount - lost);
            return;
        }

        if (IsPhysicalResource(target))
        {
            var lost = RemovePhysicalResource(target, amount);
            if (lost > 0)
                AddNarrativeText($"Lose {lost} {target}.");
            if (applyShortfall && lost < amount)
                ApplyShortfall(amount - lost);
            return;
        }

        if (string.Equals(target, "Leadership", StringComparison.OrdinalIgnoreCase))
        {
            var lost = Math.Min(Leadership, amount);
            Leadership -= lost;
            AddNarrativeText($"Lose {lost} Leadership.");
            if (applyShortfall && lost < amount)
                ApplyShortfall(amount - lost);
            return;
        }

        if (string.Equals(target, "Morale", StringComparison.OrdinalIgnoreCase))
        {
            Morale -= amount;
            AddNarrativeText($"Lose {amount} Morale.");
            return;
        }

        var available = ResourceValue(target);
        var actual = Math.Min(available, amount);
        AdjustResource(target, -actual);
        if (actual > 0)
            AddNarrativeText($"Lose {actual} {target}.");
        if (applyShortfall && actual < amount)
            ApplyShortfall(amount - actual);
        else if (!ifAble && actual < amount)
            _logger.LogInformation("Could not lose full {Amount} {Target}; lost {Actual}.", amount, target, actual);
    }

    private void ApplySpend(string target, int amount, IReadOnlyList<JsonElement>? flags = null)
    {
        if (string.IsNullOrWhiteSpace(target) || amount <= 0)
            return;

        var safeFlags = flags ?? Array.Empty<JsonElement>();
        var ifAble = HasFlag(safeFlags, "IfAble") || HasFlag(safeFlags, "NoShortfall");
        var applyShortfall = !ifAble
            && (HasFlag(safeFlags, "ApplyGlobalShortfall") || UsesGlobalShortfallByDefault(target));

        if (target.StartsWith("Rapport:", StringComparison.OrdinalIgnoreCase))
        {
            AdjustRapport(target[8..], -amount, safeFlags);
            return;
        }

        if (string.Equals(target, "Rapport", StringComparison.OrdinalIgnoreCase))
        {
            AdjustRapport("CurrentTribe", -amount, safeFlags);
            return;
        }

        if (IsPhysicalResource(target))
        {
            var spent = RemovePhysicalResource(target, amount);
            if (applyShortfall && spent < amount)
                ApplyShortfall(amount - spent);
            return;
        }

        if (string.Equals(target, "Leadership", StringComparison.OrdinalIgnoreCase))
        {
            var spent = Math.Min(Leadership, amount);
            Leadership -= spent;
            if (applyShortfall && spent < amount)
                ApplyShortfall(amount - spent);
            return;
        }

        var available = ResourceValue(target);
        var actual = Math.Min(available, amount);
        AdjustResource(target, -actual);
        if (applyShortfall && actual < amount)
            ApplyShortfall(amount - actual);
    }

    private static bool UsesGlobalShortfallByDefault(string target)
        => IsPhysicalResource(target)
            || string.Equals(target, "Coin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "Leadership", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "Research", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "Rapport", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("Rapport:", StringComparison.OrdinalIgnoreCase);

    private void ApplyShortfall(int missing)
    {
        if (missing <= 0)
            return;
        Morale -= missing;
        AddNarrativeText($"Lose {missing} Morale from the shortfall.");
    }

    private void MarketBuy(string resource, int quantity, int coinModifier = 0)
    {
        if (quantity <= 0 || !CanMarketBuy(resource, quantity, coinModifier))
            return;

        var market = Market.First(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        var cost = Math.Max(0, (market.BuyPrice * quantity) + coinModifier);
        AdjustResource("Coin", -cost);
        var gained = AddPhysicalResource(resource, quantity);
        AddNarrativeText($"Buy {gained} {resource} for {cost} Coin.");
    }

    private void MarketSell(string resource, int quantity)
    {
        var market = Market.FirstOrDefault(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        if (market is null || quantity <= 0)
            return;

        var sold = RemovePhysicalResource(resource, quantity);
        var coins = sold * market.SellPrice;
        AdjustResource("Coin", coins);
        AddNarrativeText($"Sell {sold} {resource} for {coins} Coin.");
    }

    private void PurchaseResource(string resource, int quantity, IReadOnlyList<JsonElement> flags)
    {
        var cost = FlagInt(flags, "CoinCost", 0);
        if (ResourceValue("Coin") < cost || !CanAddPhysicalResource(resource, quantity))
            return;

        AdjustResource("Coin", -cost);
        var gained = AddPhysicalResource(resource, quantity);
        AddNarrativeText($"Purchase {gained} {resource} for {cost} Coin.");
    }

    private void PlaceTerrainToken(string target, string token)
    {
        if (!string.Equals(target, "CurrentTerrain", StringComparison.OrdinalIgnoreCase))
            return;

        var index = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Terrain && p.IsHostLocation);
        if (index < 0)
            index = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Terrain);
        if (index < 0)
            return;

        if (string.Equals(token, "Depleted", StringComparison.OrdinalIgnoreCase))
        {
            PlayArea[index] = PlayArea[index] with { IsDepleted = true };
            AddNarrativeText("This Terrain is Depleted.");
        }
        else if (string.Equals(token, "Food", StringComparison.OrdinalIgnoreCase))
        {
            PlayArea[index] = PlayArea[index] with { BonusFood = PlayArea[index].BonusFood + 1 };
            AddNarrativeText("Place 1 bonus Food on this Terrain.");
        }
    }

    private void ConvertUnits(string target, int amount)
    {
        if (!string.Equals(target, "InfantryToCavalry", StringComparison.OrdinalIgnoreCase))
            return;

        if (amount <= 0)
        {
            AddNarrativeText("No matching pairs are rolled, so no Infantry gain mounts.");
            return;
        }

        var availableCavalry = AvailableForceCount("Cavalry");
        var converted = Math.Min(amount, Math.Min(HostCount("Infantry"), availableCavalry));
        if (converted <= 0)
        {
            AddNarrativeText("No Infantry can be converted to Cavalry.");
            return;
        }

        SetHostCount("Infantry", HostCount("Infantry") - converted);
        SetHostCount("Cavalry", HostCount("Cavalry") + converted);
        AdjustAvailableForce("Infantry", converted);
        AdjustAvailableForce("Cavalry", -converted);
        AddNarrativeText($"Convert {converted} Infantry to Cavalry.");
    }

    private void LoseByRollResults(string range)
    {
        var (low, high) = ParseRange(range);
        var totalLost = 0;
        foreach (var pair in _lastUnitTypeRolls)
        {
            if (pair.Value < low || pair.Value > high)
                continue;
            var lost = LoseHostUnit(pair.Key, 1);
            totalLost += lost;
            if (lost > 0)
                AddNarrativeText($"Lose 1 {pair.Key}.");
        }

        if (totalLost == 0)
            AddNarrativeText("The Host weathers the sickness without losses.");
    }

    private void Recruit(string target, int amount, IReadOnlyList<JsonElement> flags)
    {
        if (amount <= 0)
            return;

        if (string.Equals(target, "Levy", StringComparison.OrdinalIgnoreCase))
        {
            var room = Math.Max(0, 6 - HostCount("Levy"));
            var recruited = Math.Min(amount, Math.Min(room, AvailableForceCount("Levy")));
            if (recruited <= 0)
                return;
            SetHostCount("Levy", HostCount("Levy") + recruited);
            AdjustAvailableForce("Levy", -recruited);
            AddNarrativeText($"Gain {recruited} Levy.");
            return;
        }

        if (string.Equals(target, "Mercenary", StringComparison.OrdinalIgnoreCase))
        {
            var row = Host.FindIndex(u => string.Equals(u.Id, "MER-INF", StringComparison.OrdinalIgnoreCase));
            if (row < 0)
                Host.Add(new("MER-INF", "Mercenaries", amount));
            else
                Host[row] = Host[row] with { Count = Host[row].Count + amount };
            AddNarrativeText($"Recruit {amount} Mercenar{(amount == 1 ? "y" : "ies")}.");
            return;
        }

        if (string.Equals(target, "HedgeKnight", StringComparison.OrdinalIgnoreCase))
        {
            var row = Host.FindIndex(u => string.Equals(u.Id, "MER-CAV", StringComparison.OrdinalIgnoreCase));
            if (row < 0)
                Host.Add(new("MER-CAV", "Hedge Knights", amount));
            else
                Host[row] = Host[row] with { Count = Host[row].Count + amount };
            AddNarrativeText($"Recruit {amount} Hedge Knight{(amount == 1 ? string.Empty : "s")}.");
        }
    }

    private void AdjustRapport(string target, int delta, IReadOnlyList<JsonElement>? flags = null)
    {
        if (delta == 0)
            return;

        var safeFlags = flags ?? Array.Empty<JsonElement>();
        var noShortfall = HasFlag(safeFlags, "IfAble") || HasFlag(safeFlags, "NoShortfall");

        if (string.Equals(target, "AllTribes", StringComparison.OrdinalIgnoreCase))
        {
            var missingRapport = 0;
            for (var i = 0; i < Tribes.Count; i++)
            {
                if (string.Equals(Tribes[i].Rapport, "Unmet", StringComparison.OrdinalIgnoreCase))
                    continue;

                var oldRapport = Tribes[i].Rapport;
                var updatedRapport = ShiftRapport(oldRapport, delta);
                Tribes[i] = Tribes[i] with { Rapport = updatedRapport };
                if (delta < 0 && !noShortfall)
                    missingRapport += MissingRapportLoss(oldRapport, updatedRapport, -delta);
            }
            AddNarrativeText(delta < 0 ? "Rapport worsens with every encountered tribe." : "Rapport improves with every encountered tribe.");
            if (missingRapport > 0)
                ApplyShortfall(missingRapport);
            SyncPlayAreaHostilityFromRapport();
            RemoveQuietusPriestIfNeeded();
            return;
        }

        var tribe = string.Equals(target, "CurrentTribe", StringComparison.OrdinalIgnoreCase)
            ? _currentArrivalTribe
            : target;
        if (string.IsNullOrWhiteSpace(tribe))
            return;

        var index = Tribes.FindIndex(t => NormalizeTribeName(t.Name) == NormalizeTribeName(tribe));
        if (index < 0 || string.Equals(Tribes[index].Rapport, "Unmet", StringComparison.OrdinalIgnoreCase))
            return;

        var old = Tribes[index].Rapport;
        var updated = ShiftRapport(old, delta);
        Tribes[index] = Tribes[index] with { Rapport = updated };
        if (!string.Equals(old, updated, StringComparison.OrdinalIgnoreCase))
        {
            AddNarrativeText(delta > 0 ? $"Gain 1 Rapport with {Tribes[index].Name}." : $"Lose 1 Rapport with {Tribes[index].Name}.");
            if (!string.Equals(old, "Allied", StringComparison.OrdinalIgnoreCase)
                && string.Equals(updated, "Allied", StringComparison.OrdinalIgnoreCase))
                AddAlliedBenefitNarrative(Tribes[index].Name);
        }

        if (delta < 0 && !noShortfall)
        {
            var missingRapport = MissingRapportLoss(old, updated, -delta);
            if (missingRapport > 0)
                ApplyShortfall(missingRapport);
        }

        SyncPlayAreaHostilityFromRapport();
        RemoveQuietusPriestIfNeeded();
    }

    private static int MissingRapportLoss(string before, string after, int requestedLoss)
    {
        if (requestedLoss <= 0)
            return 0;

        var states = new[] { "Hostile", "Neutral", "Friendly", "Allied" };
        var beforeIndex = Array.FindIndex(states, state => string.Equals(state, before, StringComparison.OrdinalIgnoreCase));
        var afterIndex = Array.FindIndex(states, state => string.Equals(state, after, StringComparison.OrdinalIgnoreCase));
        if (beforeIndex < 0 || afterIndex < 0)
            return 0;

        var actualLoss = Math.Max(0, beforeIndex - afterIndex);
        return Math.Max(0, requestedLoss - actualLoss);
    }

    private void SetRapport(string target, string value)
    {
        if (string.Equals(target, "UnmetTribes", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < Tribes.Count; i++)
            {
                if (string.Equals(Tribes[i].Rapport, "Unmet", StringComparison.OrdinalIgnoreCase))
                    Tribes[i] = Tribes[i] with { Rapport = value };
            }
            SyncPlayAreaHostilityFromRapport();
            RemoveQuietusPriestIfNeeded();
            return;
        }

        if (string.Equals(target, "CurrentTribe", StringComparison.OrdinalIgnoreCase))
            target = _currentArrivalTribe ?? string.Empty;

        var index = Tribes.FindIndex(t => NormalizeTribeName(t.Name) == NormalizeTribeName(target));
        if (index >= 0)
        {
            var old = Tribes[index].Rapport;
            Tribes[index] = Tribes[index] with { Rapport = value };
            if (!string.Equals(old, value, StringComparison.OrdinalIgnoreCase))
            {
                AddNarrativeText($"{Tribes[index].Name} Rapport changes from {old} to {value}.");
                if (!string.Equals(old, "Allied", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value, "Allied", StringComparison.OrdinalIgnoreCase))
                    AddAlliedBenefitNarrative(Tribes[index].Name);
            }
            SyncPlayAreaHostilityFromRapport();
            RemoveQuietusPriestIfNeeded();
        }
    }

    private void GainAdvancement(string target, IReadOnlyList<JsonElement> flags)
    {
        string? id = target;
        if (string.Equals(target, "ArmorNext", StringComparison.OrdinalIgnoreCase))
            id = _armorSupply.FirstOrDefault()?.Id;

        if (string.IsNullOrWhiteSpace(id) || HasAdvancement(id))
            return;

        var advancement = _catalog?.GetAdvancement(id);
        if (advancement is null)
            return;

        AcquiredAdvancements.Add(advancement);
        _armorSupply.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        _advancementDeck.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        if (HasFlag(flags, "ReshuffleAfterGain"))
            Shuffle(_advancementDeck);

        RefreshAdvancementOffer();
        AddNarrativeText($"Gain {advancement.Name}.");
    }

    private void SeedEchoInternal(string type, string target, string? value)
    {
        if (string.Equals(type, "SeedRandomEcho", StringComparison.OrdinalIgnoreCase))
        {
            var pool = string.Equals(target, "EchoPool", StringComparison.OrdinalIgnoreCase) ? value : target;
            SeedEchoByPool(pool, random: true);
            return;
        }

        if (string.Equals(type, "SeedEcho", StringComparison.OrdinalIgnoreCase))
        {
            var cardId = string.Equals(target, "CardID", StringComparison.OrdinalIgnoreCase) ? value : target;
            SeedSpecificEcho(cardId);
            return;
        }

        var entry = $"{type}:{target}:{value}";
        _seededEchoes.Add(entry);
        _logger.LogInformation("Internal Echo seed: {Entry}", entry);
    }

    private void SeedEchoByPool(string? pool, bool random)
    {
        if (string.IsNullOrWhiteSpace(pool))
            return;
        var candidates = _unseededEchoes.Where(c => string.Equals(c.EchoPool, pool, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0)
            return;
        var card = random ? candidates[Random.Shared.Next(candidates.Count)] : candidates[0];
        _unseededEchoes.Remove(card);
        _campDiscard.Add(card);
        _seededEchoes.Add(card.Id);
        RecordEchoOrigin(card.Id);
        _logger.LogInformation("Internal Echo seeded into Camp discard: {CardId}", card.Id);
    }

    private void SeedSpecificEcho(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return;
        var card = _unseededEchoes.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase));
        if (card is null)
            return;
        _unseededEchoes.Remove(card);
        _campDiscard.Add(card);
        _seededEchoes.Add(card.Id);
        RecordEchoOrigin(card.Id);
        _logger.LogInformation("Internal Echo seeded into Camp discard: {CardId}", card.Id);
    }

    private void RecordEchoOrigin(string echoId)
    {
        var source = CurrentEchoSeedSourceTitle();
        if (!string.IsNullOrWhiteSpace(source))
            _echoOrigins[echoId] = source;
    }

    private string? CurrentEchoSeedSourceTitle()
    {
        if (_activeFlowContext == FlowContext.Camp && PendingCampCard is not null)
        {
            if (string.Equals(PendingCampCard.CardType, "Echo", StringComparison.OrdinalIgnoreCase)
                && _echoOrigins.TryGetValue(PendingCampCard.Id, out var rootOrigin))
                return rootOrigin;
            return PendingCampCard.Title;
        }
        if (_activeFlowContext == FlowContext.Explore && PendingExploreCard is not null)
            return PendingExploreCard.Title;
        if (_activeFlowContext == FlowContext.Arrival && PendingArrivalCard is not null)
            return PendingArrivalCard.Title;

        // Some Echoes are seeded as a consequence of Combat after the source
        // flow has stepped away. Preserve the most meaningful visible card
        // title still associated with that resolution.
        if (PendingArrivalCard is not null)
            return PendingArrivalCard.Title;
        if (PendingExploreCard is not null)
            return PendingExploreCard.Title;
        return null;
    }

    private void SeedHostileEchoForTribe(string tribe)
    {
        var id = NormalizeTribeName(tribe) switch
        {
            "suntouched" => "ECHO-A-01",
            "palehands" => "ECHO-A-02",
            "nightstone" => "ECHO-A-03",
            "quietus" => "ECHO-A-04",
            _ => null
        };
        if (id is not null)
            SeedSpecificEcho(id);
    }

    private bool CampBranchAllowed(string label)
    {
        if (PendingCampCard is null)
            return true;

        var clean = Regex.Replace(label, @"\s*\(\d+\+\)\s*$", string.Empty).Trim();
        return (PendingCampCard.Id, clean) switch
        {
            ("CAMP-003", "Chapel") => !HasBuilding("Chapel") && CanPayCostBundle("1W+1C"),
            ("CAMP-003", "Academy") => !HasBuilding("Academy") && CanPayCostBundle("1S+1C"),
            ("CAMP-004", "Market") => !HasBuilding("Market") && CanPayCostBundle("1W+1C"),
            ("CAMP-004", "Baggage Cart") => !HasBuilding("Baggage Cart") && CanPayCostBundle("1W"),
            ("CAMP-012", "Train Scouts") => !HasScouting && ResourceValue("Research") >= 1,
            ("CAMP-007", "Provision") => ResourceValue("Coin") >= 2 && new[] { "Food", "Wood", "Stone" }.Any(r => CanAddPhysicalResource(r, 1)),
            ("CAMP-007", "Buy 1 additional Resource") => ResourceValue("Coin") >= 1 && new[] { "Food", "Wood", "Stone" }.Any(r => CanAddPhysicalResource(r, 1)),
            ("CAMP-009", "Raise Levies") => ResourceValue("Food") >= 1 && AvailableForceCount("Levy") > 0 && HostCount("Levy") < 6,
            ("CAMP-012", "Appoint Camp Steward") => !HasToken("CampSteward") && ResourceValue("Coin") >= 2,
            ("SCENE-020", "Send in a Fighter") => Host.Any(u => u.Count > 0 && u.Type is "Archers" or "Cavalry" or "Infantry"),
            _ => true
        };
    }

    private bool BranchConditionAllowed(JsonElement branch)
    {
        var input = GetString(branch, "input");
        var op = GetString(branch, "operator");
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(op))
            return true;

        var actual = ResolveInput(input);
        var expected = branch.TryGetProperty("value", out var value) ? JsonScalar(value) : null;
        if (expected is string expectedText && string.Equals(expectedText, "LastRoll", StringComparison.OrdinalIgnoreCase))
            expected = LastNumericRollResult();
        return Compare(actual, op, expected);
    }

    private object? ResolveInput(string input)
    {
        if (input.StartsWith("Player.", StringComparison.OrdinalIgnoreCase))
        {
            var playerField = input[7..];
            if (string.Equals(playerField, "Leadership", StringComparison.OrdinalIgnoreCase))
                return Leadership;
            if (string.Equals(playerField, "Morale", StringComparison.OrdinalIgnoreCase))
                return Morale;
            if (string.Equals(playerField, "HasPriest", StringComparison.OrdinalIgnoreCase))
                return _hasPriest;
            if (string.Equals(playerField, "Tokens", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = ActiveTokens.Concat(PersistentItems.Select(p => p.Name)).ToList();
                if (_hasPriest)
                    tokens.Add("Priest");
                return tokens.ToArray();
            }
            if (string.Equals(playerField, "Buildings", StringComparison.OrdinalIgnoreCase))
                return PersistentItems.Select(p => p.Name).ToArray();
            if (playerField is "Coin" or "Food" or "Wood" or "Stone" or "Research")
                return ResourceValue(playerField);
        }
        if (string.Equals(input, "Player.Leadership", StringComparison.OrdinalIgnoreCase))
            return Leadership;
        if (string.Equals(input, "Host.TotalUnits", StringComparison.OrdinalIgnoreCase))
            return TotalHostUnits();
        if (string.Equals(input, "Host.Infantry", StringComparison.OrdinalIgnoreCase))
            return HostCount("Infantry");
        if (string.Equals(input, "Host.InfantryClass", StringComparison.OrdinalIgnoreCase))
            return HostCount("Infantry") + HostCount("Mercenaries");
        if (string.Equals(input, "Player.Advancements", StringComparison.OrdinalIgnoreCase))
            return AcquiredAdvancements.Select(a => a.Id).ToArray();
        if (string.Equals(input, "Combat.Outcome", StringComparison.OrdinalIgnoreCase))
            return _flowVars.TryGetValue("Combat.Outcome", out var outcome) ? outcome : null;
        if (string.Equals(input, "MarketBuyableResources", StringComparison.OrdinalIgnoreCase))
            return Market.Where(m => CanMarketBuy(m.Resource, 1)).Select(m => m.Resource).ToArray();
        if (string.Equals(input, "MarketSellableResources", StringComparison.OrdinalIgnoreCase))
            return Market.Where(m => ResourceValue(m.Resource) > 0).Select(m => m.Resource).ToArray();
        if (input.StartsWith("CanMarketBuy:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split(':');
            var qty = parts.Length > 2 && int.TryParse(parts[2], out var q) ? q : 1;
            var coinModifier = parts.Length > 3 && int.TryParse(parts[3], out var m) ? m : 0;
            return parts.Length > 1 && CanMarketBuy(parts[1], qty, coinModifier);
        }
        if (input.StartsWith("CanPurchaseResource:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split(':');
            if (parts.Length < 4)
                return false;
            var qty = int.TryParse(parts[2], out var q) ? q : 1;
            var cost = int.TryParse(parts[3], out var c) ? c : 0;
            return ResourceValue("Coin") >= cost && CanAddPhysicalResource(parts[1], qty);
        }
        if (string.Equals(input, "CanSpendRollResource", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(_lastRollText) && ResourceValue(_lastRollText) >= 1;
        if (string.Equals(input, "Quietus.Encountered", StringComparison.OrdinalIgnoreCase))
            return !string.Equals(GetTribeRapport("Quietus"), "Unmet", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(input, "Quietus.Rapport", StringComparison.OrdinalIgnoreCase))
            return GetTribeRapport("Quietus");
        if (string.Equals(input, "SelectedTribe.Rapport", StringComparison.OrdinalIgnoreCase))
        {
            var selected = _flowVars.TryGetValue("SelectedTribe", out var tribe) ? Convert.ToString(tribe) : null;
            return string.IsNullOrWhiteSpace(selected) ? "Unmet" : GetTribeRapport(selected!);
        }
        if (string.Equals(input, "Rapport.NearestTribe", StringComparison.OrdinalIgnoreCase))
            return NearestTribeRapport();
        if (string.Equals(input, "Player.ResourcesMatchingAdjacentTerrain", StringComparison.OrdinalIgnoreCase))
            return ResourceValue(AdjacentTerrainResource());
        if (string.Equals(input, "CanRepairBaggageSlot", StringComparison.OrdinalIgnoreCase))
            return ResourceValue("Wood") >= 1 && Baggage.Any(b => b.Damaged);
        if (string.Equals(input, "Firepots.Location", StringComparison.OrdinalIgnoreCase))
            return HasToken("Firepots") && Baggage.Any(b => string.Equals(b.Contents, "Firepots", StringComparison.OrdinalIgnoreCase))
                ? "BaggageTrain"
                : "NotInBaggageTrain";
        if (string.Equals(input, "SelectedAdjacentSlot.ContainsResource", StringComparison.OrdinalIgnoreCase))
        {
            var position = ToInt(_flowVars.TryGetValue("SelectedAdjacentSlot", out var selectedSlot) ? selectedSlot : 0);
            var slot = Baggage.FirstOrDefault(b => b.Position == position);
            return slot is not null && IsPhysicalResource(slot.Contents ?? string.Empty) && slot.Quantity > 0;
        }
        if (string.Equals(input, "CurrentTribe.Rapport", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(_currentArrivalTribe) ? "Unmet" : GetTribeRapport(_currentArrivalTribe);
        if (input.EndsWith(".Rapport", StringComparison.OrdinalIgnoreCase))
            return GetTribeRapport(input[..^8]);
        if (string.Equals(input, "CurrentClearing.HasBarbarianIcon", StringComparison.OrdinalIgnoreCase))
        {
            var clearing = PlayArea.LastOrDefault(p => p.Kind == PlayAreaKind.Clearing);
            return clearing?.HasBarbarianIcon ?? false;
        }
        if (_flowVars.TryGetValue(input, out var variable))
            return variable;
        return null;
    }

    private JsonElement? FindMatchingBranch(JsonElement node, object? actual)
    {
        if (!node.TryGetProperty("branches", out var branches))
            return null;
        foreach (var branch in branches.EnumerateArray())
        {
            var op = GetString(branch, "operator");
            if (string.IsNullOrWhiteSpace(op))
                continue;
            var expected = branch.TryGetProperty("value", out var value) ? JsonScalar(value) : null;
            if (Compare(actual, op, expected))
                return branch;
        }
        return null;
    }

    private JsonElement? FindAlwaysBranch(JsonElement node)
    {
        if (!node.TryGetProperty("branches", out var branches))
            return null;
        foreach (var branch in branches.EnumerateArray())
        {
            if (string.Equals(GetString(branch, "operator"), "ALWAYS", StringComparison.OrdinalIgnoreCase))
                return branch;
        }
        return null;
    }

    private string? FindMatchingBranchNext(JsonElement node, object? actual)
        => FindMatchingBranch(node, actual) is { } branch ? GetString(branch, "next") : null;

    private string? FindAlwaysBranchNext(JsonElement node)
        => FindAlwaysBranch(node) is { } branch ? GetString(branch, "next") : null;

    private static bool Compare(object? actual, string op, object? expected)
    {
        if (string.Equals(op, "ALWAYS", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(op, "ANY", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(op, "NOT_EMPTY", StringComparison.OrdinalIgnoreCase))
            return actual switch
            {
                Array arrayValue => arrayValue.Length > 0,
                System.Collections.ICollection c => c.Count > 0,
                string s => !string.IsNullOrWhiteSpace(s),
                _ => actual is not null
            };
        if (string.Equals(op, "HAS", StringComparison.OrdinalIgnoreCase))
            return actual is IEnumerable<string> values && values.Any(v => string.Equals(v, Convert.ToString(expected), StringComparison.OrdinalIgnoreCase));
        if (string.Equals(op, "NOT_HAS", StringComparison.OrdinalIgnoreCase))
            return actual is not IEnumerable<string> values || !values.Any(v => string.Equals(v, Convert.ToString(expected), StringComparison.OrdinalIgnoreCase));
        if (string.Equals(op, "HAS_ANY", StringComparison.OrdinalIgnoreCase))
        {
            var expectedValues = (Convert.ToString(expected) ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return actual is IEnumerable<string> values && expectedValues.Any(e => values.Any(v => string.Equals(v, e, StringComparison.OrdinalIgnoreCase)));
        }
        if (string.Equals(op, "HAS_NONE", StringComparison.OrdinalIgnoreCase))
        {
            var expectedValues = (Convert.ToString(expected) ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return actual is not IEnumerable<string> values || expectedValues.All(e => !values.Any(v => string.Equals(v, e, StringComparison.OrdinalIgnoreCase)));
        }
        if (string.Equals(op, "IN", StringComparison.OrdinalIgnoreCase))
        {
            var expectedValues = (Convert.ToString(expected) ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return expectedValues.Any(v => string.Equals(v, Convert.ToString(actual), StringComparison.OrdinalIgnoreCase));
        }
        if (string.Equals(op, "BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            var parts = (Convert.ToString(expected) ?? string.Empty).Split("..", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 2
                && double.TryParse(Convert.ToString(actual), out var betweenActual)
                && double.TryParse(parts[0], out var low)
                && double.TryParse(parts[1], out var high)
                && betweenActual >= low && betweenActual <= high;
        }
        if (string.Equals(op, "EQ", StringComparison.OrdinalIgnoreCase))
        {
            if (actual is bool ab && expected is bool eb)
                return ab == eb;
            return string.Equals(Convert.ToString(actual), Convert.ToString(expected), StringComparison.OrdinalIgnoreCase);
        }
        if (string.Equals(op, "NE", StringComparison.OrdinalIgnoreCase))
        {
            if (actual is bool actualBool && expected is bool expectedBool)
                return actualBool != expectedBool;
            return !string.Equals(Convert.ToString(actual), Convert.ToString(expected), StringComparison.OrdinalIgnoreCase);
        }

        if (!double.TryParse(Convert.ToString(actual), out var actualNumber) || !double.TryParse(Convert.ToString(expected), out var expectedNumber))
            return false;
        return op.ToUpperInvariant() switch
        {
            "GTE" => actualNumber >= expectedNumber,
            "GT" => actualNumber > expectedNumber,
            "LTE" => actualNumber <= expectedNumber,
            "LT" => actualNumber < expectedNumber,
            _ => false
        };
    }

    private object? RollComparableValue()
    {
        if (!string.IsNullOrWhiteSpace(_lastRollText))
            return _lastRollText;
        if (_lastRollResults.Count == 1)
            return _lastRollResults[0];
        return _lastRollResults.Sum();
    }

    private object? ResolveActionValue(JsonElement value)
    {
        var scalar = JsonScalar(value);
        if (scalar is not string text)
            return scalar;

        if (string.Equals(text, "Roll.Result", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(_lastRollText) ? _lastRollText : LastNumericRollResult();
        if (string.Equals(text, "Roll.DoublePairs", StringComparison.OrdinalIgnoreCase))
            return _lastRollResults.GroupBy(x => x).Sum(g => g.Count() / 2);
        if (string.Equals(text, "SelectedCount", StringComparison.OrdinalIgnoreCase))
            return _flowVars.TryGetValue("SelectedCount", out var count) ? count : 0;
        if (string.Equals(text, "SelectedCount*2", StringComparison.OrdinalIgnoreCase))
            return 2 * ToInt(_flowVars.TryGetValue("SelectedCount", out var count) ? count : 0);
        if (string.Equals(text, "LastRoll", StringComparison.OrdinalIgnoreCase))
            return LastNumericRollResult();
        if (string.Equals(text, "All", StringComparison.OrdinalIgnoreCase))
            return "All";
        if (_flowVars.TryGetValue(text, out var variableValue))
            return variableValue;
        return text;
    }

    private string ResolveTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;
        if (_flowVars.TryGetValue(target, out var selected))
        {
            // Multi-select variables are consumed by actions as structured data.
            // Preserve the variable name instead of stringifying the dictionary.
            if (selected is Dictionary<string, int>)
                return target;
            return selected is null ? string.Empty : Convert.ToString(selected) ?? string.Empty;
        }
        if (string.Equals(target, "Roll.Result", StringComparison.OrdinalIgnoreCase))
            return _lastRollText ?? LastNumericRollResult().ToString();
        if (string.Equals(target, "ResourceMatchingAdjacentTerrain", StringComparison.OrdinalIgnoreCase))
            return AdjacentTerrainResource();
        if (string.Equals(target, "Rapport:SelectedTribe", StringComparison.OrdinalIgnoreCase))
        {
            var selectedTribe = _flowVars.TryGetValue("SelectedTribe", out var tribe) ? Convert.ToString(tribe) : null;
            return string.IsNullOrWhiteSpace(selectedTribe) ? target : $"Rapport:{selectedTribe}";
        }
        return target;
    }

    private static string PickNarrative(params string[] lines)
    {
        var usable = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return usable.Length == 0 ? string.Empty : usable[Random.Shared.Next(usable.Length)];
    }

    private string ExploreTravelNarrative(TerrainType terrain, bool firstMarch, bool forced)
    {
        if (firstMarch)
        {
            return terrain switch
            {
                TerrainType.Plains => PickNarrative(
                    "The Host leaves the Starting Clearing and advances into open country.",
                    "The Starting Clearing falls behind as the Host steps out beneath a wide, open sky."),
                TerrainType.Forest => PickNarrative(
                    "The Host leaves the Starting Clearing and enters the forest road.",
                    "The last familiar ground disappears behind the Host as the road narrows beneath the trees."),
                TerrainType.Mountain => PickNarrative(
                    "The Host leaves the Starting Clearing and climbs toward the high country.",
                    "The march begins in earnest, the Host trading familiar ground for a road that climbs into stone."),
                _ => "The Host leaves the Starting Clearing and takes to the road."
            };
        }

        if (forced)
        {
            return terrain switch
            {
                TerrainType.Plains => PickNarrative("The Host is forced onward into open country.", "With no time to settle, the Host is driven back onto the road and out across the plains."),
                TerrainType.Forest => PickNarrative("The Host is forced onward beneath the forest canopy.", "Denied a pause, the Host presses into the trees while the last settlement vanishes behind the branches."),
                TerrainType.Mountain => PickNarrative("The Host is forced onward into the high country.", "There is no welcome behind you, only the climb ahead. The Host pushes into the high country."),
                _ => "The Host is forced farther along the road."
            };
        }

        return terrain switch
        {
            TerrainType.Plains => PickNarrative(
                "The Host advances into open country.",
                "The road spills onto broad plains where every distant movement looks important for a moment.",
                "Open country stretches ahead, generous with distance and stingy with shelter."),
            TerrainType.Forest => PickNarrative(
                "The Host enters the forest road.",
                "The trees close around the column, swallowing the road a few dozen paces at a time.",
                "The march turns green and dim as the Host follows the road beneath the canopy."),
            TerrainType.Mountain => PickNarrative(
                "The Host climbs toward the high country.",
                "The road tilts upward, and conversation thins as the climb begins to demand everyone's breath.",
                "Stone replaces soil beneath the march as the Host works its way into the high country."),
            _ => "The Host pushes farther along the road."
        };
    }

    private void AddUneventfulExploreNarrative()
    {
        var text = CurrentExploreTerrain switch
        {
            TerrainType.Plains => PickNarrative(
                "The miles pass quietly. Nothing on the open road demands the Host's attention.",
                "The plains offer distance, wind, and very little else. For once, the march is uneventful.",
                "No riders appear on the horizon and no trouble finds the road. The Host keeps moving."),
            TerrainType.Forest => PickNarrative(
                "The woods remain only woods. No ambush, omen, or opportunity interrupts the march.",
                "Branches scrape carts and birds complain overhead, but nothing of consequence troubles the Host.",
                "The forest keeps its secrets today. The Host passes beneath the trees without incident."),
            TerrainType.Mountain => PickNarrative(
                "The climb is hard enough without adding drama. The high road offers no encounter beyond stone, wind, and tired legs.",
                "Nothing waits around the next bend except another bend. The Host crosses the heights without incident.",
                "The mountains make the march difficult, but not eventful. That is a distinction the Host is happy to accept."),
            _ => PickNarrative(
                "The road is quiet. The Host travels without incident.",
                "Nothing of consequence interrupts the march.")
        };

        Chronicle.Add(new(
            CalendarPhaseStamp("Explore"),
            text,
            ChronicleTone.Narrative,
            "Uneventful Travel"));
    }

    private string ArrivalSettlementNarrative(string tribe)
    {
        var name = string.IsNullOrWhiteSpace(tribe) ? "tribe" : tribe;
        return NormalizeTribeName(name) switch
        {
            "suntouched" => PickNarrative(
                $"The Host reaches a settlement held by the {name}.",
                $"Sun-marked banners and watchful faces announce {name} ground before the Host reaches the first buildings."),
            "nightstone" => PickNarrative(
                $"The Host reaches a settlement held by the {name}.",
                $"Dark stonework and guarded approaches leave little doubt whose settlement lies ahead: the {name}."),
            "palehands" => PickNarrative(
                $"The Host reaches a settlement held by the {name}.",
                $"Pale faces watch from the settlement as the Host approaches. The {name} have seen you coming."),
            "quietus" => PickNarrative(
                $"The Host reaches a settlement held by the {name}.",
                $"The road enters a settlement of the {name}, where the Host is noticed long before anyone chooses to acknowledge it."),
            _ => $"The Host reaches a settlement held by the {name}."
        };
    }

    private string FirstContactNarrative(string tribe, string rapport)
    {
        var name = string.IsNullOrWhiteSpace(tribe) ? "tribe" : tribe;
        return rapport switch
        {
            "Hostile" => PickNarrative(
                $"First contact goes badly. The {name} receive the Host with open hostility.",
                $"Whatever goodwill might have existed dies quickly. The {name} make it plain that the Host is not welcome.",
                $"The first words do not become second words. The {name} meet the Host with unmistakable hostility."),
            "Friendly" => PickNarrative(
                $"First contact goes unusually well. The {name} receive the Host with unexpected warmth.",
                $"Suspicion gives way faster than expected. The {name} greet the Host with genuine warmth.",
                $"The first exchange finds common ground. The {name} seem pleased, perhaps even relieved, to receive the Host."),
            _ => PickNarrative(
                $"The {name} receive the Host warily, but without open hostility.",
                $"The {name} keep their distance and their judgment to themselves. For now, the meeting remains civil.",
                $"Neither welcome nor threat greets the Host. The {name} watch carefully and wait to see what you will do.")
        };
    }

    private static string TributeDemandNarrative(string tribe, int amount, string resource)
        => PickNarrative(
            $"The {tribe} demand {amount} {resource} in tribute.",
            $"Passage has a price. The {tribe} require {amount} {resource} before the Host proceeds.",
            $"The terms are simple, if not generous: {amount} {resource} for the {tribe}.");

    private static string TributePaidNarrative(string tribe, int amount, string resource)
        => PickNarrative(
            $"The Host pays {amount} {resource} in tribute.",
            $"The Host surrenders {amount} {resource}, and the {tribe} accept the tribute.",
            $"{amount} {resource} changes hands. Whatever the {tribe} think of the Host, the tribute is paid.");

    private string ArrivalTestFlavor(string? sourceId, string? testLabel, bool passed, string authoredPlayerText)
    {
        if (string.Equals(sourceId, "ARRIVAL-018", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(testLabel)
            && testLabel.Contains("Buy the Jars", StringComparison.OrdinalIgnoreCase))
        {
            return passed
                ? PickNarrative(
                    "The keepers exchange a few doubtful looks, then start wrapping jars for the road. Apparently your bargaining was better than it sounded.",
                    "The price is settled without bloodshed or broken pottery, which is more than can be said for some negotiations on this road.")
                : PickNarrative(
                    "One of your people picks up a jar while arguing the price, fumbles it, and sends honey and pottery across the ground. The keepers charge you a Coin anyway.",
                    "The bargaining collapses at almost the same moment a jar does. The Sun-Touched are unmoved by explanations and insist on a Coin for the damage.",
                    "A demonstration of the jars becomes a demonstration of gravity. The honey is lost, the keeper is furious, and the Host is still charged a Coin.");
        }

        var tribe = string.IsNullOrWhiteSpace(_currentArrivalTribe) ? "The locals" : $"The {_currentArrivalTribe}";
        if (passed)
        {
            return NormalizeTribeName(_currentArrivalTribe ?? string.Empty) switch
            {
                "suntouched" => PickNarrative(authoredPlayerText, $"{tribe} weigh your words, then give a small nod. The arrangement will stand.", $"{tribe} accept the proposal after a long look over the Host and its baggage."),
                "nightstone" => PickNarrative(authoredPlayerText, $"{tribe} offer little ceremony, but the answer is yes. Terms are accepted."),
                "palehands" => PickNarrative(authoredPlayerText, $"{tribe} confer in low voices before signaling agreement. Nothing about it feels casual."),
                "quietus" => PickNarrative(authoredPlayerText, $"{tribe} answer with the smallest possible sign of assent. It is enough."),
                _ => PickNarrative(authoredPlayerText, "The proposal is accepted, and the tension eases by a degree.")
            };
        }

        return NormalizeTribeName(_currentArrivalTribe ?? string.Empty) switch
        {
            "suntouched" => PickNarrative(authoredPlayerText, $"{tribe} are not persuaded. The answer comes back firm and immediate.", $"{tribe} let you finish speaking before refusing every part of the proposal."),
            "nightstone" => PickNarrative(authoredPlayerText, $"{tribe} reject the terms with the finality of a door being barred."),
            "palehands" => PickNarrative(authoredPlayerText, $"{tribe} refuse without raising their voices. Somehow that makes the refusal feel sharper."),
            "quietus" => PickNarrative(authoredPlayerText, $"{tribe} offer no argument and little explanation. The answer is simply no."),
            _ => PickNarrative(authoredPlayerText, "The proposal fails to move them. Whatever happens next will require another answer.")
        };
    }

    private void QueueOutcomeNarrative(string? canonicalText, string? browserFlavor)
    {
        _pendingOutcomeText = string.IsNullOrWhiteSpace(canonicalText) ? null : canonicalText.Trim();
        _pendingBrowserFlavor = string.IsNullOrWhiteSpace(browserFlavor) ? null : browserFlavor.Trim();
    }

    private void FlushPendingOutcomeNarrative()
    {
        var canonical = _pendingOutcomeText;
        var flavor = _pendingBrowserFlavor;
        _pendingOutcomeText = null;
        _pendingBrowserFlavor = null;

        if (!string.IsNullOrWhiteSpace(canonical))
            AddNarrativeText(canonical);
        if (!string.IsNullOrWhiteSpace(flavor))
            AddBrowserFlavorText(flavor);
    }

    private void AddBrowserFlavorText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        Chronicle.Add(new(CalendarPhaseStamp("Chronicle"), text.Trim(), ChronicleTone.Narrative));
    }

    private void AddNarrativeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Chronicle text is append-only. Once the player has seen a line,
        // later resolution never rewrites or replaces it.
        Chronicle.Add(new(CalendarStamp, text.Trim(), ChronicleTone.Result));
    }

    private void AddSystemText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        Chronicle.Add(new(CalendarStamp, text.Trim(), ChronicleTone.System));
    }

    public bool IsChronicleDecisionActive(int index)
        => _activeChronicleDecisionIndex == index && FlowPrompt is not null;

    private void RecordChronicleDecisionSelection(string optionId)
    {
        if (_activeChronicleDecisionIndex is not int index || index < 0 || index >= Chronicle.Count)
            return;

        var entry = Chronicle[index];
        if (entry.Choices is null || !entry.Choices.Any(c => string.Equals(c.Id, optionId, StringComparison.OrdinalIgnoreCase)))
            return;

        Chronicle[index] = entry with { SelectedChoiceId = optionId };
        _activeChronicleDecisionIndex = null;
    }

    private void AddPlayerText(JsonElement element)
    {
        var playerText = GetString(element, "player_text");
        if (string.IsNullOrWhiteSpace(playerText))
            return;

        AddNarrativeText(ReplacePlaceholders(playerText));
    }

    private string ReplacePlaceholders(string text)
    {
        var result = text;
        result = result.Replace("{Roll.Result}", _lastRollText ?? LastNumericRollResult().ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{EnemyHost}", CurrentArrivalEnemyHost(), StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private string ActiveFlowHeading()
        => _activeFlowContext switch
        {
            FlowContext.Arrival => PendingArrivalCard?.Title ?? "Arrival",
            FlowContext.Explore => PendingExploreCard?.Title ?? "Explore",
            FlowContext.Camp => PendingCampCard?.Title ?? "Camp",
            _ => PendingCampCard?.Title ?? PendingArrivalCard?.Title ?? PendingExploreCard?.Title ?? "War Chronicle"
        };

    private static string BaggageContentsLabel(BaggageSlot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Contents))
            return "Empty";
        return string.Equals(slot.Contents, "Food", StringComparison.OrdinalIgnoreCase) && slot.Quantity > 1
            ? $"Food ({slot.Quantity})"
            : slot.Contents;
    }

    private bool CanMarketBuy(string resource, int quantity, int coinModifier = 0)
    {
        var market = Market.FirstOrDefault(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        var cost = market is null ? int.MaxValue : Math.Max(0, (market.BuyPrice * quantity) + coinModifier);
        return market is not null
            && ResourceValue("Coin") >= cost
            && CanAddPhysicalResource(resource, quantity);
    }

    private bool CanAddPhysicalResource(string resource, int quantity)
    {
        var copy = Baggage.ToList();
        return TryPackResource(copy, resource, quantity) == quantity;
    }

    private int AddPhysicalResource(string resource, int quantity)
    {
        var added = TryPackResource(Baggage, resource, quantity);
        if (added > 0)
            AdjustResource(resource, added);
        return added;
    }

    private static int TryPackResource(List<BaggageSlot> baggage, string resource, int quantity)
    {
        var added = 0;
        for (var n = 0; n < quantity; n++)
        {
            var placed = false;
            if (string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase))
            {
                var foodIndex = baggage.FindIndex(b => string.Equals(b.Contents, "Food", StringComparison.OrdinalIgnoreCase) && b.Quantity < 2 && !b.Damaged);
                if (foodIndex >= 0)
                {
                    baggage[foodIndex] = baggage[foodIndex] with { Quantity = baggage[foodIndex].Quantity + 1 };
                    placed = true;
                }
            }

            if (!placed)
            {
                var emptyIndex = baggage.FindIndex(b => string.IsNullOrWhiteSpace(b.Contents) && !b.Damaged);
                if (emptyIndex >= 0)
                {
                    baggage[emptyIndex] = baggage[emptyIndex] with { Contents = resource, Quantity = 1 };
                    placed = true;
                }
            }

            if (!placed)
                break;
            added++;
        }
        return added;
    }

    private int RemovePhysicalResource(string resource, int quantity)
    {
        var removed = 0;
        for (var n = 0; n < quantity; n++)
        {
            var index = Baggage.FindLastIndex(b => string.Equals(b.Contents, resource, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                break;

            if (string.Equals(resource, "Food", StringComparison.OrdinalIgnoreCase) && Baggage[index].Quantity > 1)
                Baggage[index] = Baggage[index] with { Quantity = Baggage[index].Quantity - 1 };
            else
                Baggage[index] = Baggage[index] with { Contents = null, Quantity = 1 };

            removed++;
        }

        if (removed > 0)
            AdjustResource(resource, -removed);
        return removed;
    }

    private void AdjustResource(string name, int delta)
    {
        var index = Resources.FindIndex(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Resources.Add(new(name, Math.Max(0, delta)));
            return;
        }
        Resources[index] = Resources[index] with { Value = Math.Max(0, Resources[index].Value + delta) };
    }

    private void ResolveAcademyResearchBonus(int researchGained)
    {
        if (researchGained <= 0 || !HasBuilding("Academy"))
            return;

        var rolls = Enumerable.Range(0, researchGained).Select(_ => Random.Shared.Next(1, 7)).ToArray();
        var bonus = rolls.Count(r => r >= 4);
        AddSystemText($"Academy roll{(rolls.Length == 1 ? string.Empty : "s")}: {string.Join(", ", rolls)}. Each 4–6 grants +1 Research.");
        if (bonus <= 0)
        {
            AddNarrativeText("The Academy yields no additional Research this time.");
            return;
        }

        AdjustResource("Research", bonus);
        AddNarrativeText($"The Academy grants +{bonus} additional Research.");
    }

    private int HostCount(string type) =>
        Host.FirstOrDefault(u => string.Equals(u.Type, type, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

    private void SetHostCount(string type, int count)
    {
        var index = Host.FindIndex(u => string.Equals(u.Type, type, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            Host[index] = Host[index] with { Count = Math.Max(0, count) };
    }

    private int LoseHostUnit(string type, int amount, bool applyPainkiller = true)
    {
        var index = Host.FindIndex(u => string.Equals(u.Type, type, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return 0;

        var attempted = Math.Min(Host[index].Count, amount);
        if (attempted <= 0)
            return 0;

        var prevented = 0;
        if (applyPainkiller
            && HasToken("Painkiller")
            && type is "Archers" or "Cavalry" or "Infantry" or "Levy" or "Mercenaries" or "Hedge Knights")
        {
            var rolls = new List<int>();
            for (var i = 0; i < attempted; i++)
            {
                var roll = Random.Shared.Next(1, 7);
                rolls.Add(roll);
                if (roll >= 5)
                    prevented++;
            }

            AddNarrativeText($"Painkiller: roll {string.Join(", ", rolls)}. Prevent {prevented} of {attempted} {SingularizeUnitLabel(type, attempted)} loss{(attempted == 1 ? string.Empty : "es")}.");
        }

        var lost = attempted - prevented;
        if (lost <= 0)
            return 0;

        Host[index] = Host[index] with { Count = Host[index].Count - lost };

        if (type is "Archers" or "Cavalry" or "Infantry" or "Levy")
            AdjustAvailableForce(type, lost);
        return lost;
    }

    private int TotalHostUnits() => Host.Sum(u => u.Count);

    private int AvailableForceCount(string type) =>
        AvailableForces.FirstOrDefault(f => string.Equals(f.Type, type, StringComparison.OrdinalIgnoreCase))?.Available ?? 0;

    private void AdjustAvailableForce(string type, int delta)
    {
        var index = AvailableForces.FindIndex(f => string.Equals(f.Type, type, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            AvailableForces[index] = AvailableForces[index] with { Available = Math.Max(0, AvailableForces[index].Available + delta) };
    }

    private bool IsAdvancementAvailable(string id)
    {
        if (HasAdvancement(id))
            return false;
        return _armorSupply.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
            || _advancementDeck.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasAdvancement(string id) =>
        AcquiredAdvancements.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    private string GetTribeRapport(string name)
    {
        var normalized = NormalizeTribeName(name);
        return Tribes.FirstOrDefault(t => NormalizeTribeName(t.Name) == normalized)?.Rapport ?? "Unmet";
    }

    private static string NormalizeTribeName(string name) =>
        name.Replace("The ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" Clan", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToLowerInvariant();

    private static string ShiftRapport(string rapport, int delta)
    {
        var states = new[] { "Hostile", "Neutral", "Friendly", "Allied" };
        var index = Array.FindIndex(states, s => string.Equals(s, rapport, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return rapport;
        return states[Math.Clamp(index + delta, 0, states.Length - 1)];
    }

    private static int RapportDrm(string rapport) => rapport.ToLowerInvariant() switch
    {
        "hostile" => -1,
        "neutral" => 0,
        "friendly" => 1,
        "allied" => 2,
        _ => 0
    };

    private int CountRollsInRange(string range)
    {
        var (low, high) = ParseRange(range);
        return _lastRollResults.Count(r => r >= low && r <= high);
    }

    private static (int Low, int High) ParseRange(string range)
    {
        var parts = range.Split("..", StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var low) && int.TryParse(parts[1], out var high))
            return (low, high);
        return (int.MinValue, int.MaxValue);
    }

    private int LastNumericRollResult() => _lastRollResults.Count == 0 ? 0 : _lastRollResults[0];

    private static bool IsPhysicalResource(string name) =>
        name.Equals("Food", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Wood", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Stone", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnitType(string name) =>
        name.Equals("Archers", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Cavalry", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Infantry", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Levy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Mercenaries", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Hedge Knights", StringComparison.OrdinalIgnoreCase);

    private TerrainType DrawTerrain()
    {
        if (_terrainDeck.Count == 0)
        {
            _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Plains, 10));
            _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Forest, 8));
            _terrainDeck.AddRange(Enumerable.Repeat(TerrainType.Mountain, 6));
            Shuffle(_terrainDeck);
            _logger.LogInformation("Terrain deck reshuffled.");
        }

        var terrain = _terrainDeck[0];
        _terrainDeck.RemoveAt(0);
        return terrain;
    }

    private void AdvanceTime(int amount)
    {
        amount = Math.Max(0, amount);
        if (amount == 0)
            return;

        var before = Time;
        var target = before + amount;

        // Time may carry across Spring and Summer while End of Season is
        // pending. The calendar has 33 visible spaces; Time 34 represents
        // moving past the End of Autumn space so that Autumn can actually
        // cross its threshold rather than triggering merely by landing on it.
        target = Math.Min(target, 34);

        Time = target;

        var advanced = Time - before;
        if (advanced > 0)
            AddSystemText($"Advance {advanced} Time.");

        var threshold = SeasonThreshold(Season);
        if (!_endOfSeasonPending && before <= threshold && Time > threshold)
        {
            if (Year >= 3 && string.Equals(Season, "Autumn", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Finale triggered immediately at Time {Time} during {Phase}.", Time, Phase);
                EnterFinale();
                return;
            }

            _endOfSeasonPending = true;
            _endOfSeasonName = Season;
            _endOfSeasonTriggerPhase = Phase;
            _logger.LogInformation("End of {Season} became pending at Time {Time} during {Phase}.", Season, Time, Phase);
        }
    }

    private static int SeasonThreshold(string season) => season.ToLowerInvariant() switch
    {
        "spring" => 11,
        "summer" => 22,
        "autumn" => 33,
        _ => 33
    };

    private bool HasToken(string name) => ActiveTokens.Contains(name);

    private static string PersistentTokenRulesText(string token) => token switch
    {
        "Firepots" => "Before the first Combat round, you may remove Firepots and roll 3d6. Each 4+ inflicts 1 casualty as if from a Cavalry attack.",
        "Painkiller" => "Whenever a Military Unit or Levy would be lost from any game effect, roll 1d6 for that loss. On 5–6, prevent it.",
        "ForcedMarch" or "Forced March" => "While active, normal Explore costs 1 Time instead of 2.",
        "ReignInBlood" or "Reign in Blood" => "While active, the Host may not Disengage from Combat.",
        "Scouting" => "When Exploring, draw 2 Terrain cards and choose 1.",
        "CampSteward" or "Camp Steward" => "During Work, you may instead gain 1 Resource matching the adjacent Terrain or buy 1 Resource for 1 Coin less than its Market Buy value.",
        _ => string.Empty
    };

    private void AddAlliedBenefitNarrative(string tribeName)
    {
        AddNarrativeText($"{tribeName} is now Allied: gain +2 DRM on tests involving this tribe, and its Settlements count as Controlled Settlements for Camp cards. Some Arrival and Echo effects may grant additional Allied benefits.");
        if (NormalizeTribeName(tribeName) == NormalizeTribeName("Quietus"))
            AddNarrativeText("While the Quietus are Allied, certain Quietus Arrival effects can provide a Priest if the Host does not already have one.");
    }

    private static string DisplayTokenName(string token) => token switch
    {
        "AncientPathway" => "Ancient Pathway",
        "HighOverlookClearRoad" => "Clear Road",
        "HighOverlookScoutApproach" => "Scout the Approach",
        "ReignInBlood" => "Reign in Blood",
        "ForcedMarch" => "Forced March",
        "CampSteward" => "Camp Steward",
        _ => token
    };

    private int CurrentTribeRapportDrm()
        => string.IsNullOrWhiteSpace(_currentArrivalTribe) ? 0 : RapportDrm(GetTribeRapport(_currentArrivalTribe));

    private bool CurrentTribeIsHostile()
        => !string.IsNullOrWhiteSpace(_currentArrivalTribe)
            && string.Equals(GetTribeRapport(_currentArrivalTribe), "Hostile", StringComparison.OrdinalIgnoreCase);

    private void SyncPlayAreaHostilityFromRapport()
    {
        for (var i = 0; i < PlayArea.Count; i++)
        {
            var clearing = PlayArea[i];
            if (clearing.Kind != PlayAreaKind.Clearing
                || string.IsNullOrWhiteSpace(clearing.Controller)
                || string.Equals(clearing.Controller, "Player", StringComparison.OrdinalIgnoreCase))
                continue;

            var tribe = Tribes.FirstOrDefault(t =>
                NormalizeTribeName(t.Name) == NormalizeTribeName(clearing.Controller));
            if (tribe is null)
                continue;

            PlayArea[i] = clearing with
            {
                IsHostile = string.Equals(tribe.Rapport, "Hostile", StringComparison.OrdinalIgnoreCase)
            };
        }
    }

    private void RemoveQuietusPriestIfNeeded()
    {
        if (!_hasPriest || string.IsNullOrWhiteSpace(_priestSource)
            || NormalizeTribeName(_priestSource) != NormalizeTribeName("Quietus"))
            return;

        if (!string.Equals(GetTribeRapport("Quietus"), "Allied", StringComparison.OrdinalIgnoreCase))
        {
            _hasPriest = false;
            _priestSource = null;
            AddNarrativeText("The Quietus Priest leaves the Host as the alliance falters.");
        }
    }

    private string CurrentArrivalEnemyHost()
    {
        var text = PendingArrivalCard?.Host ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return "Enemy Host";

        var pattern = $@"(?i)Y{Year}:\s*(.*?)(?=\s*-\s*Y\d:|$)";
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : text.Replace("Enemy Host", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }

    private void DamageBaggageSlot(int position)
    {
        var index = Baggage.FindIndex(b => b.Position == position && !b.Damaged);
        if (index < 0)
            return;
        Baggage[index] = Baggage[index] with { Damaged = true };
        AddNarrativeText($"Baggage Train space {position} is damaged.");
    }

    private void RepairBaggageSlot(int position)
    {
        var index = Baggage.FindIndex(b => b.Position == position && b.Damaged);
        if (index < 0)
            return;
        Baggage[index] = Baggage[index] with { Damaged = false };
        AddNarrativeText($"The Host spends 1 Wood repairing Baggage Train space {position}.");
    }

    private void CaptureCurrentSettlement()
    {
        var index = PlayArea.FindLastIndex(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        if (index < 0)
            return;

        var tribe = _currentArrivalTribe;
        PlayArea[index] = PlayArea[index] with { Controller = "Player", IsHostile = false };
        AddNarrativeText("The Host takes control of the settlement.");
        if (!string.IsNullOrWhiteSpace(tribe))
        {
            SeedHostileEchoForTribe(tribe);
            _logger.LogInformation("Internal Hostile Echo seeded after capturing {Tribe} settlement.", tribe);
        }
    }

    private void PresentArrivalForcedCombat(string? leadIn = null)
    {
        if (!string.IsNullOrWhiteSpace(leadIn))
            AddNarrativeText(leadIn);

        _specialPrompt = "ArrivalForcedCombat";
        _arrivalCombatResolved = false;
        _combatResumeMode = CombatResumeMode.ArrivalForced;
        _combatResumeNextNode = null;
        var enemyName = string.IsNullOrWhiteSpace(_currentArrivalTribe) ? "Tribal Host" : _currentArrivalTribe;
        BeginInteractiveCombat(
            enemyName,
            ParseCombatHost(CurrentArrivalEnemyHost()),
            mayDisengage: true,
            noDisengage: false,
            playerInitiated: false,
            isHostileEcho: false,
            sourceLabel: PendingArrivalCard?.Title ?? "Arrival Combat");
    }

    private void ResolveArrivalForcedCombatPrompt(string optionId)
    {
        var outcome = optionId[7..];
        _arrivalCombatResolved = true;
        _specialPrompt = null;
        FlowPrompt = null;

        switch (outcome)
        {
            case "Won":
                AddNarrativeText("The Host wins the battle.");
                CaptureCurrentSettlement();
                break;
            case "Disengaged":
                AddNarrativeText("The Host disengages after the first round and is forced onward.");
                BeginForcedMobilization();
                Changed?.Invoke();
                return;
            case "Lost":
                AddNarrativeText("The Host is defeated in battle.");
                break;
        }

        CompleteArrivalStep();
        Changed?.Invoke();
    }


    private void AddAuthoredCompletionNarrative()
    {
        if (_activeFlowContext != FlowContext.Camp || PendingCampCard is null)
            return;

        var text = PendingCampCard.Id switch
        {
            "ECHO-Q-01" => "Those who heard the tale offer provisions freely, grateful that your host once kept smoke from becoming ruin.",
            "ECHO-Q-02" => "People receive your host more warmly, and that goodwill lends extra weight to your words.",
            "ECHO-C-01" => "They press good stores and honest silver into your hands, grateful that someone chose to help when they had little left to offer in return.",
            "ECHO-C-02" => "People receive your host with a little more warmth, and that goodwill lends extra weight to your words.",
            "ECHO-C-03" => "It is not much, but this time they insist on paying something for the help they once could not repay.",
            "ECHO-P-01" => "The food taken that day turned foul in camp, and the sickness that followed is no accident. Dr. Pepper was heard boasting, “The joke’s on them. They’ll need that silver to hire and train more troops.”",
            "ECHO-P-03" => "The silver is seized, the fine is paid, and your host leaves with a new name in local gossip: the Baked Good Bandits.",
            "ECHO-M-01" => "The truth is known at last, and the host is left to swallow what dignity it can.",
            "ECHO-M-02" => "The man’s name is cleared, and the relief runs through the host faster than the rumor ever did.",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(text))
            AddNarrativeText(text);
    }
    private void CompleteActiveFlow()
    {
        AddAuthoredCompletionNarrative();
        switch (_activeFlowContext)
        {
            case FlowContext.Arrival:
                CompleteArrivalEncounter();
                break;
            case FlowContext.Explore:
                CompleteExploreEncounter();
                break;
            case FlowContext.Camp:
                CompleteCampCard();
                break;
            default:
                ResetActiveFlowState();
                break;
        }
    }

    private void CompleteArrivalEncounter()
    {
        var disengaged = _combatDisengagedPending;
        _combatDisengagedPending = false;
        ResetActiveFlowState();
        if (disengaged)
        {
            BeginForcedMobilization();
            return;
        }
        if (CurrentTribeIsHostile() && CurrentClearingControlledByCurrentTribe() && !_arrivalCombatResolved)
        {
            PresentArrivalForcedCombat($"The {_currentArrivalTribe} become Hostile and attack.");
            return;
        }

        CompleteArrivalStep();
    }

    private bool CurrentClearingControlledByCurrentTribe()
    {
        if (string.IsNullOrWhiteSpace(_currentArrivalTribe))
            return false;
        var clearing = PlayArea.LastOrDefault(p => p.Kind == PlayAreaKind.Clearing && p.IsHostLocation);
        return clearing is not null
            && NormalizeTribeName(clearing.Controller ?? string.Empty) == NormalizeTribeName(_currentArrivalTribe);
    }

    private void CompleteArrivalStep()
    {
        ResetActiveFlowState();
        FlowPrompt = null;
        _specialPrompt = null;
        PendingArrivalCard = null;
        _currentArrivalTribe = null;
        _arrivalCombatResolved = false;
        _arrivalCardRevealed = false;
        _pendingTributeFallbackCost = 0;
        _pendingTributeFallbackResource = null;
        _activeChronicleEntryIndex = null;
        MobilizationStep = MobilizationStep.ArrivalComplete;

        if (_endOfSeasonPending)
        {
            EnterEndOfSeason();
            return;
        }

        Phase = GamePhase.Camp;
        CampStep = CampStep.Ready;
    }

    private void ResetActiveFlowState()
    {
        _activeFlow = null;
        _activeNodeId = null;
        _activeFlowContext = FlowContext.None;
        FlowPrompt = null;
        _flowVars.Clear();
        _pendingSelectedUnits.Clear();
        _pendingMultiSelectRemaining = 0;
        _pendingMultiSelectVariable = null;
        _pendingMultiSelectNextNode = null;
        _pendingSelectVariable = null;
        _pendingSelectNextNode = null;
        _pendingCombatNextNode = null;
        _pendingTestNarrativeLabel = null;
        _pendingOutcomeText = null;
        _pendingBrowserFlavor = null;
    }

    private void CompleteExploreEncounter()
    {
        // Combat encountered during Explore is part of the Explore step.
        // Disengaging ends that encounter, but does not start a second Explore.
        // Resume the original Mobilization at the Arrival gate. The player explicitly
        // proceeds so the Explore result has time to be read before the Clearing is revealed.
        _combatDisengagedPending = false;
        ResetActiveFlowState();
        PendingExploreCard = null;
        _specialPrompt = null;
        _activeChronicleEntryIndex = null;
        MobilizationStep = MobilizationStep.ArrivalReady;
        Changed?.Invoke();
    }

    private static IReadOnlyList<JsonElement> GetFlags(JsonElement element)
    {
        if (!element.TryGetProperty("flags", out var flags) || flags.ValueKind != JsonValueKind.Array)
            return [];
        return flags.EnumerateArray().Select(f => f.Clone()).ToArray();
    }

    private static bool HasFlag(IEnumerable<JsonElement> flags, string name) =>
        flags.Any(f => string.Equals(GetString(f, "name"), name, StringComparison.OrdinalIgnoreCase));

    private static int FlagInt(IEnumerable<JsonElement> flags, string name, int fallback)
    {
        foreach (var flag in flags)
        {
            if (!string.Equals(GetString(flag, "name"), name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (flag.TryGetProperty("value", out var value))
                return ToInt(JsonScalar(value));
        }
        return fallback;
    }

    private static string? ScalarText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return Convert.ToString(JsonScalar(value));
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : Convert.ToString(JsonScalar(value));
    }

    private static object? JsonScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static int ToInt(object? value)
    {
        if (value is int i)
            return i;
        if (value is long l)
            return (int)l;
        if (value is double d)
            return (int)d;
        return int.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
