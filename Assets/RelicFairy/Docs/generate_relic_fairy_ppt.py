"""
랠릭페어리 (Relic Fairy) - 게임 기획서 PPT 생성기 v0.5
"""

from pptx import Presentation
from pptx.util import Inches, Pt, Emu
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
import copy

# ─── 컬러 팔레트 ───────────────────────────────────────────────
BG_DARK       = RGBColor(0x0D, 0x11, 0x1A)
BG_CARD       = RGBColor(0x16, 0x1B, 0x2E)
BG_CARD2      = RGBColor(0x1E, 0x24, 0x3A)
GOLD          = RGBColor(0xC9, 0xA8, 0x4C)
GOLD_LIGHT    = RGBColor(0xE8, 0xD0, 0x8A)
PURPLE        = RGBColor(0x6B, 0x3F, 0x8E)
TEAL          = RGBColor(0x4A, 0xC2, 0xB8)
RED_DARK      = RGBColor(0x8B, 0x2A, 0x2A)
WHITE         = RGBColor(0xFF, 0xFF, 0xFF)
GRAY_LIGHT    = RGBColor(0xB8, 0xBE, 0xCC)
GRAY_MID      = RGBColor(0x72, 0x7A, 0x8E)
GRAY_DARK     = RGBColor(0x2E, 0x36, 0x4A)
ORANGE        = RGBColor(0xFF, 0x70, 0x20)
RED_MID       = RGBColor(0xCC, 0x44, 0x44)
AMBER         = RGBColor(0xFF, 0xB8, 0x40)

# ─── 헬퍼 함수 ────────────────────────────────────────────────

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

def add_bullet_list(slide, items, left, top, width, spacing=0.3,
                    font_size=12, color=GRAY_LIGHT, bullet="▸ "):
    for i, item in enumerate(items):
        add_text_box(slide, bullet + item, left, top + i * spacing, width, spacing,
                     font_size=font_size, color=color)

def add_table_rows(slide, headers, rows, left, top, col_widths,
                   row_height=0.32, header_color=GOLD, header_bg=BG_CARD2,
                   row_colors=None):
    x = left
    for i, (h, w) in enumerate(zip(headers, col_widths)):
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

# ─── PPT 생성 시작 ─────────────────────────────────────────────

prs = Presentation()
prs.slide_width  = Inches(13.33)
prs.slide_height = Inches(7.5)

