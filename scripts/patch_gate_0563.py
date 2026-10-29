from pathlib import Path
R=Path(__file__).resolve().parents[1]
p=R/'scripts/verify_release.py'
s=p.read_text()
anchor='''            fire_node = flows.get('EF-SCENE-022', {}).get('nodes', {}).get('N10', {})'''
addition='''            # Choice-information contract: all branch consequences are stored in
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
                    and 'Combat' not in note('EF-ECHO-RG-01','N01',2) or
                    'fight the Tollmen' in note('EF-ECHO-RG-01','N01',2),
                    'Tollmen Echo choice mentions Rapport and fight')
            preview = read('WarChronicle.Web/Services/GameSession.cs').decode('utf8', errors='replace')
            require('card.Effect ?? string.Empty' in preview
                    and 'exploreEffectPreview' in preview
                    and 'before any part of it is resolved' in preview,
                    'Clear Road intercept previews the actual current Explore card effect')
            require(flows['EF-TOKEN-HIGH-OVERLOOK-CLEAR-ROAD-REVEAL']['nodes']['N20']['type'] == 'End',
                    'Dormant Clear Road flow keeps the token on Resolve')
'''
assert anchor in s
s=s.replace(anchor,addition+anchor)
p.write_text(s)
