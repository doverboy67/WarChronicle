War Chronicle Browser Update 0522

Update 0522 (v0.5.0.22):
- Fixed the GitHub Pages data package. WarChronicle.Pages/wwwroot/Data/wc_data.json had remained on the older Camp-card data even though the source Pages/Data/wc_data.json and local Web data were updated.
- Synced the GitHub Pages static data to the authoritative current data, so the hosted build now receives the revised Camp cards including Supply Convoy, Raise Local Levies, Respite, Market Day, Drill the Host, Send Terms Ahead, and Host a Common Table.
- Added a cache-busting query to the Pages data request (Data/wc_data.json?v=0522) so browsers do not keep using the stale JSON after deployment.
- No new game-design changes in this patch; this corrects what the hosted Pages build actually serves.

Update 0519 (v0.5.0.19):
- Re-audited the authoritative camp-cards tab in War Chronicle Master(8).xlsx and refreshed the browser Camp card data from that primary source. The revised Camp HTML for the changed cards passes a strict tag-balance check.
- Work now formally includes Baggage repair as a normal Work choice: advance 2 Time, spend 1 Wood, and repair 1 damaged Baggage space. No normal Work reward is gained when repairing.
- Market Day keeps its Market action and now adds Rest: gain 1 Morale. The card still costs 1 Time and retains its Barbarian Settlement / Market-building location rules.
- Hire Mercenaries may now be used at any Settlement. Its recruitment rules and 1 Coin per Mercenary cost are unchanged.
- Hire Wagoners is replaced by Supply Convoy at a Controlled Settlement. Provision spends 2 Coin for 1 Food, Wood, or Stone, with an optional additional 1 Coin for a second Food, Wood, or Stone; the two Resources may match. Rest gains 1 Morale.
- Respite no longer costs Food. It spends 1 Leadership, then gains 2 Leadership and 1 Morale.
- Raise Local Levies no longer costs Leadership. The Raise Levies option spends 1 Food to add 2 Levy; Rest instead gains 1 Morale without paying the Food cost.
- Drill the Host still requires 1 Leadership to begin, but gains 1 Leadership after resolving the chosen Drill effect, making the activation Leadership a soft gate rather than a net cost.
- Send Terms Ahead no longer requires Tribute. Choose a Hostile tribe and test 6+; ignore the Hostile Rapport DRM and Leadership may not be spent on the test. Success gains 1 Rapport with that tribe and 1 Morale; failure has no effect.
- Host a Common Table now tests 6+ with the selected tribe's Rapport DRM, and Leadership may not be spent on the test. Failure has no effect. On success gain 2 Coin and 1 Leadership; a Neutral tribe also gains 1 Rapport, while a Friendly tribe grants 1 additional Leadership instead.
- Fixed Remove or Discard after play. Sacred Foundations, Frontier Works, Hire Mercenaries, Send Terms Ahead, Field Staff, and Host a Common Table now explicitly ask the player to Discard or Remove from game after resolution. The browser no longer auto-decides based on success or failure.
- Expanded Finale victory narration. The canonical victory result appears first, followed by a browser-only epilogue that responds to surviving Host strength, Morale, tribal relationships, and remaining stores before closing with “The Host has survived.”
- Preserved the global Shortfall engine, universal Combat-victory Leadership, Forage 4-6, Baggage rules, narrative layering, GitHub Pages hosting, logging, and all prior 0518 behavior not superseded above.
- WarChronicle.Web and WarChronicle.Pages remain synchronized.

War Chronicle Browser Update 0518

Update 0518 (v0.5.0.18):
- Fixed the Global Shortfall Rule at the engine level. Required losses/payments of Food, Wood, Stone, Coin, Leadership, Research, and Rapport now apply 1 Morale loss for each missing unit by default unless a flow explicitly says IfAble / NoShortfall.
- A Joke Grows Teeth now resolves correctly when the Host has 0 Leadership: Lose 0 Leadership, then lose 1 Morale from the missing Leadership.
- The fix is global rather than card-specific, so bare EventFlow Lose/Spend actions no longer need an ApplyGlobalShortfall flag for the core shortfall currencies/resources. Existing explicit ApplyGlobalShortfall flags remain valid, and explicit NoShortfall exceptions still override the default.
- Rapport losses now also honor Global Shortfall at the Hostile floor, including multi-tribe Rapport losses. Unit losses remain outside the default Shortfall rule unless a specific flow explicitly requests it.
- Preserved all Update 0517 UI/narrative work, including the Proceed to Arrival click, card-style Advancement offers, tabletop-inspired Rapport display, contextual browser narration, Arrival tribe suffixes, Cursed Battlefield Mercenary eligibility, No Remörse clarification, and the Starting Clearing status fix.
- WarChronicle.Web and WarChronicle.Pages remain synchronized.

