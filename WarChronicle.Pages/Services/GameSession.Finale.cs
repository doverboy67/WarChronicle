using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

public sealed partial class GameSession
{
    private Random _finaleRandom = Random.Shared;
    private CustomFinaleSetup? _customFinaleReplay;
    public CompletedBattleLog? LastCustomFinaleBattle { get; private set; }

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
        if (_customFinaleReplay is not null)
            StartCustomFinaleCore(_customFinaleReplay.Copy(), seed.Value);
        else
            StartTestFinale(seed.Value);
    }

    public void RerollTestFinale()
    {
        if (_customFinaleReplay is null ||
            (Finale is not { IsCustomTest: true } && Combat is not { IsFinale: true, IsTest: true }))
            return;
        var nextSeed = Random.Shared.Next(1, int.MaxValue);
        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        StartCustomFinaleCore(_customFinaleReplay.Copy(), nextSeed);
    }

    public void CancelTestFinale()
    {
        if (Finale is not { IsTest: true } && Combat is not { IsFinale: true, IsTest: true })
            return;

        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        _customFinaleReplay = null;
        Changed?.Invoke();
    }

    // Launch directly after the Resolve check, using the normal Finale combat engine.
    public bool StartCustomFinale(CustomFinaleSetup setup)
    {
        if (Combat is not null || Finale is not null || _catalog is null || setup is null)
            return false;
        if (setup.Morale is < 0 or > 10 || setup.Leadership is < 0 or > 99 || setup.Seed < 0)
            return false;
        if (setup.SpecialPlayerUnits.Values.Any(x => x is < 0 or > 24) ||
            setup.PlayerUnits.Values.Any(x => x is < 0 or > 24) ||
            setup.MiddelalderenBaseUnits.Values.Any(x => x is < 0 or > 24) ||
            setup.MiddelalderenOverrideUnits.Values.Any(x => x is < 0 or > 24))
            return false;
        if (setup.PlayerUnits.Values.Sum() + setup.SpecialPlayerUnits.Values.Sum() <= 0 || setup.MiddelalderenAdvancements.Count > 10 ||
            setup.MiddelalderenAdvancements.Count == 0 ||
            setup.Leadership < setup.CommittedTactics.Count)
            return false;
        var baseIds = _catalog.GetBaseAdvancements().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enemyIds = _catalog.GetMiddelalderenAdvancements().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!setup.PlayerAdvancements.All(baseIds.Contains) ||
            !setup.MiddelalderenAdvancements.All(enemyIds.Contains) ||
            !setup.CommittedTactics.All(id => setup.PlayerAdvancements.Contains(id) && AdvancementNumber(id) is >= 7 and <= 13 &&
                UnitTacticUnavailableReason(id, CustomFinaleCombatUnits(setup), mayDisengage: false) is null))
            return false;
        if (setup.CommittedTactics.Contains("ADV-013") &&
            (setup.SteadyAdvanceType is not ("Archers" or "Cavalry" or "Infantry") ||
             CustomFinaleCombatUnits(setup).GetValueOrDefault(setup.SteadyAdvanceType) <= 0))
            return false;
        var bonusTypes = new[] { "Archers", "Cavalry", "Infantry", "Levy" };
        var enemyUnits = new Dictionary<string, int>(setup.MiddelalderenBaseUnits, StringComparer.OrdinalIgnoreCase);
        foreach (var type in bonusTypes)
            if (setup.MiddelalderenAdvancements.Any(id => string.Equals(MiddelalderenUnitBonusType(id), type, StringComparison.OrdinalIgnoreCase)))
                enemyUnits[type]++;
        if (setup.OverrideMiddelalderenUnits)
            enemyUnits = new(setup.MiddelalderenOverrideUnits, StringComparer.OrdinalIgnoreCase);
        if (enemyUnits.Values.Sum() <= 0)
            return false;
        _customFinaleReplay = setup.Copy();
        StartCustomFinaleCore(_customFinaleReplay.Copy(), setup.Seed);
        return true;
    }

    private static Dictionary<string, int> CustomFinaleCombatUnits(CustomFinaleSetup setup)
    {
        var units = new Dictionary<string, int>(setup.PlayerUnits, StringComparer.OrdinalIgnoreCase);
        units["Infantry"] = units.GetValueOrDefault("Infantry") + setup.SpecialPlayerUnits.GetValueOrDefault("Mercenaries");
        units["Cavalry"] = units.GetValueOrDefault("Cavalry") + setup.SpecialPlayerUnits.GetValueOrDefault("Hedge Knights");
        return units;
    }

    private void StartCustomFinaleCore(CustomFinaleSetup setup, int seed)
    {
        _finaleRandom = new Random(seed);
        LastCustomFinaleBattle = null;
        var state = new FinaleState
        {
            IsTest = true,
            IsCustomTest = true,
            TestSeed = seed,
            Step = FinaleStep.RevealMiddelalderen,
            Morale = setup.Morale,
            Leadership = setup.Leadership - setup.CommittedTactics.Count,
            HasPriest = setup.HasPriest,
            SteadyAdvanceType = setup.CommittedTactics.Contains("ADV-013") ? setup.SteadyAdvanceType : null
        };
        foreach (var pair in CustomFinaleCombatUnits(setup))
            state.PlayerUnits[pair.Key] = pair.Value;
        state.PlayerAdvancements.UnionWith(setup.PlayerAdvancements);
        state.CommittedTactics.UnionWith(setup.CommittedTactics);
        state.MiddelalderenAdvancements.UnionWith(setup.MiddelalderenAdvancements);
        foreach (var pair in setup.OverrideMiddelalderenUnits ? setup.MiddelalderenOverrideUnits : setup.MiddelalderenBaseUnits)
            state.MiddelalderenUnits[pair.Key] = pair.Value;
        if (!setup.OverrideMiddelalderenUnits)
            foreach (var type in new[] { "Archers", "Cavalry", "Infantry", "Levy" })
                if (state.MiddelalderenAdvancements.Any(id => string.Equals(MiddelalderenUnitBonusType(id), type, StringComparison.OrdinalIgnoreCase)))
                    state.MiddelalderenUnits[type]++;
        state.Log.Add($"CUSTOM FINALE • Seed {seed}. Debug-only battle; campaign state unchanged. Host Resolve check skipped.");
        state.Log.Add($"Player specialists: {setup.SpecialPlayerUnits.GetValueOrDefault("Mercenaries")} Mercenaries (Infantry class), {setup.SpecialPlayerUnits.GetValueOrDefault("Hedge Knights")} Hedge Knights (Cavalry class). These counts are included in their combat class totals.");
        state.Log.Add($"Player: {FormatCombatHost(state.PlayerUnits)}. Morale {state.Morale}, Priest {(state.HasPriest ? "yes" : "no")}. Leadership after Resolve {setup.Leadership}; {state.CommittedTactics.Count} committed Tactic(s); {state.Leadership} Leadership entering combat.");
        state.Log.Add($"Player Advancements: {string.Join(", ", state.PlayerAdvancements.OrderBy(id => id).Select(CombatTacticName))}.");
        state.Log.Add($"Middelalderen ({(setup.OverrideMiddelalderenUnits ? "manual final units" : "base + Advancement bonuses")}): {FormatCombatHost(state.MiddelalderenUnits)}. Advancements: {string.Join(", ", state.MiddelalderenAdvancements.OrderBy(id => id).Select(CombatTacticName))}.");
        Finale = state;
        Changed?.Invoke();
    }

    // Stored separately from BattleHistory, so debug results never affect campaign statistics.
    internal void CaptureCustomFinaleBattle(CombatState combat, string outcome)
    {
        LastCustomFinaleBattle = new CompletedBattleLog
        {
            BattleNumber = 0,
            StartedUtc = combat.StartedUtc,
            EndedUtc = DateTime.UtcNow,
            SourceLabel = combat.SourceLabel,
            EnemyName = combat.EnemyName,
            IsFinale = true,
            Outcome = outcome,
            InitialPlayerUnits = new(combat.InitialPlayerUnits, StringComparer.OrdinalIgnoreCase),
            InitialEnemyUnits = new(combat.InitialEnemyUnits, StringComparer.OrdinalIgnoreCase),
            FinalPlayerUnits = new(combat.PlayerUnits, StringComparer.OrdinalIgnoreCase),
            FinalEnemyUnits = new(combat.EnemyUnits, StringComparer.OrdinalIgnoreCase),
            Entries = combat.Log.ToList()
        };
    }

    public string CreateCustomFinaleTestLogJson(string appVersion)
    {
        var finale = Finale;
        if (finale is not { IsCustomTest: true } || _customFinaleReplay is null)
            throw new InvalidOperationException("No custom Finale is active.");
        var document = new
        {
            schemaVersion = 1,
            logType = "War Chronicle Custom Finale Test Log",
            appVersion,
            snapshotUtc = DateTime.UtcNow,
            randomSeed = finale.TestSeed,
            postResolveSetup = _customFinaleReplay.Copy(),
            deployedPlayerUnits = CustomFinaleCombatUnits(_customFinaleReplay),
            startingPlayerUnits = _customFinaleReplay.PlayerUnits,
            specialPlayerUnits = _customFinaleReplay.SpecialPlayerUnits,
            deployedEnemyUnits = finale.MiddelalderenUnits,
            remainingPlayerUnits = finale.PlayerUnits,
            initialLeadership = _customFinaleReplay.Leadership,
            committedTactics = finale.CommittedTactics.OrderBy(id => id).ToArray(),
            combatLeadership = finale.Step == FinaleStep.Complete ? finale.Leadership :
                Combat is not null ? Combat.PlayerLeadership : finale.Leadership,
            steadyAdvanceType = finale.SteadyAdvanceType,
            outcome = finale.Outcome,
            finaleEvents = finale.Log.ToArray(),
            completedBattle = LastCustomFinaleBattle,
            activeBattle = Combat is { IsFinale: true, IsTest: true } combat ? new
            {
                round = combat.Round,
                step = combat.Step.ToString(),
                playerUnits = combat.PlayerUnits,
                enemyUnits = combat.EnemyUnits,
                entries = combat.Log.ToArray()
            } : null
        };
        return System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private void StartTestFinale(int seed)
    {
        if (_catalog is null)
            return;

        _customFinaleReplay = null;
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

    public string? FinaleTacticUnavailableReason(string id)
    {
        if (Finale is not { } finale)
            return null;
        return UnitTacticUnavailableReason(id, finale.PlayerUnits, mayDisengage: false)
            ?? (finale.Leadership <= 0 && !finale.CommittedTactics.Contains(id) ? "Requires 1 Leadership." : null);
    }

    public bool CanToggleFinaleTactic(string id)
    {
        var finale = Finale;
        if (finale is null || finale.Step != FinaleStep.DeployHost || !finale.PlayerAdvancements.Contains(id))
            return false;
        var number = AdvancementNumber(id);
        if (number < 7 || number > 15 || number is 14 or 15)
            return false;
        return finale.CommittedTactics.Contains(id) || (finale.Leadership > 0 && FinaleTacticUnavailableReason(id) is null);
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
        foreach (var advancement in _catalog.GetMiddelalderenAdvancements().OrderBy(_ => _finaleRandom.Next()).Take(MiddelalderenAdvancementCount))
            finale.MiddelalderenAdvancements.Add(advancement.Id);

        finale.MiddelalderenUnits.Clear();
        finale.MiddelalderenUnits["Archers"] = 2;
        finale.MiddelalderenUnits["Cavalry"] = 2;
        finale.MiddelalderenUnits["Infantry"] = 6;
        finale.MiddelalderenUnits["Levy"] = 6;

        foreach (var type in new[] { "Archers", "Cavalry", "Infantry", "Levy" })
        {
            if (finale.MiddelalderenAdvancements.Any(id => string.Equals(MiddelalderenUnitBonusType(id), type, StringComparison.OrdinalIgnoreCase)))
                finale.MiddelalderenUnits[type]++;
        }

        var names = finale.MiddelalderenAdvancements.Select(CombatTacticName).ToArray();
        finale.Log.Add($"Middelalderen reveals {MiddelalderenAdvancementCount} Advancements: {string.Join(", ", names)}.");
        finale.Log.Add($"Middelalderen Host: {FormatCombatHost(finale.MiddelalderenUnits)}.");
        finale.Step = FinaleStep.RevealMiddelalderen;
        Changed?.Invoke();
    }

    private static string? MiddelalderenUnitBonusType(string id) => id.ToUpperInvariant() switch
    {
        "ADV-M4" or "ADV-M10" => "Infantry",
        "ADV-M5" or "ADV-M7" => "Cavalry",
        "ADV-M6" or "ADV-M9" => "Archers",
        "ADV-M8" => "Levy",  // Gritty provides +1 Levy in addition to its combat effect.
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
            sourceLabel: finale.IsCustomTest ? "Custom Finale • Middelalderen" : "Finale • Middelalderen",
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

        if (outcome == "Won")
        {
            var epilogue = BuildFinaleVictoryEpilogue(finale, combat);
            foreach (var line in epilogue)
                finale.Log.Add(line);

            if (!finale.IsTest)
            {
                Chronicle.Add(new(
                    CalendarStamp,
                    "Middelalderen falls. The Host survives the final battle.",
                    ChronicleTone.Result,
                    "Middelalderen Falls"));
                foreach (var line in epilogue)
                {
                    Chronicle.Add(new(
                        CalendarStamp,
                        line,
                        ChronicleTone.Narrative,
                        "After the Battle"));
                }
            }
        }

        if (!finale.IsTest)
        {
            if (outcome == "Won")
                EndCampaign("Victory", "Middelalderen has fallen. The campaign is won.");
            else
                EndCampaign("Defeat", "The Host was destroyed in the final battle at Middelalderen.");
        }
    }

    private IReadOnlyList<string> BuildFinaleVictoryEpilogue(FinaleState finale, CombatState combat)
    {
        var lines = new List<string>
        {
            "The gates are broken. Smoke hangs above the walls as the last organized defenders scatter through Middelalderen."
        };

        var initialUnits = combat.InitialPlayerUnits.Values.Sum();
        var survivingUnits = combat.PlayerUnits.Values.Sum();
        var lostUnits = Math.Max(0, initialUnits - survivingUnits);
        var survivingFighters = survivingUnits * 100;

        if (survivingUnits <= 2)
            lines.Add($"What remains of the Host enters the city in a ragged column. Roughly {survivingFighters:N0} fighters still stand after losing {lostUnits} Units in the assault.");
        else if (survivingUnits <= 5)
            lines.Add($"The battered Host enters the city with roughly {survivingFighters:N0} fighters still under its banners after losing {lostUnits} Units in the assault.");
        else
            lines.Add($"The Host enters Middelalderen in force, roughly {survivingFighters:N0} fighters still under its banners after losing {lostUnits} Units in the assault.");

        if (finale.Morale <= 3)
            lines.Add("They reached the walls with their spirits worn thin, but the final victory carries through the ranks like a second wind.");
        else if (finale.Morale >= 7)
            lines.Add("The confidence that carried the Host to the walls survives the battle with it.");
        else
            lines.Add("Exhaustion gives way to the first stunned realization that the long campaign is finally over.");

        var allies = Tribes.Where(t => string.Equals(t.Rapport, "Allied", StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).ToArray();
        var friendly = Tribes.Where(t => string.Equals(t.Rapport, "Friendly", StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).ToArray();
        var hostile = Tribes.Where(t => string.Equals(t.Rapport, "Hostile", StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).ToArray();

        if (allies.Length > 0)
            lines.Add($"The names of old allies travel with the victory: {string.Join(", ", allies)}.");
        else if (friendly.Length > 0)
            lines.Add($"Not every road to Middelalderen was made by force. The Host leaves friends behind it among {string.Join(", ", friendly)}.");

        if (hostile.Length > 0)
            lines.Add($"Old grudges remain beyond the city walls. {string.Join(", ", hostile)} will remember the Host differently.");

        var stores = ResourceValue("Food") + ResourceValue("Wood") + ResourceValue("Stone");
        if (stores <= 2)
            lines.Add("The baggage train is nearly bare. The Host did not arrive rich, only alive.");
        else if (stores >= 7)
            lines.Add("Enough remains in the baggage train to remind the survivors that preparation mattered as much as courage.");

        lines.Add("Every hard march, bargain, refusal, risk, and compromise now lies behind the Host. Some consequences returned on the road. Others never had the chance.");
        lines.Add("The Host has survived.");
        return lines;
    }

    public void EndTestFinale()
    {
        if (Finale is not { IsTest: true })
            return;
        Combat = null;
        CombatVisible = false;
        Finale = null;
        _combatResumeMode = CombatResumeMode.None;
        _customFinaleReplay = null;
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
