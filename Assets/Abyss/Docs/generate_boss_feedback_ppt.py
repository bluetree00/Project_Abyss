"""보스 기획서 피드백 가이드라인 PPT 생성 스크립트"""

from pptx import Presentation
from pptx.util import Inches, Pt, Emu
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE

# ── 컬러 팔레트 ──
BG_DARK = RGBColor(0x1A, 0x1A, 0x2E)
BG_CARD = RGBColor(0x22, 0x22, 0x3A)
ACCENT_RED = RGBColor(0xE8, 0x4D, 0x4D)
ACCENT_ORANGE = RGBColor(0xF0, 0x9A, 0x56)
ACCENT_YELLOW = RGBColor(0xF0, 0xD0, 0x56)
ACCENT_GREEN = RGBColor(0x5A, 0xC8, 0x7A)
ACCENT_BLUE = RGBColor(0x5A, 0xA0, 0xE8)
TEXT_WHITE = RGBColor(0xF0, 0xF0, 0xF0)
TEXT_GRAY = RGBColor(0xB0, 0xB0, 0xB0)
TEXT_DIM = RGBColor(0x80, 0x80, 0x90)

prs = Presentation()
prs.slide_width = Inches(16)
prs.slide_height = Inches(9)

SLIDE_W = Inches(16)
SLIDE_H = Inches(9)


def add_bg(slide, color=BG_DARK):
    bg = slide.background
    fill = bg.fill
    fill.solid()
    fill.fore_color.rgb = color


def add_shape_rect(slide, left, top, width, height, fill_color, border_color=None):
    shape = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left, top, width, height)
    shape.fill.solid()
    shape.fill.fore_color.rgb = fill_color
    if border_color:
        shape.line.color.rgb = border_color
        shape.line.width = Pt(1.5)
    else:
        shape.line.fill.background()
    # 모서리 둥글기
    shape.adjustments[0] = 0.02
    return shape


def add_text_box(slide, left, top, width, height, text, font_size=18, color=TEXT_WHITE,
                 bold=False, alignment=PP_ALIGN.LEFT, font_name="맑은 고딕"):
    txBox = slide.shapes.add_textbox(left, top, width, height)
    tf = txBox.text_frame
    tf.word_wrap = True
    p = tf.paragraphs[0]
    p.text = text
    p.font.size = Pt(font_size)
    p.font.color.rgb = color
    p.font.bold = bold
    p.font.name = font_name
    p.alignment = alignment
    return txBox


def add_bullet_list(slide, left, top, width, height, items, font_size=16,
                    color=TEXT_WHITE, spacing=Pt(8)):
    txBox = slide.shapes.add_textbox(left, top, width, height)
    tf = txBox.text_frame
    tf.word_wrap = True
    for i, item in enumerate(items):
        if i == 0:
            p = tf.paragraphs[0]
        else:
            p = tf.add_paragraph()
        p.text = item
        p.font.size = Pt(font_size)
        p.font.color.rgb = color
        p.font.name = "맑은 고딕"
        p.space_after = spacing
    return txBox


def add_tag(slide, left, top, text, color=ACCENT_RED):
    w, h = Inches(1.8), Inches(0.4)
    shape = add_shape_rect(slide, left, top, w, h, color)
    shape.text_frame.paragraphs[0].text = text
    shape.text_frame.paragraphs[0].font.size = Pt(13)
    shape.text_frame.paragraphs[0].font.color.rgb = TEXT_WHITE
    shape.text_frame.paragraphs[0].font.bold = True
    shape.text_frame.paragraphs[0].font.name = "맑은 고딕"
    shape.text_frame.paragraphs[0].alignment = PP_ALIGN.CENTER
    shape.text_frame.paragraphs[0].space_before = Pt(0)
    return shape


def add_table_slide(slide, left, top, width, rows_data, col_widths, header_color=ACCENT_BLUE):
    row_count = len(rows_data)
    col_count = len(rows_data[0])
    row_h = Inches(0.45)
    table_h = row_h * row_count
    tbl_shape = slide.shapes.add_table(row_count, col_count, left, top, width, table_h)
    tbl = tbl_shape.table

    for ci, cw in enumerate(col_widths):
        tbl.columns[ci].width = cw

    for ri, row in enumerate(rows_data):
        for ci, cell_text in enumerate(row):
            cell = tbl.cell(ri, ci)
            cell.text = cell_text
            for p in cell.text_frame.paragraphs:
                p.font.size = Pt(13)
                p.font.name = "맑은 고딕"
                p.font.color.rgb = TEXT_WHITE
                if ri == 0:
                    p.font.bold = True
            cell.vertical_anchor = MSO_ANCHOR.MIDDLE
            # 배경
            cell.fill.solid()
            if ri == 0:
                cell.fill.fore_color.rgb = header_color
            else:
                cell.fill.fore_color.rgb = BG_CARD if ri % 2 == 1 else RGBColor(0x2A, 0x2A, 0x44)
    return tbl_shape


