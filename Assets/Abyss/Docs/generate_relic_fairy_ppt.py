"""
랠릭페어리 (Relic Fairy) - 게임 기획서 PPT 생성기
"""

from pptx import Presentation
from pptx.util import Inches, Pt, Emu
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
from pptx.util import Inches, Pt
import copy

# ─── 컬러 팔레트 ───────────────────────────────────────────────
BG_DARK       = RGBColor(0x0D, 0x11, 0x1A)   # 메인 배경
BG_CARD       = RGBColor(0x16, 0x1B, 0x2E)   # 카드 배경
BG_CARD2      = RGBColor(0x1E, 0x24, 0x3A)   # 카드 배경 2
GOLD          = RGBColor(0xC9, 0xA8, 0x4C)   # 골드 강조
GOLD_LIGHT    = RGBColor(0xE8, 0xD0, 0x8A)   # 밝은 골드
PURPLE        = RGBColor(0x6B, 0x3F, 0x8E)   # 보라 강조
TEAL          = RGBColor(0x4A, 0xC2, 0xB8)   # 청록
RED_DARK      = RGBColor(0x8B, 0x2A, 0x2A)   # 어두운 빨강
WHITE         = RGBColor(0xFF, 0xFF, 0xFF)
GRAY_LIGHT    = RGBColor(0xB8, 0xBE, 0xCC)
GRAY_MID      = RGBColor(0x72, 0x7A, 0x8E)
GRAY_DARK     = RGBColor(0x2E, 0x36, 0x4A)

# ─── 헬퍼 함수 ────────────────────────────────────────────────

def set_slide_bg(slide, color):
    fill = slide.background.fill
    fill.solid()
    fill.fore_color.rgb = color

def add_rect(slide, left, top, width, height, fill_color=None, line_color=None, line_width=None):
    shape = slide.shapes.add_shape(1, Inches(left), Inches(top), Inches(width), Inches(height))
    shape.line.fill.background() if line_color is None else None
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
    # 골드 상단 라인
    add_rect(slide, 0, 0, 13.33, 0.06, fill_color=GOLD)
    # 타이틀
    add_text_box(slide, title, 0.5, 0.15, 12.3, 0.6,
                 font_size=26, bold=True, color=GOLD, align=PP_ALIGN.LEFT)
    # 구분선
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
    # 헤더
    x = left
    for i, (h, w) in enumerate(zip(headers, col_widths)):
        add_rect(slide, x, top, w, row_height, fill_color=header_bg,
                 line_color=GRAY_DARK, line_width=10000)
        add_text_box(slide, h, x + 0.08, top + 0.04, w - 0.16, row_height - 0.08,
                     font_size=10, bold=True, color=header_color, align=PP_ALIGN.CENTER)
        x += w
    # 데이터 행
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
    # 중앙 골드 라인
    add_rect(slide, 1.5, 3.2, 10.3, 0.04, fill_color=GOLD)
    add_rect(slide, 1.5, 4.55, 10.3, 0.04, fill_color=GRAY_DARK)
    # 섹션 번호/제목
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

# 배경 그라데이션 효과 (레이어)
add_rect(slide, 0, 0, 13.33, 7.5, fill_color=BG_DARK)
add_rect(slide, 0, 0, 13.33, 2.0, fill_color=RGBColor(0x10, 0x16, 0x26))
add_rect(slide, 0, 5.5, 13.33, 2.0, fill_color=RGBColor(0x10, 0x16, 0x26))

# 상단 골드 라인
add_rect(slide, 0, 0, 13.33, 0.08, fill_color=GOLD)
add_rect(slide, 0, 7.42, 13.33, 0.08, fill_color=GOLD)

# 중앙 장식 라인
add_rect(slide, 3.5, 3.52, 6.3, 0.02, fill_color=GOLD)
add_rect(slide, 3.5, 4.28, 6.3, 0.02, fill_color=GOLD)

