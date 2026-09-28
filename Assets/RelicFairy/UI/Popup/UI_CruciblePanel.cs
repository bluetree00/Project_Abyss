using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 재련소 UI 패널 (Canvas_Popup, Addressable "UI_CruciblePanel").
///
/// 무기 강화 화면(다크 판타지·대장간 톤, ShopUIStyle 재사용). 1600×1000 판(2026-09-20 개편):
///  - 머리줄: 브랜드 · 탭(무기강화/원거리 파츠) · 재화(HUD와 같은 칸) · 나가기
///  - 무대(좌 60%): 모루 위 무기 한 자루 — 단계 방패 · 달굼 빛 · 공격력 · 게이지. 강화하면 망치 → 불꽃 → 판정
///  - 정보(우 35%): 성공률 한 숫자 → 표 5줄 → [강화하기 · 비용] → [진화]
///  - 대사 밴드(하단): 대장장이 초상 · 대사 · 이벤트/위험 배지
///
/// 제물(sacrifice) 메커닉은 제거됨 — 한 무기에 집중하는 강화. 데이터/계산/결정성은 CrucibleRoomController가 권위.
/// </summary>
public sealed class UI_CruciblePanel : UI_Popup
{
    public override bool BlocksGameplay => true; // 재련 중 시간정지 + 입력잠금
    public override bool CloseOnEscape  => true; // ESC = 나가기(기존 동작, EscKeyListener 공용 경로)

    // 전체화면 탭형 레이아웃 — 재련소 개편 시안(2026-09-20). 판 크기는 디자인 의뢰서의 1600×1000.
    // 머리줄 74 · 무대/정보 748 · 대사 밴드 108을 빈 띠 없이 쌓는다(무대 60% · 정보 35% · 밴드 11%).
    // 예전 합성본 좌표계(1167×834)는 위쪽 10%가 빈 띠였고, 판에 비해 정보창 글자가 작았다.
    // 창 크기는 UIWindowFitter가 화면에 맞춰 키운다 — 여기 숫자는 그대로 두면 된다.
    private const float MockW = 1600f, MockH = 1000f;

    // 머리줄 — 브랜드 · 탭 2 · 재화 2 · 나가기
    private const float HdrY     = 18f;
    private const float TabY     = 16f,    TabW  = 236f, TabH  = 78f;    // 탭 아트 390×131(2.98) ≈ 236×78(3.03)
    private const float Tab0X    = 342f,   Tab1X = 590f;
    private const float PillY    = 30f,    PillW = 176f, PillH = 50f;
    private const float MatPillX = 1078f,  OrePillX = 1264f;
    private const float ExitX    = 1458f,  ExitY = 30f,  ExitW = 118f, ExitH = 50f;

    // 본문
    private const float StageX = 24f,   StageY = 108f, StagePanelW = 960f, StagePanelH = 748f;
    private const float InfoX  = 1008f, InfoY  = 108f, InfoW = 568f,       InfoH = 748f;
    private const float LogX   = 24f,   LogY   = 872f, LogW  = 1552f,      LogH  = 108f;

    // 무대 안쪽(무대 960×748 기준 상대 좌표)
    private const float SlotX   = 330f, SlotY   = 120f, SlotWH = 300f;                 // 강화 무기 바탕 524 정사각
    private const float GlowX   = 270f, GlowY   = 60f,  GlowWH = 420f;                 // 달굼 빛(무기 뒤)
    private const float LvTagX  = 405f, LvTagY  = 92f,  LvTagW = 150f, LvTagH = 113f;  // 강화 표기 266×201
    private const float BadgeX = 196f, BadgeY = 34f,  BadgeW = 89f,  BadgeH = 67f;
    private const float RiskX   = 640f, RiskY   = 128f, RiskW  = 180f, RiskH  = 64f;   // 위험 하락 180×64
    private const float AnvilX  = 367f, AnvilY  = 410f, AnvilW = 226f, AnvilH = 142f;  // 재련소 모루 113×71 ×2
    private const float AtkX    = 230f, AtkY    = 566f, AtkW   = 500f, AtkH   = 52f;   // 공격력 표기칸 1128×111
    private const float GaugeX  = 60f,  GaugeY  = 640f, GaugeW = 840f, GaugeH = 92f;
    // 망치가 닿는 점 = 불꽃이 튀는 점(모루 윗면 가운데). 회전축은 그 오른쪽 205px — 0°가 내려친 자세다.
    private const float StrikeX = 495f, StrikeY = 425f, HammerPivotX = 700f, HammerReach = 205f;
    private const int   MaxTicks = 15;   // 게이지 눈금 수 상한(티어 4 강화 상한)

    private const float Margin   = 28f;
    private const float TopBarH  = 92f;
    private const float InfoColW = 440f;    // 우측 강화정보 컬럼 폭
    private const float ColGap   = 26f;

    // ── 연출 노브 (도파민 레이어; 표시층 전용, 결과/데이터 불변) ──
    private const float PunchScale        = 0.14f;  // 성공 카드 스케일 펀치 진폭
    private const float PunchDur          = 0.22f;
    private const float JackpotPunchScale = 0.28f;  // 잭팟 강한 펀치
    private const float JackpotPunchDur   = 0.34f;
    private const float CountStepDur      = 0.07f;  // 레벨 1단계 카운트 시간(초)
    private const float CountMaxDur       = 0.45f;  // 카운트 총 상한
    private const float FlashDur          = 0.28f;  // 카드 색 플래시 감쇠 시간
    private const float FailShakeDur      = 0.34f;
    private const float FailShakeAmp      = 12f;    // 실패 카드 좌우 흔들림(px)
    private const float NearMissShakeMult = 1.6f;   // 니어미스 시 흔들림 배수
    private const float NearMissBand      = 1.1f;   // roll < chance*이 배수면 니어미스
    private const float JackpotPulsePeak  = 0.7f;   // 전체화면 펄스 강도
    private const float JackpotPulseDur   = 0.4f;

    // ── 스테이지 중앙 결과 팝업 (판정을 시선 위에서 크게) ──
    private const float ResultPopDur    = 0.12f;  // 등장(큰 글씨 → 제자리)
    private const float ResultHoldDur   = 0.85f;
    private const float ResultFadeDur   = 0.30f;
    private const float SuccessFontSize = 64f;    // 무기 위 한가운데 도장
    private const float SuccessPopScale = 1.35f;
    private const float JackpotFontSize = 72f;    // 잭팟은 한눈에 다르게
    private const float JackpotPopScale = 1.9f;
    private const float RejectFontSize  = 32f;    // 거부(재료 부족 등)는 판정이 아니라 안내
    private const float RejectPopScale  = 1.12f;

    private static readonly Color SuccessFlash  = new(0.28f, 0.72f, 0.34f, 1f);
    private static readonly Color JackpotFlash  = new(1f,    0.78f, 0.30f, 1f);
    private static readonly Color FailFlash     = new(0.60f, 0.14f, 0.14f, 1f);
    private static readonly Color NearMissFlash = new(0.78f, 0.42f, 0.12f, 1f);

    private static readonly Color GaugeTrack = new(0.05f, 0.05f, 0.08f, 1f);
    private static readonly Color GaugeFillC = new(0.92f, 0.62f, 0.22f, 1f);
    private static readonly Color CardTargetBg = new(0.20f, 0.16f, 0.09f, 1f);

    // ── 무대 연출(개편 09-20) — 포커스 → 망치 → 임팩트 → 판정. 전부 unscaled(팝업은 시간이 멈춘다) ──
    private const float FocusDur         = 0.10f;  // 정보·대사가 흐려지고 무대가 다가오는 시간
    private const float FocusDim         = 0.38f;  // 포커스 중 정보창·대사 밴드 알파
    private const float FocusVeilAlpha   = 0.90f;
    private const float FocusStageScale  = 1.03f;
    private const float HammerRaiseDur   = 0.10f;  // 치기 전 멈칫(더 들어올림)
    private const float HammerDropDur    = 0.30f;  // 내려치기 — 끝에서 가속
    private const float HammerRecoverDur = 0.15f;
    private const float HammerFadeDur    = 0.25f;
    private const float HammerRaisedDeg  = -65f;   // 0° = 내려친 자세
    private const float HammerWindDeg    = -76f;
    private const float HammerReboundDeg = -14f;
    private const float ImpactPunchPx    = 7f;     // 임팩트 순간 창이 아래로 꺼지는 거리
    private const float ImpactPunchDur   = 0.16f;
    private const float WindowShakeAmp   = 9f;     // 실패 흔들림(창 전체)
    private const float WindowShakeDur   = 0.32f;
    private const float StageFlashDur    = 0.42f;
    private const float AtkCountDur      = 0.52f;  // 공격력 카운트
    private const float JackpotPauseDur  = 0.30f;  // 판정 멈칫
    private const float JackpotPulseGap  = 0.26f;  // 금빛 맥박 3회 간격
    private const float JackpotShowDur   = 1.60f;  // 빛살·배너
    private const float JackpotRefundDelay = 0.50f;
    private const float CoolDur          = 0.90f;  // 실패 후 무기가 식었다가 돌아오는 시간
    private const float SettleDur        = 0.25f;  // 판정 뒤 여운(대사가 읽히는 틈)
    private const float BadgePopDur      = 0.42f;
    private const float HeatAlpha        = 0.50f;  // 도박 구간 화면 가장자리 열기(0.75는 창 밖이 주황으로 칠해졌다)
    private const float SparkGravity     = 900f;   // 불꽃 낙하(px/s²)
    private const int   StrikeSparks     = 36;
    private const int   JackpotSparks    = 60;
    private const int   SparkPool        = 64;
    private const int   EmberPool        = 24;
    private const float EmberRate        = 8f;     // 초당 불씨 수

    private const string AtkHoldFormat = "공격 {0:1}";
    private const string AtkUpFormat   = "공격 <color=#7AD46E>{0:1}</color>";
    private const string AtkDownFormat = "공격 <color=#FF7A6A>{0:1}</color>";

    private static readonly Color WindowEdge   = new(0.54f, 0.36f, 0.16f, 1f);    // 창 청동 테두리
    private static readonly Color WindowInk    = new(0.06f, 0.043f, 0.035f, 1f);
    private static readonly Color WindowShade  = new(0.04f, 0.03f, 0.02f, 0.62f);
    private static readonly Color StageEdge    = new(0.48f, 0.31f, 0.13f, 1f);
    private static readonly Color StageInk     = new(0.047f, 0.031f, 0.024f, 1f);
    private static readonly Color StageShade   = new(0.03f, 0.02f, 0.016f, 0.22f);
    private static readonly Color TextMuted    = new(0.70f, 0.64f, 0.55f, 1f);
    private static readonly Color RateGold     = new(1f, 0.76f, 0.35f, 1f);
    private static readonly Color LevelGold    = new(1f, 0.89f, 0.63f, 1f);
    private static readonly Color LevelTagFill = new(0.10f, 0.07f, 0.05f, 0.92f);
    private static readonly Color DangerFill   = new(0.42f, 0.14f, 0.16f, 1f);
    private static readonly Color TabIdle      = new(0.72f, 0.68f, 0.62f, 0.55f);
    private static readonly Color BronzeEdge   = new(0.60f, 0.42f, 0.20f, 1f);
    private static readonly Color BronzeFill   = new(0.19f, 0.12f, 0.05f, 1f);
    private static readonly Color BronzeInk    = new(0.96f, 0.86f, 0.68f, 1f);
    private static readonly Color PortraitInk  = new(0.10f, 0.06f, 0.035f, 1f);
    private static readonly Color EventEdge    = new(0.71f, 0.33f, 0.17f, 1f);
    private static readonly Color EventFill    = new(0.24f, 0.08f, 0.035f, 0.85f);
    private static readonly Color EventInk     = new(1f, 0.76f, 0.63f, 1f);
    private static readonly Color RowLine      = new(1f, 0.78f, 0.55f, 0.12f);
    private static readonly Color TickDim      = new(0.70f, 0.62f, 0.52f, 1f);   // 09-25: 0.49 → 0.70(검은 막대 밑에서 안 읽혔다)
    private static readonly Color TickNext     = new(1f, 0.80f, 0.55f, 1f);      // 다음 단계 — 성공하면 닿는 곳
    private static readonly Color GhostColor   = new(1f, 0.55f, 0.20f, 0.55f);   // 다음 한 칸 미리보기(맥박)
    private static readonly Color TickDanger   = new(1f, 0.54f, 0.44f, 1f);
    private static readonly Color TickLine     = new(1f, 0.78f, 0.55f, 0.18f);
    private static readonly Color MatTint      = new(0.90f, 0.55f, 0.20f, 1f);    // HUD 강화재료 테두리색
    private static readonly Color OreTint      = new(0.35f, 0.70f, 0.95f, 1f);    // HUD 원석 테두리색
    private static readonly Color CostInk      = new(1f, 0.89f, 0.66f, 1f);
    private static readonly Color GlowColor    = new(1f, 0.59f, 0.24f, 0f);
    private static readonly Color ForgeGlowColor = new(1f, 0.43f, 0.12f, 0.42f);
    private static readonly Color HeatColor    = new(1f, 0.35f, 0.08f, 0f);
    private static readonly Color EmberColor   = new(1f, 0.59f, 0.24f, 1f);
    private static readonly Color SparkGold    = new(1f, 0.82f, 0.47f, 1f);
    private static readonly Color SparkJackpot = new(1f, 0.88f, 0.55f, 1f);
    private static readonly Color RaysColor    = new(1f, 0.84f, 0.47f, 0f);
    private static readonly Color JackpotInk   = new(1f, 0.91f, 0.63f, 1f);
    // 판정 빛 — 처음엔 0.75~0.85였는데 무대 전체가 한 색으로 덮여 무기·도장이 묻혔다(실측 09-20).
    private static readonly Color StageFlashSuccess = new(1f, 0.80f, 0.43f, 0.60f);
    private static readonly Color StageFlashJackpot = new(1f, 0.82f, 0.43f, 0.72f);
    private static readonly Color StageFlashFail    = new(1f, 0.24f, 0.16f, 0.55f);
    private static readonly Color StageFlashNear    = new(1f, 0.50f, 0.20f, 0.55f);
    private static readonly Color CoolTint     = new(0.55f, 0.68f, 1f, 1f);
    private static readonly Color HammerWood   = new(0.42f, 0.27f, 0.14f, 1f);
    private static readonly Color HammerEdge   = new(0.17f, 0.18f, 0.21f, 1f);
    private static readonly Color HammerSteel  = new(0.60f, 0.63f, 0.68f, 1f);
    private static readonly Color HammerShine  = new(0.91f, 0.93f, 0.95f, 0.9f);
    private static readonly Color EvolveReady  = new(0.40f, 0.28f, 0.62f, 1f);
    private static readonly Color EvolveLocked = new(0.12f, 0.10f, 0.13f, 0.9f);

    private static Sprite _vignette;   // 코드로 그린 비네트(한 번 만들어 계속 쓴다)

    private CrucibleRoomController _controller;
    private int _targetSlot = PlayerWeaponManager.Slot0;

    // 헤더
    [SerializeField] private TMP_Text _fuelText;
    [SerializeField] private TMP_Text _oreText;
    [SerializeField] private TMP_Text _dialogText;

    // 슬롯 카드(2)
    private readonly Image[]         _cardBg        = new Image[2];
    private readonly TMP_Text[]      _cardName      = new TMP_Text[2];
    private readonly TMP_Text[]      _cardLevel     = new TMP_Text[2];
    private readonly TMP_Text[]      _cardAtk       = new TMP_Text[2];
    private readonly RectTransform[] _cardGaugeFill = new RectTransform[2];
    private readonly Vector2[]       _cardBasePos   = new Vector2[2]; // 카드 기준 앵커 위치(쉐이크 복원용)

    // 원거리 파츠 — 완성본 원거리 탭은 4슬롯(활·분열·관통·빈). 슬롯 UI는 레이아웃, 로직은 Phase 3.
    // 파츠 5종 = 5행. 내장형이라 '슬롯'이 아니라 파츠 정의 목록의 인덱스가 곧 행 번호다.
    private const int PartRows = 5;
    private readonly Image[]    _partsSlots = new Image[PartRows];
    private readonly Image[]    _partsFrames = new Image[PartRows];   // 행 테두리(납품 아트 9-slice 사본, InitRuntimeFx)
    private bool _partRowArt;                                          // 행 아트가 입혀졌는가(색 규칙이 갈린다)
    private static Sprite _rowBgSliced, _rowFrameSliced;
    private readonly TMP_Text[] _partsMark  = new TMP_Text[PartRows];

    // 전체화면 탭형 재설계 — 스킨/탭/스테이지 컨테이너
    private CrucibleSkinSO _skin;
    [SerializeField] private RectTransform  _meleeStage, _rangedStage;
    [SerializeField] private TMP_Text       _stageResult;      // 스테이지 중앙 결과 팝업(두 탭 공용)
    private int            _stageResultSeq;   // 연타 시 이전 페이드가 새 결과를 지우지 않게 하는 세대 번호
    // 스킬 각인 택1 — 구운 프리팹에 없고 처음 필요할 때 만든다(EnsureEngravePanel).
    private GameObject _engravePanel;
    private TMP_Text   _engraveTitle;
    private readonly TMP_Text[] _engraveName = new TMP_Text[2], _engraveDesc = new TMP_Text[2];
    private readonly List<SkillSO.EngravingDef> _engraveOptions = new();
    private WeaponData _engraveWeapon;
    private SkillSO    _engraveSkill;
    // 전설 승급 택1 — 진화가 끝난 검이 최대 강화에 닿으면 연다(09-27 복원: 승급 줄이 숨겨져 아무도 닿지 못했다).
    private GameObject _legendPanel;
    private readonly List<GameObject> _legendCards = new();
    [SerializeField] private Image          _tabWeaponImg, _tabRangedImg;
    private int            _activeTab;   // 0=무기강화 1=원거리 파츠

    // 근접 집중 카드 — 안전/도박 구간 배지 + 진화 마일스톤(실데이터: DropAt/레벨/승급)
    [SerializeField] private Image    _zoneBg;
    [SerializeField] private TMP_Text _zoneTag;
    [SerializeField] private TMP_Text _milestoneLabel;

    // 이벤트 배너(HasEvent — Discount/Fever) + 진화 선택 패널(선택 연출)
    [SerializeField] private GameObject _eventBanner;
    [SerializeField] private TMP_Text   _eventBannerText;
    [SerializeField] private GameObject _evolvePanel;
    [SerializeField] private Button     _evolveBtn;
    [SerializeField] private TMP_Text   _branchAName, _branchBName;
    [SerializeField] private Button     _branchABtn,  _branchBBtn;

    // 디테일 콘텐츠 — 무기 타입·티어 라인 + "다음 강화 상세"(성공/실패/잭팟) + 정보 잭팟·이벤트효과
    [SerializeField] private TMP_Text _focusTypeText;
    [SerializeField] private TMP_Text _detailSuccess, _detailFail, _detailJackpot;
    [SerializeField] private TMP_Text _dockTypeText;
    [SerializeField] private TMP_Text _eventEffectText;

    // 정보
    [SerializeField] private TMP_Text _successText;
    [SerializeField] private TMP_Text _costText;
    [SerializeField] private TMP_Text _streakText;
    [SerializeField] private TMP_Text _resultText;

    [SerializeField] private Button   _enhanceBtn;
    [SerializeField] private TMP_Text _enhanceLabel;

    // 원거리 무기(슬롯1) 강화 행 — 파츠 슬롯을 여는 유일한 투자 경로.

    // ── 원거리 탭(2차 레이아웃) ──
    [SerializeField] private TMP_Text _slotSummary;
    [SerializeField] private TMP_Text _partKindText;
    [SerializeField] private TMP_Text _partGrowthText;
    [SerializeField] private TMP_Text _partDescText;
    [SerializeField] private TMP_Text _partSynergyText;
    [SerializeField] private TMP_Text _partCostText;
    [SerializeField] private Button   _partEnhanceBtn;
    [SerializeField] private TMP_Text _partEnhanceLabel;

    // 슬롯 카드 내부 — 클릭 없이 조합을 읽으려면 카드가 스스로 내용을 말해야 한다.
    private readonly TMP_Text[] _slotKind = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotVal  = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotName = new TMP_Text[PartRows];
    private readonly TMP_Text[] _slotLv   = new TMP_Text[PartRows];
    private readonly RectTransform[] _slotBar = new RectTransform[PartRows];
    // 행마다 강화 버튼을 둔다 — 이 화면의 질문이 "어디에 몰아줄까"라, 비용이 한 번에
    // 하나씩만 보이면(예전의 상세 패널 버튼 1개) 5종 비교가 성립하지 않는다.
    private readonly Button[]   _slotBtn   = new Button[PartRows];
    private readonly TMP_Text[] _slotBtnLbl= new TMP_Text[PartRows];
    [SerializeField] private RectTransform _promoteRow;
    private readonly List<Button> _legendBtns = new();

    private bool _built;
    private bool _closing;
    private bool _animating;   // 연출 재생 중(결과는 이미 확정 — 표시층만 진행 중)
    private bool _skipAnim;    // 재입력이 들어와 진행 중 연출을 즉시 끝내는 중
    private Action _queued;    // 연출 중 눌린 다음 강화(항상 최신 1개만 유지)

    // ── 개편(09-20) 무대 연출 참조 — 구운 프리팹에 저장되도록 직렬화한다 ──
    [SerializeField] private RectTransform _window;       // 임팩트 펀치·실패 흔들림
    [SerializeField] private RectTransform _stageArea;    // 포커스 확대
    [SerializeField] private Image         _veil;
    [SerializeField] private Image         _heat;         // 도박 구간 가장자리 열기
    [SerializeField] private CanvasGroup   _infoGroup, _bandGroup;   // 포커스 때 흐려지는 쪽
    [SerializeField] private Image         _weaponImg, _weaponGlow, _forgeGlow;
    [SerializeField] private RectTransform _hammer;
    [SerializeField] private CanvasGroup   _hammerGroup;
    [SerializeField] private RectTransform _sparkRoot, _emberRoot;
    [SerializeField] private Image         _stageFlash, _rays;
    [SerializeField] private TMP_Text      _jackpotBanner;
    [SerializeField] private Image         _gaugeFillImg;
    [SerializeField] private Image         _gaugeGhost;   // 다음 한 칸(성공 시 여기까지) — 맥박은 AmbientLoopAsync
    [SerializeField] private TMP_Text[]    _ticks;
    [SerializeField] private TMP_Text      _rateLabel;
    [SerializeField] private TMP_Text[]    _rowLabels;
    [SerializeField] private TMP_Text      _enhanceCost;
    [SerializeField] private TMP_Text      _evolveLabel;
    [SerializeField] private GameObject    _matPill, _orePill;

    // 런타임 전용 — 코드로 그린 스프라이트·입자 풀은 프리팹에 저장할 수 없어 Bind에서 만든다.
    private bool      _fxReady;
    private bool      _danger;          // 대상이 도박 구간인가 — 열기·배지·대사 밴드가 읽는다
    private float     _heatLevel01;     // 무기 달굼 목표(단계/상한)
    private float     _glowShown;       // 화면에 보이는 달굼(목표를 따라 천천히)
    private float     _focus01;
    private int       _hammerGen, _flashGen, _coolGen, _jackpotGen;
    private Image[]   _sparks, _embers;
    private Vector2[] _sparkPos, _sparkVel, _emberPos, _emberVel;
    private float[]   _sparkLife, _sparkDecay, _emberLife, _emberDecay;
    private int       _sparksAlive, _sparkNext;
    private float     _emberSpawn;

