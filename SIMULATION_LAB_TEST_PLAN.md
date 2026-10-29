## 0573 targeted regression — Leadership, diplomacy, Research, and planned escape

1. In Spring/Summer Year I, force a Neutral tribe's unpaid-tribute **Passage 8+** prompt with 1 Leadership. The pilot must choose `gate:leadership`, regardless of whether it has any Military.
2. Force a Hostile passage after tribute with 1 Leadership. Again choose `gate:leadership` even when Leadership-poor.
3. Start with 1 existing Hostile tribe; approach a second, Neutral tribe without tribute. Verify pilot spends Leadership to avert a two-tribe crisis, even with a large Host.
4. Strong Year II/III army vs weak enemy with no Cavalry and no other Hostile tribe: legal normal passage is an allowed strategic choice. If that test fails and battle starts, pilot should choose the first legal disengagement rather than fighting to destruction; if test passes, clear the plan before subsequent events.
5. Repeat (4) with enemy Cavalry or Reign in Blood and verify pilot favors the passage reroll instead; if disengagement is illegal in combat, the pilot must not invent it.
6. Check printed Arrival `Test 7+` option whose fail clause is `Lose 1 Rapport` (e.g., Force Passage); contrast a printed `Test 8+` fail clause of only `Lose 1 Coin` and verify the latter isn't automatically classed as a diplomatic crisis.
7. In Autumn Year III, ensure Commission Study loses its early-game priority; if Drill offers a legal immediate Advancement/Armor purchase and sufficient Research is held, that purchase should remain worthwhile.
8. Run comparable 20-game batches with the same content sets and Finale advancements. Compare Year I unpaid-tribute Leadership spending, number reaching 2+ Hostile tribes, Year I defeats, disengagement timing, outcomes, and stalls against 0572. No rule-balance conclusions solely from differences in novice-vs-improved AI.

## 0572 targeted regression — Advancement buying and Research banking

Use the real browser Simulation Lab after compiling this source release. All purchasing decisions must pass through the game's original `CanBuyRefitAdvancement` legality check.

1. **Reproduce 0571 Game 6's bad posture:** In Summer Year III Refit & Reform, with 4 Research, no Coin, only 1 Archer + 1 Cavalry + 1 Infantry, and Armor available, the pilot **must buy affordable Advancements** rather than refuse all purchases because the Military count is below eight. Repeat until no legal Research purchase remains.
2. **Final-R&R timing:** With an expensive offered Advancement that cannot be bought, the AI still spends available RT on the best purchasable options (often Armor) instead of banking RT for a future R&R that will not happen.
3. **Earlier banking:** At Summer/Winter R&R in Years I-II, with only 1 Research, an appealing 2R Halberds offer and two Infantry, preserving the RT for a later R&R is reasonable. Verify it does not buy a 1R Armor merely because it is the affordable option; recruiting other units may proceed independently when legal.
4. **Prospective buying:** Armor should be considered with zero permanent Military; Longbows before Archers can also be worthwhile early, without hard prerequisites. At Summer III with two Infantry and sufficient RT, Halberds should compete strongly with Armor.
5. **Multiple Armors:** If the supply contains two or three Armor cards and available Research permits, purchasing an Armor must not suppress subsequent, distinct Armor copies solely because an Armor with the same name is already owned.
6. **No game-rule changes:** Unit recruitment rules, Advancement costs, order of offers, Painkiller casualty resolution, Save/Continue, and regular player UI should match 0571. Continue a 10-game sample with the same expansions and four enemy Advancements; compare purchases and Finale results to the 0570/0571 baseline.

## 0571 targeted regression test — Painkiller exhausts on failed save

1. Build/deploy 0571. In either a normal battle or Finale, obtain Painkiller doses and take an enemy hit with no Shields or Loose Formation available. Spend each dose against the same threatened casualty until the final one **fails** (1–4).
2. **Expected:** The threatened unit is lost exactly once, the pending enemy hit decreases by one, and combat proceeds to the next hit or next lane. The pilot must not report `No state change after 'Advance combat CasualtyChoice'`.
3. With a remaining Shield or legal Loose Formation option after the final dose fails, the legal options must still be offered; only apply the casualty automatically when **no defense choices remain**.
4. Also check successful final dose (5–6) prevents the casualty; failed dose with remaining doses offers another attempt; an ordinary `defense:accept` still applies exactly one casualty.
5. Run a fresh 10-game 0571 Simulation Lab batch with the same content sets and 4 Middelalderen Advancements. Compare to prior data, especially Painkiller-related stalls. These are manual/runtime validations; static source checks do not run gameplay.

# Simulation Lab (Build 0570) — AI strategy validation

This is **uncompiled source**. Deploy with GitHub Actions or compile both applications, then verify the browser shows `v0.5.0.70`.

1. Run 10–20 campaigns with the same expansion selections as 0568; save JSON reports for outcome, Winter Attrition, Research, purchased Advancements, and starting/final Finale strength.
2. For every player's non-Levy combat Leadership reroll, verify the original die was **not** a hit using its unit threshold and any applicable advancements; the pilot must select only the first displayed missed die. The normal-game UI remains unchanged.
3. For Levy rerolls, confirm the pilot only spends Leadership on a natural 1 when the **entire** Host is a single Levy. Multiple natural-1 rout dice are never individually selected by the AI.
4. For a legal casualty choice, confirm the pilot sacrifices an eligible friendly Levy before a Military Unit, and selects a high-threat enemy unit when the legal options are tied. This does not override the engine's casualty eligibility restrictions.
5. Verify *Commission Study* and *Drill the Host* are chosen over low-value Work when it is safe, and that Research translates into purchased Advancements during legal Refit & Reform (or through the legal Drill purchase option). Prioritize early Host survival when necessary.
6. Inspect Winter Leadership spending and verify the pilot always chooses the **lower** of two d10 results (not the first), then inspect Finale combat tactics, then compare outcome rates and cause-of-defeat patterns with 0568.
7. Ensure no normal-game engine, data, save format, or UI changes. The simulation still runs in an independent `GameSession`.

## New 0570 AI checks

8. At Refit, verify that a full enough Host avoids buying a seventh Archer merely to reach ten units, regardless of Longbows or Darken; prioritize affordable missing Cavalry/Infantry, while staying within normal costs and source limits.
9. When Year III Hire Mercenaries (Camp) or Hedge Knights (Explore) appears, verify that the maximum affordable number is recruited, even when the Coin balance reaches zero. In Years I/II, taking one Mercenary for immediate survival remains possible; avoid indiscriminate early hiring.
10. In the final actionable Autumn III Camp (usually Time 28–31), prefer Work's 1 Coin + 1 Leadership option, unless an immediate legal Levy/Mercenary gain is worth more. At Time 32–33, a 2-Time action cannot resolve before the Finale; the pilot must not treat Research/other unresolvable effects as real benefits.
11. Plague Pit multi-unit losses should choose a seventh Archer before losing effective attack dice, then eligible Levy before other Military. In Trade Caravan, verify that optional Food sale for 0 Coin is not selected; choose profitable alternatives or Pass.
12. When March ör Die occurs at the Finale's doorstep, or when Leadership is already zero, prefer Let Them Breathe rather than taking a shortfall for a travel discount that will no longer help.
13. Check outcome reports for **both** starting Finale armies, both Advancement sets, and substantive AI decisions (not just outcomes). Three 0569 campaigns all reached but lost the Finale; this is not yet a statistical win-rate estimate.