# ================================================================
# 슬라이드 1: 표지
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)

# 상단 라인
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_text_box(slide, Inches(1.5), Inches(2.5), Inches(13), Inches(1.5),
             "보스 기획서 2.0v", font_size=52, bold=True, color=TEXT_WHITE,
             alignment=PP_ALIGN.CENTER)

add_text_box(slide, Inches(1.5), Inches(4.0), Inches(13), Inches(1.0),
             "피드백 가이드라인", font_size=36, bold=False, color=ACCENT_RED,
             alignment=PP_ALIGN.CENTER)

add_text_box(slide, Inches(1.5), Inches(5.5), Inches(13), Inches(0.5),
             "대상: 왕의 정원 파수꾼  |  2026-04-17", font_size=18, color=TEXT_GRAY,
             alignment=PP_ALIGN.CENTER)

# 하단 라인
add_shape_rect(slide, Inches(0), Inches(8.94), SLIDE_W, Inches(0.06), ACCENT_RED)

# ================================================================
# 슬라이드 2: 총평
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_text_box(slide, Inches(0.8), Inches(0.4), Inches(10), Inches(0.6),
             "총평", font_size=36, bold=True, color=TEXT_WHITE)

# 핵심 문제 카드
card = add_shape_rect(slide, Inches(0.8), Inches(1.3), Inches(14.4), Inches(2.0),
                      BG_CARD, ACCENT_RED)

add_text_box(slide, Inches(1.2), Inches(1.5), Inches(13.5), Inches(0.5),
             "현재 기획서는 \"공격 패턴 카탈로그\"이지, 보스 행동 설계서가 아님",
             font_size=22, bold=True, color=ACCENT_RED)

add_bullet_list(slide, Inches(1.2), Inches(2.1), Inches(13.5), Inches(1.0), [
    "보스가 공격하지 않는 시간(전체의 60~70%)에 대한 행동 설계가 전무",
    "구체적 수치(HP, 데미지, 강인도 등)가 하나도 없어 밸런싱 및 구현 불가",
], font_size=16, color=TEXT_WHITE)

# 보완 요청 우선순위
add_text_box(slide, Inches(0.8), Inches(3.8), Inches(10), Inches(0.5),
             "보완 요청 우선순위", font_size=24, bold=True, color=TEXT_WHITE)

add_table_slide(slide, Inches(0.8), Inches(4.5), Inches(14.4), [
    ["순위", "항목", "이유"],
    ["1", "이동/행동 설계", "보스 AI의 절반 이상이 비어 있음"],
    ["2", "수치 데이터", "수치 없으면 구현/밸런싱 불가"],
    ["3", "공방 리듬 시퀀스", "전투 체감의 근간"],
    ["4", "페이즈별 성격 정의", "2페이즈 차별화의 핵심"],
    ["5", "전투 공간 사양", "패턴 사거리와 직결"],
    ["6", "기타 (반응행동, 트리거, 미결정 항목, 전환 연출)", "상세 보완"],
], [Inches(1.0), Inches(4.0), Inches(9.4)], header_color=ACCENT_RED)

# ================================================================
# 슬라이드 3: 수치 데이터 부재
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_tag(slide, Inches(0.8), Inches(0.4), "우선순위 #2")
add_text_box(slide, Inches(2.8), Inches(0.35), Inches(10), Inches(0.6),
             "수치 데이터 전면 부재", font_size=32, bold=True, color=TEXT_WHITE)

add_text_box(slide, Inches(0.8), Inches(1.2), Inches(14), Inches(0.5),
             "기획서에 구체적 수치가 전부 빠져 있음. 개발자가 임의로 넣게 되고 밸런싱 책임 소재가 불명확해짐.",
             font_size=16, color=TEXT_GRAY)

