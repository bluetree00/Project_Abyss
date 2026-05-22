"""
랠릭페어리 PPT 생성기 v0.5 — 2부
1부(PART1) 파일을 로드해 나머지 섹션을 추가한 뒤 최종 파일로 저장
"""

from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN

# ─── 컬러 팔레트 ───────────────────────────────────────────────
BG_DARK  = RGBColor(0x0D, 0x11, 0x1A)
BG_CARD  = RGBColor(0x16, 0x1B, 0x2E)
BG_CARD2 = RGBColor(0x1E, 0x24, 0x3A)
GOLD     = RGBColor(0xC9, 0xA8, 0x4C)
GOLD_LIGHT = RGBColor(0xE8, 0xD0, 0x8A)
PURPLE   = RGBColor(0x6B, 0x3F, 0x8E)
TEAL     = RGBColor(0x4A, 0xC2, 0xB8)
WHITE    = RGBColor(0xFF, 0xFF, 0xFF)
GRAY_LIGHT = RGBColor(0xB8, 0xBE, 0xCC)
GRAY_MID   = RGBColor(0x72, 0x7A, 0x8E)
GRAY_DARK  = RGBColor(0x2E, 0x36, 0x4A)
ORANGE   = RGBColor(0xFF, 0x70, 0x20)
RED_MID  = RGBColor(0xCC, 0x44, 0x44)
AMBER    = RGBColor(0xFF, 0xB8, 0x40)

# ─── 헬퍼 함수 (1부와 동일) ────────────────────────────────────

def set_slide_bg(slide, color):
    fill = slide.background.fill
    fill.solid()
    fill.fore_color.rgb = color

def add_rect(slide, left, top, width, height, fill_color=None, line_color=None, line_width=None):
    shape = slide.shapes.add_shape(1, Inches(left), Inches(top), Inches(width), Inches(height))
    if fill_color:
        shape.fill.solid()
        shape.fill.fore_color.rgb = fill_color
    else:
        shape.fill.background()
    if line_color:
        shape.line.color.rgb = line_color
        if line_width:
            shape.line.width = line_width
    else:
        shape.line.fill.background()
    return shape

def add_text_box(slide, text, left, top, width, height,
                 font_size=14, bold=False, color=WHITE, align=PP_ALIGN.LEFT,
                 font_name="맑은 고딕", wrap=True):
    txBox = slide.shapes.add_textbox(Inches(left), Inches(top), Inches(width), Inches(height))
    txBox.word_wrap = wrap
    tf = txBox.text_frame
    tf.word_wrap = wrap
    p = tf.paragraphs[0]
    p.alignment = align
    run = p.add_run()
    run.text = text
    run.font.size = Pt(font_size)
    run.font.bold = bold
    run.font.color.rgb = color
    run.font.name = font_name
    return txBox

def add_slide_title(slide, title, subtitle=None):
    add_rect(slide, 0, 0, 13.33, 0.06, fill_color=GOLD)
    add_text_box(slide, title, 0.5, 0.15, 12.3, 0.6,
                 font_size=26, bold=True, color=GOLD, align=PP_ALIGN.LEFT)
    add_rect(slide, 0.5, 0.82, 12.3, 0.03, fill_color=GRAY_DARK)
    if subtitle:
        add_text_box(slide, subtitle, 0.5, 0.88, 12.3, 0.3,
                     font_size=12, color=GRAY_LIGHT, align=PP_ALIGN.LEFT)

def add_card(slide, left, top, width, height, title=None, title_color=GOLD):
    add_rect(slide, left, top, width, height, fill_color=BG_CARD,
             line_color=GRAY_DARK, line_width=15000)
    if title:
        add_rect(slide, left, top, width, 0.38, fill_color=BG_CARD2)
        add_text_box(slide, title, left + 0.15, top + 0.06, width - 0.3, 0.28,
                     font_size=12, bold=True, color=title_color)

def add_table_rows(slide, headers, rows, left, top, col_widths,
                   row_height=0.32, header_color=GOLD, header_bg=BG_CARD2,
                   row_colors=None):
    x = left
    for h, w in zip(headers, col_widths):
        add_rect(slide, x, top, w, row_height, fill_color=header_bg,
                 line_color=GRAY_DARK, line_width=10000)
        add_text_box(slide, h, x + 0.08, top + 0.04, w - 0.16, row_height - 0.08,
                     font_size=10, bold=True, color=header_color, align=PP_ALIGN.CENTER)
        x += w
    for ri, row in enumerate(rows):
        y = top + (ri + 1) * row_height
        bg = BG_CARD if ri % 2 == 0 else BG_CARD2
        if row_colors and ri < len(row_colors) and row_colors[ri]:
            bg = row_colors[ri]
        x = left
        for ci, (cell, w) in enumerate(zip(row, col_widths)):
            add_rect(slide, x, y, w, row_height, fill_color=bg,
                     line_color=GRAY_DARK, line_width=8000)
            add_text_box(slide, str(cell), x + 0.08, y + 0.04, w - 0.16, row_height - 0.08,
                         font_size=9.5, color=WHITE if ci == 0 else GRAY_LIGHT,
                         align=PP_ALIGN.CENTER)
            x += w

