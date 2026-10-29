# War Chronicle Browser — Update 0573 (UNCOMPILED SOURCE)

- **Simulation Lab AI only — diplomacy:** Repair the unpaid-tribute 8+ passage-test blind spot. Both `gate:normal` / `gate:leadership` paths (hostile-but-paid AND Neutral-but-unpaid) now assess combat danger; with the fragile Year I Host, the pilot spends **even its final Leadership** when available to improve the test.
- **Diplomatic stability:** The AI evaluates *all four tribes' Rapport states*. Going from one Hostile tribe to two, or already having two or more Hostile tribes, sharply increases the preference for spending Leadership to prevent fighting or further hostility. No mechanical Rapport or First Contact changes.
- **Threat-informed late game:** With a healthy late Host, the pilot may conserve Leadership if it can reasonably endure a round and disengage. It reads the current Arrival's printed Year-specific enemy army, estimates incoming hits, counts Levy/Armor buffers, and treats enemy Cavalry pursuit or Reign in Blood as unsafe escape conditions.
- **Arrival event tests:** A generic `Test 7+` choice can conceal a fail effect of *Lose 1 Rapport*. The pilot matches the printed test threshold to its printed failure clause and applies the diplomatic safety policy when that loss can cause hostility. Ordinary non-diplomatic test handling stays distinct.
- **Combat continuity:** When the pilot knowingly risks passage without spending Leadership and battle follows, it remembers that it was planning an early disengagement and selects the first legal exit; Feigned Withdrawal is preferred when offered. It clears that intention if passage succeeded or the Arrival changes. In multi-Hostile crises it also disengages from protracted unfavorable ordinary combat sooner.
- **Research:** In Autumn Year III, after the final routine R&R, *Commission Study* is devalued because Research normally cannot be spent before the Finale. *Drill the Host* retains higher priority with enough Research to make a legal discounted Advancement purchase during Camp; late-Camp immediate military readiness is still prioritized.
- **Scope:** The two SimulationPilot implementations, release/cache metadata, static policy checks, and documentation only. **No** game engine, game-data, rules, existing saves, ordinary UI, or card-effect modifications.
- **Validation:** Uncompiled source preview. Static regression and exact diff checks are included; compilation and real-browser Simulation Lab smoke tests remain necessary.

# War Chronicle Browser — Update 0572 (UNCOMPILED SOURCE)

- **Simulation Lab / Refit & Reform only:** Removed the erroneous minimum-permanent-Military prerequisite to purchasing Advancements. 0571 could carry four Research through Summer Year III because its Host was below eight units and even though Armor was affordable. Recruitment still builds a defensive core when Coin permits, but never blocks legal Research investment when Coin is exhausted.
- **Long-term acquisitions:** Armor can be purchased before having any Military Units; Halberds, Stirrups, Longbows, and other Advancements are evaluated for the units the Host already has **and** potential later recruits. Unit counts affect desirability, not purchase eligibility. Armor duplicates are independently purchasable by their distinct Advancement IDs, subject to the original supply and engine costs.
- **Saving vs. spending:** In Years I–II, if an attractive currently displayed Advancement is close to affordable, the pilot may bank Research rather than settling for Armor. During Summer Year III R&R, this saving rule turns off: buy the best legally affordable options repeatedly until available Research cannot be spent on an eligible purchase. Printed Coin and Research costs still apply. A high-value matching development (e.g., Halberds with two Infantry) can take precedence over Armor.
- **Scope:** Both `SimulationPilot.cs` files, release version/cache markers, documentation, and static policy regression checks. Preserves 0571's Painkiller exhaustion fix and all previous engine/rules/card/save behavior. No player-persona or risk-tolerance changes.
- **Validation:** Static source/release gate only; compile both .NET 8 targets and test the Simulation Lab in the browser. To reproduce the bug's conditions, use a depleted Summer III Host with several Research, no Coin, and Armor available; expect Advancement purchases rather than banking all RT.

# War Chronicle Browser — Update 0571 (UNCOMPILED SOURCE)

- **Combat bug fix (both Web and Pages):** When a Painkiller dose fails to prevent a casualty and the final dose is exhausted, `ResolveCombatDefenseChoice` checks for any remaining Shield or Loose Formation option. If none is available, it applies the *same threatened casualty*, consumes exactly one pending enemy hit, and continues the ordinary combat lane. This fixes the no-choice `CasualtyChoice` stall observed twice in 0570 Set 3 (Dr. Pepper in Autumn I and Finale in Autumn III).
- **Rules unchanged:** Each dose still grants one 1d6 attempt, saving on 5–6; retrying the same threatened casualty is allowed while doses remain. No additional casualties, free saves, or automatic redirection are introduced. Existing defense choices remain available when legal.
- **Scope:** Only two identical `GameSession.Combat.cs` implementations, release version/cache references, regression checks in the static release gate, and release documents. The Simulation Lab AI, event/card catalog, other gameplay logic, and save serialization are unchanged.
- **Testing:** Static regression contracts cover the final-failed-dose fallback, preservation of remaining defense choices, and both project copies. Actual .NET compilation and gameplay simulation must be run in Visual Studio/GitHub Actions; they could not be run in the source-generation environment.

