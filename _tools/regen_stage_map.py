"""
모든 방 디자인 재생성 — 30x30 통일.
토큰:
  W=Wall, F=Floor, O=Obstacle, P=PlayerStart, S=Shop, B=Boss
  C=BuffCrystal, N=NPC/Altar
  M*=confirmed spawner, m*=candidate spawner (c/r/e + count)
  d[t/p/n/f/l/m]=Decoration (tree/pine/bigTree/flower/bush/mushroom)
플레이어는 모든 방 하단 중앙 (row=27, col=14).
"""
import json
import os

GRID = 30  # 30x30 통일
PLAYER_ROW, PLAYER_COL = 27, 14

# ──────────────────────────────────────────────────────────────
# 헬퍼
# ──────────────────────────────────────────────────────────────

def empty_grid():
    g = [["F"] * GRID for _ in range(GRID)]
    for i in range(GRID):
        g[0][i] = "W"
        g[GRID - 1][i] = "W"
        g[i][0] = "W"
        g[i][GRID - 1] = "W"
    return g

def place(g, cells, token, overwrite_floor_only=True):
    for r, c in cells:
        if 0 <= r < GRID and 0 <= c < GRID:
            if not overwrite_floor_only or g[r][c] == "F":
                g[r][c] = token

def grid_to_csv(g):
    return ";".join(",".join(row) for row in g)

# ──────────────────────────────────────────────────────────────
# 방 레이아웃 정의
# ──────────────────────────────────────────────────────────────

def build_battle_forest():
    g = empty_grid()
    # 데코 (Forest: 나무/꽃/버섯/부시 풍부)
    place(g, [(3, 5), (3, 24), (26, 5), (26, 24)], "dt")
    place(g, [(6, 14), (23, 14)], "dp")
    place(g, [(12, 3), (12, 26), (18, 3), (18, 26)], "dn")
    place(g, [(15, 14)], "df")
    place(g, [(20, 8), (20, 21)], "dl")
    place(g, [(9, 14), (25, 14)], "dm")
    # 장애물 (나무 바위 틈)
    place(g, [(10, 10), (10, 19), (17, 7), (17, 22), (22, 12), (22, 17)], "O")
    # 몬스터 스포너 — Forest T1: 확정 2 + 후보 3 → max=3
    place(g, [(7, 8), (7, 21)], "Mc3")
    place(g, [(14, 8), (14, 21), (19, 14)], "mc3")
    # 플레이어
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 3

def build_battle_cave():
    g = empty_grid()
    # 데코 (Cave: 버섯/이끼만)
    place(g, [(7, 14), (22, 14)], "dm")
    place(g, [(14, 4), (14, 25)], "dl")
    # O 동굴 바위 많이
    rocks = [
        (3, 6), (3, 14), (3, 23),
        (6, 4), (6, 25),
        (10, 9), (10, 20),
        (13, 14),
        (16, 7), (16, 22),
        (20, 4), (20, 25),
        (23, 11), (23, 18),
        (26, 6), (26, 23),
    ]
    place(g, rocks, "O")
    # 스포너 — Cave T1: 확정 2 + 후보 4 → max=4
    place(g, [(8, 8), (8, 21)], "Mc4")
    place(g, [(14, 10), (14, 19), (21, 8), (21, 21)], "mc4")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 4

def build_battle_crypt():
    g = empty_grid()
    # 데코 최소 (어두운 분위기)
    place(g, [(14, 5), (14, 24)], "dl")
    # O 석관·비석 규칙적 배치
    tombs = [
        (6, 6), (6, 14), (6, 23),
        (11, 4), (11, 25),
        (15, 10), (15, 19),
        (19, 4), (19, 25),
        (24, 6), (24, 14), (24, 23),
    ]
    place(g, tombs, "O")
    # 스포너 — Crypt T1: rare 확정 2 + 후보 3 → max=4
    place(g, [(9, 9), (9, 20)], "Mr4")
    place(g, [(17, 7), (17, 22), (22, 14)], "mr4")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 4

