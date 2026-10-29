#!/usr/bin/env python3
"""Reproducible 0563 choice-information repair. Mutates only documented fields.

All text below is read directly against 0562 event-node action sequences. Echo
seeding is deliberately not disclosed by name; the existing Echo indicator remains.
"""
import csv
import json
import re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PATH=ROOT/'WarChronicle.Web/Data/wc_data.json'
j=json.loads(PATH.read_text())
flows=j['flows']
changed=[]

# All 23 second-stage Arrival reward branches are described in their existing
# labels. Expand those labels into full player-facing sentence descriptions.
for id, flow in flows.items():
    if flow['source'].get('type')!='Arrival':continue
    for nodeid, node in flow['nodes'].items():
        if node.get('type')!='Choice':continue
        for idx,b in enumerate(node.get('branches',[])):
            if str(b.get('notes') or '').strip():continue
            label=b.get('label','')
            if not re.match(r'^Option \d+: ',label):continue
            simple=re.sub(r'^Option \d+: ', '', label).replace(', ', '; ')
            if 'Rapport' in simple: simple=simple.replace('Rapport','Rapport with the current tribe')
            b['notes']=simple+'.'
            changed.append((id,nodeid,idx,b['notes']))

# Explicit, reviewed descriptions for every undecorated Camp, Explore and
# non-hostile Echo branch, keyed by flow source, node and branch index.
D={}
def pair(src,node,*notes):
    for idx,nt in enumerate(notes): D[(src,node,idx)]=nt
pair('CAMP-001','N81',
     'If you have Camp Steward, roll 1d6: on 1–4 you may buy 1 Resource for 1 Coin less than Market Buy (minimum 1 Coin), or sell at Market Sell; on 5–6 gain 1 Resource matching adjacent Terrain for free. Baggage capacity applies.',
     'Skip the optional Camp Steward search. No further effect.')
# Hostile Echo choices already receive specific resource/tribe details in the
# dedicated parley renderer; these explicit notes also protect data-only views.
for n in range(1,5):
    pair(f'ECHO-A-0{n}','N20',
         'Pay 2 Resources matching adjacent Terrain, then test Rapport 9+. Pass: restore this tribe to Neutral and end the Echo. Fail: Combat; Camp ends.',
         'Pay 5 Coin, then test Rapport 9+. Pass: restore this tribe to Neutral and end the Echo. Fail: Combat; Camp ends.',
         'Decline Parley and fight the Hostile tribe. Camp ends.')
pair('ECHO-B-01','N01','Advance 1 Time.','Lose 1 Leadership.')
pair('ECHO-B-02','N01','Spend 2 Coin. The Echo ends without a test.',
     'Test 7+. Pass: no further effect. Fail: lose 1 Morale. The Echo ends either way.')
pair('ECHO-D-02','N02','Recruit 1 Levy for free, subject to the normal Levy limit.','Do not recruit a Levy. No further effect.')
pair('ECHO-E-02','N01','Gain 1 Coin.','Gain 1 Food, subject to Baggage capacity.')
pair('ECHO-F-01','N01','Gain 1 Food and lose 1 Morale.', 'Refuse the offering. No immediate Resource or Morale change.')
pair('ECHO-H-01','N10','Use your Priest to test 7+. Pass: prevent the Morale loss. Fail: lose 1 Morale.',
     'Do not intervene. Lose 1 Morale.')
pair('ECHO-H-02','N01','Advance 1 Time.',
     'Test 6+ if you have Scouting, otherwise 8+. Pass: no further effect. Fail: lose 1 Morale.')
pair('ECHO-KB-02','N10','Spend 1 Wood and advance 1 Time to repair the just-damaged Baggage slot.',
     'Leave the damaged Baggage slot unrepaired. No further effect.')
pair('ECHO-RG-01','N01','Spend 3 Coin. The Echo ends.',
     'Advance 2 Time instead of paying. The Echo ends.',
     'Lose 1 Rapport with the nearest tribe, then fight the Tollmen. The Echo ends after Combat.')
pair('ECHO-T-01','N10','Gain 1 Morale.','Gain 1 Leadership.')
pair('ECHO-U-01','N20','Keep Firepots and discard this Echo, allowing it to return later.',
     'Discard Firepots and remove this Echo permanently.')
