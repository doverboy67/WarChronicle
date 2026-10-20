War Chronicle Browser Update 0545 (v0.5.0.45)
===================================================
Starting source: verified WarChronicleBrowser_Update_0544.zip.

1. End-of-Season / Winter buttons:
   - Begin End of Spring/Summer/Autumn, Resolve Vulnerability,
     Gather the Harvest, Consolidate the Route and Begin Winter
     now produce permanent, disabled-with-checkmark Chronicle choices.
   - Harvest preparation/packing and Summer/Winter Refit & Reform
     remain the existing interactive workspaces. Their evolving
     controls and Undo operations are not snapshotted as decisions.
   - Winter Attrition selections were already retained.

2. Explore pacing:
   - Automatically resolved Explore events pause at a persistent
     'Proceed to Arrival' gate for the player to read the event.
   - Explore events with real player decisions proceed directly
     to Arrival when their effects finish; no redundant extra gate.
   - Existing ease-out scrolling is unchanged, including its speed.

3. Chronicle transactions:
   - Muted rust left border for expenditure/loss, teal for receipt,
     split marker for straightforward mixed income/cost sentences.
   - Bold numerical/resource quantities and the already bundled
     actual token / player-unit artwork in the Chronicle.
   - Adds semantic handling for 'The Host pays/offers/surrenders' and
     'N Resource changes hands' tribute wording.
   - Resource labels remain in actual copyable/selectable text;
     Leadership and Levy reuse the game's existing CSS token symbols.
     Morale remains text-only because no Morale icon asset is bundled.

4. Finale difficulty:
   - Easy=3, Standard=4 (default), Hard=5 randomly drawn
     Middelalderen Advancements. Select on New Chronicle setup.
   - Keep Middelalderen's unit base and existing Advancement
     weighting, unique unit-bonus handling, and all other rules.
   - Difficulty is persisted in save files and noted in JSON game logs.
   - The previous rulebook PDF is bundled unchanged; its printed
     Finale text must be updated separately by the designer.

5. Both hosted WarChronicle.Web and GitHub Pages WarChronicle.Pages
   updated. Pages retains no @rendermode InteractiveServer directive.
   Asset cache versions bumped to 0545; playlist retained; latest
   120 Chronicle entries policy, selected-option checkmarks and
   scrolling preserved. Login/password/IP metadata/email remain
   deliberately deferred.

Verification: static checks performed across both projects. dotnet SDK
not present in the build environment, so compiling and browser testing
must be performed on the user's machine before production deployment.


War Chronicle Browser Update 0544 (v0.5.0.44)
=====================================================

0543 Chronicle phase-button correction + gentler scroll
-------------------------------------------------------
- Explore and Proceed to Arrival no longer live in disappearing wizard panels. Each now appears as a full, shaded Chronicle decision card, with a clear selected state after it is clicked.
- Also retain the Begin Camp gate to avoid the same disappearance at the next phase transition.
- Completed phase buttons are disabled; their full original descriptions and unselected options stay visible in chronological context. The Chronicle state is serializable without changing save schema v1.
- Existing 0543 scrolling remains ease-out cubic with natural bottom clamping and no artificial spacer, but is about 15% slower (210-470ms, distance-adjusted).
- 120 visible entries and Show Earlier History remain unchanged. Playlist link unchanged.
- Implemented in both server-hosted Web and standalone GitHub Pages projects. Pages must NOT use InteractiveServer.
- Source-level checks only. The .NET SDK is not installed, so compilation and browser behavior need validation in the user's environment.
- Other non-Chronicle interactive panels (e.g. End of Season, Winter/Refit, Combat) have not been converted by this focused update.

War Chronicle Browser Update 0543 (v0.5.0.43)
=====================================================

Chronicle usability and soundtrack
---------------------------------
- New Soundtrack button beside Rulebook opens the user-provided YouTube playlist in a new tab.
- Chronicle renders only the most recent 120 entries by default. Older history stays in the saved log; Show Earlier History still works.
- Existing Chronicle option panels remain full-size after selection, with selected button highlighted and all buttons inactive.
- Camp-card offering is now snapshotted in the Chronicle, with its original titles, flavor, effects, eligibility and role notes. Its full panel remains visible after choosing a card, instead of being removed from the live wizard.
- Explore, Arrival, and Camp headings receive subtle separators, while Scene and Echo styling stays intact.
- New Chronicle content scrolls within the Chronicle panel using an interruptible ease-out cubic animation (up to 400 ms, shorter for short distances). Scroll targets the first new entry, never the last; it respects reduced-motion preference and the browser's natural bottom limit, with no artificial padding.
- JS tracks a visible entry before action clicks and restores its position after offscreen history is pruned, then animates forward.
- Static GitHub Pages and server-hosted Web receive the same changes; Pages remains WebAssembly and Web retains InteractiveServer.
- Saves remain format v1, and existing v0542 saves can be read by v0543 subject to prior existing version-warning UI.
- Source-level verification only: .NET SDK is not installed in this container, so the C# changes cannot be compiled or the app exercised end to end. Please compile and browser-test before releasing externally.

War Chronicle Browser Update 0542 (v0.5.0.42)
=====================================================