def add_section_divider(prs, title, subtitle=""):
    slide_layout = prs.slide_layouts[6]
    slide = prs.slides.add_slide(slide_layout)
    set_slide_bg(slide, BG_DARK)
    add_rect(slide, 1.5, 3.2, 10.3, 0.04, fill_color=GOLD)
    add_rect(slide, 1.5, 4.55, 10.3, 0.04, fill_color=GRAY_DARK)
    add_text_box(slide, title, 0, 3.3, 13.33, 1.0,
                 font_size=40, bold=True, color=WHITE, align=PP_ALIGN.CENTER)
    if subtitle:
        add_text_box(slide, subtitle, 0, 4.35, 13.33, 0.5,
                     font_size=16, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
    return slide

def new_slide(prs):
    slide_layout = prs.slide_layouts[6]
    slide = prs.slides.add_slide(slide_layout)
    set_slide_bg(slide, BG_DARK)
    return slide

# ─── 1부 파일 로드 ─────────────────────────────────────────────
PART1 = r"c:/Users/u/Documents/GitHub/Project_Abyss/Assets/Abyss/Docs/RelicFairy_Proposal_PART1.pptx"
prs = Presentation(PART1)

# ══════════════════════════════════════════════════════════════
# 07 — 보스 봉인 시스템
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "07  보스 봉인 시스템 (Boss Seal System)")

add_card(slide, 0.4, 1.05, 12.5, 1.1, "핵심 컨셉")
add_text_box(slide,
    "\"균열이 충분히 쌓이면 봉인은 스스로 파열된다.\"\n"
    "보스는 매 런 조우할 때마다 균열이 누적된다. 리치는 봉인 해제가 아닌 부패 변이 수식어 해금의 마일스톤.",
    0.6, 1.35, 12.1, 0.65, font_size=10.5, color=GOLD_LIGHT)

# 4단계 봉인 흐름
seal_stages = [
    ("봉인 상태",      GRAY_MID, "1차 조우",              "Phase 1만",                   "봉인된 기억 보장 + 잔재"),
    ("봉인 균열 예고", TEAL,     "2차 조우",              "Phase 2 예고 (10~20초 노출)",  "봉인된 기억 보장 + 잔재"),
    ("완전 해방",      GOLD,     "3차+ 조우",             "Phase 1 + Phase 2 완전 해금",  "유물 코어 ×1"),
    ("부패 변이",      PURPLE,   "리치 격파 이후 재도전", "Phase 1+2 + 변이 수식어",      "잔류 각인 ×3~5"),
]

