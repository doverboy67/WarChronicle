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