Update 0517 (v0.5.0.17):
- Added an explicit Proceed to Arrival gate after every Explore step. Explore results no longer auto-scroll directly into Arrival; matched Explore cards, Explore-combat returns, and uneventful/mismatched Explore draws all pause before the Clearing is revealed.
- Uneventful travel now receives a short contextual Chronicle entry instead of silently advancing. Browser-only travel/arrival/first-contact/tribute narration now draws from small phrase pools so repeat plays are less repetitive.
- Began the richer web-only narrative layer while preserving printed card text as authoritative. A Night at the Alehouse now keeps its printed outcome text, then adds lighter browser-only flavor after the mechanical result. Arrival test flavor is likewise presented as supplemental narration rather than a replacement for the result.
- Added contextual Arrival narration, including card/outcome-specific flavor for Buy the Jars, with generic tribe-aware fallbacks for other Arrival tests.
- Arrival card headings now append a subtle tribe-name suffix without changing the underlying card title.
- Redesigned the State-panel Advancement offer as white card-like panels with title, cost, and effect text visible without hovering.
- Redesigned Rapport as a compact tabletop-inspired four-row track with Hostile / Neutral / Friendly / Allied spaces and visible -1 / +1 / +2 DRMs.
- Fixed the stale “The Host is ready to leave the Starting Clearing” message so it only appears before the first march. Later Explore steps use a continuing-march message.
- Fixed Cursed Battlefield so Infantry-class Mercenaries are eligible to be assigned. If both regular Infantry and Mercenaries are present, the player chooses which Infantry-class unit is assigned before rolling.
- Clarified No Remörse in the live test prompt: Pass removes Reign in Blood and the Echo and restores 1 Morale; Fail discards the Echo while Reign in Blood remains. If Reign in Blood is already gone, the Echo is simply removed.
- Preserved all Update 0516 card-data corrections, Baggage repair, universal Combat-victory Leadership, Forage 4-6, GitHub Pages noindex/nofollow, logging, and local/Pages dual-host structure.
- WarChronicle.Web and WarChronicle.Pages remain synchronized for the gameplay/UI changes in this build.

Update 0516 (v0.5.0.16):
- Refreshed Camp, Explore, and Arrival card data from the revised primary workbook tabs in War Chronicle Master(5).xlsx. CardData and EventFlow were treated as derived/stale sources and reconciled where primary-card mechanics changed.
- Commission Study now costs 1 Coin.
- Drill the Host now costs 1 Leadership with no Coin premium.
- Work preserves the established Baggage repair option and now also shows the Camp Steward choices from the primary Camp card.
- Preserved Raise Local Levies at 1 Leadership + 1 Food because that cost is explicit in the primary card Effect text even though its legacy Cost cell still lists only 1 Food.
- Rat Salad now works as written: lose 1 Food unless Camp Steward succeeds on the 7+ avoidance test; without Camp Steward, the Food loss is automatic.
- Roaming Nomads now buys 1 Food for current Market Buy value -1 Coin.
- The Timbermen's Guild now buys 1 Wood for current Market Buy value -1 Coin rather than a fixed 2 Coin.
- Caravan Remains now grants 1 Leadership when the Host chooses not to loot.
- Arrival card text was refreshed from all 20 revised primary Arrival cards. The Last Bell's Take the Relic success and The Candle Toll's Pocket the Candle Silver success now grant the added Leadership shown in the workbook.
- Arrival Attack summaries reflect the universal +1 Leadership Combat-victory rule while the engine still awards that Leadership only once, separately from card-specific rewards.
- Added generic discounted-Market-buy support to EventFlow so card effects can key off the live Market Buy value instead of a fixed Coin amount.
- All Update 0515 rules remain, including +1 Leadership for every successful non-test Combat, Forage succeeding on 4-6, Explore-combat return-context handling, and GitHub Pages noindex/nofollow.
- WarChronicle.Web and WarChronicle.Pages remain synchronized and can be promoted to GitHub from this same package.