for i, (stage, c, cond, phase, reward) in enumerate(seal_stages):
    x = 0.4 + i * 3.2
    add_rect(slide, x, 2.3, 3.05, 2.8, fill_color=BG_CARD, line_color=c, line_width=18000)
    add_rect(slide, x, 2.3, 3.05, 0.45, fill_color=BG_CARD2)
    add_text_box(slide, stage, x, 2.33, 3.05, 0.4, font_size=12, bold=True, color=c, align=PP_ALIGN.CENTER)
    add_text_box(slide, "조건", x + 0.15, 2.83, 0.55, 0.22, font_size=8, color=GRAY_MID, bold=True)
    add_text_box(slide, cond, x + 0.15, 3.08, 2.75, 0.3, font_size=9.5, color=WHITE)
    add_rect(slide, x + 0.15, 3.42, 2.75, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "페이즈", x + 0.15, 3.5, 0.7, 0.22, font_size=8, color=GRAY_MID, bold=True)
    add_text_box(slide, phase, x + 0.15, 3.75, 2.75, 0.32, font_size=9.5, color=c)
    add_rect(slide, x + 0.15, 4.12, 2.75, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "보상", x + 0.15, 4.2, 0.55, 0.22, font_size=8, color=GOLD, bold=True)
    add_text_box(slide, reward, x + 0.15, 4.45, 2.75, 0.3, font_size=9.5, color=GOLD_LIGHT)
    if i < 3:
        add_text_box(slide, "→", x + 3.07, 3.55, 0.2, 0.4, font_size=18, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# 균열 예고 노출 시간
add_card(slide, 0.4, 5.28, 6.2, 2.0, "균열 예고 — 조우 횟수별 Phase 2 노출 시간")
crack_rows = [
    ["1차",  "없음",    "HP 0 → 봉인 파열 연출 → 퇴각"],
    ["2차",  "10~20초", "Phase 2 형태 노출 후 재봉인 — 다음 조우 예고"],
    ["3차+", "완전 해방","Phase 1 + Phase 2 완전 해금"],
]
add_table_rows(slide, ["조우","Phase 2 노출","연출"], crack_rows, 0.6, 5.72, [0.7, 1.2, 4.1], row_height=0.38)

# 리치 격파 시 흐름
add_card(slide, 6.85, 5.28, 6.1, 2.0, "리치 격파 — 부패 변이 해금 흐름")
add_text_box(slide,
    "리치 사망 (1차)\n"
    "  → 봉인 파열 연출 후 퇴각\n"
    "  → 부패 변이 수식어 해금\n"
    "  → 유물 코어 ×1 지급\n"
    "  → 멀린: \"부패가 남긴 흔적들이 변이를 일으키고 있어.\"\n"
    "  → 이후 런 리치 재조우 시 변이 수식어 적용",
    7.05, 5.72, 5.7, 1.4, font_size=9.5, color=GRAY_LIGHT)

# 부패 변이 수식어 풀
add_card(slide, 0.4, 7.32, 12.5, 0.0)  # 페이지 하단 한 줄 메모
# 실제론 슬라이드 밖이라 생략, 아래에 별도 슬라이드는 안 만들고 여기 설명만 넣음

# 부패 변이 수식어 — 별도 슬라이드
slide = new_slide(prs)
add_slide_title(slide, "07  부패 변이 수식어 & 사망 카운터 시스템")

add_card(slide, 0.4, 1.05, 7.8, 5.9, "부패 변이 수식어 풀 (완전 해방 보스 재도전 시 랜덤 1개)")
mutation_rows = [
    ["공격", "분노의 부패",   "공격력 +40%, 공격 속도 +20%"],
    ["공격", "관통의 저주",   "모든 공격 방어 무시"],
    ["공격", "연쇄의 광기",   "피격 시 주변에 연쇄 피해"],
    ["방어", "철갑의 잠식",   "받는 피해 -40%, HP 회복 불가"],
    ["방어", "재생의 부패",   "초당 최대HP 0.3% 회복"],
    ["환경", "독안개 확산",   "전장 외곽에서 독 안개 서서히 수축"],
    ["환경", "중력 왜곡",     "플레이어 대시/점프 거리 -30%"],
    ["특수", "기억 역류",     "이번 런 첫 획득 아이템 효과를 보스가 사용"],
]
add_table_rows(slide, ["분류", "수식어명", "효과"], mutation_rows, 0.6, 1.55, [1.1, 2.2, 4.3], row_height=0.52,
               row_colors=[None, None, None, RGBColor(0x1A,0x10,0x14), RGBColor(0x1A,0x10,0x14),
                            RGBColor(0x0A,0x18,0x14), RGBColor(0x0A,0x18,0x14), None])

# 사망 카운터 + 성장 시간대
add_card(slide, 8.45, 1.05, 4.4, 2.5, "사망 카운터 시스템")
add_text_box(slide, "\"고통도 기억이다.\"", 8.65, 1.5, 4.0, 0.35, font_size=11, color=GOLD_LIGHT)
add_text_box(slide,
    "5회 사망 누적 시 봉인된 기억 ×1 보장 지급.\n"
    "카운터 초기화.\n\n"
    "사망: 0 → 1 → 2 → 3 → 4 → [5] → 기억 +1\n\n"
    "멀린: \"고통도 기억이다. 받아라.\"",
    8.65, 1.9, 4.0, 1.5, font_size=10, color=GRAY_LIGHT)

add_card(slide, 8.45, 3.75, 4.4, 3.2, "성장 시간대 설계")
timeline = [
    ("1~5런",    TEAL,   "서약 생존 안정화\n챕터2 첫 도달"),
    ("5~15런",   GOLD,   "유물 해방 1~2개\n서약 공격/이동 해금"),
    ("15~30런",  PURPLE, "완전 해방 보스 도전\n(3차+ 조우, 코어 획득 시작)"),
    ("리치 격파", RED_MID,"부패 변이 해금 + 코어 ×1\n서약 5~6번 해금"),
    ("리치 이후", AMBER,  "부패 변이 보스 도전\n잔류 각인 → 코어"),
]
for i, (span, c, goal) in enumerate(timeline):
    y = 4.2 + i * 0.52
    add_rect(slide, 8.65, y, 1.4, 0.42, fill_color=c)
    add_text_box(slide, span, 8.65, y + 0.06, 1.4, 0.3, font_size=8.5, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, goal, 10.15, y + 0.06, 2.55, 0.3, font_size=8.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 08 — 챕터 & 레벨 디자인
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "08  챕터 & 레벨 디자인", "Chapter & Level Design")

# 챕터 개요
slide = new_slide(prs)
add_slide_title(slide, "08  챕터 구성 개요")

add_card(slide, 0.4, 1.05, 12.5, 2.0, "챕터 개요")
headers4 = ["챕터", "배경", "보스", "몬스터 배수", "보상 배수", "장벽 테마"]
cw4 = [0.9, 2.2, 2.8, 1.4, 1.4, 2.1]
rows4 = [
    ["CH.1", "부패의 숲",    "그린 나이트",           "×1.0", "×1.0", "흑수 (검은 물)"],
    ["CH.2", "용암 대지",    "이 드레이그 고흐",       "×1.5", "×1.4", "용암·화산재"],
    ["CH.3", "잠식된 성채",  "모드레드",              "×2.2", "×1.8", "마법 안개"],
    ["CH.4", "암흑대지",     "리치 (멀린의 육체)",     "×3.0", "×2.5", "공허의 균열"],
]
add_table_rows(slide, headers4, rows4, 0.55, 1.5, cw4, row_height=0.47,
               row_colors=[None, None, None, RGBColor(0x1A, 0x10, 0x24)])

# 존 컨셉 분포
add_card(slide, 0.4, 3.2, 5.8, 4.0, "존 컨셉 분포 (26존, 런당 12존 방문)")
zone_rows = [
    ["시작방",  "1 (고정)", "1",  "준비 공간, 전투 없음"],
    ["전투방",  "14",       "~6", "일반 전투, 아이템 보상"],
    ["엘리트방","5",        "~2", "강화 몬스터, 높은 보상"],
    ["상점방",  "3",        "~1", "인게임 골드로 아이템 구매"],
    ["이벤트방","2",        "~1", "선택지, 서사 단편, 특수 보상"],
    ["보스방",  "1 (고정)", "1",  "챕터 보스"],
]
add_table_rows(slide, ["컨셉","전체 수","런당 방문","내용"], zone_rows, 0.6, 3.65, [1.4, 1.0, 1.2, 2.0], row_height=0.44)

# 부패 압박 시스템
add_card(slide, 6.5, 3.2, 3.0, 4.0, "부패 압박 시스템")
add_text_box(slide,
    "클리어하지 않고 지나친 존은\n챕터 보스를 강화한다.",
    6.7, 3.65, 2.6, 0.55, font_size=9.5, color=GRAY_LIGHT)
pressure_rows = [
    ["0",    "기본 상태"],
    ["1~3",  "보스 HP +5%/존"],
    ["4~7",  "+5%/존 + 패턴 1개"],
    ["8+",   "+5%/존 + 패턴 2개\n+ 필드 부패 이펙트"],
]
add_table_rows(slide, ["미클리어 존","보스 강화"], pressure_rows, 6.7, 4.25, [1.3, 1.6], row_height=0.45)
add_rect(slide, 6.7, 6.29, 2.6, 0.5, fill_color=RGBColor(0x1A, 0x10, 0x14), line_color=RED_MID, line_width=10000)
add_text_box(slide, "탐험 FOMO를 자연스럽게 유도.\n최단 경로만 달리면 보스가 강해진다.", 6.85, 6.35, 2.35, 0.38, font_size=8.5, color=GRAY_LIGHT)

# 단축로 & 히든 보상
add_card(slide, 9.7, 3.2, 3.2, 4.0, "히든 보상 & 단축로")
add_text_box(slide, "히든 보상", 9.9, 3.65, 2.8, 0.28, font_size=10, bold=True, color=GOLD)
add_text_box(slide,
    "26존 중 약 8존에 숨겨진 보상\n"
    "재화 캐시 / 소형 아이템 / 서사 단편\n"
    "전투 중 지형 탐색으로 발견",
    9.9, 3.98, 2.8, 0.7, font_size=9, color=GRAY_LIGHT)
add_rect(slide, 9.9, 4.74, 2.8, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "단축로 (Shortcut)", 9.9, 4.82, 2.8, 0.28, font_size=10, bold=True, color=TEAL)
add_text_box(slide,
    "인접 존 두 곳이 모두 Cleared 시\n"
    "두 존을 직접 연결하는 통로 개방\n"
    "상점 재방문 / 체크포인트 복귀 용도",
    9.9, 5.15, 2.8, 0.7, font_size=9, color=GRAY_LIGHT)
add_rect(slide, 9.9, 5.9, 2.8, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "사망 & 다시하기 규칙", 9.9, 5.98, 2.8, 0.28, font_size=10, bold=True, color=AMBER)
add_text_box(slide,
    "저장: 노드 맵에서 방 선택 순간\n"
    "재진입: 선택한 방 입구부터\n"
    "이미 클리어한 방 보상은 미유지",
    9.9, 6.3, 2.8, 0.65, font_size=8.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# 08 — 26존 단일 세계 맵
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "08  단일 세계 맵 시스템 — 26존 레이어 구조")

add_card(slide, 0.4, 1.05, 5.5, 5.9, "26존 레이어 구조")
add_text_box(slide,
    "\"맵 전체가 처음부터 존재한다.\n다만, 길이 막혀 있을 뿐이다.\"\n\n"
    "챕터 = 하나의 거대한 물리 세계.\n"
    "부패 물질(흑수·용암·안개·공허)이 세계를\n"
    "뒤덮고 있으며, 존을 클리어·선택하면\n"
    "그 구역의 장벽이 물러난다.\n"
    "플레이어는 직접 걸어서 이동.",
    0.6, 1.55, 5.1, 2.0, font_size=10, color=GRAY_LIGHT)

zone_layer_rows = [
    ["L12 (보스)", "Z26",   "1", "보스방 — 고정"],
    ["L11",        "Z25",   "1", "보스 전 준비존"],
    ["L10",        "Z23~24","2", ""],
    ["L9",         "Z21~22","2", ""],
    ["L8",         "Z18~20","3", ""],
    ["L7",         "Z15~17","3", ""],
    ["L6",         "Z12~14","3", ""],
    ["L5",         "Z9~11", "3", ""],
    ["L4",         "Z6~8",  "3", ""],
    ["L3",         "Z4~5",  "2", ""],
    ["L2",         "Z2~3",  "2", ""],
    ["L1 (시작)",  "Z1",    "1", "시작방 — 고정"],
]
add_table_rows(slide, ["레이어","존","수","비고"], zone_layer_rows, 0.6, 3.65, [1.6, 1.2, 0.5, 2.1], row_height=0.3,
               row_colors=[RGBColor(0x18,0x10,0x28), None, None, None, None, None, None, None, None, None, None, RGBColor(0x10,0x18,0x22)])

# 이동 방식 비교
add_card(slide, 6.2, 1.05, 6.8, 2.8, "기존 타일맵 vs 단일 세계 맵")
compare_rows = [
    ["세계 존재", "방 선택 시 인스턴스 생성", "처음부터 물리적으로 존재"],
    ["이동",     "타일 클릭 → 씬 로드",     "플레이어가 지형을 걸어서 이동"],
    ["장벽",     "UI 상 봉인 표시",          "장벽 물질이 실제 지형을 덮음"],
    ["탐험감",   "없음 (추상적 노드)",        "있음 (발견·탐험 감각)"],
]
add_table_rows(slide, ["항목","구 방식 (타일맵)","신 방식 (단일 세계)"], compare_rows, 6.4, 1.55, [1.5, 2.45, 2.65], row_height=0.42)

# 존 상태 정의
add_card(slide, 6.2, 4.05, 6.8, 2.9, "존 상태 정의")
state_rows = [
    ["Sealed",    "봉인됨",        "장벽이 지형 덮음, 실루엣만"],
    ["Available", "선택 가능",     "장벽 일부 후퇴, 컨셉 아이콘"],
    ["Selected",  "개방 중",       "장벽 완전 후퇴 애니메이션"],
    ["Active",    "현재 플레이",   "존 구역 하이라이트"],
    ["Cleared",   "클리어 완료",   "체크포인트 활성화"],
]
add_table_rows(slide, ["상태","설명","시각적 표현"], state_rows, 6.4, 4.5, [1.5, 1.8, 3.3], row_height=0.42)

# ══════════════════════════════════════════════════════════════
# 08 — 보스 상세 설계 (봉인 시스템 연동)
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "08  보스 상세 설계 — 봉인 시스템 연동")

