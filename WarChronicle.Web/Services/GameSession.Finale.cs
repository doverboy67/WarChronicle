using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

public sealed partial class GameSession
{
    private Random _finaleRandom = Random.Shared;

    public FinaleState? Finale { get; private set; }
    public bool IsFinaleActive => Finale is not null;
    public string? CampaignOutcome { get; private set; }
    public string? CampaignEndReason { get; private set; }

    public void ForceTestFinale()
    {
        if (Combat is not null || Finale is not null || _catalog is null)
            return;

        StartTestFinale(Random.Shared.Next(100000, 1000000));
    }

    public void RestartTestFinale()
    {
        var seed = Combat is { IsFinale: true, IsTest: true, TestSeed: int combatSeed }
            ? combatSeed
            : Finale is { IsTest: true, TestSeed: int finaleSeed }
                ? finaleSeed
                : (int?)null;
        if (seed is null)
            return;

        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        StartTestFinale(seed.Value);
    }

    public void CancelTestFinale()
    {
        if (Finale is not { IsTest: true } && Combat is not { IsFinale: true, IsTest: true })
            return;

        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        Changed?.Invoke();
    }

    private void StartTestFinale(int seed)
    {
        if (_catalog is null)
            return;

        var rng = new Random(seed);
        _finaleRandom = rng;
        var state = new FinaleState
        {
            IsTest = true,
            TestSeed = seed,
            Step = FinaleStep.ResolveReady,
            Morale = rng.Next(3, 9),
            Leadership = rng.Next(2, 8),
            HasPriest = rng.NextDouble() < .35
        };

        var militaryRemaining = rng.Next(6, 11);
        var archers = rng.Next(1, Math.Min(4, militaryRemaining) + 1);
        militaryRemaining -= archers;
        var cavalry = rng.Next(1, Math.Min(4, militaryRemaining) + 1);
        militaryRemaining -= cavalry;
        var infantry = Math.Max(1, militaryRemaining);
        while (archers + cavalry + infantry > 10)
            infantry--;

        state.PlayerUnits["Archers"] = archers;
        state.PlayerUnits["Cavalry"] = cavalry;
        state.PlayerUnits["Infantry"] = infantry;
        state.PlayerUnits["Levy"] = rng.Next(2, 7);

        var advancementPool = _catalog.GetBaseAdvancements().OrderBy(_ => rng.Next()).ToList();
        foreach (var advancement in advancementPool.Take(rng.Next(3, 7)))
            state.PlayerAdvancements.Add(advancement.Id);

        state.Log.Add($"TEST FINALE • Seed {seed}. Campaign state will not be changed.");
        state.Log.Add($"Test Host: {FormatCombatHost(state.PlayerUnits)}. Morale {state.Morale}, Leadership {state.Leadership}{(state.HasPriest ? ", Priest present" : string.Empty)}.");
        Finale = state;
        Changed?.Invoke();
    }

    private void EnterFinale()
    {
        if (Finale is not null || Phase == GamePhase.GameOver)
            return;

        _finaleRandom = Random.Shared;
        Phase = GamePhase.Finale;
        Season = "Autumn";
        _endOfSeasonPending = false;
        _endOfSeasonName = null;
        FlowPrompt = null;
        _specialPrompt = null;
        _activeFlow = null;
        _activeNodeId = null;
        _activeFlowContext = FlowContext.None;
        PendingExploreCard = null;
        PendingArrivalCard = null;
        PendingCampCard = null;
        _activeChronicleEntryIndex = null;

        var state = new FinaleState
        {
            Step = FinaleStep.ResolveReady,
            Morale = Morale,
            Leadership = Leadership,
            HasPriest = _hasPriest
        };
        foreach (var type in CombatUnitTypes)
            state.PlayerUnits[type] = CampaignCombatCount(type);
        foreach (var advancement in AcquiredAdvancements)
            state.PlayerAdvancements.Add(advancement.Id);

        state.Log.Add("The march reaches Middelalderen. The Finale begins immediately.");
        Finale = state;
        Chronicle.Add(new(
            CalendarStamp,
            "The road ends at Middelalderen. There is no Winter now, only the final reckoning.",
            ChronicleTone.Narrative,
            "The Finale"));
    }

    public bool CanSpendLeadershipForFinaleResolve => Finale is { Step: FinaleStep.ResolveReady, Leadership: > 0 };

