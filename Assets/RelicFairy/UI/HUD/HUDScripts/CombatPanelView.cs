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

    [Header("HP 애니메이션")]
    [SerializeField] private float hpLerpSpeed   = 3f;
    [SerializeField] private float ghostDelay    = 0.35f;
    [SerializeField] private float ghostLerpSpeed = 1.2f;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI slot0;
    [SerializeField] private WeaponSlotUI slot1;

    [Header("Skill Slots — Q / E")]
    [SerializeField] private SkillSlotUI skillQ;
    [SerializeField] private SkillSlotUI skillE;

    [Header("Active Slots")]
    [SerializeField] private ActiveSlotUI[] activeSlots = new ActiveSlotUI[3];

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
    private TMP_Text   _rCooldownTxt;

    // ── 스탯 표시 런타임 ──
    private TMP_Text _atkText;
    private TMP_Text _defText;
    private TMP_Text _hpMaxText;
    private GameObject _statRoot;

    // ── 버프 그리드 UI 런타임 ──
    private GridLayoutGroup _buffGrid;                       // buffListRoot에 부착(아이콘+스택)
    private readonly List<BuffCell> _buffCells = new();      // 셀 풀(재사용)
    private GameObject _buffTooltip;                         // 재사용 툴팁 1개
    private TMP_Text   _buffTooltipText;
    private BuffCell   _hoveredCell;

    // ── 분리된 게이지 영역 런타임(그리드와 별개) ──
    private Transform _gaugeRoot;
    private readonly List<GaugeBar> _gaugeBars = new();      // 게이지 바 풀(재사용)

    /// <summary>분리 게이지 바 1개의 위젯 참조.</summary>
    private struct GaugeBar
    {
        public GameObject    go;
        public Image         icon;
        public RectTransform fill;   // 폭=anchorMax.x
        public Image         fillImg;
    }

    private float _noticeTimer;

    // ── 버프창 도킹(좌측 중앙 — 원신/명조식, 주변시야 배치) ──
    // 그리드: 화면 왼쪽에서 오른쪽으로 늘고 위로 쌓임(유물 패시브=좌하단 첫 셀). 게이지: 그리드 아래.
    private const float BuffDockX       = 16f;    // 화면 왼쪽 가장자리 인셋
    private const float BuffDockBottomY = 20f;    // 좌측중앙 앵커 기준 Y

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

    // 무기 슬롯 활성 강조
    private RectTransform _weaponSlot0, _weaponSlot1;
    private Outline       _weaponOutline0, _weaponOutline1;

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
            hpFillImage.color = HpColorFor(ratio);   // 초록(가득)→노랑→빨강(위험)
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
    }

    // ─────────────────────────────────────────────────────────
    // 스킬 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetSkillIcon(SkillType skill, Sprite icon)
    {
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
        if (skill == SkillType.R)
        {
            bool onCd = remaining > 0.05f;
            if (_rCooldownBg != null) _rCooldownBg.SetActive(onCd);
            if (_rCooldownTxt != null)
                _rCooldownTxt.text = onCd ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
            return;
        }
        GetSkillSlot(skill)?.SetCooldown(remaining, total);
    }

    private SkillSlotUI GetSkillSlot(SkillType skill) => skill switch
    {
        SkillType.Q => skillQ,
        SkillType.E => skillE,
        _           => null,
    };

    // ─────────────────────────────────────────────────────────
    // Active 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetActiveSlot(int index, Sprite icon)
    {
        if (index >= 0 && index < activeSlots.Length)
            activeSlots[index]?.SetIcon(icon);
    }

    // ─────────────────────────────────────────────────────────
    // 무기 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class WeaponSlotUI
    {
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private Image      iconImage;

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
                    Sprite resolved = info.Icon != null ? info.Icon : ResolveTypeIcon(info.Type);
                    iconImage.sprite = resolved;
                    iconImage.gameObject.SetActive(resolved != null);
                }
                else
                {
                    iconImage.gameObject.SetActive(false);
                }
            }
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

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
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
    }

    private void OnEnable()  => SceneManager.sceneLoaded += HandleSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    /// <summary>씬 전환(예: 게이트 통과) 시 전환성 획득/안내 알림을 즉시 클리어 — 다음 씬으로 잔류 방지.</summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _noticeTimer = 0f;
        if (buffNoticeText != null) buffNoticeText.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // 레이아웃 분해(원신/명조식): 프리팹 authored 요소를 역할별 컨테이너로 재배치.
    // 재부모는 GameObject를 보존하므로 CombatPanelView 직렬화 참조(슬라이더/아이콘)는 유지됨.
    // ─────────────────────────────────────────────────────────
    private void EnsureLayout()
    {
        if (_layoutBuilt) return;
        _layoutBuilt = true;

        // HP → 하단중앙 얇은 모던 바 (올림 → 아래에 무기 2종 자리)
        _hpBar = CreateContainer("HpBar", new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(520f, 16f), new Vector2(0.5f, 0f));
        if (FindChildRecursive(transform, "HUD_Hp") is RectTransform hpRT)
        {
            hpRT.SetParent(_hpBar, false);
            StretchFill(hpRT);
        }
        // HP fill 이미지가 미할당이면 슬라이더 Fill에서 확보(색상 제어용)
        if (hpFillImage == null && hpSlider != null && hpSlider.fillRect != null)
            hpFillImage = hpSlider.fillRect.GetComponent<Image>();
        CleanHpBarVisual();   // 늘어난 장식 아트 → 단색 플랫 바

        // 스킬 2포드(우하단): 유물(금) / 무기(청) — 스킬=최상위 위계(가장 큼)
        _relicPod  = CreatePod("RelicPod",  new Vector2(-336f, 26f), new Vector2(118f, 118f), RelicColor);
        _weaponPod = CreatePod("WeaponPod", new Vector2(-24f,  26f), new Vector2(300f, 118f), WeaponColor);
        ReparentSkill("HUD_QSkile", _relicPod,  new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(90f, 90f));
        ReparentSkill("HUD_ESkile", _weaponPod, new Vector2(0f, 0.5f),   new Vector2(94f, -6f), new Vector2(78f, 78f));
        AddPodLabel(_relicPod,  "유물", RelicColor);
        AddPodLabel(_weaponPod, "무기", WeaponColor);
        // R은 EnsureRSlot이 _weaponPod 우측에 배치(궁극=가장 큼)

        // 무기 2종 → 좌하단(빈 공간), 확대. 활성 무기 강조용 슬롯/아웃라인 캐싱.
        if (FindChildRecursive(transform, "WeaponPanel") is RectTransform wpRT)
        {
            wpRT.SetParent(transform, false);
            Anchor(wpRT, new Vector2(0f, 0f), new Vector2(30f, 60f), new Vector2(170f, 78f), new Vector2(0f, 0f));
            wpRT.localScale = Vector3.one;   // 확대(0.85→1.0)
            _weaponSlot0 = FindChildRecursive(wpRT, "Weapon_01") as RectTransform;
            _weaponSlot1 = FindChildRecursive(wpRT, "Weapon_02") as RectTransform;
            _weaponOutline0 = EnsureSlotOutline(_weaponSlot0);
            _weaponOutline1 = EnsureSlotOutline(_weaponSlot1);
            DisableChildrenNamed(wpRT, "EmptyText");   // 빈 슬롯 "비어있음" 텍스트 정리
        }

        // 유물 아이덴티티 바 (체력바 아래) — 활성 유물 IRelicResource 표시
        CreateRelicBar();

        // 중앙 하단 HUD 가시성↑: HP·유물바·스탯 뒤 어두운 배경 패널(밝은 바닥 대비)
        var backdrop = CreateContainer("CenterBackdrop", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(560f, 106f), new Vector2(0.5f, 0f));
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.34f);
        bdImg.raycastTarget = false;
        backdrop.SetAsFirstSibling();   // 중앙 요소들 뒤로

        // 액티브 아이템 1/2/3 → 상단 좌측 소형 행(무기 슬롯과 분리)
        ReanchorActive("HUD_Active_01", new Vector2(-300f, 172f));
        ReanchorActive("HUD_Active_02", new Vector2(-346f, 172f));
        ReanchorActive("HUD_Active_03", new Vector2(-392f, 172f));

        // 자식이 모두 빠져나간 원래 컨테이너(배경 이미지)를 숨김 — 하단중앙 빈 박스 잔류 방지
        if (FindChildRecursive(transform, "CombatStatusRoot") is RectTransform legacyRoot)
            legacyRoot.gameObject.SetActive(false);
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
    private RectTransform CreatePod(string name, Vector2 pos, Vector2 size, Color color)
    {
        var rt = CreateContainer(name, new Vector2(1f, 0f), pos, size, new Vector2(1f, 0f));
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

    private void ReanchorActive(string childName, Vector2 pos)
    {
        if (FindChildRecursive(transform, childName) is not RectTransform rt) return;
        rt.SetParent(transform, false);
        Anchor(rt, new Vector2(1f, 0f), pos, new Vector2(46f, 46f), new Vector2(0.5f, 0.5f));
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
        _relicBar = CreateContainer("RelicIdentityBar", new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(360f, 14f), new Vector2(0.5f, 0f));
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

    private Outline EnsureSlotOutline(RectTransform slot)
    {
        if (slot == null) return null;
        if (!slot.TryGetComponent<Outline>(out var ol)) ol = slot.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(1f, 0.85f, 0.30f, 0.95f);   // 금색 강조
        ol.effectDistance = new Vector2(3f, -3f);
        ol.enabled = false;
        return ol;
    }

    /// <summary>활성 무기 슬롯 강조(아웃라인 + 확대 연출). HudPresenter가 CurrentSlotIndex로 호출.</summary>
    public void SetActiveWeapon(int index)
    {
        SetSlotActive(_weaponSlot0, _weaponOutline0, index == 0);
        SetSlotActive(_weaponSlot1, _weaponOutline1, index == 1);
    }

    private static void SetSlotActive(RectTransform slot, Outline ol, bool active)
    {
        if (ol != null)   ol.enabled = active;
        if (slot != null) slot.localScale = Vector3.one * (active ? 1.15f : 1.0f);
    }

    /// <summary>유물 아이덴티티 바에 활성 유물 리소스를 연결(null이면 숨김). 라벨은 OnChanged, Fill/색은 Update 폴링.</summary>
    public void SetRelicResource(IRelicResource res)
    {
        if (_relicBar == null) return;
        if (_relicResource != null && _relicChanged != null)
            _relicResource.OnChanged -= _relicChanged;

        _relicResource = res;
        if (res == null) { _relicBar.gameObject.SetActive(false); return; }

        _relicChanged ??= RefreshRelicLabel;
        res.OnChanged += _relicChanged;
        _relicBar.gameObject.SetActive(true);
        RefreshRelicLabel();
    }

    private void RefreshRelicLabel()
    {
        if (_relicBarLabel != null && _relicResource != null)
            _relicBarLabel.text = _relicResource.Label;
    }

    // ─────────────────────────────────────────────────────────
    // 슬롯 레이블 초기화 (Q/E/1/2/3)
    // ─────────────────────────────────────────────────────────
    private static readonly string[] ActiveLabelTexts = { "1", "2", "3" };
    private static readonly string[] SkillLabelTexts  = { "Q", "E" };

    private void EnsureSlotLabels()
    {
        EnsureSkillLabel(skillQ, SkillLabelTexts[0]);
        EnsureSkillLabel(skillE, SkillLabelTexts[1]);

        for (int i = 0; i < activeSlots.Length && i < ActiveLabelTexts.Length; i++)
            EnsureActiveLabel(activeSlots[i], ActiveLabelTexts[i]);
    }

    private void EnsureSkillLabel(SkillSlotUI slot, string text)
    {
        if (slot == null) return;
        if (slot.keyLabel != null)
        {
            slot.keyLabel.text = text;
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
            slot.indexLabel.text = text;
            return;
        }

        string goName = text switch
        {
            "1" => "HUD_Active_01",
            "2" => "HUD_Active_02",
            "3" => "HUD_Active_03",
            _   => null,
        };
        if (goName == null) return;

        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        slot.indexLabel = CreateCornerLabel(slotGo, text);
    }

    /// <summary>
    /// 슬롯 오브젝트 우측 하단 외부에 작은 레이블 TMP_Text를 생성한다.
    /// anchoredPosition을 슬롯 우측 하단 바깥쪽으로 배치한다.
    /// </summary>
    private TMP_Text CreateCornerLabel(Transform slotRoot, string text)
    {
        var go = new GameObject($"Label_{text}", typeof(RectTransform));
        go.transform.SetParent(slotRoot, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin  = new Vector2(1f, 0f);
        rect.anchorMax  = new Vector2(1f, 0f);
        rect.pivot      = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(2f, -2f);
        rect.sizeDelta  = new Vector2(20f, 20f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        tmp.text      = text;
        tmp.fontSize  = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = new Color(1f, 0.9f, 0.6f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;

        if (slotLabelFont != null)
            tmp.font = slotLabelFont;

        return tmp;
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

        // 유물 아이덴티티 바: Fill(폭)·색 매 프레임 폴링 + 게이지 맥동/절정 연출(라벨은 OnChanged)
        if (_relicResource != null && _relicBarFill != null)
        {
            float fill = Mathf.Clamp01(_relicResource.Fill);
            _relicBarFill.rectTransform.anchorMax = new Vector2(fill, 1f);
            var c = _relicResource.BarColor;
            _relicBarFill.color = c;

            // 절정 구간(가웨인 정오 등 IsSkillReady): 진입 플래시 + 빠르고 강한 맥동. 랜슬롯은 게이지 비례.
            bool ready = _relicResource.IsSkillReady;
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
            _relicBar.localScale = Vector3.one * (1f + _relicFlash * 0.10f);
        }

        if (_noticeTimer > 0f)
        {
            _noticeTimer -= Time.unscaledDeltaTime;   // 씬 전환/일시정지(timeScale=0)에도 얼지 않게 — 알림 잔류 방지

            if (buffNoticeText != null)
            {
                if (_noticeTimer <= 0f)
                {
                    buffNoticeText.gameObject.SetActive(false);
                }
                else if (_noticeTimer < NoticeFadeTime)
                {
                    float alpha = _noticeTimer / NoticeFadeTime;
                    var c = buffNoticeText.color;
                    c.a = alpha;
                    buffNoticeText.color = c;
                }
            }
        }

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

        buffNoticeText.text = message;
        buffNoticeText.gameObject.SetActive(true);

        // 알파 복원
        var c = buffNoticeText.color;
        c.a = 1f;
        buffNoticeText.color = c;

        _noticeTimer = NoticeDuration + NoticeFadeTime;
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

        // ── 분리된 게이지 영역 — Remaining01>=0 항목만 ──
        EnsureGaugeArea();
        RepositionGaugeBelowGrid(count);   // 그리드 실제 높이만큼 게이지를 아래로(침범 방지)
        int gaugeCount = 0;
        for (int i = 0; i < count; i++)
            if (items[i].Remaining01 >= 0f) gaugeCount++;

        while (_gaugeBars.Count < gaugeCount)
            _gaugeBars.Add(CreateGaugeBar());

        int gi = 0;
        for (int i = 0; i < count; i++)
        {
            if (items[i].Remaining01 < 0f) continue;
            BindGauge(_gaugeBars[gi], items[i]);
            gi++;
        }
        for (; gi < _gaugeBars.Count; gi++)
            if (_gaugeBars[gi].go != null) _gaugeBars[gi].go.SetActive(false);

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

        // 게이지 채움(분리 영역) — Remaining01 보유 항목 순서대로 바와 매칭
        int gi = 0;
        for (int i = 0; i < items.Count && gi < _gaugeBars.Count; i++)
        {
            if (items[i].Remaining01 < 0f) continue;
            var bar = _gaugeBars[gi];
            if (bar.fill != null)
                bar.fill.anchorMax = new Vector2(Mathf.Clamp01(items[i].Remaining01), 1f);
            gi++;
        }

        if (_hoveredCell != null && _hoveredCell.gameObject.activeSelf)
            SetTooltipContent(_hoveredCell.Item);
    }

    private BuffCell CreateBuffCell()
    {
        var go = new GameObject($"BuffCell_{_buffCells.Count}", typeof(RectTransform));
        go.transform.SetParent(_buffGrid.transform, false);
        var cell = go.AddComponent<BuffCell>();
        cell.Initialize(GetSafeFont(), OnBuffCellHover);
        return cell;
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
        public float timer;
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
            timer = ItemNoticeDuration + ItemNoticeFadeTime,
        });
    }

    private void UpdateItemNotices()
    {
        for (int i = _itemNotices.Count - 1; i >= 0; i--)
        {
            var entry = _itemNotices[i];
            entry.timer -= Time.deltaTime;
            _itemNotices[i] = entry;

            if (entry.timer <= 0f)
            {
                if (entry.go != null) Destroy(entry.go);
                _itemNotices.RemoveAt(i);
            }
            else if (entry.timer < ItemNoticeFadeTime && entry.text != null)
            {
                float alpha = entry.timer / ItemNoticeFadeTime;
                var c = entry.text.color;
                c.a = alpha;
                entry.text.color = c;
            }
        }
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
        cdBG.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
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
        if (_atkText != null) _atkText.SetText($"ATK {atk}");
        if (_defText != null) _defText.SetText($"DEF {def}");
    }

    private void EnsureStatPanel()
    {
        if (_statRoot != null) return;
        if (_hpBar == null) return;   // EnsureLayout이 먼저 생성

        // 스탯 행: HP 바 바로 위 24px (ATK 좌 / DEF 우)
        var statGO = new GameObject("StatRow", typeof(RectTransform));
        statGO.transform.SetParent(_hpBar, false);
        _statRoot = statGO;

        var rt = statGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, 24f);
        rt.anchoredPosition = new Vector2(0f, 4f);

        // 반투명 배경
        var bg = statGO.AddComponent<Image>();
        bg.color         = new Color(0f, 0f, 0f, 0.35f);
        bg.raycastTarget = false;

        // ── ATK 텍스트 (좌측 절반) ──
        _atkText = MakeStatText(statGO.transform, "AtkText",
            new Vector2(0f, 0f), new Vector2(0.5f, 1f),
            new Color(1.0f, 0.55f, 0.25f, 1f));

        // ── DEF 텍스트 (우측 절반) ──
        _defText = MakeStatText(statGO.transform, "DefText",
            new Vector2(0.5f, 0f), new Vector2(1f, 1f),
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
        tmp.fontSize  = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Midline;
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

        // 화면 좌측 중하단에 도킹 → 왼쪽에서 오른쪽으로 늘고 위로 쌓임(주변시야). 레이아웃은 EnsureBuffGrid에서 부착.
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.38f);
        rect.anchorMax = new Vector2(0f, 0.38f);
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
        _buffGrid.cellSize        = new Vector2(46f, 46f);
        _buffGrid.spacing         = new Vector2(4f, 4f);
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

    // ── 분리된 게이지 영역 ───────────────────────────────────
    /// <summary>그리드와 구분된 게이지 영역(세로 바 목록) 보장. 그리드 아래쪽에 별도 배치.</summary>
    private void EnsureGaugeArea()
    {
        if (_gaugeRoot != null) return;

        var go = new GameObject("BuffGaugeArea", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.38f);
        rect.anchorMax = new Vector2(0f, 0.38f);
        rect.pivot = new Vector2(0f, 1f);   // 좌상단 피벗 → 그리드 아래에서 아래로 쌓임
        // 그리드(좌측) 바로 아래에 분리 배치.
        rect.anchoredPosition = new Vector2(BuffDockX, BuffDockBottomY - 8f);
        rect.sizeDelta = new Vector2(210f, 100f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _gaugeRoot = go.transform;
    }

    /// <summary>게이지 영역을 그리드 시작점 왼쪽에 고정 배치(분리 영역). 그리드는 오른쪽+위로 자라므로 buffCount와 무관.</summary>
    private void RepositionGaugeBelowGrid(int buffCount)
    {
        if (_gaugeRoot == null) return;
        ((RectTransform)_gaugeRoot).anchoredPosition =
            new Vector2(BuffDockX, BuffDockBottomY - 8f);
    }

    private GaugeBar CreateGaugeBar()
    {
        var go = new GameObject($"BuffGauge_{_gaugeBars.Count}", typeof(RectTransform));
        go.transform.SetParent(_gaugeRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(210f, 16f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 16f;
        le.minHeight = 16f;

        // 아이콘(좌측)
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot     = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(0f, 0f);
        irt.sizeDelta = new Vector2(14f, 14f);
        var icon = iconGo.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // 트랙(아이콘 우측 ~ 우측 끝)
        var trackGo = new GameObject("Track", typeof(RectTransform));
        trackGo.transform.SetParent(go.transform, false);
        var trt = trackGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 0.5f);
        trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot     = new Vector2(0f, 0.5f);
        trt.offsetMin = new Vector2(18f, -4f);
        trt.offsetMax = new Vector2(0f, 4f);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = new Color(0f, 0f, 0f, 0.55f);
        trackImg.raycastTarget = false;

        // 채움(anchorMax.x로 폭 — 스프라이트 불필요, in-place 갱신 가벼움)
        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.raycastTarget = false;

        return new GaugeBar { go = go, icon = icon, fill = frt, fillImg = fillImg };
    }

    private void BindGauge(in GaugeBar bar, in BuffViewItem item)
    {
        if (bar.go == null) return;
        bar.go.SetActive(true);

        if (bar.icon != null)
            bar.icon.sprite = EffectIconRegistry.GetSprite(item.IconKey);

        if (bar.fillImg != null)
            bar.fillImg.color = item.IsDebuff
                ? new Color(0.90f, 0.40f, 0.40f, 0.95f)
                : new Color(0.40f, 0.80f, 0.85f, 0.95f);

        if (bar.fill != null)
            bar.fill.anchorMax = new Vector2(Mathf.Clamp01(item.Remaining01), 1f);
    }

    private void EnsureBuffNoticeText()
    {
        if (buffNoticeText != null) return;

        var go = new GameObject("BuffNoticeText", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.7f);
        rect.anchorMax = new Vector2(0.5f, 0.7f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(400f, 40f);

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