# War Chronicle Browser — Update 0570 (UNCOMPILED SOURCE)

- **Simulation Lab AI only:** Refit recruitment considers combat-die diversification, sharply reduces unit-type stacking even when Longbows/Darken is owned, and will not purchase a seventh unit of a type merely to fill an optional Host-count target. It still respects real affordability and game recruitment rules.
- **Simulation Lab AI only:** Year III Hire Mercenaries and Hedge Knights buy the maximum affordable cap-exempt forces, even when that spends the last Coin. Early years reserve Coin when appropriate: Hire Mercenaries may buy one emergency body and prefer discarding the card so it can recur; early Hedge Knights are situational because they disband at Winter.
- **Simulation Lab AI only:** Autumn Year III Camp choices favor immediate fighting readiness: late Work for 1 Coin + 1 Leadership, available additional Levy, and Year III Mercenaries. If a Camp card's Time would cross the finale threshold before resolving the effect, avoid choosing it for a benefit it cannot deliver.
- **Simulation Lab AI only:** Plague Pit and similar noncombat unit-loss selectors weigh marginal combat dice, preferring excess seventh units and then Levy instead of losing multiple high-value Military dice. Optional Trade Caravan sales avoid zero-proceeds Food; the AI can pass instead. March ör Die no longer spends missing Leadership on Forced March when it has no value.
- **Scope:** Two identical SimulationPilot.cs files, release metadata/cache markers, and documentation. No changes to engine rules, game card data, ordinary combat UI, save/load, or campaign mechanics.
- **Build caveat:** Source-only, uncompiled release. Run static release checks, then compile via GitHub Actions or Visual Studio and verify actual Simulation Lab campaigns.

# War Chronicle Browser — Update 0569 (UNCOMPILED SOURCE)

- **Simulation Lab AI only:** Combat Leadership rerolls consider only `PlayerRoll` dice that the actual engine marked as misses (including current Advancement modifiers); choose the first miss, never a hit. Retain the strict last-one-Levy natural-1 rout exception, without selecting between identical dice.
- **Simulation Lab AI only:** When selecting the Host's own legal casualty, prioritize Levy over Military; when selecting an enemy casualty, favor dangerous Cavalry/Infantry in ties. The engine's legal casualty list and all underlying casualty rules remain unchanged.
- **Simulation Lab AI only:** Research acquisition is prioritized using legal Camp cards *Commission Study* and *Drill the Host* whenever not overridden by immediate danger/need for Coin, Levies, Mercenaries, or recovery. Drill can purchase an immediately affordable Advancement rather than always banking Research.
- **Simulation Lab AI only:** Winter Attrition weighs low Morale and the value of a mature Host when choosing the legal Leadership reroll, and correctly selects the lower of the two results (the prior pilot simply selected the first); Finale uses last available Leadership on missed military attacks and can choose Inspired when beneficial. Retreat logic now recognizes legal Feigned Withdrawal at round end.
- **No game engine/UI/rules/cards/saves changes.** The classic combat reroll UI still displays individual dice; only the simulator's choice logic is simplified. No current campaign state is modified by Simulation Lab.
- **Build caveat:** This environment does not have the .NET SDK. Source-only, uncompiled release. Compile via GitHub Actions/Visual Studio and smoke-test in browser before relying on it.

# War Chronicle Browser — Update 0568 (UNCOMPILED SOURCE)

- **Simulation Lab only:** During Refit & Reform, purchase permanent Archers, Infantry, and Cavalry **before Advancements** whenever the army is understrength; the first priority is a defensive core (Year I 4 Military Units; Year II 6; Year III 8).
- **Simulation Lab only:** Once the defensive core is established, acquire useful Advancements and recruit more permanent troops toward Year-specific targets (6/8/10, within existing Host caps and available recruits). Retain a small reserve of Coin in Years I–II when it won't compromise that core.
- **Simulation Lab only:** Levy *combat* policy now follows the designer's rule: reroll a natural 1 only when the last surviving unit is one Levy; otherwise preserve Leadership and retreat at the first legal round end if that Levy is alone.
- **Simulation Lab only:** Levy recruitment supplements the military army, never substitutes for it. More descriptive recruitment decisions appear in exported simulation logs.
- **Rules unchanged:** Permanent unit recruitment remains legal *only* in actual Summer/Winter Refit & Reform; no recruitment is added to ordinary Camp/Explore phases. Earlier survival depends on legal event and Camp-card opportunities.
- Both game engines, combat code, card data, and saving logic remain byte-identical to 0567. The static release validator also now accepts unchanged files within its cumulative allowlist, avoiding a false failure on unchanged CSS/save helpers. **Uncompiled source preview**; requires a GitHub Actions build and browser smoke test.