add_table_slide(slide, Inches(0.8), Inches(2.0), Inches(14.4), [
    ["누락 항목", "필요한 내용"],
    ["보스 HP", "1페이즈/2페이즈 총합, 예상 클리어 타임과 함께 산출"],
    ["패턴별 데미지", "플레이어 HP 대비 비율 (예: 펀치 = HP의 8%)"],
    ["강인도 게이지 최댓값", "정수 기준 명시 (예: 100)"],
    ["강인도 감소량", "강공격/스킬별 차등 수치"],
    ["강인도 자연 회복 속도", "초당 수치 (예: 3초 미피격 시 초당 5 회복)"],
    ["패턴 가중치 기본값/최댓값", "패턴별 개별 설정값"],
    ["가중치 회복 속도", "정확한 곡선 또는 초당 수치 (\"서서히\"는 안 됨)"],
], [Inches(5.0), Inches(9.4)], header_color=ACCENT_ORANGE)

# ================================================================
# 슬라이드 4: 이동/행동 설계 부재 - 개요
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_tag(slide, Inches(0.8), Inches(0.4), "우선순위 #1")
add_text_box(slide, Inches(2.8), Inches(0.35), Inches(12), Inches(0.6),
             "이동/행동 설계 전면 부재 (가장 큰 문제)", font_size=32, bold=True, color=TEXT_WHITE)

add_text_box(slide, Inches(0.8), Inches(1.2), Inches(14), Inches(0.5),
             "보스가 공격하지 않는 시간에 대한 행동 설계가 완전히 비어 있음. 4개 영역 전부 추가 필요.",
             font_size=16, color=TEXT_GRAY)

# 4개 카드
cards = [
    ("기본 대치 행동", "Idle Combat", [
        "대치 시 이동 방식 (서클링? 스텝? 고정?)",
        "선호 교전 거리 (예: 1페 5m, 2페 3m)",
        "플레이어 tracking 방식/회전 속도",
        "이동 연출 (묵직한 발걸음, 지면 흔들림)",
    ], ACCENT_BLUE),
    ("접근 행동", "Approach", [
        "원거리에서 거리 좁히는 방법",
        "느린 위압 접근 vs 스텝 점프 vs 돌진",
        "접근 중 페인트 (멈추기, 방향 전환)",
        "페이즈별 접근 성향 차이",
    ], ACCENT_GREEN),
    ("공격 후 행동", "Post-Attack", [
        "각 패턴 종료 후 보스가 취하는 행동",
        "후퇴? 제자리? 추격? 도발 모션?",
        "이것이 곧 플레이어의 딜타임 설계",
        "패턴별 후딜 테이블 필수",
    ], ACCENT_ORANGE),
    ("거리 벌리기", "Disengage", [
        "파고들기 외 일반 후퇴 수단 필요",
        "후퇴 후 멈추는 거리 명시",
        "후퇴 후 선택 가능한 행동 분기",
        "페이즈별 후퇴 빈도/성향 차이",
    ], ACCENT_RED),
]

for i, (title_kr, title_en, items, color) in enumerate(cards):
    col = i % 2
    row = i // 2
    x = Inches(0.8) + col * Inches(7.4)
    y = Inches(2.0) + row * Inches(3.3)

    card_shape = add_shape_rect(slide, x, y, Inches(7.0), Inches(3.0), BG_CARD, color)

    add_text_box(slide, x + Inches(0.3), y + Inches(0.15), Inches(6.0), Inches(0.4),
                 f"{title_kr} ({title_en})", font_size=18, bold=True, color=color)

    add_bullet_list(slide, x + Inches(0.3), y + Inches(0.6), Inches(6.4), Inches(2.2),
                    [f"  {item}" for item in items], font_size=13, color=TEXT_WHITE, spacing=Pt(4))


# ================================================================
# 슬라이드 5: 공격 후 행동 테이블
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_text_box(slide, Inches(0.8), Inches(0.4), Inches(14), Inches(0.6),
             "공격 후 행동 - 기획자가 채워야 할 테이블", font_size=28, bold=True, color=TEXT_WHITE)

add_text_box(slide, Inches(0.8), Inches(1.1), Inches(14), Inches(0.5),
             "이 테이블이 곧 플레이어의 딜타임 설계임. 반드시 채울 것.",
             font_size=16, color=ACCENT_ORANGE)