boss_list = [
    {
        "name": "그린 나이트",
        "chapter": "CH.1 — 부패의 숲",
        "color": TEAL,
        "hp_sealed": "기준 HP ×1.0",
        "ph2_trigger": "HP 50% 이하 (완전 해방 시)",
        "patterns": ["근접 강타", "가시 투척", "덩굴 함정", "재생 시작 (HP 50% 이하, 초당 0.5%)", "Phase2: 화염 피해로 재생 중단"],
        "lore": "숲의 수호자가 부패로 거대화.\n가웨인 유물 사용 시 특별 대사.",
    },
    {
        "name": "이 드레이그 고흐",
        "chapter": "CH.2 — 용암 대지",
        "color": ORANGE,
        "hp_sealed": "기준 HP ×1.5",
        "ph2_trigger": "HP 40% 이하 (완전 해방 시)",
        "patterns": ["화염 브레스", "꼬리 휩쓸기", "낙하 충격", "Phase2: 불꽃·어둠 형체로 분열"],
        "lore": "멀린이 예언한 존재가 부패의 도구로.\n격파 후 멀린 자책 대사.",
    },
    {
        "name": "모드레드",
        "chapter": "CH.3 — 잠식된 성채",
        "color": RED_MID,
        "hp_sealed": "기준 HP ×2.2",
        "ph2_trigger": "HP 40% 이하 (완전 해방 시)",
        "patterns": ["검술 3연격", "그림자 분신", "광역 참격", "Phase2: 분신 2체 추가 소환"],
        "lore": "격파 직전 의식 회복.\n\"그 자는 모든 것을 복사했어...\"",
    },
    {
        "name": "리치 (멀린의 육체)",
        "chapter": "CH.4 — 암흑대지",
        "color": PURPLE,
        "hp_sealed": "기준 HP ×3.0",
        "ph2_trigger": "HP 40% 이하 (리치 격파 후 재조우)",
        "patterns": ["영웅 복사체 연속전 (Ph0)", "마법 투사체", "영역 봉인", "Phase2: 전장 어둠화 + 복사체 재소환"],
        "lore": "복사체 연속전 → 멀린 마지막 메시지.\n격파 후 봉인 파열 + 코어 ×4 일괄.",
    },
]

