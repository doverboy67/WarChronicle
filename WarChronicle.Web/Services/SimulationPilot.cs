using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

/// <summary>
/// Debug-only, human-inspired automated game player. It drives the real GameSession
/// with public legal actions; it never alters any mechanics or the live session.
/// If it encounters an unsupported decision, it halts with an explicit diagnosis.
/// </summary>
public sealed class SimulationPilot
{
    private readonly GameSession _game;
    private readonly Random _policyRandom = new();
    private string? _plannedDisengagementArrivalId; // Local intent: passage gamble -> first safe exit.
    private int _refitActions;
    private string _lastRefitKey = string.Empty;
    private int _decisions;
    private readonly List<SimulationDecision> _decisionsLog = [];
    public int Steps { get; private set; }
    public string? StalledReason { get; private set; }
    public IReadOnlyList<SimulationDecision> Decisions => _decisionsLog;

    public SimulationPilot(GameSession game) => _game = game;

    public bool Complete => _game.Phase == GamePhase.GameOver || StalledReason is not null;

    public void Step()
    {
        if (Complete) return;
        if (++Steps > 12000)
        {
            StalledReason = "Safety limit: more than 12,000 decisions in one campaign.";
            return;
        }
        _game.EnsurePhaseGateChronicle();
        // Do not carry an earlier settlement's exit plan into a later encounter.
        if (_plannedDisengagementArrivalId is not null &&
            !string.Equals(_game.PendingArrivalCard?.Id, _plannedDisengagementArrivalId,
                StringComparison.OrdinalIgnoreCase))
            _plannedDisengagementArrivalId = null;
        var before = Fingerprint();
        string action;

        try
        {
            action = DecideAndExecute() ?? "";
            if (action.Length == 0)
            {
                StalledReason = $"Unsupported decision at {_game.CalendarStamp}, " +
                    $"{_game.Phase} / {_game.MobilizationStep} / {_game.CampStep} / {_game.EndOfSeasonStep} / {_game.WinterStep}.";
                return;
            }
            var after = Fingerprint();
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                StalledReason = $"No state change after '{action}' at {_game.CalendarStamp}; stopped rather than skipping a game rule.";
                return;
            }
        }
        catch (Exception ex)
        {
            StalledReason = $"Error at {_game.CalendarStamp}: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        _decisions++;
        _decisionsLog.Add(new SimulationDecision(
            _game.CalendarStamp, action, _game.Phase.ToString(),
            _game.ResourceValue("Coin"), _game.Leadership,
            _game.Host.Where(x => x.Count > 0).GroupBy(x => x.Type)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count))));
    }

    private string Fingerprint()
    {
        var flow = _game.FlowPrompt;
        var c = _game.Combat;
        var f = _game.Finale;
        return string.Join("|",
            _game.Phase, _game.Year, _game.Season, _game.Time, _game.MobilizationStep,
            _game.CampStep, _game.EndOfSeasonStep, _game.WinterStep,
            _game.Chronicle.Count, _game.Chronicle.LastOrDefault()?.SelectedChoiceId,
            flow?.Heading, flow?.Prompt, flow?.Options.Count,
            string.Join(",", flow?.Options.Select(x => x.Id) ?? Enumerable.Empty<string>()),
            _game.CampHand.Count,
            string.Join(",", _game.PendingTerrainChoices),
            // CombatVisible is an actual UI/game-session state transition: opening
            // a pending battle is a valid step even when no Chronicle entry is added.
            _game.CombatVisible, _game.HasPendingCombat,
            c?.Step, c?.Round, c?.Choices.Count, c?.Log.Count, c?.AwaitingClose,
            c?.ChoicePrompt, c?.PlayerLeadership, c?.SteadyAdvanceType,
            c?.ResumeStep, c?.CurrentUnitType, c?.PendingAttackerType, c?.PendingTargetType,
            c?.PendingPlayerHits, c?.PendingEnemyHits,
            c?.PlayerShields, c?.EnemyShields, c?.FirepotsResolved, c?.ResolvingPursuit,
            string.Join(",", c?.PlayerUnits.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}") ?? Enumerable.Empty<string>()),
            string.Join(",", c?.EnemyUnits.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}") ?? Enumerable.Empty<string>()),
            string.Join(",", c?.Choices.Select(x => x.Id) ?? Enumerable.Empty<string>()),
            string.Join(",", c?.CommittedTactics.OrderBy(x => x) ?? Enumerable.Empty<string>()),
            f?.Step, f?.ResolveRolls.Count, f?.ResolveSelected, f?.Log.Count,
            f?.Leadership, f?.SteadyAdvanceType,
            string.Join(",", f?.CommittedTactics.OrderBy(x => x) ?? Enumerable.Empty<string>()),
            _game.ResourceValue("Coin"), _game.ResourceValue("Food"), _game.ResourceValue("Research"),
            _game.Morale, _game.Leadership,
            string.Join(",", _game.Host.Select(x => x.Id + ":" + x.Count)),
            string.Join(",", _game.AcquiredAdvancements.Select(x => x.Id)),
            string.Join(",", _game.AdvancementOffer.Select(x => x.Id)),
            string.Join(",", _game.HarvestStaging.Select(x => x.Key + ":" + x.Value)),
            _game.BattleHistory.Count, _game.CampaignOutcome);
    }

    private string? DecideAndExecute()
    {
        // Battle controls have priority over all phase gates and event prompts.
        if (_game.Combat is { } combat)
        {
            if (_game.HasPendingCombat) { _game.OpenPendingCombat(); return "Open battle board"; }
            if (combat.Choices.Count > 0)
            {
                var choice = ChooseCombat(combat);
                _game.ResolveCombatChoice(choice.Id);
                return $"Combat: {choice.Label}";
            }
            if (combat.AwaitingClose) { _game.CloseCombat(); return "Close combat"; }
            if (combat.Step == CombatStep.Setup && combat.CommittedTactics.Count == 0)
            {
                // Commit a cheap, currently legal unit-specific tactic if possible.
                var tactic = combat.PlayerTactics.FirstOrDefault(id =>
                    (id is "ADV-009" or "ADV-011" or "ADV-012") && _game.CanToggleCombatTactic(id));
                if (tactic is not null)
                {
                    _game.ToggleCombatTactic(tactic);
                    return $"Commit combat tactic {_game.CombatTacticName(tactic)}";
                }
            }
            _game.AdvanceCombat();
            return $"Advance combat {combat.Step}";
        }

        // The engine models every EventFlow choice, Tribute, Work option, Test,
        // and Winter roll as a legal FlowPrompt; do not duplicate their rules.
        if (_game.FlowPrompt is { } prompt)
        {
            var legal = prompt.Options.Where(x => !x.Disabled).ToList();
            if (legal.Count == 0) return null;
            var choice = ChoosePrompt(prompt, legal);
            _game.ResolveFlowPrompt(choice.Id);
            // A successful passage test should not leak a disengagement plan
            // into an unrelated subsequent Arrival event choice or battle.
            var plannedEscape = _plannedDisengagementArrivalId is not null &&
                _game.Combat is not null &&
                string.Equals(_plannedDisengagementArrivalId, _game.PendingArrivalCard?.Id,
                    StringComparison.OrdinalIgnoreCase);
            if (!plannedEscape &&
                (choice.Id is "gate:normal" or "test:normal"))
                _plannedDisengagementArrivalId = null;
            return $"{prompt.Heading}: {choice.Label}" +
                (plannedEscape ? " (plan early disengagement if attacked)" : "");
        }

        // Archived Chronicle gates are sometimes not FlowPrompts.
        if (_game.Chronicle.Count > 0)
        {
            var ix = _game.Chronicle.Count - 1;
            if (_game.IsChroniclePhaseGateActive(ix))
            {
                var gate = _game.Chronicle[ix].Choices!.First(x => !x.Disabled);
                _game.ResolveChronicleChoice(ix, gate.Id);
                return $"Phase gate: {gate.Label}";
            }
            if (_game.IsChronicleDecisionActive(ix))
            {
                var options = _game.Chronicle[ix].Choices?.Where(x => !x.Disabled).ToList();
                if (options?.Count > 0)
                {
                    var choice = options[0];
                    _game.ResolveChronicleChoice(ix, choice.Id);
                    return $"Chronicle: {choice.Label}";
                }
            }
        }

        if (_game.Phase == GamePhase.Finale && _game.Finale is { } finale)
        {
            switch (finale.Step)
            {
                case FinaleStep.ResolveReady:
                    var spend = finale.Leadership > 0 && finale.Morale <= 6;
                    _game.ResolveFinaleCheck(spend);
                    return spend ? "Finale Resolve: Leadership roll" : "Finale Resolve: normal roll";
                case FinaleStep.ResolveChoose:
                    var best = finale.ResolveRolls.IndexOf(finale.ResolveRolls.Min());
                    _game.ChooseFinaleResolveRoll(best);
                    return $"Finale Resolve: choose {finale.ResolveRolls[best]}";
                case FinaleStep.DeployHost:
                    if (finale.Leadership > 0 && !finale.CommittedTactics.Contains("ADV-013")
                        && _game.CanToggleFinaleTactic("ADV-013") &&
                        finale.PlayerUnits.Any(kv => (kv.Key is "Archers" or "Cavalry" or "Infantry") && kv.Value > 0))
                    {
                        _game.ToggleFinaleTactic("ADV-013");
                        return "Commit Steady Advance";
                    }
                    if (finale.CommittedTactics.Contains("ADV-013") && string.IsNullOrWhiteSpace(finale.SteadyAdvanceType))
                    {
                        var type = finale.PlayerUnits.Where(kv => kv.Value > 0 && kv.Key != "Levy")
                            .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                        if (type is null) return null;
                        _game.SetFinaleSteadyAdvanceType(type);
                        return $"Steady Advance: {type}";
                    }
                    if (!_game.CanRevealMiddelalderen) return null;
                    _game.RevealMiddelalderen();
                    return "Reveal Middelalderen";
                case FinaleStep.RevealMiddelalderen:
                    _game.StartFinaleCombat();
                    return "Begin Finale combat";
                case FinaleStep.Combat:
                    return null; // Combat must exist and is handled above.
                case FinaleStep.Complete:
                    return null;
            }
        }

        if (_game.Phase == GamePhase.Mobilization)
        {
            if (_game.MobilizationStep == MobilizationStep.TerrainChoice)
            {
                var terrain = _game.PendingTerrainChoices
                    .OrderByDescending(t => t == TerrainType.Plains ? 3 : t == TerrainType.Forest ? 2 : 1)
                    .FirstOrDefault();
                if (terrain == TerrainType.None) return null;
                _game.ChooseTerrain(terrain);
                return $"Scouting: {terrain}";
            }
            if (_game.MobilizationStep == MobilizationStep.ExploreReady)
            { _game.BeginExplore(); return "Explore"; }
            if (_game.MobilizationStep == MobilizationStep.ArrivalReady)
            { _game.ProceedToArrival(); return "Proceed to Arrival"; }
        }
        if (_game.Phase == GamePhase.Camp)
        {
            if (_game.CampStep == CampStep.Ready)
            { _game.BeginCamp(); return "Begin Camp"; }
            if (_game.CampStep == CampStep.ChoosingAction)
            {
                var offered = _game.CampActionChoices.Where(_game.CanChooseCampCard).ToList();
                if (offered.Count == 0) return null;
                var card = ChooseCamp(offered);
                _game.ChooseCampCard(card.Id);
                return $"Camp: {card.Title}";
            }
        }
        if (_game.Phase == GamePhase.EndOfSeason)
        {
            switch (_game.EndOfSeasonStep)
            {
                case EndOfSeasonStep.Ready:
                    _game.BeginEndOfSeason(); return "Begin End of Season";
                case EndOfSeasonStep.VulnerabilityReady:
                    _game.ResolveEndOfSeasonVulnerability(); return "Vulnerability";
                case EndOfSeasonStep.HarvestReady:
                    _game.BeginEndOfSeasonHarvest(); return "Gather Harvest";
                case EndOfSeasonStep.HarvestPreparing:
                    _game.BeginEndOfSeasonPacking(); return "Begin packing";
                case EndOfSeasonStep.HarvestPacking:
                    var staged = _game.HarvestStaging
                        .Where(x => x.Value > 0 && _game.CanPackHarvestResource(x.Key))
                        .OrderByDescending(x => x.Key == "Food" ? 4 : x.Key == "Wood" ? 3 : 2).FirstOrDefault();
                    if (staged.Key is not null)
                    { _game.PackHarvestResource(staged.Key); return $"Pack {staged.Key}"; }
                    _game.FinishEndOfSeasonHarvest(); return "Finish Harvest";
                case EndOfSeasonStep.ConsolidationReady:
                    _game.ResolveEndOfSeasonConsolidation(); return "Consolidation";
                case EndOfSeasonStep.RefitReform:
                    return RefitAction();
            }
        }
        if (_game.Phase == GamePhase.Winter)
        {
            if (_game.WinterStep == WinterStep.Ready)
            { _game.BeginWinter(); return "Begin Winter"; }
            if (_game.WinterStep == WinterStep.RefitReform) return RefitAction();
        }
        return null;
    }

    private string RefitAction()
    {
        var key = $"{_game.Year}/{_game.Season}/{_game.Phase}";
        if (key != _lastRefitKey)
        { _refitActions = 0; _lastRefitKey = key; }
        if (++_refitActions > 24)
        { _game.FinishRefitReform(); return "Finish Refit: purchase limit"; }

        var military = Count("Archers") + Count("Infantry") + Count("Cavalry");
        var coin = _game.ResourceValue("Coin");
        var food = _game.ResourceValue("Food");
        var research = _game.ResourceValue("Research");
        // No more routine R&R follows Summer III. Research remaining after
        // this point has no reliable means of becoming an Advancement.
        var finalRefit = _game.Year >= 3;
        var minimumMilitary = _game.Year >= 3 ? 8 : _game.Year == 2 ? 6 : 4;
        var targetMilitary = _game.Year >= 3 ? 10 : _game.Year == 2 ? 8 : 6;
        var coinReserve = finalRefit ? 0 : 1;
        var unitOffers = new[] { "Archers", "Infantry", "Cavalry" }
            .Where(_game.CanRefitRecruit)
            .Select(t => (type: t, score: RecruitValue(t), cost: t == "Archers" ? 1 : 2))
            .OrderByDescending(t => t.score)
            .ToList();

        // A defensible core is worth immediate cash when soldiers can be
        // recruited, but NEVER gate Research purchases on an army headcount:
        // Armor and prospective unit-specific investments are useful even
        // before the appropriate units have joined the Host.
        if (military < minimumMilitary && unitOffers.Count > 0)
        {
            var chosen = unitOffers[0].type;
            _game.RefitRecruit(chosen);
            return $"Recruit {chosen} (build permanent Host: {military + 1}/{minimumMilitary} minimum)";
        }

        // R&R shows Armor (from a separate supply) and the current top
        // Advancement. Each Armor has a DISTINCT ID, so buying one must not
        // prevent purchasing the second or third copy later.
        var ownedIds = _game.AcquiredAdvancements.Select(a => a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offered = _game.AdvancementOffer
            .Where(a => !ownedIds.Contains(a.Id))
            .Select(a => (card: a, score: AdvancementValue(a.Name),
                researchCost: AdvancementCost(a.Cost, "R"),
                coinCost: AdvancementCost(a.Cost, "C"),
                affordable: _game.CanBuyRefitAdvancement(a.Id)))
            .OrderByDescending(a => a.score)
            .ToList();

        // During Years I-II, banking RT is intentional if a compelling
        // visible Advancement is just out of reach in Research or Coin.
        // The offered top card stays on top for the next R&R. Do not buy a
        // merely satisfactory Armor instead if it would spoil that plan.
        // At Summer III R&R this exception disappears: spend every RT that
        // can legally be converted into useful Advancements right now.
        var bankForFuture = !finalRefit && research > 0 && offered.Any(a =>
            !a.affordable && !a.card.Name.Equals("Armor", StringComparison.OrdinalIgnoreCase)
            && a.researchCost > 0 && a.score >= 7.0
            && a.researchCost - research <= 2 && a.coinCost - coin <= 2);

        var affordable = offered.Where(a => a.affordable)
            .OrderByDescending(a => a.score)
            .ThenByDescending(a => a.researchCost)
            .ToList();
        if (!bankForFuture && affordable.Count > 0 &&
            // In the final R&R take even modest useful upgrades if those
            // Research Tokens otherwise become worthless.
            affordable[0].score >= (finalRefit ? 1.0 : 4.0))
        {
            var chosen = affordable[0].card;
            _game.BuyRefitAdvancement(chosen.Id);
            return finalRefit ? $"Acquire {chosen.Name} (final R&R: spend Research)"
                : $"Acquire {chosen.Name} (current/future Host investment)";
        }

        // Build out a diversified permanent Host when Coin remains; never
        // recruit a seventh unit of one type merely to meet a headcount goal.
        if (military < targetMilitary)
        {
            var recruit = unitOffers.FirstOrDefault(t =>
                coin - t.cost >= coinReserve && Count(t.type) < 6);
            if (recruit.type is not null)
            {
                _game.RefitRecruit(recruit.type);
                return $"Recruit {recruit.type} (build permanent Host: {military + 1}/{targetMilitary} target)";
            }
        }

        // Levy fill the defensive line without costing Research. Reserving
        // Food early still leaves flexibility for Cavalry and tribute.
        if (Count("Levy") < (food > 1 ? 4 : 2) &&
            (food > 1 || military < minimumMilitary) && _game.CanRefitRecruit("Levy"))
        {
            _game.RefitRecruit("Levy");
            return "Recruit Levy (support permanent Military Units)";
        }

        // Re-check inexpensive (but worthwhile) purchases after recruitment.
        // This is normally redundant with the pass above but makes explicit
        // that the no-minimum rule applies to every stage of R&R.
        if (!bankForFuture && affordable.Count > 0 &&
            affordable[0].score >= (finalRefit ? 0.5 : 3.0))
        {
            var chosen = affordable[0].card;
            _game.BuyRefitAdvancement(chosen.Id);
            return $"Acquire {chosen.Name}";
        }

        _game.FinishRefitReform();
        return bankForFuture ? "Finish Refit & Reform (bank Research for offered Advancement)"
            : "Finish Refit & Reform";
    }

    // Use the same printed R/C tokens as GameSession's advancement-cost
    // check, but the game engine alone decides actual legal affordability.
    private static int AdvancementCost(string cost, string resource)
    {
        var match = System.Text.RegularExpressions.Regex.Match(cost ?? string.Empty,
            $@"(\d+)\s*{resource}(?:T)?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private double AdvancementValue(string name)
    {
        var lower = name.ToLowerInvariant();
        var archers = Count("Archers");
        var infantry = Count("Infantry");
        var cavalry = Count("Cavalry");
        var levy = Count("Levy");
        var futureRecruitment = _game.Year < 3;
        var armorOwned = _game.AcquiredAdvancements.Count(a =>
            a.Name.Equals("Armor", StringComparison.OrdinalIgnoreCase));

        // Value future recruitment as well as today's fighting strength.
        // A development is not useless merely because it has no matching
        // units yet: an early Longbows or Armor purchase can shape the Host.
        // By the final R&R, matching troops matter much more.
        if (lower.Contains("armor")) return 7.8 - .45 * armorOwned;
        if (lower.Contains("halberd"))
            return 6.3 + 1.15 * Math.Min(4, infantry) +
                (infantry == 0 ? (futureRecruitment ? 1.0 : -2.6) : 0);
        if (lower.Contains("stirrup"))
            return 6.7 + 1.1 * Math.Min(4, cavalry) +
                (cavalry == 0 ? (futureRecruitment ? .6 : -3.5) : 0);
        if (lower.Contains("longbow"))
            return 6.8 + .8 * Math.Min(4, archers) +
                (archers == 0 ? (futureRecruitment ? .8 : -3.0) : 0);
        if (lower.Contains("darken")) return 5.2 + .6 * Math.Min(4, archers) +
            (archers == 0 && !futureRecruitment ? -2.0 : 0);
        if (lower.Contains("charge")) return 4.6 + .6 * Math.Min(4, cavalry) +
            (cavalry == 0 && !futureRecruitment ? -2.0 : 0);
        if (lower.Contains("surge")) return 4.4 + .55 * Math.Min(4, infantry) +
            (infantry == 0 && !futureRecruitment ? -2.0 : 0);
        if (lower.Contains("gritty")) return 5.1 + .3 * Math.Min(6, levy);
        if (lower.Contains("covering")) return 4.4 + .6 * Math.Min(4, archers) +
            (archers == 0 && !futureRecruitment ? -2.0 : 0);
        if (lower.Contains("steady advance"))
            return 6.0 + .3 * Math.Min(6, archers + infantry + cavalry);
        if (lower.Contains("inspired"))
            return 5.2 + .4 * Math.Min(6, archers + infantry + cavalry);
        if (lower.Contains("loose formation"))
            return 5.3 + .3 * Math.Min(6, archers + infantry + cavalry);
        if (lower.Contains("feigned withdrawal"))
            return futureRecruitment ? 3.0 : .1; // Cannot disengage in Finale.
        return 3.5;
    }

    private int Count(string type) => _game.Host.Where(h => h.Type == type).Sum(h => h.Count);
    private bool Owns(string key) => _game.AcquiredAdvancements.Any(x =>
        x.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
    private double RecruitValue(string type)
    {
        var units = Count(type);
        // Offensive efficiency matters more than the raw number of bodies.
        // The seventh die of any one type cannot be rolled while six survive.
        // Give unit-specific advancements a BONUS, not an unlimited mandate to
        // buy Archers, and strongly prefer filling thin Infantry/Cavalry lines.
        var value = type switch { "Archers" => 3.5, "Infantry" => 4.5, _ => 4.8 };
        value -= units * .60;
        if (type == "Archers" && Owns("Longbow")) value += 1.0;
        if (type == "Archers" && Owns("Darken")) value += 1.0;
        if (type == "Infantry" && Owns("Halberd")) value += 1.3;
        if (type == "Cavalry" && Owns("Charge")) value += 1.3;
        if (type == "Cavalry" && _game.ResourceValue("Food") < 2) value -= 2.5;
        if (Count("Infantry") == 0 && type == "Infantry") value += 2.5;
        if (Count("Archers") == 0 && type == "Archers") value += 1.0;
        if (Count("Cavalry") == 0 && type == "Cavalry") value += 2.0;
        // Six dice of one type is the offensive cap. A seventh body is still
        // legal, but offers casualty depth only, not additional attack dice.
        if (units >= 6) value -= 8.0;
        // Penalize extreme imbalance even before reaching the hard dice cap.
        if (units >= 4 && (Count("Infantry") <= 1 || Count("Cavalry") <= 1))
            value -= 1.5;
        return value;
    }

    private bool IsFinaleYear => _game.Year == 3;
    private bool IsFinalAutumn => IsFinaleYear &&
        string.Equals(_game.Season, "Autumn", StringComparison.OrdinalIgnoreCase);

    // This is the last part of the road in which a completed 2-Time Camp
    // action is likely to be useful. Time 32+ is already too late: advancing
    // past the Autumn threshold of 33 enters Finale before card effects.
    private bool IsFinalCampPreparation => IsFinalAutumn && _game.Time >= 28;
    private bool CompletesBeforeFinale(CampCardState card) =>
        !IsFinalAutumn || _game.Time + card.Time <= 33;

    private CampCardState ChooseCamp(List<CampCardState> cards)
    {
        var coin = _game.ResourceValue("Coin");
        var research = _game.ResourceValue("Research");
        var military = Count("Archers") + Count("Infantry") + Count("Cavalry");
        var scored = cards.Select(c =>
        {
            // Work is reliable for money; research cannot be obtained that
            // way. Do not accumulate Coin indefinitely when the army is ready.
            var score = c.Id == "CAMP-001" ?
                (coin < 3 ? 14 : coin < 5 ? 9 : coin < 8 ? 5 : 1.5) : 3.0;
            if (c.Id == "CAMP-005") // Commission Study: 1 Coin -> 1 Research.
                score = IsFinalAutumn ? 1.0 : coin >= 3 ?
                    (research == 0 ? 17 : research == 1 ? 15 : research == 2 ? 11 : 6) :
                    (coin >= 2 ? 10 : 3);
            if (c.Id == "CAMP-010") // Drill can also spend Research NOW.
                score = IsFinalAutumn ? (research >= 2 ? 19 : 2) :
                    research == 0 ? 17 : research == 1 ? 15 : research == 2 ? 11 : 6;
            if (c.Title.Contains("Field Staff", StringComparison.OrdinalIgnoreCase))
                score = !_game.HasScouting ? 9.5 : 3;
            if (c.Title.Contains("Sacred Foundations", StringComparison.OrdinalIgnoreCase)) score = 6;
            if (c.Title.Contains("Frontier Works", StringComparison.OrdinalIgnoreCase)) score = 6;
            if (c.Title.Contains("Raise Local", StringComparison.OrdinalIgnoreCase))
                score = Count("Levy") < 6 && _game.ResourceValue("Food") > 0 && IsFinaleYear ? 21
                    : military < 3 && Count("Levy") < 4 && _game.ResourceValue("Food") > 0 ? 20 : 4;
            // Temporary Mercenaries disband in Winter. In Year III there is
            // no more Winter before the Finale: affordable, cap-exempt bodies
            // are worth spending the last Coin, even at the risk of tribute.
            // Earlier, buy only when survival really needs them. Using this
            // unique opportunity too early has a genuine opportunity cost.
            if (c.Title.Contains("Hire Mercenaries", StringComparison.OrdinalIgnoreCase))
                score = IsFinaleYear && coin >= 1 ? 45
                    : military == 0 && Count("Levy") <= 1 && coin >= 1 ? 25
                    : military == 0 && Count("Levy") <= 2 && coin >= 4 ? 19
                    : military <= 1 && Count("Levy") <= 2 && coin >= 6 ? 14 : 3;
            if (IsFinalCampPreparation)
            {
                // The final *resolvable* Camp is usually best spent on
                // immediate readiness: Work (1 Coin + 1 Leadership), Levy,
                // or cap-exempt Year III Mercenaries. Late Research/Academies
                // cannot normally be turned into Advancements in time.
                if (c.Id == "CAMP-001") score = 31;
                if (c.Title.Contains("Raise Local", StringComparison.OrdinalIgnoreCase)
                    && Count("Levy") < 6 && _game.ResourceValue("Food") > 0) score = 39;
                if (c.Title.Contains("Hire Mercenaries", StringComparison.OrdinalIgnoreCase)
                    && coin >= 1) score = 48;
            }
            if (!CompletesBeforeFinale(c))
            {
                // The engine will legally advance Time and immediately
                // trigger Finale, without resolving this card's benefit.
                // Prefer a zero-Time card if one exists; otherwise choose
                // Work as the harmless transition, not a fake investment.
                score = c.Time == 0 ? score + 20 : c.Id == "CAMP-001" ? 1 : -10;
            }
            if (c.Title.Contains("Respite", StringComparison.OrdinalIgnoreCase))
                score = _game.Morale <= 3 ? 19 :
                    (_game.Morale <= 4 && _game.Season.ToString() == "Autumn" ? 16 :
                    _game.Morale <= 4 ? 12 : 4);
            if (c.Title.Contains("Terms", StringComparison.OrdinalIgnoreCase))
                score = _game.Tribes.Any(t => t.Rapport == "Hostile") ? 7 : 2;
            // Every candidate is already verified legal by CanChooseCampCard.
            return (card:c,score:score + _policyRandom.NextDouble()*.15);
        }).OrderByDescending(x=>x.score).First();
        return scored.card;
    }

    // Diplomacy must be evaluated as a campaign-wide risk: adding a second
    // Hostile tribe is much worse than the first one. Never assume the next
    // Forced Explore will encounter a friendly settlement.
    private int HostileTribeCount => _game.Tribes.Count(t =>
        string.Equals(t.Rapport, "Hostile", StringComparison.OrdinalIgnoreCase));

    // The currently revealed Arrival prints the enemy's units by Year. Read
    // that information rather than assuming every Hostile tribe is identical.
    // Only used to choose a *legal* die option; combat remains engine-owned.
    private (int Archers, int Cavalry, int Infantry, int Levy) ArrivalEnemyUnits()
    {
        var printed = _game.PendingArrivalCard?.Host ?? string.Empty;
        var plain = System.Text.RegularExpressions.Regex.Replace(printed, @"<[^>]+>", "");
        var match = System.Text.RegularExpressions.Regex.Match(plain,
            $@"Y{_game.Year}:\s*(.*?)(?=\s*-\s*Y\d:|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return (0, 0, 0, 0);
        var units = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match unit in
                 System.Text.RegularExpressions.Regex.Matches(match.Groups[1].Value, @"(\d+)\s*([ACIL])"))
            units[unit.Groups[2].Value] = int.Parse(unit.Groups[1].Value);
        int Get(string key) => units.TryGetValue(key, out var count) ? count : 0;
        return (Get("A"), Get("C"), Get("I"), Get("L"));
    }

    private bool NeedLeadershipForDiplomaticSafety(bool currentTribeAlreadyHostile)
    {
        if (_game.Leadership <= 0) return false;
        var military = Count("Archers") + Count("Infantry") + Count("Cavalry");
        var total = military + Count("Levy") + Count("Mercenaries") + Count("Hedge Knights");
        var otherHostile = HostileTribeCount - (currentTribeAlreadyHostile ? 1 : 0);
        // Year I's tiny pre-refit Host is no match for a full tribal Host.
        // Spend even the last point of Leadership, without an arbitrary reserve.
        if (_game.Year == 1 || military < 4 || total <= 5) return true;
        // A potential *second* enemy is a strategic tipping point, whether
        // the current tribe is hostile already or failure will make it so.
        if (otherHostile >= 1 || HostileTribeCount >= 2) return true;
        // A large army can sometimes withstand one round and then escape.
        // But Cavalry pursuit or Reign in Blood removes the safe-exit option.
        if (_game.ActiveTokens.Contains("ReignInBlood")) return true;
        var enemy = ArrivalEnemyUnits();
        var enemyTotal = enemy.Archers + enemy.Cavalry + enemy.Infantry + enemy.Levy;
        if (enemyTotal <= 0) return true; // Unknown enemy: do not assume safety.
        if (enemy.Cavalry > 0) return true;
        var expectedEnemyHits = Math.Min(6, enemy.Archers) / 6.0 +
            Math.Min(6, enemy.Infantry) / 3.0 +
            Math.Min(6, enemy.Cavalry) / 2.0 +
            Math.Min(6, enemy.Levy) / 6.0;
        var screens = Count("Levy") + _game.AcquiredAdvancements.Count(a =>
            a.Name.Contains("Armor", StringComparison.OrdinalIgnoreCase));
        var acceptableExposure = screens >= 2 ? 2.1 : screens == 1 ? 1.5 : 0.9;
        // Save Leadership only when the friendly Host is genuinely strong and
        // the worst likely first round is relatively cheap to endure.
        var canSurviveOneRound = military >= 5 && total >= 7 &&
            expectedEnemyHits <= acceptableExposure &&
            total >= enemyTotal + 2;
        return !canSurviveOneRound;
    }

    // Arrival choose-one Tests may penalize Rapport even though the prompt
    // only says "Test 7+". Match this Test's printed failure outcome rather
    // than treating every 7+/8+ roll as a diplomatic crisis.
    private bool ArrivalEventTestThreatensRapport(string prompt)
    {
        if (_game.PendingArrivalCard is null) return false;
        var target = System.Text.RegularExpressions.Regex.Match(prompt, @"(\d+)\s*\+");
        if (!target.Success) return false;
        var effect = _game.PendingArrivalCard.Effect;
        foreach (System.Text.RegularExpressions.Match entry in
                 System.Text.RegularExpressions.Regex.Matches(effect,
                     @"\((?<target>\d+)\+\).*?<li>\s*<b>Fail:</b>\s*(?<loss>.*?)</li>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            if (entry.Groups["target"].Value != target.Groups[1].Value) continue;
            var failure = entry.Groups["loss"].Value;
            if (failure.Contains("Rapport", StringComparison.OrdinalIgnoreCase) ||
                failure.Contains("Combat", StringComparison.OrdinalIgnoreCase) ||
                failure.Contains("Hostile", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool IsCurrentArrivalTribeHostile()
    {
        var tribe = _game.PendingArrivalCard?.Tribe;
        if (tribe is null) return false;
        return _game.Tribes.Any(t =>
            (string.Equals(t.Name, tribe, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Contains(tribe, StringComparison.OrdinalIgnoreCase) ||
             tribe.Contains(t.Name, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(t.Rapport, "Hostile", StringComparison.OrdinalIgnoreCase));
    }

    private void PlanDisengagementIfRiskingPassage() =>
        _plannedDisengagementArrivalId = _game.PendingArrivalCard?.Id;

    private bool PlannedDisengagementApplies(CombatState combat) =>
        _plannedDisengagementArrivalId is not null &&
        string.Equals(_plannedDisengagementArrivalId, _game.PendingArrivalCard?.Id,
            StringComparison.OrdinalIgnoreCase) &&
        _game.PendingArrivalCard is not null &&
        (combat.EnemyName.Contains(_game.PendingArrivalCard.Tribe, StringComparison.OrdinalIgnoreCase) ||
         _game.PendingArrivalCard.Tribe.Contains(combat.EnemyName, StringComparison.OrdinalIgnoreCase));

    private FlowPromptOption ChoosePrompt(FlowPromptState prompt, List<FlowPromptOption> legal)
    {
        string head = prompt.Heading.ToLowerInvariant();
        string ask = prompt.Prompt.ToLowerInvariant();
        var first = legal[0];
        FlowPromptOption? Id(string prefix) => legal.FirstOrDefault(c => c.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        FlowPromptOption? Text(string part) => legal.FirstOrDefault(c => c.Label.Contains(part,StringComparison.OrdinalIgnoreCase));

        if (head == "winter attrition")
        {
            // After spending Leadership the SAME heading appears again,
            // now with two "winter-roll:N" choices. Never choose the first
            // die blindly: select the LOWER actual result.
            var rolled = legal.Where(x => x.Id.StartsWith("winter-roll:", StringComparison.OrdinalIgnoreCase))
                .Select(x => (choice: x, value: int.TryParse(x.Label.Replace("Choose ", "", StringComparison.OrdinalIgnoreCase), out var n) ? n : int.MaxValue))
                .OrderBy(x => x.value).FirstOrDefault();
            if (rolled.choice is not null) return rolled.choice;

            // Before rolling, poor Morale puts valuable permanent troops at
            // risk even when Leadership is scarce.
            var military = Count("Archers") + Count("Infantry") + Count("Cavalry");
            var spend = _game.Leadership > 0 &&
                (_game.Morale <= 7 || (_game.Morale == 8 && military >= 6 && _game.Leadership >= 2));
            return spend ? Id("winter-attrition:leadership") ?? first
                : Id("winter-attrition:normal") ?? first;
        }
        if (head.Contains("roll") || head.Contains("choose result"))
        {
            var rollChoice = legal.Select(x => (item:x,value:int.TryParse(x.Label,out var n)?n:int.MaxValue))
                .OrderBy(x=>x.value).FirstOrDefault();
            if (rollChoice.value < int.MaxValue) return rollChoice.item;
        }
        if (Id("test:normal") is { } normal)
        {
            // The printed option may hide Rapport loss behind a generic
            // "Test 7+" heading. Preserve the pilot's earlier ordinary-test
            // valuation when failure has no diplomatic consequences.
            var diplomaticThreat = ArrivalEventTestThreatensRapport(ask) ||
                ask.Contains("hostility", StringComparison.OrdinalIgnoreCase) ||
                ask.Contains("combat", StringComparison.OrdinalIgnoreCase);
            if (diplomaticThreat)
            {
                var spendForSafety = NeedLeadershipForDiplomaticSafety(IsCurrentArrivalTribeHostile());
                if (spendForSafety && Id("test:leadership") is { } protection) return protection;
                PlanDisengagementIfRiskingPassage();
                return normal;
            }
            var ordinarySpend = _game.Leadership >= 2 &&
                (ask.Contains("hostile") || ask.Contains("lose ") || ask.Contains("8+"));
            return ordinarySpend ? Id("test:leadership") ?? normal : normal;
        }
        if (Id("gate:normal") is { } gate)
        {
            // Both Hostile-but-paid passage and unpaid Neutral passage use
            // this same engine prompt. Either can trigger fatal combat.
            // In Year I spend even the *last* Leadership; in a sturdy late
            // Host judge the enemy and the whole diplomatic landscape.
            var isPassage = head.Contains("passage") &&
                (ask.Contains("tribute", StringComparison.OrdinalIgnoreCase) ||
                 ask.Contains("passage", StringComparison.OrdinalIgnoreCase));
            if (isPassage)
            {
                if (NeedLeadershipForDiplomaticSafety(IsCurrentArrivalTribeHostile()) &&
                    Id("gate:leadership") is { } protection) return protection;
                PlanDisengagementIfRiskingPassage();
            }
            return gate;
        }
        if (head.Contains("march ör die") && Id("branch:0") is { } forcedMarch)
        {
            var breathe = Text("Let Them Breathe") ?? Id("branch:1");
            // Forced March's Explore discount has no value after the last
            // Autumn journey; never take a Leadership shortfall for it.
            return (_game.Leadership <= 0 || (IsFinalAutumn && _game.Time >= 28)
                    || _game.Morale <= 2)
                ? breathe ?? forcedMarch : forcedMarch;
        }
        if (head.Contains("hedge knights") && ask.Contains("recruit"))
        {
            // In Year III buy all affordable extra Cavalry (not part of the
            // permanent Host cap); in Years I/II they vanish next Winter.
            var max = legal.Where(x => x.Id.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
                .Select(x => (choice: x, count: int.TryParse(x.Id[7..], out var n) ? n : -1))
                .OrderByDescending(x => x.count).ToList();
            var permanent = Count("Archers") + Count("Cavalry") + Count("Infantry");
            var recruit = IsFinaleYear ? max.FirstOrDefault().count
                : permanent <= 1 && _game.ResourceValue("Coin") >= 4 ? 1 : 0;
            return max.FirstOrDefault(x => x.count == recruit).choice ?? first;
        }
        if (head.Contains("trade caravan"))
        {
            if (ask.Contains("choose a resource to sell"))
            {
                // The transaction is legal at 0 Coin. It is simply not a
                // sensible AI exchange unless baggage MUST be emptied (not
                // the case for the optional Trade Caravan sale).
                var profitable = legal.Where(x =>
                    x.Detail is not null && !x.Detail.Contains("Gain 0 Coin", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => int.TryParse(x.Detail?.Split(' ').ElementAtOrDefault(1), out var n) ? n : 0)
                    .FirstOrDefault();
                return profitable ?? first; // The sale was already committed.
            }
            if (ask.Contains("trade with the caravan"))
            {
                var buy = Text("Buy 1 Resource");
                var sell = Text("Sell 1 Resource");
                var pass = Text("Pass");
                // Buy scarce Food if there is space and enough Coin; otherwise
                // sell only resources with positive proceeds. Don't convert a
                // zero-value Food into literally nothing.
                if (buy is not null && _game.ResourceValue("Food") == 0 &&
                    _game.ResourceValue("Coin") >= 2) return buy;
                if (sell is not null &&
                    (_game.ResourceValue("Wood") > 0 || _game.ResourceValue("Stone") > 0) &&
                    _game.ResourceValue("Coin") < 4) return sell;
                return pass ?? buy ?? sell ?? first;
            }
        }
        if (legal.Any(x => x.Id.StartsWith("multi:", StringComparison.OrdinalIgnoreCase))
            && (ask.Contains("lose", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("plague pit", StringComparison.OrdinalIgnoreCase)))
            return ChooseLossUnit(legal);
        if (head.Contains("cursed battlefield") && ask.Contains("lose", StringComparison.OrdinalIgnoreCase)
            && legal.Any(x => x.Id.StartsWith("select:", StringComparison.OrdinalIgnoreCase)))
            return ChooseLossUnit(legal);
        if (Id("arrival:forage") is { } forage)
            return _game.ResourceValue("Food") == 0 || _game.ResourceValue("Wood") == 0
                ? forage : Id("arrival:continue") ?? first;
        if (head.Contains("friendly tribute") || legal.Any(c=>c.Id.StartsWith("tribute:friendly:")))
        {
            // Do not automatically give away the LAST Wood when plenty of
            // Coin exists: future settlements may demand Wood, with a costly
            // fallback. Use surplus physical resources where practical.
            if (_game.ResourceValue("Food") > 1 && Id("tribute:friendly:food") is { } food) return food;
            if (_game.ResourceValue("Wood") > 1 && Id("tribute:friendly:wood") is { } wood) return wood;
            if (_game.ResourceValue("Stone") > 1 && Id("tribute:friendly:stone") is { } stone) return stone;
            if (_game.ResourceValue("Coin") >= 5 && Id("tribute:friendly:coin") is { } coin) return coin;
            return Id("tribute:friendly:wood") ?? Id("tribute:friendly:food") ??
                Id("tribute:friendly:stone") ?? Id("tribute:friendly:coin") ?? first;
        }
        // On Hire Mercenaries' Select node, the legal list is Recruit 1,
        // Recruit 2. Picking the first entry wastes the emergency opportunity.
        if (head.Contains("hire mercenaries") && ask.Contains("recruit"))
        {
            var max = legal.Where(x => x.Id.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
                .Select(x => (choice: x, count: int.TryParse(x.Id[7..], out var n) ? n : -1))
                .OrderByDescending(x => x.count).ToList();
            // Early-game emergency: one affordable body can help survive,
            // but preserving Coin for the first permanent Refit matters.
            // In Y3 take the maximum, even if the treasury is emptied.
            var recruit = IsFinaleYear || Count("Archers") + Count("Cavalry") + Count("Infantry") == 0
                && _game.ResourceValue("Coin") >= 6 ? max.FirstOrDefault().count : 1;
            return max.FirstOrDefault(x => x.count == recruit).choice ?? max.First().choice;
        }
        if (head.Contains("riff raff") && ask.Contains("recruit") &&
            Count("Archers") + Count("Cavalry") + Count("Infantry") < 3)
            return legal.OrderByDescending(c => c.Id == "select:2" ? 2 : c.Id == "select:1" ? 1 : 0).First();
        if (head == "work")
            return IsFinalCampPreparation
                ? Text("1 Coin and 1 Leadership") ?? first
                : _game.Leadership < 1 && _game.ResourceValue("Coin") >= 3
                    ? Text("Leadership") ?? first : Text("2 Coin") ?? first;
        if (head.Contains("baggage") || ask.Contains("overflow"))
            return Id("baggage-overflow:purge:") ?? Text("Purge") ?? Text("Discard") ?? first;
        if (head.Contains("field staff")) return Text("Train Scouts") ?? first;
        if (head.Contains("drill"))
        {
            // When the legal, discounted purchase is available, use it if
            // Research is already stocked. Otherwise gain Research rather
            // than spending another Camp action earning surplus Coin.
            if (ask.Contains("choose drill result") && _game.ResourceValue("Research") >= 2)
                return Text("Purchase Advancement/Armor") ?? Text("Research") ?? first;
            if (ask.Contains("choose purchase"))
                return Text("Armor Development") ?? Text("Advancement") ?? first;
            if (ask.Contains("discard top advancement"))
                return Text("No") ?? first;
            return Text("Research") ?? first;
        }
        if (head.Contains("levies")) return Text("Recruit") ?? first;
        if (head.Contains("terms")) return legal.FirstOrDefault(x => x.Id.StartsWith("select:",StringComparison.OrdinalIgnoreCase)) ?? first;
        if (head.Contains("tribute")) return Text("Pay") ?? first;
        if (head.Contains("brigand") || head.Contains("thieves")) return Text("Refuse") ?? first;
        if (Id("camp-disposition:discard") is { } earlyMercDiscard &&
            _game.Year < 3 && head.Contains("hire mercenaries"))
            return earlyMercDiscard; // Leave the card eligible to recur in Y3.
        if (Id("camp-disposition:remove") is { } remove) return remove;
        if (Id("camp-disposition:discard") is { } discard) return discard;
        if (Id("branch:0") is { } branch) return branch;
        return first;
    }

    private FlowPromptOption ChooseLossUnit(List<FlowPromptOption> eligible)
    {
        // For unit-loss selections outside combat (Plague Pit, Cursed
        // Battlefield), account for *marginal* fighting strength. An excess
        // seventh Archer is cheaper to lose than an attack die; after that,
        // spend Levy before sacrificing more Military dice. Work only within
        // the selection the engine actually offered.
        double LossCost(FlowPromptOption option)
        {
            var unit = option.Id.StartsWith("multi:", StringComparison.OrdinalIgnoreCase)
                ? option.Id[6..] : option.Id.StartsWith("select:", StringComparison.OrdinalIgnoreCase)
                    ? option.Id[7..] : option.Label;
            var count = Count(unit);
            if (unit == "Levy") return 1;
            if (unit == "Mercenaries" || unit == "Hedge Knights")
                return IsFinaleYear ? 3.0 : 1.8;
            if (count > 6) return .6; // No immediate loss of attack dice.
            return unit switch
            {
                "Archers" => 4.0 + (Owns("Longbow") || Owns("Darken") ? .6 : 0),
                "Infantry" => 4.4 + (count == 1 ? 1.5 : 0),
                "Cavalry" => 5.0 + (count == 1 ? 1.5 : 0),
                _ => 4.0
            };
        }
        return eligible.OrderBy(LossCost).First();
    }

    private CombatChoice ChooseCombat(CombatState combat)
    {
        var options = combat.Choices;
        CombatChoice? Id(string prefix) => options.FirstOrDefault(x => x.Id.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
        if (Id("firepots:use") is { } firepots) return firepots;
        if (Id("priest:attempt") is { } priest) return priest;
        if (Id("reroll:keep") is { } keep)
        {
            if (combat.CurrentUnitType == "Levy")
            {
                // Special rule requested by the designer: spend Leadership
                // only to avert rout of a *single last surviving Levy*.
                // When multiple 1s appear, always select the first.
                if (combat.PlayerUnits.Values.Sum() == 1 &&
                    combat.PlayerUnits.TryGetValue("Levy", out var levy) && levy == 1)
                {
                    var routIndex = combat.PlayerRoll.FindIndex(d => d.Value == 1 && d.Rout);
                    if (routIndex >= 0 && Id($"reroll:{routIndex}") is { } saveLevy)
                        return saveLevy;
                }
                return keep;
            }

            // Hit is marked by the live engine and includes unit-specific
            // development modifiers. Never reroll a hit, and never ask the AI
            // to choose which identical miss: use the FIRST legal missed die.
            var missIndex = combat.PlayerRoll.FindIndex(d => !d.Hit);
            var canSpend = combat.IsFinale || combat.PlayerLeadership >= 2;
            if (missIndex >= 0 && canSpend && Id($"reroll:{missIndex}") is { } miss)
                return miss;
            return keep;
        }
        if (Id("defense:shield:") is { } shield) return shield;
        if (Id("defense:painkiller:") is { } dose) return dose;
        // Casually throwing away Archers used to be the default first option.
        // Respect the actual legal largest-stack casualty rule, sacrificing
        // weaker friendly types and choosing stronger enemy types when tied.
        var ownCasualties = options.Where(x => x.Id.StartsWith("target:player:", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ownCasualties.Count > 0)
        {
            var order = new[] { "Levy", "Infantry", "Archers", "Cavalry" };
            return ownCasualties.OrderBy(x => Array.IndexOf(order, x.Id[14..])).First();
        }
        var enemyCasualties = options.Where(x => x.Id.StartsWith("target:enemy:", StringComparison.OrdinalIgnoreCase)).ToList();
        if (enemyCasualties.Count > 0)
        {
            var order = new[] { "Cavalry", "Infantry", "Archers", "Levy" };
            return enemyCasualties.OrderBy(x => Array.IndexOf(order, x.Id[13..])).First();
        }
        if (Id("inspired:skip") is { } inspired)
        {
            // Inspired's +2 dice are most valuable in the final battle.
            if (combat.IsFinale && combat.PlayerLeadership >= 2)
            {
                var type = new[] { "Cavalry", "Infantry", "Archers" }
                    .FirstOrDefault(t => combat.PlayerUnits.TryGetValue(t, out var n) && n >= 2);
                if (type is not null && Id($"inspired:{type}") is { } useInspired) return useInspired;
            }
            return inspired;
        }
        if (Id("feigned:continue") is { } continueBattle)
        {
            var mine = combat.PlayerUnits.Values.Sum();
            var enemy = combat.EnemyUnits.Values.Sum();
            // A calculated diplomacy gamble should result in an actual early
            // withdrawal, not an accidental decision to fight to the death.
            return PlannedDisengagementApplies(combat) || enemy > mine || mine <= 2
                ? Id("feigned:use") ?? continueBattle : continueBattle;
        }
        if (Id("round:continue") is { } keepFighting)
        {
            var myTotal = combat.PlayerUnits.Values.Sum();
            var enemyTotal = combat.EnemyUnits.Values.Sum();
            var military = combat.PlayerUnits.Where(kv => kv.Key != "Levy").Sum(kv => kv.Value);
            // Only withdraw when the engine says it is legal. Feigned
            // Withdrawal has a separate, safer no-pursuit exit option.
            var run = Id("round:feigned") ?? Id("round:disengage");
            var plannedEscape = PlannedDisengagementApplies(combat);
            var hostileCrisis = HostileTribeCount >= 2 && !combat.IsFinale &&
                enemyTotal >= Math.Max(2, myTotal / 2);
            return run is not null &&
                (plannedEscape || hostileCrisis ||
                 (military == 0 && (myTotal == 1 || enemyTotal >= myTotal)) ||
                 myTotal * 2 <= enemyTotal) ? run : keepFighting;
        }
        return options[0];
    }
}

public sealed record SimulationDecision(string Calendar, string Action, string Phase, int Coin, int Leadership,
    IReadOnlyDictionary<string, int> Host);

public sealed record SimulationResult(
    string Status, string? Outcome, string? Reason, int Steps, long ElapsedMilliseconds,
    int Year, string Season, int Time, int Morale, int Leadership,
    IReadOnlyDictionary<string,int> Host, IReadOnlyDictionary<string,int> Resources,
    IReadOnlyDictionary<string,string> Rapport, IReadOnlyList<string> Advancements,
    int Battles, IReadOnlyList<SimulationDecision> Decisions, string? GameLogJson);