# ══════════════════════════════════════════════════════════════
# 슬라이드 1 — 타이틀
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_rect(slide, 0, 0, 13.33, 7.5, fill_color=BG_DARK)
add_rect(slide, 0, 0, 13.33, 2.0, fill_color=RGBColor(0x10, 0x16, 0x26))
add_rect(slide, 0, 5.5, 13.33, 2.0, fill_color=RGBColor(0x10, 0x16, 0x26))
add_rect(slide, 0, 0, 13.33, 0.08, fill_color=GOLD)
add_rect(slide, 0, 7.42, 13.33, 0.08, fill_color=GOLD)
add_rect(slide, 3.5, 3.52, 6.3, 0.02, fill_color=GOLD)
add_rect(slide, 3.5, 4.28, 6.3, 0.02, fill_color=GOLD)
add_text_box(slide, "RELIC FAIRY", 0, 1.6, 13.33, 1.2,
             font_size=64, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
add_text_box(slide, "랠릭페어리", 0, 2.65, 13.33, 0.7,
             font_size=28, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
add_text_box(slide, "게임 기획서  |  v0.5  |  2026. 05. 21", 0, 3.62, 13.33, 0.5,
             font_size=14, color=GRAY_MID, align=PP_ALIGN.CENTER)
tags = ["로그라이크 액션 RPG", "모던 다크 동화", "아서왕 전설", "PC (Steam)"]
tag_x = 2.4
for tag in tags:
    add_rect(slide, tag_x, 4.7, 2.1, 0.38, fill_color=BG_CARD, line_color=GOLD, line_width=12000)
    add_text_box(slide, tag, tag_x, 4.72, 2.1, 0.38,
                 font_size=10, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
    tag_x += 2.25
add_text_box(slide, "\"죽어도 영혼은 남는다. 멀린의 의지가 다시 불러온다.\"",
             0, 5.5, 13.33, 0.5, font_size=13, color=GRAY_LIGHT, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# 슬라이드 2 — 목차
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "목차", "Table of Contents — v0.5")

sections = [
    ("01", "프로젝트 개요",      "게임 기본 정보 · 핵심 컨셉 · 레퍼런스"),
    ("02", "세계관 & 시나리오",  "배경 · 핵심 인물 · 서사 아크"),
    ("03", "코어 게임 루프",     "거시적/미시적 루프 · 씬 구성"),
    ("04", "영웅 유물 시스템",   "5종 영웅 · 고유 메커닉 · 해금 시스템"),
    ("05", "장비 & 빌드",       "장비 구조 · 조합표 · 아이템 구조"),
    ("06", "런 내 성장",        "스탯 레이어 · 서약 시스템 · 시너지"),
    ("07", "메타 진행",         "재화 4종 · 멀린의 서약 · 보스 봉인 시스템"),
    ("08", "챕터 & 레벨 디자인","26존 세계맵 · 보스 설계 · 엘리트 진화"),
    ("09", "비주얼 & 톤",       "아트 방향 · 챕터별 컬러 · UI/UX"),
    ("10", "전투 연출 & 게임 느낌", "슬로우모션 · 히트 이펙트 · 전진성 · 사망화면"),
    ("11", "튜토리얼 & 온보딩",  "첫 런 플로우 · 스타트방 · 로비 전시대"),
]

col1 = sections[:6]
col2 = sections[6:]

for i, (num, title, desc) in enumerate(col1):
    y = 1.05 + i * 1.05
    add_rect(slide, 0.4, y, 6.0, 0.9, fill_color=BG_CARD, line_color=GRAY_DARK, line_width=10000)
    add_rect(slide, 0.4, y, 0.7, 0.9, fill_color=BG_CARD2)
    add_text_box(slide, num, 0.4, y + 0.18, 0.7, 0.45, font_size=15, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
    add_text_box(slide, title, 1.2, y + 0.06, 4.9, 0.32, font_size=13, bold=True, color=WHITE)
    add_text_box(slide, desc, 1.2, y + 0.42, 4.9, 0.32, font_size=9, color=GRAY_LIGHT)

for i, (num, title, desc) in enumerate(col2):
    y = 1.05 + i * 1.05
    add_rect(slide, 6.9, y, 6.0, 0.9, fill_color=BG_CARD, line_color=GRAY_DARK, line_width=10000)
    add_rect(slide, 6.9, y, 0.7, 0.9, fill_color=BG_CARD2)
    add_text_box(slide, num, 6.9, y + 0.18, 0.7, 0.45, font_size=15, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
    add_text_box(slide, title, 7.7, y + 0.06, 4.9, 0.32, font_size=13, bold=True, color=WHITE)
    add_text_box(slide, desc, 7.7, y + 0.42, 4.9, 0.32, font_size=9, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 01 — 프로젝트 개요
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "01  프로젝트 개요", "Project Overview")

slide = new_slide(prs)
add_slide_title(slide, "01  프로젝트 개요", "게임 기본 정보 · 핵심 컨셉 · 레퍼런스")

add_card(slide, 0.4, 1.05, 4.2, 5.9, "게임 기본 정보")
info = [
    ("타이틀",   "랠릭페어리 (Relic Fairy)"),
    ("장르",     "로그라이크 액션 RPG"),
    ("플랫폼",   "PC  (Steam)"),
    ("시점",     "3D 액션 (쿼터뷰)"),
    ("타깃",     "로그라이크 · 액션 RPG 팬"),
]
for i, (k, v) in enumerate(info):
    y = 1.55 + i * 0.9
    add_rect(slide, 0.55, y, 3.9, 0.75, fill_color=BG_CARD2)
    add_text_box(slide, k, 0.7, y + 0.06, 1.1, 0.28, font_size=9.5, color=GRAY_MID, bold=True)
    add_text_box(slide, v, 0.7, y + 0.32, 3.6, 0.3, font_size=11.5, color=WHITE, bold=True)

add_card(slide, 4.8, 1.05, 4.8, 2.8, "핵심 컨셉")
add_text_box(slide,
    "\"죽어도 영혼은 남는다.\n멀린의 의지가 다시 불러온다.\"",
    5.0, 1.55, 4.4, 1.0, font_size=13, color=GOLD_LIGHT, bold=True)
add_text_box(slide,
    "멀린이 죽고 세계가 부패한다. 그의 잔존 의지가\n"
    "신성한 영혼(주인공)을 소환하고, 주인공은\n"
    "쓰러진 영웅들의 유물(육체)을 빌려 전진한다.\n"
    "실패해도 돌아온다. 조금 더 강해져서.",
    5.0, 2.55, 4.4, 1.2, font_size=10.5, color=GRAY_LIGHT)

add_card(slide, 4.8, 4.05, 4.8, 2.9, "레퍼런스 게임")
refs = [
    ("Hades",        "메타 진행 구조, 실패=성장 패러다임"),
    ("Dead Cells",   "빠른 전투 리듬, 런 내 빌드 다양성"),
    ("Hollow Knight","다크 동화 비주얼 톤, 세계관 밀도"),
    ("Path of Exile","스탯 레이어 깊이, 빌드 다양성"),
]
for i, (game, desc) in enumerate(refs):
    y = 4.55 + i * 0.55
    add_rect(slide, 5.0, y, 4.4, 0.46, fill_color=BG_CARD2)
    add_text_box(slide, game, 5.15, y + 0.04, 1.5, 0.36, font_size=10, bold=True, color=GOLD)
    add_text_box(slide, desc, 6.7, y + 0.08, 2.6, 0.28, font_size=9, color=GRAY_LIGHT)

add_card(slide, 9.8, 1.05, 3.1, 2.8, "핵심 키워드")
keywords = ["모던 다크 동화", "아서왕 전설", "영웅 유물", "서약 시스템", "빌드 다양성", "시나리오 연동"]
for i, kw in enumerate(keywords):
    y = 1.55 + i * 0.38
    add_rect(slide, 9.95, y, 2.8, 0.3, fill_color=RGBColor(0x1A, 0x22, 0x38),
             line_color=GOLD, line_width=8000)
    add_text_box(slide, kw, 9.95, y + 0.02, 2.8, 0.28, font_size=10, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)

add_card(slide, 9.8, 4.05, 3.1, 2.9, "장르 포지셔닝")
add_text_box(slide,
    "로그라이크의\n매 런 신선함\n\n+\n\n액션 RPG의\n빌드 깊이\n\n+\n\n서약 아이덴티티\n성장의 의미",
    9.95, 4.45, 2.8, 2.4, font_size=10.5, color=WHITE, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# SECTION 02 — 세계관 & 시나리오
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "02  세계관 & 시나리오", "World & Scenario")

slide = new_slide(prs)
add_slide_title(slide, "02  세계관 — 배경 & 핵심 인물")

add_card(slide, 0.4, 1.05, 6.0, 2.8, "배경 설정")
add_text_box(slide,
    "아서왕 전설의 세계. 최고의 마법사 멀린이 의문의 외부 존재에 의해 살해된다.\n"
    "그의 죽음과 동시에 세계는 부패하기 시작하고, 자연·고대 존재·인간 영웅들이\n"
    "차례로 잠식된다.\n\n"
    "멀린의 잔존 의지는 세계 너머에서 신성한 영혼(주인공)을 소환하고,\n"
    "쓰러진 영웅들의 유물(육체)을 그 그릇으로 삼는다.",
    0.6, 1.5, 5.6, 2.2, font_size=10.5, color=GRAY_LIGHT)

add_card(slide, 0.4, 4.0, 6.0, 3.0, "톤 & 무드")
add_text_box(slide, "모던 다크 동화 (Modern Dark Fairy Tale)", 0.6, 4.45, 5.6, 0.35,
             font_size=12, bold=True, color=GOLD)
add_text_box(slide,
    "한때 빛났던 동화의 페이지가 잠식되고 부패한 세계.\n"
    "귀엽거나 밝지 않다. 어둡고 잔인하지만\n"
    "아름다운 잔재가 남아있는 분위기.",
    0.6, 4.85, 5.6, 1.8, font_size=10.5, color=GRAY_LIGHT)

chars = [
    ("멀린",     GOLD,   "사망한 마법사. 잔존 의지로 안내.\n런마다 목소리로 대화. 점점 약해짐."),
    ("주인공",   TEAL,   "신성한 영혼. 영웅 유물(육체)을\n빌려 전투. 사망 시 멀린이 복구."),
    ("모드레드",  RED_MID,"3챕터 보스. 배신자 원탁 기사.\n리치에게 잠식된 첫 번째 영웅."),
    ("리치",     PURPLE, "최종 보스. 멀린의 육체 탈취.\n멀린의 전성기 능력 그대로 복사."),
    ("모르가나",  RGBColor(0x88,0x44,0xAA), "포스트 엔딩 배후.\n리치를 통해 멀린을 도구로 활용."),
]
for i, (name, color, desc) in enumerate(chars):
    x = 6.65 + (i % 3) * 2.2
    y = 1.05 + (i // 3) * 3.05
    add_rect(slide, x, y, 2.05, 2.7, fill_color=BG_CARD, line_color=color, line_width=18000)
    add_rect(slide, x, y, 2.05, 0.5, fill_color=BG_CARD2)
    add_text_box(slide, name, x, y + 0.08, 2.05, 0.36, font_size=13, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, x + 0.1, y + 0.6, 1.85, 2.0, font_size=9.5, color=GRAY_LIGHT)

# 서사 아크
slide = new_slide(prs)
add_slide_title(slide, "02  서사 아크 — 챕터별 빌드업")

chapters_story = [
    ("CH.1\n부패의 숲",   TEAL,   "그린 나이트",          "세상이 이상하다.\n원인 불명.",
     "영혼이여, 근원을 찾아라."),
    ("CH.2\n용암 대지",   GOLD,   "이 드레이그 고흐",     "고대 존재까지 잠식.\n하나의 의지가 있음.",
     "이 부패는 의지를 가지고 있어."),
    ("CH.3\n잠식된 성채", RED_MID,"모드레드",             "영웅이 잠식됨.\n복사의 흔적 발견.",
     "내 기억이 흐릿해지고 있어..."),
    ("CH.4\n암흑대지",    PURPLE, "리치 (멀린의 육체)",   "복사체 연속전.\n진실 완전히 드러남.",
     "막아줘."),
]

for i, (ch, color, boss, story, voice) in enumerate(chapters_story):
    x = 0.4 + i * 3.2
    add_rect(slide, x, 1.1, 3.0, 5.8, fill_color=BG_CARD, line_color=color, line_width=15000)
    add_rect(slide, x, 1.1, 3.0, 0.55, fill_color=BG_CARD2)
    add_text_box(slide, ch, x, 1.1, 3.0, 0.55, font_size=12, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, "BOSS", x + 0.15, 1.8, 0.6, 0.25, font_size=8, color=GRAY_MID, bold=True)
    add_rect(slide, x + 0.15, 2.08, 2.7, 0.38, fill_color=RGBColor(0x20,0x18,0x30))
    add_text_box(slide, boss, x + 0.15, 2.1, 2.7, 0.34, font_size=10, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, "서사", x + 0.15, 2.58, 0.5, 0.25, font_size=8, color=GRAY_MID, bold=True)
    add_text_box(slide, story, x + 0.15, 2.85, 2.7, 0.8, font_size=10, color=WHITE)
    add_rect(slide, x + 0.15, 3.75, 2.7, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "멀린의 의지", x + 0.15, 3.85, 2.0, 0.25, font_size=8, color=GRAY_MID, bold=True)
    add_rect(slide, x + 0.15, 4.15, 2.7, 1.5, fill_color=RGBColor(0x14, 0x10, 0x20))
    add_text_box(slide, f'"{voice}"', x + 0.25, 4.25, 2.5, 1.3, font_size=10, color=GOLD_LIGHT)
    if i < 3:
        add_text_box(slide, "▶", x + 3.0, 3.8, 0.25, 0.4, font_size=16, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# SECTION 03 — 코어 게임 루프
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "03  코어 게임 루프", "Core Game Loop")

slide = new_slide(prs)
add_slide_title(slide, "03  코어 게임 루프 (v0.5 미시적 루프 갱신)")

add_card(slide, 0.4, 1.05, 5.8, 5.9, "거시적 루프 (Macro Loop)")
loop_items = [
    (GOLD,   "허브: 멀린의 탑 잔해",      "영웅 유물 + 장비 선택"),
    (TEAL,   "런 시작",                   "서약 3종 중 1개 선택 (기본)"),
    (WHITE,  "챕터 1~4 진행",             "방 탐색 · 전투 · 아이템 그리드"),
    (RED_MID,"사망 또는 클리어",           ""),
    (GOLD,   "허브 복귀",                 "재화 소비 · 서약/해방/각성"),
    (TEAL,   "강해진 영혼으로 재도전",     "반복"),
]
for i, (color, title, desc) in enumerate(loop_items):
    y = 1.6 + i * 0.85
    add_rect(slide, 0.6, y, 0.08, 0.55, fill_color=color)
    add_text_box(slide, title, 0.85, y + 0.02, 3.5, 0.3, font_size=12, bold=True, color=color)
    if desc:
        add_text_box(slide, desc, 0.85, y + 0.32, 3.5, 0.25, font_size=9.5, color=GRAY_LIGHT)
    if i < len(loop_items) - 1:
        add_text_box(slide, "↓", 0.55, y + 0.6, 0.3, 0.2, font_size=11, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# v0.5 미시적 루프
add_card(slide, 6.5, 1.05, 6.4, 5.9, "미시적 루프 (Micro Loop — v0.5 갱신)")
micro = [
    (TEAL,   "런 시작",             "서약 3종 중 1개 선택 (기본 상태)"),
    (WHITE,  "방 전투 수행",         "영웅 메커닉 + 장비 조합 활용"),
    (GOLD,   "아이템 획득",          "Staging → 그리드 배치 → 효과 발동"),
    (TEAL,   "이벤트방",             "보유 서약 중 1개 선택 → 서약 강화"),
    (WHITE,  "특별 분기 구간",        "서약 추가 획득 (최대 3~4개)"),
    (GOLD,   "보스방",               "격파 → 서약 1개 진화 + 유물 코어 획득"),
]
for i, (color, title, desc) in enumerate(micro):
    y = 1.6 + i * 0.85
    add_rect(slide, 6.7, y, 0.08, 0.55, fill_color=color)
    add_text_box(slide, title, 6.95, y + 0.02, 3.5, 0.3, font_size=12, bold=True, color=color)
    if desc:
        add_text_box(slide, desc, 6.95, y + 0.32, 3.5, 0.25, font_size=9.5, color=GRAY_LIGHT)
    if i < len(micro) - 1:
        add_text_box(slide, "↓", 6.65, y + 0.6, 0.3, 0.2, font_size=11, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# SECTION 04 — 영웅 유물 시스템
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "04  영웅 유물 시스템", "Hero Relic System")

slide = new_slide(prs)
add_slide_title(slide, "04  영웅 유물 — 시스템 개요")

add_card(slide, 0.4, 1.05, 12.5, 1.5, "설계 원칙")
add_text_box(slide,
    "영웅의 이름은 잊혀졌다. 남은 건 유물뿐이다.  —  패시브 이름·스킬·대사로만 정체를 암시. 플레이어가 직접 유추.",
    0.6, 1.35, 12.1, 0.45, font_size=12, color=GOLD_LIGHT)
add_text_box(slide,
    "모든 영웅이 모든 장비 사용 가능  |  영웅마다 고유 패시브 + 고유 스킬 + 고유 메커닉 보유  |  해금형 순차 개방",
    0.6, 1.85, 12.1, 0.45, font_size=11, color=GRAY_LIGHT)

hero_data = [
    ("성배의 수호자", TEAL,   "방어형",   "신성 게이지", "기본 제공", "HP 150 / ATK 80 / DEF 40"),
    ("원탁의 균열",   GOLD,   "공격형",   "콤보 게이지", "기억 ×3",   "HP 120 / ATK 110 / DEF 20"),
    ("여정의 창",     WHITE,  "돌진형",   "여정 스택",   "기억 ×5",   "HP 135 / ATK 95 / DEF 30"),
    ("태양의 서약",   AMBER,  "중전사형", "태양 타이머", "기억 ×8",   "HP 140 / ATK 105 / DEF 35"),
    ("비련의 화살",   PURPLE, "원거리형", "거리 시스템", "기억 ×12",  "HP 100 / ATK 115 / DEF 15"),
]

for i, (name, color, style, mech, unlock, stats) in enumerate(hero_data):
    x = 0.4 + i * 2.55
    y = 2.75
    add_rect(slide, x, y, 2.4, 4.3, fill_color=BG_CARD, line_color=color, line_width=15000)
    add_rect(slide, x, y, 2.4, 0.5, fill_color=BG_CARD2)
    add_text_box(slide, name, x, y + 0.06, 2.4, 0.38, font_size=11, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, style, x + 0.15, y + 0.65, 2.1, 0.3, font_size=10.5, color=WHITE, bold=True)
    add_rect(slide, x + 0.15, y + 1.05, 2.1, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "고유 메커닉", x + 0.15, y + 1.15, 2.1, 0.22, font_size=8.5, color=GRAY_MID, bold=True)
    add_rect(slide, x + 0.15, y + 1.4, 2.1, 0.34, fill_color=RGBColor(0x1A,0x20,0x34))
    add_text_box(slide, mech, x + 0.15, y + 1.42, 2.1, 0.3, font_size=10.5, color=color, align=PP_ALIGN.CENTER, bold=True)
    add_text_box(slide, stats, x + 0.12, y + 1.9, 2.15, 0.35, font_size=8.5, color=GRAY_LIGHT)
    add_rect(slide, x + 0.15, y + 2.95, 2.1, 0.38, fill_color=BG_CARD2)
    add_text_box(slide, "해금: " + unlock, x + 0.15, y + 2.97, 2.1, 0.34, font_size=9.5, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)

# 영웅 상세 (성배의 수호자)
slide = new_slide(prs)
color = TEAL
add_slide_title(slide, "04  영웅 유물 — 성배의 수호자  [갈라하드 암시]", "방어형 / 초보 친화 / 진입장벽 낮음")
add_rect(slide, 0, 0, 13.33, 0.06, fill_color=color)

add_card(slide, 0.4, 1.05, 3.0, 1.7, "기본 스탯", color)
for i, (k, v) in enumerate([("HP","150"),("ATK","80"),("DEF","40"),("SPD","×1.0")]):
    x = 0.55 + i * 0.72
    add_rect(slide, x, 1.5, 0.65, 0.85, fill_color=BG_CARD2)
    add_text_box(slide, v, x, 1.55, 0.65, 0.38, font_size=15, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, k, x, 1.9, 0.65, 0.28, font_size=8, color=GRAY_MID, align=PP_ALIGN.CENTER)

add_card(slide, 0.4, 2.9, 3.0, 2.2, "고유 패시브", color)
for i, (pname, pdesc) in enumerate([
    ("성스러운 방패", "방어 시 받은 피해의 30%를 전방 적에게 반사"),
    ("아버지의 죄",   "피격 시 신성 게이지 +15 / 방어 성공 시 +25"),
]):
    y = 3.38 + i * 0.75
    add_rect(slide, 0.55, y, 2.7, 0.62, fill_color=BG_CARD2)
    add_text_box(slide, pname, 0.7, y + 0.03, 2.4, 0.26, font_size=10, bold=True, color=color)
    add_text_box(slide, pdesc, 0.7, y + 0.3, 2.4, 0.28, font_size=8.5, color=GRAY_LIGHT)

add_card(slide, 3.6, 1.05, 4.8, 1.7, "고유 스킬", color)
add_text_box(slide, "성배의 빛", 3.8, 1.42, 3.0, 0.35, font_size=13, bold=True, color=color)
add_text_box(slide, "쿨다운 18초", 6.9, 1.5, 1.3, 0.25, font_size=9, color=GRAY_MID, align=PP_ALIGN.RIGHT)
add_text_box(slide, "전방 4m 범위 신성 폭발. 피해 공격력 ×180%\n게이지 30 이상 시 범위 +50%, 피해 +50%", 3.8, 1.82, 4.4, 0.82, font_size=10, color=GRAY_LIGHT)

add_card(slide, 3.6, 2.9, 4.8, 2.2, "고유 메커닉 — 신성 게이지", color)
for i, b in enumerate(["최대 게이지: 100", "피격 +15 / 방어성공 +25 / 스킬 사용 +20", "만충(100) 시: 5초 무적 + 공격력 150%", "이후 30초 쿨다운 (재충전 불가)"]):
    add_text_box(slide, "▸  " + b, 3.8, 3.38 + i * 0.44, 4.4, 0.38, font_size=10, color=WHITE)

add_card(slide, 8.6, 1.05, 4.3, 4.05, "메커닉 변형 아이템", color)
for i, (iname, idesc) in enumerate([
    ("죄의 대가",    "공격 시 게이지 +8 → 공격형 전환"),
    ("성배의 축복",  "게이지 50마다 소폭 발동 → 지속형"),
    ("순교의 서약",  "만충 효과 5초 → 10초 연장"),
]):
    y = 1.55 + i * 1.15
    add_rect(slide, 8.8, y, 3.9, 0.95, fill_color=BG_CARD2)
    add_rect(slide, 8.8, y, 3.9, 0.38, fill_color=RGBColor(0x20, 0x18, 0x30))
    add_text_box(slide, iname, 8.95, y + 0.05, 3.6, 0.28, font_size=11, bold=True, color=GOLD)
    add_text_box(slide, idesc, 8.95, y + 0.45, 3.6, 0.38, font_size=9.5, color=GRAY_LIGHT)

add_card(slide, 0.4, 5.25, 12.5, 1.8, "해금 정보 & 대사", color)
add_text_box(slide, "기본 제공  |  해금 대사: \"이 빛은... 아직 꺼지지 않았군\"", 0.6, 5.65, 12.1, 0.35, font_size=11, color=GOLD_LIGHT)
add_text_box(slide, "가웨인 유물 장착 시 특별 대사: 그린 나이트 \"...그 유물. 태양의 기사가 보냈는가?\"", 0.6, 6.05, 12.1, 0.35, font_size=10, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 05 — 장비 & 빌드
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "05  장비 & 빌드 시스템", "Equipment & Build System")

slide = new_slide(prs)
add_slide_title(slide, "05  장비 & 빌드 — 조합표 & 아이템 구조")

add_card(slide, 0.4, 1.05, 8.5, 5.9, "영웅 × 장비 시너지 조합표")
headers = ["영웅 유물", "카타나", "대검", "창", "활", "검+방패"]
col_w = [1.9, 1.2, 1.2, 1.2, 1.2, 1.2]
rows = [
    ["성배의 수호자", "보통",    "탱딜★",  "표준",    "지연",    "최적★★"],
    ["원탁의 균열",   "최적★★", "고위험★", "콤보★",   "안전딜",  "방어콤보"],
    ["여정의 창",     "빠른누적","보스킬★", "시너지★★","안전",    "균형형"],
    ["태양의 서약",   "연속딜",  "버스트★", "돌진버스트","원거리", "방어유지★"],
    ["비련의 화살",   "위험",    "최고위험","히트앤런", "최적★★", "방어방지"],
]
add_table_rows(slide, headers, rows, 0.55, 1.55, col_w, row_height=0.82)

add_card(slide, 9.15, 1.05, 3.8, 3.0, "아이템 이중 효과 구조")
add_rect(slide, 9.35, 1.55, 3.4, 2.3, fill_color=BG_CARD2, line_color=GRAY_DARK, line_width=10000)
add_text_box(slide, "[ 분노의 맹세 ]", 9.35, 1.6, 3.4, 0.35, font_size=11, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
add_rect(slide, 9.35, 1.95, 3.4, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "기본 효과", 9.5, 2.02, 1.2, 0.25, font_size=9, color=GRAY_MID, bold=True)
add_text_box(slide, "공격력 +10%", 9.5, 2.28, 3.0, 0.28, font_size=11, color=WHITE)
add_rect(slide, 9.35, 2.6, 3.4, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "원탁의 균열 친화", 9.5, 2.67, 2.5, 0.25, font_size=9, color=GOLD, bold=True)
add_text_box(slide, "피격 시 콤보 게이지 +3", 9.5, 2.93, 3.0, 0.28, font_size=10.5, color=TEAL)

add_card(slide, 9.15, 4.25, 3.8, 2.7, "아이템 등급 & 드롭률")
grade_data = [
    ("Common",    "55%", GRAY_MID, "범용 스탯"),
    ("Rare",      "30%", TEAL,     "조건부 + 친화"),
    ("Epic",      "12%", PURPLE,   "메커닉 변형"),
    ("Legendary",  "3%", GOLD,     "빌드 전환"),
]
for i, (grade, pct, c, desc) in enumerate(grade_data):
    y = 4.75 + i * 0.52
    add_rect(slide, 9.35, y, 0.8, 0.42, fill_color=c)
    add_text_box(slide, grade, 9.35, y + 0.06, 0.8, 0.3, font_size=8.5, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, pct, 10.25, y + 0.06, 0.7, 0.3, font_size=11, bold=True, color=c, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, 11.0, y + 0.1, 1.8, 0.25, font_size=9, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 06 & 07 — 성장 시스템
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "06 · 07  성장 시스템 (v0.5)", "Vow System & Meta Progression")

# 스탯 레이어 & 그리드 시너지
slide = new_slide(prs)
add_slide_title(slide, "06  런 내 성장 — 스탯 레이어 & 그리드 시너지")

add_card(slide, 0.4, 1.05, 5.5, 5.9, "스탯 레이어 구조 (7단계 합산)")
layers = [
    (GOLD,   "① 기본",   "영웅 유물 기본 스탯"),
    (TEAL,   "② 서약",   "멀린의 서약 영구 보너스 (메타)"),
    (PURPLE, "③ 각성",   "유물 각성 효과 (메타)"),
    (WHITE,  "④ 장비",   "선택한 장비 스탯"),
    (GOLD,   "⑤ 아이템", "그리드 배치 아이템 합산"),
    (TEAL,   "⑥ 서약",   "활성화된 서약 효과 (런 내)"),  # v0.5 변경: 방버프 → 서약
    (PURPLE, "⑦ 시너지", "그리드 시너지 Always/조건부"),
]
for i, (c, num, desc) in enumerate(layers):
    y = 1.55 + i * 0.74
    add_rect(slide, 0.6, y, 5.1, 0.62, fill_color=BG_CARD2)
    add_rect(slide, 0.6, y, 0.08, 0.62, fill_color=c)
    add_text_box(slide, num, 0.85, y + 0.08, 1.3, 0.28, font_size=11, bold=True, color=c)
    add_text_box(slide, desc, 2.25, y + 0.1, 3.3, 0.28, font_size=10.5, color=GRAY_LIGHT)

# v0.5 배지
add_rect(slide, 0.6, 5.7, 5.1, 0.4, fill_color=RGBColor(0x1A, 0x22, 0x18), line_color=TEAL, line_width=10000)
add_text_box(slide, "v0.5: ⑥ 방버프 → 서약 (런 내 활성 서약 효과)로 교체 완료", 0.75, 5.78, 4.8, 0.26, font_size=9.5, color=TEAL)

# 그리드 시너지 트리거
add_card(slide, 6.2, 1.05, 3.2, 5.9, "그리드 시너지 트리거")
triggers = [
    ("Always",   GOLD,   "그리드 완성 즉시\n스탯 영구 반영"),
    ("OnHit",    TEAL,   "공격 적중 시 스택 누적\n최대 스택까지, duration 유지"),
    ("OnLowHp",  RED_MID,"HP ≤ threshold\n조건 만족 시 활성화"),
]
for i, (t, c, desc) in enumerate(triggers):
    y = 1.55 + i * 1.72
    add_rect(slide, 6.4, y, 2.9, 1.5, fill_color=BG_CARD2, line_color=c, line_width=15000)
    add_text_box(slide, t, 6.4, y + 0.1, 2.9, 0.45, font_size=16, bold=True, color=c, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, 6.55, y + 0.6, 2.6, 0.75, font_size=10, color=GRAY_LIGHT)

# 시너지 예시
add_card(slide, 9.6, 1.05, 3.3, 5.9, "그리드 시너지 예시")
syn_data = [
    ("기사의 각오",  GOLD,   "Always",   "공격력 +15%"),
    ("원탁의 결속",  TEAL,   "Always",   "MaxHP +50"),
    ("심판의 일격",  AMBER,  "OnHit",    "스택당 공격력 +2% (최대 10스택)"),
    ("사지의 저항",  RED_MID,"OnLowHp",  "방어력 +40%, 이동속도 +15%"),
]
for i, (sname, c, trigger, eff) in enumerate(syn_data):
    y = 1.55 + i * 1.28
    add_rect(slide, 9.8, y, 2.9, 1.1, fill_color=BG_CARD2, line_color=c, line_width=12000)
    add_rect(slide, 9.8, y, 2.9, 0.38, fill_color=BG_DARK)
    add_text_box(slide, sname, 9.9, y + 0.04, 2.5, 0.28, font_size=11, bold=True, color=c)
    add_text_box(slide, trigger, 11.4, y + 0.06, 1.25, 0.24, font_size=9, color=GRAY_MID, align=PP_ALIGN.RIGHT)
    add_text_box(slide, eff, 9.9, y + 0.48, 2.7, 0.52, font_size=9.5, color=WHITE)

# ══════════════════════════════════════════════════════════════
# 06 — 서약 시스템 Overview
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "06  서약 시스템 — 개요 & 획득 구조 (v0.5 핵심 신규)")

add_card(slide, 0.4, 1.05, 5.5, 5.9, "서약 시스템 개요")
add_text_box(slide,
    "\"멀린이 살아있던 시절, 그는 강력한 존재들과 계약을 맺었다.\n"
    "그 서약들은 지금도 봉인된 채로 남아있다.\n"
    "런마다 봉인이 하나씩 열린다.\"",
    0.6, 1.55, 5.1, 0.95, font_size=10.5, color=GOLD_LIGHT)
add_rect(slide, 0.6, 2.55, 5.1, 0.02, fill_color=GRAY_DARK)
add_text_box(slide,
    "서약은 단순 수치 강화가 아닌 플레이 방식 자체를 결정하는\n아이덴티티 능력이다.",
    0.6, 2.62, 5.1, 0.55, font_size=10.5, color=WHITE)
add_text_box(slide, "전체 서약 수: 12종", 0.6, 3.25, 5.1, 0.3, font_size=11, bold=True, color=GOLD)

layers_vow = [
    (GOLD,   "아서 전설",  "5종 — 니무에·모르가나·아서·갤러해드·모드레드"),
    (TEAL,   "켈트 신화",  "4종 — 모리건·쿠훌린·루·발로르"),
    (PURPLE, "외부 신화",  "3종 — 헤카테·솔로몬·프로메테우스"),
]
for i, (c, lname, desc) in enumerate(layers_vow):
    y = 3.65 + i * 0.72
    add_rect(slide, 0.6, y, 5.1, 0.6, fill_color=BG_CARD2, line_color=c, line_width=12000)
    add_rect(slide, 0.6, y, 0.08, 0.6, fill_color=c)
    add_text_box(slide, lname, 0.85, y + 0.1, 1.4, 0.28, font_size=11, bold=True, color=c)
    add_text_box(slide, desc, 2.35, y + 0.12, 3.2, 0.28, font_size=9.5, color=GRAY_LIGHT)

add_card(slide, 6.2, 1.05, 6.8, 2.8, "3단계 성장 구조")
stages = [
    ("기본",  TEAL,   "런 시작 / 특별 분기 구간",  "서약 고유 기능 활성화"),
    ("강화",  GOLD,   "이벤트 방 클리어",          "수치 개선 (기능 동일, 효과량 증가)"),
    ("진화",  PURPLE, "보스 처치",                "기능 자체가 변형 — 플레이 스타일 전환"),
]
for i, (stage, c, trigger, desc) in enumerate(stages):
    y = 1.55 + i * 0.7
    add_rect(slide, 6.4, y, 6.4, 0.6, fill_color=BG_CARD2)
    add_rect(slide, 6.4, y, 0.9, 0.6, fill_color=c)
    add_text_box(slide, stage, 6.4, y + 0.1, 0.9, 0.38, font_size=14, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, trigger, 7.45, y + 0.06, 2.6, 0.24, font_size=9, color=GRAY_MID)
    add_text_box(slide, desc, 7.45, y + 0.3, 5.0, 0.24, font_size=10, color=WHITE)

add_card(slide, 6.2, 4.05, 6.8, 2.9, "획득 구조 & 런당 최대 보유")
acq = [
    ("런 시작",        "서약 3종 제시 → 1개 선택 (기본)"),
    ("이벤트방",       "보유 서약 중 1개 → 강화"),
    ("특별 분기 구간", "서약 1개 추가 획득"),
    ("보스 처치",      "보유 서약 중 1개 → 진화"),
]
for i, (timing, desc) in enumerate(acq):
    y = 4.55 + i * 0.54
    add_rect(slide, 6.4, y, 6.4, 0.44, fill_color=BG_CARD2)
    add_rect(slide, 6.4, y, 1.65, 0.44, fill_color=BG_DARK)
    add_text_box(slide, timing, 6.5, y + 0.08, 1.5, 0.28, font_size=9.5, bold=True, color=GOLD)
    add_text_box(slide, desc, 8.15, y + 0.08, 4.3, 0.28, font_size=10, color=GRAY_LIGHT)
add_rect(slide, 6.4, 6.75, 6.4, 0.38, fill_color=RGBColor(0x1A, 0x22, 0x18), line_color=TEAL, line_width=10000)
add_text_box(slide, "런당 최대 보유 서약: 3~4개  |  보스를 잡지 못하면 진화 불가", 6.55, 6.82, 6.1, 0.24, font_size=9.5, color=TEAL)

# ══════════════════════════════════════════════════════════════
# 06 — 서약 목록 (아서 전설 레이어)
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "06  서약 목록 — 아서 전설 레이어 (5종)")

vows_arthur = [
    ("니무에의 서약",   TEAL,
     "피격 시 피해의 40%를 흡수하는\n보호막 생성 (3초)",
     "흡수율 70%, 지속 4초",
     "보호막 만료 시 흡수 피해량을\n주변 적에게 폭발로 반사"),
    ("모르가나의 서약", RGBColor(0x88,0x44,0xAA),
     "처치마다 최대 체력의 3% 흡혈",
     "흡혈 5%, 누적 최대 30%까지",
     "누적 흡혈량이 최대 체력 초과 시\n초과분이 다음 공격의 추가 피해로 방출"),
    ("아서의 서약",     GOLD,
     "HP 30% 이하 시 공격력 +35%",
     "공격력 +55%, 이동속도 +20%",
     "HP 30% 이하 구간에서 5초마다\n방어력 무시 충격파 자동 발동"),
    ("갤러해드의 서약", WHITE,
     "10초마다 피해 1회 완전 무효",
     "쿨타임 7초",
     "무효 발동 직후 3초간 모든 공격에\n신성 폭발 부가 (범위 피해)"),
    ("모드레드의 서약", RED_MID,
     "공격력 +50%, 방어력 -30%",
     "공격력 +70%, 방어력 -15%",
     "처치 20마리 달성 시 방어력 패널티 제거,\n공격력 +20% 추가 고정"),
]

for i, (vname, c, basic, enhanced, evolved) in enumerate(vows_arthur):
    x = 0.35 + i * 2.6
    add_rect(slide, x, 1.1, 2.45, 6.0, fill_color=BG_CARD, line_color=c, line_width=15000)
    add_rect(slide, x, 1.1, 2.45, 0.48, fill_color=BG_CARD2)
    add_text_box(slide, vname, x, 1.13, 2.45, 0.42, font_size=10, bold=True, color=c, align=PP_ALIGN.CENTER)

    for j, (stage, stage_c, txt) in enumerate([
        ("기본",  TEAL,   basic),
        ("강화",  GOLD,   enhanced),
        ("진화",  PURPLE, evolved),
    ]):
        y = 1.68 + j * 1.7
        add_rect(slide, x + 0.1, y, 2.25, 0.28, fill_color=stage_c)
        add_text_box(slide, stage, x + 0.1, y + 0.02, 2.25, 0.24, font_size=9, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
        add_rect(slide, x + 0.1, y + 0.3, 2.25, 1.3, fill_color=BG_CARD2)
        add_text_box(slide, txt, x + 0.2, y + 0.38, 2.05, 1.1, font_size=8.5, color=WHITE)

# ══════════════════════════════════════════════════════════════
# 06 — 서약 목록 (켈트 + 외부 신화)
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "06  서약 목록 — 켈트 신화 (4종) + 외부 신화 (3종)")

vows_other = [
    ("모리건의 서약",      TEAL,
     "처치마다 공격속도 +2%\n(최대 10중첩, 방 이동 시 초기화)",
     "+3%, 최대 15중첩",
     "10중첩 달성 시 영구 유지\n중첩마다 처치 시 폭발 발생"),
    ("쿠훌린의 서약",      RED_MID,
     "HP 50% 이하 게시: 피해 +60%,\n방어력 -40%",
     "발동 HP 65%, 피해 +80%",
     "게시 중 처치 시 HP 2% 회복\n+ 게시 지속 연장 (사실상 무한)"),
    ("루의 서약",          GOLD,
     "방 입장마다 무작위 스탯 1종\n+15% (방 종료 시 소멸)",
     "+25%, 2종 동시",
     "방마다 획득한 스탯 버프가\n소멸하지 않고 런 내 영구 누적"),
    ("발로르의 서약",      PURPLE,
     "8초마다 전방 관통 시선 발동\n(고정 피해)",
     "쿨타임 5초, 피해 +50%",
     "시선 적중 적에게 저주 부여\n→ 처치 시 연쇄 폭발로 전파"),
    ("헤카테의 서약",      AMBER,
     "스킬 사용 시 3가지 효과 중\n무작위 1개 추가 발동",
     "2개 동시 발동",
     "3가지 전부 동시 발동\n대신 스킬 쿨타임 +30%"),
    ("솔로몬의 서약",      TEAL,
     "처치 시 25% 확률로\n유령 소환 (10초, 자동 공격)",
     "확률 45%, 최대 2기 동시",
     "소환 유령이 처치한 적도 유령 소환\n연쇄, 최대 5기"),
    ("프로메테우스의 서약", ORANGE,
     "스킬 쿨타임 -40%\n사용마다 최대 체력 2% 소모",
     "쿨타임 -55%, 소모 1.5%",
     "체력 소모량이 즉시 다음 스킬의\n추가 피해로 전환"),
]

n_per_row = 4
for i, (vname, c, basic, enhanced, evolved) in enumerate(vows_other):
    row = i // n_per_row
    col = i % n_per_row
    x = 0.35 + col * 3.25
    y_base = 1.1 + row * 3.3
    w = 3.1
    add_rect(slide, x, y_base, w, 3.1, fill_color=BG_CARD, line_color=c, line_width=15000)
    add_rect(slide, x, y_base, w, 0.42, fill_color=BG_CARD2)
    add_text_box(slide, vname, x, y_base + 0.06, w, 0.3, font_size=10, bold=True, color=c, align=PP_ALIGN.CENTER)

    for j, (stage, stage_c, txt) in enumerate([
        ("기본",  TEAL,   basic),
        ("강화",  GOLD,   enhanced),
        ("진화",  PURPLE, evolved),
    ]):
        yy = y_base + 0.5 + j * 0.84
        add_rect(slide, x + 0.1, yy, w - 0.2, 0.22, fill_color=stage_c)
        add_text_box(slide, stage, x + 0.1, yy + 0.01, w - 0.2, 0.2, font_size=8.5, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
        add_rect(slide, x + 0.1, yy + 0.22, w - 0.2, 0.56, fill_color=BG_CARD2)
        add_text_box(slide, txt, x + 0.18, yy + 0.25, w - 0.36, 0.5, font_size=8, color=WHITE)

# 켈트/외부 레이어 구분 표시
add_rect(slide, 0.35, 4.4, 12.55, 0.03, fill_color=GRAY_DARK)
add_text_box(slide, "↑ 켈트 신화 레이어 (4종)          외부 신화 레이어 (3종) ↓", 0.35, 4.44, 12.55, 0.24, font_size=9, color=GRAY_MID, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# 06 — 파워 커브 (v0.5 갱신)
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "06  런 내 파워 커브 (v0.5 — 서약 기반)")

add_card(slide, 0.4, 1.05, 8.5, 5.9, "목표 배율 (런 시작 기준 1.0×)")
headers_pc = ["지점", "목표 배율", "주요 출처"]
cw_pc = [3.2, 1.5, 3.6]
rows_pc = [
    ["런 시작",         "1.0×",  "기본 스탯 + 서약 1개 (기본)"],
    ["챕터1 중반",      "1.4×",  "서약 강화 + 아이템 그리드 누적"],
    ["챕터1 보스 전",   "1.8×",  "서약 1개 진화 + 아이템 그리드 30~40%"],
    ["챕터2 보스 전",   "2.5×",  "서약 2개 + 그리드 55~65% + 서약 1개 진화"],
    ["챕터3 보스 전",   "3.5×",  "서약 3개 + 그리드 75~85% + 시너지 발동 시작"],
    ["챕터4 보스 전",   "5.0×+", "서약 3~4개(진화 포함) + 풀빌드 + 시너지 완성"],
]
add_table_rows(slide, headers_pc, rows_pc, 0.55, 1.55, cw_pc, row_height=0.62)

# 서약 성장 타임라인
add_card(slide, 0.4, 5.5, 8.5, 1.5, "서약 성장 타임라인")
timeline_rows = [
    ["런 시작",        "서약 1개 (기본)",  "런 시작 시 선택"],
    ["챕터1 이벤트방", "서약 강화",        "이벤트 방 클리어"],
    ["챕터1 보스",     "서약 1개 진화",    "챕터1 보스 처치"],
    ["챕터2~3 진행",   "서약 2~3개 확장",  "특별 분기 구간"],
    ["챕터2~3 보스",   "추가 서약 진화",   "각 챕터 보스 처치"],
]
add_table_rows(slide, ["구간", "서약 상태", "트리거"], timeline_rows, 0.55, 5.95, [2.5, 2.5, 3.3], row_height=0.32)

# 그리드 채움 목표
add_card(slide, 9.15, 1.05, 3.8, 3.0, "아이템 그리드 채움 목표")
grid_rows = [
    ["CH.1 완료", "30~40%", "9~12칸"],
    ["CH.2 완료", "55~65%", "16~20칸"],
    ["CH.3 완료", "75~85%", "23~25칸"],
    ["CH.4 보스 전", "90~100%", "27~30칸"],
]
add_table_rows(slide, ["챕터", "채움률", "칸 수(30칸)"], grid_rows, 9.35, 1.55, [1.5, 1.0, 1.1], row_height=0.42)

add_card(slide, 9.15, 4.25, 3.8, 2.7, "파워 설계 방향")
add_text_box(slide,
    "▸ 챕터1~2: 아이템 개별 효과 중심\n\n"
    "▸ 챕터3~4: 시너지 조합 파워 피크\n\n"
    "▸ 서약 진화 = 보스 클리어의 직접 보상\n  → 보스를 못 잡으면 성장 막힘",
    9.35, 4.7, 3.4, 2.1, font_size=10, color=WHITE)

# ══════════════════════════════════════════════════════════════
# 07 — 메타 진행: 재화 4종
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "07  메타 진행 — 재화 4종 & 성장 구조")

currency_data = [
    ("영혼의 잔재",  "★ 풍부",       GOLD,   "런 종료 시 진행 거리 기반",         "멀린의 서약\n영구 스탯 강화",     "◈"),
    ("봉인된 기억",  "★★★ 희귀",    TEAL,   "챕터 보스 처치 보장 +\n확률 드롭",  "유물 해방\n새 영웅 언락",         "◉"),
    ("유물 코어",    "★★★★★ 극희귀",PURPLE, "리치 첫 격파 ×4 일괄\n+ 완전해방 보스 첫 격파 ×1", "유물 각성\n메커닉 심화", "✦"),
    ("잔류 각인",    "★★★ 희귀",    AMBER,  "완전 해방 보스 재격파 시\n×3~5",    "유물 코어 교환\n(각인 ×5 = 코어 ×1)", "◆"),
]

for i, (name, rarity, c, obtain, use, icon) in enumerate(currency_data):
    x = 0.35 + i * 3.27
    add_rect(slide, x, 1.05, 3.1, 3.7, fill_color=BG_CARD, line_color=c, line_width=20000)
    add_rect(slide, x, 1.05, 3.1, 0.55, fill_color=BG_CARD2)
    add_text_box(slide, name, x, 1.1, 3.1, 0.45, font_size=14, bold=True, color=c, align=PP_ALIGN.CENTER)
    add_text_box(slide, icon, x, 1.65, 3.1, 0.7, font_size=32, color=c, align=PP_ALIGN.CENTER)
    add_text_box(slide, rarity, x, 2.4, 3.1, 0.28, font_size=9.5, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
    add_rect(slide, x + 0.2, 2.75, 2.7, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "획득", x + 0.2, 2.84, 0.6, 0.22, font_size=8, color=GRAY_MID, bold=True)
    add_text_box(slide, obtain, x + 0.9, 2.82, 2.0, 0.5, font_size=8.5, color=GRAY_LIGHT)
    add_text_box(slide, "소비", x + 0.2, 3.38, 0.6, 0.22, font_size=8, color=c, bold=True)
    add_text_box(slide, use, x + 0.9, 3.36, 2.0, 0.5, font_size=10, color=WHITE, bold=True)

# 런당 획득량 테이블
add_card(slide, 0.35, 4.95, 12.6, 2.2, "런당 평균 획득량 (풀클리어 기준)")
headers_cur = ["재화", "CH.1 클리어", "CH.2 클리어", "CH.3 클리어", "CH.4 클리어"]
cw_cur = [2.2, 2.5, 2.5, 2.5, 2.5]
rows_cur = [
    ["영혼의 잔재", "400~600",    "700~900",    "900~1,100",   "1,200~1,500"],
    ["봉인된 기억", "×1 보장",    "×1 보장",    "×2 보장",     "×2 보장"],
    ["유물 코어",   "—",          "—",          "—",           "×4 (리치 첫 격파 일괄)"],
    ["잔류 각인",   "—",          "—",          "—",           "리치 이후 보스당 ×3~5"],
]
add_table_rows(slide, headers_cur, rows_cur, 0.55, 5.42, cw_cur, row_height=0.4)

# 07 — 멀린의 서약
slide = new_slide(prs)
add_slide_title(slide, "07  멀린의 서약 — 영구 강화 항목 & 비용")

add_card(slide, 0.4, 1.05, 12.5, 0.8, "구조 설명")
add_text_box(slide,
    "6개 슬롯  |  각 슬롯마다 좌(적색) / 우(청색) 선택  |  언제든 리셋 가능 (소비 잔재 전액 환급)  |  Tier 1~5, 비용 2배씩 증가",
    0.6, 1.2, 12.1, 0.45, font_size=11, color=GRAY_LIGHT)

headers_sw = ["슬롯", "해금 조건", "좌 선택 (적색)", "우 선택 (청색)", "Tier 1", "Tier 5"]
cw_sw = [1.3, 2.3, 2.7, 2.7, 1.0, 1.0]
rows_sw = [
    ["생존",   "기본 제공",          "MaxHP +15/Tier",            "피격 시 HP 1% 회복",       "100",  "1,600"],
    ["공격",   "챕터 2 첫 도달",     "기본 공격력 +3%/Tier",      "치명타율 +2%/Tier",        "150",  "2,400"],
    ["이동",   "챕터 3 첫 도달",     "이동속도 +3%/Tier",         "구르기 쿨다운 -5%/Tier",   "120",  "1,920"],
    ["지속력", "챕터 4 첫 도달",     "방 클리어 HP 2% 회복",      "보스전 피해 -5%/Tier",     "200",  "3,200"],
    ["획득",   "리치 첫 격파",       "잔재 획득 +10%/Tier",       "기억 드롭률 +5%/Tier",     "300",  "4,800"],
    ["아이템", "리치 첫 격파 후 허브","등급 상승 확률 +5%/Tier",  "변형 아이템 +8%/Tier",    "400",  "6,400"],
]
add_table_rows(slide, headers_sw, rows_sw, 0.4, 2.05, cw_sw, row_height=0.7)

add_rect(slide, 0.4, 6.35, 12.5, 0.55, fill_color=BG_CARD2)
add_text_box(slide, "전체 최대 강화 완료 시 필요 영혼의 잔재:  약 42,000  (100런 이상 소요 예상)",
             0.6, 6.42, 12.1, 0.38, font_size=11, color=GOLD_LIGHT, bold=True)

# ──────────────────────────────────────────────
# 여기서 1부 저장 (임시 — 2부에서 덮어씌움)
OUTPUT_PART1 = r"c:/Users/u/Documents/GitHub/Project_Abyss/Assets/Abyss/Docs/RelicFairy_Proposal_PART1.pptx"
prs.save(OUTPUT_PART1)
print("1부 저장 완료: " + OUTPUT_PART1)
print("슬라이드 수 (1부): " + str(len(prs.slides)) + "장")
