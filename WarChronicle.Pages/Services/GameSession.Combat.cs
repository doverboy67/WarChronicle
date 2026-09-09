using System.Text.RegularExpressions;
using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

public sealed partial class GameSession
{
    private enum CombatResumeMode
    {
        None,
        Flow,
        ArrivalForced,
        Finale,
        Test
    }

    private CombatResumeMode _combatResumeMode;
    private string? _combatResumeNextNode;
    private bool _combatNoDisengage;
    private bool _combatMayDisengage;
    private string? _combatSourceTribe;
    private bool _combatDisengagedPending;
    private bool _skipNextDisengagePenaltyAction;
    private Random _combatRandom = Random.Shared;

    public CombatState? Combat { get; private set; }
    public bool IsCombatActive => Combat is not null;
    public bool CombatVisible { get; private set; }
    public bool HasPendingCombat => Combat is not null && !CombatVisible;

    public void OpenPendingCombat()
    {
        if (Combat is null)
            return;

        CombatVisible = true;
        Changed?.Invoke();
    }

    public void ForceTestCombat()
    {
        if (Combat is not null || Finale is not null)
            return;

        StartTestCombat(Random.Shared.Next(100000, 1000000));
    }

    public void RestartTestCombat()
    {
        if (Combat is { IsFinale: true, IsTest: true })
        {
            RestartTestFinale();
            return;
        }
        if (Combat is not { IsTest: true, TestSeed: int seed })
            return;

        Combat = null;
        CombatVisible = false;
        _combatResumeMode = CombatResumeMode.None;
        StartTestCombat(seed);
    }

    private void StartTestCombat(int seed)
    {
        if (Combat is not null || _catalog is null)
            return;

        var rng = new Random(seed);
        var arrivals = _catalog.GetArrivalCards();
        if (arrivals.Count == 0)
            return;

        var card = arrivals[rng.Next(arrivals.Count)];
        var enemySpec = HostForYear(card.Host, Year);
        var enemy = ParseCombatHost(enemySpec);
        if (enemy.Values.Sum() == 0)
            enemy["Infantry"] = Math.Max(1, Year + 1);

        var militaryRemaining = rng.Next(4, 11);
        var archers = rng.Next(0, Math.Min(4, militaryRemaining + 1));
        militaryRemaining -= archers;
        var cavalry = rng.Next(0, Math.Min(4, militaryRemaining + 1));
        militaryRemaining -= cavalry;
        var infantry = Math.Max(1, militaryRemaining);
        while (archers + cavalry + infantry > 10)
            infantry--;

        var player = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Archers"] = archers,
            ["Cavalry"] = cavalry,
            ["Infantry"] = infantry,
            ["Levy"] = rng.Next(0, 7)
        };

        var advancements = _catalog.GetBaseAdvancements();
        var developmentPool = advancements.Where(a => AdvancementNumber(a.Id) is >= 1 and <= 6).ToList();
        var tacticPool = advancements.Where(a => AdvancementNumber(a.Id) is >= 7 and <= 15).ToList();

        var chosenDevelopments = new List<AdvancementOfferState>();
        var armorCount = rng.Next(0, 4);
        chosenDevelopments.AddRange(developmentPool.Where(a => AdvancementNumber(a.Id) is >= 1 and <= 3).Take(armorCount));
        foreach (var development in developmentPool.Where(a => AdvancementNumber(a.Id) is >= 4 and <= 6))
        {
            if (rng.NextDouble() < .42)
                chosenDevelopments.Add(development);
        }

        var chosenTactics = tacticPool.OrderBy(_ => rng.Next()).Take(rng.Next(0, 4)).ToList();

        BeginInteractiveCombat(
            $"{card.Tribe} training scenario",
            enemy,
            player,
            playerPriest: rng.NextDouble() < .3,
            enemyPriest: enemy.TryGetValue("Priest", out var ep) && ep > 0,
            leadership: rng.Next(2, 7),
            developments: chosenDevelopments,
            tactics: chosenTactics,
            mayDisengage: true,
            noDisengage: false,
            playerInitiated: rng.NextDouble() < .5,
            isHostileEcho: false,
            isTest: true,
            sourceLabel: $"Test Combat • {card.Title}",
            testSeed: seed,
            combatRandom: rng,
            openImmediately: true);

