War Chronicle Browser Update 0541 (v0.5.0.41)
=====================================================

Base: WarChronicleBrowser_Update_0538.zip (v0.5.0.38); fixes 0539 Pages-data packaging defect

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