def build_elite_forest():
    g = empty_grid()
    # 엘리트 방: 넓은 전투공간 + 풍부한 데코
    place(g, [(3, 5), (3, 24), (25, 5), (25, 24)], "dt")
    place(g, [(5, 14), (24, 14)], "dp")
    place(g, [(11, 3), (11, 26), (20, 3), (20, 26)], "dn")
    place(g, [(8, 14), (22, 14)], "df")
    place(g, [(15, 6), (15, 23)], "dl")
    place(g, [(17, 14)], "dm")
    # O 중앙 라인 (전투 구도)
    place(g, [(14, 10), (14, 19), (18, 10), (18, 19)], "O")
    # 엘리트 스포너 — confirmed 2 (Me1 = elite grade, 1마리씩)
    place(g, [(12, 14), (20, 14)], "Me1")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 2  # max=2 (엘리트 전원 활성)

def build_event_forest():
    g = empty_grid()
    # 이벤트 방: 중앙 제단 + 원형 데코
    # 중앙 알터
    g[14][14] = "N"
    # 중앙 주변 원형 꽃/버섯 장식
    place(g, [(12, 12), (12, 16), (16, 12), (16, 16)], "df")
    place(g, [(11, 14), (17, 14), (14, 11), (14, 17)], "dm")
    # 외곽 나무
    place(g, [(3, 5), (3, 14), (3, 24)], "dt")
    place(g, [(26, 5), (26, 24)], "dt")
    place(g, [(7, 6), (7, 23), (21, 6), (21, 23)], "dp")
    place(g, [(10, 3), (10, 26), (19, 3), (19, 26)], "dn")
    place(g, [(23, 10), (23, 19)], "dl")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 0

def build_event_cave():
    g = empty_grid()
    # 중앙 버프 크리스탈
    g[14][14] = "C"
    # 크리스탈 주변 이끼·버섯 조금
    place(g, [(12, 14), (16, 14), (14, 12), (14, 16)], "dm")
    # 동굴 외곽 바위
    place(g, [(4, 6), (4, 23), (25, 6), (25, 23)], "O")
    place(g, [(10, 4), (10, 25), (19, 4), (19, 25)], "O")
    place(g, [(7, 14), (22, 14)], "dl")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 0

def build_shop_town():
    g = empty_grid()
    # 상점 좌대 — 상단 3칸 일렬 + 측면 2칸
    place(g, [(10, 10), (10, 14), (10, 19)], "S")
    place(g, [(16, 7), (16, 22)], "S")
    # 소소한 부시
    place(g, [(6, 6), (6, 23), (24, 6), (24, 23)], "dl")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 0

def build_start_forest():
    g = empty_grid()
    # 시작 방: 탐험 분위기, 적 없음, 데코 풍부
    # 플레이어 주변 클리어 + 전방으로 유도하는 숲길
    # 좌우 나무 라인
    for r in (3, 6, 9, 12, 15, 18, 21, 24):
        g[r][3] = "dt"
        g[r][26] = "dt"
    # 중앙 꽃밭
    place(g, [(11, 13), (11, 16), (12, 14), (12, 15), (13, 13), (13, 16)], "df")
    place(g, [(15, 14), (15, 15)], "dm")
    # 활엽수 장식
    place(g, [(7, 8), (7, 21), (20, 8), (20, 21)], "dn")
    # 부시
    place(g, [(22, 12), (22, 17)], "dl")
    # 전나무
    place(g, [(5, 14), (25, 10), (25, 19)], "dp")
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 0

def build_boss_castle():
    g = empty_grid()
    # 보스룸: 중앙 넓게, 외곽 기둥 규칙 배치
    # 기둥 (O)
    pillars = [
        (4, 4), (4, 14), (4, 25),
        (14, 4), (14, 25),
        (24, 4), (24, 14), (24, 25),
    ]
    place(g, pillars, "O")
    # 대칭 양측 보조 기둥
    place(g, [(9, 8), (9, 21), (19, 8), (19, 21)], "O")
    # 중앙 보스
    g[13][14] = "B"
    # 플레이어는 입구 (가장 아래)
    g[PLAYER_ROW][PLAYER_COL] = "P"
    return g, 0