for i, boss in enumerate(boss_list):
    x = 0.35 + i * 3.27
    c = boss["color"]
    add_rect(slide, x, 1.1, 3.1, 6.0, fill_color=BG_CARD, line_color=c, line_width=15000)
    add_rect(slide, x, 1.1, 3.1, 0.48, fill_color=BG_CARD2)
    add_text_box(slide, boss["chapter"], x, 1.13, 3.1, 0.22, font_size=8, color=GRAY_MID, align=PP_ALIGN.CENTER)
    add_text_box(slide, boss["name"], x, 1.3, 3.1, 0.28, font_size=13, bold=True, color=c, align=PP_ALIGN.CENTER)

    add_text_box(slide, f"봉인 HP: {boss['hp_sealed']}", x + 0.15, 1.65, 2.8, 0.26, font_size=9, color=WHITE)
    add_text_box(slide, f"Phase2 발동: {boss['ph2_trigger']}", x + 0.15, 1.93, 2.8, 0.28, font_size=8.5, color=TEAL)
    add_rect(slide, x + 0.15, 2.25, 2.8, 0.02, fill_color=GRAY_DARK)

    add_text_box(slide, "공격 패턴", x + 0.15, 2.33, 2.0, 0.22, font_size=8, color=GRAY_MID, bold=True)
    for j, pat in enumerate(boss["patterns"]):
        col = c if "Phase2" in pat else GRAY_LIGHT
        add_text_box(slide, "▸ " + pat, x + 0.15, 2.58 + j * 0.36, 2.8, 0.3, font_size=8, color=col)

    add_rect(slide, x + 0.15, 4.1, 2.8, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, boss["lore"], x + 0.15, 4.18, 2.8, 0.9, font_size=8.5, color=GRAY_LIGHT)

    # 봉인 상태/해방 보상
    add_rect(slide, x + 0.15, 5.15, 2.8, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "봉인 처치 보상", x + 0.15, 5.23, 2.0, 0.22, font_size=8, color=GOLD, bold=True)
    add_text_box(slide, "봉인된 기억 보장 + 잔재", x + 0.15, 5.48, 2.8, 0.26, font_size=8.5, color=GOLD_LIGHT)
    add_text_box(slide, "완전 해방 처치: 유물 코어 ×1", x + 0.15, 5.76, 2.8, 0.26, font_size=8.5, color=GOLD)

# ══════════════════════════════════════════════════════════════
# 08 — 엘리트 진화 시스템
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "08  엘리트 진화 시스템 & 레벨 디자인 원칙")

add_card(slide, 0.4, 1.05, 6.5, 5.9, "엘리트 진화 시스템 (8.4.1)")
add_text_box(slide,
    "일반 몬스터가 스폰 시 일정 확률로 엘리트 진화.\n"
    "지정 엘리트(MAP_TOKEN Me1)와 구분되는 별개 시스템.",
    0.6, 1.5, 6.1, 0.55, font_size=10, color=GRAY_LIGHT)

