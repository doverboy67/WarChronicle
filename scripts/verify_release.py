#!/usr/bin/env python3
"""War Chronicle release gate. Python 3 stdlib only.

Run at repository root as: python3 scripts/verify_release.py
Optional: python3 scripts/verify_release.py --previous ../WarChronicleBrowser_Update_0560.zip

Exit status 1 on any mismatch. This is static validation, not an app playtest.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP_ROOTS = ('WarChronicle.Web', 'WarChronicle.Pages')
DATA_FILES = (
    'WarChronicle.Web/Data/wc_data.json',
    'WarChronicle.Pages/Data/wc_data.json',
    'WarChronicle.Pages/wwwroot/Data/wc_data.json',
)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def run() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--previous', help='Optional previous ZIP to audit actual changed file list')
    args = parser.parse_args()
    errors: list[str] = []
    checked: list[str] = []

    def require(condition: bool, message: str) -> None:
        if not condition:
            errors.append(message)
        else:
            checked.append(message)

    def read(relative: str) -> bytes:
        p = ROOT / relative
        if not p.is_file():
            errors.append(f'MISSING: {relative}')
            return b''
        return p.read_bytes()

    v = read('RELEASE_VERSION.txt').decode('utf-8-sig', errors='replace').strip()
    require(bool(re.fullmatch(r'\d{4}', v)), 'RELEASE_VERSION.txt must contain exactly 4 digits')
    if not re.fullmatch(r'\d{4}', v):
        print('\n'.join('FAIL: '+x for x in errors), file=sys.stderr)
        return 1
    app_version = f'v0.5.0.{int(v)-500}'
    for document in ('README.md', 'CHANGELOG.md', 'DEVELOPMENT_STATUS.md'):
        require((ROOT / document).is_file(), f'{document} exists')
    leftover = [p.name for p in ROOT.iterdir() if p.is_file() and
                (re.fullmatch(r'README-\d{4}\.txt',p.name) or p.name.startswith(('AUDIT-','HANDOFF-','CHANGE-MANIFEST-')))]
    require(not leftover, f'Historical per-build documents removed from release root ({leftover})')

    for project in APP_ROOTS:
        file = f'{project}/Components/Pages/Home.razor'
        content = read(file).decode('utf-8-sig', errors='replace')
        matches = re.findall(r'private\s+const\s+string\s+AppVersion\s*=\s*"([^"]+)"\s*;', content)
        require(matches == [app_version], f'{file}: AppVersion exactly {app_version}, found {matches}')
        for marker in ('Session.CreateSaveJson(AppVersion)', 'Session.CreateGameLogJson(AppVersion)'):
            require(marker in content, f'{file}: {marker} stays wired to version metadata')

    # Force a clean refresh when updating the application. All assets must match release marker.
    assets = {
        'WarChronicle.Pages/Services/WcDataCatalog.cs': r'Data/wc_data\.json\?v=(\d{4})',
        'WarChronicle.Pages/wwwroot/index.html': r'app\.css\?v=(\d{4})',
        'WarChronicle.Web/Components/App.razor': r'app\.css\?v=(\d{4})',
    }
    for file, expression in assets.items():
        content = read(file).decode('utf8', errors='replace')
        versions = re.findall(expression, content)
        require(versions == [v], f'{file}: asset cache marker must be {v}, found {versions}')

    data = [read(x) for x in DATA_FILES]
    require(bool(data[0]) and data[0] == data[1] == data[2], 'All 3 game-data copies are byte-identical')
    if data[0]:
        try:
            parsed = json.loads(data[0])
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            errors.append(f'Invalid game-data JSON: {exc}')
        else:
            flows = parsed.get('flows', {})
            require(len(flows) >= 156, f'Event-flow count at least 156 (found {len(flows)})')
            bad_edges = []
            for flow_id, f in flows.items():
                nodes = f.get('nodes', {})
                start = f.get('start_node')
                if start not in nodes:
                    bad_edges.append(f'{flow_id}: missing start {start}')
                for node_id, node in nodes.items():
                    destinations = []
                    if node.get('next'):
                        destinations.append(node['next'])
                    destinations += [b.get('next') for b in node.get('branches', []) if b.get('next')]
                    for target in destinations:
                        if target not in nodes:
                            bad_edges.append(f'{flow_id}/{node_id}: missing target {target}')
            require(not bad_edges, f'All event-flow references resolve ({len(bad_edges)} broken: {bad_edges[:5]})')
            scenes = [(k, n_id, b.get('label')) for k, flow in flows.items()
                      if k.startswith('EF-SCENE')
                      for n_id, node in flow['nodes'].items() if node.get('type') == 'Choice'
                      for b in node.get('branches', []) if not str(b.get('notes') or '').strip()]
            require(not scenes, f'All Scene-choice branches have notes ({len(scenes)} missing: {scenes[:5]})')
            # Choice-information contract: all branch consequences are stored in
            # the game-data source, not merely in a fragile collection of C# fallbacks.
            all_choices = [(fid, node_id, idx, b) for fid, f in flows.items()
                           for node_id, node in f['nodes'].items() if node.get('type') == 'Choice'
                           for idx, b in enumerate(node.get('branches', []))]
            missing_details = [(fid, node_id, idx) for fid, node_id, idx, b in all_choices
                               if not str(b.get('notes') or '').strip()]
            require(len(all_choices) == 231, f'Choice inventory stays at 231 or must be re-audited (found {len(all_choices)})')
            require(not missing_details, f'All event Choice branches have written outcome descriptions (missing {missing_details[:10]})')

            def note(fid, node_id, idx):
                return flows[fid]['nodes'][node_id]['branches'][idx].get('notes', '')
            require('Spend 2 Coin' in note('EF-ECHO-B-02','N01',0)
                    and '7+' in note('EF-ECHO-B-02','N01',1)
                    and 'lose 1 Morale' in note('EF-ECHO-B-02','N01',1),
                    'Grudge Carried Forward warns of payment and refusal consequences')
            require('lose 1 Morale' in note('EF-ECHO-F-01','N01',0)
                    and 'Gain 1 Food' in note('EF-ECHO-F-01','N01',0),
                    'Feast Echo warns about its simultaneous reward and cost')
            require('6+' in note('EF-ECHO-H-02','N01',1)
                    and '8+' in note('EF-ECHO-H-02','N01',1),
                    'Regroup Echo explains both conditional test thresholds')
            require('Lose 1 Rapport' in note('EF-ECHO-RG-01','N01',2)
                    and 'fight the Tollmen' in note('EF-ECHO-RG-01','N01',2),
                    'Tollmen Echo choice mentions Rapport and fight')
            preview = read('WarChronicle.Web/Services/GameSession.cs').decode('utf8', errors='replace')
            require('card.Effect ?? string.Empty' in preview
                    and 'exploreEffectPreview' in preview
                    and 'before any part of it is resolved' in preview,
                    'Clear Road intercept previews the actual current Explore card effect')
            require(flows['EF-TOKEN-HIGH-OVERLOOK-CLEAR-ROAD-REVEAL']['nodes']['N20']['type'] == 'End',
                    'Dormant Clear Road flow keeps the token on Resolve')
            fire_node = flows.get('EF-SCENE-022', {}).get('nodes', {}).get('N10', {})
            fire_flags = fire_node.get('flags', [])
            require(any(x.get('name') == 'Location' and x.get('value') == 'BaggageTrain' for x in fire_flags),
                    'Firepots gain node identifies BaggageTrain location')
            camp = parsed.get('cards', {})
            require('CAMP-007' in camp and 'CAMP-002' in camp,
                    'Hospitality and Gathering cards present')
            # Mechanic-level source/data contracts that previously regressed
            # when the card catalog was regenerated from an older workbook.
            # Six 0552 data-import regressions: printed cards changed even
            # though the approved 0551 event flows were retained unchanged.
            # Require card wording and charged cost to stay aligned with those
            # specific old mechanics until the designer explicitly revises them.
            def card_cost(id):
                return camp.get(id, {}).get('cost', {})
            def card_effect(id):
                return camp.get(id, {}).get('text', {}).get('effect', '')
            require(card_cost('CAMP-005').get('value') == '1C'
                    and 'Spend 1 Coin' in card_effect('CAMP-005'),
                    'Commission Study: old approved 1 Coin cost (not imported 3 Coin)')
            require('up to 2 Mercenaries' in card_effect('CAMP-006')
                    and 'Integer0..2' in str(flows.get('EF-CAMP-006', {})),
                    'Hire Mercenaries: maximum 2 recruits matches the flow')
            require(card_cost('CAMP-008').get('value') == '1L'
                    and 'Spend 1 Leadership' in card_effect('CAMP-008')
                    and 'and 1 Food' not in card_effect('CAMP-008'),
                    'Respite: costs 1 Leadership only, not an imported extra Food')
            require(card_cost('CAMP-009').get('type') == 'Variable'
                    and 'Choose 1' in card_effect('CAMP-009')
                    and 'Rest:' in card_effect('CAMP-009'),
                    'Raise Local Levies: optional Raise-or-Rest choice preserved')
            terms_flow = flows.get('EF-CAMP-011', {}).get('nodes', {})
            require('6+ test' in card_effect('CAMP-011')
                    and 'gain 1 Morale' in card_effect('CAMP-011')
                    and 'Test 6+' == terms_flow.get('N02', {}).get('prompt')
                    and 'NoLeadership' in str(terms_flow.get('N02')),
                    'Send Terms Ahead: gift and no-Leadership 6+ test with Morale on success')
            table_flow = flows.get('EF-CAMP-013', {}).get('nodes', {})
            require('test 6+' in card_effect('CAMP-013')
                    and '2 Coins and 1 Leadership' in card_effect('CAMP-013')
                    and 'Test 6+' == table_flow.get('N02', {}).get('prompt')
                    and 'NoLeadership' in str(table_flow.get('N02')),
                    'Host a Common Table: approved no-Leadership 6+ effect matches flow')
            drill = camp.get('CAMP-010', {})
            require(drill.get('cost', {}).get('value') == '1L'
                    and 'Spend 1 Leadership' in drill.get('text', {}).get('effect', ''),
                    'Drill the Host printed and charged costs both require only 1 Leadership')
            herd = flows.get('EF-EXPLORE-006', {}).get('nodes', {})
            require('Convert available pairs' in str(herd.get('N05', {}).get('branches', [{}])[0].get('label', '')),
                    'Herd of Wild Steeds preserves optional conversion choice')
            for project in APP_ROOTS:
                engine = read(f'{project}/Services/GameSession.cs').decode('utf8', errors='replace')
                require('CanAffordDrillAdvancement(_advancementDeck.FirstOrDefault())' in engine
                        and 'CanAffordDrillAdvancement(_armorSupply.FirstOrDefault())' in engine
                        and 'Math.Max(1, ParseResearchCost(advancement.Cost) - 1)' in engine,
                        f'{project}: Drill purchase options check available stock and discounted affordability')
                require('herdUnavailableReason is null' in engine
                        and 'the Host has no Infantry to convert' in engine
                        and 'no matching doubles were rolled' in engine
                        and 'AvailableForceCount("Cavalry") <= 0' in engine,
                        f'{project}: Herd conversion disables unusable choices with explanations')

    for project in APP_ROOTS:
        home = read(f'{project}/Components/Pages/Home.razor').decode('utf8', errors='replace')
        js = read(f'{project}/wwwroot/wcSave.js').decode('utf8', errors='replace')
        require('LoadSavedGameInChunksAsync()' in home
                and 'JS.InvokeAsync<string?>("wcSave.readChunk", offset, chunkSize)' in home
                and 'JS.InvokeAsync<string?>("wcSave.load")' not in home,
                f'{project}: save metadata and Continue path use bounded chunks, not one large transfer')
        require('RetrySavedGameCheckAsync()' in home
                and 'Could not check for a saved campaign' in home
                and '_saveCheckError' in home
                and '_showContentSetup = false;\n            _saveCheckError' in home,
                f'{project}: a failed check prompts retry instead of silently starting anew')
        require('readChunk: function (start, size)' in js
                and 'getLength: function ()' in js
                and 'charCodeAt(end - 1)' in js,
                f'{project}: JS storage reader is chunked and UTF-16-safe')
        scripts_path = ('WarChronicle.Web/Components/App.razor' if project == 'WarChronicle.Web'
                        else 'WarChronicle.Pages/wwwroot/index.html')
        require(f'wcSave.js?v={v}' in read(scripts_path).decode('utf8',errors='replace'),
                f'{project}: updated JS helper is cache-busted on deployment')
    require(read('WarChronicle.Web/wwwroot/wcSave.js') == read('WarChronicle.Pages/wwwroot/wcSave.js'),
            'Both JS save helpers are byte-identical')

    # Debug Simulation Lab is separate from the normal campaign and recognizes
    # visibility-only transitions. The first 0565 smoke test incorrectly stalled
    # immediately after opening a pending Combat because the fingerprint omitted
    # CombatVisible even though OpenPendingCombat changed it correctly.
    for project in APP_ROOTS:
        pilot = read(f'{project}/Services/SimulationPilot.cs').decode('utf8',errors='replace')
        home = read(f'{project}/Components/Pages/Home.razor').decode('utf8',errors='replace')
        require('if (_game.HasPendingCombat) { _game.OpenPendingCombat(); return "Open battle board"; }' in pilot
                and '_game.CombatVisible, _game.HasPendingCombat' in pilot,
                f'{project}: opening a pending Combat is accepted as an actual state change')
        require('c?.CommittedTactics.OrderBy' in pilot
                and 'f?.CommittedTactics.OrderBy' in pilot
                and 'f?.SteadyAdvanceType' in pilot
                and 'flow?.Prompt' in pilot,
                f'{project}: simulation state guard detects tactical and event-choice transitions')
        require('Session.EnabledContentSets.Select(SimulationContentSetName)' in home
                and '"PackC" => "Riff Raff"' in home
                and '"SiaSL" => "Stranger in a Strange Land"' in home
                and '"SitA" => "Seasons in the Abyss"' in home,
                f'{project}: Debug content summary uses actual expansion names')
        require('simGame.StartNewGame(Catalog, Session.EnabledContentSets.ToArray());' in home
                and 'new GameSession(SimulationLoggerFactory.CreateLogger<GameSession>())' in home,
                f'{project}: simulation uses independent engine and copies selected content packs')
    require(read('WarChronicle.Pages/Services/SimulationPilot.cs')
            == read('WarChronicle.Web/Services/SimulationPilot.cs'),
            'Simulation pilots in GitHub Pages and local versions are identical')

    # 0572: No Military-size gate on buying Advancements, including with
    # zero Military Units. Banking in Years I-II is a real strategic choice;
    # the final Summer III R&R cannot use that rationale to strand Research.
    for project in APP_ROOTS:
        pilot = read(f'{project}/Services/SimulationPilot.cs').decode('utf8', errors='replace')
        action = pilot.split('    private string RefitAction()', 1)[1].split('    private int Count(string type)', 1)[0]
        require('var bankForFuture = !finalRefit && research > 0' in action
                and 'var finalRefit = _game.Year >= 3;' in action
                and 'var affordable = offered.Where(a => a.affordable)' in action,
                f'{project}: early Research banking disabled at final R&R')
        require('!ownedIds.Contains(a.Id)' in action
                and '!owned.Contains(a.Name)' not in action,
                f'{project}: distinct Armor IDs may be purchased repeatedly')
        require('AdvancementValue(a.Name)' in action
                and 'AdvancementCost(a.Cost, "R")' in action
                and '_game.CanBuyRefitAdvancement(a.Id)' in action
                and '_game.BuyRefitAdvancement(chosen.Id)' in action,
                f'{project}: real engine legality and Advancement costs retained')
        require('if (military >= minimumMilitary && improvements.Count > 0)' not in action
                and 'if (!bankForFuture && affordable.Count > 0' in action
                and 'if (lower.Contains("halberd"))' in action
                and 'if (lower.Contains("longbow"))' in action
                and 'var futureRecruitment = _game.Year < 3;' in action,
                f'{project}: future-oriented Advancement purchases have no army-size gate')

    # 0573: Diplomatic Leadership decisions must cover BOTH passage cases,
    # have a nonlinear multi-Hostile risk threshold, and preserve any planned
    # first-round retreat when the gamble actually leads to battle.
    for project in APP_ROOTS:
        pilot = read(f'{project}/Services/SimulationPilot.cs').decode('utf8', errors='replace')
        require('NeedLeadershipForDiplomaticSafety(IsCurrentArrivalTribeHostile())' in pilot
                and 'if (_game.Year == 1 || military < 4 || total <= 5) return true;' in pilot
                and 'if (otherHostile >= 1 || HostileTribeCount >= 2) return true;' in pilot,
                f'{project}: early and multiple-hostile diplomacy spends last Leadership')
        require('if (Id("gate:normal") is { } gate)' in pilot
                and 'Id("gate:leadership") is { } protection' in pilot
                and 'ask.Contains("tribute", StringComparison.OrdinalIgnoreCase)' in pilot,
                f'{project}: unpaid-tribute and paid-Hostile passage both assessed')
        require('ArrivalEnemyUnits()' in pilot
                and 'enemy.Cavalry > 0' in pilot
                and 'ReignInBlood' in pilot
                and 'expectedEnemyHits <= acceptableExposure' in pilot,
                f'{project}: later-game decisions assess enemy and retreat risk')
        require('ArrivalEventTestThreatensRapport(ask)' in pilot
                and 'entry.Groups["loss"]' in pilot,
                f'{project}: choose-one event tests inspect actual Rapport-loss stakes')
        require('PlannedDisengagementApplies(combat)' in pilot
                and 'plannedEscape || hostileCrisis' in pilot
                and 'if (!plannedEscape &&' in pilot,
                f'{project}: disengage plan carried into combat and cleared after safe passage')
        require('score = IsFinalAutumn ? 1.0' in pilot
                and 'score = IsFinalAutumn ? (research >= 2 ? 19 : 2)' in pilot,
                f'{project}: Autumn III Research devalued while Drill purchase retained')

    # 0571: The final failed Painkiller dose must not leave a pending hit in
    # CasualtyChoice when no remaining Shield/Loose Formation defense exists.
    # Preserve the defense prompt for successful retries and other legal options.
    for project in APP_ROOTS:
        combat = read(f'{project}/Services/GameSession.Combat.cs').decode('utf8', errors='replace')
        defense = combat.split('private void ResolveCombatDefenseChoice(string choiceId)', 1)[-1]
        defense = defense.split('private void ResolveCavalryPursuit()', 1)[0]
        fallback = 'if (!saved)\n            {\n                // A failed dose does not consume the incoming hit. Offer any\n                // remaining dose, Shield, or Loose Formation defense first.\n                if (PresentPlayerDefense(type))\n                    return;\n\n                // The last dose failed and no defenses remain. Resolve the\n                // pending casualty instead of leaving combat in CasualtyChoice\n                // with an empty choice list and an unconsumed enemy hit.\n                ApplyCombatCasualty(attackerIsPlayer: false, type);\n            }'
        require(defense.count(fallback) == 1,
                f'{project}: exhausted failed Painkiller applies casualty only if no other defense is possible')
        require(defense.find(fallback) < defense.find('combat.PendingEnemyHits--;')
                and 'combat.PendingEnemyHits--;\n        ContinueCombatCasualties();' in defense,
                f'{project}: exhausted failed Painkiller consumes exactly one hit and resumes combat')
        require('if (_painkillerDoses == 0) ActiveTokens.Remove("Painkiller");' in defense,
                f'{project}: depleted Painkiller token removed')
    require(read('WarChronicle.Pages/Services/GameSession.Combat.cs')
            == read('WarChronicle.Web/Services/GameSession.Combat.cs'),
            'Combat engines in GitHub Pages and local versions are identical')

    baseline = [line.strip() for line in read('RELEASE_BASELINE_0551_FILES.txt').decode('utf8').splitlines() if line.strip()]
    require(len(baseline) == 146, f'Protected 0551 inventory lists exactly 146 paths ({len(baseline)} found)')
    # The original baseline lists historical READMEs that were intentionally
    # consolidated in 0564; source-code/configuration files remain protected.
    protected = [name for name in baseline if not name.startswith('README-')]
    missing = [name for name in protected if not (ROOT / name).is_file()]
    require(not missing, f'All protected 0551 paths still exist ({len(missing)} missing: {missing[:10]})')

    if args.previous:
        with zipfile.ZipFile(args.previous) as z:
            before = {i.filename: z.read(i) for i in z.infolist() if not i.is_dir()}
        after = {str(p.relative_to(ROOT)).replace('\\','/'): p.read_bytes()
                 for p in ROOT.rglob('*') if p.is_file()}
        changed = {key for key in set(before)|set(after) if before.get(key)!=after.get(key)}
        changed_app = {key for key in changed if key.startswith(APP_ROOTS)}
        permitted_app = {
            'WarChronicle.Web/Components/Pages/Home.razor',
            'WarChronicle.Pages/Components/Pages/Home.razor',
            'WarChronicle.Web/wwwroot/wcSave.js',
            'WarChronicle.Pages/wwwroot/wcSave.js',
            'WarChronicle.Web/Components/App.razor',
            'WarChronicle.Pages/wwwroot/index.html',
            'WarChronicle.Pages/Services/WcDataCatalog.cs',
            'WarChronicle.Pages/Services/SimulationPilot.cs',
            'WarChronicle.Web/Services/SimulationPilot.cs',
            'WarChronicle.Pages/Services/GameSession.Combat.cs',
            'WarChronicle.Web/Services/GameSession.Combat.cs',
            'WarChronicle.Pages/wwwroot/app.css',
            'WarChronicle.Web/wwwroot/app.css',
        }
        # Later incremental releases need not modify every path in the cumulative
        # allowlist (e.g., the save helper and CSS remain byte-identical).
        require(not (changed_app - permitted_app),
                f'Only approved application files changed vs previous release ({sorted(changed_app - permitted_app)})')
        deleted_app = [key for key in before if key.startswith(APP_ROOTS) and key not in after]
        require(not deleted_app, f'No application files deleted ({deleted_app})')
        unchanged_game = ['WarChronicle.Web/Data/wc_data.json',
                          'WarChronicle.Pages/Data/wc_data.json',
                          'WarChronicle.Web/Services/GameSession.cs',
                          'WarChronicle.Web/Services/GameSession.Save.cs',
                          'WarChronicle.Pages/Services/GameSession.cs',
                          'WarChronicle.Pages/Services/GameSession.Save.cs']
        require(all(before.get(x)==after.get(x) for x in unchanged_game),
                'Core GameSession, save serializers, and game data remain unchanged vs previous release')

    print(f'War Chronicle release gate — {v} ({app_version})')
    for message in checked:
        print('PASS:', message)
    for message in errors:
        print('FAIL:', message)
    print(f'Result: {"FAILED" if errors else "PASSED"} ({len(checked)} checks passed; {len(errors)} failed).')
    if not args.previous:
        print('NOTE: Exact source diff vs previous ZIP not checked (pass --previous to check).')
    print('NOTE: Static gate only; .NET compile and browser gameplay are separate tests.')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(run())