add_table_slide(slide, Inches(0.8), Inches(1.8), Inches(14.4), [
    ["패턴", "공격 후 행동", "플레이어 딜타임", "비고"],
    ["펀치", "??? (제자리? 후퇴?)", "???", ""],
    ["내려 찍기", "??? (경직 유무?)", "???", ""],
    ["회전 킥", "??? (어지러움?)", "???", ""],
    ["돌진", "중심잡기 1.5초 (유일하게 명시됨)", "1.5초", ""],
    ["돌 던지기", "??? (접근? 대기?)", "???", ""],
    ["잡아 던지기", "??? (추격? 도발?)", "???", ""],
    ["브레스 (2페)", "??? (과열 경직?)", "???", ""],
    ["땅 가르기 (2페)", "??? (팔 뽑기 후딜?)", "???", ""],
], [Inches(3.0), Inches(5.0), Inches(3.0), Inches(3.4)], header_color=ACCENT_ORANGE)

# ================================================================
# 슬라이드 6: 페이즈별 성격 정의
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_tag(slide, Inches(0.8), Inches(0.4), "우선순위 #4")
add_text_box(slide, Inches(2.8), Inches(0.35), Inches(12), Inches(0.6),
             "페이즈별 행동 성격 정의 필요", font_size=32, bold=True, color=TEXT_WHITE)

add_text_box(slide, Inches(0.8), Inches(1.2), Inches(14), Inches(0.8),
             "현재 1페이즈 → 2페이즈 차이가 \"더 빠르고, 더 넓고, 더 세게\"에 그침.\n"
             "플레이어가 수치가 아닌 행동으로 \"얘가 달라졌다\"를 느껴야 함.",
             font_size=16, color=TEXT_GRAY)

# 비교 테이블
add_table_slide(slide, Inches(0.8), Inches(2.3), Inches(14.4), [
    ["항목", "1페이즈 (기획자 작성)", "2페이즈 (기획자 작성)"],
    ["전투 성격", "예: 수호자 (대기 → 반격)", "예: 포식자 (압박 → 몰아붙이기)"],
    ["선호 교전 거리", "???", "???"],
    ["접근 성향", "???", "???"],
    ["공격 후 행동 경향", "???", "???"],
    ["후퇴 빈도", "???", "???"],
    ["대치 중 이동 패턴", "???", "???"],
], [Inches(3.5), Inches(5.45), Inches(5.45)], header_color=ACCENT_BLUE)

# 현재 문제 카드
card = add_shape_rect(slide, Inches(0.8), Inches(6.0), Inches(14.4), Inches(2.5), BG_CARD, ACCENT_YELLOW)

add_text_box(slide, Inches(1.2), Inches(6.2), Inches(13.5), Inches(0.4),
             "현재 페이즈 차이 (수치만 올린 것)", font_size=18, bold=True, color=ACCENT_YELLOW)

add_bullet_list(slide, Inches(1.2), Inches(6.7), Inches(6.5), Inches(1.6), [
    "펀치: 60도 → 90도, 0.3초 → 0.2초",
    "내려 찍기: 5m → 7m",
    "회전 킥: 3m/s → 6m/s",
    "돌진: 12m/s → 18m/s",
], font_size=14, color=TEXT_WHITE, spacing=Pt(4))

add_bullet_list(slide, Inches(8.0), Inches(6.7), Inches(6.5), Inches(1.6), [
    "신규 패턴은 브레스, 땅 가르기 2개뿐",
    "나머지 전부 기존 패턴의 수치 강화",
    "플레이어 체감: \"같은 놈인데 더 짜증남\"",
    "필요한 체감: \"다른 존재와 싸우는 느낌\"",
], font_size=14, color=ACCENT_RED, spacing=Pt(4))


# ================================================================
# 슬라이드 7: 공방 리듬 설계
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_tag(slide, Inches(0.8), Inches(0.4), "우선순위 #3")
add_text_box(slide, Inches(2.8), Inches(0.35), Inches(12), Inches(0.6),
             "공방 리듬 (Attack-Defense Rhythm) 설계 부재", font_size=32, bold=True, color=TEXT_WHITE)

add_text_box(slide, Inches(0.8), Inches(1.2), Inches(14), Inches(0.5),
             "플레이어가 \"언제 때리고, 언제 피해야 하는지\"가 명시되지 않음. 패턴별 시퀀스 정리 필요.",
             font_size=16, color=TEXT_GRAY)