elite_rows = [
    ["랜덤 확률",    "L1~4: 8% / L5~8: 12% / L9+: 18%"],
    ["지정 엘리트",  "MAP_TOKEN Me1 → 100% 엘리트 스폰"],
    ["시각 표현",    "크기 1.2배, 부패 속성 아우라 이펙트"],
    ["슈퍼아머",     "피격 경직 없음 — 맞으면서도 공격 지속"],
    ["속성 부여",    "스폰 시 랜덤 속성 1개 (Water/Fire/Grass/Earth/Lightning)"],
    ["스탯 강화",    "HP ×1.5 / 공격력 ×1.3"],
    ["넉백 면역",    "플레이어 → 몬스터 넉백 무효"],
    ["추가 보상",    "처치 시 골드 +50%, 아이템 드롭 가중치 +20%"],
]
add_table_rows(slide, ["항목", "내용"], elite_rows, 0.6, 2.1, [2.2, 4.1], row_height=0.48)

# 6대 아레나 패턴
add_card(slide, 7.15, 1.05, 5.8, 5.9, "6대 아레나 패턴 & 레이어별 진행")
add_text_box(slide, "Hades 6대 아레나 패턴", 7.35, 1.52, 5.4, 0.28, font_size=11, bold=True, color=GOLD)
arena_rows = [
    ["PINCER",    "협공형",  "플레이어 측면 양방 진입"],
    ["RING",      "포위형",  "중앙 → 사방 등장"],
    ["CORRIDOR",  "선형형",  "좁은 통로 정면 압박"],
    ["ISLAND",    "도서형",  "발판, 낙사 위협"],
    ["TIERED",    "고저형",  "고·저지대 혼재"],
    ["LABYRINTH", "미로형",  "시야 분산, 기습"],
]
add_table_rows(slide, ["코드","유형","핵심"], arena_rows, 7.35, 1.85, [1.4, 1.1, 3.1], row_height=0.38)

add_rect(slide, 7.35, 4.22, 5.4, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "Hades 3대 설계 규칙", 7.35, 4.3, 5.4, 0.28, font_size=11, bold=True, color=TEAL)
rules = [
    "입장 안전 1초: 진입 지점 반경 5m 항상 비워둠",
    "탈출로 2개 이상: 전진·후퇴 경로 최소 2개",
    "엄폐물 4~6개: 방당 바위/나무/잔해",
]
for i, r in enumerate(rules):
    add_text_box(slide, "▸ " + r, 7.35, 4.62 + i * 0.42, 5.4, 0.36, font_size=9.5, color=WHITE)

add_rect(slide, 7.35, 5.92, 5.4, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "Dead Cells 수직성 원칙", 7.35, 6.0, 5.4, 0.28, font_size=11, bold=True, color=PURPLE)
add_text_box(slide,
    "히든 보상은 수직 이동(등반·낙하)으로 발견.\n단축로는 수직 낙하 통로로 구현.",
    7.35, 6.3, 5.4, 0.45, font_size=9.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 09 — 비주얼 & 톤
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "09  비주얼 & 톤 방향", "Visual & Tone Direction")

slide = new_slide(prs)
add_slide_title(slide, "09  비주얼 & 톤 — 아트 방향 & 챕터 컬러")

add_card(slide, 0.4, 1.05, 12.5, 1.2, "아트 방향성")
add_text_box(slide,
    "\"한때 빛났던 동화의 페이지가 잠식되고 부패한 세계. 귀엽거나 밝지 않다. 어둡고 잔인하지만 아름다운 잔재가 남아있다.\"",
    0.6, 1.35, 12.1, 0.55, font_size=12, color=GOLD_LIGHT)

chapter_visuals = [
    ("CH.1\n부패의 숲",   RGBColor(0x2A,0x5C,0x2A), RGBColor(0x4A,0x1A,0x5C), "탁한 초록 + 검은 보라\n생명이 부패한 숲"),
    ("CH.2\n용암 대지",   RGBColor(0x8B,0x2A,0x0A), RGBColor(0x1A,0x0A,0x0A), "붉은 주황 + 검정\n뜨거운 침묵의 지형"),
    ("CH.3\n잠식된 성채", RGBColor(0x2A,0x2A,0x4A), RGBColor(0x0A,0x0A,0x1A), "차가운 회색 + 남색\n한때 영웅의 성채"),
    ("CH.4\n암흑대지",    RGBColor(0x1A,0x0A,0x2A), RGBColor(0x08,0x04,0x10), "순수한 흑 + 희미한 보라\n빛이 없는 근원"),
]
for i, (ch, c1, c2, desc) in enumerate(chapter_visuals):
    x = 0.4 + i * 3.27
    add_rect(slide, x, 2.5, 3.1, 2.0, fill_color=c2, line_color=c1, line_width=15000)
    add_rect(slide, x, 2.5, 1.55, 2.0, fill_color=c1)
    add_text_box(slide, ch, x, 4.6, 3.1, 0.5, font_size=11, bold=True, color=WHITE, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, x + 0.1, 5.15, 2.9, 0.7, font_size=9.5, color=GRAY_LIGHT)