# War Chronicle Browser — Update 0567 (UNCOMPILED SOURCE)

- **Simulation Lab only:** Observed player now prioritizes available Hire Mercenaries when the Host is dangerously weak. Also preferentially recruits the maximum number permitted by that card instead of the first Select option.
- **Simulation Lab only:** The hostile passage-test decision detects the actual "hostility remains" 8+ prompt and may spend Leadership to prevent an otherwise catastrophic combat.
- **Simulation Lab only:** Avoids low-value Levy-die rerolls that depleted Leadership in the 0566 test, and disengages at the first safe opportunity when facing overwhelming force with no Military Units.
- **Simulation Lab only:** Protects the last Wood from Friendly Tribute when Coin is abundant; Wooden tribute may avoid a much costlier Coin fallback later.
- **Simulation Lab only:** Prioritizes Raise Local Levies when the Host is badly depleted and Food is available.
- **All other rules and source data remain identical to 0566**; no changes to gameplay engine, cards, save format, or normal interface except version number. Simulation heuristics remain experimental and may play poorly.
- Uncompiled preview: must build through GitHub Actions and smoke-test in-browser.

# War Chronicle Browser — Update 0566 (UNCOMPILED SOURCE)

- **Simulation Lab: Fixed false stall when opening a pending battle.** The action updates `CombatVisible` but does not append a Chronicle entry; the pilot fingerprint now tracks battle visibility and pending-combat status.
- **Simulation Lab: Tracked more legitimate transitions.** Combat tactic commitments, combat choice prompts/options, Finale tactic and Steady Advance selections, and FlowPrompt text/options now contribute to the progress guard to prevent similar false positives.
- **Simulation Lab: Friendly content labels.** Internal `PackC` is displayed as **Riff Raff**; `SitA` and `SiaSL` also show their proper names. Content selection was correct in 0565; this was a labeling issue.
- Same isolated simulated `GameSession`; normal campaign, game data, rule engine, and save serializer unchanged.
- No compile/test via .NET in this environment; verify through GitHub Actions and repeat the smoke test in the browser.

# War Chronicle Browser — Update 0565 (UNCOMPILED PREVIEW)

- Added Debug → Simulation Lab entry behind the existing five-click version number unlock.
- The pilot drives a separate fresh GameSession through the same legal game-engine actions; never writes to the current player save or log archive.
- Initial single-campaign, observed-strategy pilot based on prior user logs: unit/Advancement synergy, varied recruitment, Camp Work priorities.
- Explicit stalled, stopped, or completed status; decision guard and hard limit.
- Debug report download includes the actual GameSession Chronicle and pilot decisions; chunked JS interop for Blazor Server.
- No gameplay rules changed.

**Testing caveat:** This environment does not include a .NET 8 compiler; build has not been compiled or run. Do not deploy without compiling and smoke-testing.
# War Chronicle Browser — Change history

## 0564 — Save/Continue repair
- Fix the Blazor Server connection failure when loading saves larger than the default 32-KB incoming message limit. Saved-game reads now use UTF-16-safe chunks of at most ~8 KB; the saved JSON format and key are unchanged.
- If a save check fails, show a retry prompt without silently offering New Chronicle.
- Keep changes isolated to startup/read logic, JavaScript asset cache markers, version metadata and release validation. No card/game-rule changes.
- Consolidate prior per-build root documents into README, CHANGELOG and DEVELOPMENT_STATUS. Earlier release ZIPs retain the historical paperwork.

## 0563 — Event choice information and card data
- Add stored outcomes for all 231 inventoried event-flow choice branches; preview Explore effects before Clear Road decisions.
- Restore six approved Camp-card definitions from working reference; preserve 0551 for parity checks.

## 0562 — Targeted eligibility fixes
- Drill the Host: correct cost to 1 Leadership; gate discounted purchases by actual affordability.
- Herd of Wild Steeds: disable optional conversion when no Infantry, matching doubles or Cavalry stock.

## 0561 and earlier
- Previous individual build notes and audits are preserved in the historical ZIP archives rather than repeated in this release.
