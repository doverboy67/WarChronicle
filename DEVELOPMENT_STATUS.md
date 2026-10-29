## 0573 — Diplomatic Leadership and strategic disengagement

**Implemented (pilot only):** Explicit Leadership use on unpaid-tribute passage tests, last-point spending for fragile Year I armies, nonlinear diplomatic threat assessment across the four tribes, Year-specific enemy composition and pursuit-aware later-game decisions, and a first-safe-round disengagement plan when deliberately gambling on passage. Generic Arrival event tests whose printed Fail clause loses Rapport are also handled. Autumn III standalone Research acquisition is devalued, but Drill remains an on-the-spot purchase option.

**Test first in browser:** (1) Year I unpaid Neutral tribute with exactly 1 Leadership must choose `gate:leadership`; (2) paid Hostile passage with 1 Leadership likewise; (3) strong Year III Host, no other hostile tribe, weak no-Cavalry opponent can conserve Leadership and disengage at the first legal opportunity if the roll fails; (4) one other Hostile tribe reverses that conservation; (5) printed Arrival choose-one 7+ fail = Lose Rapport uses the new logic; (6) non-hostility 7+/8+ rolls retain normal value logic; (7) Autumn III Commission Study is no longer an AI default. Check First Contact 2–4 and all existing combat and card rules are unchanged. **.NET compiler unavailable here.**

## 0572 — Final R&R Research and future-oriented Advancement evaluation

**Implemented (AI only):** No mandatory army-size threshold for acquiring Advancements. The pilot can buy Armor with zero Military Units, invest in future-use unit-specific cards, and save Research in Years I–II when the visible tactic/development is promising but not yet affordable. The final Summer Year III R&R eliminates deliberate Research banking and uses available legal purchases, including repeated distinct Armor cards. The underlying purchase costs, offered-card ordering, military restrictions, and gameplay engine are unchanged.

**Testing focus:** 0571 Batch 04 Game 6 finished with 6 RT after starting Summer III R&R with 4 RT, 0 Coin, and 3 Military Units. 0572 should now convert Research into Armor/other available Advancements. Run 10-game batches with the same content sets and four enemy Advancements; check last R&R Research before/after, army size, acquired Advancements, campaign outcomes, and any Painkiller stalls. .NET compilation and actual campaign tests are pending.

## 0571 — Painkiller exhaustion combat stall (targeted bug fix)

Confirmed in two 0570 Set 3 stalled reports: when the last Painkiller dose failed to prevent a hit and neither Shield nor Loose Formation was available, the defender remained on `CasualtyChoice` without choices and with the hit unresolved. Both combat engine copies now apply the threatened casualty and continue the ordinary hit-processing sequence, while preserving legal further defenses when they exist. No change to card text, per-dose odds, or pilot AI policies. Needs compiled browser smoke test: replay until Painkiller is exhausted during a battle (both a regular engagement and Finale), and confirm no stalemate. Uncompiled source; static validation cannot replace runtime testing.

## 0570 — Simulator decision quality after three 0569 campaigns

Only the Simulation Lab pilot changes. Priority rules: Year III cap-exempt hiring, diversified permanent army within six effective dice/type, optional zero-Coin trade avoidance, noncombat casualty valuation, Force March expiration, and late-Autumn action timing. Earlier Mercenaries remain a contingent survival purchase (often just one); costly attrition/research/Leadership choices still need further empirical testing. No engine, UI or game card changes. 0570 is uncompiled and must be built and tested in the browser.

## 0569 — Simulator AI-only tactical / Research revision

Update only SimulationPilot decision heuristics in both builds. Focus on missed-die Leadership rerolls, legal casualty targeting, scarce Research opportunity selection, tactical Finale spending and Winter attrition judgments. The engine and normal gameplay UI are unchanged. The game UI's unnecessary individual-hit reroll selections remain a separate, **deferred** improvement. The source-only release needs compilation and browser testing.

**Combat policy refinement (designer-approved):** Reroll a natural-1 Levy die only when the sole remaining Host unit is one Levy, to try to prevent rout. Otherwise never spend Leadership rerolling Levy dice, and withdraw a lone Levy at the first legal round end. This is SimulationPilot-only; the combat engine is unchanged.

## 0568 — Simulation Lab: prioritizing permanent military recruitment

The 0566 full-engine campaign ended in Year I Summer, *before the first Refit & Reform*. Therefore it could not recruit regular soldiers despite having Coin; only special Camp/event recruitment was legal. The pilot's existing R&R purchase priority was also advancement-first, potentially leaving it unable to purchase enough troops. In 0568 the automated player recruits a core of permanent Military Units before prioritizing advancements, then builds toward a mixed army at each legal R&R. No changes to game rules or normal gameplay.

# Development status — Build 0568

**Current focus:** Simulation Lab compatibility following the first real-browser 0565 test. The rules, card catalog, and save mechanics remain identical to 0564. Earlier 0563 gameplay and choice-presentation corrections are retained.

**Verified statically:** first simulation stall was caused by an omitted `CombatVisible` fingerprint field, now included. The simulation summary maps `PackC` to Riff Raff. Both pilots are byte-identical, with no game-engine, game-data, or save-serialization changes. Existing save chunking checks remain green.

**Not yet verified for 0568:** .NET compilation, a full real-engine simulation in the browser, and the completed report download. A successful static release gate cannot guarantee that all runtime prompts are handled.

**Still outstanding:** voluntary Painkiller use in noncombat losses and Levy rout handling; runtime smoke tests of all special prompts and all 231 choice screens before declaring the game suitable for balance testing.

**Recovery:** do not press Begin Chronicle or clear localStorage if a saved campaign is missing. Keep the independent save JSON backup and run on the same `https://localhost:7148` origin. Select Continue Saved Game when offered.


## 0565 — Debug Simulation Lab preview
The simulation is isolated and guarded, but not compiled/tested here. Compile with dotnet 8; test complete campaign plus forced combat, Hostile Echo, Winter, Finale, stop, and JSON export before deployment. Initial strategy intentionally uses heuristics rather than actual model inference.


## 0567 — Simulation Lab strategy update

The first real-engine run of 0566 completed 56 decisions, two battles and an early defeat in 212 ms. The pilot declined available Mercenaries while down to 1 Levy and kept Leadership for low-odds rerolls while rolling ordinary hostile Passage tests. These strategy-only rules are corrected in 0567; see CHANGELOG.

## 0566 — Simulation Lab follow-up
Fixed false positive no-progress check after opening combat, added detection of other valid combat/Finale choice mutations, and made PackC display as Riff Raff. Engine and game data unchanged. Pending .NET compilation and a full live simulation; first prior 0565 run stalled Summer Y1 after 31 decisions at Begin Combat because CombatVisible was not included in the fingerprint.
