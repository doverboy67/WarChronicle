#!/usr/bin/env python3
"""Reconcile six 0552-imported Camp cards against prior approved 0551 rules.
Requires exact supplied 0551 reference archive as argument, not a guessed
workbook. Preserve all other card fields and all event flows.
"""
from pathlib import Path
import json,sys,zipfile
r=Path(__file__).resolve().parents[1]
base=Path(sys.argv[1] if len(sys.argv)>1 else '/mnt/data/WarChronicleBrowser_Update_0551(2).zip')
with zipfile.ZipFile(base) as z:
 earlier=json.loads(z.read('WarChronicle.Web/Data/wc_data.json'))
current=json.loads((r/'WarChronicle.Web/Data/wc_data.json').read_text())
ids=['CAMP-005','CAMP-006','CAMP-008','CAMP-009','CAMP-011','CAMP-013']
for id in ids:
 old=earlier['cards'][id];new=current['cards'][id]
 assert old['id']==new['id'] and old['title']==new['title']
 new['text']['effect']=old['text']['effect']
 new['cost']=old['cost']
 print(id,new['title'],new['cost'])
for path in ['WarChronicle.Web/Data/wc_data.json','WarChronicle.Pages/Data/wc_data.json','WarChronicle.Pages/wwwroot/Data/wc_data.json']:
 (r/path).write_text(json.dumps(current,ensure_ascii=False,indent=2)+'\n')