# 메인 타이틀
add_text_box(slide, "RELIC FAIRY", 0, 1.6, 13.33, 1.2,
             font_size=64, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
add_text_box(slide, "랠릭페어리", 0, 2.65, 13.33, 0.7,
             font_size=28, bold=False, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)

# 서브타이틀
add_text_box(slide, "게임 기획서  |  v0.1  |  2026. 05", 0, 3.62, 13.33, 0.5,
             font_size=14, color=GRAY_MID, align=PP_ALIGN.CENTER)

# 키워드 태그
tags = ["로그라이크 액션 RPG", "모던 다크 동화", "아서왕 전설", "PC (Steam)"]
tag_x = 2.4
for tag in tags:
    add_rect(slide, tag_x, 4.7, 2.1, 0.38, fill_color=BG_CARD,
             line_color=GOLD, line_width=12000)
    add_text_box(slide, tag, tag_x, 4.72, 2.1, 0.38,
                 font_size=10, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
    tag_x += 2.25

add_text_box(slide, "\"죽어도 영혼은 남는다. 멀린의 의지가 다시 불러온다.\"",
             0, 5.5, 13.33, 0.5, font_size=13, color=GRAY_LIGHT, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# 슬라이드 2 — 목차
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_slide_title(slide, "목차", "Table of Contents")

sections = [
    ("01", "프로젝트 개요",      "게임 기본 정보 · 핵심 컨셉 · 레퍼런스"),
    ("02", "세계관 & 시나리오",  "배경 · 핵심 인물 · 서사 아크 · 빌드업"),
    ("03", "코어 게임 루프",     "거시적/미시적 루프 · 씬 구성"),
    ("04", "영웅 유물 시스템",   "5종 영웅 · 메커닉 · 해금 시스템"),
    ("05", "장비 & 빌드",       "장비 구조 · 조합표 · 아이템 이중 효과"),
    ("06", "런 내 성장",        "스탯 레이어 · 아이템 · 시너지 · 방버프"),
    ("07", "메타 진행",         "재화 3종 · 멀린의 서약 · 유물 해방/각성"),
    ("08", "챕터 & 레벨 디자인","챕터 구성 · 보스 설계 · 노드 맵"),
    ("09", "비주얼 & 톤",       "아트 방향 · 챕터별 컬러 · UI/UX"),
]

col1 = sections[:5]
col2 = sections[5:]

for i, (num, title, desc) in enumerate(col1):
    y = 1.1 + i * 1.12
    add_rect(slide, 0.4, y, 6.0, 0.95, fill_color=BG_CARD, line_color=GRAY_DARK, line_width=10000)
    add_rect(slide, 0.4, y, 0.7, 0.95, fill_color=BG_CARD2)
    add_text_box(slide, num, 0.4, y + 0.18, 0.7, 0.5, font_size=16, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
    add_text_box(slide, title, 1.2, y + 0.06, 4.9, 0.35, font_size=13, bold=True, color=WHITE)
    add_text_box(slide, desc, 1.2, y + 0.42, 4.9, 0.35, font_size=9.5, color=GRAY_LIGHT)

for i, (num, title, desc) in enumerate(col2):
    y = 1.1 + i * 1.12
    add_rect(slide, 6.9, y, 6.0, 0.95, fill_color=BG_CARD, line_color=GRAY_DARK, line_width=10000)
    add_rect(slide, 6.9, y, 0.7, 0.95, fill_color=BG_CARD2)
    add_text_box(slide, num, 6.9, y + 0.18, 0.7, 0.5, font_size=16, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
    add_text_box(slide, title, 7.7, y + 0.06, 4.9, 0.35, font_size=13, bold=True, color=WHITE)
    add_text_box(slide, desc, 7.7, y + 0.42, 4.9, 0.35, font_size=9.5, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 01 — 프로젝트 개요
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "01  프로젝트 개요", "Project Overview")

slide = new_slide(prs)
add_slide_title(slide, "01  프로젝트 개요", "게임 기본 정보 · 핵심 컨셉 · 레퍼런스")

# 좌측: 기본 정보
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

# 중앙: 핵심 컨셉
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

# 중앙 하단: 레퍼런스
add_card(slide, 4.8, 4.05, 4.8, 2.9, "레퍼런스 게임")
refs = [
    ("Hades",        "메타 진행 구조, 실패=성장 패러다임"),
    ("Dead Cells",   "빠른 전투 리듬, 런 내 빌드 다양성"),
    ("Hollow Knight","다크 동화 비주얼 톤"),
    ("Path of Exile","스탯 레이어 깊이, 빌드 다양성"),
]
for i, (game, desc) in enumerate(refs):
    y = 4.55 + i * 0.55
    add_rect(slide, 5.0, y, 4.4, 0.46, fill_color=BG_CARD2)
    add_text_box(slide, game, 5.15, y + 0.04, 1.5, 0.36, font_size=10, bold=True, color=GOLD)
    add_text_box(slide, desc, 6.7, y + 0.08, 2.6, 0.28, font_size=9, color=GRAY_LIGHT)

# 우측: 키워드
add_card(slide, 9.8, 1.05, 3.1, 2.8, "핵심 키워드")
keywords = ["모던 다크 동화", "아서왕 전설", "영웅 유물", "메타 진행", "빌드 다양성", "시나리오 연동"]
for i, kw in enumerate(keywords):
    y = 1.55 + i * 0.38
    add_rect(slide, 9.95, y, 2.8, 0.3, fill_color=RGBColor(0x1A, 0x22, 0x38),
             line_color=GOLD, line_width=8000)
    add_text_box(slide, kw, 9.95, y + 0.02, 2.8, 0.28, font_size=10, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)

add_card(slide, 9.8, 4.05, 3.1, 2.9, "장르 포지셔닝")
add_text_box(slide,
    "로그라이크의\n매 런 신선함\n\n+\n\n액션 RPG의\n빌드 깊이\n\n+\n\n서사 연동\n성장의 의미",
    9.95, 4.45, 2.8, 2.4, font_size=10.5, color=WHITE, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# SECTION 02 — 세계관 & 시나리오
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "02  세계관 & 시나리오", "World & Scenario")

# 슬라이드: 배경 & 핵심 인물
slide = new_slide(prs)
add_slide_title(slide, "02  세계관 — 배경 & 핵심 인물")

add_card(slide, 0.4, 1.05, 6.0, 2.8, "배경 설정")
add_text_box(slide,
    "아서왕 전설의 세계. 최고의 마법사 멀린이 의문의 외부 존재에 의해 살해된다.\n"
    "그의 죽음과 동시에 세계는 부패하기 시작하고, 자연·고대 존재·인간 영웅들이\n"
    "차례로 잠식된다.\n\n"
    "멀린의 잔존 의지는 사후에도 흩어지지 않고 하나의 목적으로 수렴한다.\n"
    "그는 세계 너머에서 신성한 영혼(주인공)을 소환하고,\n"
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

# 핵심 인물
chars = [
    ("멀린",     GOLD,       "사망한 마법사. 잔존 의지로 주인공을 안내.\n런마다 목소리로 대화. 점점 약해짐."),
    ("주인공",   TEAL,       "신성한 영혼. 영웅 유물(육체)을 빌려 전투.\n사망 시 멀린이 복구 → 재도전."),
    ("모드레드",  RGBColor(0xCC,0x55,0x55), "3챕터 보스. 원탁의 기사이자 배신자.\n리치에게 잠식된 첫 번째 영웅."),
    ("리치",     PURPLE,     "최종 보스. 멀린의 육체를 탈취한 외부 존재.\n멀린의 전성기 능력을 그대로 복사."),
    ("모르가나",  RGBColor(0x88,0x44,0xAA), "포스트 엔딩 암시. 멀린의 제자이자 숙적.\n리치를 통해 멀린을 도구로 삼은 진짜 배후."),
]
for i, (name, color, desc) in enumerate(chars):
    x = 6.65 + (i % 3) * 2.2
    y = 1.05 + (i // 3) * 3.05
    h = 2.7
    add_rect(slide, x, y, 2.05, h, fill_color=BG_CARD, line_color=color, line_width=18000)
    add_rect(slide, x, y, 2.05, 0.5, fill_color=BG_CARD2)
    add_text_box(slide, name, x, y + 0.08, 2.05, 0.36, font_size=13, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, x + 0.1, y + 0.6, 1.85, h - 0.7, font_size=9.5, color=GRAY_LIGHT)

# 슬라이드: 서사 아크
slide = new_slide(prs)
add_slide_title(slide, "02  서사 아크 — 챕터별 빌드업")

chapters_story = [
    ("CH.1\n부패의 숲",   TEAL,   "그린 나이트",   "세상이 이상하다.\n원인 불명.",
     "영혼이여, 근원을 찾아라."),
    ("CH.2\n용암 대지",   GOLD,   "이 드레이그 고흐", "고대 존재까지 잠식.\n하나의 의지가 있음.",
     "이 부패는 의지를 가지고 있어."),
    ("CH.3\n잠식된 성채", RGBColor(0xCC,0x55,0x55), "모드레드", "영웅이 잠식됨.\n복사의 흔적 발견.",
     "내 기억이 흐릿해지고 있어..."),
    ("CH.4\n암흑대지",    PURPLE, "리치 (멀린의 육체)", "복사체 연속전.\n진실 완전히 드러남.",
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
add_slide_title(slide, "03  코어 게임 루프")

# 거시적 루프 (좌)
add_card(slide, 0.4, 1.05, 5.8, 5.9, "거시적 루프 (Macro Loop)")
loop_items = [
    (GOLD,   "허브: 멀린의 탑 잔해",      "영웅 유물 + 장비 선택"),
    (TEAL,   "런 시작",                   "챕터 탐색 시작"),
    (WHITE,  "챕터 1~4 진행",             "방 탐색 · 전투 · 아이템"),
    (RGBColor(0xCC,0x55,0x55), "사망 또는 클리어", ""),
    (GOLD,   "허브 복귀",                 "재화 소비 · 성장"),
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

# 미시적 루프 (우)
add_card(slide, 6.5, 1.05, 6.4, 5.9, "미시적 루프 (Micro Loop — 인게임)")
micro = [
    (TEAL,   "방 입장",           "방 버프 3~5개 중 1개 선택"),
    (WHITE,  "전투 수행",         "영웅 메커닉 + 장비 조합 활용"),
    (GOLD,   "아이템 획득",       "Staging → 그리드 배치 → 효과 발동"),
    (TEAL,   "방 클리어 보상",    "골드 · 아이템 · 봉인된 기억"),
    (WHITE,  "노드 맵에서 선택",  "전투방/아이템방/상점방/엘리트방"),
    (GOLD,   "보스방",            "격파 시 유물 코어 획득 (첫 격파)"),
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

# 시스템 개요 슬라이드
slide = new_slide(prs)
add_slide_title(slide, "04  영웅 유물 — 시스템 개요")

add_card(slide, 0.4, 1.05, 12.5, 1.5, "설계 원칙")
add_text_box(slide,
    "영웅의 이름은 잊혀졌다. 남은 건 유물뿐이다.  —  패시브 이름·스킬·대사로만 정체를 암시. 플레이어가 직접 유추.",
    0.6, 1.35, 12.1, 0.45, font_size=12, color=GOLD_LIGHT)
add_text_box(slide,
    "모든 영웅이 모든 장비 사용 가능  |  영웅마다 고유 패시브 + 고유 스킬 + 고유 메커닉 보유  |  해금형 순차 개방",
    0.6, 1.85, 12.1, 0.45, font_size=11, color=GRAY_LIGHT)

# 영웅 5종 요약
hero_data = [
    ("성배의 수호자", TEAL,   "⚔ 방어형",  "신성 게이지", "기본 제공",    "HP 150 / ATK 80 / DEF 40"),
    ("원탁의 균열",   GOLD,   "⚡ 공격형",  "콤보 게이지", "기억 ×3",      "HP 120 / ATK 110 / DEF 20"),
    ("여정의 창",     WHITE,  "🏃 돌진형",  "여정 스택",   "기억 ×5",      "HP 135 / ATK 95 / DEF 30"),
    ("태양의 서약",   RGBColor(0xFF,0xB8,0x40), "💪 중전사형", "태양 타이머", "기억 ×8", "HP 140 / ATK 105 / DEF 35"),
    ("비련의 화살",   PURPLE, "🎯 원거리형", "거리 시스템", "기억 ×12",     "HP 100 / ATK 115 / DEF 15"),
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

# 영웅별 상세 슬라이드 (2종 대표)
for hero_info in [
    {
        "name": "성배의 수호자  [갈라하드 암시]",
        "color": TEAL,
        "style": "방어형 / 초보 친화 / 진입장벽 낮음",
        "stats": [("HP", "150"), ("ATK", "80"), ("DEF", "40"), ("SPD", "×1.0")],
        "passives": [
            ("성스러운 방패", "방어 시 받은 피해의 30%를 전방 적에게 반사"),
            ("아버지의 죄", "피격 시 신성 게이지 +15 / 방어 성공 시 +25 충전"),
        ],
        "skill": ("성배의 빛", "쿨다운 18초", "전방 4m 범위 신성 폭발. 피해 공격력 ×180%\n게이지 30 이상 시 범위 +50%, 피해 +50%"),
        "mechanic": ("신성 게이지", [
            "최대 게이지: 100",
            "피격 +15 / 방어성공 +25 / 스킬 사용 +20",
            "만충 시: 5초 무적 + 공격력 150%",
            "이후 쿨다운: 30초",
        ]),
        "transforms": [
            ("죄의 대가", "공격 시 게이지 +8 → 공격형 전환"),
            ("성배의 축복", "50마다 소폭 발동 → 지속형"),
            ("순교의 서약", "만충 효과 5초 → 10초"),
        ],
    },
    {
        "name": "태양의 서약  [가웨인 암시]",
        "color": RGBColor(0xFF,0xB8,0x40),
        "style": "중전사형 / 타이밍 집중 / 버스트 딜러",
        "stats": [("HP", "140"), ("ATK", "105"), ("DEF", "35"), ("SPD", "×0.85")],
        "passives": [
            ("정오의 서약", "태양 타이머 강화 구간에서 공격력 +200%"),
            ("명예의 기사", "HP 50% 이상 시 방어력 +25%, 받는 피해 -10%"),
        ],
        "skill": ("태양의 강타", "쿨다운 20초", "전방 3m 광역 내리찍기. 피해 공격력 ×250%\n강화 구간 사용 시 피해 +100%, 범위 +80%"),
        "mechanic": ("태양 타이머", [
            "강화 구간 20초: 공격력 +200%, 방어 +30%",
            "약화 구간 15초: 공격력 -20%",
            "전략: 강화=공세 / 약화=회피·준비",
        ]),
        "transforms": [
            ("영원한 정오", "타이머 고정 → 지속 딜 빌드"),
            ("일몰의 역설", "약화 구간도 강화 50% 적용"),
            ("태양의 분노", "피격 시 강화 타이머 +3초"),
        ],
    },
]:
    slide = new_slide(prs)
    color = hero_info["color"]
    add_slide_title(slide, f"04  영웅 유물 — {hero_info['name']}", hero_info["style"])
    add_rect(slide, 0, 0, 13.33, 0.06, fill_color=color)

    # 스탯
    add_card(slide, 0.4, 1.05, 3.0, 1.7, "기본 스탯", color)
    for i, (k, v) in enumerate(hero_info["stats"]):
        x = 0.55 + i * 0.72
        add_rect(slide, x, 1.5, 0.65, 0.85, fill_color=BG_CARD2)
        add_text_box(slide, v, x, 1.55, 0.65, 0.38, font_size=15, bold=True, color=color, align=PP_ALIGN.CENTER)
        add_text_box(slide, k, x, 1.9, 0.65, 0.28, font_size=8, color=GRAY_MID, align=PP_ALIGN.CENTER)

    # 패시브
    add_card(slide, 0.4, 2.9, 3.0, 2.2, "고유 패시브", color)
    for i, (pname, pdesc) in enumerate(hero_info["passives"]):
        y = 3.38 + i * 0.75
        add_rect(slide, 0.55, y, 2.7, 0.62, fill_color=BG_CARD2)
        add_text_box(slide, pname, 0.7, y + 0.03, 2.4, 0.26, font_size=10, bold=True, color=color)
        add_text_box(slide, pdesc, 0.7, y + 0.3, 2.4, 0.28, font_size=8.5, color=GRAY_LIGHT)

    # 스킬
    add_card(slide, 3.6, 1.05, 4.8, 1.7, "고유 스킬", color)
    sname, scd, sdesc = hero_info["skill"]
    add_text_box(slide, sname, 3.8, 1.42, 3.0, 0.35, font_size=13, bold=True, color=color)
    add_text_box(slide, scd, 6.9, 1.5, 1.3, 0.25, font_size=9, color=GRAY_MID, align=PP_ALIGN.RIGHT)
    add_text_box(slide, sdesc, 3.8, 1.82, 4.4, 0.82, font_size=10, color=GRAY_LIGHT)

    # 메커닉
    mname, mbullets = hero_info["mechanic"]
    add_card(slide, 3.6, 2.9, 4.8, 2.2, f"고유 메커닉 — {mname}", color)
    for i, b in enumerate(mbullets):
        add_text_box(slide, "▸  " + b, 3.8, 3.38 + i * 0.44, 4.4, 0.38, font_size=10, color=WHITE)

    # 메커닉 변형 아이템
    add_card(slide, 8.6, 1.05, 4.3, 4.05, "메커닉 변형 아이템", color)
    for i, (iname, idesc) in enumerate(hero_info["transforms"]):
        y = 1.55 + i * 1.15
        add_rect(slide, 8.8, y, 3.9, 0.95, fill_color=BG_CARD2)
        add_rect(slide, 8.8, y, 3.9, 0.38, fill_color=RGBColor(0x20, 0x18, 0x30))
        add_text_box(slide, iname, 8.95, y + 0.05, 3.6, 0.28, font_size=11, bold=True, color=GOLD)
        add_text_box(slide, idesc, 8.95, y + 0.45, 3.6, 0.38, font_size=9.5, color=GRAY_LIGHT)

    # 해금 정보
    add_card(slide, 0.4, 5.25, 7.8, 1.8, "해금 시 출력 대사", color)

# ══════════════════════════════════════════════════════════════
# SECTION 05 — 장비 & 빌드
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "05  장비 & 빌드 시스템", "Equipment & Build System")

slide = new_slide(prs)
add_slide_title(slide, "05  장비 & 빌드 — 조합표 & 아이템 구조")

# 조합표
add_card(slide, 0.4, 1.05, 8.5, 5.9, "영웅 × 장비 시너지 조합표")
headers = ["영웅 유물", "카타나", "대검", "창", "활", "검+방패"]
col_w = [1.9, 1.2, 1.2, 1.2, 1.2, 1.2]
rows = [
    ["성배의 수호자", "보통",    "탱딜★", "표준", "지연",    "최적★★"],
    ["원탁의 균열",   "최적★★", "고위험★","콤보★","안전딜",  "방어콤보"],
    ["여정의 창",     "빠른누적","보스킬★","시너지★★","안전", "균형형"],
    ["태양의 서약",   "연속딜",  "버스트★","돌진버스트","원거리","방어유지★"],
    ["비련의 화살",   "위험",    "최고위험","히트앤런","최적★★","방어방지"],
]
add_table_rows(slide, headers, rows, 0.55, 1.55, col_w, row_height=0.82)

# 아이템 이중 효과
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
    ("Common",   "55%", RGBColor(0x88,0x88,0x88), "범용 스탯"),
    ("Rare",     "30%", TEAL,                       "조건부 + 친화"),
    ("Epic",     "12%", PURPLE,                     "메커닉 변형"),
    ("Legendary", "3%", GOLD,                       "빌드 전환"),
]
for i, (grade, pct, color, desc) in enumerate(grade_data):
    y = 4.75 + i * 0.52
    add_rect(slide, 9.35, y, 0.8, 0.42, fill_color=color)
    add_text_box(slide, grade, 9.35, y + 0.06, 0.8, 0.3, font_size=8.5, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, pct, 10.25, y + 0.06, 0.7, 0.3, font_size=11, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, 11.0, y + 0.1, 1.8, 0.25, font_size=9, color=GRAY_LIGHT)

# ══════════════════════════════════════════════════════════════
# SECTION 06 & 07 — 성장 시스템
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "06 · 07  성장 시스템", "Growth System")

slide = new_slide(prs)
add_slide_title(slide, "06  런 내 성장 — 스탯 레이어 & 시너지")

# 스탯 레이어
add_card(slide, 0.4, 1.05, 5.5, 5.9, "스탯 레이어 구조 (7단계 합산)")
layers = [
    (GOLD,   "① 기본",       "영웅 유물 기본 스탯"),
    (TEAL,   "② 서약",       "멀린의 서약 영구 보너스"),
    (PURPLE, "③ 각성",       "유물 각성 효과"),
    (WHITE,  "④ 장비",       "선택한 장비 스탯"),
    (GOLD,   "⑤ 아이템",     "그리드 배치 아이템 합산"),
    (TEAL,   "⑥ 방버프",     "현재 활성 방버프"),
    (PURPLE, "⑦ 시너지",     "그리드 시너지 Always/조건부"),
]
for i, (color, num, desc) in enumerate(layers):
    y = 1.55 + i * 0.74
    add_rect(slide, 0.6, y, 5.1, 0.62, fill_color=BG_CARD2)
    add_rect(slide, 0.6, y, 0.08, 0.62, fill_color=color)
    add_text_box(slide, num, 0.85, y + 0.08, 1.3, 0.28, font_size=11, bold=True, color=color)
    add_text_box(slide, desc, 2.25, y + 0.1, 3.3, 0.28, font_size=10.5, color=GRAY_LIGHT)

# 방버프
add_card(slide, 6.2, 1.05, 3.2, 5.9, "방 버프 시스템")
add_text_box(slide, "방 입장 시 3~5개 중 1개 선택", 6.4, 1.5, 2.8, 0.35, font_size=10, color=GRAY_LIGHT)
buff_tiers = [("Tier 1", "3방 지속", "×1.0"), ("Tier 2", "4방 지속", "×1.5"), ("Tier 3", "5방 지속", "×2.0")]
for i, (t, dur, mult) in enumerate(buff_tiers):
    y = 2.0 + i * 0.75
    colors_t = [GRAY_MID, TEAL, GOLD]
    add_rect(slide, 6.4, y, 2.8, 0.62, fill_color=BG_CARD2)
    add_text_box(slide, t, 6.55, y + 0.08, 0.85, 0.28, font_size=11, bold=True, color=colors_t[i])
    add_text_box(slide, dur, 7.45, y + 0.1, 1.0, 0.25, font_size=9.5, color=GRAY_LIGHT)
    add_text_box(slide, mult, 8.5, y + 0.08, 0.55, 0.28, font_size=11, bold=True, color=colors_t[i], align=PP_ALIGN.CENTER)
add_rect(slide, 6.4, 4.4, 2.8, 0.02, fill_color=GRAY_DARK)
add_text_box(slide, "최대 10슬롯 (초과 시 오래된 버프 제거)", 6.4, 4.5, 2.8, 0.3, font_size=9, color=GRAY_LIGHT)
add_rect(slide, 6.4, 4.9, 2.8, 0.55, fill_color=RGBColor(0x1A, 0x22, 0x15))
add_text_box(slide, "보스 처치 시\n전체 버프 Tier +1 승급 + 지속 리셋", 6.55, 4.95, 2.5, 0.45, font_size=9.5, color=TEAL)

# 그리드 시너지 트리거
add_card(slide, 9.6, 1.05, 3.3, 5.9, "그리드 시너지 트리거")
triggers = [
    ("Always",   GOLD,   "그리드 완성 즉시\n스탯 영구 반영"),
    ("OnHit",    TEAL,   "공격 적중 시 스택 누적\n최대 스택까지, duration 유지"),
    ("OnLowHp",  RGBColor(0xCC,0x55,0x55), "HP ≤ threshold\n조건 만족 시 활성화"),
]
for i, (t, color, desc) in enumerate(triggers):
    y = 1.55 + i * 1.72
    add_rect(slide, 9.8, y, 2.9, 1.5, fill_color=BG_CARD2, line_color=color, line_width=15000)
    add_text_box(slide, t, 9.8, y + 0.1, 2.9, 0.45, font_size=16, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, 9.95, y + 0.6, 2.6, 0.75, font_size=10, color=GRAY_LIGHT)

# 메타 진행 슬라이드
slide = new_slide(prs)
add_slide_title(slide, "07  메타 진행 — 재화 3종 & 성장 구조")

# 재화 3종
for i, (name, rarity, color, obtain, use, icon) in enumerate([
    ("영혼의 잔재", "★ 풍부", GOLD, "매 방 클리어 · 보스 처치", "멀린의 서약\n영구 스탯 강화", "◈"),
    ("봉인된 기억", "★★★ 희귀", TEAL, "드문 방 보상 · 챕터 보스", "유물 해방\n새 영웅 언락", "◉"),
    ("유물 코어",   "★★★★★ 극희귀", PURPLE, "보스 첫 격파 시에만", "유물 각성\n메커닉 심화", "✦"),
]):
    x = 0.4 + i * 4.3
    add_rect(slide, x, 1.05, 4.1, 3.5, fill_color=BG_CARD, line_color=color, line_width=20000)
    add_rect(slide, x, 1.05, 4.1, 0.55, fill_color=BG_CARD2)
    add_text_box(slide, name, x, 1.1, 4.1, 0.45, font_size=15, bold=True, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, icon, x, 1.65, 4.1, 0.8, font_size=36, color=color, align=PP_ALIGN.CENTER)
    add_text_box(slide, rarity, x, 2.5, 4.1, 0.3, font_size=10, color=GOLD_LIGHT, align=PP_ALIGN.CENTER)
    add_rect(slide, x + 0.2, 2.85, 3.7, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "획득", x + 0.2, 2.95, 0.7, 0.25, font_size=8.5, color=GRAY_MID, bold=True)
    add_text_box(slide, obtain, x + 1.0, 2.95, 2.9, 0.3, font_size=9.5, color=GRAY_LIGHT)
    add_text_box(slide, "소비", x + 0.2, 3.32, 0.7, 0.25, font_size=8.5, color=color, bold=True)
    add_text_box(slide, use, x + 1.0, 3.3, 2.9, 0.5, font_size=10, color=WHITE, bold=True)

# 런당 평균 획득
add_card(slide, 0.4, 4.75, 7.8, 2.3, "런당 평균 획득량")
headers2 = ["재화", "CH.1 클리어", "CH.2 클리어", "CH.3 클리어", "CH.4 클리어"]
cw2 = [2.0, 1.4, 1.4, 1.4, 1.4]
rows2 = [
    ["영혼의 잔재", "400~600", "700~900", "900~1,100", "1,200~1,500"],
    ["봉인된 기억", "0~1",     "0~1",     "1~2",       "1~2"],
    ["유물 코어",   "0~1",     "0~1",     "0~1",       "0~1"],
]
add_table_rows(slide, headers2, rows2, 0.6, 5.22, cw2, row_height=0.5)

# 성장 시간대
add_card(slide, 8.5, 4.75, 4.5, 2.3, "성장 시간대")
timeline = [
    ("1~10런",  TEAL,   "서약 생존 안정화"),
    ("10~30런", GOLD,   "유물 해방 + 서약 확장"),
    ("30~50런", PURPLE, "유물 각성 시작"),
    ("50런+",   WHITE,  "전체 각성 · 챌린지"),
]
for i, (span, color, goal) in enumerate(timeline):
    y = 5.2 + i * 0.45
    add_rect(slide, 8.7, y, 1.15, 0.36, fill_color=color)
    add_text_box(slide, span, 8.7, y + 0.04, 1.15, 0.28, font_size=8.5, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, goal, 9.95, y + 0.06, 2.8, 0.25, font_size=9.5, color=GRAY_LIGHT)

# 멀린의 서약 상세
slide = new_slide(prs)
add_slide_title(slide, "07  멀린의 서약 — 영구 강화 항목 & 비용")

add_card(slide, 0.4, 1.05, 12.5, 0.8, "구조 설명")
add_text_box(slide,
    "6개 슬롯  |  각 슬롯마다 좌(적색) / 우(청색) 중 선택  |  언제든 리셋 가능 (소비 잔재 전액 환급)  |  Tier 1~5, 비용 2배씩 증가",
    0.6, 1.2, 12.1, 0.45, font_size=11, color=GRAY_LIGHT)

headers3 = ["슬롯", "좌 선택 (적색)", "우 선택 (청색)", "Tier 1 비용", "Tier 5 비용", "합계 비용"]
cw3 = [1.5, 3.2, 3.2, 1.2, 1.2, 1.5]
rows3 = [
    ["생존",   "MaxHP +15/Tier",         "피격 시 HP 1% 회복",       "100",  "1,600",  "3,100"],
    ["공격",   "기본 공격력 +5%/Tier",   "치명타율 +2%/Tier",        "150",  "2,400",  "4,650"],
    ["이동",   "이동속도 +3%/Tier",      "구르기 쿨다운 -5%/Tier",   "120",  "1,920",  "3,720"],
    ["지속력", "방 클리어 HP 2% 회복",   "보스전 피해 -5%/Tier",     "200",  "3,200",  "6,200"],
    ["획득",   "잔재 획득 +10%/Tier",    "기억 드롭률 +5%/Tier",     "300",  "4,800",  "9,300"],
    ["아이템", "등급 상승 확률 +5%/Tier","변형 아이템 드롭 +8%/Tier","400",  "6,400", "12,400"],
]
add_table_rows(slide, headers3, rows3, 0.4, 2.05, cw3, row_height=0.72)

add_rect(slide, 0.4, 6.38, 12.5, 0.55, fill_color=BG_CARD2)
add_text_box(slide, "전체 최대 강화 완료 시 필요 영혼의 잔재:  약 42,000  (100런 이상 소요 예상)",
             0.6, 6.44, 12.1, 0.38, font_size=11, color=GOLD_LIGHT, bold=True)

# ══════════════════════════════════════════════════════════════
# SECTION 08 — 챕터 & 레벨 디자인
# ══════════════════════════════════════════════════════════════
add_section_divider(prs, "08  챕터 & 레벨 디자인", "Chapter & Level Design")

slide = new_slide(prs)
add_slide_title(slide, "08  챕터 구성 & 노드 맵 레벨 디자인")

# 챕터 개요 테이블
add_card(slide, 0.4, 1.05, 12.5, 2.0, "챕터 개요")
headers4 = ["챕터", "배경", "보스", "몬스터 배수", "보상 배수", "방 수 범위"]
cw4 = [1.0, 2.2, 2.8, 1.5, 1.5, 1.9]
rows4 = [
    ["CH.1", "부패의 숲",    "그린 나이트",           "×1.0", "×1.0", "7~10"],
    ["CH.2", "용암 대지",    "이 드레이그 고흐",       "×1.5", "×1.4", "8~11"],
    ["CH.3", "잠식된 성채",  "모드레드",              "×2.2", "×1.8", "9~12"],
    ["CH.4", "암흑대지",     "리치 (멀린의 육체)",     "×3.0", "×2.5", "6~8"],
]
add_table_rows(slide, headers4, rows4, 0.55, 1.5, cw4, row_height=0.47,
               row_colors=[None, None, None, RGBColor(0x1A, 0x10, 0x24)])

# 노드 맵 구조
add_card(slide, 0.4, 3.25, 5.8, 4.0, "노드 맵 레이어 구조")
add_text_box(slide,
    "[시작 노드]  (1개)\n"
    "       ↓\n"
    "[중간 레이어]  (5~8층)\n"
    "  분지 확률 35~45%\n"
    "  레이어마다 2~3개 노드 분기\n"
    "       ↓\n"
    "[보스 노드]  (1개)",
    0.6, 3.7, 5.4, 2.3, font_size=12, color=WHITE)

# 방 타입 분포
add_card(slide, 6.5, 3.25, 3.0, 4.0, "방 타입 분포")
room_types = [
    ("전투방",  "55%", TEAL),
    ("아이템방","15%", GOLD),
    ("상점방",  "10%", PURPLE),
    ("특수방",  "10%", GRAY_MID),
    ("엘리트방","10%", RGBColor(0xCC,0x55,0x55)),
]
for i, (rt, pct, color) in enumerate(room_types):
    y = 3.75 + i * 0.62
    bar_w = float(pct.replace('%','')) / 100 * 2.5
    add_rect(slide, 6.7, y, 2.5, 0.42, fill_color=BG_CARD2)
    add_rect(slide, 6.7, y, bar_w, 0.42, fill_color=color)
    add_text_box(slide, rt, 6.75, y + 0.07, 1.5, 0.28, font_size=10, color=BG_DARK if bar_w > 1.0 else WHITE, bold=True)
    add_text_box(slide, pct, 9.3, y + 0.07, 0.6, 0.28, font_size=10, bold=True, color=color, align=PP_ALIGN.RIGHT)

# 상점 가격
add_card(slide, 9.7, 3.25, 3.2, 4.0, "상점 가격표")
shop_data = [
    ("Common 아이템",  "50~120 G"),
    ("Rare 아이템",    "150~280 G"),
    ("Epic 아이템",    "350~500 G"),
    ("체력 회복",      "80 G (HP 30%)"),
    ("버프 구매",      "100~200 G"),
]
for i, (item, price) in enumerate(shop_data):
    y = 3.75 + i * 0.62
    add_rect(slide, 9.9, y, 2.8, 0.5, fill_color=BG_CARD2)
    add_text_box(slide, item, 10.05, y + 0.08, 1.6, 0.3, font_size=9.5, color=GRAY_LIGHT)
    add_text_box(slide, price, 11.7, y + 0.08, 0.9, 0.3, font_size=9.5, bold=True, color=GOLD, align=PP_ALIGN.RIGHT)

# 보스 상세 슬라이드
slide = new_slide(prs)
add_slide_title(slide, "08  보스 상세 설계")

boss_list = [
    {
        "name": "그린 나이트",
        "chapter": "CH.1 — 부패의 숲",
        "color": TEAL,
        "origin": "가웨인과 녹색 기사",
        "hp": "기준 HP ×1.0",
        "patterns": ["근접 강타", "가시 투척", "덩굴 함정", "HP 50% 이하: 재생 시작 (초당 0.5%)"],
        "weakness": "화염 피해 시 재생 중단",
        "reward": "잔재 ×250 · 기억 50% · 코어 (첫 격파)",
        "lore": "숲의 수호자가 부패로 거대화.\n가웨인 유물 사용 시 특별 대사.",
    },
    {
        "name": "이 드레이그 고흐",
        "chapter": "CH.2 — 용암 대지",
        "color": RGBColor(0xFF,0x70,0x20),
        "origin": "멀린이 예언한 웨일스의 붉은 용",
        "hp": "기준 HP ×1.5",
        "patterns": ["화염 브레스", "꼬리 휩쓸기", "낙하 충격", "HP 30% 이하: 광역 화염 폭발 (페이즈 2)"],
        "weakness": "수속성 공격 시 +30% 피해",
        "reward": "잔재 ×400 · 기억 60% · 코어 (첫 격파)",
        "lore": "멀린이 예언한 존재가 부패의 도구로.\n격파 후 멀린의 자책 대사.",
    },
    {
        "name": "모드레드",
        "chapter": "CH.3 — 잠식된 성채",
        "color": RGBColor(0xCC,0x44,0x44),
        "origin": "원탁의 기사이자 배신자",
        "hp": "기준 HP ×2.2",
        "patterns": ["검술 3연격", "그림자 분신", "광역 참격", "HP 60/30%: 분신 2체 추가 소환"],
        "weakness": "성스러운/빛 속성 +25% 피해",
        "reward": "잔재 ×600 · 기억 70% · 코어 (첫 격파)",
        "lore": "격파 직전 잠시 의식 회복.\n\"그 자는 모든 것을 복사했어. 너희까지도...\"",
    },
    {
        "name": "리치 (멀린의 육체)",
        "chapter": "CH.4 — 암흑대지",
        "color": PURPLE,
        "origin": "멀린의 육체를 탈취한 외부 존재",
        "hp": "기준 HP ×3.0",
        "patterns": ["마법 투사체", "영역 봉인", "멀린 지팡이 강타", "HP 40%: 전장 어둠화 + 복사체 재소환"],
        "weakness": "없음 (근원)",
        "reward": "잔재 ×1,000 · 기억 ×3 · 코어 (첫 격파)",
        "lore": "복사체 연속전 → 멀린 마지막 메시지\n→ 리치 등장. 격파 후 모르가나 암시.",
    },
]

for i, boss in enumerate(boss_list):
    x = 0.35 + i * 3.27
    color = boss["color"]
    add_rect(slide, x, 1.05, 3.1, 6.0, fill_color=BG_CARD, line_color=color, line_width=15000)
    add_rect(slide, x, 1.05, 3.1, 0.48, fill_color=BG_CARD2)
    add_text_box(slide, boss["chapter"], x, 1.08, 3.1, 0.22, font_size=8, color=GRAY_MID, align=PP_ALIGN.CENTER)
    add_text_box(slide, boss["name"], x, 1.28, 3.1, 0.28, font_size=13, bold=True, color=color, align=PP_ALIGN.CENTER)

    add_text_box(slide, boss["origin"], x + 0.15, 1.6, 2.8, 0.28, font_size=8.5, color=GRAY_MID)
    add_text_box(slide, f"HP: {boss['hp']}", x + 0.15, 1.9, 2.8, 0.28, font_size=10, bold=True, color=WHITE)
    add_rect(slide, x + 0.15, 2.22, 2.8, 0.02, fill_color=GRAY_DARK)

    add_text_box(slide, "공격 패턴", x + 0.15, 2.3, 2.0, 0.22, font_size=8, color=GRAY_MID, bold=True)
    for j, pat in enumerate(boss["patterns"]):
        add_text_box(slide, "▸ " + pat, x + 0.15, 2.55 + j * 0.36, 2.8, 0.3, font_size=8.5, color=GRAY_LIGHT)

    add_rect(slide, x + 0.15, 4.1, 2.8, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "약점", x + 0.15, 4.18, 0.8, 0.22, font_size=8, color=GRAY_MID, bold=True)
    add_text_box(slide, boss["weakness"], x + 0.15, 4.42, 2.8, 0.3, font_size=9, color=TEAL)

    add_rect(slide, x + 0.15, 4.77, 2.8, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, "처치 보상", x + 0.15, 4.85, 2.0, 0.22, font_size=8, color=GOLD, bold=True)
    add_text_box(slide, boss["reward"], x + 0.15, 5.1, 2.8, 0.3, font_size=9, color=GOLD_LIGHT)

    add_rect(slide, x + 0.15, 5.46, 2.8, 0.02, fill_color=GRAY_DARK)
    add_text_box(slide, boss["lore"], x + 0.15, 5.55, 2.8, 0.4, font_size=8.5, color=GRAY_LIGHT)

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

# 챕터 컬러
chapter_visuals = [
    ("CH.1\n부패의 숲",    RGBColor(0x2A,0x5C,0x2A), RGBColor(0x4A,0x1A,0x5C), "탁한 초록 + 검은 보라\n생명이 부패한 숲"),
    ("CH.2\n용암 대지",    RGBColor(0x8B,0x2A,0x0A), RGBColor(0x1A,0x0A,0x0A), "붉은 주황 + 검정\n뜨거운 침묵의 지형"),
    ("CH.3\n잠식된 성채",  RGBColor(0x2A,0x2A,0x4A), RGBColor(0x0A,0x0A,0x1A), "차가운 회색 + 남색\n영웅의 성채, 이제는 적의 거점"),
    ("CH.4\n암흑대지",     RGBColor(0x1A,0x0A,0x2A), RGBColor(0x08,0x04,0x10), "순수한 흑 + 희미한 보라\n빛이 존재하지 않는 근원"),
]
for i, (ch, c1, c2, desc) in enumerate(chapter_visuals):
    x = 0.4 + i * 3.27
    add_rect(slide, x, 2.5, 3.1, 2.0, fill_color=c2, line_color=c1, line_width=15000)
    add_rect(slide, x, 2.5, 1.55, 2.0, fill_color=c1)
    add_text_box(slide, ch, x, 4.6, 3.1, 0.5, font_size=11, bold=True, color=WHITE, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, x + 0.1, 5.15, 2.9, 0.7, font_size=9.5, color=GRAY_LIGHT)

# 허브 변화
add_card(slide, 0.4, 5.9, 12.5, 1.2, "허브 공간 — 멀린의 탑 잔해 상태 변화")
hub_states = [("초반 런", TEAL, "탑 잔해, 희미한 빛"), ("중반 런", GOLD, "빛 감소, 어두워짐"), ("후반 런", RGBColor(0x88,0x44,0xAA), "절반 잠식"), ("최종 전", WHITE, "한순간 밝아짐 (각성)")]
for i, (label, color, desc) in enumerate(hub_states):
    x = 0.6 + i * 3.2
    add_rect(slide, x, 6.08, 2.8, 0.28, fill_color=color)
    add_text_box(slide, label, x, 6.1, 2.8, 0.24, font_size=10, bold=True, color=BG_DARK, align=PP_ALIGN.CENTER)
    add_text_box(slide, desc, x, 6.42, 2.8, 0.28, font_size=9, color=GRAY_LIGHT, align=PP_ALIGN.CENTER)

# ══════════════════════════════════════════════════════════════
# 마지막 슬라이드 — 총정리
# ══════════════════════════════════════════════════════════════
slide = new_slide(prs)
add_rect(slide, 0, 0, 13.33, 0.08, fill_color=GOLD)
add_rect(slide, 0, 7.42, 13.33, 0.08, fill_color=GOLD)

add_text_box(slide, "RELIC FAIRY  —  핵심 요약", 0, 0.6, 13.33, 0.7,
             font_size=32, bold=True, color=GOLD, align=PP_ALIGN.CENTER)
add_rect(slide, 1.5, 1.38, 10.3, 0.03, fill_color=GRAY_DARK)

summary = [
    ("세계관",    TEAL,   "멀린 사망 → 리치(육체 탈취) → 모르가나(진짜 배후) / 모던 다크 동화 아서왕 전설"),
    ("영웅",      GOLD,   "5종 유물 (암시형 네이밍) / 고유 메커닉+패시브+스킬 / 해금형 순차 개방"),
    ("장비",      WHITE,  "모든 영웅 × 모든 장비 / 이중 효과 구조 / 드롭 가중치 조정"),
    ("런 내",     TEAL,   "7단계 스탯 레이어 / 그리드 시너지(3트리거) / 방버프(10슬롯, 티어 승급)"),
    ("메타",      GOLD,   "재화 3종 / 멀린의 서약(6슬롯, 리셋 가능) / 유물 해방 / 유물 각성"),
    ("레벨",      PURPLE, "챕터 4개 / 보스 4종 / 노드 맵 분기 / 방 5타입"),
]
for i, (cat, color, desc) in enumerate(summary):
    y = 1.5 + i * 0.92
    add_rect(slide, 0.5, y, 12.3, 0.78, fill_color=BG_CARD)
    add_rect(slide, 0.5, y, 0.06, 0.78, fill_color=color)
    add_text_box(slide, cat, 0.75, y + 0.1, 1.3, 0.35, font_size=13, bold=True, color=color)
    add_text_box(slide, desc, 2.2, y + 0.12, 10.4, 0.5, font_size=11, color=GRAY_LIGHT)

add_text_box(slide, "v0.1  |  2026.05  |  랠릭페어리 기획팀",
             0, 7.1, 13.33, 0.3, font_size=10, color=GRAY_DARK, align=PP_ALIGN.CENTER)

# ─── 저장 ────────────────────────────────────────────────────
OUTPUT = r"c:/Users/user/Documents/GitHub/Project_Abyss/Assets/Abyss/Docs/RelicFairy_Proposal.pptx"
prs.save(OUTPUT)
print("저장 완료: " + OUTPUT)
print("슬라이드 수: " + str(len(prs.slides)) + "장")