# UI/UX 방향
add_card(slide, 0.4, 5.88, 12.5, 1.52, "UI/UX 방향")
uiux_data = [
    ("HUD",         "최소화 — HP 바, 메커닉 게이지, 스킬 쿨다운만"),
    ("메커닉 게이지","영웅별 고유 시각 (신성=원형 빛, 콤보=선형 스택, 태양=시계)"),
    ("아이템 그리드","퍼즐 보드 중심. 배치 시 빛 연출"),
    ("멀린의 서약", "고딕 책 스타일 UI. 좌/우 선택이 책 페이지처럼"),
]
for i, (k, v) in enumerate(uiux_data):
    y = 6.2 + i * 0.28
    add_text_box(slide, k + ":", 0.6, y, 1.7, 0.24, font_size=9.5, bold=True, color=GOLD)
    add_text_box(slide, v, 2.4, y, 10.2, 0.24, font_size=9.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 10 — 전투 연출 & 게임 느낌
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "10  전투 연출 & 게임 느낌", "Combat Feel & Presentation")

slide = new_slide(prs)
add_slide_title(slide, "10  전투 연출 — 히트감 & 카메라 & 물리")

# 왼쪽 컬럼
add_card(slide, 0.4, 1.05, 6.2, 2.8, "마지막 몬스터 슬로우모션")
add_text_box(slide,
    "전투 방에서 생존 몬스터가 1마리가 되는 순간:\n\n"
    "▸ Time.timeScale → 0.3배 감속 (슬로우모션)\n"
    "▸ Cinemachine 카메라 → 마지막 몬스터 추적\n"
    "▸ 처치 시 timeScale 1.0으로 복원\n"
    "▸ 웨이브 모드 / 레거시 모드 모두 적용\n"
    "▸ 보스방은 별도 연출 처리",
    0.6, 1.55, 5.8, 2.1, font_size=10, color=GRAY_LIGHT)

add_card(slide, 0.4, 4.05, 6.2, 1.5, "아이템 획득 연출")
add_text_box(slide,
    "①  월드 스페이스 팝업 — 아이템명 + 등급 색상\n"
    "②  파티클 버스트 이펙트 (등급별 색상)\n"
    "③  Epic 이상: 카메라 줌인 0.5초",
    0.6, 4.55, 5.8, 0.9, font_size=10, color=GRAY_LIGHT)

add_card(slide, 0.4, 5.7, 6.2, 1.5, "공격 히트 이펙트")
add_text_box(slide,
    "몬스터 피격: 기존 혈흔 VFX\n"
    "벽/장애물 충돌: SparkOnContact (흰색~주황색 파티클)\n"
    "WeaponEffectHandler → 레이어 판별 후 파티클 스폰",
    0.6, 6.18, 5.8, 0.9, font_size=10, color=GRAY_LIGHT)

# 오른쪽 컬럼
add_card(slide, 6.85, 1.05, 6.1, 1.6, "공격 전진성 (Forward Momentum)")
add_text_box(slide,
    "공격 입력 시 타격 방향으로 짧게 전진\n"
    "Rigidbody.AddForce(forward × attackDashForce, Impulse)\n"
    "기본값: attackDashForce = 3.0 (무기 SO에서 조정)",
    7.05, 1.55, 5.7, 0.95, font_size=10, color=GRAY_LIGHT)

add_card(slide, 6.85, 2.8, 6.1, 1.6, "이동 중 공격 관성 (Attack Inertia)")
add_text_box(slide,
    "공격 진입 시 현재 MoveScale 기록\n"
    "0.25초에 걸쳐 MoveScale이 0으로 선형 감소\n"
    "공격 종료 후 MoveScale 1.0 복원",
    7.05, 3.3, 5.7, 0.9, font_size=10, color=GRAY_LIGHT)

add_card(slide, 6.85, 4.55, 6.1, 1.5, "회피로 장애물 통과")
add_text_box(slide,
    "구르기 진입: 레이어 → PlayerDodge\n"
    "Physics Matrix: PlayerDodge ↔ Obstacle = 충돌 없음\n"
    "구르기 종료 시 Player 레이어로 복원\n"
    "방 외곽 벽(Wall)은 충돌 유지",
    7.05, 5.05, 5.7, 0.9, font_size=10, color=GRAY_LIGHT)

add_card(slide, 6.85, 6.2, 6.1, 1.0, "사망 화면 시퀀스")
add_text_box(slide,
    "HP 0  →  ①블랙 페이드(0.5초)  →  ②카메라 클로즈업 줌인(2~3초)  →  ③ \"다시하기 / 포기\" UI",
    7.05, 6.55, 5.7, 0.5, font_size=9.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 11 — 튜토리얼 & 온보딩
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "11  튜토리얼 & 온보딩", "Tutorial & Onboarding")

slide = new_slide(prs)
add_slide_title(slide, "11  튜토리얼 & 온보딩 — 첫 런 플로우 & 로비")