# 포맷 제시
card = add_shape_rect(slide, Inches(0.8), Inches(2.0), Inches(14.4), Inches(1.5), BG_CARD, ACCENT_GREEN)

add_text_box(slide, Inches(1.2), Inches(2.1), Inches(13.5), Inches(0.4),
             "요청 포맷", font_size=18, bold=True, color=ACCENT_GREEN)

add_text_box(slide, Inches(1.2), Inches(2.55), Inches(13.5), Inches(0.8),
             "[전조 모션 Xms]  →  [공격 판정 Xms]  →  [후딜/경직 Xms]  →  [플레이어 딜타임 X초]",
             font_size=20, bold=True, color=TEXT_WHITE, font_name="Consolas")

# 예시들
add_text_box(slide, Inches(0.8), Inches(3.8), Inches(14), Inches(0.5),
             "예시 (기획자가 이런 식으로 패턴별 전부 작성할 것)", font_size=18, bold=True, color=TEXT_WHITE)

examples = [
    ("돌진", "준비 모션 0.5초  →  돌진 1.5초  →  중심잡기 1.5초  →  딜타임 1.5초", ACCENT_BLUE),
    ("내려 찍기", "팔 올리기 0.6초  →  찍기 0.2초  →  충격파 0.8초  →  딜타임 0.8초", ACCENT_GREEN),
    ("잡아 던지기", "양손 벌리기 0.8초  →  잡기 0.3초  →  던지기 연출 2.5초  →  ???", ACCENT_ORANGE),
]

for i, (name, seq, color) in enumerate(examples):
    y = Inches(4.5) + i * Inches(1.0)
    card = add_shape_rect(slide, Inches(0.8), y, Inches(14.4), Inches(0.8), BG_CARD, color)
    add_text_box(slide, Inches(1.2), y + Inches(0.05), Inches(2.5), Inches(0.35),
                 name, font_size=16, bold=True, color=color)
    add_text_box(slide, Inches(3.5), y + Inches(0.05), Inches(11.5), Inches(0.35),
                 seq, font_size=15, color=TEXT_WHITE, font_name="Consolas")

add_text_box(slide, Inches(0.8), Inches(7.8), Inches(14), Inches(0.5),
             "각 패턴마다 이 시퀀스가 있어야 전투 리듬이 설계된 것임.",
             font_size=16, bold=True, color=ACCENT_RED)


# ================================================================
# 슬라이드 8: 기타 보완 사항
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_text_box(slide, Inches(0.8), Inches(0.4), Inches(14), Inches(0.6),
             "기타 보완 사항", font_size=32, bold=True, color=TEXT_WHITE)

# 카드 1: 반응형 행동
card = add_shape_rect(slide, Inches(0.8), Inches(1.3), Inches(7.0), Inches(3.2), BG_CARD, ACCENT_YELLOW)
add_text_box(slide, Inches(1.2), Inches(1.4), Inches(6.0), Inches(0.4),
             "반응형 행동 설계 의도 보충", font_size=18, bold=True, color=ACCENT_YELLOW)
add_bullet_list(slide, Inches(1.2), Inches(1.9), Inches(6.4), Inches(2.4), [
    "차징 → 도약 (30%/60%): 왜 이 확률인지?",
    "강인도 20% → 파고들기 (50%/80%): 근거?",
    "3연속 피격 → 목질 강화 (40%/70%): 근거?",
    "",
    "각 반응에 \"플레이어가 느끼길 원하는 것\"",
    "한 줄씩 추가할 것",
], font_size=13, color=TEXT_WHITE, spacing=Pt(3))

# 카드 2: 파고들기 트리거
card = add_shape_rect(slide, Inches(8.2), Inches(1.3), Inches(7.0), Inches(3.2), BG_CARD, ACCENT_RED)
add_text_box(slide, Inches(8.6), Inches(1.4), Inches(6.0), Inches(0.4),
             "파고들기 트리거 조건 불일치", font_size=18, bold=True, color=ACCENT_RED)
add_bullet_list(slide, Inches(8.6), Inches(1.9), Inches(6.4), Inches(2.4), [
    "Step 2 (p5):",
    "  \"강인도가 30% 이하일 때\" → 파고들기",
    "",
    "패턴 상세 (p10):",
    "  \"체력이 짧은 시간 내에 일정량 이상 깎일 때\"",
    "",
    "어느 쪽이 맞는지 확정 + 수치 구체화",
], font_size=13, color=TEXT_WHITE, spacing=Pt(3))

