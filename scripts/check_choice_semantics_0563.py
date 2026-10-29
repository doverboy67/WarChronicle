#!/usr/bin/env python3
"""Independent source semantic diff: approved 0562->0563 event data edits only."""
from pathlib import Path
import json,zipfile,sys,copy
r=Path(__file__).resolve().parents[1]
prior=Path(sys.argv[1] if len(sys.argv)>1 else '/mnt/data/WarChronicleBrowser_Update_0562.zip')
with zipfile.ZipFile(prior) as z:
    old=json.loads(z.read('WarChronicle.Web/Data/wc_data.json'))
now=json.loads((r/'WarChronicle.Web/Data/wc_data.json').read_text())
assert set(now)==set(old)
assert len(now['flows'])==len(old['flows'])==156
count=0
for fid,f in now['flows'].items():
    prev=copy.deepcopy(old['flows'][fid]); cur=copy.deepcopy(f)
    for nid,n in cur['nodes'].items():
        if n.get('type')!='Choice':continue
        for i,b in enumerate(n['branches']):
            earlier=prev['nodes'][nid]['branches'][i]
            if not earlier.get('notes'):
                assert b.get('notes') and len(b['notes'].strip())>3,(fid,nid,i)
                count+=1
                b.pop('notes')
    if fid=='EF-TOKEN-HIGH-OVERLOOK-CLEAR-ROAD-REVEAL':
        assert cur['nodes']['N20']=={'type':'End'}
        cur['nodes']['N20']=prev['nodes']['N20']
    assert cur==prev, f'Unapproved event-flow change in {fid}'
assert count==129, count
left=copy.deepcopy(now); right=copy.deepcopy(old)
left.pop('flows');right.pop('flows')
# 0552 imported six card effects/costs inconsistent with their unchanged,
# historically approved 0551 flows. Restored printed effect/cost only.
with zipfile.ZipFile(sys.argv[2] if len(sys.argv)>2 else '/mnt/data/WarChronicleBrowser_Update_0551(2).zip') as z:
    approved=json.loads(z.read('WarChronicle.Web/Data/wc_data.json'))
restored=('CAMP-005','CAMP-006','CAMP-008','CAMP-009','CAMP-011','CAMP-013')
for id in restored:
    assert left['cards'][id]['text']['effect']==approved['cards'][id]['text']['effect'],id
    assert left['cards'][id]['cost']==approved['cards'][id]['cost'],id
    left['cards'][id]['text']['effect']=right['cards'][id]['text']['effect']
    left['cards'][id]['cost']=right['cards'][id]['cost']
assert left==right, 'Unapproved data change beyond six corrected cards, 129 Choice notes, and dormant token' 
choices=[b for f in now['flows'].values() for n in f['nodes'].values() if n.get('type')=='Choice' for b in n['branches']]
assert len(choices)==231 and all(b.get('notes') for b in choices)
for project in ['WarChronicle.Web','WarChronicle.Pages']:
    with zipfile.ZipFile(prior) as z:
        earlier=z.read(f'{project}/Services/GameSession.cs').decode()
    current=(r/project/'Services/GameSession.cs').read_text()
    start=earlier.index('        if (clearRoadActive)\n        {')
    end=earlier.index('        StartExploreFlow(card);',start)
    bstart=current.index('        if (clearRoadActive)\n        {')
    bend=current.index('        StartExploreFlow(card);',bstart)
    assert earlier[:start]+earlier[end:]==current[:bstart]+current[bend:], f'Unexpected gameplay-engine edit: {project}'
    assert 'card.Effect ?? string.Empty' in current[bstart:bend], 'Actual triggering card effect must display'
    assert 'new("high-overlook:resolve"' in current[bstart:bend]
    assert 'new("high-overlook:ignore"' in current[bstart:bend]
    assert 'StartExploreFlow(' not in current[bstart:bend], 'No event executes before Clear Road choice'
print('PASS: 156 flow definitions compared, 129 note-only additions, dormant Clear Road parity corrected')
print('PASS: Six Camp card effects/costs restored EXACTLY to 0551; other card data unchanged')
print('PASS: 231/231 choice notes present')
print('PASS: Web + Pages engine edit limited to Clear Road prompt, no pre-choice resolution')