pair('ECHO-V-01','N01','Lose 1 Food and gain 1 Leadership.',
     'Reclaim the provisions. No immediate change.')

pair('EXPLORE-001','N01',
     'Buy 1 Resource at its current Market Buy price, subject to Baggage capacity.',
     'Sell 1 carried Resource at its current Market Sell price.',
     'Leave the caravan without trading. No effect.')
pair('EXPLORE-006','N05',
     'For matching pairs among the Infantry dice, optionally convert eligible Infantry to Cavalry, limited by available Cavalry. No conversion if no matching pairs or Infantry.',
     'Make no conversion. Keep your Infantry.')
# Obsolete/unreachable Roaming Nomads branch; annotate in case old flow is ever
# activated, but do not reconnect or alter the approved live route.
pair('EXPLORE-007','N01','Spend the current Market Buy price to buy 1 Food; then roll 1d6, gaining 1 Levy on 5–6.',
     'Do not buy Food; roll 1d6, gaining 1 Levy on 5–6.')
pair('EXPLORE-014','N02','Spend 1 Coin and gain 1 Leadership.', 'Keep the Wood already received. No additional effect.')
pair('EXPLORE-017','N30','Lose 1 Infantry.','Lose 1 Morale instead.')
pair('EXPLORE-019','N10','Spend 1 {Roll.Result} and gain 1 Research.', 'Do not pay. No Research gained.')
pair('EXPLORE-024','N01','Spend 3 Coin and gain 1 Research.', 'Leave the offer. No effect.')
pair('EXPLORE-026','N01','Spend 1 Leadership and gain 1 Research.', 'Decline. No effect.')
pair('TOKEN-FIREPOTS','N01',
     'Use and discard Firepots before the first Combat round. Roll 3d6; each 4+ inflicts 1 enemy casualty, assigned as a Cavalry attack.',
     'Keep Firepots for a later Combat. No effect now.')
pair('TOKEN-HIGH-OVERLOOK-CLEAR-ROAD','N01',
     'Discard Clear Road to cancel the Explore card before resolving any of its effects.',
     'Resolve this Explore card normally and KEEP Clear Road for a future Explore card.')

review_csv=ROOT/'AUDIT-0562-CHOICE-BRANCHES.csv'
needs={(r['Card ID'],r['Node ID'],int(r['Branch #']))
       for r in csv.DictReader(review_csv.open())
       if r['Status']=='No stored or exact fallback'}
for id,f in flows.items():
    src=f['source'].get('id')
    for nodeid,node in f['nodes'].items():
        if node.get('type')!='Choice':continue
        for idx,b in enumerate(node['branches']):
            if str(b.get('notes') or '').strip() or (src,nodeid,idx) not in needs:continue
            key=(src,nodeid,idx)
            if key not in D:
                raise ValueError(f'No reviewed description for choice {id}/{nodeid}/{idx} {b.get("label")}')
            b['notes']=D[key]
            changed.append((id,nodeid,idx,b['notes']))

# Repair the contradictory dormant Clear Road flow definition; the live special
# prompt already keeps the token when the player chooses to resolve the event.
high=flows['EF-TOKEN-HIGH-OVERLOOK-CLEAR-ROAD-REVEAL']
assert high['nodes']['N20']['action']=={'type':'RemoveToken','target':'HighOverlookClearRoad','value':1}
high['nodes']['N20']={'type':'End'}

assert len(changed)==83, f'Expected 83 branches repaired; got {len(changed)}'
for path in ['WarChronicle.Web/Data/wc_data.json', 'WarChronicle.Pages/Data/wc_data.json', 'WarChronicle.Pages/wwwroot/Data/wc_data.json']:
    (ROOT/path).write_text(json.dumps(j,ensure_ascii=False,indent=2)+'\n')
with (ROOT/'CHOICE_NOTES_REVIEW_0563.csv').open('w',newline='',encoding='utf8') as out:
    w=csv.writer(out);w.writerow(['flow','node','branch_index','added_player_description']);w.writerows(changed)
print('Added',len(changed),'player-choice descriptions and reconciled dormant Clear Road flow')