add_card(slide, 0.4, 1.05, 5.8, 5.9, "첫 런 튜토리얼 플로우")
add_text_box(slide, "게임 최초 실행 시 자동 진입 (TutorialDone = 0)", 0.6, 1.5, 5.4, 0.3, font_size=10, color=GRAY_MID)
tutorial_steps = [
    (TEAL,   "스타트방 자동 진입",     "약한 초기 캐릭터 + 튜토리얼 기본 장비"),
    (WHITE,  "기본 조작 안내",         "가이드 텍스트 UI (이동/공격/회피/스킬)"),
    (RED_MID,"플레이어 사망",          "의도적 낮은 스펙 — 튜토리얼 관문"),
    (GOLD,   "멀린 컷씬 재생",         "\"이 유물들 중 하나를 골라라.\""),
    (TEAL,   "허브(로비) 진입",        "캐릭터 선택 + 장비 선택 활성화"),
    (WHITE,  "일반 런 시작",           "TutorialDone = 1 저장"),
]
for i, (c, title, desc) in enumerate(tutorial_steps):
    y = 1.95 + i * 0.8
    add_rect(slide, 0.6, y, 0.08, 0.55, fill_color=c)
    add_text_box(slide, title, 0.85, y + 0.02, 3.5, 0.28, font_size=11, bold=True, color=c)
    add_text_box(slide, desc, 0.85, y + 0.3, 4.8, 0.22, font_size=9.5, color=GRAY_LIGHT)
    if i < len(tutorial_steps) - 1:
        add_text_box(slide, "↓", 0.55, y + 0.6, 0.3, 0.2, font_size=11, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# 스타트방 구성
add_card(slide, 6.45, 1.05, 6.5, 2.5, "스타트방 구성 (Zone 1 — 모든 런 고정)")
start_rows = [
    ["확정 특별 아이템", "난이도 맞춤 Common~Rare 1개 확정 드롭"],
    ["더미/약한 몬스터", "소형 몬스터 1~2마리 또는 연습 더미"],
    ["멀린 환영 대사",   "스타트방 진입 시 한 줄 환영 + 진행 안내"],
    ["장비 확인 공간",   "스탯 패널 확인 가능, 무기 교체 기회"],
]
add_table_rows(slide, ["요소","내용"], start_rows, 6.65, 1.55, [2.2, 4.1], row_height=0.46)

# 가이드 텍스트 UI
add_card(slide, 6.45, 3.75, 6.5, 2.0, "가이드 텍스트 UI (튜토리얼 런에서만)")
guide_rows = [
    ["방 입장 직후",   "\"WASD로 이동 / 마우스 좌클릭으로 공격\""],
    ["첫 회피 유도",   "\"Shift로 구르기 — 장애물도 통과 가능\""],
    ["스킬 언락 후",   "\"Q / E 키로 스킬 사용\""],
    ["아이템 획득 후", "\"Tab 키로 아이템 그리드 열기 — 배치해야 효과 발동\""],
]
add_table_rows(slide, ["시점","텍스트"], guide_rows, 6.65, 4.22, [1.8, 4.5], row_height=0.36)

# 로비 전시대
add_card(slide, 6.45, 5.9, 6.5, 1.1, "로비 — 캐릭터 & 장비 전시대 (Hades 스타일)")
add_text_box(slide,
    "▸ 해금 영웅 유물 3D 회전 전시  |  \"연습\" 버튼: 샌드박스 방 즉시 진입\n"
    "▸ 보유 무기 3D 전시  |  영웅별 친화도 배지 (★★★)  |  최대 2슬롯 장비 선택",
    6.65, 6.28, 6.1, 0.55, font_size=9.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# 최종 슬라이드 — 총정리
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_rect(slide, 0, 0, 13.33, 0.08, fill_color=GOLD)
add_rect(slide, 0, 7.42, 13.33, 0.08, fill_color=GOLD)

add_text_box(slide, "RELIC FAIRY  —  핵심 요약  v0.5", 0, 0.55, 13.33, 0.7,
             font_size=30, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
add_rect(slide, 1.5, 1.32, 10.3, 0.03, fill_color=GRAY_DARK)

summary = [
    ("세계관",    TEAL,   "멀린 사망 → 리치(육체 탈취) → 모르가나(진짜 배후) / 모던 다크 동화 아서왕 전설"),
    ("영웅",      GOLD,   "5종 유물 (암시형 네이밍) / 고유 메커닉+패시브+스킬 / 해금형 순차 개방"),
    ("장비",      WHITE,  "모든 영웅 × 모든 장비 / 이중 효과 구조 / 드롭 가중치 조정"),
    ("서약",      TEAL,   "12종 × 3단계(기본→강화→진화) / 런 시작 선택 → 이벤트방 강화 → 보스 진화"),
    ("메타",      GOLD,   "재화 4종 / 멀린의 서약(6슬롯) / 유물 해방 / 유물 각성 / 보스 봉인 시스템"),
    ("세계맵",    PURPLE, "26존 단일 물리 세계 / 런당 12존 / 부패 압박 / 히든 보상 / 단축로"),
    ("연출",      AMBER,  "슬로우모션 / 전진성 / 관성 / 히트 이펙트 / 사망 화면 / 튜토리얼 플로우"),
]
for i, (cat, c, desc) in enumerate(summary):
    y = 1.45 + i * 0.82
    add_rect(slide, 0.5, y, 12.3, 0.7, fill_color=BG_CARD)
    add_rect(slide, 0.5, y, 0.06, 0.7, fill_color=c)
    add_text_box(slide, cat, 0.75, y + 0.1, 1.3, 0.32, font_size=13, bold=True, color=c)
    add_text_box(slide, desc, 2.2, y + 0.12, 10.4, 0.42, font_size=10.5, color=GRAY_LIGHT)

add_text_box(slide, "v0.5  |  2026.05.21  |  랠릭페어리 기획팀",
             0, 7.1, 13.33, 0.3, font_size=10, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# ─── 최종 저장 ─────────────────────────────────────────────────
OUTPUT = r"c:/Users/u/Documents/GitHub/Project_Abyss/Assets/Abyss/Docs/RelicFairy_Proposal.pptx"
prs.save(OUTPUT)
print("최종 저장 완료: " + OUTPUT)
print("전체 슬라이드 수: " + str(len(prs.slides)) + "장")
