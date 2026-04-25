import json
with open(r'C:\Users\u\Documents\GitHub\Project_Abyss\Assets\Abyss\Resources\STAGEDATA_MAP.json', 'r', encoding='utf-8') as f:
    data = json.load(f)
print(f'Total rooms: {len(data["rooms"])}')
for r in data['rooms']:
    g = r['grid_csv']
    counts = {}
    for cell in g.replace(';', ',').split(','):
        counts[cell] = counts.get(cell, 0) + 1
    tokens = {k: counts.get(k, 0) for k in
              ['P','B','S','N','C','O','Mc3','mc3','Mc4','mc4','Mr4','mr4','Me1',
               'dt','dp','dn','df','dl','dm']}
    tokens = {k: v for k, v in tokens.items() if v > 0}
    print(f'[{r["room_id"]:13}] {r["theme"]:7} {r["width"]}x{r["height"]} max={r["max_active_spawners"]}  {tokens}')
