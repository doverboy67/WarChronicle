#!/usr/bin/env python3
"""Consolidate previously hardcoded consequence text into event source data.
Preserves C# dynamic overrides and does not change outcomes.
"""
import json,csv
from pathlib import Path
R=Path(__file__).resolve().parents[1]
j=json.loads((R/'WarChronicle.Web/Data/wc_data.json').read_text())
D={}
def put(id,node,*texts):
 for i,t in enumerate(texts):D[(id,node,i)]=t
put('CAMP-001','N01','Gain 2 Coin.','Gain 1 Coin and 1 Leadership.','Spend 1 Wood to repair one damaged Baggage Train space.')
put('CAMP-003','N01','Spend 1 Wood and 1 Coin to build a Chapel and gain a Priest.','Spend 1 Stone and 1 Coin to build an Academy.')
put('CAMP-004','N01','Spend 1 Wood and 1 Coin to build a Market.','Spend 1 Wood to add 2 Baggage Train spaces.')
put('CAMP-007','N01','Spend 1 Food. Gain 1 Leadership and 1 Morale.',
    'Spend 1 Food. Gain 1 Leadership and one Resource matching adjacent Terrain, subject to Baggage capacity.',
    'Spend 1 Food. Gain 1 Morale and one Resource matching adjacent Terrain, subject to Baggage capacity.')
put('CAMP-009','N01','Spend 1 Food and add 2 Levy to the Host.','Gain 1 Morale.')
put('CAMP-010','N01','Buy an Advancement or Armor for 1 less Research (minimum 1 Research). All other printed costs apply. Afterward regain 1 Leadership.',
    'Gain 1 Research; optionally discard and replace the top Advancement. Afterward regain 1 Leadership.')
put('CAMP-010','N10','Buy the top Advancement card for 1 less Research (minimum 1 Research), plus any other printed cost.',
    'Buy Armor from the R&R offer for 1 less Research (minimum 1 Research), plus any other printed cost.')
put('CAMP-010','N21','Discard the top Advancement card for free and reveal a new one.',
    'Keep the top Advancement card. No further effect.')
put('CAMP-012','N01','Spend 1 Coin to gain Scouting (on Explore, draw 2 Terrain cards and choose one).',
    'Spend 2 Coin to gain Camp Steward (additional Work search benefit).')
put('ECHO-U-02','N10','Discard Firepots and end this Echo.','Keep Firepots. No immediate change.')
put('ECHO-L-01','N10','Lose 1 Leadership and keep Forced March.', 'Advance 2 Time and remove Forced March.')
put('ECHO-L-02','N01','Lose 1 Leadership and keep Forced March.', 'Remove Forced March and gain 1 Morale.')
put('EXPLORE-031','N01','Gain 3 Coin, 1 Leadership and Reign in Blood (you cannot Disengage while active). Future consequences are possible.',
    'Advance 1 Time to bury the dead. Future consequences are possible.')
put('ECHO-G-02','N01','Lose 1 Leadership.', 'Spend 2 Coin.',
    'Requires a Priest. Test 7+. Pass: no further effect. Fail: lose 1 Morale.')
put('EXPLORE-012','N01','Choose 1 Unit, then roll 1d6. On 1, lose that Unit; otherwise gain 1 Food.', 'Leave the fungi. No effect.')
put('EXPLORE-016','N01','Roll 1d6. On 6, lose 1 Infantry if possible; otherwise gain Coin equal to the die result.', 'Leave it. No effect.')
put('EXPLORE-017','N01','Fight a Hill Clan force with Infantry equal to the current Year.',
    'Roll 1d6. On 5–6, negotiate safe passage; on 1–4, lose 1 Infantry or 1 Morale.')
put('EXPLORE-022','N01','Test 6+ with combined Rapport DRM. Pass: gain 1 Leadership; fail: no effect.',
    'Test 7+ with combined Rapport DRM. Pass: gain 3 Coin; fail: no effect.',
    'Test 8+ with combined Rapport DRM. Pass: gain 6 Coin; fail: reduce Rapport with every encountered tribe (unmet tribes become Hostile).')
put('EXPLORE-023','N01','Gain Clear Road. Before resolving a future triggered Explore event, you may discard Clear Road to ignore it.',
    'At the next Clearing reveal, gain 1 Leadership if Barbarian; otherwise gain 1 Morale.')
put('EXPLORE-028','N01','Assign 1 Infantry and roll 1d6. On 4–6, gain Halberds or Longbows if available; on 1–3, lose the assigned Infantry.',
    'Leave the dead alone. No effect.')
put('EXPLORE-029','N01','Roll 1d6. On 5–6, gain an Armor Advancement; on 1, lose 1d3 Units of your choice; on 2–4, no effect.',
    'Leave the site. No effect.')
added=[]
for fid,flow in j['flows'].items():
 s=flow['source']['id']
 for nid,n in flow['nodes'].items():
  if n.get('type')!='Choice':continue
  for i,b in enumerate(n['branches']):
   if b.get('notes'):continue
   key=(s,nid,i)
   if key not in D:raise RuntimeError(f'Unreviewed: {fid} {nid} {i} {b["label"]}')
   b['notes']=D[key];added.append((fid,nid,i,b['notes']))
assert len(added)==46,len(added)
for path in ['WarChronicle.Web/Data/wc_data.json','WarChronicle.Pages/Data/wc_data.json','WarChronicle.Pages/wwwroot/Data/wc_data.json']:
 (R/path).write_text(json.dumps(j,indent=2,ensure_ascii=False)+'\n')
with (R/'CHOICE_NOTES_REVIEW_0563.csv').open('a',newline='',encoding='utf8') as f:csv.writer(f).writerows(added)
print('Consolidated',len(added),'hardcoded descriptions; all 231 branches now have stored notes')