    // ── Lifecycle ───────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        if (_controller != null)
        {
            _controller.OnCrucibleChanged -= RefreshAll;
            _controller.NotifyPanelClosed();
        }
    }

    // ── Public API ──────────────────────────────────────────

    public void Bind(CrucibleRoomController controller)
    {
        _controller = controller;
        if (_controller != null)
            _controller.OnCrucibleChanged += RefreshAll;

        InitRuntimeFx();
        ShopUIStyle.PlaySfx("shop_open");
        _targetSlot = PlayerWeaponManager.Slot0;
        BuildLegendButtons();
        _dialogText.text = _controller.GetDialogue(CrucibleMood.Idle);   // 이벤트는 이제 전용 배너로 표시
        RefreshAll();
        TryShowEngraveOffer();   // 전 방문에 올라 두고 안 고른 각인이 있으면 먼저 묻는다
    }

    public override void ClosePopupUI()
    {
        if (_closing) return;
        _closing = true;
        ShopUIStyle.PlaySfx("shop_close");
        base.ClosePopupUI();
    }

    // ── 빌드 ────────────────────────────────────────────────

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        _skin = UISkin.Crucible;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);
        _veil = veil;

        // 도박 구간 열기 — 화면 가장자리가 달아오른다. 비네트 스프라이트는 코드로 그려 Bind에서 얹는다.
        _heat = ShopUIStyle.MakeImage(transform, "Heat", HeatColor);
        ShopUIStyle.Stretch(_heat.rectTransform);

        // 창 — 1600×1000 판. 바깥 Image가 청동 테두리(2px), 안쪽 Backdrop이 배경 아트를 <b>덮어</b> 깐다.
        // 배경 아트(1676×798 = 2.1)를 1.6 판에 그냥 늘리면 마법진이 가로로 눌린다 — 넘치는 좌우는 잘라낸다.
        // 화면 맞춤은 UIWindowFitter가 한다 — 자식이 비율 앵커라 통째로 따라온다.
        var window = ShopUIStyle.MakeImage(transform, "Window", WindowEdge, raycast: true);
        var wrt = window.rectTransform;
        wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
        wrt.anchoredPosition = Vector2.zero;
        wrt.sizeDelta = new Vector2(MockW, MockH);
        window.gameObject.AddComponent<UIWindowFitter>().Configure(maxScale: UIWindowFitter.ContentScreen);
        _window = wrt;
        var w = window.transform;

        var backdrop = ShopUIStyle.MakeImage(w, "Backdrop", WindowInk);
        ShopUIStyle.Stretch(backdrop.rectTransform, 2f);
        backdrop.gameObject.AddComponent<RectMask2D>();
        MakeCoverArt(backdrop.transform, "Art", _skin?.background);
        var shade = ShopUIStyle.MakeImage(backdrop.transform, "Shade", WindowShade);
        ShopUIStyle.Stretch(shade.rectTransform);

        BuildTopBar(w);
        BuildStages(w);
        BuildInfoColumn(w);
        BuildActionsColumn(w);
        BuildDialogueBand(w);
        BuildCornerDiamonds(w);
        BuildEvolvePanel(w);   // 선택 연출 오버레이(최상단, 기본 비활성)

        // 승급행은 폐기 예정이나 BuildLegendButtons가 non-null을 요구 → 숨긴 컨테이너만 유지.
        var promo = ShopUIStyle.MakeRect(w, "PromoteRow", typeof(HorizontalLayoutGroup));
        _promoteRow = (RectTransform)promo.transform;
        _promoteRow.gameObject.SetActive(false);

        SelectTab(0);
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    ///
    /// <para>파츠 행 배열들은 <c>readonly</c>라 직렬화되지 않는다(Unity는 readonly 필드를 저장하지 않는다).
    /// 행 이름 <c>PartRow{i}</c>로 찾아 다시 채우고, 클릭·호버도 그때 같이 건다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _skin = UISkin.Crucible;

        for (int i = 0; i < PartRows; i++)
        {
            var row = FindDeep($"PartRow{i}");
            if (row == null) continue;

            _partsSlots[i] = row.GetComponent<Image>();
            _partsFrames[i] = row.Find("RowFrame")?.GetComponent<Image>();
            _slotKind[i]   = Txt(row, "Mark");
            _slotName[i]   = Txt(row, "Nm");
            _slotVal[i]    = Txt(row, "Val");
            _slotLv[i]     = Txt(row, "Lv");
            _slotBar[i]    = row.Find("LvBar") as RectTransform;
            _partsMark[i]  = Txt(row, "Lock");

            var enhance = row.Find("RowEnhance");
            if (enhance != null && enhance.TryGetComponent<Button>(out var eb))
            {
                int bi = i;
                _slotBtn[i]    = eb;
                _slotBtnLbl[i] = enhance.GetComponentInChildren<TMP_Text>(true);
                eb.onClick.RemoveAllListeners();
                eb.onClick.AddListener(() => EnhancePartRow(bi));
            }

            int idx = i;
            if (row.TryGetComponent<Button>(out var rowBtn))
            {
                rowBtn.onClick.RemoveAllListeners();
                rowBtn.onClick.AddListener(() => SelectPartSlot(idx));
            }
            if (row.TryGetComponent<EventTrigger>(out var hover))
            {
                hover.triggers.Clear();
                AddTrigger(hover, EventTriggerType.PointerEnter, () => { _hoverPartSlot = idx; RefreshRangedCard(); });
                AddTrigger(hover, EventTriggerType.PointerExit,  () => { _hoverPartSlot = -1;  RefreshRangedCard(); });
            }
        }

        // ── 무기 카드 재바인딩 ──
        // 카드 참조 배열들은 readonly라 직렬화되지 않는다 — 구운 프리팹에서는 전부 null로 되살아난다.
        // RefreshAll은 _cardBg[0].color를 가드 없이 만지므로, 여기서 잇지 않으면 화면이 열리는 순간 죽는다.
        var target = FindDeep("TargetCard");
        if (target != null)
        {
            _cardBg[0]      = target.GetComponent<Image>();
            _cardBasePos[0] = Vector2.zero;              // 빌드 경로와 같은 펀치 연출 기준점
        }
        _cardName[0]      = FindDeep("WeaponName")?.GetComponent<TMP_Text>();
        _cardLevel[0]     = FindDeep("LevelTag")?.Find("Level")?.GetComponent<TMP_Text>();
        _cardAtk[0]       = FindDeep("Atk")?.GetComponent<TMP_Text>();
        _cardGaugeFill[0] = FindDeep("GaugeTrack")?.Find("Fill") as RectTransform;

        var partDetail = FindDeep("PartDetail");
        if (partDetail != null)
        {
            _cardBg[1]        = partDetail.GetComponent<Image>();
            _cardBasePos[1]   = ((RectTransform)partDetail).anchoredPosition;
            _cardName[1]      = Txt(partDetail, "Name");
            _cardLevel[1]     = Txt(partDetail, "Level");
            _cardAtk[1]       = Txt(partDetail, "Delta");
            _cardGaugeFill[1] = partDetail.Find("LevelBar")?.Find("Fill") as RectTransform;
        }

        Rewire(_enhanceBtn, OnEnhanceClicked);
        Rewire(_evolveBtn,  ShowEvolvePanel);
        Rewire(_branchABtn, () => OnBranchClicked(0));
        Rewire(_branchBBtn, () => OnBranchClicked(1));
        Rewire(FindDeep("Exit")?.GetComponent<Button>(),          ClosePopupUI);
        Rewire(FindDeep("EvolveCancel")?.GetComponent<Button>(),  HideEvolvePanel);

        // 탭 클릭은 람다로 건 <b>비영속 리스너</b>라 프리팹에 저장되지 않는다 —
        // 다시 걸지 않으면 구운 화면에서 원거리 파츠 탭으로 갈 길 자체가 없다.
        Rewire(FindDeep("TabWeapon")?.GetComponent<Button>(), () => SelectTab(0));
        Rewire(FindDeep("TabRanged")?.GetComponent<Button>(), () => SelectTab(1));
        SelectTab(0);
    }

    private Transform FindDeep(string name)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static TMP_Text Txt(Transform p, string n) => p.Find(n)?.GetComponent<TMP_Text>();

    /// <summary>같은 팝업이 다시 열려도 리스너가 겹쳐 쌓이지 않도록 지우고 건다.</summary>
    private static void Rewire(Button btn, UnityEngine.Events.UnityAction fn)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(fn);
    }

    private void BuildTopBar(Transform w)
    {
        // 브랜드 — 모루 + 「재련소」 + 한 줄 소개. 모루 아트 113×71 → 76×48.
        var anvil = ShopUIStyle.MakeImage(w, "Anvil", ShopUIStyle.Gold);
        Place(anvil.rectTransform, 24f, HdrY + 13f, 76f, 48f);
        anvil.preserveAspect = true;
        ShopUIStyle.Skin(anvil, _skin?.anvilIcon);

        var title = ShopUIStyle.MakeText(w, "Title", 40f, FontStyles.Bold,
                                         TextAlignmentOptions.BottomLeft, ShopUIStyle.Gold);
        title.text = "재련소";
        title.textWrappingMode = TextWrappingModes.NoWrap;   // 줄바꿈이 켜져 있으면 좁은 칸에서 세 줄로 쪼개진다
        Place(title.rectTransform, 112f, HdrY, 210f, 46f);

        var sub = MakeLabel(w, "Subtitle", 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft, TextMuted);
        sub.text = "쇠를 두들겨 운을 시험한다";
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        Place(sub.rectTransform, 112f, HdrY + 48f, 220f, 24f);

        // 탭 — 무기강화 / 원거리 파츠
        _tabWeaponImg = MakeTab(w, "TabWeapon", "무기강화",    Tab0X, () => SelectTab(0));
        _tabRangedImg = MakeTab(w, "TabRanged", "원거리 파츠", Tab1X, () => SelectTab(1));

        // 재화 — HUD와 같은 칸. 이 방에서 쓰는 강화재료가 앞, 원석이 뒤.
        _matPill = MakeCurrencyPill(w, "MatPill", MatPillX, _skin?.MaterialIcon, MatTint, out _fuelText);
        _orePill = MakeCurrencyPill(w, "OrePill", OrePillX, _skin?.OreIcon,      OreTint, out _oreText);

        // 나가기 — 청동. 신규 납품 「나가기 버튼」은 파란 판이라 이 화면의 청동·주황 톤에서 혼자 튀었다.
        var exitBtn = MakeStyledButton(w, "Exit", "나가기", out var exitLbl);
        Place((RectTransform)exitBtn.transform, ExitX, ExitY, ExitW, ExitH);
        exitBtn.GetComponent<Image>().color = BronzeEdge;
        var exitFill = ShopUIStyle.MakeImage(exitBtn.transform, "Fill", BronzeFill);
        ShopUIStyle.Stretch(exitFill.rectTransform, 1.5f);
        exitFill.transform.SetAsFirstSibling();   // 라벨 뒤
        ShopUIStyle.ApplyButtonColors(exitBtn, exitFill);
        exitLbl.color = BronzeInk;
        exitBtn.onClick.AddListener(ClosePopupUI);
    }

    /// <summary>
    /// 재화 칸 — HUD와 <b>같은 조각·같은 색</b>(칸_바탕 + 재화색으로 물든 칸_테두리 + 아이콘 + 숫자).
    /// 런 내내 보던 칸이 재련소에서도 그대로 보여야 "이 숫자가 그 재료"라는 게 한눈에 이어진다.
    /// 이름은 HUD처럼 칸 안에 쓰지 않고 툴팁으로 둔다(InitRuntimeFx).
    /// </summary>
    private GameObject MakeCurrencyPill(Transform w, string name, float x, Sprite icon, Color tint, out TMP_Text value)
    {
        var pill = ShopUIStyle.MakeImage(w, name, ShopUIStyle.GoldPillBg, raycast: true);   // raycast = 툴팁 호버면
        Place(pill.rectTransform, x, PillY, PillW, PillH);
        ShopUIStyle.Skin(pill, _skin?.CurrencyInner, sliced: true);

        if (_skin?.CurrencyFrame != null)
        {
            var frame = ShopUIStyle.MakeImage(pill.transform, "Frame", tint);
            ShopUIStyle.Stretch(frame.rectTransform);
            ShopUIStyle.Skin(frame, _skin.CurrencyFrame, sliced: true, tint: tint);
        }

        var ic = ShopUIStyle.MakeImage(pill.transform, "Icon", tint);
        PlaceIn(ic.rectTransform, 18f, 10f, 30f, 30f, PillW, PillH);
        ic.preserveAspect = true;
        ShopUIStyle.Skin(ic, icon);

        value = ShopUIStyle.MakeText(pill.transform, "Value", 24f, FontStyles.Bold,
                                     TextAlignmentOptions.MidlineRight, Color.white);
        value.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(value.rectTransform, 56f, 4f, 102f, 42f, PillW, PillH);
        return pill.gameObject;
    }

    /// <summary>상단 탭 하나 — 두 탭 모두 미선택 아트를 쓰고, 선택은 밝기와 금빛 밑줄로 보여준다(ApplyTab).</summary>
    private Image MakeTab(Transform w, string name, string label, float xFromLeft, Action onClick)
    {
        var tab = ShopUIStyle.MakeImage(w, name, ShopUIStyle.BandFill, raycast: true);
        Place(tab.rectTransform, xFromLeft, TabY, TabW, TabH);
        var t = ShopUIStyle.MakeText(tab.transform, "L", 20f, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        t.text = label;
        ShopUIStyle.Stretch(t.rectTransform);
        var btn = tab.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, tab);
        btn.onClick.AddListener(() => onClick?.Invoke());

        var line = ShopUIStyle.MakeImage(tab.transform, "Underline", ShopUIStyle.Gold);
        PlaceIn(line.rectTransform, TabW * 0.14f, TabH + 2f, TabW * 0.72f, 4f, TabW, TabH);
        line.gameObject.SetActive(false);
        return tab;
    }

    /// <summary>탭 전환 — 스테이지 표시 토글 + 대상 슬롯 전환 + 탭 아트 갱신 + 갱신.</summary>
    private void SelectTab(int tab)
    {
        _activeTab = tab;
        if (_meleeStage != null)  _meleeStage.gameObject.SetActive(tab == 0);
        if (_rangedStage != null) _rangedStage.gameObject.SetActive(tab == 1);

        ApplyTab(_tabWeaponImg, _skin?.tabWeaponOff, tab == 0);
        ApplyTab(_tabRangedImg, _skin?.tabRangedOff, tab == 1);

        // 액션 컬럼의 강화·진화 버튼은 <b>근접 탭 전용</b>이다.
        // 파츠 탭에는 행마다 강화 버튼이 있고, 진화(승급)는 근접 무기 대상이라
        // 원거리 화면에 남겨두면 "지금 무엇을 올리는 버튼인지"가 어긋난다.
        if (_enhanceBtn != null) _enhanceBtn.gameObject.SetActive(tab == 0);
        if (_evolveBtn  != null && tab == 1) _evolveBtn.gameObject.SetActive(false);
        if (tab == 0)
        {
            if (_enhanceLabel != null) _enhanceLabel.text = "강화하기";
            SkinButton(_enhanceBtn, _skin?.enhanceButton, _enhanceLabel);
        }

        // 강화 대상 슬롯은 <b>항상 근접</b>이다. 원거리 무기 강화를 폐지했으므로 탭을 옮겨도 바뀌지 않는다.
        // 예전엔 탭1에서 Slot1로 바꿨는데, 연출 큐에 남은 예약이 탭 전환 뒤 실행되면
        // 엉뚱한 슬롯을 강화할 수 있는 경로였다.
        _targetSlot = PlayerWeaponManager.Slot0;

        // 결과 줄은 직전 행동의 결과다 — 탭을 옮기면 다른 대상의 결과(「성공! +6」)가 남아 보였다(실측 09-20).
        if (_resultText != null) _resultText.text = "";

        // 정보창은 탭 전환에도 살아 있는 공용 컬럼이다 — 파츠 탭에서는 발사 미리보기로 쓴다.
        if (_infoTitle != null) _infoTitle.text = tab == 0 ? "강화 정보" : "발사 미리보기";

        if (tab == 1)
        {
            // 첫 파츠를 골라 둔다 — 가운데 상세가 「파츠를 고르세요」 빈 판으로 열렸다(09-25). 다시 누르면 해제는 그대로.
            if (_selectedPartSlot < 0 && (Managers.WeaponParts?.All?.Count ?? 0) > 0) _selectedPartSlot = 0;
            RefreshRangedParts();   // 파츠 레벨·비용을 열 때마다 최신으로
            RefreshRangedCard();
        }
        if (_controller != null) { UpdateTargetDialogue(); RefreshAll(); }
    }

    /// <summary>
    /// 탭 아트. 선택 탭도 <b>미선택 아트</b>를 쓴다 — 선택 아트(금판 위 주황 글자)는 대비가 낮아 글자가 묻혔다
    /// (09-19 전수 검수). 선택은 밝기(흐림 해제)와 금빛 밑줄로 보여준다. 아트에 탭 이름이 구워져 있어 코드 라벨은 끈다.
    /// </summary>
    private static void ApplyTab(Image img, Sprite art, bool active)
    {
        if (img == null) return;
        var lbl = img.GetComponentInChildren<TMP_Text>(true);
        if (art != null)
        {
            ShopUIStyle.Skin(img, art, sliced: true);
            img.color = active ? Color.white : TabIdle;
            if (lbl != null) lbl.gameObject.SetActive(false);
        }
        else
        {
            img.color = active ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
            if (lbl != null) lbl.gameObject.SetActive(true);
        }
        var line = img.transform.Find("Underline");
        if (line != null) line.gameObject.SetActive(active);
    }

    private void BuildStages(Transform w)
    {
        var area = ShopUIStyle.MakeRect(w, "StageArea").GetComponent<RectTransform>();
        Place(area, StageX, StageY, StagePanelW, StagePanelH);
        _stageArea = area;

        _meleeStage  = MakeStage(area, "MeleeStage");
        _rangedStage = MakeStage(area, "RangedStage");

        BuildMeleeCard(_meleeStage);
        BuildRangedCard(_rangedStage);

        BuildStageResult(area);   // 마지막 형제 = 두 스테이지 위에 겹쳐 그려진다
    }

    /// <summary>
    /// 스테이지 중앙 결과 팝업. 성공/실패/잭팟 판정이 반대편 정보창 여덟째 줄에만 뜨면
    /// 시선(카드·게이지)과 어긋나 판정을 놓친다 — 같은 문구를 카드 바로 위에 크게 겹쳐 띄운다.
    /// 두 탭 스테이지가 같은 자리에 겹치므로 탭 컨테이너가 아니라 StageArea에 직접 붙인다.
    /// </summary>
    private void BuildStageResult(Transform area)
    {
        var t = ShopUIStyle.MakeText(area, "StageResult", SuccessFontSize, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        // 무기 위 한가운데 — 망치가 닿는 순간 판정이 시선 자리에 찍힌다.
        PlaceIn(t.rectTransform, 30f, 250f, StagePanelW - 60f, 100f, StagePanelW, StagePanelH);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        _stageResult = t;
        t.gameObject.SetActive(false);
    }

    /// <summary>
    /// 결과 팝업 표시. 문구는 정보창(<see cref="_resultText"/>)이 방금 만든 것을 그대로 쓴다 —
    /// 두 곳에서 각자 문장을 만들면 판정이 어긋날 수 있다.
    /// </summary>
    private void ShowStageResult(string richText, float fontSize, float popScale)
    {
        if (_stageResult == null || string.IsNullOrEmpty(richText)) return;
        _stageResult.text     = richText;
        _stageResult.fontSizeMax = fontSize;   // 자동 크기가 켜져 있어 최대를 같이 올려야 잭팟 도장이 커진다
        _stageResult.fontSize    = fontSize;
        _stageResultSeq++;
        StageResultAsync(_stageResultSeq, popScale).Forget();
    }

    private void HideStageResult()
    {
        _stageResultSeq++;   // 굴러가던 페이드는 세대가 바뀌어 스스로 물러난다
        if (_stageResult != null) _stageResult.gameObject.SetActive(false);
    }

    /// <summary>등장 → 유지 → 페이드. 연출 잠금 바깥에서 굴러 연타를 막지 않는다(세대가 바뀌면 즉시 물러남).</summary>
    private async UniTaskVoid StageResultAsync(int seq, float popScale)
    {
        var rt = _stageResult.rectTransform;
        _stageResult.gameObject.SetActive(true);
        _stageResult.alpha = 1f;

        try
        {
            float t = 0f;
            while (t < 1f)
            {
                if (seq != _stageResultSeq) return;
                t = Mathf.Min(t + Time.unscaledDeltaTime / ResultPopDur, 1f);
                rt.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, t);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            t = 0f;
            while (t < ResultHoldDur)
            {
                if (seq != _stageResultSeq) return;
                t += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            t = 0f;
            while (t < ResultFadeDur)
            {
                if (seq != _stageResultSeq) return;
                t += Time.unscaledDeltaTime;
                _stageResult.alpha = 1f - t / ResultFadeDur;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }   // 패널 파괴

        if (seq != _stageResultSeq) return;
        _stageResult.gameObject.SetActive(false);
        _stageResult.alpha = 1f;
    }

    /// <summary>
    /// 탭별 무대 — StageArea를 채우는 청동 테두리 판. 안쪽에 마법진 아트(바탕 2264×1476 = 1.53)를 <b>덮어</b> 깐다.
    /// 무대(960×748 = 1.28)에 늘려 끼우면 마법진이 가로로 눌린다 — 넘치는 좌우는 안쪽 마스크가 자른다.
    /// 바깥 마스크는 불꽃·빛살이 무대 밖(정보창·머리줄)으로 새지 않게 한다.
    /// </summary>
    private RectTransform MakeStage(Transform parent, string name)
    {
        var img = ShopUIStyle.MakeImage(parent, name, StageEdge, raycast: true);
        ShopUIStyle.Stretch(img.rectTransform);
        img.gameObject.AddComponent<RectMask2D>();

        var ink = ShopUIStyle.MakeImage(img.transform, "Ink", StageInk);
        ShopUIStyle.Stretch(ink.rectTransform, 2f);
        ink.gameObject.AddComponent<RectMask2D>();
        MakeCoverArt(ink.transform, "Art", _skin?.stagePanel);
        var shade = ShopUIStyle.MakeImage(ink.transform, "Shade", StageShade);
        ShopUIStyle.Stretch(shade.rectTransform);
        return img.rectTransform;
    }

    /// <summary>
    /// 아트를 칸을 <b>덮도록</b>(cover) 깐다. 넘치는 쪽은 부모의 RectMask2D가 자른다.
    /// 비율이 다른 칸에 그냥 늘리면 원형 문양(마법진)이 타원으로 눌린다.
    /// </summary>
    private static void MakeCoverArt(Transform parent, string name, Sprite sprite)
    {
        if (sprite == null) return;
        var img = ShopUIStyle.MakeImage(parent, name, Color.white);
        img.sprite = sprite;
        var fit = img.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode  = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = sprite.rect.width / sprite.rect.height;
    }

    /// <summary>창 네 모서리 마름모 — 의뢰서 테두리 장식. 모서리에서 6px 밖으로 걸친다.</summary>
    private void BuildCornerDiamonds(Transform w)
    {
        if (_skin?.diamondDecor == null) return;
        const float size = 34f, out_ = 6f;
        PlaceDiamond(w, -out_,                -out_);
        PlaceDiamond(w, MockW - size + out_,  -out_);
        PlaceDiamond(w, -out_,                MockH - size + out_);
        PlaceDiamond(w, MockW - size + out_,  MockH - size + out_);
    }

    private void PlaceDiamond(Transform w, float x, float y)
    {
        var d = ShopUIStyle.MakeImage(w, "Diamond", Color.white);
        Place(d.rectTransform, x, y, 34f, 34f);
        d.preserveAspect = true;
        ShopUIStyle.Skin(d, _skin.diamondDecor);
    }

    /// <summary>입자·연출용 빈 층 — 무대를 꽉 채운다. 자식은 무대 좌상단 기준 px로 움직인다.</summary>
    private static RectTransform MakeLayer(RectTransform stage, string name)
    {
        var rt = ShopUIStyle.MakeRect(stage, name).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(rt);
        return rt;
    }

    /// <summary>
    /// 강화 게이지 — 검은 트랙 + 불꽃 채움(Filled: 늘리지 않고 잘라 보인다) + 테두리 + 단계 눈금.
    /// 트랙·채움 792×30, 테두리 840×58(시안 좌표). 눈금은 상한만큼 켜서 RefreshTicks가 칸을 나눈다.
    /// fill의 RectTransform을 돌려준다(_cardGaugeFill[0]).
    /// </summary>
    private RectTransform BuildGauge(RectTransform stage)
    {
        var box = ShopUIStyle.MakeRect(stage, "Gauge").GetComponent<RectTransform>();
        PlaceIn(box, GaugeX, GaugeY, GaugeW, GaugeH, StagePanelW, StagePanelH);

        var track = ShopUIStyle.MakeImage(box, "GaugeTrack", GaugeTrack);
        PlaceIn(track.rectTransform, 24f, 18f, GaugeW - 48f, 30f, GaugeW, GaugeH);
        ShopUIStyle.Skin(track, _skin?.gaugeTrack, sliced: true);

        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", GaugeFillC);
        ShopUIStyle.Stretch(fill.rectTransform);
        ShopUIStyle.Skin(fill, _skin?.gaugeFill);
        fill.type       = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;
        _gaugeFillImg = fill;

        // 다음 한 칸 — 성공하면 여기까지 찬다(룬 선택의 「놓을 자리」처럼 결과를 미리 보인다). 칸 위치는 SetGauge가 맞춘다.
        _gaugeGhost = ShopUIStyle.MakeImage(track.transform, "NextGhost", GhostColor);
        _gaugeGhost.raycastTarget = false;
        var gRT = _gaugeGhost.rectTransform;
        gRT.anchorMin = Vector2.zero;
        gRT.anchorMax = new Vector2(0f, 1f);
        gRT.offsetMin = gRT.offsetMax = Vector2.zero;

        if (_skin?.gaugeFrame != null)
        {
            var frame = ShopUIStyle.MakeImage(box, "GaugeFrame", Color.white);
            PlaceIn(frame.rectTransform, 0f, 4f, GaugeW, 58f, GaugeW, GaugeH);
            ShopUIStyle.Skin(frame, _skin.gaugeFrame, sliced: true);
        }

        var ticks = ShopUIStyle.MakeRect(box, "Ticks").GetComponent<RectTransform>();
        PlaceIn(ticks, 24f, 58f, GaugeW - 48f, 34f, GaugeW, GaugeH);
        _ticks = new TMP_Text[MaxTicks];
        for (int i = 0; i < MaxTicks; i++)
        {
            var t = MakeLabel(ticks, $"Tick{i}", 18f, FontStyles.Bold, TextAlignmentOptions.TopLeft, TickDim);   // 16 → 18
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.margin = new Vector4(6f, 4f, 0f, 0f);
            var line = ShopUIStyle.MakeImage(t.transform, "Line", TickLine);
            ShopUIStyle.Anchor(line.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                               Vector2.zero, new Vector2(1.5f, 0f));
            t.gameObject.SetActive(false);
            _ticks[i] = t;
        }
        return fill.rectTransform;
    }

    /// <summary>
    /// 무기강화 무대 — 모루 위 무기 한 자루. 위에서부터 이름 · 단계 방패 · 무기 칸(+달굼 빛) · 위험 배지 · 모루 ·
    /// 공격력 한 줄 · 게이지. 망치·불꽃·빛살·배너는 연출 때만 보인다.
    /// 예전의 「무기 카드 ▸ 결과 카드」 두 장은 같은 무기를 두 번 그렸다 — 한 칸으로 합치고 단계는 방패가 말한다.
    /// </summary>
    private void BuildMeleeCard(RectTransform stage)
    {
        // 화로 빛 — 무대 아래에서 올라오는 주황 빛. 불씨가 그 위로 떠오른다(스프라이트는 Bind에서).
        _forgeGlow = ShopUIStyle.MakeImage(stage, "ForgeGlow", ForgeGlowColor);
        PlaceIn(_forgeGlow.rectTransform, -160f, 460f, StagePanelW + 320f, 620f, StagePanelW, StagePanelH);
        _emberRoot = MakeLayer(stage, "Embers");

        // 무기 이름(+전설) · 타입/티어 — 무대 맨 위 가운데
        _cardName[0] = MakeLabel(stage, "WeaponName", 26f, FontStyles.Bold,
                                 TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        PlaceIn(_cardName[0].rectTransform, 40f, 18f, StagePanelW - 80f, 36f, StagePanelW, StagePanelH);
        _focusTypeText = MakeLabel(stage, "TypeTier", 16f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
        PlaceIn(_focusTypeText.rectTransform, 40f, 56f, StagePanelW - 80f, 24f, StagePanelW, StagePanelH);

        // 잭팟 빛살 · 달굼 빛 — 무기 칸 뒤
        _rays = ShopUIStyle.MakeImage(stage, "Rays", RaysColor);
        PlaceIn(_rays.rectTransform, 180f, -30f, 600f, 600f, StagePanelW, StagePanelH);
        _rays.gameObject.SetActive(false);
        _weaponGlow = ShopUIStyle.MakeImage(stage, "WeaponGlow", GlowColor);
        PlaceIn(_weaponGlow.rectTransform, GlowX, GlowY, GlowWH, GlowWH, StagePanelW, StagePanelH);

        // 무기 칸 — 「강화 무기 바탕」(524 정사각). 연출(펀치·플래시) 대상이라 무기 그림을 자식으로 단다.
        var slot = ShopUIStyle.MakeImage(stage, "TargetCard", ShopUIStyle.CardFill);
        PlaceIn(slot.rectTransform, SlotX, SlotY, SlotWH, SlotWH, StagePanelW, StagePanelH);
        ShopUIStyle.Skin(slot, _skin?.slotFrame);
        _cardBg[0]      = slot;
        _cardBasePos[0] = Vector2.zero;   // 펀치 연출은 anchoredPosition을 흔든다 — 비율 앵커라 기준점은 0이다

        // 무기 그림 — 장착 무기 아이콘(WeaponData.icon, RefreshAll). 없으면 「무기 예시」.
        _weaponImg = ShopUIStyle.MakeImage(slot.transform, "Weapon", Color.white);
        PlaceIn(_weaponImg.rectTransform, 50f, 50f, SlotWH - 100f, SlotWH - 100f, SlotWH, SlotWH);
        _weaponImg.preserveAspect = true;
        if (_skin?.weaponExample != null) ShopUIStyle.Skin(_weaponImg, _skin.weaponExample);
        else _weaponImg.color = new Color(1f, 1f, 1f, 0.12f);

        // 단계 방패 — 「강화 표기」 위에 +단계 / 상한
        var tag = ShopUIStyle.MakeImage(stage, "LevelTag", LevelTagFill);
        PlaceIn(tag.rectTransform, LvTagX, LvTagY, LvTagW, LvTagH, StagePanelW, StagePanelH);
        ShopUIStyle.Skin(tag, _skin?.LevelTag);
        _cardLevel[0] = ShopUIStyle.MakeText(tag.transform, "Level", 44f, FontStyles.Bold,
                                             TextAlignmentOptions.Center, LevelGold);
        _cardLevel[0].textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_cardLevel[0].rectTransform, 8f, 10f, LvTagW - 16f, 70f, LvTagW, LvTagH);

        // 위험 배지 — 도박 구간(실패 시 하락)에서만. 아트에 「위험-하락」이 구워져 있다.
        _zoneBg = ShopUIStyle.MakeImage(stage, "ZoneBg", DangerFill);
        PlaceIn(_zoneBg.rectTransform, RiskX, RiskY, RiskW, RiskH, StagePanelW, StagePanelH);
        ShopUIStyle.Skin(_zoneBg, _skin?.riskBadge);
        _zoneTag = MakeLabel(_zoneBg.transform, "ZoneTag", 18f, FontStyles.Bold,
                             TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        _zoneTag.text = "위험 · 하락";
        ShopUIStyle.Stretch(_zoneTag.rectTransform);
        _zoneTag.gameObject.SetActive(_skin?.riskBadge == null);   // 아트가 있으면 글자는 이미 그려져 있다
        _zoneBg.gameObject.SetActive(false);

        // 모루
        var anvil = ShopUIStyle.MakeImage(stage, "StageAnvil", ShopUIStyle.Gold);
        PlaceIn(anvil.rectTransform, AnvilX, AnvilY, AnvilW, AnvilH, StagePanelW, StagePanelH);
        anvil.preserveAspect = true;
        ShopUIStyle.Skin(anvil, _skin?.anvilIcon);

        _hammer    = BuildHammer(stage);
        _sparkRoot = MakeLayer(stage, "Sparks");

        // 공격력 전→후. 무대가 보여주는 건 단계뿐이라 "그래서 얼마나 세지나"를 여기 한 줄로 둔다.
        // 공격력 표기칸 아트를 글자 <b>뒤에</b> 깐다 — 형제 순서가 곧 그리기 순서라 먼저 만든다.
        if (_skin?.atkPlate != null)
        {
            var atkPlate = ShopUIStyle.MakeImage(stage, "AtkPlate", Color.white);
            PlaceIn(atkPlate.rectTransform, AtkX, AtkY, AtkW, AtkH, StagePanelW, StagePanelH);
            ShopUIStyle.Skin(atkPlate, _skin.atkPlate);
        }
        _cardAtk[0] = MakeLabel(stage, "Atk", 28f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        _cardAtk[0].textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_cardAtk[0].rectTransform, AtkX, AtkY, AtkW, AtkH, StagePanelW, StagePanelH);

        _cardGaugeFill[0] = BuildGauge(stage);

        // 판정 빛(무대 전체) · 잭팟 배너 — 맨 위
        _stageFlash = ShopUIStyle.MakeImage(stage, "Flash", Color.clear);
        // 무기 칸 둘레로 번지는 빛(반경 500×400) — 무대 가장자리는 거의 물들지 않는다.
        PlaceIn(_stageFlash.rectTransform, StagePanelW * 0.5f - 500f, 270f - 400f, 1000f, 800f, StagePanelW, StagePanelH);
        _jackpotBanner = ShopUIStyle.MakeText(stage, "JackpotBanner", 64f, FontStyles.Bold,
                                              TextAlignmentOptions.Center, JackpotInk);
        _jackpotBanner.text = "◆ JACKPOT ◆";
        _jackpotBanner.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_jackpotBanner.rectTransform, 0f, 30f, StagePanelW, 84f, StagePanelW, StagePanelH);
        _jackpotBanner.gameObject.SetActive(false);
    }

    /// <summary>
    /// 망치(자리 표시) — 손잡이 끝이 회전축, 머리는 왼쪽 <see cref="HammerReach"/>px. 0°가 내려친 자세다.
    /// 대장장이 망치질 아트가 오면 이 그림만 갈아끼운다. 평소엔 투명(연출 때만 보인다).
    /// 회전이 목적이라 자식은 비율 앵커가 아니라 축 기준 px로 단다.
    /// </summary>
    private RectTransform BuildHammer(RectTransform stage)
    {
        var root = ShopUIStyle.MakeRect(stage, "Hammer", typeof(CanvasGroup)).GetComponent<RectTransform>();
        var pivot = new Vector2(HammerPivotX / StagePanelW, 1f - StrikeY / StagePanelH);
        ShopUIStyle.Anchor(root, pivot, pivot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        root.localRotation = Quaternion.Euler(0f, 0f, HammerRaisedDeg);

        var mid = new Vector2(0.5f, 0.5f);
        var handle = ShopUIStyle.MakeImage(root, "Handle", HammerWood);
        ShopUIStyle.Anchor(handle.rectTransform, mid, mid, new Vector2(1f, 0.5f), Vector2.zero,
                           new Vector2(HammerReach - 12f, 16f));

        var head = ShopUIStyle.MakeFrame(root, "Head", HammerEdge, HammerSteel, 3f);
        ShopUIStyle.Anchor((RectTransform)head.transform.parent, mid, mid, mid,
                           new Vector2(-HammerReach, 0f), new Vector2(54f, 90f));
        var shine = ShopUIStyle.MakeImage(head.transform, "Shine", HammerShine);
        ShopUIStyle.Anchor(shine.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                           new Vector2(7f, 0f), new Vector2(8f, -14f));

        _hammerGroup = root.GetComponent<CanvasGroup>();
        _hammerGroup.alpha          = 0f;
        _hammerGroup.blocksRaycasts = false;
        _hammerGroup.interactable   = false;
        return root;
    }

    // ── 원거리 탭 레이아웃 상수 (스테이지 754×491 = StagePanelW×H, 좌상단 기준) ──
    //
    // 이 탭에는 <b>행동이 하나</b>다 — 파츠 강화. 원거리 무기 강화는 폐지했다:
    // 그건 파츠 슬롯 게이지를 채우려고 붙은 것이고, 내장형 전환으로 슬롯이 사라지면서 이유도 사라졌다
    // (원래 기획 2026-07-18도 "무기 자체 레벨 강화 없음 · 성장은 파츠로만"이었다).
    // 공격력·공격속도는 캐릭터 스탯이 담당하므로 파츠는 '발사 형태'만 다룬다.
    //
    // 예전엔 옛 스테이지(1257×599) 좌표를 배율 레이어(×0.6)로 통째로 줄였는데, 창 fitter의 글자 배율과
    // 겹쳐 실제 글자가 10~13px까지 내려갔다(실측 09-08). 이제 스테이지 실크기 좌표로 짜서 비율 앵커로
    // 굳히고(PlaceIn) fitter 배율만 받는다 — 근접 탭과 같은 규약. 3띠(헤더/본문 2열)·좌우 폭 비율은 그대로다.
    // 개편(09-20): 무대가 754×491 → 960×748로 커지며 행·글자를 키웠다(행 74 → 108, 글자 14~17 → 17~21).
    private const float RxPad     = 28f;    // 무대 좌우 여백
    private const float RxHeadY   = 22f;    // 헤더 띠 y
    private const float RxHeadH   = 36f;
    private const float RxBodyY   = 84f;    // 본문 y
    private const float RxBodyH   = 596f;   // 5행 × 108 + 4갭 × 14 (본문 높이와 정확히 일치)
    private const float RxColLW   = 440f;   // 좌 — 파츠 목록
    private const float RxColRX   = 492f;   // 우 — 상세 시작 x
    private const float RxColRW   = 440f;   // 492 + 440 = 932 = 960 − 28
    private const float RxRowH    = 108f;   // 파츠 행
    private const float RxRowGap  = 14f;

    /// <summary>목업 좌상단 기준 사각형을 창(<see cref="MockW"/>×<see cref="MockH"/>) 기준 비율 앵커로 굳힌다.</summary>
    private static void Place(RectTransform rt, float x, float y, float w, float h)
        => UIProportional.Place(rt, x, y, w, h, MockW, MockH);

    /// <summary>목업 좌표를 <b>부모 사각형 기준</b>으로 앉힌다(스테이지 안쪽 요소용).</summary>
    private static void PlaceIn(RectTransform rt, float x, float y, float w, float h, float pw, float ph)
        => UIProportional.Place(rt, x, y, w, h, pw, ph);

    private static void PlaceTL(RectTransform rt, float x, float y, float w, float h)
    {
        var tl = new Vector2(0f, 1f);
        ShopUIStyle.Anchor(rt, tl, tl, tl, new Vector2(x, -y), new Vector2(w, h));
    }

    /// <summary>
    /// 원거리 파츠 탭 — 3단 구성.
    ///  · 헤더  : 제목 + 슬롯 요약
    ///  · 본문  : 좌 조합 4칸(2×2) / 우 선택 파츠 상세 + 파츠 강화 버튼
    ///  · 무기행: 원거리 무기 정보 + 투자 게이지 + 무기 강화 버튼
    /// </summary>
    private void BuildRangedCard(RectTransform stage)
    {
        _dockTypeText = MakeLabel(stage, "RangedTitle", 26f, FontStyles.Bold,
                                  TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        PlaceIn(_dockTypeText.rectTransform, RxPad, RxHeadY, RxColLW, RxHeadH, StagePanelW, StagePanelH);
        _dockTypeText.text = "원거리 파츠";

        _slotSummary = MakeLabel(stage, "SlotSummary", 17f, FontStyles.Normal,
                                 TextAlignmentOptions.MidlineRight, TextMuted);
        PlaceIn(_slotSummary.rectTransform, RxColRX - 40f, RxHeadY, RxColRW + 40f, RxHeadH, StagePanelW, StagePanelH);

        BuildRangedParts(stage);
        BuildPartDetail(stage);
    }

    /// <summary>
    /// 우측 상세 패널. 고른 파츠 하나를 깊게 보여주고 <b>같은 패널 안에서 강화</b>한다 —
    /// 읽은 자리에서 바로 누르고, 결과(레벨·효과)도 이 패널에서 갱신되므로 연타 시 시선이 왕복하지 않는다.
    /// 연출 대상 <c>_cardBg[1]</c>도 이 패널이라 성공 펀치·플래시가 누른 자리에서 터진다.
    /// </summary>
    private void BuildPartDetail(RectTransform stage)
    {
        var panel = ShopUIStyle.MakeImage(stage, "PartDetail", new Color(0.11f, 0.09f, 0.14f, 1f));
        PlaceIn(panel.rectTransform, RxColRX, RxBodyY, RxColRW, RxBodyH, StagePanelW, StagePanelH);
        // 정보창과 같은 「강화정보 칸」(1028×1484 = 0.69 ≈ 패널 0.74). 옛 「원거리 강화 바탕」(316×233)은
        // 이 패널에선 세로로 2.6배 늘어나 모서리 사선이 뭉개졌다.
        ShopUIStyle.Skin(panel, _skin?.infoPanel, sliced: true);
        _cardBg[1]      = panel;
        _cardBasePos[1] = panel.rectTransform.anchoredPosition;
        var c = panel.transform;

        const float padX = 24f;
        float innerW = RxColRW - padX * 2f;

        _cardName[1] = MakeLabel(c, "Name", 26f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceIn(_cardName[1].rectTransform, padX, 26f, innerW, 36f, RxColRW, RxBodyH);

        _partKindText = MakeLabel(c, "Kind", 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        PlaceIn(_partKindText.rectTransform, padX, 66f, innerW, 26f, RxColRW, RxBodyH);

        _cardLevel[1] = MakeLabel(c, "Level", 22f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.Gold);
        PlaceIn(_cardLevel[1].rectTransform, padX, 108f, innerW, 30f, RxColRW, RxBodyH);

        _cardGaugeFill[1] = BuildBar(c, "LevelBar", padX, 144f, innerW, 12f, RxColRW, RxBodyH);

        _cardAtk[1] = MakeLabel(c, "Delta", 22f, FontStyles.Bold, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceIn(_cardAtk[1].rectTransform, padX, 172f, innerW, 34f, RxColRW, RxBodyH);

        _partGrowthText = MakeLabel(c, "Growth", 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft, TextMuted);
        PlaceIn(_partGrowthText.rectTransform, padX, 214f, innerW, 26f, RxColRW, RxBodyH);

        _partDescText = MakeLabel(c, "Desc", 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft, ShopUIStyle.TextPrimary);
        PlaceIn(_partDescText.rectTransform, padX, 254f, innerW, 170f, RxColRW, RxBodyH);

        _partSynergyText = MakeLabel(c, "Synergy", 17f, FontStyles.Normal,
                                     TextAlignmentOptions.TopLeft, new Color(0.50f, 0.89f, 1f));
        PlaceIn(_partSynergyText.rectTransform, padX, 434f, innerW, 54f, RxColRW, RxBodyH);

        // 강화 버튼은 좌열의 각 행으로 옮겼다 — 이 패널은 이제 <b>결과 전담</b>이다.
        // (_partEnhanceBtn / _partCostText 는 null로 남으며, 갱신 함수들이 null을 걸러낸다.)
        _partCostText = MakeLabel(c, "PartCost", 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft, TextMuted);
        PlaceIn(_partCostText.rectTransform, padX, RxBodyH - 60f, innerW, 30f, RxColRW, RxBodyH);
    }

    /// <summary>단순 진행 막대(트랙 + 채움). 채움 RectTransform을 돌려준다.</summary>
    private RectTransform BuildBar(Transform parent, string name, float x, float y, float w, float h,
                                   float pw, float ph)
    {
        var track = ShopUIStyle.MakeImage(parent, name, new Color(0.10f, 0.09f, 0.12f, 1f));
        PlaceIn(track.rectTransform, x, y, w, h, pw, ph);
        // 근접 「강화 게이지바」(2048x234 = 8.75:1)는 이 얇은 막대(최대 30:1)엔 3.5배 늘어난다.
        // 호출처가 둘 다 원거리이므로 근접 아트를 걷어내고 무지 판으로 둔다.

        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", ShopUIStyle.Gold);
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        return fr;
    }


    /// <summary>
    /// 파츠 슬롯 4칸. 내용은 <see cref="RangedPartsState"/>가 정하고, 클릭하면 강화 대상으로 선택된다.
    /// 슬롯 해금 수는 원거리 무기 강화 레벨에 종속되므로 잠긴 칸은 회색으로 남는다.
    /// </summary>
    private void BuildRangedParts(Transform c)
    {
        // 5행 리스트. 행 = 파츠 정의 목록의 인덱스이며 슬롯이 아니다 —
        // 파츠는 항상 5종 전부 존재하고 레벨(0=꺼짐)만 다르다.
        var data = Managers.WeaponParts;

        for (int i = 0; i < PartRows; i++)
        {
            float ry = RxBodyY + i * (RxRowH + RxRowGap);

            var row = ShopUIStyle.MakeImage(c, $"PartRow{i}", new Color(0.10f, 0.08f, 0.13f, 1f));
            PlaceIn(row.rectTransform, RxPad, ry, RxColLW, RxRowH, StagePanelW, StagePanelH);
            // 납품 카드 아트(316×233)는 가로로 긴 행에 그대로 늘리면 테두리선이 뭉개진다 —
            // 런타임에 9-slice 사본을 만들어 입힌다(ApplyPartRowArt). 여기선 자리만 만든다.
            _partsSlots[i] = row;
            var rowFrame = ShopUIStyle.MakeImage(row.transform, "RowFrame", new Color(1f, 1f, 1f, 0f));
            ShopUIStyle.Stretch(rowFrame.rectTransform);
            rowFrame.raycastTarget = false;
            _partsFrames[i] = rowFrame;

            // 행 108px 안 3줄: 표식·이름·레벨 / 효과값 / 레벨 막대. 버튼은 오른쪽 세로 중앙.
            _slotKind[i] = MakeLabel(row.transform, "Mark", 20f, FontStyles.Bold,
                                     TextAlignmentOptions.Center, ShopUIStyle.Gold);
            PlaceIn(_slotKind[i].rectTransform, 14f, 14f, 26f, 32f, RxColLW, RxRowH);

            _slotName[i] = MakeLabel(row.transform, "Nm", 21f, FontStyles.Bold,
                                     TextAlignmentOptions.MidlineLeft, ShopUIStyle.TextPrimary);
            PlaceIn(_slotName[i].rectTransform, 48f, 12f, 210f, 36f, RxColLW, RxRowH);

            _slotLv[i] = MakeLabel(row.transform, "Lv", 18f, FontStyles.Bold,
                                   TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
            PlaceIn(_slotLv[i].rectTransform, 262f, 14f, 64f, 32f, RxColLW, RxRowH);

            _slotVal[i] = MakeLabel(row.transform, "Val", 17f, FontStyles.Normal,
                                    TextAlignmentOptions.MidlineLeft, TextMuted);
            PlaceIn(_slotVal[i].rectTransform, 48f, 52f, 278f, 26f, RxColLW, RxRowH);

            _slotBar[i] = BuildBar(row.transform, "LvBar", 48f, 86f, 278f, 8f, RxColLW, RxRowH);

            // 행 강화 버튼 — 누른 자리에서 결과(레벨·게이지)가 바로 갱신된다.
            _slotBtn[i] = MakeStyledButton(row.transform, "RowEnhance", "강화", out _slotBtnLbl[i]);
            PlaceIn((RectTransform)_slotBtn[i].transform, 334f, 24f, 94f, 60f, RxColLW, RxRowH);
            int bi = i;
            _slotBtn[i].onClick.AddListener(() => EnhancePartRow(bi));

            // 잠금·미해금 문구용 중앙 텍스트. 평시엔 꺼둔다.
            _partsMark[i] = ShopUIStyle.MakeText(row.transform, "Lock", 15f, FontStyles.Normal,
                                                 TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            ShopUIStyle.Stretch(_partsMark[i].rectTransform);
            _partsMark[i].gameObject.SetActive(false);

            int idx = i;
            var btn = row.gameObject.AddComponent<Button>();
            ShopUIStyle.ApplyButtonColors(btn, row);
            btn.onClick.AddListener(() => SelectPartSlot(idx));

            // 호버 미리보기 — 손만 올려도 상세가 그 파츠를 보여준다. 클릭은 대상 고정 전용.
            var hover = row.gameObject.AddComponent<EventTrigger>();
            AddTrigger(hover, EventTriggerType.PointerEnter, () => { _hoverPartSlot = idx; RefreshRangedCard(); });
            AddTrigger(hover, EventTriggerType.PointerExit,  () => { _hoverPartSlot = -1;  RefreshRangedCard(); });
        }

        RefreshRangedParts();
    }

    private static void AddTrigger(EventTrigger t, EventTriggerType type, System.Action fn)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(_ => fn());
        t.triggers.Add(e);
    }

    // ── 투자 진척 · 파츠 지급 ───────────────────────────────

    /// <summary>마우스가 올라간 슬롯. -1이면 없음. 선택(_selectedPartSlot)과 별개로 상세만 미리 보여준다.</summary>
    private int _hoverPartSlot = -1;



    /// <summary>강화 대상으로 고른 파츠 행. -1이면 미선택.</summary>
    private int _selectedPartSlot = -1;

    /// <summary>
    /// 파츠 행 클릭 — 강화 대상 선택 전용(다시 누르면 해제). 강화는 행의 버튼이 한다.
    /// index는 파츠 정의 목록의 인덱스다(슬롯이 아니다).
    /// </summary>
    private void SelectPartSlot(int index)
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || index < 0 || index >= all.Count) return;

        if (_selectedPartSlot == index)
        {
            _selectedPartSlot = -1;
            RefreshRangedParts();
            RefreshRangedCard();
            return;
        }

        _selectedPartSlot = index;
        RefreshRangedParts();
        RefreshRangedCard();
    }

    /// <summary>행 버튼 — 그 행을 대상으로 고정하고 곧바로 강화한다(연출·큐는 기존 경로 재사용).</summary>
    private void EnhancePartRow(int index)
    {
        _selectedPartSlot = index;
        RefreshRangedCard();
        EnhanceSelectedPart();
    }

    /// <summary>파츠 탭 안내 문구. 전용 슬롯이 없어 NPC 대사창을 그대로 쓴다(정보가 한 곳에 모임).</summary>
    private void SetPartHint(string msg)
    {
        if (_dialogText != null) _dialogText.text = msg;
    }

    /// <summary>5행을 파츠 정의 목록으로 채운다. 파츠 탭을 열거나 강화한 뒤 호출.</summary>
    private void RefreshRangedParts()
    {
        var state = RangedPartsState.Current;
        var all   = Managers.WeaponParts?.All;
        int have  = _controller != null ? _controller.FuelAmount : 0;

        if (_slotSummary != null && state != null)
            _slotSummary.text = $"켠 파츠 <color=#FFD24A>{state.ActiveCount}</color> / {(all?.Count ?? 0)}"
                              + $"   ·   보유 재료 <color=#FFD24A>{have}</color>";

        for (int i = 0; i < PartRows; i++)
        {
            if (_partsSlots[i] == null) continue;

            var def = all != null && i < all.Count ? all[i] : null;
            bool exists = def != null;

            SetSlotDetailVisible(i, exists);
            _partsMark[i].gameObject.SetActive(!exists);
            if (_slotBtn[i] != null) _slotBtn[i].gameObject.SetActive(exists);

            if (!exists)
            {
                // 정의가 없는 남는 행 — 데이터가 5종보다 적을 때만 보인다.
                _partsMark[i].text   = "";
                _partsSlots[i].color = new Color(0.09f, 0.07f, 0.11f, 1f);
                continue;
            }

            int level = state != null ? state.LevelOf(def.part_id) : 0;
            int max   = def.max_level > 0 ? def.max_level : Mathf.Max(1, level);
            bool on   = level > 0;
            bool maxed = def.max_level > 0 && level >= def.max_level;

            // 꺼짐(Lv0)도 목록에 그대로 남긴다 — 없는 것과 안 켠 것은 다르다.
            _slotKind[i].text  = on ? "◆" : "◇";
            _slotKind[i].color = on ? ShopUIStyle.Gold : ShopUIStyle.TextDim;
            _slotName[i].text  = def.part_name;
            // 효과가 한눈에 — 꺼진 파츠는 켜면 얻는 것, 켠 파츠는 지금 → 다음 강화(09-29 사용자 「효과를 알 수 있는 디자인」).
            _slotVal[i].text   = !on   ? $"<color=#C9A66A>켜면</color> {PartValueText(def, def.ValueAt(1))}"
                               : maxed ? PartValueText(def, def.ValueAt(level))
                                       : $"{PartValueText(def, def.ValueAt(level))} <color=#7AD46E>→ {PartValueText(def, def.ValueAt(level + 1))}</color>";
            _slotLv[i].text    = on ? $"Lv.{level}" : "—";
            SetBar(_slotBar[i], max > 0 ? (float)level / max : 0f);

            int cost = _controller != null ? _controller.PartCostAt(def.part_id) : 0;
            bool afford = have >= cost;
            // 버튼 = 행동 + 비용 두 줄. 「◆4」는 무엇을 쓰는지 안 읽혔다 — 재료 N, 모자라면 붉게(09-29).
            if (_slotBtnLbl[i] != null)
                _slotBtnLbl[i].text = maxed ? "최대"
                    : $"{(on ? "강화" : "켜기")}\n<size=76%><color={(afford ? "#E8D9B0" : "#FF7A6A")}>재료 {cost}</color></size>";
            if (_slotBtn[i] != null)
            {
                _slotBtn[i].interactable = !maxed && afford && !_animating;
                UIAffordGlow.Set(_slotBtn[i], _slotBtn[i].interactable);   // 누를 수 있을 때만 은은한 불
            }

            bool sel = (i == _selectedPartSlot);
            if (_partRowArt)
            {
                // 아트 행 — 바탕은 밝기로, 테두리는 선택(금빛) · 켬(흰) · 꺼짐(옅게)
                _partsSlots[i].color = sel ? Color.white : on ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f);
                if (_partsFrames[i] != null)
                    _partsFrames[i].color = sel ? new Color(1f, 0.84f, 0.45f, 1f)
                                          : on  ? new Color(1f, 1f, 1f, 0.70f) : new Color(1f, 1f, 1f, 0.38f);
            }
            else
                _partsSlots[i].color = sel  ? new Color(0.18f, 0.14f, 0.09f, 1f)
                                     : on   ? new Color(0.12f, 0.10f, 0.15f, 1f)
                                            : new Color(0.09f, 0.07f, 0.11f, 1f);
            _slotName[i].color   = sel ? ShopUIStyle.Gold
                                 : on  ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
        }
    }

    private void SetSlotDetailVisible(int i, bool on)
    {
        if (_slotKind[i] != null) _slotKind[i].gameObject.SetActive(on);
        if (_slotVal[i]  != null) _slotVal[i].gameObject.SetActive(on);
        if (_slotName[i] != null) _slotName[i].gameObject.SetActive(on);
        if (_slotLv[i]   != null) _slotLv[i].gameObject.SetActive(on);
        if (_slotBar[i]  != null) _slotBar[i].parent.gameObject.SetActive(on);
    }

    private static void SetBar(RectTransform fill, float ratio01)
    {
        if (fill == null) return;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio01), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }

    /// <summary>카드용 짧은 종류 이름(카드 폭이 좁아 설명 부분은 뺀다).</summary>
    private static string PartKindShort(WeaponPartEntry def) => def.Kind switch
    {
        RangedPartKind.Split   => "분열",
        RangedPartKind.Pierce  => "관통",
        RangedPartKind.Explode => "폭발",
        RangedPartKind.Homing  => "유도",
        _                      => "거력",
    };

    /// <summary>
    /// 우측 강화 정보 — 위계: 성공률 한 숫자(72px) → 표 5줄(이름 | 값) → 연속·이벤트 → 직전 결과 → [강화하기 · 비용] → [진화].
    /// 탭마다 표의 이름이 바뀐다(무기: 필요 재료·성공 시… / 파츠: 확산·관통…) — 값 칸은 같은 필드를 쓴다.
    /// </summary>
    private void BuildInfoColumn(Transform w)
    {
        var panel = ShopUIStyle.MakeImage(w, "InfoPanel", ShopUIStyle.BandFill);
        Place(panel.rectTransform, InfoX, InfoY, InfoW, InfoH);
        ShopUIStyle.Skin(panel, _skin?.infoPanel, sliced: true);
        _infoGroup = panel.gameObject.AddComponent<CanvasGroup>();
        var p = panel.transform;

        _infoTitle = MakeLabel(p, "InfoTitle", 26f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
        _infoTitle.text = "강화 정보";
        PlaceIn(_infoTitle.rectTransform, 0f, 26f, InfoW, 38f, InfoW, InfoH);

        _rateLabel = MakeLabel(p, "RateLabel", 18f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
        _rateLabel.text = "성공률";
        PlaceIn(_rateLabel.rectTransform, 0f, 72f, InfoW, 26f, InfoW, InfoH);

        _successText = ShopUIStyle.MakeText(p, "Rate", 72f, FontStyles.Bold, TextAlignmentOptions.Center, RateGold);
        _successText.richText = true;
        _successText.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_successText.rectTransform, 0f, 98f, InfoW, 86f, InfoW, InfoH);

        var top = ShopUIStyle.MakeImage(p, "RowLineTop", RowLine);
        PlaceIn(top.rectTransform, RowPad, RowY0, RowW, 1f, InfoW, InfoH);
        _rowLabels = new TMP_Text[InfoRows];
        _costText       = InfoRow(p, 0);
        _detailSuccess  = InfoRow(p, 1);
        _detailFail     = InfoRow(p, 2);
        _detailJackpot  = InfoRow(p, 3);
        _milestoneLabel = InfoRow(p, 4);
        SetRowLabels(MeleeRowLabels);

        float y = RowY0 + InfoRows * RowH + 10f;
        _streakText = MakeLabel(p, "Streak", 16f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
        PlaceIn(_streakText.rectTransform, RowPad, y, RowW, 26f, InfoW, InfoH);
        _eventEffectText = MakeLabel(p, "EventEffect", 16f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, ShopUIStyle.Gold);
        PlaceIn(_eventEffectText.rectTransform, RowPad, y, RowW, 26f, InfoW, InfoH);

        _resultText = MakeLabel(p, "Result", 18f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        _resultText.richText = true;
        PlaceIn(_resultText.rectTransform, RowPad, y + 30f, RowW, 28f, InfoW, InfoH);
    }

    /// <summary>정보창 제목 — 탭에 따라 「강화 정보」 / 「발사 미리보기」.</summary>
    [SerializeField] private TMP_Text _infoTitle;

    // 표 격자 — 정보창(568×748) 기준. 행 52px(값은 두 줄까지 접힌다), 이름 칸 132px.
    private const int   InfoRows  = 5;
    private const float RowPad    = 38f, RowW = 492f, RowY0 = 196f, RowH = 52f, RowLabelW = 132f;
    // 무기 탭은 3줄만(09-29 사용자 「방해 요소가 많다」) — 성공 시는 무대 공격 줄, 다음 목표는 게이지 눈금이 말한다.
    private static readonly string[] MeleeRowLabels  = { "필요 재료", "실패 시", "잭팟", "", "" };
    private static readonly string[] MaxedRowLabels  = { "상태", "다음", "마스터리", "", "" };
    private const int WeaponRowCount = 3;
    private static readonly string[] RangedRowLabels = { "확산", "관통", "폭발", "유도", "배율" };

    /// <summary>표 한 줄 — 왼쪽 이름(탭별로 바뀐다) · 오른쪽 값 · 아래 구분선.</summary>
    private TMP_Text InfoRow(Transform p, int i)
    {
        float y = RowY0 + i * RowH;
        _rowLabels[i] = MakeLabel(p, $"RowLabel{i}", 18f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
        PlaceIn(_rowLabels[i].rectTransform, RowPad, y, RowLabelW, RowH, InfoW, InfoH);

        var t = MakeLabel(p, $"Row{i}", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, ShopUIStyle.TextPrimary);
        t.richText = true;
        PlaceIn(t.rectTransform, RowPad + RowLabelW, y, RowW - RowLabelW, RowH, InfoW, InfoH);

        var line = ShopUIStyle.MakeImage(p, $"RowLine{i}", RowLine);
        PlaceIn(line.rectTransform, RowPad, y + RowH - 1f, RowW, 1f, InfoW, InfoH);
        return t;
    }

    private void SetRowLabels(string[] labels)
    {
        if (_rowLabels == null) return;
        for (int i = 0; i < _rowLabels.Length && i < labels.Length; i++)
            if (_rowLabels[i] != null) _rowLabels[i].text = labels[i];
    }

    private Transform[] _rowLines;

    /// <summary>표에서 앞의 n줄만 보인다(이름 · 값 · 구분선). 무기 탭 3줄, 파츠 탭 5줄.</summary>
    private void SetRowCount(int n)
    {
        if (_rowLines == null)
        {
            _rowLines = new Transform[InfoRows];
            for (int i = 0; i < InfoRows; i++) _rowLines[i] = FindDeep($"RowLine{i}");
        }
        for (int i = 0; i < InfoRows; i++)
        {
            bool on = i < n;
            if (_rowLabels != null && i < _rowLabels.Length && _rowLabels[i] != null) _rowLabels[i].gameObject.SetActive(on);
            var val = RowValue(i);
            if (val != null) val.gameObject.SetActive(on);
            if (_rowLines[i] != null) _rowLines[i].gameObject.SetActive(on);
        }
        // 결과 문구는 표 바로 아래 — 3줄로 줄자 버튼 위 빈 칸 한가운데 떴다. 파츠 탭(5줄)은 연속·이벤트 줄 밑 그대로.
        if (_resultText != null)
            PlaceIn(_resultText.rectTransform, RowPad, n >= InfoRows ? RowY0 + InfoRows * RowH + 40f : RowY0 + n * RowH + 18f,
                    RowW, 28f, InfoW, InfoH);
    }

    private TMP_Text RowValue(int i) => i switch
    {
        0 => _costText,
        1 => _detailSuccess,
        2 => _detailFail,
        3 => _detailJackpot,
        _ => _milestoneLabel,
    };

    private TMP_Text _rateBreak;

    /// <summary>
    /// 성공률 아래 한 줄 — 무엇이 확률을 움직였는지(기본 · 인장 · 화로 이벤트). 구운 프리팹에 없어 처음 쓸 때 만든다.
    /// 사용자(09-29) 「강화 확률 증가 내용과 이벤트 강화 확률 같은 건 즉시 피드백」.
    /// </summary>
    private TMP_Text EnsureRateBreak()
    {
        if (_rateBreak != null) return _rateBreak;
        var panel = _infoGroup != null ? _infoGroup.transform : FindDeep("InfoPanel");
        if (panel == null) return null;
        var found = panel.Find("RateBreak");
        _rateBreak = found != null ? found.GetComponent<TMP_Text>()
                                   : MakeLabel(panel, "RateBreak", 17f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
        _rateBreak.richText         = true;
        _rateBreak.textWrappingMode = TextWrappingModes.NoWrap;
        // 큰 숫자(72px · 86 높이)를 조금 줄여 올리고 그 아래에 한 줄 — 표 첫 줄(196)과 겹치지 않게.
        if (_successText != null)
        {
            _successText.fontSize = 64f;
            PlaceIn(_successText.rectTransform, 0f, 94f, InfoW, 74f, InfoW, InfoH);
        }
        PlaceIn(_rateBreak.rectTransform, 0f, 168f, InfoW, 24f, InfoW, InfoH);
        TMPOutlineHelper.ApplySoftShadow(_rateBreak);
        return _rateBreak;
    }

    /// <summary>「기본 55% · ▲ 인장 +8%p · ▲ 화로 +15%p」 — 보정이 하나도 없으면 빈 줄.</summary>
    private string RateBreakText(int slot)
    {
        float sig = CrucibleRoomController.SigilBonus;
        float ev  = _controller.EventSuccessBonus;
        bool  cut = _controller.IsDiscountEvent;
        if (sig <= 0f && Mathf.Approximately(ev, 0f) && !cut) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("<color=#9A93A6>기본 ").Append((_controller.BaseSuccessChanceAt(slot) * 100f).ToString("F0")).Append("%</color>");
        if (sig > 0f) sb.Append("    <color=#E8C07A>▲ 인장 +").Append((sig * 100f).ToString("F0")).Append("%p</color>");
        if (ev > 0f)  sb.Append("    <color=#FFA35A><b>▲ 화로 +").Append((ev * 100f).ToString("F0")).Append("%p</b></color>");
        if (ev < 0f)  sb.Append("    <color=#FF6A5A><b>▼ 저주 ").Append((ev * 100f).ToString("F0")).Append("%p</b></color>");
        if (cut)      sb.Append("    <color=#7FD8A0><b>▼ 재료 반값</b></color>");
        return sb.ToString();
    }

    /// <summary>
    /// 강화하기(비용 포함) · 진화 — 정보창 아래쪽. 판단(성공률·표)을 읽은 자리에서 바로 누르게 한다.
    /// 강화하기 아트 629×172 → 470×128. 아트에 「강화하기」가 구워져 있어 코드 라벨은 끄고, 비용만 글자 오른쪽 빈자리에 얹는다.
    /// </summary>
    private void BuildActionsColumn(Transform w)
    {
        var p = _infoGroup.transform;

        _enhanceBtn = MakeStyledButton(p, "Enhance", "강화하기", out _enhanceLabel);
        PlaceIn((RectTransform)_enhanceBtn.transform, (InfoW - 470f) * 0.5f, 536f, 470f, 128f, InfoW, InfoH);
        SkinButton(_enhanceBtn, _skin?.enhanceButton, _enhanceLabel);
        _enhanceBtn.onClick.AddListener(OnEnhanceClicked);

        // 비용 — 아트의 글자는 폭의 72%까지 차 있다(실측). 그 오른쪽 빈자리에 재료 아이콘 + 숫자.
        // 아이콘은 숫자의 자식이다 — 비용이 없을 때(최대 강화) 아이콘만 덩그러니 남지 않게 통째로 감춘다.
        _enhanceCost = ShopUIStyle.MakeText(_enhanceBtn.transform, "Cost", 24f, FontStyles.Bold,
                                            TextAlignmentOptions.MidlineLeft, CostInk);
        _enhanceCost.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(_enhanceCost.rectTransform, 384f, 38f, 64f, 52f, 470f, 128f);
        var costIcon = ShopUIStyle.MakeImage(_enhanceCost.transform, "CostIcon", MatTint);
        PlaceIn(costIcon.rectTransform, -32f, 8f, 28f, 36f, 64f, 52f);
        costIcon.preserveAspect = true;
        ShopUIStyle.Skin(costIcon, _skin?.MaterialIcon);

        // 진화 — 진화가 있는 무기면 늘 보인다. 조건(최대 강화)을 채우기 전엔 잠겨서 "어디서 열리는지"를 말한다.
        _evolveBtn = MakeStyledButton(p, "Evolve", "◆ 진화", out _evolveLabel);
        PlaceIn((RectTransform)_evolveBtn.transform, (InfoW - 470f) * 0.5f, 674f, 470f, 52f, InfoW, InfoH);
        _evolveBtn.onClick.AddListener(ShowEvolvePanel);
        _evolveBtn.gameObject.SetActive(false);
    }

    /// <summary>
    /// 하단 대사 밴드 — 이 화면의 주역(의뢰서). 초상 · 이름 · 큰 흰 대사(27px) · 오른쪽 배지(돌발 이벤트 / 위험 구간).
    /// 결과에 대장장이가 반응하는 곳이 여기라, 강화 연출이 끝나는 자리도 여기다.
    /// </summary>
    private void BuildDialogueBand(Transform w)
    {
        var band = ShopUIStyle.MakeImage(w, "DialogueBand", ShopUIStyle.BandFill);
        Place(band.rectTransform, LogX, LogY, LogW, LogH);
        ShopUIStyle.Skin(band, _skin?.dialogueBand, sliced: true);   // 신규 「대장장이칸」
        _bandGroup = band.gameObject.AddComponent<CanvasGroup>();
        var b = band.transform;

        // 초상 — 대장장이 초상 아트가 없어 모루로 자리를 지킨다. 아트가 오면 Portrait의 Art 스프라이트만 바꾼다.
        var por = ShopUIStyle.MakeFrame(b, "Portrait", BronzeEdge, PortraitInk, 2f);
        PlaceIn((RectTransform)por.transform.parent, 26f, 10f, 88f, 88f, LogW, LogH);
        var porArt = ShopUIStyle.MakeImage(por.transform, "Art", ShopUIStyle.Gold);
        var mid = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(porArt.rectTransform, mid, mid, mid, Vector2.zero, new Vector2(62f, 40f));
        porArt.preserveAspect = true;
        ShopUIStyle.Skin(porArt, _skin?.anvilIcon);

        var who = MakeLabel(b, "Speaker", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);
        who.text = "대장장이";
        who.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceIn(who.rectTransform, 134f, 0f, 104f, LogH, LogW, LogH);

        _dialogText = ShopUIStyle.MakeText(b, "Dialog", 27f, FontStyles.Normal,
                                           TextAlignmentOptions.MidlineLeft, Color.white);
        _dialogText.fontSizeMin = 20f;
        PlaceIn(_dialogText.rectTransform, 248f, 8f, 970f, LogH - 16f, LogW, LogH);
        _dialogText.text = "쇠는 두드릴수록 강해지지… 운이 따라준다면 말이야.";

        BuildEventBanner(b);
    }

    // ── 배지(대사 밴드 오른쪽) — 돌발 이벤트가 있으면 그 문구, 없으면 무기 탭의 도박 구간 경고 ──
    private void BuildEventBanner(Transform band)
    {
        var fill = ShopUIStyle.MakeFrame(band, "EventBanner", EventEdge, EventFill, 1.5f);
        var brt  = (RectTransform)fill.transform.parent;
        PlaceIn(brt, 1236f, 26f, 292f, 56f, LogW, LogH);
        _eventBanner = brt.gameObject;
        _eventBannerText = MakeLabel(fill.transform, "Text", 16f, FontStyles.Bold, TextAlignmentOptions.Center, EventInk);
        ShopUIStyle.Stretch(_eventBannerText.rectTransform, 6f);
        _eventBanner.SetActive(false);
    }

    // ── 진화 선택 패널 (선택 연출 오버레이) ──────────────────
    private void BuildEvolvePanel(Transform w)
    {
        var dim = ShopUIStyle.MakeImage(w, "EvolveDim", new Color(0.02f, 0.02f, 0.04f, 0.90f), raycast: true);
        ShopUIStyle.Stretch(dim.rectTransform);
        _evolvePanel = dim.gameObject;
        var d = dim.transform;

        var title = ShopUIStyle.MakeText(d, "EvolveTitle", 28f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        title.text = "무기가 형태를 선택한다";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -120), new Vector2(760, 48));

        var sub = ShopUIStyle.MakeText(d, "EvolveSub", 15f, FontStyles.Italic,
                                       TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        sub.text = "◇ 되돌릴 수 없는 선택 ◇";
        ShopUIStyle.Anchor(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -172), new Vector2(760, 28));

        _branchABtn = BuildBranchCard(d, -200f, out _branchAName);
        _branchBBtn = BuildBranchCard(d,  200f, out _branchBName);
        _branchABtn.onClick.AddListener(() => OnBranchClicked(0));
        _branchBBtn.onClick.AddListener(() => OnBranchClicked(1));

        var cancel = MakeStyledButton(d, "EvolveCancel", "취소", out _);
        ShopUIStyle.Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 56), new Vector2(200, 50));
        cancel.onClick.AddListener(HideEvolvePanel);

        _evolvePanel.SetActive(false);
    }

    private Button BuildBranchCard(Transform d, float xCenter, out TMP_Text nameText)
    {
        var card = ShopUIStyle.MakeFrame(d, "Branch", ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: true);
        var rt = (RectTransform)card.transform.parent;
        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(xCenter, 10f), new Vector2(340, 300));
        var c = card.transform;

        nameText = ShopUIStyle.MakeText(c, "Name", 24f, FontStyles.Bold,
                                        TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -110), new Vector2(-24, 70));

        var hint = ShopUIStyle.MakeText(c, "Hint", 14f, FontStyles.Normal,
                                        TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        hint.text = "클릭하여 이 형태로 진화";
        ShopUIStyle.Anchor(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 30), new Vector2(-24, 24));

        var pick = card.transform.parent.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(pick);
        return pick;
    }

    /// <summary>현재 장착 무기의 진화 분기(WeaponEvolutionSO). 진화 불가면 빈 목록.</summary>
    private IReadOnlyList<WeaponEvolutionSO.Branch> CurrentBranches
    {
        get
        {
            var wd = _controller?.Run?.Player?.WeaponManager?.CurrentWeaponData;
            return wd?.evolution != null ? wd.evolution.Branches : System.Array.Empty<WeaponEvolutionSO.Branch>();
        }
    }

    private void ShowEvolvePanel()
    {
        if (_evolvePanel == null || _controller == null) return;

        var wd       = _controller.Run?.Player?.WeaponManager?.CurrentWeaponData;
        var branches = CurrentBranches;

        if (branches.Count == 0)
        {
            if (IsPromoteRoute(_controller.GetSlot(PlayerWeaponManager.Slot0))) { ShowLegendPanel(); return; }
            if (_resultText != null) _resultText.text = "<color=#9A8FB5>이 무기는 더 진화하지 않는다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        int lv = wd?.enhanceLevel ?? 0;
        SetBranchLabel(_branchAName, branches.Count > 0 ? branches[0] : null, lv);
        SetBranchLabel(_branchBName, branches.Count > 1 ? branches[1] : null, lv);

        _evolvePanel.SetActive(true);
        ShopUIStyle.PlaySfx("shop_open");
    }

    /// <summary>분기 라벨 — 진화 후 이름 + 잠금 조건(강화 레벨 미달 시 회색 안내).</summary>
    private static void SetBranchLabel(TMP_Text label, WeaponEvolutionSO.Branch b, int enhanceLevel)
    {
        if (label == null) return;
        if (b == null || !b.IsValid) { label.text = "—"; return; }

        string name = b.target != null && !string.IsNullOrEmpty(b.target.displayName)
            ? b.target.displayName : b.branchId;

        if (!WeaponEvolutionSO.IsUnlocked(b, enhanceLevel))
            label.text = $"<color=#7A7290>{name}\n<size=70%>강화 {b.requiredEnhanceLevel} 필요</size></color>";
        else
            label.text = $"{name}\n<size=70%>{(b.cost > 0 ? $"재료 {b.cost}" : "진화 가능")}</size>";
    }

    private void HideEvolvePanel()
    {
        if (_evolvePanel != null) _evolvePanel.SetActive(false);
    }

    private void OnBranchClicked(int branch) => EvolveAsync(branch).Forget();

    /// <summary>진화 실행 — 무기를 통째로 교체하고 강화 레벨은 계승된다(PlayerWeaponManager가 처리).</summary>
    private async UniTaskVoid EvolveAsync(int branchIndex)
    {
        var branches = CurrentBranches;
        if (branchIndex < 0 || branchIndex >= branches.Count)
        {
            HideEvolvePanel();
            if (_resultText != null) _resultText.text = "<color=#C7554A>진화 분기가 없다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        var b  = branches[branchIndex];
        var wm = _controller?.Run?.Player?.WeaponManager;
        var wd = wm?.CurrentWeaponData;
        if (wm == null || wd == null) { HideEvolvePanel(); return; }

        // 조건: 강화 레벨
        // 거절 사유는 진화 패널이 <b>덮고 있는</b> 정보창에 뜬다 — 패널을 닫아야 그 줄이 보인다
        // (성공 경로와 같은 처리).
        if (!WeaponEvolutionSO.IsUnlocked(b, wd.enhanceLevel))
        {
            HideEvolvePanel();
            if (_resultText != null)
                _resultText.text = $"<color=#C7554A>강화 {b.requiredEnhanceLevel} 이상이어야 한다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        // 조건: 재료(설정된 경우만 차감)
        var fuel = _controller.Run?.FuelBank;
        if (b.cost > 0 && !(fuel?.TrySpend(FuelKind.EnhanceMaterial, b.cost) ?? false))
        {
            HideEvolvePanel();
            if (_resultText != null) _resultText.text = "<color=#C7554A>재료가 부족하다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        HideEvolvePanel();

        bool ok;
        try { ok = await wm.EvolveCurrentWeaponAsync(b, this.GetCancellationTokenOnDestroy()); }
        catch (System.OperationCanceledException) { return; }

        if (ok)
        {
            string name = b.target != null ? b.target.displayName : b.branchId;
            if (_resultText != null) _resultText.text = $"<color=#9D7EE6>진화 — {name}</color>";
            ShopUIStyle.PlaySfx("enhance_success");
        }
        else
        {
            // 실패 시 재료 환불(차감했다면)
            if (b.cost > 0) fuel?.Add(FuelKind.EnhanceMaterial, b.cost);
            if (_resultText != null) _resultText.text = "<color=#C7554A>진화에 실패했다</color>";
            ShopUIStyle.PlaySfx("shop_reject");
        }

        RefreshAll();
        if (ok) TryShowEngraveOffer();   // 진화한 무기의 스킬에 고를 거리가 생길 수 있다
    }

    // ── 스킬 각인 (기획 스킬구성_재련소연결 A안 · 09-26 시범: 환영베기 2단계) ──

    /// <summary>
    /// 아직 안 고른 각인이 있으면 둘 중 하나를 고르는 화면을 띄운다(근접 → 원거리 순). 건너뛰기는 없다 —
    /// 창을 닫고 나가도 고를 거리는 무기 상태로 남아 다음에 재련소를 열 때 다시 묻는다.
    /// 고르기 전엔 행동 SO가 각인 없는 예전 분기로 돈다.
    /// </summary>
    private bool TryShowEngraveOffer()
    {
        if (_controller == null || (_engravePanel != null && _engravePanel.activeSelf)) return false;
        for (int i = PlayerWeaponManager.Slot0; i <= PlayerWeaponManager.Slot1; i++)
        {
            var w = _controller.GetSlot(i);
            if (!SkillEngravingService.TryGetPendingOffer(w, _engraveOptions, out var skill, out int tier)) continue;

            EnsureEngravePanel();
            _engraveWeapon = w;
            _engraveSkill  = skill;
            string skillName = string.IsNullOrEmpty(skill.skillName) ? skill.name : skill.skillName;
            _engraveTitle.text = $"{skillName} — {tier}단계 각인";
            // 기획은 「둘 중 하나」 — 같은 단계에 셋 이상 넣으면 앞의 둘만 보인다.
            for (int k = 0; k < 2; k++)
            {
                _engraveName[k].text = _engraveOptions[k].displayName;
                _engraveDesc[k].text = _engraveOptions[k].description;
            }
            _engravePanel.transform.SetAsLastSibling();
            _engravePanel.SetActive(true);
            ShopUIStyle.PlaySfx("shop_open");
            return true;
        }
        return false;
    }

    private void OnEngravePicked(int k)
    {
        if (_engraveWeapon == null || _engraveSkill == null || k >= _engraveOptions.Count) return;
        var def = _engraveOptions[k];
        bool ok = SkillEngravingService.Choose(_engraveWeapon, _engraveSkill, def.id);
        _engravePanel.SetActive(false);
        _engraveWeapon = null;
        _engraveSkill  = null;
        if (ok)
        {
            RunFlowController.Active?.SaveNow("crucible-engrave");
            if (_resultText != null) _resultText.text = $"<color=#9D7EE6>각인 — {def.displayName}</color>";
            ShopUIStyle.PlaySfx("enhance_success");
        }
        RefreshAll();
        TryShowEngraveOffer();   // E·R이 함께 열렸으면 이어서 묻는다
    }

    /// <summary>진화 선택 화면과 같은 틀(창 전체 암막 + 제목 + 카드 두 장). 취소 버튼은 없다.</summary>
    private void EnsureEngravePanel()
    {
        if (_engravePanel != null) return;
        var host = _window != null ? (Transform)_window : transform;
        var dim = ShopUIStyle.MakeImage(host, "EngraveDim", new Color(0.02f, 0.02f, 0.04f, 0.90f), raycast: true);
        ShopUIStyle.Stretch(dim.rectTransform);
        _engravePanel = dim.gameObject;
        var d = dim.transform;

        _engraveTitle = MakeLabel(d, "EngraveTitle", 30f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(_engraveTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -150), new Vector2(900, 50));

        var sub = MakeLabel(d, "EngraveSub", 18f, FontStyles.Italic, TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        sub.text = "◇ 둘 중 하나를 새긴다 · 되돌릴 수 없다 ◇";
        ShopUIStyle.Anchor(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -206), new Vector2(900, 30));

        for (int k = 0; k < 2; k++)
        {
            int captured = k;
            var pick = BuildEngraveCard(d, k == 0 ? -230f : 230f, out _engraveName[k], out _engraveDesc[k]);
            pick.onClick.AddListener(() => OnEngravePicked(captured));
        }
        _engravePanel.SetActive(false);
    }

    private static Button BuildEngraveCard(Transform d, float xCenter, out TMP_Text nameText, out TMP_Text descText,
                                           string hintText = "눌러서 새긴다")
    {
        var card = ShopUIStyle.MakeFrame(d, "Engrave", ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 3f, raycast: true);
        var rt = (RectTransform)card.transform.parent;
        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(xCenter, -20f), new Vector2(400, 320));
        var c = card.transform;

        nameText = MakeLabel(c, "Name", 28f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
        ShopUIStyle.Anchor(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -40), new Vector2(-32, 44));

        descText = MakeLabel(c, "Desc", 20f, FontStyles.Normal, TextAlignmentOptions.Top, ShopUIStyle.TextPrimary);
        descText.textWrappingMode = TextWrappingModes.Normal;
        ShopUIStyle.Anchor(descText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -104), new Vector2(-56, 150));

        var hint = MakeLabel(c, "Hint", 16f, FontStyles.Normal, TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        hint.text = hintText;
        ShopUIStyle.Anchor(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 26), new Vector2(-32, 24));

        var pick = card.transform.parent.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(pick);
        return pick;
    }

    // ── 전설 승급 (09-27 복원) ──

    /// <summary>진화 분기가 없는(마지막 형태) 검 — 끝에서 전설로 승급한다. 이미 승급했으면 아니다.</summary>
    private bool IsPromoteRoute(WeaponData w) =>
        w != null && string.IsNullOrEmpty(w.legendId) && CurrentBranches.Count == 0
        && (w.weaponType == WeaponType.Katana || w.weaponType == WeaponType.Greatsword);

    /// <summary>
    /// 전설 택1. 이 무기에 맞는 전설만 보이고(타입 필터), 기억의 제단이 아직 안 연 전설은 잠긴 칸에 이름과 해금처.
    /// 재료가 모자라면 값을 붉게 — 눌러도 거절 문구가 결과 칸에 뜬다.
    /// </summary>
    private void ShowLegendPanel()
    {
        var w = _controller?.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null || !_controller.CanPromote(PlayerWeaponManager.Slot0)) return;
        if (_engravePanel != null && _engravePanel.activeSelf) return;   // 각인 택1이 먼저다(강제 선택)
        EnsureLegendPanel();
        foreach (var g in _legendCards) if (g != null) Destroy(g);
        _legendCards.Clear();

        var all     = _controller.Table != null ? _controller.Table.Legends : System.Array.Empty<EnhanceTableSO.LegendDef>();
        var allowed = _controller.Legends;
        var fit = new List<(EnhanceTableSO.LegendDef def, bool open)>();
        foreach (var legend in all)
        {
            if (legend.weaponFilter != WeaponType.None && legend.weaponFilter != w.weaponType) continue;
            bool open = System.Array.Exists(allowed, a => a.legendId == legend.legendId);
            fit.Add((legend, open));
        }
        if (fit.Count == 0) return;

        const float CardW = 400f, Gap = 40f;
        float startX = -(fit.Count * CardW + (fit.Count - 1) * Gap) * 0.5f + CardW * 0.5f;
        int have = _controller.FuelAmount;
        var node = MemoryAltarCatalog.Get(MemoryAltarCatalog.WeaponEvolve);
        var d = _legendPanel.transform;
        for (int i = 0; i < fit.Count; i++)
        {
            var (legend, open) = fit[i];
            float x = startX + i * (CardW + Gap);
            if (!open)
            {
                var locked = UILockedSlot.Build(d, $"LegendLocked_{legend.legendId}", new Vector2(x, -20f), new Vector2(CardW, 320f),
                    $"기억의 제단 「{node?.DisplayName ?? "전설 무기"}」에서 해금");
                var lt = ShopUIStyle.FindDeep(locked.transform, "LockTitle");
                if (lt != null && lt.TryGetComponent<TMP_Text>(out var ltt)) ltt.text = legend.displayName;
                _legendCards.Add(locked);
                continue;
            }
            string id = legend.legendId;
            bool afford = have >= legend.promoteCost;
            var pick = BuildEngraveCard(d, x, out var nameText, out var descText, "눌러서 승급한다");
            nameText.text = legend.displayName;
            descText.text = $"공격력 +{(legend.attackBonusMult - 1f) * 100f:0}%\n승급 재료 "
                          + $"<color={(afford ? "#E8D9B0" : "#FF7A6E")}>{legend.promoteCost}</color>  (보유 {have})";
            pick.onClick.AddListener(() => { HideLegendPanel(); OnPromoteClicked(id); });
            _legendCards.Add(pick.gameObject);
        }
        _legendPanel.transform.SetAsLastSibling();
        _legendPanel.SetActive(true);
        ShopUIStyle.PlaySfx("shop_open");
    }

    private void HideLegendPanel()
    {
        if (_legendPanel != null) _legendPanel.SetActive(false);
    }

    /// <summary>진화·각인 선택 화면과 같은 틀(창 전체 암막 + 제목). 승급은 선택이라 취소가 있다.</summary>
    private void EnsureLegendPanel()
    {
        if (_legendPanel != null) return;
        var host = _window != null ? (Transform)_window : transform;
        var dim = ShopUIStyle.MakeImage(host, "LegendDim", new Color(0.02f, 0.02f, 0.04f, 0.90f), raycast: true);
        ShopUIStyle.Stretch(dim.rectTransform);
        _legendPanel = dim.gameObject;
        var d = dim.transform;

        var title = MakeLabel(d, "LegendTitle", 30f, FontStyles.Bold, TextAlignmentOptions.Center, ShopUIStyle.Gold);
        title.text = "전설 승급";
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -150), new Vector2(900, 50));
        var sub = MakeLabel(d, "LegendSub", 18f, FontStyles.Italic, TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        sub.text = "◇ 검에 전설의 이름을 새긴다 · 되돌릴 수 없다 ◇";
        ShopUIStyle.Anchor(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                           new Vector2(0, -206), new Vector2(900, 30));

        var cancel = MakeStyledButton(d, "LegendCancel", "취소", out _);
        ShopUIStyle.Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                           new Vector2(0, 56), new Vector2(200, 50));
        cancel.onClick.AddListener(HideLegendPanel);
        _legendPanel.SetActive(false);
    }

    private void BuildLegendButtons()
    {
        foreach (var b in _legendBtns) if (b != null) Destroy(b.gameObject);
        _legendBtns.Clear();
        if (_controller == null || _promoteRow == null) return;

        foreach (var legend in _controller.Legends)
        {
            string legendId = legend.legendId;
            var btn = MakeStyledButton(_promoteRow, $"Legend_{legendId}", $"승급: {legend.displayName}", out _);
            btn.onClick.AddListener(() => OnPromoteClicked(legendId));
            _legendBtns.Add(btn);
        }
    }

    private static Button MakeStyledButton(Transform parent, string name, string label, out TMP_Text labelText)
    {
        var go = ShopUIStyle.MakeRect(parent, name, typeof(Image), typeof(Button));
        var img = go.GetComponent<Image>();
        img.color = ShopUIStyle.BuyFill;
        var btn = go.GetComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, img);
        labelText = ShopUIStyle.MakeText(go.transform, "Label", 18f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        labelText.text = label;
        ShopUIStyle.Stretch(labelText.rectTransform);
        return btn;
    }

    /// <summary>MakeText + 읽기 하한(16px) — 자동 크기가 칸에 맞추느라 16 아래로 내려가지 않게 묶는다.</summary>
    private static TMP_Text MakeLabel(Transform parent, string name, float size, FontStyles style,
                                      TextAlignmentOptions align, Color color)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, style, align, color);
        t.fontSizeMin = Mathf.Min(size, 16f);
        return t;
    }

    /// <summary>
    /// 버튼에 아트를 얹는다. 재련소 버튼 아트(강화하기·전환·나가기·파츠 강화하기)는 <b>글자가 이미 구워져 있어</b>
    /// 아트가 붙는 순간 코드 라벨을 꺼야 한다 — 안 그러면 "강화하가기"처럼 두 벌이 겹쳐 읽힌다.
    /// 아트가 없을 때만 라벨이 남아 색 폴백에서도 무슨 버튼인지 알 수 있다.
    /// </summary>
    private static void SkinButton(Button btn, Sprite art, TMP_Text label = null, bool bakedLabel = true)
    {
        if (btn == null) return;
        if (art == null) return;

        var img = btn.GetComponent<Image>();
        ShopUIStyle.Skin(img, art, sliced: true);
        if (!bakedLabel) return;   // 글자 없는 판 아트 — 라벨을 그대로 둔다
        var lbl = label != null ? label : btn.GetComponentInChildren<TMP_Text>(true);
        if (lbl != null) lbl.gameObject.SetActive(false);
    }

    // ── 핸들러 ──────────────────────────────────────────────

    /// <summary>대상 전환 시 — 성공률 낮으면 재련공이 도발.</summary>
    private void UpdateTargetDialogue()
    {
        if (_controller == null) return;
        if (_controller.CanEnhance(_targetSlot) && _controller.SuccessChanceAt(_targetSlot) < 0.4f)
            _dialogText.text = _controller.GetDialogue(CrucibleMood.Taunt);
    }

    /// <summary>
    /// 연출 중 재입력 처리. 강화는 반복 행위라 매회 연출이 끝날 때까지 잠그면 대기가 곧 불편이 된다 —
    /// 다시 누르면 진행 중 연출을 즉시 완료(<see cref="_skipAnim"/>)시키고 그 동작을 예약해 이어서 실행한다.
    /// 결과·재료 차감은 컨트롤러가 이미 확정한 뒤이고 연출은 표시층 전용이라, 스킵해도 데이터는 그대로다.
    /// </summary>
    private bool QueueWhileAnimating(Action again)
    {
        if (!_animating) return false;
        _skipAnim = true;
        _queued   = again;   // 덮어쓰기 = 예약은 1개 — 연타가 쌓여 나중에 몰아서 터지지 않게
        return true;
    }

    private void OnEnhanceClicked()
    {
        if (_controller == null) return;
        if (QueueWhileAnimating(OnEnhanceClicked)) return;

        // 파츠 탭은 무기가 아니라 '장착된 파츠'를 올린다 — 대상이 다르므로 경로를 가른다.
        if (_activeTab == 1) { EnhanceSelectedPart(); return; }

        // 컨트롤러가 결과를 즉시 확정(OnCrucibleChanged→RefreshAll 동기 발화). 연출은 표시층만 재생.
        var result = _controller.TryEnhance(_targetSlot);
        PlayEnhanceSequence(result, EnhanceView.MeleeWeapon).Forget();
    }

    /// <summary>
    /// 선택한 파츠를 1레벨 올린다. 임계를 넘으면 효과가 계단 상승한다(분열 갈래 +1 등).
    /// 판정은 <b>확정</b>이고 재료만 든다 — 실패 도박은 무기 강화의 축이고, 계단형 파츠에 겹치면
    /// 체감이 탁해진다. 재료 차감·저장은 컨트롤러(<see cref="CrucibleRoomController.TryEnhancePart"/>)가 소유한다.
    /// </summary>
    private void EnhanceSelectedPart()
    {
        if (_controller == null) return;
        // 행 버튼에 직결돼 있어 연출 중에도 클릭이 들어온다 — 근접 강화와 같은 큐를 태운다
        // (연출을 건너뛰고 다음 강화로 이어지되, 예약은 1개만 남아 나중에 몰아서 터지지 않는다).
        if (QueueWhileAnimating(EnhanceSelectedPart)) return;

        string partId = SelectedPartId();
        if (partId == null)
        {
            SetPartHint("강화할 파츠를 먼저 선택하세요");
            ShopUIStyle.PlaySfx("shop_reject");
            return;
        }

        var result = _controller.TryEnhancePart(partId);
        PlayEnhanceSequence(result, EnhanceView.Part).Forget();
    }

    /// <summary>지금 강화 대상으로 선택된 파츠 id. 미선택/무효면 null.</summary>
    private string SelectedPartId()
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || _selectedPartSlot < 0 || _selectedPartSlot >= all.Count) return null;
        return all[_selectedPartSlot].part_id;
    }

    /// <summary>선택 파츠의 정의. 미선택이면 null.</summary>
    private WeaponPartEntry SelectedPartDef()
    {
        string id = SelectedPartId();
        return id == null ? null : Managers.WeaponParts?.GetById(id);
    }

    private void OnPromoteClicked(string legendId)
    {
        if (_controller == null || _animating) return;
        var result = _controller.TryPromote(_targetSlot, legendId);
        ShowPromoteResult(result);
        RefreshAll();
    }

    // ── 도파민 연출 시퀀스 (표시층 전용; 결과/데이터/세이브 불변) ──

    /// <summary>
    /// 한 시퀀스를 공유하는 세 강화 대상. 탭만으로는 갈리지 않는다 —
    /// 원거리 탭에는 <b>파츠</b>와 <b>원거리 무기</b> 두 대상이 함께 있다.
    /// </summary>
    private enum EnhanceView { MeleeWeapon, RangedWeapon, Part }

    /// <summary>
    /// 강화 결과를 비동기 연출로 재생. 연출 종료 후 RefreshAll로 최종 확정.
    /// 무기 강화는 <b>포커스 → 망치 → 임팩트 → 판정</b> 순서다 — 결과는 이미 확정돼 있지만,
    /// 망치가 닿기 전에 단계·공격력·대사가 바뀌면 판정이 새어 나가 한 번의 강화가 "큰 순간"이 되지 못한다.
    /// </summary>
    private async UniTaskVoid PlayEnhanceSequence(EnhanceResult r, EnhanceView view)
    {
        if (r.IsReject)
        {
            ShowResultText(r, view);
            ShowStageResult(_resultText.text, RejectFontSize, RejectPopScale);
            if (r.outcome != EnhanceOutcome.RejectMaxed) ShopUIStyle.PlaySfx("crucible_fail");
            RefreshAll();
            return;
        }

        _animating = true;
        // 직전 판정 도장이 아직 떠 있으면 걷는다 — 다음 망치질 동안 옛 결과가 무대에 남으면 두 판정이 섞여 읽힌다(실측 09-20).
        HideStageResult();

        // 파츠 탭은 1번 카드에 파츠를 그리므로 상한도 파츠 정의에서 온다(무기 강화 상한이 아님).
        bool isPart  = view == EnhanceView.Part;
        bool isMelee = view == EnhanceView.MeleeWeapon;
        int slot = isMelee ? _targetSlot : RangedCard;
        int max  = isPart ? (SelectedPartDef()?.max_level ?? 1)
                          : _controller.MaxAt(isMelee ? PlayerWeaponManager.Slot0 : PlayerWeaponManager.Slot1);
        // 원거리 무기는 자기 레벨 셀이 없다 — 1번 카드는 파츠를 그린다. 남의 셀에 무기 단계를 찍으면
        // 연출 0.5초 동안 파츠가 엉뚱한 레벨로 보인다. 카운트만 생략하고 카드 반응은 그대로 준다.
        bool count = view != EnhanceView.RangedWeapon;

        // 카운트 연출이 레벨 셀을 소유하도록 시작 단계로 되돌림
        // (RefreshAll이 이미 afterLevel로 세팅했으나 다음 렌더 전 동일 프레임에서 덮어씀 → 깜빡임 없음).
        if (count && IsCardSlot(slot)) { _cardLevel[slot].text = FormatLevel(r.beforeLevel, max, isPart); SetGauge(slot, r.beforeLevel, max); }

        if (isMelee) HoldBeforeState(r, max);
        else         ShowResultText(r, view);

        try
        {
            if (isMelee)
            {
                await ForgeStrikeAsync();
                ShowResultText(r, view);   // 망치가 닿는 순간 공개 — 결과 문구·대장장이 대사
            }

            switch (r.outcome)
            {
                case EnhanceOutcome.Success:
                    if (_controller.LastJackpot) await JackpotSequence(slot, r, max, count, isMelee);
                    else                         await SuccessSequence(slot, r, max, count, isMelee);
                    break;
                case EnhanceOutcome.FailDropped:
                    await FailSequence(slot, r, max, count, isMelee);
                    break;
            }
        }
        catch (OperationCanceledException) { return; } // 패널 파괴 — 정적 서비스는 자립적, 정리 불필요

        if (isMelee) { ApplyFocus(0f); HideHammer(); }   // 스킵으로 끊겼어도 포커스·망치는 제자리로
        _animating = false;
        _skipAnim  = false;
        RefreshAll(); // 연출 후 최종 확정

        // 단계에 올라 각인을 고를 차례면 그 화면이 먼저 — 예약된 다음 강화는 버린다(고르기 전에 또 오르면 순서가 꼬인다).
        if (TryShowEngraveOffer()) { _queued = null; return; }

        // 연출 중 눌린 재입력을 여기서 이어 실행한다 — 결과가 확정·반영된 뒤라야 다음 강화가 올바른 값에 걸린다.
        var next = _queued;
        _queued = null;
        next?.Invoke();
    }

    /// <summary>
    /// 망치가 닿기 전까지 무대를 <b>강화 전 모습</b>으로 붙잡는다(단계·게이지는 호출부가 되돌렸다) —
    /// 공격력 · 달굼 · 도박 구간 표시 · 눈금. 잭팟이면 환불 전 잔액을 먼저 보여 준다:
    /// 낸 재료가 빠졌다가 판정 뒤 되돌아오는 게 보여야 "환불"이 읽힌다.
    /// </summary>
    private void HoldBeforeState(EnhanceResult r, int max)
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w != null && _cardAtk[0] != null) _cardAtk[0].SetText(AtkHoldFormat, AttackAt(w, r.beforeLevel));
        _heatLevel01 = max > 0 ? Mathf.Clamp01((float)r.beforeLevel / max) : 0f;

        var table = _controller.Table;
        if (table != null) SetDanger(r.beforeLevel < max && table.DropAt(r.beforeLevel) > 0, pop: false);
        RefreshTicks(w, r.beforeLevel, max);

        if (_controller.LastJackpot)
            CurrencyCounter.Apply(_fuelText, _controller.FuelAmount - _controller.LastRefund);
    }

    private async UniTask SuccessSequence(int slot, EnhanceResult r, int max, bool count, bool melee)
    {
        ShopUIStyle.PlaySfx("crucible_success");
        HitFeelService.HitStop(0.6f, 0.05f); // 시간정지 팝업에선 timeScale 무효(무해) — 카메라측 반응만
        ShowStageResult(_resultText.text, SuccessFontSize, SuccessPopScale);   // 망치가 닿는 순간 = 판정 순간
        if (melee)
        {
            FlashStage(StageFlashSuccess);
            _heatLevel01 = max > 0 ? Mathf.Clamp01((float)r.afterLevel / max) : 0f;
        }
        await UniTask.WhenAll(count ? CountLevel(slot, r.beforeLevel, r.afterLevel, max) : UniTask.CompletedTask,
                              melee ? CountAttackAsync(r.beforeLevel, r.afterLevel) : UniTask.CompletedTask,
                              melee ? FocusAsync(false) : UniTask.CompletedTask,
                              PunchCard(slot, PunchScale, PunchDur),
                              FlashCard(slot, SuccessFlash));
        if (melee) await Hold(SettleDur);
    }

    private async UniTask JackpotSequence(int slot, EnhanceResult r, int max, bool count, bool melee)
    {
        HitFeelService.Heavy();
        if (melee)
        {
            // 판정 멈칫 → 금빛 맥박 3회 → 빛살·배너. 잭팟은 드물다 — 이 한 번은 길게 보여 줘도 된다.
            await Hold(JackpotPauseDur);
            for (int i = 0; i < 3 && !_skipAnim; i++)
            {
                FlashStage(StageFlashJackpot);
                await Hold(JackpotPulseGap);
            }
            _jackpotGen++;
            JackpotShowAsync(_jackpotGen).Forget();
            BurstSparks(JackpotSparks, SparkJackpot, StagePanelW * 0.5f, 300f);
            _heatLevel01 = max > 0 ? Mathf.Clamp01((float)r.afterLevel / max) : 0f;
        }
        ShopUIStyle.PlaySfx("crucible_jackpot");
        VolumePulseService.Pulse(JackpotPulsePeak, JackpotPulseDur); // 전체화면 크로매틱+블룸(unscaled)
        ShowStageResult(_resultText.text, JackpotFontSize, JackpotPopScale);
        await UniTask.WhenAll(count ? CountLevel(slot, r.beforeLevel, r.afterLevel, max) : UniTask.CompletedTask,
                              melee ? CountAttackAsync(r.beforeLevel, r.afterLevel) : UniTask.CompletedTask,
                              melee ? FocusAsync(false) : UniTask.CompletedTask,
                              melee ? RefundAsync() : UniTask.CompletedTask,
                              PunchCard(slot, JackpotPunchScale, JackpotPunchDur),
                              FlashCard(slot, JackpotFlash));
        if (melee) await Hold(SettleDur);
    }

    private async UniTask FailSequence(int slot, EnhanceResult r, int max, bool count, bool melee)
    {
        bool nearMiss = IsNearMiss(r);
        ShopUIStyle.PlaySfx("crucible_fail");
        HitFeelService.Light();
        float amp   = nearMiss ? FailShakeAmp * NearMissShakeMult : FailShakeAmp;
        Color flash = nearMiss ? NearMissFlash : FailFlash;
        // 쉐이크와 같은 프레임에 띄운다 — 흔들림이 곧 판정이라 문구가 늦으면 둘이 따로 논다.
        ShowStageResult(_resultText.text, SuccessFontSize, nearMiss ? JackpotPopScale : SuccessPopScale);
        if (melee)
        {
            FlashStage(nearMiss ? StageFlashNear : StageFlashFail);
            _coolGen++;
            CoolWeaponAsync(_coolGen).Forget();
            _heatLevel01 = max > 0 ? Mathf.Clamp01((float)r.afterLevel / max) : 0f;
        }
        bool dropped = r.beforeLevel != r.afterLevel;   // 하락분이 있으면 카운트다운
        float shake  = nearMiss ? WindowShakeAmp * NearMissShakeMult : WindowShakeAmp;
        await UniTask.WhenAll(count && dropped ? CountLevel(slot, r.beforeLevel, r.afterLevel, max) : UniTask.CompletedTask,
                              melee && dropped ? CountAttackAsync(r.beforeLevel, r.afterLevel) : UniTask.CompletedTask,
                              melee ? FocusAsync(false) : UniTask.CompletedTask,
                              melee ? ShakeWindowAsync(shake) : ShakeCard(slot, amp, FailShakeDur),
                              FlashCard(slot, flash));
        if (melee) await Hold(SettleDur);
    }

    private void ShowResultText(EnhanceResult r, EnhanceView view)
    {
        // 파츠는 확정 상승이라 잭팟·스트릭·니어미스가 없다 — 무기 문구를 그대로 쓰면 있지도 않은 도박을 암시한다.
        if (view == EnhanceView.Part) { ShowPartResultText(r); return; }

        switch (r.outcome)
        {
            case EnhanceOutcome.Success:
                _resultText.text = _controller.LastJackpot
                    ? $"<color=#FFD24A>잭팟! +{_controller.LastRefund} 환불</color>"
                    : $"<color=#7AD46E>성공! +{r.afterLevel}</color>";
                _resultText.color = ShopUIStyle.Gold;
                _dialogText.text = _controller.LastJackpot ? _controller.GetDialogue(CrucibleMood.Jackpot)
                                 : _controller.Streak >= 2 ? _controller.GetDialogue(CrucibleMood.Streak)
                                 : _controller.GetDialogue(CrucibleMood.Success);
                break;
            case EnhanceOutcome.FailDropped:
            {
                // 안전 구간 실패는 단계가 그대로다 — 「하락」이라고 쓰면 틀린 말이 된다.
                string what = r.beforeLevel == r.afterLevel ? "단계 유지" : "하락";
                _resultText.text = IsNearMiss(r) ? $"<color=#FF7A3A>아슬아슬! {what} (+{r.afterLevel})</color>"
                                                 : $"<color=#FF5250>실패 — {what} (+{r.afterLevel})</color>";
                _dialogText.text = _controller.GetDialogue(CrucibleMood.Fail);
                break;
            }
            case EnhanceOutcome.RejectNoFuel:
                _resultText.text = "<color=#FF5250>강화재료 부족</color>";
                break;
            case EnhanceOutcome.RejectMaxed:
                _resultText.text = "<color=#8AB0D5>이미 최대 강화</color>";
                break;
            default:
                _resultText.text = "<color=#FF5250>강화 불가</color>";
                break;
        }
    }

    /// <summary>파츠 강화 결과 문구. 임계를 넘어 효과가 실제로 오른 경우를 따로 짚어준다.</summary>
    private void ShowPartResultText(EnhanceResult r)
    {
        var def = SelectedPartDef();

        switch (r.outcome)
        {
            case EnhanceOutcome.Success:
                bool stepped = def != null &&
                               !Mathf.Approximately(def.ValueAt(r.beforeLevel), def.ValueAt(r.afterLevel));
                _resultText.text = stepped
                    ? $"<color=#FFD24A>단계 상승! {(def != null ? PartValueText(def, def.ValueAt(r.afterLevel)) : "")}</color>"
                    : $"<color=#7AD46E>강화 성공 — Lv.{r.afterLevel}</color>";
                _resultText.color = ShopUIStyle.Gold;
                _dialogText.text = stepped
                    ? "형태가 바뀌었군. 이제 다르게 날아갈 거다."
                    : "아직이다. 조금 더 두들겨야 모양이 잡힌다.";
                break;

            case EnhanceOutcome.RejectNoFuel:
                _resultText.text = "<color=#FF5250>강화재료 부족</color>";
                _dialogText.text = "재료 없이는 손도 못 댄다.";
                break;

            case EnhanceOutcome.RejectMaxed:
                _resultText.text = "<color=#8AB0D5>이미 최대 강화</color>";
                _dialogText.text = "이 파츠는 여기가 끝이다.";
                break;

            default:
                _resultText.text = "<color=#FF5250>강화할 파츠를 선택하세요</color>";
                break;
        }
    }

    /// <summary>실패가 성공확률에 아슬아슬했는지(roll이 chance의 NearMissBand배 이내).</summary>
    private static bool IsNearMiss(EnhanceResult r)
        => r.outcome == EnhanceOutcome.FailDropped
           && r.chance > 0f && r.roll < r.chance * NearMissBand;

    private bool IsCardSlot(int slot) => slot >= 0 && slot < 2 && _cardLevel[slot] != null;

    /// <summary>1번 카드는 원거리 <b>파츠</b>를 그린다(무기가 아님). 강화 단위가 달라 표기도 갈린다.</summary>
    private const int RangedCard = 1;

    private static string FormatLevel(int level, int max, bool isPart = false)
        => isPart
            ? $"Lv.{level} <size=55%><color=#9A98A0>/ {max}</color></size>"
            : $"+{level} <size=55%><color=#9A98A0>/ {max}</color></size>";

    private void SetGauge(int slot, int level, int max)
    {
        if (slot < 0 || slot >= 2 || _cardGaugeFill[slot] == null) return;
        float ratio = max > 0 ? Mathf.Clamp01((float)level / max) : 0f;
        // 무기 게이지는 Filled — 불꽃 아트를 늘리지 않고 잘라 보인다.
        if (slot == 0 && _gaugeFillImg != null)
        {
            _gaugeFillImg.fillAmount = ratio;
            if (_gaugeGhost != null)
            {
                bool next = max > 0 && level < max;
                _gaugeGhost.gameObject.SetActive(next);
                if (next)
                {
                    var g = _gaugeGhost.rectTransform;
                    g.anchorMin = new Vector2((float)level / max, 0f);
                    g.anchorMax = new Vector2((float)(level + 1) / max, 1f);
                    g.offsetMin = g.offsetMax = Vector2.zero;
                }
            }
            return;
        }
        var f = _cardGaugeFill[slot];
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(ratio, 1f);
        f.offsetMin = Vector2.zero; f.offsetMax = Vector2.zero;
    }

    /// <summary>레벨 셀 + 게이지를 before→after로 한 단계씩 표기(카운트업/다운).</summary>
    private async UniTask CountLevel(int slot, int before, int after, int max)
    {
        if (!IsCardSlot(slot)) return;
        int step  = after >= before ? 1 : -1;
        int steps = Mathf.Max(1, Mathf.Abs(after - before));
        float perStep = Mathf.Min(CountStepDur, CountMaxDur / steps);

        int lvl = before;
        _cardLevel[slot].text = FormatLevel(lvl, max);
        SetGauge(slot, lvl, max);
        while (lvl != after)
        {
            if (_skipAnim) break;
            await Hold(perStep);
            lvl += step;
            _cardLevel[slot].text = FormatLevel(lvl, max, slot == RangedCard);
            SetGauge(slot, lvl, max);
        }

        // 스킵으로 중간에 끊겨도 표기는 최종값이어야 한다(RefreshAll이 뒤에 오지만, 그 사이 프레임이 남는다).
        _cardLevel[slot].text = FormatLevel(after, max, slot == RangedCard);
        SetGauge(slot, after, max);
    }

    /// <summary>카드 스케일 펀치(ShopSlot PopAsync 이식). unscaledDeltaTime.</summary>
    private async UniTask PunchCard(int slot, float amp, float dur)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var rt = _cardBg[slot].rectTransform;
        float t = 0f;
        while (t < 1f)
        {
            if (_skipAnim) break;
            t = Mathf.Min(t + Time.unscaledDeltaTime / dur, 1f);
            float s = 1f + amp * Mathf.Sin(t * Mathf.PI);
            rt.localScale = Vector3.one * s;
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>카드 배경색 플래시 → 원색 복귀(감쇠). unscaledDeltaTime.</summary>
    private async UniTask FlashCard(int slot, Color flash)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var img = _cardBg[slot];
        Color baseCol = img.color;
        float t = 0f;
        while (t < FlashDur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            img.color = Color.Lerp(flash, baseCol, t / FlashDur);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        img.color = baseCol;
    }

    /// <summary>카드 좌우 흔들림(ShopSlot ShakeAsync 이식) — 기준 앵커 복원. unscaledDeltaTime.</summary>
    private async UniTask ShakeCard(int slot, float amp, float dur)
    {
        if (slot < 0 || slot >= 2 || _cardBg[slot] == null) return;
        var rt = _cardBg[slot].rectTransform;
        Vector2 basePos = _cardBasePos[slot];
        float t = 0f;
        while (t < dur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            float damp = 1f - (t / dur);
            rt.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 60f) * amp * damp, 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        rt.anchoredPosition = basePos;
    }

    private async UniTask Hold(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (_skipAnim) return;
            t += Time.unscaledDeltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
    }

    private void ShowPromoteResult(PromoteResult r)
    {
        _resultText.text = r.outcome switch
        {
            PromoteOutcome.Success             => "<color=#FFD24A>전설로 승급!</color>",
            PromoteOutcome.RejectNoFuel        => "<color=#FF5250>강화재료 부족</color>",
            PromoteOutcome.RejectNotMaxed      => "<color=#FF5250>강화 MAX 필요</color>",
            PromoteOutcome.RejectAlreadyLegend => "<color=#8AB0D5>이미 승급됨</color>",
            _                                  => "<color=#FF5250>승급 불가</color>",
        };
        if (r.IsSuccess) ShopUIStyle.PlaySfx("shop_open");
        else ShopUIStyle.PlaySfx("shop_reject");
    }

    // ── 무대 연출 층 (개편 09-20; 표시층 전용 — 결과/데이터/세이브 불변) ──────────

    /// <summary>
    /// 런타임 전용 연출 준비 — 코드로 그린 스프라이트(빛·빛살·비네트)와 입자 풀은 프리팹에 저장할 수 없어 Bind에서 만든다.
    /// 재화 칸 툴팁(UITooltipTrigger)도 내용이 직렬화되지 않아 여기서 건다. 한 번만.
    /// </summary>
    private void InitRuntimeFx()
    {
        if (_fxReady) return;
        _fxReady = true;

        var dot = UI_RuneSelectPopup.SoftDot;
        if (_weaponGlow != null) _weaponGlow.sprite = dot;
        if (_forgeGlow  != null) _forgeGlow.sprite  = dot;
        if (_stageFlash != null) _stageFlash.sprite = dot;
        if (_rays       != null) _rays.sprite       = UI_RuneSelectPopup.Rays;
        if (_heat       != null) _heat.sprite       = Vignette;

        _sparks     = MakePool(_sparkRoot, "Spark", SparkPool, dot);
        _embers     = MakePool(_emberRoot, "Ember", EmberPool, dot);
        _sparkPos   = new Vector2[SparkPool]; _sparkVel   = new Vector2[SparkPool];
        _sparkLife  = new float[SparkPool];   _sparkDecay = new float[SparkPool];
        _emberPos   = new Vector2[EmberPool]; _emberVel   = new Vector2[EmberPool];
        _emberLife  = new float[EmberPool];   _emberDecay = new float[EmberPool];

        // HUD 재화 칸과 같은 툴팁 문구(HudView) — 이름을 칸 안에 쓰지 않는 대신 여기서 알려 준다.
        if (_matPill != null) UITooltipTrigger.Attach(_matPill, "강화재료", "재련소에서 무기를 강화·진화한다.\n런이 끝나면 사라진다.", MatTint);
        if (_orePill != null) UITooltipTrigger.Attach(_orePill, "원석", "정제소에서 무작위 룬을 뽑는다.\n런이 끝나면 사라진다.", OreTint);

        ApplyPartRowArt();
        AmbientLoopAsync().Forget();
    }

    /// <summary>
    /// 원거리 파츠 행에 납품 카드 아트(원거리 강화 바탕·테두리)를 입힌다. 원본은 9-slice 경계가 없어
    /// 440×108 행에 늘리면 테두리선이 뭉개지므로, 같은 텍스처로 경계를 준 <b>런타임 사본</b>을 만든다(.meta는 건드리지 않는다).
    /// 아트가 없거나 사본을 못 만들면 예전 색 판 그대로.
    /// </summary>
    private void ApplyPartRowArt()
    {
        _rowBgSliced    ??= ShopUIStyle.SlicedCopy(_skin?.rangedGaugeFill,  Vector4.one * 34f);
        _rowFrameSliced ??= ShopUIStyle.SlicedCopy(_skin?.rangedGaugeFrame, Vector4.one * 34f);
        if (_rowBgSliced == null || _rowFrameSliced == null) return;

        for (int i = 0; i < PartRows; i++)
        {
            if (_partsSlots[i] != null)
            {
                _partsSlots[i].sprite = _rowBgSliced;
                _partsSlots[i].type   = Image.Type.Sliced;
            }
            if (_partsFrames[i] != null)
            {
                _partsFrames[i].sprite = _rowFrameSliced;
                _partsFrames[i].type   = Image.Type.Sliced;
            }
        }
        _partRowArt = true;
        RefreshRangedParts();
    }

    /// <summary>가장자리만 불투명한 타원 비네트(흰색) — 열기 색은 Image.color가 입힌다.</summary>
    private static Sprite Vignette => _vignette != null ? _vignette
        : (_vignette = UI_RuneSelectPopup.MakeProcSprite("Crucible_Vignette", 64, 64, (u, v) =>
          {
              float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
              float k  = Mathf.InverseLerp(0.75f, 1.4f, Mathf.Sqrt(dx * dx + dy * dy));
              return k * k * (3f - 2f * k);
          }));

    private static Image[] MakePool(RectTransform root, string name, int n, Sprite sprite)
    {
        if (root == null) return null;
        var pool = new Image[n];
        var tl   = new Vector2(0f, 1f);
        for (int i = 0; i < n; i++)
        {
            var img = ShopUIStyle.MakeImage(root, name, Color.white);
            img.sprite = sprite;
            ShopUIStyle.Anchor(img.rectTransform, tl, tl, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f));
            img.gameObject.SetActive(false);
            pool[i] = img;
        }
        return pool;
    }

    /// <summary>
    /// 상시 연출 — 불씨가 떠오르고, 무기 뒤 빛이 단계만큼 달아오르고(숨쉬기), 도박 구간이면 화면 가장자리가 달아오른다.
    /// 불꽃 입자도 여기서 움직인다. 할당 없음(풀·배열은 InitRuntimeFx에서 한 번).
    /// </summary>
    private async UniTaskVoid AmbientLoopAsync()
    {
        float time = 0f;
        try
        {
            while (true)
            {
                float dt = Time.unscaledDeltaTime;
                time += dt;
                bool melee = _activeTab == 0;

                _glowShown = Mathf.MoveTowards(_glowShown, _heatLevel01, dt * 1.5f);
                if (_weaponGlow != null)
                {
                    var c = GlowColor;
                    c.a = (0.15f + 0.6f * _glowShown) * (0.85f + 0.15f * Mathf.Sin(time * 2.2f));
                    _weaponGlow.color = c;
                }
                if (_heat != null)
                {
                    float target = _danger && melee ? HeatAlpha * (0.8f + 0.2f * Mathf.Sin(time * 3.9f)) : 0f;
                    var c = _heat.color;
                    c.a = Mathf.MoveTowards(c.a, target, dt * 1.6f);
                    _heat.color = c;
                }
                if (_gaugeGhost != null && _gaugeGhost.gameObject.activeSelf)
                {
                    var g = GhostColor;
                    g.a *= 0.55f + 0.45f * Mathf.Sin(time * 3.2f);   // 숨쉬듯 — 아직 안 찬 칸
                    _gaugeGhost.color = g;
                }
                if (melee && _embers != null) TickEmbers(dt);
                if (_sparksAlive > 0) TickSparks(dt);

                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void TickEmbers(float dt)
    {
        _emberSpawn = Mathf.Min(_emberSpawn + dt * EmberRate, 1f);   // 풀이 가득 차 있으면 쌓아 두지 않는다
        for (int i = 0; i < _embers.Length; i++)
        {
            var img = _embers[i];
            if (_emberLife[i] <= 0f)
            {
                if (_emberSpawn < 1f) continue;
                _emberSpawn -= 1f;
                _emberPos[i]   = new Vector2(UnityEngine.Random.Range(180f, 780f), StagePanelH + 8f);
                _emberVel[i]   = new Vector2(UnityEngine.Random.Range(-12f, 12f), -UnityEngine.Random.Range(36f, 108f));
                _emberLife[i]  = 1f;
                _emberDecay[i] = UnityEngine.Random.Range(0.22f, 0.42f);
                float d = UnityEngine.Random.Range(6f, 12f);   // 부드러운 원이라 밝은 심은 지름의 1/3쯤이다
                img.rectTransform.sizeDelta = new Vector2(d, d);
                img.gameObject.SetActive(true);
            }
            _emberLife[i] -= _emberDecay[i] * dt;
            if (_emberLife[i] <= 0f) { img.gameObject.SetActive(false); continue; }
            _emberPos[i] += _emberVel[i] * dt;
            var c = EmberColor;
            c.a = _emberLife[i] * 0.9f;
            img.color = c;
            img.rectTransform.anchoredPosition = new Vector2(_emberPos[i].x, -_emberPos[i].y);
        }
    }

    /// <summary>불꽃 n개 — 위쪽 반원으로 튀어 중력에 떨어진다. 풀이 차면 오래된 것부터 덮어쓴다.</summary>
    private void BurstSparks(int n, Color color, float x, float y)
    {
        if (_sparks == null) return;
        for (int k = 0; k < n; k++)
        {
            int i = _sparkNext;
            _sparkNext = (_sparkNext + 1) % _sparks.Length;
            if (_sparkLife[i] <= 0f) _sparksAlive++;

            float a = -Mathf.PI * UnityEngine.Random.value;   // 위쪽 반원(무대 좌표는 아래가 +)
            float v = UnityEngine.Random.Range(180f, 520f);
            _sparkPos[i]   = new Vector2(x, y);
            _sparkVel[i]   = new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v);
            _sparkLife[i]  = 1f;
            _sparkDecay[i] = UnityEngine.Random.Range(1.3f, 2.4f);

            var img = _sparks[i];
            img.color = color;
            float d = UnityEngine.Random.Range(12f, 22f);
            img.rectTransform.sizeDelta = new Vector2(d, d);
            img.rectTransform.anchoredPosition = new Vector2(x, -y);
            img.gameObject.SetActive(true);
        }
    }

    private void TickSparks(float dt)
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparkLife[i] <= 0f) continue;
            _sparkLife[i] -= _sparkDecay[i] * dt;
            var img = _sparks[i];
            if (_sparkLife[i] <= 0f)
            {
                img.gameObject.SetActive(false);
                _sparksAlive--;
                continue;
            }
            _sparkVel[i].y += SparkGravity * dt;
            _sparkPos[i]   += _sparkVel[i] * dt;
            var c = img.color;
            c.a = _sparkLife[i];
            img.color = c;
            img.rectTransform.anchoredPosition = new Vector2(_sparkPos[i].x, -_sparkPos[i].y);
        }
    }

    /// <summary>포커스 → 망치(들어올림 멈칫 → 내려치기) → 임팩트(창 펀치 · 불꽃). 판정 직전까지.</summary>
    private async UniTask ForgeStrikeAsync()
    {
        await FocusAsync(true);
        await SwingHammerAsync();
        ShopUIStyle.PlaySfx("crucible_hit");
        BurstSparks(StrikeSparks, SparkGold, StrikeX, StrikeY);
        _hammerGen++;
        HammerRecoverAsync(_hammerGen).Forget();
        await PunchWindowAsync();
    }

    /// <summary>포커스 — 정보창·대사 밴드가 흐려지고 암막이 짙어지며 무대가 살짝 다가온다(끄면 되돌림).</summary>
    private async UniTask FocusAsync(bool on)
    {
        float from = _focus01, to = on ? 1f : 0f;
        float dur  = on ? FocusDur : FocusDur * 2f;
        float t = 0f;
        while (t < dur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            ApplyFocus(Mathf.Lerp(from, to, t / dur));
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        ApplyFocus(to);
    }

    private void ApplyFocus(float k)
    {
        _focus01 = k;
        float a = Mathf.Lerp(1f, FocusDim, k);
        if (_infoGroup != null) _infoGroup.alpha = a;
        if (_bandGroup != null) _bandGroup.alpha = a;
        if (_veil != null)
        {
            var c = ShopUIStyle.Veil;
            c.a = Mathf.Lerp(ShopUIStyle.Veil.a, FocusVeilAlpha, k);
            _veil.color = c;
        }
        if (_stageArea != null) _stageArea.localScale = Vector3.one * Mathf.Lerp(1f, FocusStageScale, k);
    }

    private async UniTask SwingHammerAsync()
    {
        if (_hammer == null) return;
        _hammerGen++;   // 이전 회수 연출이 남아 있으면 물러나게 한다
        if (_hammerGroup != null) _hammerGroup.alpha = 1f;
        await RotateHammerAsync(HammerRaisedDeg, HammerWindDeg, HammerRaiseDur, easeIn: false);
        await RotateHammerAsync(HammerWindDeg, 0f, HammerDropDur, easeIn: true);
    }

    private async UniTask RotateHammerAsync(float from, float to, float dur, bool easeIn)
    {
        float t = 0f;
        while (t < dur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = easeIn ? k * k * k : 1f - (1f - k) * (1f - k);
            _hammer.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(from, to, k));
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _hammer.localRotation = Quaternion.Euler(0f, 0f, to);
    }

    /// <summary>튕겨 오른 뒤 사라진다. 다음 강화가 먼저 시작되면(세대 교체) 즉시 물러난다.</summary>
    private async UniTaskVoid HammerRecoverAsync(int gen)
    {
        try
        {
            float t = 0f;
            while (t < HammerRecoverDur + HammerFadeDur)
            {
                if (gen != _hammerGen) return;
                if (_skipAnim) break;
                t += Time.unscaledDeltaTime;
                float up = Mathf.Clamp01(t / HammerRecoverDur);
                _hammer.localRotation = Quaternion.Euler(0f, 0f, HammerReboundDeg * (1f - (1f - up) * (1f - up)));
                if (_hammerGroup != null) _hammerGroup.alpha = 1f - Mathf.Clamp01((t - HammerRecoverDur) / HammerFadeDur);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        if (gen == _hammerGen) HideHammer();
    }

    private void HideHammer()
    {
        _hammerGen++;
        if (_hammerGroup != null) _hammerGroup.alpha = 0f;
        if (_hammer != null) _hammer.localRotation = Quaternion.Euler(0f, 0f, HammerRaisedDeg);
    }

    /// <summary>임팩트 — 창이 한 번 아래로 꺼졌다 돌아온다.</summary>
    private async UniTask PunchWindowAsync()
    {
        if (_window == null) return;
        float t = 0f;
        while (t < ImpactPunchDur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / ImpactPunchDur);
            _window.anchoredPosition = new Vector2(0f, -ImpactPunchPx * k * k);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _window.anchoredPosition = Vector2.zero;
    }

    /// <summary>실패 — 창 전체가 흔들린다(감쇠).</summary>
    private async UniTask ShakeWindowAsync(float amp)
    {
        if (_window == null) return;
        float t = 0f;
        while (t < WindowShakeDur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            float damp = 1f - t / WindowShakeDur;
            _window.anchoredPosition = new Vector2(Mathf.Sin(t * 60f) * amp * damp, Mathf.Cos(t * 47f) * amp * 0.6f * damp);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _window.anchoredPosition = Vector2.zero;
    }

    /// <summary>판정 빛 — 무대 한가운데서 번졌다가 걷힌다. 새 빛이 오면 이전 것은 물러난다.</summary>
    private void FlashStage(Color color)
    {
        if (_stageFlash == null) return;
        _flashGen++;
        FlashStageAsync(_flashGen, color).Forget();
    }

    private async UniTaskVoid FlashStageAsync(int gen, Color color)
    {
        try
        {
            float t = 0f;
            while (t < StageFlashDur)
            {
                if (gen != _flashGen) return;
                if (_skipAnim) break;
                t += Time.unscaledDeltaTime;
                var c = color;
                c.a = color.a * (1f - Mathf.Clamp01(t / StageFlashDur));
                _stageFlash.color = c;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        if (gen == _flashGen) _stageFlash.color = Color.clear;
    }

    /// <summary>공격력 카운트 — 판정 순간부터 전→후로 굴러간다(끝나면 RefreshAll이 「전 → 다음」 한 줄로 되돌린다).</summary>
    private async UniTask CountAttackAsync(int beforeLevel, int afterLevel)
    {
        var w   = _controller?.GetSlot(PlayerWeaponManager.Slot0);
        var atk = _cardAtk[0];
        if (w == null || atk == null) return;

        float from = AttackAt(w, beforeLevel), to = AttackAt(w, afterLevel);
        string fmt = to >= from ? AtkUpFormat : AtkDownFormat;
        float t = 0f;
        while (t < AtkCountDur)
        {
            if (_skipAnim) break;
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / AtkCountDur), 3f);
            atk.SetText(fmt, Mathf.Lerp(from, to, k));
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        atk.SetText(fmt, to);
    }

    /// <summary>잭팟 환불 — 낸 재료가 재화 칸으로 되돌아온다(CurrencyCounter가 +N을 굴린다).</summary>
    private async UniTask RefundAsync()
    {
        await Hold(JackpotRefundDelay);
        CurrencyCounter.Apply(_fuelText, _controller.FuelAmount);
    }

    /// <summary>잭팟 빛살(회전) + 배너 — 1.6초. 스킵·다음 잭팟이면 즉시 물러난다.</summary>
    private async UniTaskVoid JackpotShowAsync(int gen)
    {
        if (_rays == null || _jackpotBanner == null) return;
        _rays.gameObject.SetActive(true);
        _jackpotBanner.gameObject.SetActive(true);
        var brt = _jackpotBanner.rectTransform;
        try
        {
            float t = 0f;
            while (t < JackpotShowDur)
            {
                if (gen != _jackpotGen) return;
                if (_skipAnim) break;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / JackpotShowDur);
                var rc = RaysColor;
                rc.a = Mathf.Sin(k * Mathf.PI) * 0.9f;
                _rays.color = rc;
                _rays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -80f * k);
                float s = k < 0.3f ? Mathf.Lerp(0.5f, 1.1f, k / 0.3f)
                        : k < 0.8f ? Mathf.Lerp(1.1f, 1f, (k - 0.3f) / 0.5f) : 1f;
                brt.localScale = new Vector3(s, s, 1f);
                _jackpotBanner.alpha = k < 0.3f ? k / 0.3f : k < 0.8f ? 1f : 1f - (k - 0.8f) / 0.2f;
                SetWeaponTitleAlpha(1f - _jackpotBanner.alpha);   // 배너가 이름 자리에 뜬다 — 겹쳐 읽히지 않게
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        if (gen != _jackpotGen) return;
        _rays.gameObject.SetActive(false);
        _jackpotBanner.gameObject.SetActive(false);
        SetWeaponTitleAlpha(1f);
    }

    private void SetWeaponTitleAlpha(float a)
    {
        if (_cardName[0] != null)   _cardName[0].alpha   = a;
        if (_focusTypeText != null) _focusTypeText.alpha = a;
    }

    /// <summary>실패 — 무기가 푸르게 식었다가 서서히 돌아온다.</summary>
    private async UniTaskVoid CoolWeaponAsync(int gen)
    {
        if (_weaponImg == null) return;
        try
        {
            float t = 0f;
            while (t < CoolDur)
            {
                if (gen != _coolGen) return;
                if (_skipAnim) break;
                t += Time.unscaledDeltaTime;
                _weaponImg.color = Color.Lerp(CoolTint, Color.white, t / CoolDur);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        if (gen == _coolGen) _weaponImg.color = Color.white;
    }

    /// <summary>도박 구간 배지가 들어오는 순간 튀어나온다.</summary>
    private async UniTaskVoid PopAsync(RectTransform rt)
    {
        try
        {
            float t = 0f;
            while (t < BadgePopDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / BadgePopDur);
                float s = k < 0.6f ? Mathf.Lerp(0.4f, 1.15f, k / 0.6f) : Mathf.Lerp(1.15f, 1f, (k - 0.6f) / 0.4f);
                rt.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        rt.localScale = Vector3.one;
    }

    // ── 렌더 ────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (_controller == null) return;

        // 재화 칸은 HUD처럼 숫자만 — 이름은 아이콘·툴팁이 말한다.
        CurrencyCounter.Apply(_fuelText, _controller.FuelAmount);
        if (_oreText != null)
            CurrencyCounter.Apply(_oreText, _controller.Run?.FuelBank?.RuneOre ?? 0);

        RefreshEvolveButton();

        // 0번 카드 = 근접 무기(실제 슬롯 카드). 스킨이면 흰색, 아니면 대상 하이라이트 색.
        {
            const int i = 0;
            var w = _controller.GetSlot(i);
            _cardBg[i].color = _skin != null ? Color.white : (i == _targetSlot ? CardTargetBg : ShopUIStyle.CardFill);

            if (w == null)
            {
                _cardName[i].text = "—";
                _cardLevel[i].text = "";
                if (_cardAtk[i] != null)      _cardAtk[i].text = "";
                if (_focusTypeText != null)   _focusTypeText.text = "";
                SetGauge(i, 0, 1);
                _heatLevel01 = 0f;
            }
            else
            {
                int max = _controller.MaxAt(i);
                string legend = string.IsNullOrEmpty(w.legendId) ? "" : $"  <color=#FFD24A>[{LegendName(w.legendId)}]</color>";
                _cardName[i].text  = $"{w.displayName}{legend}";
                _cardLevel[i].text = FormatLevel(w.enhanceLevel, max);
                if (_cardAtk[i] != null)    _cardAtk[i].text = AttackLine(w, !_controller.CanEnhance(i));
                if (_focusTypeText != null) _focusTypeText.text = TypeTierLabel(w, max, i);
                SetGauge(i, w.enhanceLevel, max);
                RefreshTicks(w, w.enhanceLevel, max);
                _heatLevel01 = max > 0 ? Mathf.Clamp01((float)w.enhanceLevel / max) : 0f;
                // 무기 그림 — 장착 무기 아이콘. 진화로 무기가 바뀌면 그림도 따라 바뀐다.
                if (_weaponImg != null && w.icon != null) _weaponImg.sprite = w.icon;
            }
        }

        RefreshRangedParts();   // 파츠 5행 — 강화·연료 변동으로도 버튼 상태가 바뀐다
        RefreshRangedCard();
        RefreshFocusExtras();
        RefreshInfo();
        RefreshBandBadge();
        RefreshPromoteRow();
    }

    /// <summary>
    /// 진화 버튼 — 진화가 있는 무기면 무기 탭에서 늘 보인다. 최대 강화 전엔 잠겨서 "어디서 열리는지"를 말하고,
    /// 조건을 채우면 보라빛으로 열린다. 진화가 없는 무기(이미 진화함 등)는 감춘다.
    /// </summary>
    private void RefreshEvolveButton()
    {
        if (_evolveBtn == null) return;
        // 진화 분기가 남았으면 진화, 마지막 형태의 검이면 같은 자리에서 전설 승급.
        bool promote = IsPromoteRoute(_controller.GetSlot(PlayerWeaponManager.Slot0));
        bool ready = _controller.CanPromote(PlayerWeaponManager.Slot0);
        // 열렸을 때만 보인다 — 잠긴 「◆ 진화 — +6에서 열림」은 게이지 끝 눈금(◆)과 같은 말이라 읽을 거리만 늘렸다(09-29).
        bool show = _activeTab == 0 && (CurrentBranches.Count > 0 || promote) && ready;
        _evolveBtn.gameObject.SetActive(show);
        UIAffordGlow.Set(_evolveBtn, show);
        if (!show) return;

        _evolveBtn.interactable = ready;
        if (_evolveBtn.TryGetComponent<Image>(out var img)) img.color = ready ? EvolveReady : EvolveLocked;
        if (_evolveLabel != null)
        {
            string verb = promote ? "승급" : "진화";
            _evolveLabel.text  = ready ? (promote ? "◆ 승급 — 전설을 고른다" : "◆ 진화 — 형태를 고른다")
                                       : $"◆ {verb} — +{_controller.MaxAt(PlayerWeaponManager.Slot0)}에서 열림";
            _evolveLabel.color = ready ? Color.white : TextMuted;
        }
    }

    /// <summary>대사 밴드 배지 — 돌발 이벤트가 우선이고, 없으면 무기 탭의 도박 구간 경고.</summary>
    private void RefreshBandBadge()
    {
        if (_eventBanner == null || _controller == null) return;
        bool ev   = _controller.HasEvent;
        bool warn = !ev && _activeTab == 0 && _danger;
        _eventBanner.SetActive(ev || warn);
        if (_eventBannerText != null) _eventBannerText.text = ev ? _controller.EventBanner : "위험 · 실패 시 하락";
    }

    /// <summary>
    /// 게이지 눈금 — 상한만큼 칸을 나눠 +1…+상한을 적는다. 도박 구간(그 단계에서 실패하면 하락)은 붉게, 지나온 단계는 밝게,
    /// 끝 칸은 금빛(진화가 있으면 ◆). 상한이 눈금 수보다 크면 고르게 건너뛴다.
    /// </summary>
    private void RefreshTicks(WeaponData w, int level, int max)
    {
        if (_ticks == null || max <= 0) return;
        var table = _controller?.Table;
        int n = Mathf.Min(max, _ticks.Length);
        bool roomy = (GaugeW - 48f) / n >= 80f;   // 「+6 위험」이 들어갈 칸 폭
        bool firstDanger = true;
        for (int j = 0; j < _ticks.Length; j++)
        {
            var t = _ticks[j];
            if (t == null) continue;
            bool on = j < n;
            t.gameObject.SetActive(on);
            if (!on) continue;

            int lv = n == max ? j + 1 : Mathf.RoundToInt((j + 1) * (float)max / n);
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2((float)j / n, 0f);
            rt.anchorMax = new Vector2((float)(j + 1) / n, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            bool last   = lv >= max;
            bool danger = !last && table != null && table.DropAt(lv) > 0;
            if (last)                                t.text = w != null && w.CanEvolve ? $"+{lv} ◆" : $"+{lv}";
            else if (danger && firstDanger && roomy) t.text = $"+{lv} 위험";
            else                                     t.text = $"+{lv}";
            if (danger) firstDanger = false;

            t.color = last ? ShopUIStyle.Gold : danger ? TickDanger : lv <= level ? ShopUIStyle.TextPrimary
                    : lv == level + 1 ? TickNext : TickDim;
        }
    }

    /// <summary>
    /// 1번 카드 = 선택한 <b>파츠</b>. 예전엔 여기에 원거리 무기를 그려서 탭 이름("원거리 파츠")과
    /// 내용이 어긋났고, 강화 버튼이 올리는 대상과 카드가 보여주는 대상이 달랐다.
    /// </summary>
    private void RefreshRangedCard()
    {
        if (_cardName[RangedCard] == null) return;

        // 호버가 있으면 그쪽을 임시로 보여준다 — 대상을 바꾸지 않고 4칸을 훑어볼 수 있게.
        var def = HoveredPartDef() ?? SelectedPartDef();
        var state = RangedPartsState.Current;

        if (def == null)
        {
            _cardName[RangedCard].text = "파츠를 고르세요";
            _partKindText.text = "";
            _cardLevel[RangedCard].text = "";
            _cardAtk[RangedCard].text = "";
            _partGrowthText.text = "";
            _partSynergyText.text = "";
            _partDescText.text = "왼쪽 목록에서 파츠를 고르면 여기에 결과가 나온다.";
            SetBar(_cardGaugeFill[RangedCard], 0f);
            // 고른 파츠가 없을 땐 레벨 막대 트랙도 감춘다 — 아트 바탕 위에 빈 띠만 남아 보인다.
            _cardGaugeFill[RangedCard].parent.gameObject.SetActive(false);
            SetPartButton(null, 0, 0, false);
            return;
        }
        _cardGaugeFill[RangedCard].parent.gameObject.SetActive(true);

        int level = state.LevelOf(def.part_id);
        int max   = def.max_level > 0 ? def.max_level : level;
        bool maxed = def.max_level > 0 && level >= def.max_level;

        _cardName[RangedCard].text  = def.part_name;
        _partKindText.text          = PartKindLabel(def);
        _cardLevel[RangedCard].text = $"Lv.{level} <size=70%><color=#9A98A0>/ {max}</color></size>";
        _cardAtk[RangedCard].text   = PartEffectLine(def, level);
        _partGrowthText.text        = PartGrowthLine(def, level, maxed);
        _partDescText.text          = def.description;
        _partSynergyText.text       = PartSynergyLine(def, state);
        SetBar(_cardGaugeFill[RangedCard], max > 0 ? (float)level / max : 0f);

        // 호버 중(선택과 다른 파츠)에는 버튼을 대상 밖으로 두어 오조작을 막는다.
        bool isSelected = SelectedPartDef() == def;
        SetPartButton(def, _controller != null ? _controller.PartCostAt(def.part_id) : 0,
                      _controller != null ? _controller.FuelAmount : 0, isSelected && !maxed);
    }

    /// <summary>마우스가 올라간 슬롯의 파츠. 없으면 null.</summary>
    private WeaponPartEntry HoveredPartDef()
    {
        var all = Managers.WeaponParts?.All;
        if (all == null || _hoverPartSlot < 0 || _hoverPartSlot >= all.Count) return null;
        return all[_hoverPartSlot];
    }

    /// <summary>
    /// 상세 패널의 비용 줄. 강화 버튼 자체는 좌열 각 행으로 옮겼으므로(<c>_partEnhanceBtn</c>은 null)
    /// 여기서는 "지금 얼마 드는가"만 적는다 — 누르는 곳 옆에는 비용이 이미 붙어 있고,
    /// 이 줄은 고른 파츠를 읽는 동안 같은 정보를 잃지 않게 하는 용도다.
    /// </summary>
    private void SetPartButton(WeaponPartEntry def, int cost, int have, bool usable)
    {
        if (_partCostText != null)
        {
            if (def == null) _partCostText.text = "";
            else if (def.max_level > 0 && RangedPartsState.Current.LevelOf(def.part_id) >= def.max_level)
                _partCostText.text = "<color=#8AB0D5>더 올릴 수 없다</color>";
            else
                _partCostText.text = have >= cost
                    ? $"강화 비용 <color=#FFD24A>{cost}</color> · 보유 {have} · <color=#7AD46E>실패 없음</color>"
                    : $"<color=#FF5250>강화 비용 {cost}</color> · 보유 {have}";
        }

        if (_partEnhanceBtn == null) return;
        _partEnhanceBtn.interactable = def != null && usable && have >= cost && !_animating;
    }

    /// <summary>파츠 효과 한 줄 — 지금 값과 다음 레벨 값. 계단형은 임계 전까지 값이 그대로라 남은 강수도 함께 보인다.</summary>
    private static string PartEffectLine(WeaponPartEntry def, int level)
    {
        bool maxed = def.max_level > 0 && level >= def.max_level;
        float cur  = def.ValueAt(level);
        if (maxed) return $"{PartValueText(def, cur)}  <color=#8AB0D5>(최대)</color>";

        float next = def.ValueAt(level + 1);
        if (Mathf.Approximately(cur, next))
        {
            int toStep = def.milestone_every > 0 ? def.milestone_every - ((level - 1) % def.milestone_every) : 0;
            return $"{PartValueText(def, cur)}  <size=85%><color=#9A98A0>다음 단계까지 {toStep}강</color></size>";
        }
        return $"{PartValueText(def, cur)} <color=#7AD46E>→ {PartValueText(def, next)}</color>";
    }

    /// <summary>파츠 종류별 단위를 붙인 효과값 표기 — 숫자만 띄우면 무엇이 오르는지 읽히지 않는다.</summary>
    // 시작 파츠 선택창(UI_StartPartPopup)도 같은 문구를 쓴다 — 두 화면의 효과 표기가 갈라지지 않게 한곳에.
    internal static string PartValueText(WeaponPartEntry def, float v) => def.Kind switch
    {
        RangedPartKind.Split   => $"{Mathf.RoundToInt(v) + 1}발",
        RangedPartKind.Pierce  => $"관통 {Mathf.RoundToInt(v)}",
        RangedPartKind.Explode => $"반경 {v:F2}",
        RangedPartKind.Homing  => $"유도 {v:F0}°/s",
        _                      => $"위력·크기 +{v * 100f:F0}%",
    };

    /// <summary>
    /// 강화하면 어떻게 자라는지 한 줄. 계단형(분열·관통)은 임계마다 한 단계씩 오르므로
    /// "왜 올렸는데 안 변하지"가 생긴다 — 규칙을 명시해 그 구간을 납득시킨다.
    /// </summary>
    private static string PartGrowthLine(WeaponPartEntry def, int level, bool maxed)
    {
        if (maxed) return "<color=#8AB0D5>강화</color>  더 올릴 수 없다";

        int left = def.max_level > 0 ? def.max_level - level : 0;
        string cap = left > 0 ? $"  <size=85%><color=#9A98A0>상한까지 {left}강</color></size>" : "";

        return def.milestone_every > 0
            ? $"<color=#FFD24A>계단형</color>  {def.milestone_every}강마다 한 단계 오른다{cap}"
            : $"<color=#7FE3FF>연속형</color>  1강마다 꾸준히 오른다{cap}";
    }

    /// <summary>
    /// 다른 파츠와의 관계. 이미 낀 조합은 <b>발동 중</b>으로, 아직 아닌 조합은 힌트로 보여준다 —
    /// 슬롯이 유한하므로 무엇과 같이 낄지가 실제 결정이다.
    /// </summary>
    private static string PartSynergyLine(WeaponPartEntry def, RangedPartsState state)
    {
        bool hasPierce = HasKind(state, RangedPartKind.Pierce);
        bool hasHoming = HasKind(state, RangedPartKind.Homing);
        bool hasSplit  = HasKind(state, RangedPartKind.Split);

        switch (def.Kind)
        {
            case RangedPartKind.Homing:
                return hasPierce
                    ? "<color=#7AD46E>연계 발동</color>  관통을 다 쓰면 되돌아와 다시 때린다"
                    : "<size=90%><color=#9A98A0>관통과 함께 끼면 되돌아와 재타격한다</color></size>";

            case RangedPartKind.Pierce:
                return hasHoming
                    ? "<color=#7AD46E>연계 발동</color>  유도와 맞물려 되돌아온다"
                    : "<size=90%><color=#9A98A0>유도와 함께 끼면 꿰뚫은 뒤 되돌아온다</color></size>";

            case RangedPartKind.Explode:
                return hasSplit
                    ? "<color=#7AD46E>연계 발동</color>  갈래마다 터진다 — 분열과 곱해진다"
                    : "<size=90%><color=#9A98A0>분열과 함께 끼면 갈래마다 터진다</color></size>";

            case RangedPartKind.Split:
                return HasKind(state, RangedPartKind.Power)
                    ? "<color=#FF9A5A>상충</color>  거력은 단발 강공, 분열은 다발 약공 — 축이 반대다"
                    : "<size=90%><color=#9A98A0>폭발과 함께 끼면 갈래마다 터진다</color></size>";

            default:   // Power
                return hasSplit
                    ? "<color=#FF9A5A>상충</color>  분열과 축이 반대다 — 한쪽에 몰아주는 편이 세다"
                    : "<size=90%><color=#9A98A0>단발 강공 축 — 분열과는 서로 깎아먹는다</color></size>";
        }
    }

    private static bool HasKind(RangedPartsState state, RangedPartKind kind)
    {
        if (state == null) return false;
        var data = Managers.WeaponParts;
        var list = state.Equipped_;
        for (int i = 0; i < list.Count; i++)
        {
            var d = data?.GetById(list[i].partId);
            if (d != null && d.Kind == kind) return true;
        }
        return false;
    }

    private static string PartKindLabel(WeaponPartEntry def) => def.Kind switch
    {
        RangedPartKind.Split   => "분열 — 투사체 수",
        RangedPartKind.Pierce  => "관통 — 적 통과",
        RangedPartKind.Explode => "폭발 — 착탄 범위",
        RangedPartKind.Homing  => "유도 — 궤적 조작",
        _                      => "거력 — 위력·크기",
    };

    /// <summary>다음 강화가 성공했을 때의 공격력. 테이블이 없으면 현재값 그대로.</summary>
    private float NextAttack(WeaponData w) => w == null ? 0f : AttackAt(w, w.enhanceLevel + 1);

    /// <summary>이 무기가 <paramref name="level"/>강일 때의 공격력(연출의 전→후 카운트용). 테이블이 없으면 현재값.</summary>
    private float AttackAt(WeaponData w, int level)
    {
        if (w == null) return 0f;
        var table = _controller?.Table;
        if (table == null) return w.baseAttack;

        float raw = w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack;
        return raw * table.AttackMult(level, w.legendId);
    }

    /// <summary>카드 아래 공격력 한 줄 — 지금 값과 성공 시 값. 파츠 카드의 효과 줄과 같은 형식.</summary>
    private string AttackLine(WeaponData w, bool maxed)
    {
        if (w == null) return "";
        if (maxed) return $"공격 {w.baseAttack:F1}  <size=85%><color=#8AB0D5>(최대)</color></size>";

        float next = NextAttack(w);
        return $"공격 {w.baseAttack:F1} <color=#7AD46E>→ {next:F1}</color>"
             + $" <size=80%><color=#9A98A0>(+{next - w.baseAttack:F1})</color></size>";
    }

    /// <summary>무기 탭 — 도박 구간(배지·열기) + 표의 값(성공 시 / 실패 시 / 잭팟 / 다음 목표).</summary>
    private void RefreshFocusExtras()
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null)
        {
            SetDanger(false, pop: false);
            if (_milestoneLabel != null) _milestoneLabel.text = "";
            return;
        }

        int  max   = _controller.MaxAt(PlayerWeaponManager.Slot0);
        bool maxed = !_controller.CanEnhance(PlayerWeaponManager.Slot0);
        // 실패 하락 시작 = 도박구간. 최대 강화면 더 굴릴 게 없으니 경고도 없다.
        SetDanger(!maxed && _controller.DropAt(PlayerWeaponManager.Slot0) > 0, pop: true);

        // 다음 목표(진화까지 N강 · 스킬 단계)는 게이지 끝 눈금(◆)이 말한다 — 표에서 걷었다(09-29).
        if (_milestoneLabel != null) _milestoneLabel.text = "";

        if (_detailSuccess == null) return;
        if (maxed)
        {
            string mastery = MasteryText(w);
            _detailSuccess.text = w.CanEvolve ? "진화하면 다음 구간이 열린다" : "—";
            _detailFail.text    = string.IsNullOrEmpty(mastery) ? "—" : mastery;
            _detailJackpot.text = "";
        }
        else
        {
            // 무기 탭 3줄: [0] 필요 재료(RefreshInfo) · [1] 실패 시 · [2] 잭팟. 성공 시 공격은 무대의 공격 줄이 말한다.
            int drop = _controller.DropAt(PlayerWeaponManager.Slot0);
            _detailSuccess.text = drop > 0 ? $"<color=#FF7A6A>-{drop} 하락</color>"
                                           : "<color=#7AD46E>하락 없음 (안전)</color>";

            var jb = new System.Text.StringBuilder();
            jb.Append("<color=#FFD24A>").Append((_controller.JackpotChance * 100f).ToString("F0")).Append("%</color>")
              .Append(" <size=80%><color=#9A98A0>재료 환불</color></size>");
            if (_controller.StreakJackpotBonus > 0f)
                jb.Append(" <size=80%><color=#E8C07A>▲ 연속 +").Append((_controller.StreakJackpotBonus * 100f).ToString("F0")).Append("%p</color></size>");
            if (_controller.EventJackpotBonus > 0f)
                jb.Append(" <size=80%><color=#FFA35A><b>▲ 화로 +").Append((_controller.EventJackpotBonus * 100f).ToString("F0")).Append("%p</b></color></size>");
            _detailFail.text    = jb.ToString();
            _detailJackpot.text = "";
        }
    }

    /// <summary>도박 구간 표시 — 무대 배지(들어오는 순간 튀어나온다) · 열기(상시 루프가 읽는다) · 대사 밴드 배지.</summary>
    private void SetDanger(bool danger, bool pop)
    {
        bool entered = danger && !_danger;
        _danger = danger;
        if (_zoneBg != null)
        {
            _zoneBg.gameObject.SetActive(danger);
            if (entered && pop && _fxReady) PopAsync(_zoneBg.rectTransform).Forget();
        }
        RefreshBandBadge();
    }

    /// <summary>
    /// 마일스톤 한 줄. 진화 전에는 "진화까지", 진화 후에는 마스터리 구간을 가리킨다.
    /// 진화가 강화의 종점이 아니라는 걸 이 줄에서 읽히게 하는 게 목적.
    /// </summary>
    private string MilestoneText(WeaponData w, int max, bool maxed)
    {
        int left = Mathf.Max(0, max - w.enhanceLevel);
        string line;

        if (w.CanEvolve)
            line = maxed ? "◆ 진화 가능!" : $"◆ 진화까지 {left}강";
        else if (w.evolutionStage > 0)
        {
            int mastery = WeaponEnhanceService.MasteryLevel(w, _controller.Table);
            if (maxed)           line = $"◆ 마스터리 {mastery}단계 (최종)";
            else if (mastery > 0) line = $"◆ 마스터리 {mastery}단계 · 앞으로 {left}강";
            else line = $"◆ 마스터리 개방까지 {Mathf.Max(0, WeaponEnhanceService.BaseEnhanceCap(w, _controller.Table) - w.enhanceLevel)}강";
        }
        else
            line = maxed ? "" : $"◆ 최대까지 {left}강";

        return AppendSkillTier(w, max, line);
    }

    /// <summary>
    /// 스킬 단계 안내 — 강화가 스킬에 닿는다는 걸 이 줄에서 읽히게 한다.
    /// 다음 임계가 이 무기의 상한 밖이면 표시하지 않는다.
    /// 둘째 줄에 그 단계에서 <b>무엇이 바뀌는지</b>를 미리 보인다(09-25 — 예전엔 「N단계까지 M강」뿐이라 강화의 목적이 안 보였다).
    /// </summary>
    private static string AppendSkillTier(WeaponData w, int max, string line)
    {
        if (SkillTierResolver.IsRanged(w)) return line;   // 원거리는 파츠 탭이 담당
        if (!SkillTierResolver.TryGetNext(w, max, out int nextTier, out int remaining)) return line;
        string skill = $"◇ 스킬 {nextTier}단계까지 {remaining}강";
        string head  = string.IsNullOrEmpty(line) ? skill : $"{line}  {skill}";
        string change = TierChangeLine(w, nextTier);
        return change == null ? head : $"{head}\n{change}";
    }

    /// <summary>
    /// 다음 단계에서 E·R 스킬이 바뀌는 것 한 줄(작게). 둘 다 없으면 null. R 스킬은 <c>skillQ</c>(필드명 레거시).
    /// 스킬 이름 대신 HUD 키(E·R)로 적는다 — 행 폭(360px)에 16px 글자로 들어가게.
    /// </summary>
    private static string TierChangeLine(WeaponData w, int tier)
    {
        string body = TierChangeBody(w, tier);
        return body == null ? null : $"<size=80%><color=#C9A6FF>{body}</color></size>";
    }

    private static string TierChangeBody(WeaponData w, int tier)
    {
        string e = w.skillE != null ? w.skillE.ChangeAtTier(tier) : null;
        string r = w.skillQ != null ? w.skillQ.ChangeAtTier(tier) : null;
        if (e == null && r == null) return null;
        return e != null && r != null ? $"E {e} · R {r}" : e != null ? $"E {e}" : $"R {r}";
    }

    /// <summary>원거리 스킬 단계 안내(파츠 총레벨) — 근접 「◇ 스킬 N단계까지 M강」과 같은 역할. 파츠 탭 배율 행의 둘째 줄.</summary>
    private string RangedSkillTierLine()
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot1);
        if (w == null || !SkillTierResolver.IsRanged(w)) return "";
        if (!SkillTierResolver.TryGetNext(w, 0, out int nextTier, out int remaining)) return "";
        string body = TierChangeBody(w, nextTier);
        return $"\n<size=80%>◇ {nextTier}단계까지 파츠 +{remaining}"
             + (body != null ? $": <color=#C9A6FF>{body}</color>" : "") + "</size>";
    }

    /// <summary>마스터리 누적 효과(스킬 확장) 한 줄. 없으면 빈 문자열.</summary>
    private string MasteryText(WeaponData w)
    {
        var table = _controller.Table;
        int mastery = WeaponEnhanceService.MasteryLevel(w, table);
        if (table == null || mastery <= 0) return "";
        return $"<color=#C9A6FF>마스터리 {mastery}</color>  스킬 피해 +{table.MasterySkillDamage(mastery) * 100f:F0}%" +
               $" · 쿨다운 -{table.MasterySkillCdr(mastery) * 100f:F0}%";
    }

    private void RefreshInfo()
    {
        // 파츠 탭은 무기가 아니라 파츠를 올린다 — 무기 성공률/재료를 띄우면 실제로 드는 비용과 다른 숫자가 된다.
        if (_activeTab == 1)
        {
            // 정보창은 공용이라 RefreshFocusExtras가 방금 채운 근접 무기 문구가 남는다
            // ("◆ 진화까지 3강" 같은 것이 파츠 탭에 그대로 보였다).
            if (_milestoneLabel != null) _milestoneLabel.text = "";
            SetRowCount(InfoRows);
            if (EnsureRateBreak() != null) _rateBreak.text = string.Empty;
            UIAffordGlow.Set(_enhanceBtn, false);
            RefreshPartInfo();
            return;
        }
        SetRowCount(WeaponRowCount);
        var rateBreak = EnsureRateBreak();

        // 무기 탭 복귀 — 예전엔 여기서 무조건 켰다. 무기가 없거나 최대치거나 재료가 모자라도 눌리는 버튼이라
        // 누르면 거절 문구만 뜨는 '고장난 버튼'이 됐다. 아래 분기가 조건을 만족할 때만 다시 켠다
        // (파츠 강화·원거리 강화 버튼과 같은 규약).
        if (_enhanceBtn != null) _enhanceBtn.interactable = false;

        var w = _controller.GetSlot(_targetSlot);
        if (w == null)
        {
            SetRowLabels(MeleeRowLabels);
            if (_successText != null)     _successText.text = "—";
            if (_costText != null)        _costText.text = "";
            if (_streakText != null)      _streakText.text = "";
            if (_eventEffectText != null) _eventEffectText.text = "";
            if (rateBreak != null)        rateBreak.text = string.Empty;
            SetEnhanceCost(0, true);
            UIAffordGlow.Set(_enhanceBtn, false);
            return;
        }

        bool maxed = !_controller.CanEnhance(_targetSlot);
        if (maxed)
        {
            SetRowLabels(MaxedRowLabels);
            if (_rateLabel != null) _rateLabel.text = "강화 단계";
            _successText.text = "<color=#8AB0D5>MAX</color>";
            _costText.text    = w.CanEvolve ? "<color=#C9A6FF>진화 대기</color>" : "<color=#8AB0D5>최대 강화 도달</color>";
            if (rateBreak != null) rateBreak.text = string.Empty;
            SetEnhanceCost(0, true);
            UIAffordGlow.Set(_enhanceBtn, false);
        }
        else
        {
            SetRowLabels(MeleeRowLabels);
            float chance = _controller.SuccessChanceAt(_targetSlot);
            int cost = _controller.CostAt(_targetSlot);
            int have = _controller.FuelAmount;
            // 해금·이벤트가 값을 하는 만큼 화면이 그걸 말해야 한다 — 성공률 바로 아래 한 줄에 무엇이 올렸는지(09-29).
            if (_rateLabel != null) _rateLabel.text = "성공률";
            if (rateBreak != null)  rateBreak.text  = RateBreakText(_targetSlot);
            float evBonus = _controller.EventSuccessBonus;
            _successText.text = evBonus > 0f ? $"<color=#FFB45A>{chance * 100f:F0}%</color>"
                              : evBonus < 0f ? $"<color=#E0806A>{chance * 100f:F0}%</color>"
                                             : $"{chance * 100f:F0}%";
            _costText.text = have >= cost
                ? $"{cost} <size=80%><color=#9A98A0>· 보유 {have}</color></size>"
                : $"<color=#FF5250>{cost}</color> <size=80%><color=#9A98A0>· 보유 {have}</color></size>";
            SetEnhanceCost(cost, have >= cost);

            // 연출 중에도 켜 둔다 — 재입력이 연출을 건너뛰고 다음 강화로 이어지는 경로(QueueWhileAnimating).
            if (_enhanceBtn != null) _enhanceBtn.interactable = have >= cost;
            UIAffordGlow.Set(_enhanceBtn, have >= cost);   // 누를 수 있을 때만 은은한 불(09-29)
        }

        // 연속 · 이벤트 효과는 성공률 아래 줄과 잭팟 줄에 붙었다 — 따로 떠 있던 두 줄은 비운다(09-29).
        if (_streakText != null)      _streakText.text = "";
        if (_eventEffectText != null) _eventEffectText.text = "";
    }

    private const string NoneMark = "<color=#8C8497>없음</color>";

    /// <summary>강화하기 버튼의 비용 칸. 비용이 없으면(최대 강화·파츠 탭) 아이콘째 감춘다.</summary>
    private void SetEnhanceCost(int cost, bool affordable)
    {
        if (_enhanceCost == null) return;
        bool show = cost > 0;
        _enhanceCost.gameObject.SetActive(show);
        if (!show) return;
        _enhanceCost.text  = cost.ToString();
        _enhanceCost.color = affordable ? CostInk : ShopUIStyle.RejectRed;
    }

    /// <summary>
    /// 파츠 탭 정보창. 확정 상승이라 성공률 칸에는 확률 대신 <b>무엇이 바뀌는지</b>를 쓴다.
    /// 개별 파츠 상세는 무대 우측 패널이 맡고, 여기서는 <b>조합의 총합</b>만 말한다 —
    /// 파츠를 여럿 끼웠을 때 "내 화살이 결국 몇 발 나가고 뭘 뚫는가"를 볼 자리가 그동안 없었다.
    /// </summary>
    private void RefreshPartInfo()
    {
        // 계산은 실제 발사 퍼널을 그대로 돌린다(표시용 수식을 따로 두면 코드가 바뀔 때 화면만 옛 값을 말한다).
        var req = ProjectileRequest.Create("preview", Vector3.zero, Vector3.forward, 100f, null,
                                           RangedParts.RangedSlot);
        RangedParts.Apply(ref req);

        int  shots  = Mathf.Max(1, req.count);
        bool hasExp = req.explodeRadius > 0f;
        bool hasHom = req.homingStrength > 0f;

        SetRowLabels(RangedRowLabels);
        if (_rateLabel != null) _rateLabel.text = "투사체 — 현재 조합의 총합";
        _successText.text   = $"{shots}발";
        _costText.text      = shots > 1 ? $"<color=#FFD24A>{req.spreadDeg:F0}°</color>" : NoneMark;
        _detailSuccess.text = req.pierce > 0 ? $"<color=#FFD24A>{req.pierce}회</color>" : NoneMark;
        _detailFail.text    = hasExp
            ? $"반경 <color=#FFD24A>{req.explodeRadius:F2}</color>"
              + $" <size=80%><color=#9A98A0>· 피해 {req.explodeDamageRatio * 100f:F0}%</color></size>"
            : NoneMark;
        _detailJackpot.text = hasHom
            ? $"<color=#FFD24A>{req.homingStrength:F0}</color>°/s"
              + (req.returnOnPierce ? " <size=80%><color=#7FE3FF>· 관통 후 복귀</color></size>" : "")
            : NoneMark;
        _milestoneLabel.text = $"피해 ×{req.damageMult:F2} · 크기 ×{req.sizeMult:F2}" + RangedSkillTierLine();
        _streakText.text     = "2번 키(원거리)로 쏠 때만 적용된다";
        SetEnhanceCost(0, true);

        // 강화 버튼은 무대 행 안으로 옮겼다 — 정보창의 버튼은 파츠 탭에서 쓰지 않는다.
        if (_eventEffectText != null)
            _eventEffectText.text = _controller.HasEvent ? $"↗ {_controller.EventEffectDesc}" : "";
    }

    /// <summary>
    /// 무기 타입·티어 라벨(카드 상단 디테일). 티어는 강화 상한(6/9/12/15)으로 추정.
    ///
    /// 두 예외를 둔다.
    ///  · <b>무형검</b>(근접 슬롯 · 진화 0단계): weaponType이 Katana라 그대로 찍으면 "카타나 · 티어 1"이 된다.
    ///    하지만 이 무기의 정체성은 <b>아직 형태가 정해지지 않았다</b>는 것 자체다 — 진화 전에는 타입을 말하지 않는다.
    ///  · <b>원거리</b>: 진화 트리가 없고 공용 파츠 강화로만 성장한다. 티어를 붙이면 없는 승급 체계를 암시한다.
    /// </summary>
    private string TypeTierLabel(WeaponData w, int max, int slotIndex)
    {
        if (w == null) return "";

        // 진화 전 주무기 = 무형검. 타입·티어를 숨긴다.
        if (slotIndex == PlayerWeaponManager.Slot0 && w.evolutionStage <= 0)
            return "무형 · 형태 없음";

        string type = w.weaponType switch
        {
            WeaponType.Katana     => "카타나",
            WeaponType.Greatsword => "대검",
            WeaponType.Bow        => "활",
            WeaponType.Crossbow   => "석궁",
            WeaponType.Staff      => "지팡이",
            _                     => w.weaponType.ToString(),
        };

        if (slotIndex != PlayerWeaponManager.Slot0) return type;   // 원거리 — 티어 개념 없음

        int tier = max <= 6 ? 1 : max <= 9 ? 2 : max <= 12 ? 3 : 4;
        return $"{type} · 티어 {tier}";
    }

    private void RefreshPromoteRow()
    {
        // legendId 승급행 폐기 예정 — 진화 패널(WeaponEvolutionSO)로 대체. 지금은 숨겨 레이아웃 정리.
        if (_promoteRow != null) _promoteRow.gameObject.SetActive(false);
    }

    private string LegendName(string legendId)
    {
        if (_controller?.Table != null && _controller.Table.TryGetLegend(legendId, out var l))
            return l.displayName;
        return legendId;
    }
}