    public void ResolveFinaleCheck(bool spendLeadership)
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.ResolveReady)
            return;
        if (spendLeadership && finale.Leadership <= 0)
            return;

        finale.ResolveRolls.Clear();
        finale.ResolveSelected = null;
        if (spendLeadership)
        {
            finale.Leadership--;
            finale.ResolveRolls.Add(_finaleRandom.Next(1, 11));
            finale.ResolveRolls.Add(_finaleRandom.Next(1, 11));
            finale.Step = FinaleStep.ResolveChoose;
            finale.Log.Add($"Spend 1 Leadership for the Host Resolve Check. Rolls: {finale.ResolveRolls[0]} and {finale.ResolveRolls[1]}. Choose one.");
        }
        else
        {
            var roll = _finaleRandom.Next(1, 11);
            finale.ResolveRolls.Add(roll);
            ApplyFinaleResolveRoll(roll);
        }
        Changed?.Invoke();
    }

    public void ChooseFinaleResolveRoll(int index)
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.ResolveChoose || index < 0 || index >= finale.ResolveRolls.Count)
            return;

        ApplyFinaleResolveRoll(finale.ResolveRolls[index]);
        Changed?.Invoke();
    }

    private void ApplyFinaleResolveRoll(int roll)
    {
        var finale = Finale!;
        finale.ResolveSelected = roll;
        var before = finale.Leadership;
        if (roll < finale.Morale)
        {
            var gain = Math.Min(3, finale.Morale - roll);
            if (finale.HasPriest)
                gain++;
            finale.Leadership += gain;
            finale.Log.Add($"Host Resolve: {roll} against Morale {finale.Morale}. Gain {gain} Leadership{(finale.HasPriest ? " after the Priest modifier" : string.Empty)}.");
        }
        else if (roll > finale.Morale)
        {
            var loss = Math.Min(3, roll - finale.Morale);
            if (finale.HasPriest)
                loss = Math.Max(0, loss - 1);
            finale.Leadership = Math.Max(0, finale.Leadership - loss);
            finale.Log.Add($"Host Resolve: {roll} against Morale {finale.Morale}. Lose {loss} Leadership{(finale.HasPriest ? " after the Priest modifier" : string.Empty)}.");
        }
        else
        {
            finale.Log.Add($"Host Resolve: {roll} equals Morale {finale.Morale}. Leadership is unchanged.");
        }
        finale.Log.Add($"Leadership: {before} → {finale.Leadership}.");
        finale.Step = FinaleStep.DeployHost;
    }

    public bool CanToggleFinaleTactic(string id)
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.DeployHost || !finale.PlayerAdvancements.Contains(id))
            return false;
        var number = AdvancementNumber(id);
        if (number < 7 || number > 15 || number is 14 or 15)
            return false;
        return finale.CommittedTactics.Contains(id) || finale.Leadership > 0;
    }

    public void ToggleFinaleTactic(string id)
    {
        var finale = Finale;
        if (finale is null || !CanToggleFinaleTactic(id))
            return;

        if (finale.CommittedTactics.Remove(id))
        {
            finale.Leadership++;
            if (id == "ADV-013")
                finale.SteadyAdvanceType = null;
        }
        else
        {
            finale.CommittedTactics.Add(id);
            finale.Leadership--;
        }
        Changed?.Invoke();
    }

    public void SetFinaleSteadyAdvanceType(string type)
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.DeployHost || !finale.CommittedTactics.Contains("ADV-013")
            || !new[] { "Archers", "Cavalry", "Infantry" }.Contains(type))
            return;
        finale.SteadyAdvanceType = type;
        Changed?.Invoke();
    }

    public bool CanRevealMiddelalderen => Finale is { Step: FinaleStep.DeployHost } finale
        && (!finale.CommittedTactics.Contains("ADV-013") || !string.IsNullOrWhiteSpace(finale.SteadyAdvanceType));

    public void RevealMiddelalderen()
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.DeployHost || _catalog is null || !CanRevealMiddelalderen)
            return;

        finale.MiddelalderenAdvancements.Clear();
        foreach (var advancement in _catalog.GetMiddelalderenAdvancements().OrderBy(_ => _finaleRandom.Next()).Take(3))
            finale.MiddelalderenAdvancements.Add(advancement.Id);

        finale.MiddelalderenUnits.Clear();
        finale.MiddelalderenUnits["Archers"] = 2;
        finale.MiddelalderenUnits["Cavalry"] = 2;
        finale.MiddelalderenUnits["Infantry"] = 6;
        finale.MiddelalderenUnits["Levy"] = 6;

        foreach (var type in new[] { "Archers", "Cavalry", "Infantry" })
        {
            if (finale.MiddelalderenAdvancements.Any(id => string.Equals(MiddelalderenUnitBonusType(id), type, StringComparison.OrdinalIgnoreCase)))
                finale.MiddelalderenUnits[type]++;
        }

        var names = finale.MiddelalderenAdvancements.Select(CombatTacticName).ToArray();
        finale.Log.Add($"Middelalderen reveals 3 Advancements: {string.Join(", ", names)}.");
        finale.Log.Add($"Middelalderen Host: {FormatCombatHost(finale.MiddelalderenUnits)}.");
        finale.Step = FinaleStep.RevealMiddelalderen;
        Changed?.Invoke();
    }

    private static string? MiddelalderenUnitBonusType(string id) => id.ToUpperInvariant() switch
    {
        "ADV-M4" or "ADV-M10" => "Infantry",
        "ADV-M5" or "ADV-M7" => "Cavalry",
        "ADV-M6" or "ADV-M9" => "Archers",
        _ => null
    };

    public void StartFinaleCombat()
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.RevealMiddelalderen)
            return;

        var developments = finale.PlayerAdvancements
            .Where(id => AdvancementNumber(id) is >= 1 and <= 6)
            .Select(id => _catalog?.GetAdvancement(id))
            .Where(a => a is not null)
            .Cast<AdvancementOfferState>()
            .ToArray();
        var tactics = finale.PlayerAdvancements
            .Where(id => AdvancementNumber(id) is >= 7 and <= 15)
            .Select(id => _catalog?.GetAdvancement(id))
            .Where(a => a is not null)
            .Cast<AdvancementOfferState>()
            .ToArray();

        BeginInteractiveCombat(
            "Middelalderen",
            new Dictionary<string, int>(finale.MiddelalderenUnits, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(finale.PlayerUnits, StringComparer.OrdinalIgnoreCase),
            playerPriest: finale.HasPriest,
            enemyPriest: false,
            leadership: finale.Leadership,
            developments: developments,
            tactics: tactics,
            mayDisengage: false,
            noDisengage: true,
            playerInitiated: true,
            isHostileEcho: false,
            isTest: finale.IsTest,
            sourceLabel: "Finale • Middelalderen",
            testSeed: finale.TestSeed,
            combatRandom: _finaleRandom,
            enemyAdvancements: finale.MiddelalderenAdvancements,
            committedTactics: finale.CommittedTactics,
            lockPlayerTactics: true,
            isFinale: true,
            openImmediately: true);

        _combatResumeMode = CombatResumeMode.Finale;
        finale.Step = FinaleStep.Combat;
        if (Combat is not null)
        {
            Combat.SteadyAdvanceType = finale.SteadyAdvanceType;
            Combat.Log.Add(new("Finale Combat must be resolved to completion. Disengagement is not allowed.", true));
            if (finale.MiddelalderenAdvancements.Count > 0)
                Combat.Log.Add(new($"Middelalderen Advancements: {string.Join(", ", finale.MiddelalderenAdvancements.Select(CombatTacticName))}.", true));
        }
        Changed?.Invoke();
    }

    internal void CompleteFinaleCombat(string outcome, CombatState combat)
    {
        var finale = Finale;
        if (finale is null)
            return;

        finale.Outcome = outcome;
        finale.Leadership = combat.PlayerLeadership;
        finale.PlayerUnits.Clear();
        foreach (var type in CombatUnitTypes)
            finale.PlayerUnits[type] = combat.PlayerUnits[type];
        finale.Step = FinaleStep.Complete;
        finale.Log.Add(outcome == "Won"
            ? "Middelalderen falls. The Host survives the final battle."
            : "The Host is destroyed before Middelalderen.");

        if (!finale.IsTest)
        {
            if (outcome == "Won")
                EndCampaign("Victory", "Middelalderen has fallen. The campaign is won.");
            else
                EndCampaign("Defeat", "The Host was destroyed in the final battle at Middelalderen.");
        }
    }

    public void EndTestFinale()
    {
        if (Finale is not { IsTest: true })
            return;
        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        Changed?.Invoke();
    }

    internal void EndCampaign(string outcome, string reason)
    {
        CampaignOutcome = outcome;
        CampaignEndReason = reason;
        GameEndedUtc ??= DateTime.UtcNow;
        Phase = GamePhase.GameOver;
        FlowPrompt = null;
        _specialPrompt = null;
        _activeFlow = null;
        _activeNodeId = null;
        _activeFlowContext = FlowContext.None;
        _endOfSeasonPending = false;
        _endOfSeasonName = null;
        _activeChronicleEntryIndex = null;
        Chronicle.Add(new(
            CalendarStamp,
            reason,
            string.Equals(outcome, "Victory", StringComparison.OrdinalIgnoreCase) ? ChronicleTone.Result : ChronicleTone.Warning,
            outcome));
    }
}