        _combatResumeMode = CombatResumeMode.Test;
        Combat?.Log.Add(new($"TEST SCENARIO • Seed {seed}. Campaign state will not be changed by this Combat.", true));
        Changed?.Invoke();
    }

    private static int AdvancementNumber(string id)
    {
        var match = Regex.Match(id ?? string.Empty, @"(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private void BeginInteractiveCombat(
        string enemyName,
        Dictionary<string, int> enemyUnits,
        Dictionary<string, int>? playerUnits = null,
        bool? playerPriest = null,
        bool? enemyPriest = null,
        int? leadership = null,
        IEnumerable<AdvancementOfferState>? developments = null,
        IEnumerable<AdvancementOfferState>? tactics = null,
        bool mayDisengage = true,
        bool noDisengage = false,
        bool playerInitiated = false,
        bool isHostileEcho = false,
        bool isTest = false,
        string sourceLabel = "Combat",
        int? testSeed = null,
        Random? combatRandom = null,
        IEnumerable<string>? enemyAdvancements = null,
        IEnumerable<string>? committedTactics = null,
        bool lockPlayerTactics = false,
        bool isFinale = false,
        bool openImmediately = false)
    {
        enemyName = CombatEnemyDisplayName(enemyName);

        var reignInBloodActive = !isTest && HasToken("ReignInBlood");
        var firepotsActive = !isTest
            && HasToken("Firepots")
            && Baggage.Any(b => string.Equals(b.Contents, "Firepots", StringComparison.OrdinalIgnoreCase));
        var painkillerActive = !isTest && HasToken("Painkiller");

        var state = new CombatState
        {
            IsTest = isTest,
            TestSeed = testSeed,
            EnemyName = enemyName,
            SourceLabel = sourceLabel,
            Step = CombatStep.Setup,
            Round = 1,
            MayDisengage = mayDisengage,
            NoDisengage = noDisengage || reignInBloodActive,
            PlayerInitiated = playerInitiated,
            IsHostileEcho = isHostileEcho,
            IsFinale = isFinale,
            LockPlayerTactics = lockPlayerTactics,
            PlayerPriest = playerPriest ?? _hasPriest,
            EnemyPriest = enemyPriest ?? (enemyUnits.TryGetValue("Priest", out var priestCount) && priestCount > 0),
            PlayerLeadership = leadership ?? Leadership,
            InitialPlayerLeadership = leadership ?? Leadership,
            FirepotsAvailable = firepotsActive,
            PainkillerActive = painkillerActive
        };

        foreach (var type in CombatUnitTypes)
        {
            var count = playerUnits is not null && playerUnits.TryGetValue(type, out var supplied)
                ? supplied
                : CampaignCombatCount(type);
            state.PlayerUnits[type] = Math.Max(0, count);
            state.InitialPlayerUnits[type] = Math.Max(0, count);
            state.EnemyUnits[type] = enemyUnits.TryGetValue(type, out var enemyCount) ? Math.Max(0, enemyCount) : 0;
            state.InitialEnemyUnits[type] = state.EnemyUnits[type];
        }

        var developmentList = developments?.ToList() ?? AcquiredAdvancements.Where(a => AdvancementNumber(a.Id) is >= 1 and <= 6).ToList();
        foreach (var development in developmentList)
            state.PlayerDevelopments.Add(development.Id);

        var tacticList = tactics?.ToList() ?? AcquiredAdvancements.Where(a => AdvancementNumber(a.Id) is >= 7 and <= 15).ToList();
        foreach (var tactic in tacticList)
            state.PlayerTactics.Add(tactic.Id);
        foreach (var tacticId in committedTactics ?? Enumerable.Empty<string>())
        {
            if (state.PlayerTactics.Contains(tacticId))
                state.CommittedTactics.Add(tacticId);
        }
        foreach (var advancementId in enemyAdvancements ?? Enumerable.Empty<string>())
            state.EnemyAdvancements.Add(advancementId);

        state.PlayerShields = state.PlayerDevelopments.Count(id => AdvancementNumber(id) is >= 1 and <= 3);
        state.EnemyShields = state.EnemyAdvancements.Count(id => id is "ADV-M1" or "ADV-M2" or "ADV-M3");
        Combat = state;
        CombatVisible = openImmediately;
        _combatRandom = combatRandom ?? Random.Shared;
        _combatNoDisengage = state.NoDisengage;
        _combatMayDisengage = mayDisengage;

        state.Log.Add(new($"Combat begins against {enemyName}."));
        if (reignInBloodActive)
            state.Log.Add(new("Reign in Blood is active. The Host may not Disengage from this Combat."));
        if (painkillerActive)
            state.Log.Add(new("Painkiller is active. Each Host Unit loss is checked on 1d6; 5–6 prevents that loss.", true));
        if (firepotsActive)
            state.Log.Add(new("Firepots are ready and may be used before the first round.", true));
        state.Log.Add(new($"Player Host: {FormatCombatHost(state.PlayerUnits)}", true));
        state.Log.Add(new($"Enemy Host: {FormatCombatHost(state.EnemyUnits)}", true));
        Changed?.Invoke();
    }

    private static readonly string[] CombatUnitTypes = ["Archers", "Cavalry", "Infantry", "Levy"];

    private int CampaignCombatCount(string type) => type switch
    {
        "Infantry" => HostCount("Infantry") + HostCount("Mercenaries"),
        "Cavalry" => HostCount("Cavalry") + HostCount("Hedge Knights"),
        _ => HostCount(type)
    };

    private static Dictionary<string, int> ParseCombatHost(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Archers"] = 0,
            ["Cavalry"] = 0,
            ["Infantry"] = 0,
            ["Levy"] = 0,
            ["Priest"] = 0
        };

        foreach (Match match in Regex.Matches(text ?? string.Empty, @"(?i)(\d+)\s*([ACILP])"))
        {
            var count = int.Parse(match.Groups[1].Value);
            var type = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "A" => "Archers",
                "C" => "Cavalry",
                "I" => "Infantry",
                "L" => "Levy",
                "P" => "Priest",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(type))
                result[type] += count;
        }
        return result;
    }

    private static string HostForYear(string text, int year)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var match = Regex.Match(text, $@"(?i)Y{year}:\s*(.*?)(?=\s*-\s*Y\d:|$)");
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    private static string CombatEnemyDisplayName(string enemyName)
        => enemyName switch
        {
            "DrPepperHost" => "Dr. Pepper’s Host",
            "BanditHorde" => "Bandit Horde",
            "HillClan" => "Hill Clan",
            _ => enemyName
        };

    private string? SpecialCombatDefeatReason(CombatState combat)
    {
        if (string.Equals(PendingCampCard?.Id, "ECHO-P-02", StringComparison.OrdinalIgnoreCase)
            || string.Equals(combat.EnemyName, "Dr. Pepper’s Host", StringComparison.OrdinalIgnoreCase))
        {
            return "You are defeated by Dr. Pepper. From Pepperridge Farm. Because you stuck your nose into a dispute over baked goods and your campaign ends.";
        }

        return null;
    }

    private static string FormatCombatHost(IReadOnlyDictionary<string, int> units)
    {
        var parts = CombatUnitTypes
            .Where(t => units.TryGetValue(t, out var count) && count > 0)
            .Select(t => $"{units[t]} {t}")
            .ToList();
        return parts.Count == 0 ? "No Units" : string.Join(", ", parts);
    }

    public bool CanToggleCombatTactic(string id)
    {
        var combat = Combat;
        if (combat is null || combat.Step != CombatStep.Setup || combat.LockPlayerTactics || !combat.PlayerTactics.Contains(id))
            return false;
        var number = AdvancementNumber(id);
        if (number is 14 or 15)
            return false;
        if (combat.CommittedTactics.Contains(id))
            return true;
        return combat.PlayerLeadership > 0;
    }

    public void ToggleCombatTactic(string id)
    {
        var combat = Combat;
        if (combat is null || !CanToggleCombatTactic(id))
            return;

        if (combat.CommittedTactics.Remove(id))
        {
            combat.PlayerLeadership++;
            if (AdvancementNumber(id) == 13)
                combat.SteadyAdvanceType = null;
        }
        else
        {
            combat.CommittedTactics.Add(id);
            combat.PlayerLeadership--;
        }
        Changed?.Invoke();
    }

    public void SetSteadyAdvanceType(string type)
    {
        if (Combat is null || !Combat.CommittedTactics.Contains("ADV-013") || !CombatUnitTypes.Take(3).Contains(type))
            return;
        Combat.SteadyAdvanceType = type;
        Changed?.Invoke();
    }

    public void AdvanceCombat()
    {
        var combat = Combat;
        if (combat is null || combat.AwaitingClose)
            return;

        switch (combat.Step)
        {
            case CombatStep.Setup:
                BeginCombatRounds();
                break;
            case CombatStep.Priest:
                ResolvePriestStep();
                break;
            case CombatStep.RoundStart:
                BeginRoundStart();
                break;
            case CombatStep.Archers:
                ResolveCombatLane("Archers");
                break;
            case CombatStep.Cavalry:
                ResolveCombatLane("Cavalry");
                break;
            case CombatStep.Infantry:
                ResolveCombatLane("Infantry");
                break;
            case CombatStep.Levy:
                ResolveCombatLane("Levy");
                break;
            case CombatStep.RoundEnd:
                PresentRoundEndChoices();
                break;
        }
        Changed?.Invoke();
    }

    private void BeginCombatRounds()
    {
        var combat = Combat!;
        if (CheckCombatEnd())
            return;
        if (combat.CommittedTactics.Contains("ADV-013") && string.IsNullOrWhiteSpace(combat.SteadyAdvanceType))
        {
            combat.ChoicePrompt = "Steady Advance must be committed to a Military unit type.";
            combat.Choices.Clear();
            foreach (var type in CombatUnitTypes.Take(3).Where(t => combat.PlayerUnits[t] > 0))
                combat.Choices.Add(new($"steady:{type}", type));
            return;
        }

        if (combat.FirepotsAvailable && !combat.FirepotsResolved)
        {
            combat.ChoicePrompt = "Use the Firepots before the first round?";
            combat.Choices.Clear();
            combat.Choices.Add(new("firepots:use", "Use Firepots", "Remove the Firepots. Roll 3d6; each 4+ inflicts 1 casualty as if from a Cavalry attack."));
            combat.Choices.Add(new("firepots:keep", "Keep Firepots", "Do not use them in this Combat."));
            return;
        }

        var committedNames = combat.CommittedTactics.Select(CombatTacticName).ToArray();
        if (committedNames.Length > 0)
            combat.Log.Add(new($"Committed Tactics: {string.Join(", ", committedNames)}. Leadership remaining: {combat.PlayerLeadership}."));

        if (combat.PlayerPriest || combat.EnemyPriest)
        {
            combat.Step = CombatStep.Priest;
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            if (combat.PlayerPriest && !combat.EnemyPriest)
            {
                combat.ChoicePrompt = "Your Priest may attempt Proselytization before the first round.";
                combat.Choices.Add(new("priest:attempt", "Attempt Proselytization", "Roll 1d6. On 5–6, convert 1 enemy Unit."));
                combat.Choices.Add(new("priest:skip", "Do not Proselytize"));
                return;
            }

            // There is no player decision when both Priests cancel or only the
            // enemy has a Priest, so resolve the step immediately.
            ResolvePriestStep();
            return;
        }

        combat.Log.Add(new("Priest phase skipped: no Priest is present."));
        StartNewCombatRound();
    }

    private void ResolvePriestStep()
    {
        var combat = Combat!;
        if (combat.PlayerPriest && combat.EnemyPriest)
        {
            combat.Log.Add(new("The Priests counter one another. No Proselytization roll is made."));
            StartNewCombatRound();
            return;
        }

        var playerActs = combat.PlayerPriest;
        var roll = _combatRandom.Next(1, 7);
        combat.Log.Add(new($"Proselytization roll: {roll}.", true));
        if (roll < 5)
        {
            combat.Log.Add(new(playerActs ? "The Priest fails to sway the enemy." : "The enemy Priest fails to sway the Host."));
            StartNewCombatRound();
            return;
        }

        var target = FirstPresentUnit(playerActs ? combat.EnemyUnits : combat.PlayerUnits, ["Levy", "Infantry", "Archers", "Cavalry"]);
        if (target is null)
        {
            combat.Log.Add(new("There is no Unit available to convert."));
            StartNewCombatRound();
            return;
        }

        if (playerActs)
        {
            combat.EnemyUnits[target]--;
            combat.ConvertedForPlayer[target] = combat.ConvertedForPlayer.TryGetValue(target, out var count) ? count + 1 : 1;
            combat.Log.Add(new($"The Priest converts 1 enemy {SingularUnit(target)}. It is removed from the battle and may join the Host if the Host survives."));
        }
        else
        {
            combat.PlayerUnits[target]--;
            combat.Log.Add(new($"The enemy Priest converts 1 {SingularUnit(target)}. It leaves the Host and is removed from the battle."));
        }

        if (CheckCombatEnd())
            return;
        StartNewCombatRound();
    }

    private void StartNewCombatRound()
    {
        var combat = Combat!;
        combat.RerolledTypesThisRound.Clear();
        combat.BonusDiceThisRound.Clear();
        combat.InspiredType = null;
        combat.FeignedWithdrawalReady = false;
        combat.CoveringFirePending = false;
        combat.SteadyAdvanceAwardedThisRound = false;
        combat.Log.Add(new($"Round {combat.Round} begins."));
        combat.Step = CombatStep.RoundStart;
        BeginRoundStart();
    }

    private void BeginRoundStart()
    {
        var combat = Combat!;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;

        if (combat.PlayerTactics.Contains("ADV-014") && combat.PlayerLeadership > 0)
        {
            combat.ChoicePrompt = "Inspired: spend 1 Leadership to strengthen one unit type this round?";
            combat.Choices.Add(new("inspired:skip", "Do not use Inspired"));
            foreach (var type in CombatUnitTypes.Where(t => combat.PlayerUnits[t] > 0))
            {
                var bonus = combat.PlayerUnits[type] >= 2 ? 2 : 1;
                combat.Choices.Add(new($"inspired:{type}", $"{type} +{bonus} dice", "Spend 1 Leadership"));
            }
            return;
        }

        AdvanceToNextMeaningfulCombatStep(null);
    }

    private void AdvanceToNextMeaningfulCombatStep(string? completedType)
    {
        var combat = Combat!;
        var order = new[] { "Archers", "Cavalry", "Infantry", "Levy" };
        var startIndex = string.IsNullOrWhiteSpace(completedType)
            ? 0
            : Array.FindIndex(order, t => string.Equals(t, completedType, StringComparison.OrdinalIgnoreCase)) + 1;
        if (startIndex < 0)
            startIndex = 0;

        for (var i = startIndex; i < order.Length; i++)
        {
            var type = order[i];
            var playerDice = Math.Min(6, combat.PlayerUnits[type] + CombatBonusDice(type));
            var enemyDice = Math.Min(6, combat.EnemyUnits[type] + EnemyCombatBonusDice(type));
            if (playerDice <= 0 && enemyDice <= 0)
            {
                combat.Log.Add(new($"{type} phase skipped: no {type} are present."));
                continue;
            }

            combat.Step = StepForType(type);
            return;
        }

        combat.Step = CombatStep.RoundEnd;
    }

    private void ResolveCombatLane(string type)
    {
        var combat = Combat!;
        combat.CurrentUnitType = type;
        combat.PlayerRoll.Clear();
        combat.EnemyRoll.Clear();

        var playerDice = Math.Min(6, combat.PlayerUnits[type] + CombatBonusDice(type));
        var enemyDice = Math.Min(6, combat.EnemyUnits[type] + EnemyCombatBonusDice(type));

        for (var i = 0; i < playerDice; i++)
            combat.PlayerRoll.Add(new(_combatRandom.Next(1, 7)));
        for (var i = 0; i < enemyDice; i++)
            combat.EnemyRoll.Add(new(_combatRandom.Next(1, 7)));

        MarkCombatHits(type);
        combat.Log.Add(new($"{type}: Player rolls {RollList(combat.PlayerRoll)}. Enemy rolls {RollList(combat.EnemyRoll)}.", true));

        if (combat.PlayerLeadership > 0 && combat.PlayerRoll.Count > 0 && !combat.RerolledTypesThisRound.Contains(type))
        {
            combat.Step = CombatStep.RerollChoice;
            combat.ResumeStep = StepForType(type);
            combat.ChoicePrompt = $"Spend 1 Leadership to reroll one {SingularUnit(type)} die?";
            combat.Choices.Clear();
            combat.Choices.Add(new("reroll:keep", "Keep the roll"));
            for (var index = 0; index < combat.PlayerRoll.Count; index++)
                combat.Choices.Add(new($"reroll:{index}", $"Reroll the {combat.PlayerRoll[index].Value}"));
            return;
        }

        if (type == "Levy")
            ApplyLevyRout();
        BeginCombatCasualties(type);
    }

    private int CombatBonusDice(string type)
    {
        var combat = Combat!;
        var bonus = combat.BonusDiceThisRound.TryGetValue(type, out var amount) ? amount : 0;
        if (combat.CommittedTactics.Contains("ADV-007") && type == "Cavalry") bonus++;
        if (combat.CommittedTactics.Contains("ADV-009") && type == "Archers") bonus++;
        if (combat.CommittedTactics.Contains("ADV-010") && type == "Infantry") bonus++;
        return bonus;
    }

    private int EnemyCombatBonusDice(string type)
    {
        var combat = Combat!;
        var bonus = 0;
        if (combat.EnemyAdvancements.Contains("ADV-M7") && type == "Cavalry") bonus++;
        if (combat.EnemyAdvancements.Contains("ADV-M9") && type == "Archers") bonus++;
        if (combat.EnemyAdvancements.Contains("ADV-M10") && type == "Infantry") bonus++;
        return bonus;
    }

    private void MarkCombatHits(string type)
    {
        var combat = Combat!;
        var playerDrm = type switch
        {
            "Archers" when combat.PlayerDevelopments.Contains("ADV-006") => 1,
            "Cavalry" when combat.PlayerDevelopments.Contains("ADV-005") => 1,
            "Infantry" when combat.PlayerDevelopments.Contains("ADV-004") => 1,
            _ => 0
        };
        var enemyDrm = type switch
        {
            "Archers" when combat.EnemyAdvancements.Contains("ADV-M6") => 1,
            "Cavalry" when combat.EnemyAdvancements.Contains("ADV-M5") => 1,
            "Infantry" when combat.EnemyAdvancements.Contains("ADV-M4") => 1,
            _ => 0
        };
        var threshold = CombatHitThreshold(type);
        for (var i = 0; i < combat.PlayerRoll.Count; i++)
        {
            var die = combat.PlayerRoll[i];
            var hit = type == "Levy" ? die.Value == 6 : die.Value + playerDrm >= threshold;
            var rout = type == "Levy"
                && die.Value == 1
                && !combat.CommittedTactics.Contains("ADV-008");
            combat.PlayerRoll[i] = die with { Hit = hit, Rout = rout };
        }
        for (var i = 0; i < combat.EnemyRoll.Count; i++)
        {
            var die = combat.EnemyRoll[i];
            var hit = type == "Levy" ? die.Value == 6 : die.Value + enemyDrm >= threshold;
            var rout = type == "Levy"
                && die.Value == 1
                && !combat.EnemyAdvancements.Contains("ADV-M8");
            combat.EnemyRoll[i] = die with { Hit = hit, Rout = rout };
        }
    }

    private static int CombatHitThreshold(string type) => type switch
    {
        "Archers" => 6,
        "Cavalry" => 4,
        "Infantry" => 5,
        "Levy" => 6,
        _ => 7
    };

    private void ApplyLevyRout()
    {
        var combat = Combat!;
        var playerRouts = combat.PlayerRoll.Count(d => d.Rout);
        var enemyRouts = combat.EnemyRoll.Count(d => d.Rout);
        if (playerRouts > 0)
        {
            var attempted = Math.Min(playerRouts, combat.PlayerUnits["Levy"]);
            var lost = 0;
            var prevented = 0;
            var painkillerRolls = new List<int>();
            for (var i = 0; i < attempted; i++)
            {
                if (combat.PainkillerActive)
                {
                    var roll = _combatRandom.Next(1, 7);
                    painkillerRolls.Add(roll);
                    if (roll >= 5)
                    {
                        prevented++;
                        continue;
                    }
                }
                lost++;
            }
            combat.PlayerUnits["Levy"] -= lost;
            if (combat.PainkillerActive)
                combat.Log.Add(new($"Painkiller vs Levy routs: {string.Join(", ", painkillerRolls)}. {prevented} rout loss{(prevented == 1 ? string.Empty : "es")} prevented.", true));
            if (lost > 0)
                combat.Log.Add(new($"{lost} Player Levy rout{(lost == 1 ? "s" : string.Empty)} from the battle."));
        }
        if (enemyRouts > 0)
        {
            var lost = Math.Min(enemyRouts, combat.EnemyUnits["Levy"]);
            combat.EnemyUnits["Levy"] -= lost;
            combat.Log.Add(new($"{lost} Enemy Levy rout{(lost == 1 ? "s" : string.Empty)} from the battle."));
        }
    }

    private void BeginCombatCasualties(string type)
    {
        var combat = Combat!;
        combat.PendingAttackerType = type;
        combat.PendingPlayerHits = combat.PlayerRoll.Count(d => d.Hit);
        combat.PendingEnemyHits = combat.EnemyRoll.Count(d => d.Hit);
        combat.ResolvingPlayerHits = true;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;

        if (combat.PendingPlayerHits == 0 && combat.PendingEnemyHits == 0)
            combat.Log.Add(new($"No casualties are inflicted by {type}."));
        else
            combat.Log.Add(new($"{type} inflict {combat.PendingPlayerHits} Player hit{(combat.PendingPlayerHits == 1 ? string.Empty : "s")} and {combat.PendingEnemyHits} Enemy hit{(combat.PendingEnemyHits == 1 ? string.Empty : "s")}."));

        ContinueCombatCasualties();
    }

    private void ContinueCombatCasualties()
    {
        var combat = Combat!;
        while (true)
        {
            if (combat.ResolvingPlayerHits)
            {
                if (combat.PendingPlayerHits <= 0)
                {
                    combat.ResolvingPlayerHits = false;
                    continue;
                }
                if (PresentOrApplyCombatHit(attackerIsPlayer: true))
                    return;
                combat.PendingPlayerHits--;
                continue;
            }

            if (combat.PendingEnemyHits <= 0)
            {
                if (combat.ResolvingFirepots)
                {
                    combat.ResolvingFirepots = false;
                    combat.PendingAttackerType = null;
                    combat.Choices.Clear();
                    combat.ChoicePrompt = string.Empty;
                    if (!CheckCombatEnd())
                        BeginCombatRounds();
                    return;
                }

                if (combat.ResolvingPursuit)
                {
                    CompletePursuitLane();
                    return;
                }

                CompleteCombatLane(combat.PendingAttackerType ?? combat.CurrentUnitType ?? "Infantry");
                return;
            }
            if (PresentOrApplyCombatHit(attackerIsPlayer: false))
                return;
            combat.PendingEnemyHits--;
        }
    }

    private bool PresentOrApplyCombatHit(bool attackerIsPlayer)
    {
        var combat = Combat!;
        var attackerType = combat.PendingAttackerType ?? "Infantry";
        var targetUnits = attackerIsPlayer ? combat.EnemyUnits : combat.PlayerUnits;
        var candidates = CombatCasualtyCandidates(targetUnits, attackerType);
        if (candidates.Count == 0)
            return false;

        var playerChooses = attackerType switch
        {
            "Cavalry" => attackerIsPlayer,
            "Archers" or "Infantry" => !attackerIsPlayer,
            _ => false
        };

        string target;
        if (candidates.Count > 1 && playerChooses)
        {
            combat.Step = CombatStep.CasualtyChoice;
            combat.ChoiceTargetSide = attackerIsPlayer ? "enemy" : "player";
            combat.ChoicePrompt = attackerIsPlayer
                ? $"Your {attackerType} scored a hit. Choose a casualty from the ENEMY HOST."
                : $"Enemy {attackerType} scored a hit. Choose a casualty from YOUR HOST.";
            combat.Choices.Clear();
            foreach (var candidate in candidates)
            {
                var label = attackerIsPlayer ? $"Enemy {candidate}" : $"Your {candidate}";
                combat.Choices.Add(new($"target:{(attackerIsPlayer ? "enemy" : "player")}:{candidate}", label));
            }
            return true;
        }

        target = candidates.Count == 1 ? candidates[0] : ChooseAiCasualty(candidates, attackerIsPlayer);
        if (attackerIsPlayer && target != "Levy" && combat.EnemyShields > 0)
        {
            combat.EnemyShields--;
            combat.Log.Add(new($"Middelalderen spends 1 Shield to prevent the casualty to {target}."));
            return false;
        }
        if (!attackerIsPlayer && PresentPlayerDefense(target))
            return true;

        ApplyCombatCasualty(attackerIsPlayer, target);
        return false;
    }

    private static List<string> CombatCasualtyCandidates(IReadOnlyDictionary<string, int> units, string attackerType)
    {
        if (attackerType == "Levy")
        {
            var target = FirstPresentUnit(units, ["Levy", "Infantry", "Archers", "Cavalry"]);
            return target is null ? [] : [target];
        }

        var present = CombatUnitTypes.Where(t => units.TryGetValue(t, out var count) && count > 0).ToList();
        if (present.Count == 0)
            return [];
        var largest = present.Max(t => units[t]);
        return present.Where(t => units[t] == largest).ToList();
    }

    private static string ChooseAiCasualty(IReadOnlyList<string> candidates, bool attackerIsPlayer)
    {
        // When the opponent chooses its own casualty, preserve its strength.
        // When the opponent chooses ours, choose the most harmful legal loss.
        var preserveOrder = new[] { "Levy", "Archers", "Infantry", "Cavalry" };
        var harmfulOrder = new[] { "Cavalry", "Infantry", "Archers", "Levy" };
        var order = attackerIsPlayer ? preserveOrder : harmfulOrder;
        return order.First(x => candidates.Contains(x));
    }

    private bool PresentPlayerDefense(string target)
    {
        var combat = Combat!;
        var canShield = target != "Levy" && combat.PlayerShields > 0;
        var canLoose = combat.PlayerTactics.Contains("ADV-015") && combat.PlayerLeadership > 0;
        if (!canShield && !canLoose)
            return false;

        combat.PendingTargetType = target;
        combat.Step = CombatStep.CasualtyChoice;
        combat.ChoiceTargetSide = "player";
        combat.ChoicePrompt = $"YOUR HOST would lose 1 {SingularUnit(target)}. How do you respond?";
        combat.Choices.Clear();
        combat.Choices.Add(new($"defense:accept:{target}", $"Lose 1 of Your {SingularUnit(target)}"));
        if (canShield)
            combat.Choices.Add(new($"defense:shield:{target}", "Spend 1 Shield", "Prevent this Military casualty"));
        if (canLoose)
        {
            foreach (var alternative in CombatUnitTypes.Where(t => combat.PlayerUnits[t] > 0 && !string.Equals(t, target, StringComparison.OrdinalIgnoreCase)))
                combat.Choices.Add(new($"defense:loose:{alternative}", $"Loose Formation: lose 1 of Your {SingularUnit(alternative)}", "Spend 1 Leadership"));
        }
        return true;
    }

    private void ApplyCombatCasualty(bool attackerIsPlayer, string target)
    {
        var combat = Combat!;
        var targetUnits = attackerIsPlayer ? combat.EnemyUnits : combat.PlayerUnits;
        if (!targetUnits.TryGetValue(target, out var count) || count <= 0)
            return;

        if (!attackerIsPlayer && combat.PainkillerActive)
        {
            var roll = _combatRandom.Next(1, 7);
            if (roll >= 5)
            {
                combat.Log.Add(new($"Painkiller roll {roll}: prevent the loss of 1 {SingularUnit(target)}."));
                return;
            }
            combat.Log.Add(new($"Painkiller roll {roll}: the {SingularUnit(target)} loss is not prevented.", true));
        }

        targetUnits[target] = count - 1;
        combat.Log.Add(new(attackerIsPlayer ? $"Enemy loses 1 {SingularUnit(target)}." : $"The Host loses 1 {SingularUnit(target)}."));
    }

    private void CompleteCombatLane(string type)
    {
        var combat = Combat!;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;

        var playerCasualtiesInflicted = combat.PlayerRoll.Count(d => d.Hit) > 0;
        if (playerCasualtiesInflicted && combat.CommittedTactics.Contains("ADV-013")
            && string.Equals(combat.SteadyAdvanceType, type, StringComparison.OrdinalIgnoreCase)
            && !combat.SteadyAdvanceAwardedThisRound)
        {
            combat.PlayerLeadership++;
            combat.SteadyAdvanceAwardedThisRound = true;
            combat.Log.Add(new("Steady Advance restores 1 Leadership."));
        }
        if (playerCasualtiesInflicted && type == "Cavalry" && combat.CommittedTactics.Contains("ADV-011"))
            combat.FeignedWithdrawalReady = true;
        if (playerCasualtiesInflicted && type == "Archers" && combat.CommittedTactics.Contains("ADV-012"))
            combat.CoveringFirePending = true;

        if (CheckCombatEnd())
            return;

        if (combat.CoveringFirePending && type == "Archers")
        {
            combat.CoveringFirePending = false;
            combat.Step = CombatStep.CasualtyChoice;
            combat.ChoicePrompt = "Covering Fire: choose another Military unit type to roll +1 die this round.";
            foreach (var nextType in new[] { "Cavalry", "Infantry" }.Where(t => combat.PlayerUnits[t] > 0))
                combat.Choices.Add(new($"covering:{nextType}", nextType));
            if (combat.Choices.Count > 0)
                return;
        }

        if (type == "Cavalry" && combat.FeignedWithdrawalReady && !combat.NoDisengage && combat.MayDisengage)
        {
            combat.Step = CombatStep.CasualtyChoice;
            combat.ChoicePrompt = "Feigned Withdrawal is available because your Cavalry inflicted a casualty.";
            combat.Choices.Add(new("feigned:use", "Disengage now", "No Disengage penalty and no Cavalry pursuit"));
            combat.Choices.Add(new("feigned:continue", "Continue the battle"));
            return;
        }

        AdvanceToNextMeaningfulCombatStep(type);
    }

    private bool CheckCombatEnd()
    {
        var combat = Combat!;
        // A destroyed Host is defeated even if the final exchange also wipes
        // out the enemy. The campaign cannot be won by mutual annihilation.
        if (CombatUnitTypes.Sum(t => combat.PlayerUnits[t]) <= 0)
        {
            FinishInteractiveCombat("Lost");
            return true;
        }
        if (CombatUnitTypes.Sum(t => combat.EnemyUnits[t]) <= 0)
        {
            FinishInteractiveCombat("Won");
            return true;
        }
        return false;
    }

    private void PresentRoundEndChoices()
    {
        var combat = Combat!;
        combat.ChoicePrompt = $"Round {combat.Round} is complete.";
        combat.Choices.Clear();
        combat.Choices.Add(new("round:continue", "Continue to the next round"));
        if (!combat.NoDisengage && combat.MayDisengage)
        {
            var disengageHint = _combatResumeMode == CombatResumeMode.ArrivalForced || _activeFlowContext == FlowContext.Arrival
                ? "Forced Mobilization follows"
                : _activeFlowContext == FlowContext.Explore
                    ? "Resume Mobilization and proceed to the Clearing"
                    : "Leave the battle after this round";
            combat.Choices.Add(new(
                combat.FeignedWithdrawalReady ? "round:feigned" : "round:disengage",
                combat.FeignedWithdrawalReady ? "Disengage with Feigned Withdrawal" : "Disengage",
                combat.FeignedWithdrawalReady ? "No Disengage penalty and no Cavalry pursuit" : disengageHint));
        }
    }

    public void ResolveCombatChoice(string choiceId)
    {
        var combat = Combat;
        if (combat is null || string.IsNullOrWhiteSpace(choiceId))
            return;

        combat.ChoiceTargetSide = null;

        if (choiceId == "firepots:use")
        {
            ResolveFirepotsChoice(useFirepots: true);
        }
        else if (choiceId == "firepots:keep")
        {
            ResolveFirepotsChoice(useFirepots: false);
        }
        else if (choiceId == "priest:attempt")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            ResolvePriestStep();
        }
        else if (choiceId == "priest:skip")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            combat.Log.Add(new("The Priest does not attempt Proselytization."));
            StartNewCombatRound();
        }
        else if (choiceId.StartsWith("steady:", StringComparison.OrdinalIgnoreCase))
        {
            combat.SteadyAdvanceType = choiceId[7..];
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            BeginCombatRounds();
        }
        else if (choiceId.StartsWith("inspired:", StringComparison.OrdinalIgnoreCase))
        {
            var type = choiceId[9..];
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            if (!string.Equals(type, "skip", StringComparison.OrdinalIgnoreCase))
            {
                combat.PlayerLeadership--;
                combat.InspiredType = type;
                combat.BonusDiceThisRound[type] = combat.PlayerUnits[type] >= 2 ? 2 : 1;
                combat.Log.Add(new($"Inspired: spend 1 Leadership. {type} gain +{combat.BonusDiceThisRound[type]} attack dice this round."));
            }
            AdvanceToNextMeaningfulCombatStep(null);
        }
        else if (choiceId.StartsWith("reroll:", StringComparison.OrdinalIgnoreCase))
        {
            ResolveCombatRerollChoice(choiceId);
        }
        else if (choiceId.StartsWith("target:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = choiceId.Split(':', 3);
            if (parts.Length == 3)
            {
                var enemyTarget = string.Equals(parts[1], "enemy", StringComparison.OrdinalIgnoreCase);
                var target = parts[2];
                combat.Choices.Clear();
                combat.ChoicePrompt = string.Empty;
                if (!enemyTarget && PresentPlayerDefense(target))
                {
                    Changed?.Invoke();
                    return;
                }
                if (enemyTarget && target != "Levy" && combat.EnemyShields > 0)
                {
                    combat.EnemyShields--;
                    combat.Log.Add(new($"Middelalderen spends 1 Shield to prevent the casualty to {target}."));
                }
                else
                {
                    ApplyCombatCasualty(enemyTarget, target);
                }
                if (enemyTarget) combat.PendingPlayerHits--; else combat.PendingEnemyHits--;
                ContinueCombatCasualties();
            }
        }
        else if (choiceId.StartsWith("defense:", StringComparison.OrdinalIgnoreCase))
        {
            ResolveCombatDefenseChoice(choiceId);
        }
        else if (choiceId.StartsWith("covering:", StringComparison.OrdinalIgnoreCase))
        {
            var type = choiceId[9..];
            combat.BonusDiceThisRound[type] = (combat.BonusDiceThisRound.TryGetValue(type, out var bonus) ? bonus : 0) + 1;
            combat.Log.Add(new($"Covering Fire grants {type} +1 attack die this round."));
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            AdvanceToNextMeaningfulCombatStep("Archers");
        }
        else if (choiceId == "feigned:use")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            combat.Log.Add(new("Feigned Withdrawal breaks contact before the enemy can pursue."));
            if (!combat.IsTest)
                _flowVars["Combat.FeignedWithdrawal"] = true;
            FinishInteractiveCombat("Disengaged");
        }
        else if (choiceId == "feigned:continue")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            combat.FeignedWithdrawalReady = false;
            AdvanceToNextMeaningfulCombatStep("Cavalry");
        }
        else if (choiceId == "round:continue")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            combat.Round++;
            StartNewCombatRound();
        }
        else if (choiceId is "round:disengage" or "round:feigned")
        {
            combat.Choices.Clear();
            combat.ChoicePrompt = string.Empty;
            if (choiceId == "round:disengage" && combat.EnemyUnits["Cavalry"] > 0)
            {
                ResolveCavalryPursuit();
            }
            else
            {
                FinishInteractiveCombat("Disengaged");
            }
        }

        Changed?.Invoke();
    }

    private void ResolveFirepotsChoice(bool useFirepots)
    {
        var combat = Combat!;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;
        combat.FirepotsResolved = true;

        if (!useFirepots)
        {
            combat.Log.Add(new("The Host keeps the Firepots in the Baggage Train."));
            BeginCombatRounds();
            return;
        }

        combat.FirepotsAvailable = false;
        ActiveTokens.Remove("Firepots");
        var slotIndex = Baggage.FindIndex(b => string.Equals(b.Contents, "Firepots", StringComparison.OrdinalIgnoreCase));
        if (slotIndex >= 0)
            Baggage[slotIndex] = Baggage[slotIndex] with { Contents = null, Quantity = 1 };

        var rolls = Enumerable.Range(0, 3).Select(_ => _combatRandom.Next(1, 7)).ToArray();
        var hits = rolls.Count(v => v >= 4);
        combat.Log.Add(new($"Firepots: {string.Join(", ", rolls)}. {hits} hit{(hits == 1 ? string.Empty : "s")} at 4+."));
        if (hits <= 0)
        {
            BeginCombatRounds();
            return;
        }

        combat.ResolvingFirepots = true;
        combat.PendingAttackerType = "Cavalry";
        combat.PendingPlayerHits = hits;
        combat.PendingEnemyHits = 0;
        combat.ResolvingPlayerHits = true;
        ContinueCombatCasualties();
    }

    private void ResolveCombatRerollChoice(string choiceId)
    {
        var combat = Combat!;
        var type = combat.CurrentUnitType ?? "Infantry";
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;
        if (!choiceId.Equals("reroll:keep", StringComparison.OrdinalIgnoreCase))
        {
            var raw = choiceId[7..];
            if (int.TryParse(raw, out var index) && index >= 0 && index < combat.PlayerRoll.Count && combat.PlayerLeadership > 0)
            {
                var old = combat.PlayerRoll[index].Value;
                var value = _combatRandom.Next(1, 7);
                combat.PlayerLeadership--;
                combat.RerolledTypesThisRound.Add(type);
                combat.PlayerRoll[index] = new(value, false, true);
                MarkCombatHits(type);
                combat.Log.Add(new($"Spend 1 Leadership to reroll a {type} die: {old} → {value}.", true));
            }
        }
        if (type == "Levy")
            ApplyLevyRout();
        BeginCombatCasualties(type);
    }

    private void ResolveCombatDefenseChoice(string choiceId)
    {
        var combat = Combat!;
        var parts = choiceId.Split(':', 3);
        if (parts.Length < 3)
            return;
        var action = parts[1];
        var type = parts[2];
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;
        if (action == "shield" && combat.PlayerShields > 0)
        {
            combat.PlayerShields--;
            combat.Log.Add(new($"A Shield prevents the casualty to {type}."));
        }
        else if (action == "loose" && combat.PlayerLeadership > 0)
        {
            combat.PlayerLeadership--;
            ApplyCombatCasualty(attackerIsPlayer: false, type);
            combat.Log.Add(new("Loose Formation spends 1 Leadership to redirect the casualty."));
        }
        else
        {
            ApplyCombatCasualty(attackerIsPlayer: false, type);
        }
        combat.PendingEnemyHits--;
        ContinueCombatCasualties();
    }

    private void ResolveCavalryPursuit()
    {
        var combat = Combat!;
        combat.Log.Add(new("Enemy Cavalry pursue the disengaging Host. The enemy receives one free attack round at -1 DRM."));
        combat.ResolvingPursuit = true;
        combat.PursuitLaneIndex = 0;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;
        ResolveNextPursuitLane();
    }

    private void ResolveNextPursuitLane()
    {
        var combat = Combat!;

        while (combat.PursuitLaneIndex < CombatUnitTypes.Length)
        {
            var type = CombatUnitTypes[combat.PursuitLaneIndex];
            var dice = Math.Min(6, combat.EnemyUnits[type] + EnemyCombatBonusDice(type));
            if (dice <= 0)
            {
                combat.Log.Add(new($"Pursuit {type} skipped: no {type} present.", true));
                combat.PursuitLaneIndex++;
                continue;
            }

            combat.CurrentUnitType = type;
            combat.PendingAttackerType = type;
            combat.PlayerRoll.Clear();
            combat.EnemyRoll.Clear();

            var enemyWeaponDrm = type switch
            {
                "Archers" when combat.EnemyAdvancements.Contains("ADV-M6") => 1,
                "Cavalry" when combat.EnemyAdvancements.Contains("ADV-M5") => 1,
                "Infantry" when combat.EnemyAdvancements.Contains("ADV-M4") => 1,
                _ => 0
            };
            var pursuitDrm = enemyWeaponDrm - 1;
            var threshold = CombatHitThreshold(type);

            for (var i = 0; i < dice; i++)
            {
                var value = _combatRandom.Next(1, 7);
                var hit = value + pursuitDrm >= threshold;
                var rout = type == "Levy"
                    && value == 1
                    && !combat.EnemyAdvancements.Contains("ADV-M8");
                combat.EnemyRoll.Add(new(value, hit, false, rout));
            }

            var hits = combat.EnemyRoll.Count(d => d.Hit);
            var drmText = pursuitDrm == 0 ? "0" : pursuitDrm > 0 ? $"+{pursuitDrm}" : pursuitDrm.ToString();
            combat.Log.Add(new($"Pursuit {type}: {RollList(combat.EnemyRoll)}. DRM {drmText}; {hits} hit{(hits == 1 ? string.Empty : "s")}.", true));

            // Levy rout remains based on the natural die result. The pursuit
            // DRM changes their chance to hit, not the natural-1 rout rule.
            if (type == "Levy")
                ApplyLevyRout();

            combat.PendingPlayerHits = 0;
            combat.PendingEnemyHits = hits;
            combat.ResolvingPlayerHits = true;
            ContinueCombatCasualties();
            return;
        }

        combat.ResolvingPursuit = false;
        combat.PendingAttackerType = null;
        combat.CurrentUnitType = null;
        if (!CheckCombatEnd())
            FinishInteractiveCombat("Disengaged");
    }

    private void CompletePursuitLane()
    {
        var combat = Combat!;
        combat.PendingPlayerHits = 0;
        combat.PendingEnemyHits = 0;
        combat.PendingTargetType = null;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;

        if (CheckCombatEnd())
        {
            combat.ResolvingPursuit = false;
            return;
        }

        combat.PursuitLaneIndex++;
        ResolveNextPursuitLane();
    }

    private void FinishInteractiveCombat(string outcome)
    {
        var combat = Combat!;
        combat.Outcome = outcome;
        combat.ResolvingPursuit = false;
        combat.Step = CombatStep.Complete;
        combat.AwaitingClose = true;
        combat.Choices.Clear();
        combat.ChoicePrompt = string.Empty;
        combat.ChoiceTargetSide = null;
        combat.Log.Add(new(outcome switch
        {
            "Won" => "The enemy force is broken. The Host wins the Combat.",
            "Lost" => "The Host is destroyed. The Combat is lost.",
            "Disengaged" => "The Host breaks contact and disengages from the Combat.",
            _ => "Combat ends."
        }));
    }

    private void ArchiveCompletedBattle(CombatState combat, string outcome)
    {
        BattleHistory.Add(new CompletedBattleLog
        {
            BattleNumber = BattleHistory.Count + 1,
            StartedUtc = combat.StartedUtc,
            EndedUtc = DateTime.UtcNow,
            SourceLabel = combat.SourceLabel,
            EnemyName = combat.EnemyName,
            IsFinale = combat.IsFinale || _combatResumeMode == CombatResumeMode.Finale,
            Outcome = outcome,
            InitialPlayerUnits = new(combat.InitialPlayerUnits, StringComparer.OrdinalIgnoreCase),
            InitialEnemyUnits = new(combat.InitialEnemyUnits, StringComparer.OrdinalIgnoreCase),
            FinalPlayerUnits = new(combat.PlayerUnits, StringComparer.OrdinalIgnoreCase),
            FinalEnemyUnits = new(combat.EnemyUnits, StringComparer.OrdinalIgnoreCase),
            Entries = combat.Log.ToList()
        });
    }

    public void CancelTestCombat()
    {
        if (Combat is { IsFinale: true, IsTest: true })
        {
            CancelTestFinale();
            return;
        }
        if (Combat is null || !Combat.IsTest)
            return;
        Combat = null;
        CombatVisible = false;
        _combatResumeMode = CombatResumeMode.None;
        Changed?.Invoke();
    }

    public void CloseCombat()
    {
        var combat = Combat;
        if (combat is null || !combat.AwaitingClose)
            return;

        var outcome = combat.Outcome ?? "Lost";
        var isTest = combat.IsTest;
        var isFinale = combat.IsFinale || _combatResumeMode == CombatResumeMode.Finale;
        var earnedVictoryLeadership = !isTest && string.Equals(outcome, "Won", StringComparison.OrdinalIgnoreCase);

        // Every successful Combat awards 1 Leadership, whether the battle was
        // chosen by the player or forced by a card/rule. Card-specific rewards
        // remain separate and are resolved by their normal EventFlow.
        if (earnedVictoryLeadership)
        {
            combat.PlayerLeadership += 1;
            combat.Log.Add(new("Combat victory reward: gain 1 Leadership."));
        }

        if (!isTest)
        {
            ArchiveCompletedBattle(combat, outcome);
            CommitCombatStateToCampaign(combat);
            if (earnedVictoryLeadership)
                AddNarrativeText("Combat victory reward: gain 1 Leadership.");
        }

        Combat = null;
        CombatVisible = false;

        if (isFinale)
        {
            CompleteFinaleCombat(outcome, combat);
            _combatResumeMode = CombatResumeMode.None;
            Changed?.Invoke();
            return;
        }

        if (isTest || _combatResumeMode == CombatResumeMode.Test)
        {
            _combatResumeMode = CombatResumeMode.None;
            Changed?.Invoke();
            return;
        }

        // Destruction of the Host is a campaign defeat regardless of which
        // card, Arrival, Scene, or Echo initiated the Combat.
        if (outcome == "Lost")
        {
            _combatResumeMode = CombatResumeMode.None;
            _combatResumeNextNode = null;
            _pendingCombatNextNode = null;

            var authoredDefeat = SpecialCombatDefeatReason(combat);
            if (!string.IsNullOrWhiteSpace(authoredDefeat))
                EndCampaign("Defeat", authoredDefeat);
            else
            {
                AddNarrativeText("The Host is destroyed in battle.");
                EndCampaign("Defeat", "No Units remain in the Host. The campaign ends in defeat.");
            }
            Changed?.Invoke();
            return;
        }

        if (_combatResumeMode == CombatResumeMode.ArrivalForced)
        {
            // Clear the old resume state before continuing. The continuation can
            // synchronously create another Combat (for example, Disengage ->
            // Forced Mobilization -> hostile Arrival). Clearing afterward would
            // overwrite the new Combat's resume mode and deadlock the Chronicle.
            _combatResumeMode = CombatResumeMode.None;
            _combatResumeNextNode = null;
            _arrivalCombatResolved = true;
            _specialPrompt = null;
            if (outcome == "Won")
            {
                AddNarrativeText("The Host wins the battle.");
                CaptureCurrentSettlement();
                CompleteArrivalStep();
            }
            else if (outcome == "Disengaged")
            {
                AddNarrativeText("The Host disengages and is forced onward.");
                BeginForcedMobilization();
            }
            Changed?.Invoke();
            return;
        }

        if (_combatResumeMode == CombatResumeMode.Flow)
        {
            if (_activeFlowContext == FlowContext.Arrival)
                _arrivalCombatResolved = true;
            if (outcome == "Disengaged")
                _combatDisengagedPending = true;
            if (_flowVars.TryGetValue("Combat.FeignedWithdrawal", out var feigned) && feigned is true)
                _skipNextDisengagePenaltyAction = true;
            _flowVars["Combat.Outcome"] = outcome;
            AddNarrativeText(outcome switch
            {
                "Won" => "The Host wins the battle.",
                "Disengaged" => "The Host disengages after the first round.",
                _ => "The battle ends."
            });
            _activeNodeId = _combatResumeNextNode;
            _combatResumeNextNode = null;
            _combatResumeMode = CombatResumeMode.None;
            AdvanceFlow();
        }

        Changed?.Invoke();
    }

    private void CommitCombatStateToCampaign(CombatState combat)
    {
        foreach (var type in CombatUnitTypes)
        {
            var initial = combat.InitialPlayerUnits[type];
            var final = combat.PlayerUnits[type];
            var lost = Math.Max(0, initial - final);
            if (lost <= 0)
                continue;
            LoseCampaignCombatClass(type, lost);
        }

        Leadership = Math.Max(0, combat.PlayerLeadership);

        if (combat.Outcome is "Won" or "Disengaged")
        {
            foreach (var converted in combat.ConvertedForPlayer)
                AddConvertedCombatUnit(converted.Key, converted.Value);
        }
    }

    private void LoseCampaignCombatClass(string type, int amount)
    {
        if (amount <= 0)
            return;
        if (type == "Infantry")
        {
            var merc = Math.Min(amount, HostCount("Mercenaries"));
            if (merc > 0) LoseHostUnit("Mercenaries", merc, applyPainkiller: false);
            amount -= merc;
        }
        else if (type == "Cavalry")
        {
            var hedge = Math.Min(amount, HostCount("Hedge Knights"));
            if (hedge > 0) LoseHostUnit("Hedge Knights", hedge, applyPainkiller: false);
            amount -= hedge;
        }
        if (amount > 0)
            LoseHostUnit(type, amount, applyPainkiller: false);
    }

    private void AddConvertedCombatUnit(string type, int amount)
    {
        if (amount <= 0)
            return;
        if (type == "Levy")
        {
            var room = Math.Max(0, 6 - HostCount("Levy"));
            var gain = Math.Min(Math.Min(room, amount), AvailableForceCount("Levy"));
            SetHostCount("Levy", HostCount("Levy") + gain);
            AdjustAvailableForce("Levy", -gain);
            return;
        }
        var military = HostCount("Archers") + HostCount("Cavalry") + HostCount("Infantry");
        var roomMilitary = Math.Max(0, 10 - military);
        var gainMilitary = Math.Min(Math.Min(roomMilitary, amount), AvailableForceCount(type));
        SetHostCount(type, HostCount(type) + gainMilitary);
        AdjustAvailableForce(type, -gainMilitary);
    }

    public string CombatAdvanceLabel => Combat?.Step switch
    {
        CombatStep.Setup => "Begin Combat",
        CombatStep.Priest => "Resolve Priest",
        CombatStep.RoundStart => "Begin Round",
        CombatStep.Archers => "Resolve Archers",
        CombatStep.Cavalry => "Resolve Cavalry",
        CombatStep.Infantry => "Resolve Infantry",
        CombatStep.Levy => "Resolve Levy",
        CombatStep.RoundEnd => "End Round",
        _ => "Continue"
    };

    public string CombatStepLabel => Combat?.Step switch
    {
        CombatStep.Setup => "Battle Setup",
        CombatStep.Priest => "Priest",
        CombatStep.RoundStart => $"Round {Combat?.Round}",
        CombatStep.Archers => "Archers",
        CombatStep.Cavalry => "Cavalry",
        CombatStep.Infantry => "Infantry",
        CombatStep.Levy => "Levy",
        CombatStep.RerollChoice => "Leadership",
        CombatStep.CasualtyChoice => "Casualty",
        CombatStep.RoundEnd => $"Round {Combat?.Round} Complete",
        CombatStep.Complete => "Combat Complete",
        _ => "Combat"
    };

    public string CombatTacticName(string id) => _catalog?.GetAdvancement(id)?.Name ?? id;
    public string CombatTacticEffect(string id) => _catalog?.GetAdvancement(id)?.Effect ?? string.Empty;

    private static string RollList(IEnumerable<CombatDie> dice)
    {
        var values = dice.Select(d => d.Hit ? $"{d.Value}*" : d.Value.ToString()).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static string? FirstPresentUnit(IReadOnlyDictionary<string, int> units, IEnumerable<string> order)
        => order.FirstOrDefault(t => units.TryGetValue(t, out var count) && count > 0);

    private static string SingularUnit(string type) => type switch
    {
        "Archers" => "Archer",
        "Cavalry" => "Cavalry",
        "Infantry" => "Infantry",
        "Levy" => "Levy",
        _ => type
    };

    private static CombatStep StepForType(string type) => type switch
    {
        "Archers" => CombatStep.Archers,
        "Cavalry" => CombatStep.Cavalry,
        "Infantry" => CombatStep.Infantry,
        "Levy" => CombatStep.Levy,
        _ => CombatStep.Archers
    };

    private static CombatStep NextCombatStep(string type) => type switch
    {
        "Archers" => CombatStep.Cavalry,
        "Cavalry" => CombatStep.Infantry,
        "Infantry" => CombatStep.Levy,
        "Levy" => CombatStep.RoundEnd,
        _ => CombatStep.RoundEnd
    };
}