# ──────────────────────────────────────────────────────────────
# 방 레지스트리
# ──────────────────────────────────────────────────────────────

ROOMS = [
    dict(room_id="battle_001", category="Battle", theme="Forest", palette="ForestT1",
         stat_version=3, return_duration=3.0, builder=build_battle_forest),
    dict(room_id="battle_002", category="Battle", theme="Cave",   palette="CaveT1",
         stat_version=2, return_duration=3.0, builder=build_battle_cave),
    dict(room_id="battle_003", category="Battle", theme="Crypt",  palette="CryptT1",
         stat_version=2, return_duration=3.0, builder=build_battle_crypt),
    dict(room_id="elite_001",  category="Elite",  theme="Forest", palette="ForestT1",
         stat_version=3, return_duration=3.0, builder=build_elite_forest),
    dict(room_id="event_001",  category="Event",  theme="Forest", palette="ForestT1",
         stat_version=3, return_duration=2.0, builder=build_event_forest),
    dict(room_id="event_002",  category="Event",  theme="Cave",   palette="CaveT1",
         stat_version=2, return_duration=2.0, builder=build_event_cave),
    dict(room_id="shop_001",   category="Shop",   theme="Town",   palette="TownT1",
         stat_version=2, return_duration=2.0, builder=build_shop_town),
    dict(room_id="start_001",  category="Start",  theme="Forest", palette="ForestT1",
         stat_version=3, return_duration=2.0, builder=build_start_forest),
    dict(room_id="boss_001",   category="Boss",   theme="Castle", palette="CastleT1",
         stat_version=2, return_duration=4.0, builder=build_boss_castle),
]

# ──────────────────────────────────────────────────────────────
# 실행
# ──────────────────────────────────────────────────────────────

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    json_path = os.path.join(root, "Assets", "Abyss", "Resources", "STAGEDATA_MAP.json")
    csv_path  = r"C:/Users/u/Desktop/STAGEDATA_MAP.csv"

    rooms_out = []
    for spec in ROOMS:
        grid, max_active = spec["builder"]()
        scatter_range = 18.0 if spec["category"] == "Boss" else 15.0
        rooms_out.append({
            "room_id": spec["room_id"],
            "category": spec["category"],
            "theme": spec["theme"],
            "palette": spec["palette"],
            "width": GRID,
            "height": GRID,
            "grid_csv": grid_to_csv(grid),
            "layout_rule": "",
            "entrance": "Scatter",
            "scatter_range": scatter_range,
            "return_duration": spec["return_duration"],
            "stat_version": spec["stat_version"],
            "max_active_spawners": max_active,
        })

    # JSON
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump({"rooms": rooms_out}, f, ensure_ascii=False, indent=2)
    print(f"[OK] {json_path}")

    # CSV (UTF-8 with BOM)
    header = [
        "room_id","category","theme","palette","width","height","grid_csv",
        "layout_rule","entrance","scatter_range","return_duration",
        "max_active_spawners","stat_version",
    ]
    lines = [",".join(header)]
    for r in rooms_out:
        # grid_csv에 ;와 ,가 들어있으므로 따옴표로 감싼다
        row = [
            r["room_id"], r["category"], r["theme"], r["palette"],
            str(r["width"]), str(r["height"]),
            '"' + r["grid_csv"] + '"',
            r["layout_rule"], r["entrance"],
            str(r["scatter_range"]), str(r["return_duration"]),
            str(r["max_active_spawners"]), str(r["stat_version"]),
        ]
        lines.append(",".join(row))
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
        f.write("\n".join(lines) + "\n")
    print(f"[OK] {csv_path}")

if __name__ == "__main__":
    main()