War Chronicle Browser Update 0515

Update 0515 (v0.5.0.15):
- Fixed Combat return context for Explore encounters. Disengaging from Combat triggered during Explore now completes that Explore encounter and proceeds to the Clearing reveal instead of incorrectly starting a second Explore.
- Combat disengage messaging is now context-aware: Explore Combat points back to the Clearing, Arrival Combat indicates Forced Mobilization, and other contexts use neutral wording.
- Every successful non-test Combat now awards +1 Leadership, including forced Combat. This is additive to any card-specific victory/Attack reward. The reward is recorded in both the Chronicle and detailed Battle log.
- Vacant-Clearing Forage now succeeds on 4-6 instead of 5-6; the player-facing Forage prompt has been updated to match.
- Added a noindex/nofollow robots meta directive to the GitHub Pages host so compliant search engines are asked not to index the playtest site. The site remains publicly accessible to anyone with the URL.
- Both WarChronicle.Web (local InteractiveServer) and WarChronicle.Pages (GitHub Pages/WebAssembly) are included and synchronized for these gameplay changes.
- The revised Camp/Explore/Arrival spreadsheet edits are not included in this build because no newer workbook was supplied with this update.

War Chronicle Browser Update 0514 - GitHub Pages Trial

Update 0514 (v0.5.0.14 Pages build):
- Added a separate WarChronicle.Pages Blazor WebAssembly project for GitHub Pages.
- The existing WarChronicle.Web InteractiveServer project is retained for local playtesting and automatic local GameLogs filesystem writes.
- Added .github/workflows/deploy-pages.yml. GitHub Actions builds WarChronicle.Pages and deploys it to GitHub Pages.
- The workflow automatically adjusts the Blazor base path for the repository name, including project sites such as /WarChronicle/.
- The Pages build uses the same game data, components, engine, images, rulebook, localStorage save flow, Debug tools, and downloadable JSON logs as Update 0513.
- Because GitHub Pages is static hosting, the Pages build cannot automatically write completed game logs to a server-side GameLogs folder. Browser archive + Download Game Log remain active there. A future logging backend can replace this without changing the log schema.
- The local WarChronicle.Web project still writes completed logs to WarChronicle.Web\GameLogs.

GitHub Pages quick start:
1. Create a public GitHub repository and place the contents of this ZIP at the repository root.
2. In GitHub: Settings > Pages > Build and deployment > Source = GitHub Actions.
3. Commit/push the files to the main branch.
4. Open the Actions tab to watch the Deploy War Chronicle to GitHub Pages workflow.
5. When it completes, GitHub will provide the Pages site URL.

War Chronicle Browser Update 0513

Update 0513 (v0.5.0.13):
- Added Debug Scene / Echo simulation. Unlock Debug by clicking the version number 5 times, choose any Scene or Echo from the dropdown, and run it through the real EventFlow resolver. Combat launched by a simulated card is fully interactive.
- Debug simulation does not remove/discard the selected card merely because it was launched from Debug. Card effects themselves, including Echo seeding and other state changes, still resolve normally.
- Multi-resource gains are now staged as a single reward instead of being packed in JSON node order. When multiple physical Resources are offered together, the player chooses what to pack next and may purge carried Resources to make room.
- Fixed the Battle Board casualty highlight so it spans the complete selected force area, including the Levy lane.
- Internal Combat target identifiers are translated before reaching the UI. DrPepperHost now displays as Dr. Pepper’s Host.
- Losing the Pepperridge Farm Remembers combat now preserves the authored Dr. Pepper / baked-goods campaign-ending text instead of collapsing to the generic defeat message.
- New Game now uses the same button styling as Download Game Log and the two controls have proper spacing.
- All Update 0512 automatic local JSON logging, defeat New Game flow, and earlier gameplay fixes are retained.

War Chronicle Browser Update 0512