# 카드 3: 전투 공간
card = add_shape_rect(slide, Inches(0.8), Inches(4.8), Inches(7.0), Inches(3.5), BG_CARD, ACCENT_BLUE)
add_text_box(slide, Inches(1.2), Inches(4.9), Inches(6.0), Inches(0.4),
             "전투 공간 사양 부재 (우선순위 #5)", font_size=18, bold=True, color=ACCENT_BLUE)
add_bullet_list(slide, Inches(1.2), Inches(5.4), Inches(6.4), Inches(2.6), [
    "보스 방 크기 (가로 x 세로 또는 반경)",
    "  - 돌진 20m인데 방 크기가 안 정해져 있음",
    "지형 특성: 평지? 장애물? 기둥?",
    "벽 충돌 시 보스/플레이어 행동",
    "\"왕의 정원\" 설정의 전투 반영 방식",
    "  - 나무/덩굴? 파괴 가능 오브젝트?",
], font_size=13, color=TEXT_WHITE, spacing=Pt(3))

# 카드 4: 미결정 + 전환
card = add_shape_rect(slide, Inches(8.2), Inches(4.8), Inches(7.0), Inches(3.5), BG_CARD, ACCENT_ORANGE)
add_text_box(slide, Inches(8.6), Inches(4.9), Inches(6.0), Inches(0.4),
             "미결정 항목 + 페이즈 전환", font_size=18, bold=True, color=ACCENT_ORANGE)
add_bullet_list(slide, Inches(8.6), Inches(5.4), Inches(6.4), Inches(2.6), [
    "\"고민중\"으로 기획서에 포함하지 말 것:",
    "  - 엇박자 선 딜레이 → 범위 확정 필요",
    "  - 전조 방식 (장판 vs 모션) → 결정 필요",
    "  - 결정 어려우면 A안/B안 비교표 제출",
    "",
    "페이즈 전환 상세화:",
    "  - 전환 중 플레이어 공격 가능? (무적/슈아)",
    "  - 전환 완료 후 첫 행동 명시",
    "  - HP 50% 즉시? 패턴 완료 후?",
], font_size=13, color=TEXT_WHITE, spacing=Pt(3))


# ================================================================
# 슬라이드 9: 마무리
# ================================================================
slide = prs.slides.add_slide(prs.slide_layouts[6])
add_bg(slide)
add_shape_rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), ACCENT_RED)

add_text_box(slide, Inches(1.5), Inches(2.0), Inches(13), Inches(1.0),
             "핵심 요청", font_size=44, bold=True, color=TEXT_WHITE, alignment=PP_ALIGN.CENTER)

card = add_shape_rect(slide, Inches(2.0), Inches(3.2), Inches(12.0), Inches(3.5), BG_CARD, ACCENT_RED)

items = [
    "\"보스가 공격 안 하는 시간의 60~70%를 어떻게 보내는지\" 설계할 것",
    "모든 패턴에 수치(데미지, 사거리, 후딜)를 숫자로 채울 것",
    "패턴별 공방 리듬 시퀀스 (전조→공격→후딜→딜타임) 작성할 것",
    "1페이즈/2페이즈의 행동 \"성격\"을 정의하고 수치 강화가 아닌 행동 차별화를 설계할 것",
    "미결정 항목은 결정 후 재제출. \"고민중\"으로 넘기지 말 것",
]

add_bullet_list(slide, Inches(2.5), Inches(3.5), Inches(11.0), Inches(3.0),
                [f"  {item}" for item in items], font_size=17, color=TEXT_WHITE, spacing=Pt(12))

add_text_box(slide, Inches(1.5), Inches(7.5), Inches(13), Inches(0.5),
             "공격은 순간이고, 플레이어가 보스를 읽는 건 대치 중 움직임이다.",
             font_size=20, bold=True, color=ACCENT_RED, alignment=PP_ALIGN.CENTER)

# 하단 라인
add_shape_rect(slide, Inches(0), Inches(8.94), SLIDE_W, Inches(0.06), ACCENT_RED)


# ── 저장 ──
output_path = r"c:\Users\u\Documents\GitHub\Project_Abyss\Assets\Abyss\Docs\보스_기획서_피드백_가이드라인.pptx"
prs.save(output_path)
print(f"PPT 생성 완료: {output_path}")