Base: WarChronicleBrowser_Update_0538.zip (v0.5.0.38); fixes 0539 Pages-data packaging defect

0542 Stand Up and Shout unresolvable Scene correction
----------------------------------------------------
- When Stand Up and Shout (SCENE-029) is drawn before any tribe has been met, it is unresolvable. No Rapport target exists; the Scene must not show an Answer/Turn Away choice, adjust Rapport, seed an Echo, or charge its printed 1 Time.
- The browser now randomly moves one card from the unseeded Scene pool to Camp discard, shuffles Stand Up and Shout back into the unseeded Scene pool, and continues resolving the remaining drawn Scenes and the normal Camp choice. It adds a Chronicle explanation and mechanical note.
- Audited all current Scene flows: Stand Up and Shout alone starts with a mandatory, unguarded Select target. A Wager of Flesh has the voluntary Refuse option if the Host has no military units; its unit selection remains conditional.
- This is a source update to both server-hosted Web and static GitHub Pages projects. Published save-game schema remains 1. Login and automated email reporting remain outstanding.
- Source has been statically checked but NOT compiled or exercised in .NET (SDK is unavailable here). Build and browser-test before external playtesting.

0541 GitHub Pages startup correction
-----------------------------------
- Removed an erroneous `@rendermode InteractiveServer` from `WarChronicle.Pages/Components/Pages/Home.razor` introduced in update 0539. The standalone GitHub Pages application runs via Blazor WebAssembly and must not request server interactivity; the server-hosted WarChronicle.Web version retains its directive.
- Added a GitHub Actions guard to reject the server-only render mode in the Pages app before publish.
- Bumped displayed version to v0.5.0.41 and cache-query markers to 0541.
- This fixes a concrete source regression compared with working v0.5.0.38, but cannot be live-tested here without a .NET SDK and browser deployment.

0540 correction
---------------
- Corrected the static Pages runtime data: Pages/wwwroot/Data/wc_data.json now matches the updated Data/wc_data.json in both applications. The previously released 0539 ZIP served stale Field Staff and Work data in Pages.
- Advanced visible app version and cache query parameters to 0540. No additional gameplay rules changed from 0539.
- Login, shared-password authentication, player identity, IP metadata, and automated email reporting are NOT implemented in this source-only release.

Changes carried forward from 0539
-------------------------------
- Field Staff (Controlled Settlement, 2 Time): Train Scouts now costs 1 Coin instead of 1 Research; Appoint Camp Steward now costs 1 Coin instead of 2 Coin. Updated the runtime card text, event flow and validation.
- Work / Camp Steward: resolve ONE normal Work option first, then optionally choose ONE extra Steward benefit: spend 1 Coin for a Resource matching adjacent Terrain, or buy one Resource at Market Buy value -1 Coin (minimum 1). Coin earned through Work can pay the Steward cost. Baggage capacity and affordability enforced, with disabled choices explaining why.
- Combat: unit-specific bonus dice (including Surge) cannot generate dice without a unit of the affected type, on either side, in ordinary or Finale Combat. Prevent committing unavailable unit-specific Tactics (Charge, Gritty, Darken the Skies, Surge, Feigned Withdrawal, Covering Fire, Steady Advance).
- Feigned Withdrawal is disabled in combats where Disengagement is prohibited, including the Finale and Reign in Blood. Unavailable reasons appear on the Tactic tiles; Leadership is not spent on an unavailable commitment.
- Reconciled Hostile Echoes: add an in-world explanation of the lingering grievance and the lost Camp phase, plus an explicit mechanical Chronicle note (no Combat; return the Echo to unseeded pile). Applies to all four tribes.
- Chronicle: scroll to the first new entry from an action, not the last; do not auto-scroll when no new entries were added. Old saved history is not treated as new.
- Civic/Field distinctions are removed from displayed Camp card iconography and generic Camp choice instructions. Legacy Keyword fields remain in the JSON data for backward compatibility. The title Field Staff remains unchanged.
- Embedded the newly supplied War Chronicle rulebook v0.5.8.6 in both Web and Pages projects.
- Updated web assets, cache query values and displayed app version to 0539. Save schema remains at 1.

Already present in the 0538 baseline (checked, not rewritten)
------------------------------------------------------------
- Priest converts an enemy unit out of combat; converted units don't count toward surviving force when checking defeat. Converted units join only if player survives combat.
- Combat automatically checks for Host elimination when no Archers, Infantry, Cavalry or Levy remain, with Priest excluded. Defeat works in Finale and normal battles.

Verification completed
----------------------
- Validated JSON runtime data and all 151 EventFlow graphs' next-node references.
- Verified the Work normal-action -> Steward option links and Field Staff Coin costs.
- Verified matching Web / Pages source and bundled rulebook files.
- Verified JS syntax with node --check and PDF with pdfinfo (15 pages).

IMPORTANT: .NET SDK isn't installed in this execution environment, and network installation was not available. This is a modified, source-complete package, matching the format of update 0538. It has NOT been compiled or run through a .NET runtime test here. Run dotnet build WarChronicleBrowser.sln and a browser smoke test before publishing to testers.

Deploy/continue in the same way as prior update packages.