Update 0512 (v0.5.0.12):
- Completed real campaigns are now automatically written as actual JSON files to WarChronicle.Web\GameLogs on the machine running the app.
- Log filenames use the completed-game timestamp format WC_GameLog_YYYY-MM-DD_HH-MM-SS.json. Same-second collisions are preserved with a numeric suffix instead of overwriting an earlier playtest.
- Browser localStorage logging remains as a redundant backup and the manual Download Game Log button remains available.
- Added a New Game button to the Campaign Complete / defeat panel. It appears only after a defeat and returns the player to content selection for a fresh Chronicle.
- GameLogs/*.json is ignored by Git so local playtest logs are not accidentally committed.
- All Update 0511 casualty-choice highlighting, persistent Battle History, hidden Debug tools, and earlier gameplay fixes are retained.

War Chronicle Browser Update 0511

Update 0511 (v0.5.0.11):
- Casualty choices now identify the force explicitly. Enemy casualty choices are labeled as Enemy units; player casualty choices are labeled as Your units.
- When a casualty decision is active, the relevant force row on the Battle Board is highlighted and the control panel displays a matching ENEMY HOST or YOUR HOST banner.
- Added persistent per-game JSON logging for playtests. The current game log is continuously mirrored to browser local storage, including an in-progress Battle Chronicle when a real Combat is active.
- Completed real Combats are retained in a structured Battle History with start/end times, source, opponent, outcome, initial/final forces, and the full detailed Battle Chronicle. Debug Test Combat/Test Finale sessions are excluded from campaign logs.
- Game-log identity, start/end timestamps, and Battle History are included in normal campaign saves and restored when a saved campaign is resumed.
- When a campaign ends, its completed log is archived in this browser and a Download Game Log button appears on the Campaign Complete panel.
- Individual downloaded logs use the agreed browser-local end-time format: WC_GameLog_YYYY-MM-DD_HH-MM-SS.json.
- Game logs also include the normal Chronicle, final state, enabled content, and Finale log when applicable, making the JSON suitable for later collection and analysis.
- Winter defeat now uses the same campaign-end path as Combat/Finale defeat so its game log is finalized consistently.
- All Update 0510 hidden Debug behavior and earlier gameplay fixes are retained.

War Chronicle Browser Update 0510

Update 0510 (v0.5.0.10):
- Test Combat and Test Finale are hidden during normal play.
- Click the version number 5 times within 3 seconds to toggle a compact Debug tools panel.
- Debug mode is intentionally session-only and resets on page reload; it is concealment for normal users, not a security boundary.
- The Debug panel now contains Test Combat, Test Finale, and Exit Debug, and is structured as a home for future developer/test utilities.
- All Update 0509 gameplay behavior is retained.

Update 0509 (v0.5.0.9):
- Restored Repair Baggage Train as a Work result.
- The repair option is offered only when at least one Baggage Train space is damaged and the Host has at least 1 Wood.
- Repairing uses the normal Work action (2 Time), spends 1 Wood, and repairs exactly 1 chosen damaged space.
- The Work card text now states the repair option.

Update 0508 (v0.5.0.8):
- Fixed Unstable Pitch Baggage Train adjacency. Adjacent slots are now determined by the visible two-column Baggage Train layout instead of simple numeric/list order.
- Orthogonal adjacency is used: slots sharing an edge are adjacent; diagonals are not. For example, Firepots in Space 6 may damage Space 4 or Space 5.
- All Update 0507 Harvest Firepots dumping behavior and prior expansion-token functionality are retained.

Update 0507 (v0.5.0.7):
- During Harvest preparation, Firepots may now be voluntarily dumped from the Baggage Train to free their occupied slot.
- Dumping Firepots removes the active Firepots token, so later Echo and Combat checks correctly treat it as gone.
- Harvest Undo restores dumped Firepots to the same Baggage Train slot and reactivates the token.
- Market sales remain Resource-only; Firepots cannot be sold.
- All Update 0506 expansion-token artwork and gameplay behavior are retained.

Update 0506 (v0.5.0.6):
- Main screen now displays War Chronicle v0.5.0.6.
- Added the supplied Seasons in the Abyss token art for Firepots, Painkiller, Forced March, and Reign in Blood.
- Firepots now appears as its physical token art when it occupies a Baggage Train slot.
- Expansion tokens now use their supplied art in the Reminders area while preserving the existing tooltip rules text.
- Combat now shows a compact Active Tokens strip for combat-relevant expansion tokens: Firepots, Painkiller, and Reign in Blood.
- Existing token behavior is retained: Firepots can be used before the first Combat round, Painkiller checks applicable Host losses, Forced March reduces normal Explore to 1 Time, and Reign in Blood prevents Disengage while active.
- All Update 0505 Chronicle iconography, battle-stack, Rulebook, R&R, save-flow, and gameplay fixes are retained.

Visual Studio remains the definitive compile/run check because this package was prepared without a local .NET SDK.