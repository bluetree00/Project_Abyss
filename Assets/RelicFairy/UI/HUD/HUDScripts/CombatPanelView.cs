//============================================================
// CombatPanelView.cs
// - HP 슬라이더/텍스트
// - 장비 슬롯 × 2 (아이콘)
// - Q/E 스킬 슬롯 (아이콘, 쿨다운)
// - Active 슬롯 × 3
// - 버프 목록 + 획득 알림
//============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public sealed class CombatPanelView : MonoBehaviour
{
    [Header("Status — HP")]
    [SerializeField] private Slider   hpSlider;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Image    hpFillImage;
    [SerializeField] private Image    hpGhostFillImage;

    [Header("HP 스킨 (선택 — 지정 시 플랫색 대신 아트 사용)")]
    [SerializeField] private Sprite hpFrameSprite;      // 체력바 테두리
    [SerializeField] private Sprite hpTrackSprite;      // 체력바 바탕(트랙)
    [SerializeField] private Sprite hpFillHighSprite;   // 체력바 초록(고체력)
    [SerializeField] private Sprite hpFillLowSprite;    // 체력바 빨강(저체력)
    [SerializeField, Range(0f, 1f)] private float hpFillSwapThreshold = 0.4f;

    // 아트 실측: 텍스처 3379×368 안에서 가장 안쪽 선이 x 76~3302 / y 165~276
    // → 안쪽 창 = 3225×111. 여백 비율 L·R 2.28% / T 44.84% / B 24.73%.
    //   위쪽에 장식이 몰려 있어 T가 압도적으로 크다 — 여길 20으로 두면 필이 위로 넘친다.
    // 패널 rect 650×71 기준 → (14.8, 17.6, 14.8, 31.8)
    [Tooltip("체력바 프레임 아트의 '안쪽 창'에 트랙/필을 맞추는 여백 (Left, Bottom, Right, Top) — 아트 실측값.")]
    [SerializeField] private Vector4 hpInnerPadding = new Vector4(15f, 18f, 15f, 32f);

    [Header("하단중앙 클러스터 위치 (체력바 · 유물게이지 · 스탯) — 화면 아래에서의 높이")]
    [Tooltip("체력바 Y. 낮출수록 클러스터 전체가 아래로 내려간다(스탯은 체력바에 붙어 함께 이동).")]
    [SerializeField] private float hpBarY    = 88f;
    [Tooltip("유물게이지(검) Y. 체력바보다 낮아야 겹치지 않는다.")]
    [SerializeField] private float relicBarY = 20f;

    [Header("HP 애니메이션")]
    [SerializeField] private float hpLerpSpeed   = 3f;
    [SerializeField] private float ghostDelay    = 0.35f;
    [SerializeField] private float ghostLerpSpeed = 1.2f;

    private Image _hpFrameImg;
    private bool HasHpSkin => hpTrackSprite != null || hpFillHighSprite != null || hpFillLowSprite != null;

    // 무기 슬롯(배경·테두리·아이콘 원형)은 <b>프리팹이 정본</b>이다 — @UIRoot/WeaponPanel 하위에
    // CellBg·Icon·CellFrame이 실제 오브젝트로 authoring 돼 있고, 코드는 아이콘 교체와
    // 활성 칸 강조만 담당한다(WeaponSlotUI). 런타임 레이아웃/스킨 생성은 하지 않는다.

    [Header("스킬 슬롯 스킨 — E/R/액티브 (선택)")]
    [SerializeField] private Sprite skillFrameSprite;       // er 액티브 테두리 (기본/쿨다운 중 — 흰색)
    [SerializeField] private Sprite skillInnerSprite;       // er 액티브 내부
    [SerializeField] private Sprite skillFrameReadySprite;  // er_2x (사용 가능 — 금색 장식)

    [Header("유물(Q) 슬롯 스킨 (선택)")]
    [SerializeField] private Sprite relicSlotFrameSprite;    // 유물칸 테두리
    [SerializeField] private Sprite relicSlotInnerSprite;    // 유물칸 내부
    [SerializeField] private Sprite relicSlotActiveSprite;   // 유물칸 활성화 (쿨다운 완료 시 스왑)

    [Header("유물 게이지 스킨 (선택)")]
    [SerializeField] private Sprite relicGaugeFrameSprite;   // 유물게이지 테두리 (검 실루엣 외곽)
    [SerializeField] private Sprite relicGaugeTrackSprite;   // 유물 게이지 검정
    [SerializeField] private Sprite relicGaugeFillSprite;    // 유물 게이지 보라 (평시)
    [SerializeField] private Sprite relicGaugeReadySprite;   // 유물 게이지 빨강 (절정/IsSkillReady)

    [Header("유물 태양 게이지 — 가웨인 전용 (선택)")]
    [Tooltip("충전중(열린 태양) — 중앙 공간으로 충전 게이지가 통과한다.")]
    [SerializeField] private Sprite sunOpenSprite;
    [Tooltip("완성(닫힌 태양) — 정오. 내부의 채움을 줄이며 유지 시간을 표현.")]
    [SerializeField] private Sprite sunClosedSprite;
    [Tooltip("충전 게이지 바 — 열린 태양 중앙을 가로지른다.")]
    [SerializeField] private Sprite sunGaugeSprite;
    private bool HasSunSkin => sunOpenSprite != null && sunClosedSprite != null;

    [Header("버프 셀 스킨 (선택)")]
    [SerializeField] private Sprite buffFrameSprite;   // 버프 테두리
    [SerializeField] private Sprite buffInnerSprite;   // 버프 내부

    [Header("버프 아이콘 글리프 (선택 — IconKey 매핑, 추정)")]
    [SerializeField] private Sprite buffIconSpeed;      // 버프 내용1 (날개)   → speed / atkspeed
    [SerializeField] private Sprite buffIconCooldown;   // 버프 내용2 (모래시계) → cooldown
    [SerializeField] private Sprite buffIconHeal;       // 버프 내용3 (꽃)     → heal
    [SerializeField] private Sprite buffIconLuck;       // 버프 내용4 (반지)   → luck

    [Header("스탯 아이콘 (선택)")]
    [SerializeField] private Sprite atkIconSprite;   // 공격력_1
    [SerializeField] private Sprite defIconSprite;   // 방어력_1

    private bool HasSkillSkin  => skillFrameSprite != null || skillInnerSprite != null;
    private bool HasRelicSlotSkin  => relicSlotFrameSprite != null || relicSlotInnerSprite != null;
    private bool HasRelicGaugeSkin => relicGaugeTrackSprite != null || relicGaugeFillSprite != null;

    // 스킨 런타임 참조
    private int   _activeWeaponIndex = 0;
    private Image _qFrameImg;         // 유물(Q) 슬롯 프레임(쿨다운 완료 시 활성 스프라이트 스왑)
    private Image _relicGaugeFill;    // 유물 게이지 fill(Filled/Horizontal)
    private bool  _hasWeaponEquipped; // 무기 슬롯 중 하나라도 장착됐는지(프레임 스왑용)

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI slot0;
    [SerializeField] private WeaponSlotUI slot1;

    [Header("Skill Slots — Q / E")]
    [SerializeField] private SkillSlotUI skillQ;
    [SerializeField] private SkillSlotUI skillE;

    [Header("Active Slots")]
    [SerializeField] private ActiveSlotUI[] activeSlots = new ActiveSlotUI[3];

    [Header("포션")]
    [SerializeField, Tooltip("포션 슬롯 아이콘. 비우면 아이콘 없이 개수만 표시.")]
    private Sprite potionIcon;

    [Header("스킬 아이콘 폴백 (임시 — 무기/유물 SO에 아이콘이 없을 때만 사용)")]
    [SerializeField] private Sprite skillQFallbackIcon;
    [SerializeField] private Sprite skillEFallbackIcon;
    [SerializeField] private Sprite skillRFallbackIcon;

    private const string PotionKeyLabel  = "C";
    private static readonly Color PotionCountColor = new(1f, 1f, 1f, 1f);
    private static readonly Color PotionEmptyColor = new(0.55f, 0.52f, 0.60f, 1f);
    private TMP_Text _potionCountLabel;

    [Header("Buff Display")]
    [SerializeField] private Transform buffListRoot;
    [SerializeField] private TMP_Text buffNoticeText;

    [Header("슬롯 레이블 폰트 (NotoSansKR 권장)")]
    [SerializeField] private TMP_FontAsset slotLabelFont;

    // ── HP 애니메이션 런타임 ──
    private float _hpTargetRatio;
    private float _hpDisplayRatio;
    private float _ghostRatio;
    private float _ghostTimer;
    private bool  _ghostActive;
    private bool  _hpInitialized;

    // ── R 스킬 슬롯 런타임 (프리팹에 없어 코드로 생성) ──
    private Image      _rIconImg;
    private GameObject _rCooldownBg;
    private Image      _rCooldownFill;   // R 차오름 필(E/Q와 동일 의미: fillAmount = remaining/total)
    private TMP_Text   _rCooldownTxt;

    private bool _qCooldownReady = true; // Q 사용가능 = 쿨다운 완료 AND 유물 게이지 조건(IsSkillReady)
    private bool _qHasSkill = true;      // 유물이 Q를 제공하는가(구조적 보유) — 프레젠터가 무기/유물 변경 시 갱신

    // E/R 슬롯 테두리 — 쿨다운 완료 시 금색(er_2x)으로 스왑
    private Image _eFrameImg;
    private Image _rFrameImg;

    // ── 스탯 표시 런타임 ──
    private TMP_Text _atkText;
    private TMP_Text _defText;
    private TMP_Text _hpMaxText;
    private GameObject _statRoot;
    private bool       _statSkinned;   // 스탯 행이 아트 스킨 배치(아이콘+숫자)인지 — 래거시(라벨+검은 띠)와 분기

    // ── 버프 그리드 UI 런타임 ──
    private GridLayoutGroup _buffGrid;                       // buffListRoot에 부착(아이콘+스택)
    private readonly List<BuffCell> _buffCells = new();      // 셀 풀(재사용)
    private GameObject _buffTooltip;                         // 재사용 툴팁 1개
    private TMP_Text   _buffTooltipText;
    private BuffCell   _hoveredCell;

    // 알림 만료는 '남은 시간 누산'이 아니라 <b>절대 시각(unscaled)</b>으로 잡는다.
    // 누산식은 Update가 도는 동안에만 줄어서, HUD 모드 전환(HudView.SetSections)으로
    // 이 패널 GO가 꺼지면 타이머가 얼어붙고 텍스트는 켜진 채 남았다 — 다시 켜질 때
    // 철 지난 알림이 되살아나고, 안 켜지면 그대로 잔류했다.
    // 절대 시각이면 꺼져 있던 동안에도 만료가 흘러가 재활성 즉시 사라진다.
    private float _noticeHideAt = -1f;   // <0 = 표시 중 아님

    // ── 버프창 도킹(좌측 중앙 — 원신/명조식, 주변시야 배치) ──
    // 그리드: 화면 왼쪽에서 오른쪽으로 늘고 위로 쌓임(유물 패시브=좌하단 첫 셀).
    // 잔여 시간은 칸 위 스윕(BuffCell)이 직접 그린다 — 아래로 막대를 깔 세로 여유가 없다.
    // 목업 기준(1920×1080): 좌하단에서 좌 114 / 아래 275. 무기 패널(위쪽 끝 244) 위, 서약 박스(아래쪽 끝 337) 아래.
    private const float BuffDockX       = 114f;
    private const float BuffDockBottomY = 275f;

    // ── 캐릭터 HUD 레이아웃(원신/명조식): HP 하단중앙 · 스킬 우하단 2포드(유물 Q / 무기 E·R) ──
    private static readonly Color RelicColor  = new Color(1.00f, 0.80f, 0.30f, 1f);  // 유물=금
    private static readonly Color WeaponColor = new Color(0.45f, 0.70f, 1.00f, 1f);  // 무기=강철청

    // HP fill 색: 가득=초록 → 절반=노랑 → 위험=빨강 (전주의적 위험 신호)
    private static readonly Color HpColorFull = new Color(0.30f, 0.82f, 0.35f, 1f);
    private static readonly Color HpColorMid  = new Color(0.95f, 0.78f, 0.20f, 1f);
    private static readonly Color HpColorLow  = new Color(0.90f, 0.22f, 0.20f, 1f);
    private bool _layoutBuilt;
    private RectTransform _hpBar;
    private RectTransform _relicPod;
    private RectTransform _weaponPod;

    // 유물 전용 아이덴티티 바(체력바 아래) — 활성 유물 IRelicResource 표시
    private RectTransform _relicBar;
    private Image         _relicBarFill;
    private Outline       _relicBarGlow;   // 게이지 맥동 연출
    private TMP_Text      _relicBarLabel;
    private IRelicResource _relicResource;
    private System.Action  _relicChanged;
    private bool           _lastSkillReady;  // 절정(정오 등) 진입 엣지
    private float          _relicFlash;      // 진입 플래시(1→0)

    // 가웨인 태양 게이지(Style==Sun 전용) — 수평 바 대신 사용. 열린 태양(충전)+게이지 / 닫힌 태양(정오)+내부 감소.
    private RectTransform _sunRoot;
    private Image         _sunOpenImg;     // 열린 태양(충전 중)
    private RectTransform _sunGaugeRoot;   // 충전 게이지 바(열린 태양 중앙 통과)
    private Image         _sunGaugeFill;   // 충전 게이지 fill(Filled/Horizontal)
    private Image         _sunClosedImg;   // 닫힌 태양(정오) — 어두운 베이스
    private Image         _sunClosedFill;  // 닫힌 태양 내부 채움(Filled/Radial360, 정오 유지 1→0)
    private TMP_Text      _sunLabel;
    private bool          _useSunGauge;    // 현재 바인딩된 리소스가 태양형인지

    // 무기 슬롯 활성 강조

    // 툴팁 화면 클램프용 코너 버퍼(재사용 — 호버 시 GC 억제).
    private static readonly Vector3[] _tooltipCorners = new Vector3[4];

    // ─────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────
    public void SetHp(int hp, int maxHp)
    {
        int clampedMax = Mathf.Max(1, maxHp);
        int clampedHp  = Mathf.Clamp(hp, 0, clampedMax);
        float newRatio = (float)clampedHp / clampedMax;

        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = clampedMax;
            // value는 UpdateHpAnimation에서 부드럽게 갱신
        }

        if (!_hpInitialized)
        {
            _hpTargetRatio  = newRatio;
            _hpDisplayRatio = newRatio;
            _ghostRatio     = newRatio;
            _hpInitialized  = true;
            ApplyHpFill(newRatio);
        }
        else
        {
            if (newRatio < _hpTargetRatio - 0.001f)
            {
                _ghostRatio  = _hpDisplayRatio;
                _ghostTimer  = ghostDelay;
                _ghostActive = true;
            }
            _hpTargetRatio = newRatio;
        }

        if (hpText != null)
            hpText.text = $"{clampedHp} / {clampedMax}";
    }

    private void ApplyHpFill(float ratio)
    {
        if (hpSlider != null)
            hpSlider.value = ratio * hpSlider.maxValue;

        if (hpFillImage != null)
        {
            hpFillImage.fillAmount = ratio;
            if (HasHpSkin)
            {
                // 스킨: 색 틴트 대신 고/저체력 fill 스프라이트 스왑(둘 다 지정 시)
                if (hpFillHighSprite != null && hpFillLowSprite != null)
                    hpFillImage.sprite = ratio <= hpFillSwapThreshold ? hpFillLowSprite : hpFillHighSprite;
            }
            else
            {
                hpFillImage.color = HpColorFor(ratio);   // 초록(가득)→노랑→빨강(위험)
            }
        }

        if (hpGhostFillImage != null)
            hpGhostFillImage.fillAmount = Mathf.Max(_ghostRatio, ratio);
    }

    /// <summary>HP 비율에 따른 fill 색: 1.0 초록 → 0.5 노랑 → 0.0 빨강.</summary>
    private static Color HpColorFor(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        return ratio > 0.5f
            ? Color.Lerp(HpColorMid, HpColorFull, (ratio - 0.5f) * 2f)
            : Color.Lerp(HpColorLow, HpColorMid, ratio * 2f);
    }

    /// <summary>체력바의 늘어난 장식 스프라이트를 제거해 단색 플랫 바로 정리(억지 스트레치 방지). fill 색은 ApplyHpFill이 담당.</summary>
    private void CleanHpBarVisual()
    {
        if (HasHpSkin) { ApplyHpSkin(); return; }   // 스킨 지정 시 아트 적용(플랫색 스킵)

        if (_hpBar != null && FindChildRecursive(_hpBar, "Background") is RectTransform bgRT
            && bgRT.TryGetComponent<Image>(out var bgImg))
        {
            bgImg.sprite = null;
            bgImg.type   = Image.Type.Simple;
            bgImg.color  = new Color(0f, 0f, 0f, 0.55f);   // 어두운 반투명 트랙
        }
        if (hpFillImage != null)
        {
            hpFillImage.sprite = null;   // 늘어난 fill 아트 → 단색(슬라이더가 폭 제어)
            hpFillImage.type   = Image.Type.Simple;
        }
        if (hpText != null) hpText.fontSize = 11f;   // 얇은 바에 맞춘 소형 수치
    }

    /// <summary>디자이너 아트로 체력바 스킨 적용: 트랙(바탕) + fill(초록/빨강) + 테두리 오버레이.</summary>
    private void ApplyHpSkin()
    {
        // 트랙(배경) — 프레임 안쪽 창에 맞춰 인셋(넘침 방지)
        if (hpTrackSprite != null && _hpBar != null && FindChildRecursive(_hpBar, "Background") is RectTransform bgRT
            && bgRT.TryGetComponent<Image>(out var bgImg))
        {
            bgImg.sprite = hpTrackSprite;
            bgImg.type   = Image.Type.Sliced;
            bgImg.color  = Color.white;
            InsetInside(bgRT, hpInnerPadding);
        }
        // fill(가로 채움) — 색 틴트 제거, 스프라이트는 ApplyHpFill이 체력별로 스왑
        if (hpFillImage != null)
        {
            hpFillImage.type       = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.color      = Color.white;
            if (hpFillHighSprite != null) hpFillImage.sprite = hpFillHighSprite;

            // Fill Area(슬라이더 컨테이너)를 인셋 — Slider는 fillRect의 '앵커'만 구동하므로
            // 컨테이너를 줄이면 필이 프레임 창 안에 정확히 갇힌다.
            var fillArea = hpSlider != null && hpSlider.fillRect != null
                ? hpSlider.fillRect.parent as RectTransform : null;
            if (fillArea != null) InsetInside(fillArea, hpInnerPadding);
            else                  InsetInside(hpFillImage.rectTransform, hpInnerPadding);
        }

        // 감소 잔상(고스트)은 <b>본 fill과 같은 스프라이트</b>를 써야 한다.
        // 같은 rect라도 스프라이트가 다르면 아트 내부의 바 두께·여백이 달라 잔상만 굵거나 얇게 보인다
        // (레거시 Hp_bar.png는 1476×528, 디자이너 fill 아트는 3233×120 — 비율 자체가 다르다).
        // 색은 유지 — 잔상은 틴트로 구분한다.
        if (hpGhostFillImage != null && hpFillHighSprite != null)
        {
            hpGhostFillImage.sprite     = hpFillHighSprite;
            hpGhostFillImage.type       = Image.Type.Filled;
            hpGhostFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpGhostFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
        // 테두리 오버레이(fill 위) — 1회 생성
        if (hpFrameSprite != null && _hpBar != null && _hpFrameImg == null)
        {
            var f   = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            var frt = (RectTransform)f.transform;
            frt.SetParent(_hpBar, false);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            _hpFrameImg = f.GetComponent<Image>();
            _hpFrameImg.sprite        = hpFrameSprite;
            _hpFrameImg.type          = Image.Type.Sliced;
            _hpFrameImg.raycastTarget = false;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 디자이너 아트 스킨 (슬롯 / 유물 게이지 / 스탯 아이콘)
    // 스프라이트 미지정 시 아무 동작도 하지 않아 기존 절차 생성 외형이 그대로 유지된다(비파괴).
    // ─────────────────────────────────────────────────────────

    private const float FrameOverhang = 1.18f;   // 테두리 아트가 슬롯을 감싸도록 하는 여유 배율

    /// <summary>슬롯·게이지·스탯 아이콘에 디자이너 아트를 입힌다(Awake 말미 1회).</summary>
    private void ApplySlotSkins()
    {
        ApplyRelicSlotSkin();
        ApplySkillSkin();
        ApplyRelicGaugeSkin();
        ApplyStatIconSkin();
    }

    /// <summary>부모를 꽉 채우되 (L,B,R,T) 만큼 안쪽으로 들여 배치. 프레임 아트의 '안쪽 창' 정합용.</summary>
    private static void InsetInside(RectTransform rt, Vector4 pad)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad.x, pad.y);     // left, bottom
        rt.offsetMax = new Vector2(-pad.z, -pad.w);   // -right, -top
    }

    /// <summary>
    /// Q(유물) 슬롯 활성 아트 판단 — <b>쿨다운 완료 AND 유물 게이지 조건(IRelicResource.IsSkillReady)</b>
    /// 둘 다 만족해야 '사용 가능'이므로 두 조건을 합쳐 테두리를 스왑한다.
    /// 유물 리소스가 없는 경우(게이지 게이팅 없음)엔 쿨다운만으로 판단.
    /// </summary>
    private void RefreshQFrame()
    {
        bool gaugeReady = _relicResource == null || _relicResource.IsSkillReady;

        // Q는 '사용할 수 있을 때만' 열어둔다 — 스킬 자체가 없거나 게이지 조건(정오 등)이 닫혀 있으면 잠금.
        // 쿨다운은 전용 연출이 따로 있으므로 잠금 조건에서 제외한다.
        // Q 잠금의 단일 소유자가 여기다(게이지가 매 프레임 변하므로). 프레젠터는 _qHasSkill만 갱신한다.
        skillQ?.SetLocked(!_qHasSkill || !gaugeReady, GetSafeFont());

        if (_qFrameImg == null || relicSlotActiveSprite == null) return;

        bool ready = _qCooldownReady && gaugeReady;

        var target = ready ? relicSlotActiveSprite : relicSlotFrameSprite;
        if (target != null && _qFrameImg.sprite != target)
            _qFrameImg.sprite = target;
    }

    /// <summary>
    /// "지금 든 무기" 강조 — 칸 배경/테두리는 <b>프리팹이 authoring한 실제 오브젝트</b>이고,
    /// 코드는 밝기만 토글한다(활성 칸=밝게, 비활성=옅게, 미장착=더 어둡게).
    /// </summary>
    private void RefreshWeaponFrames()
    {
        slot0?.SetSelected(_hasWeaponEquipped && _activeWeaponIndex == 0);
        slot1?.SetSelected(_hasWeaponEquipped && _activeWeaponIndex == 1);
    }

    /// <summary>유물(Q) 슬롯: 마름모 내부 + 테두리. 쿨다운 완료 시 활성 테두리로 스왑.</summary>
    private void ApplyRelicSlotSkin()
    {
        if (!HasRelicSlotSkin) return;
        if (FindChildRecursive(transform, "HUD_QSkile") is not RectTransform q) return;

        AddSkinLayer(q, "SkinBg", relicSlotInnerSprite, false);
        _qFrameImg = AddSkinLayer(q, "SkinFrame", relicSlotFrameSprite, true,
            FrameSizeFor(q, relicSlotFrameSprite, FrameOverhang));
    }

    /// <summary>E/R 스킬 + 액티브 아이템 슬롯: 내부 + 테두리.</summary>
    private void ApplySkillSkin()
    {
        if (!HasSkillSkin) return;

        // E/R 프레임은 캐싱 — 쿨다운 완료 시 금색(사용가능) 아트로 스왑한다.
        _eFrameImg = SkinSquareSlot(FindChildRecursive(transform, "HUD_ESkile") as RectTransform);
        _rFrameImg = SkinSquareSlot(FindChildRecursive(transform, "HUD_RSkile") as RectTransform);
        SkinSquareSlot(FindChildRecursive(transform, "HUD_Active_01") as RectTransform);
        SkinSquareSlot(FindChildRecursive(transform, "HUD_Active_02") as RectTransform);
        SkinSquareSlot(FindChildRecursive(transform, "HUD_Active_03") as RectTransform);
    }

    /// <summary>E/R 슬롯 테두리 스왑: 사용 가능=금색(er_2x) / 쿨다운 중=기본(흰색).</summary>
    private void SetSkillFrameReady(Image frame, bool ready)
    {
        if (frame == null) return;
        var target = (ready && skillFrameReadySprite != null) ? skillFrameReadySprite : skillFrameSprite;
        if (target != null && frame.sprite != target)
            frame.sprite = target;
    }

    /// <summary>사각 슬롯(E/R/액티브)에 내부+테두리 아트를 얹고, 테두리 Image를 반환(상태 스왑용).</summary>
    private Image SkinSquareSlot(RectTransform slot)
    {
        if (slot == null) return null;

        // 슬롯 본체에 단색 Image가 있으면(R 슬롯 등) 내부 아트로 교체, 없으면 배경 레이어 추가.
        if (skillInnerSprite != null && slot.TryGetComponent<Image>(out var body))
        {
            body.sprite = skillInnerSprite;
            body.type   = Image.Type.Sliced;
            body.color  = Color.white;
        }
        else
        {
            AddSkinLayer(slot, "SkinBg", skillInnerSprite, false);
        }

        return AddSkinLayer(slot, "SkinFrame", skillFrameSprite, true,
            FrameSizeFor(slot, skillFrameSprite, FrameOverhang));
    }

    /// <summary>
    /// 유물 아이덴티티 바: 검 실루엣 아트로 교체.
    /// 폭(anchorMax.x) 방식은 검 모양을 가로로 찌그러뜨리므로, 스킨 시 Filled/Horizontal(fillAmount)로 전환한다.
    /// </summary>
    private void ApplyRelicGaugeSkin()
    {
        if (!HasRelicGaugeSkin || _relicBar == null || _relicBarFill == null) return;

        // 검 실루엣 비율(약 7:1)에 맞춰 바 높이를 키운다(기본 360×14 → 과하게 눌림).
        // 크기/위치는 CreateRelicBar가 스킨 기준(380×60, y=40)으로 이미 잡음 — 여기서 덮어쓰지 않는다.

        // 트랙(바 본체)
        if (_relicBar.TryGetComponent<Image>(out var track) && relicGaugeTrackSprite != null)
        {
            track.sprite = relicGaugeTrackSprite;
            track.type   = Image.Type.Simple;
            track.color  = Color.white;
        }

        // fill: 폭 대신 fillAmount로 채운다(rect는 트랙 전체로 스트레치).
        var frt = _relicBarFill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
        _relicBarFill.type       = Image.Type.Filled;
        _relicBarFill.fillMethod = Image.FillMethod.Horizontal;
        _relicBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _relicBarFill.color      = Color.white;
        if (relicGaugeFillSprite != null) _relicBarFill.sprite = relicGaugeFillSprite;
        _relicGaugeFill = _relicBarFill;

        // 게이지 외곽 테두리(검 실루엣 라인)
        AddSkinLayer(_relicBar, "SkinFrame", relicGaugeFrameSprite, true);

        // 라벨이 검 실루엣에 묻히지 않도록 바 위쪽으로 뺀다.
        if (_relicBarLabel != null)
        {
            var lrt = _relicBarLabel.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot     = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(0f, 0f);
            lrt.offsetMax = new Vector2(0f, 14f);
        }
    }

    /// <summary>ATK/DEF 텍스트 좌측에 검·방패 아이콘 배치.</summary>
    private void ApplyStatIconSkin()
    {
        float size = _statSkinned ? 32f : 16f;   // 목업 아이콘은 32px — 16px는 절반 이하라 눈에 안 띈다
        AddStatIcon(_atkText, atkIconSprite, size);
        AddStatIcon(_defText, defIconSprite, size);
    }

    private static void AddStatIcon(TMP_Text label, Sprite icon, float size)
    {
        if (label == null || icon == null) return;

        var go = new GameObject("StatIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(label.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot     = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-2f, 0f);
        rt.sizeDelta = new Vector2(size, size);

        var img = go.GetComponent<Image>();
        img.sprite         = icon;
        img.preserveAspect = true;
        img.raycastTarget  = false;
    }

    /// <summary>슬롯 아래/위에 스킨 이미지 레이어를 1회 생성(이름으로 재사용). size가 0이면 부모에 스트레치.</summary>
    private static Image AddSkinLayer(Transform parent, string name, Sprite sprite, bool onTop, Vector2 size = default)
    {
        if (parent == null || sprite == null) return null;

        var existing = parent.Find(name);
        var go = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        if (size == Vector2.zero)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        // ⚠️ 부모에 LayoutGroup(Grid/Horizontal/Vertical)이 있으면 이 스킨 레이어가 '레이아웃 아이템'으로
        //    끼어들어 배경이 아니라 셀처럼 찌그러진다. 반드시 레이아웃에서 제외한다.
        if (!go.TryGetComponent<LayoutElement>(out var le)) le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        if (!go.TryGetComponent<Image>(out var img)) img = go.AddComponent<Image>();
        img.sprite        = sprite;
        img.type          = Image.Type.Sliced;
        img.color         = Color.white;
        img.raycastTarget = false;

        if (onTop) rt.SetAsLastSibling();
        else       rt.SetAsFirstSibling();

        return img;
    }

    /// <summary>테두리 아트가 슬롯을 감싸도록, 아트 비율을 유지한 채 부모를 덮는 rect 크기를 구한다(찌그러짐 방지).</summary>
    private static Vector2 FrameSizeFor(RectTransform parent, Sprite art, float overhang)
    {
        if (parent == null || art == null) return Vector2.zero;

        float aspect = art.rect.height > 0f ? art.rect.width / art.rect.height : 1f;
        Vector2 p = parent.rect.size * overhang;
        if (p.x <= 0f || p.y <= 0f) return Vector2.zero;

        float w = Mathf.Max(p.x, p.y * aspect);
        float h = w / Mathf.Max(0.0001f, aspect);
        return new Vector2(w, h);
    }

    private void UpdateHpAnimation()
    {
        float dt = Time.deltaTime;
        _hpDisplayRatio = Mathf.MoveTowards(_hpDisplayRatio, _hpTargetRatio, hpLerpSpeed * dt);

        if (_ghostActive)
        {
            if (_ghostTimer > 0f)
            {
                _ghostTimer -= dt;
            }
            else
            {
                _ghostRatio = Mathf.MoveTowards(_ghostRatio, _hpTargetRatio, ghostLerpSpeed * dt);
                if (Mathf.Abs(_ghostRatio - _hpTargetRatio) < 0.002f)
                {
                    _ghostRatio  = _hpTargetRatio;
                    _ghostActive = false;
                }
            }
        }

        ApplyHpFill(_hpDisplayRatio);
    }

    // ─────────────────────────────────────────────────────────
    // 장비 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetWeaponSlot(int index, WeaponSlotInfo info)
    {
        var ui = index == 0 ? slot0 : index == 1 ? slot1 : null;
        ui?.Apply(info);

        if (index == 0 || index == 1)
        {
            _slotHasWeapon[index] = info.HasWeapon;
            _hasWeaponEquipped    = _slotHasWeapon[0] || _slotHasWeapon[1];
            RefreshWeaponFrames();   // 스킨 시 활성/비활성 테두리 스왑(미지정이면 no-op)
        }
    }

    private readonly bool[] _slotHasWeapon = new bool[2];

    // ─────────────────────────────────────────────────────────
    // 스킬 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetSkillIcon(SkillType skill, Sprite icon)
    {
        // [임시] 무기/유물 SO에 스킬 아이콘이 아직 없어 슬롯이 비어 보인다.
        //        아트가 들어오면 폴백 필드를 비우기만 하면 원래대로 동작한다.
        if (icon == null)
        {
            icon = skill switch
            {
                SkillType.Q => skillQFallbackIcon,
                SkillType.E => skillEFallbackIcon,
                SkillType.R => skillRFallbackIcon,
                _           => null,
            };
        }

        if (skill == SkillType.R)
        {
            if (_rIconImg != null)
            {
                _rIconImg.sprite = icon;
                _rIconImg.gameObject.SetActive(icon != null);
            }
            return;
        }
        GetSkillSlot(skill)?.SetIcon(icon);
    }

    public void SetSkillCooldown(SkillType skill, float remaining, float total)
    {
        bool ready = remaining <= 0.05f;

        if (skill == SkillType.R)
        {
            bool onCd = !ready;
            if (_rCooldownBg != null) _rCooldownBg.SetActive(onCd);
            if (_rCooldownFill != null)
                _rCooldownFill.fillAmount = (onCd && total > 0f) ? remaining / total : 0f;
            if (_rCooldownTxt != null)
                _rCooldownTxt.text = onCd ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
            SetSkillFrameReady(_rFrameImg, ready);   // 사용가능 → 금색 테두리
            return;
        }

        // 유물(Q): 쿨다운 상태만 기록하고, 활성 아트 판단은 게이지 조건까지 합쳐 RefreshQFrame이 결정.
        if (skill == SkillType.Q)
        {
            _qCooldownReady = ready;
            RefreshQFrame();
        }
        else if (skill == SkillType.E)
        {
            SetSkillFrameReady(_eFrameImg, ready);   // 사용가능 → 금색 테두리
        }

        GetSkillSlot(skill)?.SetCooldown(remaining, total);
    }

    private SkillSlotUI GetSkillSlot(SkillType skill) => skill switch
    {
        SkillType.Q => skillQ,
        SkillType.E => skillE,
        _           => null,
    };

    /// <summary>
    /// 스킬 슬롯 잠금 표시. 해당 슬롯에 스킬이 없으면(예: 무형검) 어둡게 덮고 자물쇠를 띄운다.
    /// 무기 진화로 스킬이 생기면 같은 경로가 locked=false로 다시 불려 자동 해제된다.
    /// R 슬롯은 별도 이미지 구조라 프레임/쿨다운 표시만 정리한다.
    /// </summary>
    public void SetSkillLocked(SkillType skill, bool locked)
    {
        // Q의 잠금은 RefreshQFrame이 게이지와 함께 매 갱신마다 결정한다(여기선 보유 여부만 반영).
        if (skill == SkillType.Q)
        {
            _qHasSkill = !locked;
            RefreshQFrame();
            return;
        }

        if (skill == SkillType.R)
        {
            // R은 Q/E와 UI 구조가 달라(별도 이미지) 프레임을 어둡게 죽이는 것으로 통일한다.
            // 이모지 자물쇠는 폰트에 글리프가 없으면 네모로 깨지므로 쓰지 않는다.
            if (_rCooldownBg  != null) _rCooldownBg.SetActive(false);
            if (_rCooldownTxt != null) _rCooldownTxt.text = string.Empty;
            if (_rFrameImg    != null)
            {
                var c = _rFrameImg.color;
                _rFrameImg.color = new Color(c.r, c.g, c.b, locked ? 0.4f : 1f);
            }
            return;
        }

        GetSkillSlot(skill)?.SetLocked(locked, GetSafeFont());
    }

    // ─────────────────────────────────────────────────────────
    // Active 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetActiveSlot(int index, Sprite icon)
    {
        if (index >= 0 && index < activeSlots.Length)
            activeSlots[index]?.SetIcon(icon);
    }

    // ─────────────────────────────────────────────────────────
    // 포션 슬롯 — Q/E 위쪽 액티브 슬롯 첫 칸(HUD_Active_01)을 쓴다.
    // 라벨은 사용 키(C), 우측에 남은 개수를 표시한다.
    // ─────────────────────────────────────────────────────────

    private const int PotionSlotIndex = 0;

    /// <summary>포션 보유 갱신. PlayerRunState.OnPotionChanged → HudPresenter가 호출.</summary>
    public void SetPotion(int count, int capacity)
    {
        if (activeSlots == null || activeSlots.Length <= PotionSlotIndex) return;
        var slot = activeSlots[PotionSlotIndex];
        if (slot == null) return;

        if (potionIcon != null) slot.SetIcon(potionIcon);

        // 코너 라벨은 키(C) 고정 — 개수는 별도 라벨로 크게 보여준다.
        EnsureActiveLabel(slot, PotionKeyLabel);

        EnsurePotionCountLabel();
        if (_potionCountLabel != null)
        {
            // 현재개수/용량("2/3") — 용량이 0(포션 미보유 체계)이면 개수만.
            _potionCountLabel.text  = capacity > 0 ? $"{count}/{capacity}" : count.ToString();
            // 0개면 흐리게 — 눌러도 안 나간다는 걸 색으로 먼저 알린다.
            _potionCountLabel.color = count > 0 ? PotionCountColor : PotionEmptyColor;
        }
    }

    private void EnsurePotionCountLabel()
    {
        if (_potionCountLabel != null) return;
        var slotGo = FindChildRecursive(transform, "HUD_Active_01");
        if (slotGo == null) return;

        // 개수는 좌상단 모서리 — 아이콘(가운데)·키 라벨(우하단)과 자리를 나눠 겹치지 않게 한다.
        // 키 라벨은 StyleKeyLabel이 모든 슬롯을 우하단(KeyLabelPivot)으로 정규화하므로,
        // 자리를 비켜주는 쪽은 포션에만 있는 개수 라벨이다. 우하단에 두면 18pt 볼드 개수가
        // 12pt 키(C)를 덮어 "무슨 키로 먹는지" 표시가 사라진다.
        var go = new GameObject("PotionCount", typeof(RectTransform));
        go.transform.SetParent(slotGo, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(3f, -2f);
        rt.sizeDelta = new Vector2(40f, 22f);

        _potionCountLabel = go.AddComponent<TextMeshProUGUI>();
        if (slotLabelFont != null) _potionCountLabel.font = slotLabelFont;
        _potionCountLabel.fontSize  = 18f;
        _potionCountLabel.fontStyle = FontStyles.Bold;
        _potionCountLabel.alignment = TextAlignmentOptions.TopLeft;
        _potionCountLabel.raycastTarget = false;
        var ol = go.AddComponent<UnityEngine.UI.Outline>();   // 아이콘 위에서도 읽히게 외곽선
        ol.effectColor    = new Color(0f, 0f, 0f, 0.9f);
        ol.effectDistance = new Vector2(1f, -1f);
    }

    // ─────────────────────────────────────────────────────────
    // 무기 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class WeaponSlotUI
    {
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private Image      iconImage;

        [Header("프리팹 authoring (칸 배경 / 장식 테두리)")]
        [Tooltip("칸 배경(바탕). 활성 무기 칸을 밝게 표시한다.")]
        [SerializeField] private Image cellBg;
        [Tooltip("칸 장식 테두리(근거리/원거리 아트). 활성 무기 칸을 밝게 표시한다.")]
        [SerializeField] private Image cellFrame;

        [Header("무기 타입 기본 아이콘 (WeaponSO icon이 없을 때 폴백)")]
        [SerializeField] private Sprite iconKatana;
        [SerializeField] private Sprite iconSword;
        [SerializeField] private Sprite iconBow;
        [SerializeField] private Sprite iconCrossbow;

        public void Apply(WeaponSlotInfo info)
        {
            SetActive(emptyRoot, !info.HasWeapon);

            if (iconImage != null)
            {
                if (info.HasWeapon)
                {
                    // 무기 타입 기본 아이콘을 먼저 쓴다. WeaponSO의 개별 아이콘을 우선하면
                    // 진화할 때마다 HUD 칸 그림이 제각각으로 바뀌어 무기 계열이 안 읽힌다.
                    // 진화별 아이콘이 갖춰지면 우선순위를 되돌리면 된다(줄 하나).
                    Sprite typeIcon = ResolveTypeIcon(info.Type);
                    Sprite resolved  = typeIcon != null ? typeIcon : info.Icon;
                    iconImage.sprite = resolved;
                    iconImage.gameObject.SetActive(resolved != null);
                }
                else
                {
                    iconImage.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 현재 든 무기 칸을 밝기로 강조한다. 배경·테두리 오브젝트는 프리팹이 정본이라
        /// 여기서는 <b>색(밝기)만</b> 건드린다(생성·배치 없음).
        /// </summary>
        internal void SetSelected(bool selected)
        {
            var c = selected ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            if (cellBg    != null) cellBg.color    = c;
            if (cellFrame != null) cellFrame.color = c;
        }

        private Sprite ResolveTypeIcon(WeaponType type) => type switch
        {
            WeaponType.Katana     => iconKatana,
            WeaponType.Greatsword => iconSword,
#pragma warning disable CS0618
            WeaponType.Sword      => iconSword,
#pragma warning restore CS0618
            WeaponType.Bow        => iconBow,
            WeaponType.Crossbow   => iconCrossbow,
            _                     => null,
        };

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 스킬 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class SkillSlotUI
    {
        [SerializeField] private Image      iconImage;
        [SerializeField] private GameObject cooldownBg;
        [SerializeField] private TMP_Text   cooldownText;
        /// <summary>선택: Radial360 FillMethod 설정된 Image — 쿨다운 진행 오버레이.</summary>
        [SerializeField] private Image      cooldownOverlay;
        /// <summary>슬롯 우측 하단 키 레이블 (Q / E). Inspector 또는 런타임 생성.</summary>
        [SerializeField] internal TMP_Text  keyLabel;

        /// <summary>스킬을 쓸 수 없는 상태인가 — 아이콘을 어둡게 죽여 표시한다.</summary>
        private bool _locked;

        public bool IsLocked => _locked;

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
            // 잠금 중이면 새 아이콘도 어둡게 유지(SetIcon이 SetLocked보다 늦게 와도 톤이 안 튄다).
            iconImage.color = _locked ? new Color(0.30f, 0.30f, 0.34f, 0.85f) : Color.white;
        }

        /// <summary>
        /// 슬롯 잠금 표시. 스킬이 없는 슬롯(예: 무형검의 E/R)을 어둡게 덮고 자물쇠를 띄운다.
        /// 무기가 진화해 스킬이 생기면 <b>같은 경로가 false로 다시 호출</b>돼 자동 해제된다.
        /// </summary>
        /// <summary>
        /// 슬롯 잠금 표시. <b>아이콘 자체를 어둡게 죽이는</b> 방식 —
        /// 슬롯을 덮는 베일을 만들면 Q(유물칸)처럼 프레임 모양이 다른 슬롯에서 배경과 어긋난다.
        /// 아이콘만 건드리므로 어떤 프레임 아트에도 안전하다.
        /// </summary>
        internal void SetLocked(bool locked, TMP_FontAsset font)
        {
            _locked = locked;

            if (iconImage != null)
            {
                // 아이콘은 계속 보이되 어둡게 — 슬롯이 비어 보이지 않으면서 "못 쓴다"가 읽힌다.
                iconImage.gameObject.SetActive(iconImage.sprite != null);
                iconImage.color = locked ? new Color(0.30f, 0.30f, 0.34f, 0.85f) : Color.white;
            }

            if (keyLabel != null)
            {
                var c = keyLabel.color;
                keyLabel.color = new Color(c.r, c.g, c.b, locked ? 0.35f : 1f);
            }

            // 잠긴 슬롯엔 쿨다운 연출이 남아있으면 안 된다.
            if (locked)
            {
                if (cooldownBg      != null) cooldownBg.SetActive(false);
                if (cooldownOverlay != null) cooldownOverlay.gameObject.SetActive(false);
                if (cooldownText    != null) cooldownText.text = string.Empty;
            }
        }

        /// <summary>
        /// 쿨다운 <b>차오름 필</b>과 <b>숫자</b>를 확보한다.
        /// 프리팹의 skillQ/skillE는 cooldownOverlay·cooldownText가 <b>모두 미할당(null)</b>이고,
        /// cooldownBg도 슬롯 구석의 28×16 작은 뱃지로 배치돼 있어 R처럼 도는 연출이 안 보였다.
        /// → 슬롯 전체를 덮는 어두운 베일로 정규화하고 숫자를 중앙에 두어 R 슬롯과 동일하게 맞춘다.
        /// </summary>
        internal void EnsureCooldownVisuals(Sprite fillSprite, TMP_FontAsset font)
        {
            if (cooldownBg == null) return;

            // 슬롯 전체를 덮도록 스트레치 — 프리팹 값(구석 뱃지)은 R과 달라 스윕이 보이지 않는다.
            var bgRT = (RectTransform)cooldownBg.transform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            // 쿨다운 배경 Image 자체를 Radial360 Filled로 전환 → 시계방향 스윕(R 슬롯과 동일 방식)
            if (cooldownOverlay == null && cooldownBg.TryGetComponent<Image>(out var bgImg))
            {
                bgImg.raycastTarget = false;
                bgImg.color         = new Color(0f, 0f, 0f, 0.65f);   // R과 동일한 어두운 베일
                if (fillSprite != null)
                {
                    bgImg.sprite        = fillSprite;
                    bgImg.type          = Image.Type.Filled;
                    bgImg.fillMethod    = Image.FillMethod.Radial360;
                    bgImg.fillOrigin    = (int)Image.Origin360.Top;
                    bgImg.fillClockwise = true;
                }
                cooldownOverlay = bgImg;
            }

            // 남은 시간 숫자 — 베일 위 중앙
            if (cooldownText == null)
            {
                var go = new GameObject("CooldownText", typeof(RectTransform));
                go.transform.SetParent(cooldownBg.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                var tmp = go.AddComponent<TextMeshProUGUI>();
                if (font != null) tmp.font = font;
                tmp.fontSize      = 18f;
                tmp.fontStyle     = FontStyles.Bold;
                tmp.alignment     = TextAlignmentOptions.Center;
                tmp.color         = Color.white;
                tmp.raycastTarget = false;
                cooldownText = tmp;
            }
        }

        public void SetCooldown(float remaining, float total)
        {
            bool onCooldown = remaining > 0.05f;

            if (cooldownBg != null && cooldownBg.activeSelf != onCooldown)
                cooldownBg.SetActive(onCooldown);

            if (cooldownText != null)
                cooldownText.text = onCooldown ? Mathf.CeilToInt(remaining).ToString() : string.Empty;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.gameObject.SetActive(onCooldown);
                cooldownOverlay.fillAmount = (onCooldown && total > 0f) ? remaining / total : 0f;
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // Active 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ActiveSlotUI
    {
        [SerializeField] private Image iconImage;
        /// <summary>슬롯 우측 하단 숫자 레이블 (1 / 2 / 3). Inspector 또는 런타임 생성.</summary>
        [SerializeField] internal TMP_Text indexLabel;

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 버프 목록 + 획득 알림
    // ─────────────────────────────────────────────────────────

    private const float NoticeDuration = 2f;
    private const float NoticeFadeTime = 0.5f;

    private void Awake()
    {
        EnsureLayout();          // 컨테이너 분해·재배치(HP 하단중앙 / 스킬 2포드 / 액티브) — 먼저
        EnsureSlotLabels();
        EnsureRSlot();           // R → WeaponPod
        EnsureStatPanel();       // ATK/DEF → HpBar

        // Q/E는 프리팹에 cooldownOverlay·cooldownText가 미할당이라 R처럼 도는 연출이 없었다 → 런타임 보강.
        skillQ?.EnsureCooldownVisuals(skillInnerSprite, GetSafeFont());
        skillE?.EnsureCooldownVisuals(skillInnerSprite, GetSafeFont());

        ApplySlotSkins();        // 디자이너 아트 스킨(미지정 시 기존 플랫 외형 유지)
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        // 꺼져 있는 동안 만료된 알림이 한 프레임 번쩍이지 않게 즉시 정리한다.
        UpdateBuffNotice();
    }

    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    /// <summary>씬 전환(예: 게이트 통과) 시 전환성 획득/안내 알림을 즉시 클리어 — 다음 씬으로 잔류 방지.</summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HideBuffNotice();
        ClearItemNotices();
    }

    // ─────────────────────────────────────────────────────────
    // 레이아웃 분해(원신/명조식): 프리팹 authored 요소를 역할별 컨테이너로 재배치.
    // 재부모는 GameObject를 보존하므로 CombatPanelView 직렬화 참조(슬라이더/아이콘)는 유지됨.
    // ─────────────────────────────────────────────────────────
    private void EnsureLayout()
    {
        if (_layoutBuilt) return;
        _layoutBuilt = true;

        // HP → 하단중앙. 스킨 시 프레임 아트 비율(3379×368 ≈ 9.2:1)에 맞춰 650×71 —
        // 아트 높이엔 위아래 장식이 포함돼 있어 실제 트랙 창은 가운데 슬롯(= hpInnerPadding으로 인셋).
        Vector2 hpSize = HasHpSkin ? new Vector2(650f, 71f) : new Vector2(520f, 16f);
        float   hpY    = HasHpSkin ? hpBarY : 96f;
        _hpBar = CreateContainer("HpBar", new Vector2(0.5f, 0f), new Vector2(0f, hpY), hpSize, new Vector2(0.5f, 0f));
        if (FindChildRecursive(transform, "HUD_Hp") is RectTransform hpRT)
        {
            hpRT.SetParent(_hpBar, false);
            StretchFill(hpRT);
        }
        // HP fill 이미지가 미할당이면 슬라이더 Fill에서 확보(색상 제어용)
        if (hpFillImage == null && hpSlider != null && hpSlider.fillRect != null)
            hpFillImage = hpSlider.fillRect.GetComponent<Image>();
        CleanHpBarVisual();   // 늘어난 장식 아트 → 단색 플랫 바

        // 스킬 2포드(우하단): 유물(Q) / 무기(E·R).
        // 스킨 시 래거시 색판(금/청 alpha 0.14)·Outline·"유물"/"무기" 라벨을 만들지 않는다 — 아트 프레임이 대체.
        // 스킨 시 Y를 올려 아이템 행(y=300)과의 간격을 목업 수준(약 120)으로 좁힌다.
        float podY = HasSkillSkin ? 120f : 26f;
        _relicPod  = CreatePod("RelicPod",  new Vector2(-336f, podY), new Vector2(118f, 118f), RelicColor,  HasRelicSlotSkin);
        _weaponPod = CreatePod("WeaponPod", new Vector2(-24f,  podY), new Vector2(300f, 118f), WeaponColor, HasSkillSkin);
        // 슬롯 rect — 테두리는 오버행 1.18배로 그려지므로 '보이는 크기 ÷ 1.18'이 rect다.
        // 목업 보이는 크기: Q 다이아 ≈123 / E ≈104 / R ≈118 → rect 104 / 88 / 100.
        ReparentSkill("HUD_QSkile", _relicPod,  new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(104f, 104f));
        ReparentSkill("HUD_ESkile", _weaponPod, new Vector2(0f, 0.5f),   new Vector2(94f, -6f), new Vector2(88f, 88f));
        if (!HasRelicSlotSkin) AddPodLabel(_relicPod,  "유물", RelicColor);
        if (!HasSkillSkin)     AddPodLabel(_weaponPod, "무기", WeaponColor);
        // R은 EnsureRSlot이 _weaponPod 우측에 배치(궁극=가장 큼)

        // 무기 2칸(WeaponPanel)은 프리팹이 정본 — 위치·크기·배경·테두리 모두 프리팹에 authoring 돼 있다.
        // 여기서 재배치하면 에디터에서 맞춘 배치가 실행 시 되돌아가므로 <b>건드리지 않는다</b>.

        // 유물 아이덴티티 바 (체력바 아래) — 활성 유물 IRelicResource 표시
        CreateRelicBar();
        BuildSunGauge();   // 가웨인 태양형(Style==Sun) 위젯 — 스프라이트 지정 시에만 생성, 초기 숨김

        // 중앙 하단 HUD 가시성↑: HP·유물바·스탯 뒤 어두운 배경 패널(밝은 바닥 대비).
        // 스킨 시엔 아트 자체가 대비를 가지므로 검은 반투명 판을 만들지 않는다(래거시 박스 잔상 방지).
        if (!HasHpSkin)
        {
            var backdrop = CreateContainer("CenterBackdrop", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(560f, 106f), new Vector2(0.5f, 0f));
            var bdImg = backdrop.gameObject.AddComponent<Image>();
            bdImg.color = new Color(0f, 0f, 0f, 0.34f);
            bdImg.raycastTarget = false;
            backdrop.SetAsFirstSibling();   // 중앙 요소들 뒤로
        }

        // 액티브 아이템 1/2/3 → 스킬 클러스터 위쪽 행. 스킨 시 목업 비율(90px, 우측 여백 56)로 확대.
        float aSize = HasSkillSkin ? 80f : 46f;   // 테두리 오버행 1.18배 → 보이는 크기 ≈95 (목업)
        float aY    = HasSkillSkin ? 300f : 172f;
        float aStep = HasSkillSkin ? 102f : 46f;
        float aX    = HasSkillSkin ? -101f : -300f;
        ReanchorActive("HUD_Active_01", new Vector2(aX,             aY), aSize);
        ReanchorActive("HUD_Active_02", new Vector2(aX - aStep,     aY), aSize);
        ReanchorActive("HUD_Active_03", new Vector2(aX - aStep * 2, aY), aSize);

        // 원래 컨테이너는 <b>끄지 않고 배경 이미지만</b> 숨긴다.
        // HP·스킬·액티브는 위에서 전부 다른 부모로 옮겨가고 여기 남는 건 WeaponPanel 하나인데,
        // WeaponPanel은 프리팹이 정본이라 이 컨테이너를 끄면 함께 사라진다.
        // (예전엔 WeaponPanel도 코드가 옮겼기 때문에 통째로 꺼도 됐다 → 지금은 배경만 숨김.)
        if (FindChildRecursive(transform, "CombatStatusRoot") is RectTransform legacyRoot
            && legacyRoot.TryGetComponent<Image>(out var legacyBg))
            legacyBg.enabled = false;   // 하단중앙 빈 박스 잔류 방지
    }

    /// <summary>transform 직속 빈 RectTransform 컨테이너 생성.</summary>
    private RectTransform CreateContainer(string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    /// <summary>우하단 스킬 포드(반투명 배경 + 테두리). 색+형태 이중부호화(WCAG 1.4.1).</summary>
    private RectTransform CreatePod(string name, Vector2 pos, Vector2 size, Color color, bool skinned = false)
    {
        var rt = CreateContainer(name, new Vector2(1f, 0f), pos, size, new Vector2(1f, 0f));
        if (skinned) return rt;   // 스킨: 래거시 색판·Outline 생략(아트 프레임이 대체)

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, 0.14f);
        img.raycastTarget = false;
        var ol = rt.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(color.r, color.g, color.b, 0.9f);
        ol.effectDistance = new Vector2(2f, -2f);
        return rt;
    }

    private void ReparentSkill(string childName, RectTransform pod, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        if (pod == null) return;
        if (FindChildRecursive(transform, childName) is not RectTransform rt) return;
        rt.SetParent(pod, false);
        Anchor(rt, anchor, pos, size, new Vector2(0.5f, 0.5f));
    }

    private void ReanchorActive(string childName, Vector2 pos, float size)
    {
        if (FindChildRecursive(transform, childName) is not RectTransform rt) return;
        rt.SetParent(transform, false);
        Anchor(rt, new Vector2(1f, 0f), pos, new Vector2(size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>포드 상단 출처 라벨(유물/무기).</summary>
    private void AddPodLabel(RectTransform pod, string text, Color color)
    {
        var go = new GameObject("PodLabel", typeof(RectTransform));
        go.transform.SetParent(pod, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f); rt.anchoredPosition = new Vector2(0f, 2f);
        rt.sizeDelta = new Vector2(70f, 16f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        tmp.text = text; tmp.fontSize = 12f; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center; tmp.color = color;
        tmp.raycastTarget = false;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>지정 이름의 모든 자손 오브젝트를 비활성화(비활성 포함 순회). 무기 빈 슬롯 텍스트 정리 등.</summary>
    private static void DisableChildrenNamed(Transform root, string name)
    {
        if (root == null) return;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name && c.gameObject.activeSelf) c.gameObject.SetActive(false);
            DisableChildrenNamed(c, name);
        }
    }

    /// <summary>유물 아이덴티티 바(체력바 아래): 트랙 + fill(anchorMax.x로 폭) + 중앙 라벨. 유물 바인딩 전엔 숨김.</summary>
    private void CreateRelicBar()
    {
        // 스킨 시 검 실루엣 아트 비율(≈7:1)에 맞춰 크게, 그리고 체력바 아래로 내려 겹침 방지.
        Vector2 rbSize = HasRelicGaugeSkin ? new Vector2(380f, 60f) : new Vector2(360f, 14f);
        float   rbY    = HasRelicGaugeSkin ? relicBarY : 72f;
        _relicBar = CreateContainer("RelicIdentityBar", new Vector2(0.5f, 0f), new Vector2(0f, rbY), rbSize, new Vector2(0.5f, 0f));
        var track = _relicBar.gameObject.AddComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.5f);
        track.raycastTarget = false;

        var fillGO = new GameObject("Fill", typeof(RectTransform));
        fillGO.transform.SetParent(_relicBar, false);
        var frt = fillGO.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 1f);   // 폭=0 시작 → Update에서 anchorMax.x=Fill
        frt.offsetMin = new Vector2(1f, 1f); frt.offsetMax = new Vector2(0f, -1f);
        frt.pivot = new Vector2(0f, 0.5f);
        _relicBarFill = fillGO.AddComponent<Image>();
        _relicBarFill.color = Color.white;
        _relicBarFill.raycastTarget = false;
        _relicBarGlow = fillGO.AddComponent<Outline>();   // fill 가장자리 맥동(게이지 피드백)
        _relicBarGlow.effectColor = new Color(1f, 1f, 1f, 0f);
        _relicBarGlow.effectDistance = new Vector2(2f, 2f);

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(_relicBar, false);
        var lrt = lblGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        _relicBarLabel = lblGO.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(_relicBarLabel);
        _relicBarLabel.fontSize = 10f;
        _relicBarLabel.alignment = TextAlignmentOptions.Center;
        _relicBarLabel.color = Color.white;
        _relicBarLabel.raycastTarget = false;
        var ol = lblGO.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.8f);
        ol.effectDistance = new Vector2(1f, -1f);

        _relicBar.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // 가웨인 태양 게이지(Style==Sun) — 수평 바를 대체하는 라디얼 태양 위젯.
    //   충전(여명·황혼) : 열린 태양 + 중앙을 가로지르는 충전 게이지(0→1)
    //   정오            : 태양이 닫히고 게이지 사라짐 → 완성 태양의 내부 채움이 감소(1→0)로 유지시간 표현
    // 스프라이트 미지정 시 아무 것도 만들지 않아 기존 수평 바만 남는다(비파괴).
    // ─────────────────────────────────────────────────────────
    // 체력바 아래 하단중앙. 태양은 크게, 게이지는 태양의 빈 중앙(입)을 관통한다.
    private const float SunBoxSize   = 100f;   // 태양 표시 박스(정사각, preserveAspect로 비율 유지)
    private const float SunClusterY  = 8f;     // 컨테이너 하단 y(화면 아래에서)
    private const float SunCenterY   = 50f;    // 컨테이너 내 태양 중심
    private const float SunGaugeOff  = 0f;     // 게이지 = 태양 정중앙(빈 공간)을 관통
    private const float SunGaugeW     = 340f;   // 충전 게이지 폭(태양보다 넓어 양옆으로 통과)
    private const float SunGaugeH     = 16f;

    // 붉은 게이지 색 — 기본색도 밝은 주황빨강, 끝으로 갈수록 더 진하고 밝게.
    private static readonly Color SunGaugeRedDark = new Color(0.95f, 0.32f, 0.10f, 1f);  // 시작(밝은 주황빨강)
    private static readonly Color SunGaugeRedHot  = new Color(1.00f, 0.52f, 0.18f, 1f);  // 선단(더 밝은 주홍)
    private static readonly Color SunNoonRed      = new Color(1.00f, 0.40f, 0.16f, 1f);  // 정오 내부 채움

    private void BuildSunGauge()
    {
        if (!HasSunSkin) return;

        // 태양은 세로로 크므로 하단중앙에 별도 컨테이너로 둔다. 큰 태양의 윗부분은 체력바 뒤로 tuck.
        _sunRoot = CreateContainer("RelicSunGauge", new Vector2(0.5f, 0f),
                                   new Vector2(0f, SunClusterY), new Vector2(360f, 104f), new Vector2(0.5f, 0f));

        Vector2 sunCenter   = new Vector2(0f, SunCenterY);                 // 태양(열림/닫힘) 중심
        Vector2 gaugeCenter = new Vector2(0f, SunCenterY + SunGaugeOff);   // 게이지는 태양의 빈 중앙을 관통

        // 충전 게이지 — 태양의 빈 중앙을 가로지르는 수평 바(트랙 + fill). 태양보다 뒤(먼저)에 깔아 가운데 공간으로 보이게.
        _sunGaugeRoot = MakeSunChild("SunGauge", gaugeCenter, new Vector2(SunGaugeW, SunGaugeH));
        var track = _sunGaugeRoot.gameObject.AddComponent<Image>();
        track.color = new Color(0.30f, 0.07f, 0.03f, 0.7f);   // 따뜻한 어두운 주황빨강 트랙(빈 구간)
        track.raycastTarget = false;

        var gaugeFillRT = MakeStretchChild(_sunGaugeRoot, "Fill");
        _sunGaugeFill = gaugeFillRT.gameObject.AddComponent<Image>();
        _sunGaugeFill.sprite      = sunGaugeSprite != null ? sunGaugeSprite : null;
        _sunGaugeFill.type        = Image.Type.Filled;
        _sunGaugeFill.fillMethod  = Image.FillMethod.Horizontal;
        _sunGaugeFill.fillOrigin  = (int)Image.OriginHorizontal.Left;
        _sunGaugeFill.color       = Color.white;   // 그라디언트가 정점색으로 곱해져 붉게 물든다
        _sunGaugeFill.raycastTarget = false;
        // 끝으로 갈수록 진하고 밝은 붉은색 — 정점 그라디언트(셰이더 없이).
        var gaugeGrad = gaugeFillRT.gameObject.AddComponent<UIHorizontalGradient>();
        gaugeGrad.SetColors(SunGaugeRedDark, SunGaugeRedHot);

        // 열린 태양(충전) — 게이지 위. 중앙 공간이 비어 게이지가 그 사이로 보인다.
        var openRT = MakeSunChild("SunOpen", sunCenter, new Vector2(SunBoxSize, SunBoxSize));
        _sunOpenImg = openRT.gameObject.AddComponent<Image>();
        _sunOpenImg.sprite         = sunOpenSprite;
        _sunOpenImg.preserveAspect = true;
        _sunOpenImg.raycastTarget  = false;

        // 닫힌 태양(정오) — 어두운 베이스 + 밝은 내부 채움(라디얼 감소). 충전 중엔 숨김.
        var closedRT = MakeSunChild("SunClosed", sunCenter, new Vector2(SunBoxSize, SunBoxSize));
        _sunClosedImg = closedRT.gameObject.AddComponent<Image>();
        _sunClosedImg.sprite         = sunClosedSprite;
        _sunClosedImg.preserveAspect = true;
        _sunClosedImg.color          = new Color(0.30f, 0.05f, 0.04f, 0.9f);   // 소진되어 남는 어두운 심홍 태양
        _sunClosedImg.raycastTarget  = false;

        var closedFillRT = MakeStretchChild(closedRT, "Fill");
        _sunClosedFill = closedFillRT.gameObject.AddComponent<Image>();
        _sunClosedFill.sprite         = sunClosedSprite;
        _sunClosedFill.preserveAspect = true;
        _sunClosedFill.type           = Image.Type.Filled;
        _sunClosedFill.fillMethod     = Image.FillMethod.Radial360;
        _sunClosedFill.fillOrigin     = (int)Image.Origin360.Top;
        _sunClosedFill.fillClockwise  = true;
        _sunClosedFill.color          = SunNoonRed;   // 정오 유지 게이지 = 붉은색
        _sunClosedFill.raycastTarget  = false;
        closedRT.gameObject.SetActive(false);

        // 라벨(태양 아래 — 컨테이너 최하단)
        var lblRT = MakeSunChild("Label", new Vector2(0f, 0f), new Vector2(300f, 15f));
        lblRT.pivot = new Vector2(0.5f, 0f);
        _sunLabel = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(_sunLabel);
        _sunLabel.fontSize      = 11f;
        _sunLabel.alignment     = TextAlignmentOptions.Center;
        _sunLabel.color         = Color.white;
        _sunLabel.raycastTarget = false;
        var lblOl = lblRT.gameObject.AddComponent<Outline>();
        lblOl.effectColor    = new Color(0f, 0f, 0f, 0.8f);
        lblOl.effectDistance = new Vector2(1f, -1f);

        // 큰 태양이라 윗부분이 체력바 영역에 닿을 수 있어 뒤(먼저)로 보내 체력바가 앞을 가리게 한다.
        _sunRoot.SetAsFirstSibling();
        _sunRoot.gameObject.SetActive(false);
    }

    /// <summary>_sunRoot 아래 중앙정렬 자식 RectTransform 생성(anchoredPosition=center).</summary>
    private RectTransform MakeSunChild(string name, Vector2 center, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_sunRoot, false);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;
        return rt;
    }

    private static RectTransform MakeStretchChild(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>가웨인 태양 위젯 갱신 — 정오면 닫힌 태양(내부 감소), 아니면 열린 태양+충전 게이지.</summary>
    private void UpdateSunGauge()
    {
        float fill = Mathf.Clamp01(_relicResource.Fill);
        bool  noon = _relicResource.IsSkillReady;

        // 게이지 조건이 바뀌면 Q 슬롯 사용가능 표시도 즉시 따라간다(쿨다운과 AND).
        RefreshQFrame();

        if (_sunOpenImg   != null && _sunOpenImg.gameObject.activeSelf   == noon)  _sunOpenImg.gameObject.SetActive(!noon);
        if (_sunGaugeRoot != null && _sunGaugeRoot.gameObject.activeSelf == noon)  _sunGaugeRoot.gameObject.SetActive(!noon);
        if (_sunClosedImg != null && _sunClosedImg.gameObject.activeSelf != noon)  _sunClosedImg.gameObject.SetActive(noon);

        if (noon)
        {
            if (_sunClosedFill != null) _sunClosedFill.fillAmount = fill;   // 정오 유지 1→0
        }
        else
        {
            if (_sunGaugeFill != null) _sunGaugeFill.fillAmount = fill;      // 충전 0→1
        }

        // 정오 진입 펀치(상승엣지).
        if (noon && !_lastSkillReady) _relicFlash = 1f;
        _lastSkillReady = noon;
        if (_relicFlash > 0f) _relicFlash = Mathf.Max(0f, _relicFlash - Time.unscaledDeltaTime * 2.5f);
        // 이 메서드의 다른 위젯 참조는 전부 널 체크를 하는데 여기만 빠져 있었다.
        // _sunRoot가 비면 Update가 매 프레임 던져 뒤쪽(알림 만료 등)이 통째로 죽는다.
        if (_sunRoot != null)
            _sunRoot.localScale = Vector3.one * (1f + _relicFlash * 0.12f);
    }

    /// <summary>활성 무기 칸 강조(칸 배경·테두리 밝기). HudPresenter가 CurrentSlotIndex로 호출.</summary>
    public void SetActiveWeapon(int index)
    {
        _activeWeaponIndex = index;
        RefreshWeaponFrames();
    }

    /// <summary>유물 아이덴티티 바에 활성 유물 리소스를 연결(null이면 숨김). 라벨은 OnChanged, Fill/색은 Update 폴링.</summary>
    public void SetRelicResource(IRelicResource res)
    {
        if (_relicBar == null) return;
        if (_relicResource != null && _relicChanged != null)
            _relicResource.OnChanged -= _relicChanged;

        _relicResource = res;

        // 태양형(가웨인)이고 스프라이트가 있으면 태양 위젯, 아니면 수평 바 — 종류를 몰라도 형태만 분기.
        _useSunGauge = res != null && res.Style == RelicGaugeStyle.Sun && _sunRoot != null;

        if (res == null)
        {
            _relicBar.gameObject.SetActive(false);
            if (_sunRoot != null) _sunRoot.gameObject.SetActive(false);
            return;
        }

        _relicChanged ??= RefreshRelicLabel;
        res.OnChanged += _relicChanged;

        _relicBar.gameObject.SetActive(!_useSunGauge);
        if (_sunRoot != null) _sunRoot.gameObject.SetActive(_useSunGauge);

        _lastSkillReady = res.IsSkillReady;   // 바인딩 순간을 기준으로 — 첫 프레임 헛 플래시 방지
        RefreshRelicLabel();
    }

    private void RefreshRelicLabel()
    {
        if (_relicResource == null) return;
        if (_useSunGauge) { if (_sunLabel != null) _sunLabel.text = _relicResource.Label; }
        else if (_relicBarLabel != null) _relicBarLabel.text = _relicResource.Label;
    }

    // ─────────────────────────────────────────────────────────
    // 슬롯 레이블 초기화 (Q/E/1/2/3)
    // ─────────────────────────────────────────────────────────
    // 첫 액티브 칸은 포션(C)이 쓴다. 2·3은 액티브 아이템용이나 아직 채우는 쪽이 없어 숨긴다.
    private static readonly string[] ActiveLabelTexts = { PotionKeyLabel, "2", "3" };
    private static readonly string[] SkillLabelTexts  = { "Q", "E" };

    /// <summary>액티브 아이템 시스템이 붙기 전까지 빈 슬롯(02·03)을 숨긴다. 구현되면 false로.</summary>
    private const bool HideUnusedActiveSlots = true;

    private void EnsureSlotLabels()
    {
        EnsureSkillLabel(skillQ, SkillLabelTexts[0]);
        EnsureSkillLabel(skillE, SkillLabelTexts[1]);

        // 포션 슬롯(첫 칸)만 라벨링 — 나머지는 아래에서 숨긴다.
        if (activeSlots != null && activeSlots.Length > 0)
            EnsureActiveLabel(activeSlots[0], ActiveLabelTexts[0]);

        if (HideUnusedActiveSlots)
        {
            var a02 = FindChildRecursive(transform, "HUD_Active_02");
            var a03 = FindChildRecursive(transform, "HUD_Active_03");
            if (a02 != null) a02.gameObject.SetActive(false);
            if (a03 != null) a03.gameObject.SetActive(false);
        }
        else
        {
            for (int i = 1; i < activeSlots.Length && i < ActiveLabelTexts.Length; i++)
                EnsureActiveLabel(activeSlots[i], ActiveLabelTexts[i]);
        }

        // 무기 교체 키 — 스킬·포션과 같은 규격으로 붙여 키 표기가 한 줄로 읽히게 한다.
        EnsureWeaponLabel("Slot_0", "1");
        EnsureWeaponLabel("Slot_1", "2");
    }

    private void EnsureSkillLabel(SkillSlotUI slot, string text)
    {
        if (slot == null) return;
        if (slot.keyLabel != null)
        {
            StyleKeyLabel(slot.keyLabel, text);   // 프리팹 라벨도 공통 규격으로 정규화
            return;
        }

        // 스킬 슬롯 부모 Transform을 검색
        // SkillSlotUI는 Serializable이므로 Transform 직접 참조가 없어
        // CombatStatusRoot 자식에서 이름으로 검색
        string goName = text == "Q" ? "HUD_QSkile" : "HUD_ESkile";
        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        slot.keyLabel = CreateCornerLabel(slotGo, text);
    }

    private void EnsureActiveLabel(ActiveSlotUI slot, string text)
    {
        if (slot == null) return;
        if (slot.indexLabel != null)
        {
            StyleKeyLabel(slot.indexLabel, text);   // 프리팹 라벨도 공통 규격으로 정규화
            return;
        }

        // 포션 슬롯(C)은 첫 칸을 쓰므로 숫자 대신 키 문자가 들어온다 → 슬롯 이름은 인덱스로 찾는다.
        string goName = text switch
        {
            "2" => "HUD_Active_02",
            "3" => "HUD_Active_03",
            _   => "HUD_Active_01",
        };

        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        slot.indexLabel = CreateCornerLabel(slotGo, text);
    }

    /// <summary>무기 슬롯(1·2) 키 라벨 — 스킬·액티브와 같은 규격으로 붙인다.</summary>
    private void EnsureWeaponLabel(string goName, string text)
    {
        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        var existing = slotGo.Find($"Label_{text}");
        if (existing != null && existing.TryGetComponent<TMP_Text>(out var tmp))
        {
            StyleKeyLabel(tmp, text);
            return;
        }
        CreateCornerLabel(slotGo, text);
    }
    // ── 키 표기 라벨 규격 (모든 슬롯 공통) ─────────────────────────
    // 슬롯마다 제각각이면 눈이 키를 못 찾는다. 위치·크기·색을 한 곳에서 강제한다.
    // 예전엔 슬롯 rect 우하단 '안쪽'에 12pt로 얹었는데, 사각 슬롯은 바로 그 자리가 프레임의
    // 코너 장식이라 글자가 파묻혀 안 보였다(다이아몬드 슬롯만 rect 모서리가 비어 Q가 보였던 것).
    // 슬롯 아래 바깥의 빈 공간으로 내리고, 크기를 키우고 외곽선을 넣어 배경과 무관하게 읽히게 한다.
    private const  float KeyLabelSize     = 17f;
    private const  float KeyLabelBoxW     = 24f;
    private const  float KeyLabelBoxH     = 20f;
    private static readonly Vector2 KeyLabelAnchor = new(1f, 0f);       // 슬롯 우하단 모서리에 고정
    private static readonly Vector2 KeyLabelPivot  = new(1f, 1f);       // 라벨은 그 아래로 늘어뜨린다
    private static readonly Vector2 KeyLabelOffset = new(-2f, -1f);
    private static readonly Color   KeyLabelColor  = new(1f, 0.88f, 0.55f, 1f);
    private static readonly Color   KeyLabelOutline = new(0f, 0f, 0f, 0.85f);

    private TMP_Text CreateCornerLabel(Transform slotRoot, string text)
    {
        var go = new GameObject($"Label_{text}", typeof(RectTransform));
        go.transform.SetParent(slotRoot, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        StyleKeyLabel(tmp, text);
        return tmp;
    }

    /// <summary>
    /// 키 라벨을 공통 규격으로 강제한다. 프리팹에 이미 배선된 라벨도 이 규격으로 맞춰
    /// 슬롯 간 위치·크기가 어긋나지 않게 한다(과거: 프리팹 라벨은 텍스트만 바꿔 제각각이었음).
    /// </summary>
    private void StyleKeyLabel(TMP_Text tmp, string text)
    {
        if (tmp == null) return;

        var rect = tmp.rectTransform;
        rect.anchorMin        = KeyLabelAnchor;
        rect.anchorMax        = KeyLabelAnchor;
        rect.pivot            = KeyLabelPivot;
        rect.anchoredPosition = KeyLabelOffset;
        rect.sizeDelta        = new Vector2(KeyLabelBoxW, KeyLabelBoxH);
        rect.localScale       = Vector3.one;

        tmp.text          = text;
        tmp.fontSize      = KeyLabelSize;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.color         = KeyLabelColor;
        tmp.alignment     = TextAlignmentOptions.TopRight;
        tmp.enableWordWrapping = false;
        tmp.overflowMode  = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        // 어떤 배경(밝은 프레임·밝은 바닥) 위에서도 읽히도록 외곽선을 강제한다.
        if (!tmp.TryGetComponent<UnityEngine.UI.Outline>(out var ol))
            ol = tmp.gameObject.AddComponent<UnityEngine.UI.Outline>();
        ol.effectColor    = KeyLabelOutline;
        ol.effectDistance = new Vector2(1.5f, -1.5f);

        if (slotLabelFont != null) tmp.font = slotLabelFont;
    }

    private static TMP_FontAsset _safeFontCache;
    private static TMP_FontAsset GetSafeFont()
    {
        if (_safeFontCache != null && _safeFontCache.material != null)
            return _safeFontCache;
        var def = TMP_Settings.defaultFontAsset;
        if (def != null && def.material != null)
            return _safeFontCache = def;
        _safeFontCache = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return _safeFontCache;
    }
    private static void AssignSafeFont(TMP_Text tmp)
    {
        if (tmp.font != null && tmp.font.material != null) return;
        var safe = GetSafeFont();
        if (safe != null) tmp.font = safe;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void Update()
    {
        if (_hpInitialized)
            UpdateHpAnimation();

        // 가웨인 태양형: 라디얼 태양 위젯을 대신 구동(수평 바는 숨김).
        if (_useSunGauge && _relicResource != null)
        {
            UpdateSunGauge();
        }
        // 유물 아이덴티티 바: Fill(폭)·색 매 프레임 폴링 + 게이지 맥동/절정 연출(라벨은 OnChanged)
        else if (_relicResource != null && _relicBarFill != null)
        {
            float fill = Mathf.Clamp01(_relicResource.Fill);
            var c = _relicResource.BarColor;

            bool ready = _relicResource.IsSkillReady;

            // 게이지 조건이 바뀌면 Q 슬롯 사용가능 표시도 즉시 따라간다(쿨다운과 AND).
            RefreshQFrame();

            if (_relicGaugeFill != null)
            {
                // 스킨: 폭 대신 fillAmount(검 실루엣 유지) + 상태별 fill 아트 스왑(평시 보라 / 절정 빨강)
                _relicGaugeFill.fillAmount = fill;
                var art = (ready && relicGaugeReadySprite != null) ? relicGaugeReadySprite : relicGaugeFillSprite;
                if (art != null && _relicGaugeFill.sprite != art) _relicGaugeFill.sprite = art;
            }
            else
            {
                _relicBarFill.rectTransform.anchorMax = new Vector2(fill, 1f);
                _relicBarFill.color = c;
            }

            // 절정 구간(가웨인 정오 등 IsSkillReady): 진입 플래시 + 빠르고 강한 맥동. 랜슬롯은 게이지 비례.
            if (ready && !_lastSkillReady) _relicFlash = 1f;               // 정오 진입 플래시(상승엣지)
            _lastSkillReady = ready;
            if (_relicFlash > 0f) _relicFlash = Mathf.Max(0f, _relicFlash - Time.unscaledDeltaTime * 2.5f);

            if (_relicBarGlow != null)
            {
                float pulse     = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (ready ? 10f : 6f));
                float intensity = ready ? 1f : fill * fill;               // 절정=상시 강, 아니면 게이지 비례
                float glow      = pulse * intensity * 0.9f + _relicFlash; // 진입 플래시 가산
                _relicBarGlow.effectColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(glow));
            }
            // 정오 진입 시 바를 잠깐 확대(펀치) — 절정 강조
            // 분기 조건이 _relicBarFill만 보고 _relicBar는 안 봐서 여기만 무방비였다(_sunRoot와 같은 결함).
            if (_relicBar != null)
                _relicBar.localScale = Vector3.one * (1f + _relicFlash * 0.10f);
        }

        UpdateBuffNotice();

        if (_itemNotices.Count > 0)
            UpdateItemNotices();
    }

    /// <summary>버프 획득/발동 시 화면 알림 (2초 표시 + 0.5초 페이드아웃).</summary>
    public void ShowBuffNotice(string message)
    {
        if (buffNoticeText == null)
        {
            EnsureBuffNoticeText();
            if (buffNoticeText == null) return;
        }

        // 연속 획득: 이전 표시분을 덮어쓰고 만료 시각도 새로 잡는다(핸들 누적 없음 — 슬롯 1개).
        buffNoticeText.text = message;
        buffNoticeText.gameObject.SetActive(true);

        // 알파 복원
        var c = buffNoticeText.color;
        c.a = 1f;
        buffNoticeText.color = c;

        _noticeHideAt = Time.unscaledTime + NoticeDuration + NoticeFadeTime;
    }

    /// <summary>표시 중인 알림의 페이드/만료 처리. Update와 OnEnable에서 호출.</summary>
    private void UpdateBuffNotice()
    {
        if (_noticeHideAt < 0f)
        {
            // 불변식: 대기 중인 알림이 없으면 텍스트 오브젝트는 꺼져 있어야 한다.
            //
            // 만료로 껐는데 <b>외부가 다시 켜는</b> 경로가 실재한다 —
            // HudBootstrapper.ForceTextsVisible이 방 전환마다 Panel_Combat 하위의
            // TextMeshProUGUI를 비활성 포함으로 긁어 전부 SetActive(true) + 알파 1로 되돌린다.
            // 그때 _noticeHideAt은 이미 -1이라 만료 감시가 꺼져 있어 낡은 문구가 영구히 남았다
            // ("2.5초 뒤 사라졌다가 다음 방에서 다시 나타나 안 없어짐" 재현 경로).
            // 되살아나면 여기서 즉시 되돌린다.
            if (buffNoticeText != null && buffNoticeText.gameObject.activeSelf)
                buffNoticeText.gameObject.SetActive(false);
            return;
        }

        if (buffNoticeText == null) { _noticeHideAt = -1f; return; }

        float remain = _noticeHideAt - Time.unscaledTime;

        if (remain <= 0f)
        {
            HideBuffNotice();
            return;
        }

        if (remain < NoticeFadeTime)
        {
            var c = buffNoticeText.color;
            c.a = remain / NoticeFadeTime;
            buffNoticeText.color = c;
        }
    }

    /// <summary>알림 강제 종료 — 알파까지 되돌려 다음 표시가 흐리게 뜨는 일이 없게 한다.</summary>
    private void HideBuffNotice()
    {
        _noticeHideAt = -1f;
        if (buffNoticeText == null) return;

        var c = buffNoticeText.color;
        c.a = 1f;
        buffNoticeText.color = c;
        buffNoticeText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 현재 활성 지속 버프 목록 전체 갱신(BuffViewItem 모델 경로).
    /// 그리드 = 모든 버프(아이콘 + 칸 안 스택 숫자). 게이지 = Remaining01 보유 항목만 분리 영역에 별도 바.
    /// 셀/바는 풀에서 재사용(부족하면 생성, 남으면 숨김). 수집은 BuffViewAggregator 담당.
    /// </summary>
    public void RefreshBuffView(IReadOnlyList<BuffViewItem> items)
    {
        EnsureBuffGrid();
        if (_buffGrid == null) return;

        int count = items?.Count ?? 0;

        // ── 그리드(아이콘 + 스택 숫자) — 모든 버프 ──
        while (_buffCells.Count < count)
            _buffCells.Add(CreateBuffCell());
        for (int i = 0; i < _buffCells.Count; i++)
        {
            if (i < count) _buffCells[i].Bind(items[i]);
            else           _buffCells[i].Hide();
        }

        // 잔여 시간은 칸 위 스윕(BuffCell)이 직접 표시한다 — 예전엔 그리드 아래 별도 막대 영역을 뒀는데
        // 좌측 도크에 세로 여유가 없어 막대 4줄이 무기 패널을 가로질렀다(설계상 그 사이 공간이 31px뿐).

        // 호버 중이던 셀이 숨겨졌으면 툴팁 정리, 살아있으면 내용 갱신
        if (_hoveredCell != null)
        {
            if (!_hoveredCell.gameObject.activeSelf) HideBuffTooltip();
            else SetTooltipContent(_hoveredCell.Item);
        }
    }

    /// <summary>
    /// 구조가 동일할 때 동적 값만 in-place 갱신: 셀의 스택 숫자 + 분리 게이지 바의 채움.
    /// 전량 재생성 없이 폴링 GC를 억제한다.
    /// </summary>
    public void UpdateBuffValues(IReadOnlyList<BuffViewItem> items)
    {
        if (items == null) return;

        // 스택 숫자(셀)
        int n = Mathf.Min(items.Count, _buffCells.Count);
        for (int i = 0; i < n; i++)
            if (_buffCells[i].gameObject.activeSelf)
                _buffCells[i].UpdateValues(items[i]);

        if (_hoveredCell != null && _hoveredCell.gameObject.activeSelf)
            SetTooltipContent(_hoveredCell.Item);
    }

    private BuffCell CreateBuffCell()
    {
        var go = new GameObject($"BuffCell_{_buffCells.Count}", typeof(RectTransform));
        go.transform.SetParent(_buffGrid.transform, false);
        var cell = go.AddComponent<BuffCell>();
        _buffIconResolver ??= BuffIconFor;
        cell.Initialize(GetSafeFont(), OnBuffCellHover, buffInnerSprite, buffFrameSprite, _buffIconResolver);
        return cell;
    }

    // ── 버프 아이콘 해석: 디자이너 글리프 우선, 없으면 기존 EffectIconRegistry ──
    private Func<string, Sprite> _buffIconResolver;

    /// <summary>IconKey → 스프라이트. 디자이너 글리프가 매핑된 키면 그 아트를, 아니면 기존 레지스트리 결과를 쓴다.</summary>
    private Sprite BuffIconFor(string iconKey)
        => GlyphFor(iconKey) ?? EffectIconRegistry.GetSprite(iconKey);

    /// <summary>디자이너가 준 버프 글리프 4종의 IconKey 매핑(모양 기반 추정 — 날개/모래시계/꽃/반지).</summary>
    private Sprite GlyphFor(string iconKey)
    {
        if (string.IsNullOrEmpty(iconKey)) return null;

        if (Same(iconKey, "speed") || Same(iconKey, "atkspeed")) return buffIconSpeed;
        if (Same(iconKey, "cooldown"))                           return buffIconCooldown;
        if (Same(iconKey, "heal"))                               return buffIconHeal;
        if (Same(iconKey, "luck"))                               return buffIconLuck;
        return null;

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // ── 호버 툴팁(재사용 1개) ────────────────────────────────
    private void OnBuffCellHover(BuffCell cell, bool entered)
    {
        if (entered)
        {
            _hoveredCell = cell;
            ShowBuffTooltip(cell);
        }
        else if (_hoveredCell == cell)
        {
            HideBuffTooltip();
        }
    }

    private void ShowBuffTooltip(BuffCell cell)
    {
        EnsureBuffTooltip();
        if (_buffTooltip == null) return;

        SetTooltipContent(cell.Item);
        _buffTooltip.transform.SetAsLastSibling();

        // 셀 우측에 배치한 뒤 화면 경계 안으로 클램프.
        float cw = _buffGrid != null ? _buffGrid.cellSize.x : 46f;
        float ch = _buffGrid != null ? _buffGrid.cellSize.y : 46f;
        _buffTooltip.transform.position = cell.Rect.position + new Vector3(cw * 0.5f + 8f, ch * 0.5f, 0f);

        _buffTooltip.SetActive(true);
        ClampTooltipToScreen();
    }

    /// <summary>툴팁이 화면 밖으로 나가지 않도록 위치 보정(Overlay 캔버스 = 스크린 픽셀 좌표).</summary>
    private void ClampTooltipToScreen()
    {
        if (_buffTooltip == null) return;
        var rt = (RectTransform)_buffTooltip.transform;

        // ContentSizeFitter 높이 반영을 위해 즉시 레이아웃 재빌드 후 실제 코너 측정.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        rt.GetWorldCorners(_tooltipCorners);   // 0:BL 1:TL 2:TR 3:BR

        float minX = _tooltipCorners[0].x, maxX = _tooltipCorners[2].x;
        float minY = _tooltipCorners[0].y, maxY = _tooltipCorners[1].y;

        float dx = 0f, dy = 0f;
        if (maxX > Screen.width)        dx  = Screen.width - maxX;          // 우측 초과 → 왼쪽으로
        if (minX + dx < 0f)             dx += -(minX + dx);                 // 좌측 초과 → 오른쪽으로
        if (minY + dy < 0f)             dy  = -minY;                        // 하단 초과 → 위로
        if (maxY + dy > Screen.height)  dy += Screen.height - (maxY + dy);  // 상단 초과 → 아래로

        if (dx != 0f || dy != 0f)
            _buffTooltip.transform.position += new Vector3(dx, dy, 0f);
    }

    private void HideBuffTooltip()
    {
        _hoveredCell = null;
        if (_buffTooltip != null && _buffTooltip.activeSelf)
            _buffTooltip.SetActive(false);
    }

    private void SetTooltipContent(in BuffViewItem item)
    {
        if (_buffTooltipText == null) return;

        string src = item.Source switch
        {
            BuffSource.Room   => "방 버프",
            BuffSource.Rune   => "룬",
            BuffSource.Relic  => "유물",
            BuffSource.Item   => "아이템",
            BuffSource.Status => "상태이상",
            _                 => "",
        };

        string text = $"<b>{item.Label}</b>";
        if (item.Stacks > 1) text += $"\n중첩 ×{item.Stacks}";
        if (!string.IsNullOrEmpty(item.RemainText)) text += $"\n잔여 {item.RemainText}";
        else if (item.Remaining01 >= 0f) text += $"\n충전 {Mathf.RoundToInt(Mathf.Clamp01(item.Remaining01) * 100f)}%";
        if (!string.IsNullOrEmpty(src)) text += $"\n<size=85%><color=#AAAAAA>{src}</color></size>";

        _buffTooltipText.text = text;
    }

    // ─────────────────────────────────────────────────────────
    // 아이템 효과 발동 알림 (스택형, 왼쪽 하단)
    // ─────────────────────────────────────────────────────────

    private const float ItemNoticeDuration = 2f;
    private const float ItemNoticeFadeTime = 0.5f;
    private const int MaxItemNotices = 5;

    private Transform _itemNoticeRoot;
    private readonly List<ItemNoticeEntry> _itemNotices = new();

    private struct ItemNoticeEntry
    {
        public GameObject go;
        public TMP_Text text;
        public float hideAt;   // Time.unscaledTime 기준 만료 시각(버프 알림과 동일 규칙)
    }

    /// <summary>아이템 효과 발동 시 왼쪽에 스택형 알림 표시.</summary>
    public void ShowItemEffectNotice(string message)
    {
        EnsureItemNoticeRoot();
        if (_itemNoticeRoot == null) return;

        // 최대 개수 초과 시 가장 오래된 것 제거
        if (_itemNotices.Count >= MaxItemNotices)
        {
            if (_itemNotices[0].go != null)
                Destroy(_itemNotices[0].go);
            _itemNotices.RemoveAt(0);
        }

        var go = new GameObject($"ItemNotice_{_itemNotices.Count}", typeof(RectTransform));
        go.transform.SetParent(_itemNoticeRoot, false);

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(280f, 22f);

        var text = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(text);
        text.text = message;
        text.fontSize = 13f;
        text.color = new Color(1f, 0.85f, 0.4f);
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1f, -1f);

        _itemNotices.Add(new ItemNoticeEntry
        {
            go = go,
            text = text,
            hideAt = Time.unscaledTime + ItemNoticeDuration + ItemNoticeFadeTime,
        });
    }

    private void UpdateItemNotices()
    {
        // Time.deltaTime을 쓰던 시절엔 timeScale=0(일시정지·차단 팝업) 동안 만료가 멈춰
        // 알림이 화면에 그대로 굳었다. 만료는 절대 시각(unscaled)으로 판정한다.
        float now = Time.unscaledTime;

        for (int i = _itemNotices.Count - 1; i >= 0; i--)
        {
            var entry = _itemNotices[i];
            float remain = entry.hideAt - now;

            if (remain <= 0f)
            {
                if (entry.go != null) Destroy(entry.go);
                _itemNotices.RemoveAt(i);
            }
            else if (remain < ItemNoticeFadeTime && entry.text != null)
            {
                var c = entry.text.color;
                c.a = remain / ItemNoticeFadeTime;
                entry.text.color = c;
            }
        }
    }

    /// <summary>스택형 아이템 알림 전부 즉시 제거(씬 전환 등) — 다음 씬으로 잔류 방지.</summary>
    private void ClearItemNotices()
    {
        for (int i = 0; i < _itemNotices.Count; i++)
            if (_itemNotices[i].go != null) Destroy(_itemNotices[i].go);
        _itemNotices.Clear();
    }

    private void EnsureItemNoticeRoot()
    {
        if (_itemNoticeRoot != null) return;

        var go = new GameObject("ItemNoticeRoot", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.3f);
        rect.anchorMax = new Vector2(0f, 0.3f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(290f, 200f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _itemNoticeRoot = go.transform;
    }

    // ─────────────────────────────────────────────────────────
    // R 스킬 슬롯 자동 생성 (Q=138, E=212, R=286 px 배치)
    // ─────────────────────────────────────────────────────────

    private void EnsureRSlot()
    {
        if (_rIconImg != null) return;
        if (_weaponPod == null) return;   // EnsureLayout이 먼저 생성

        var rGO = new GameObject("HUD_RSkile", typeof(RectTransform));
        rGO.transform.SetParent(_weaponPod, false);   // 무기 포드(청) 우측
        var rt = rGO.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(100f, 100f);  // 궁극=가장 큼(시각 위계)
        rt.anchoredPosition = new Vector2(214f, 0f);

        var slotBG = rGO.AddComponent<Image>();
        slotBG.color = new Color(0.08f, 0.08f, 0.14f, 0.85f);

        // 스킬 아이콘
        var iconGO = new GameObject("SkillIcon", typeof(RectTransform));
        iconGO.transform.SetParent(rGO.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(5f, 5f);
        iconRT.offsetMax = new Vector2(-5f, -5f);
        _rIconImg = iconGO.AddComponent<Image>();
        _rIconImg.preserveAspect = true;
        _rIconImg.gameObject.SetActive(false);

        // 쿨다운 오버레이
        var cdBG = new GameObject("Cooldown_BG", typeof(RectTransform));
        cdBG.transform.SetParent(rGO.transform, false);
        var cdRT = cdBG.GetComponent<RectTransform>();
        cdRT.anchorMin = Vector2.zero;
        cdRT.anchorMax = Vector2.one;
        cdRT.sizeDelta  = Vector2.zero;
        var cdImg = cdBG.AddComponent<Image>();
        cdImg.color         = new Color(0f, 0f, 0f, 0.65f);
        cdImg.raycastTarget = false;
        // 차오름 필 — E/Q와 동일 의미(fillAmount = remaining/total). Filled는 스프라이트가 있어야 동작하므로
        // 슬롯 내부 아트가 있을 때만 radial 스윕, 없으면 기존 단순 토글(회귀 0).
        if (skillInnerSprite != null)
        {
            cdImg.sprite        = skillInnerSprite;
            cdImg.type          = Image.Type.Filled;
            cdImg.fillMethod    = Image.FillMethod.Radial360;
            cdImg.fillOrigin    = (int)Image.Origin360.Top;
            cdImg.fillClockwise = true;
        }
        _rCooldownFill = cdImg;
        _rCooldownBg = cdBG;
        _rCooldownBg.SetActive(false);

        // 쿨다운 숫자
        var cdTxtGO = new GameObject("CooldownText", typeof(RectTransform));
        cdTxtGO.transform.SetParent(cdBG.transform, false);
        var cdTxtRT = cdTxtGO.GetComponent<RectTransform>();
        cdTxtRT.anchorMin = Vector2.zero;
        cdTxtRT.anchorMax = Vector2.one;
        cdTxtRT.sizeDelta = Vector2.zero;
        _rCooldownTxt = cdTxtGO.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(_rCooldownTxt);
        _rCooldownTxt.fontSize  = 18f;
        _rCooldownTxt.fontStyle = FontStyles.Bold;
        _rCooldownTxt.alignment = TextAlignmentOptions.Center;
        _rCooldownTxt.color     = Color.white;

        // 키 레이블 "R"
        CreateCornerLabel(rGO.transform, "R");
    }

    // ─────────────────────────────────────────────────────────
    // 스탯 표시 패널 (ATK / DEF — 자동 생성)
    // ─────────────────────────────────────────────────────────

    /// <summary>공격력·방어력을 HUD에 실시간 반영한다. HudPresenter.RefreshStats에서 호출.</summary>
    public void SetStats(int atk, int def)
    {
        // 스킨: 아이콘이 의미를 전달하므로 숫자만(목업 형식). 래거시: 라벨 포함.
        if (_atkText != null)
        {
            if (_statSkinned) _atkText.SetText("{0}", atk);
            else              _atkText.SetText($"ATK {atk}");
        }
        if (_defText != null)
        {
            if (_statSkinned) _defText.SetText("{0}", def);
            else              _defText.SetText($"DEF {def}");
        }
    }

    private void EnsureStatPanel()
    {
        if (_statRoot != null) return;
        if (_hpBar == null) return;   // EnsureLayout이 먼저 생성

        _statSkinned = HasHpSkin;

        // 스탯 행: HP 바 바로 위
        var statGO = new GameObject("StatRow", typeof(RectTransform));
        statGO.transform.SetParent(_hpBar, false);
        _statRoot = statGO;

        var rt = statGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, _statSkinned ? 36f : 24f);
        rt.anchoredPosition = new Vector2(0f, _statSkinned ? 2f : 4f);

        // 반투명 배경 — 스킨 시엔 만들지 않는다(목업엔 검은 띠가 없다. 아이콘+숫자만).
        if (!_statSkinned)
        {
            var bg = statGO.AddComponent<Image>();
            bg.color         = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false;
        }

        // 스킨 시엔 아이콘+숫자 블록을 바 중앙 기준 좌우 대칭(±0.29)으로 모은다(래거시=좌우 절반 분할).
        Vector2 atkMin = _statSkinned ? new Vector2(0.228f, 0f) : new Vector2(0f,   0f);
        Vector2 atkMax = _statSkinned ? new Vector2(0.420f, 1f) : new Vector2(0.5f, 1f);
        Vector2 defMin = _statSkinned ? new Vector2(0.802f, 0f) : new Vector2(0.5f, 0f);
        Vector2 defMax = _statSkinned ? new Vector2(1.000f, 1f) : new Vector2(1f,   1f);

        _atkText = MakeStatText(statGO.transform, "AtkText", atkMin, atkMax,
            new Color(1.0f, 0.55f, 0.25f, 1f));

        _defText = MakeStatText(statGO.transform, "DefText", defMin, defMax,
            new Color(0.35f, 0.70f, 1.00f, 1f));
    }

    private TMP_Text MakeStatText(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(6f, 0f);
        rt.offsetMax = new Vector2(-6f, 0f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        tmp.text      = "—";
        tmp.fontSize  = _statSkinned ? 22f : 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = color;
        // 스킨: 아이콘이 라벨 왼쪽에 붙으므로 [아이콘][숫자]로 읽히려면 좌측 정렬이어야 한다.
        tmp.alignment = _statSkinned ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        var ol = go.AddComponent<Outline>();
        ol.effectColor    = new Color(0f, 0f, 0f, 0.7f);
        ol.effectDistance = new Vector2(1f, -1f);

        return tmp;
    }

    // ── 자동 생성 (Inspector 미연결 시 런타임 폴백) ──

    private void EnsureBuffListRoot()
    {
        if (buffListRoot != null) return;

        var go = new GameObject("BuffGridRoot", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        // 화면 좌하단 기준 도킹 → 왼쪽에서 오른쪽으로 늘고 위로 쌓임(주변시야). 레이아웃은 EnsureBuffGrid에서 부착.
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0f, 0f);   // 좌하단 피벗 → 좌측 기준 오른쪽+위로 확장
        rect.anchoredPosition = new Vector2(BuffDockX, BuffDockBottomY);
        rect.sizeDelta = new Vector2(250f, 100f);

        buffListRoot = go.transform;
    }

    /// <summary>버프 그리드 컨테이너(GridLayoutGroup + ContentSizeFitter) 보장. buffListRoot에 부착.</summary>
    private void EnsureBuffGrid()
    {
        if (_buffGrid != null) return;

        if (buffListRoot == null)
        {
            EnsureBuffListRoot();
            if (buffListRoot == null) return;
        }

        var rootGo = buffListRoot.gameObject;

        // 이전 세로 리스트용 레이아웃이 있으면 제거(그리드와 충돌 방지).
        var vlg = rootGo.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) Destroy(vlg);

        _buffGrid = rootGo.GetComponent<GridLayoutGroup>();
        if (_buffGrid == null) _buffGrid = rootGo.AddComponent<GridLayoutGroup>();
        _buffGrid.cellSize        = new Vector2(36f, 36f);   // 목업 아이콘 36px (step 41)
        _buffGrid.spacing         = new Vector2(5f, 5f);
        _buffGrid.startCorner     = GridLayoutGroup.Corner.LowerLeft;   // 하단 행부터 채우고 위로 쌓기
        _buffGrid.startAxis       = GridLayoutGroup.Axis.Horizontal;
        _buffGrid.childAlignment  = TextAnchor.LowerLeft;
        _buffGrid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        _buffGrid.constraintCount = 8;   // 한 행 8개, 9개째부터 위로 쌓임

        var fitter = rootGo.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = rootGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
    }

    /// <summary>호버 툴팁(재사용 1개) 보장. CombatPanel 하위에 생성, 표시 시 최상위로.</summary>
    private void EnsureBuffTooltip()
    {
        if (_buffTooltip != null) return;

        var go = new GameObject("BuffTooltip", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.pivot     = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(220f, 64f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        bg.raycastTarget = false;

        var txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 6f);
        trt.offsetMax = new Vector2(-8f, -6f);

        _buffTooltipText = txtGo.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(_buffTooltipText);
        _buffTooltipText.fontSize = 13f;
        _buffTooltipText.color = Color.white;
        _buffTooltipText.alignment = TextAlignmentOptions.TopLeft;
        _buffTooltipText.textWrappingMode = TextWrappingModes.Normal;
        _buffTooltipText.raycastTarget = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        go.SetActive(false);
        _buffTooltip = go;
    }


    private void EnsureBuffNoticeText()
    {
        if (buffNoticeText != null) return;

        var go = new GameObject("BuffNoticeText", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        // 화면 하단 1/3 중앙. 예전에는 0.7(상반부)이었는데, 출구 나침반(ExitCompassHud)이
        // 출구를 월드 투영해 그리는 자리가 바로 그 띠라 안내 문구와 겹쳤다.
        // 출구는 항상 플레이어보다 카메라에서 멀어 화면 위쪽에 맺히므로, 아래로 내리면 서로 침범하지 않는다.
        rect.anchorMin = new Vector2(0.5f, 0.3f);
        rect.anchorMax = new Vector2(0.5f, 0.3f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(560f, 40f);

        buffNoticeText = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(buffNoticeText);
        buffNoticeText.fontSize = 22f;
        buffNoticeText.color = Color.yellow;
        buffNoticeText.alignment = TextAlignmentOptions.Center;
        buffNoticeText.fontStyle = FontStyles.Bold;

        // Outline 효과
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1f, -1f);

        go.SetActive(false);
    }
}
