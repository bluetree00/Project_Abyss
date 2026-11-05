using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 톤 v2 HUD(10-07 · 10-08 보정) — 사용자 「HUD의 기본 장비와 스킬 테두리 아이콘도 너무 두꺼운 느낌」 · 「활성화 연출도 제대로」,
// 그리고 10-08 「유물 컨셉 UI는 남겨야 해 · 테두리도 조금은 사용하되 얇게」.
// 납품 장식 테(장비 칸 · E/R · Q 유물칸 · 물약 · 체력바)는 지우지 않고 선만 1px 깎은 사본(UISpriteMaster.Thinned)으로 바꾸고,
// 그 테 Image를 넘겨받아(UIThinFrame.Adopt) 켜짐(테가 그려짐) · 눌림 · 교차 페이드를 건다. 유물 게이지(태양 · 검 실루엣)는 원래 그림 그대로.
// 레이아웃 · 납품 그림 배치는 스킨 경로(ApplySlotSkins) 그대로. ThinHud = false면 예전 스킨 경로 그대로.
public sealed partial class CombatPanelView
{
    // ── Constants ──
    /// <summary>톤 v2 HUD 스위치(되돌리기 한 줄).</summary>
    internal static readonly bool ThinHud = true;

    /// <summary>톤 v2면 굵게 없음(가짜 굵게 금지) · 아니면 예전 굵게.</summary>
    private static FontStyles BoldUnlessThin => ThinHud ? FontStyles.Normal : FontStyles.Bold;

    private const float LowHpRatio    = 0.30f;
    private const float LowHpPulseSec = 1.2f;
    private const float HealSweepSec  = 0.4f;
    private const float BuffBarHeight = 3f;    // 강화 남은 시간 막대(1080 기준 px)
    private const float BuffBarInset  = 10f;   // 칸 안쪽 아래 — 장식 테 안
    // R 「켜짐」 부채꼴(11-05 대검 속성 검) — 속성 색이 밴 어두운 덮개(검정 쪽 55% · 알파 0.55) · 재사용 베일(검정 0.65)과 색으로 갈리고 흰 숫자가 읽힌다
    // (밝은 금빛 0.3 · 0.2 덮개는 흰 칼 아이콘과 숫자가 섞여 읽히지 않았다 — 11-05 실측 사진)
    private const float RActiveDim = 0.45f, RActiveAlpha = 0.55f;
    private static Color RActiveTone(Color c) => new(c.r * RActiveDim, c.g * RActiveDim, c.b * RActiveDim, RActiveAlpha);

    // ── Private ──
    private UIThinFrame _qThin;
    private UIThinFrame _eThin;
    private UIThinFrame _rThin;
    private UIThinFrame _potionThin;
    private readonly UIThinFrame[] _weaponThin = new UIThinFrame[2];
    private Image _hpSweep;
    private float _hpSweepT = -1f;
    private bool _thinFontsApplied;   // 톤 v2 글꼴로 한 번 다 발랐는가(앱 부트 때 생긴 HUD는 글꼴이 늦게 올라온다)
    private readonly RectTransform[] _buffFill  = new RectTransform[2];   // 0 = E · 1 = R
    private readonly float[]         _buffLeft  = new float[2];
    private readonly float[]         _buffTotal = new float[2];
    private bool  _rOnCd;                  // R 재사용 대기 중(SetSkillCooldown) — 켜짐 표시는 대기가 아닐 때만
    private bool  _rActiveLook;            // R 칸이 지금 「켜짐」으로 그려져 있다
    private int   _rActiveTenths = -1;     // 켜짐 숫자를 0.1초가 바뀔 때만 다시 쓴다
    private Color _rFillBase, _rTxtBase;   // 켜짐 전 부채꼴 · 숫자 색(돌려놓을 값)

    // ── Public Methods ──

    /// <summary>재사용 대기 중에 눌렀다(PlayerController.SkillRejected) — E · R 칸이 살짝 흔들린다(10-08 A9).</summary>
    public void PlaySkillRejected(SkillType skill)
    {
        var frame = skill == SkillType.E ? _eThin : skill == SkillType.R ? _rThin : null;
        frame?.PlayReject();
    }

    /// <summary>
    /// 스킬 자기 강화가 걸렸다(PlayerController.SkillBuffStarted — 석궁 빨리쏘기 · 더 빨리쏘기) — 그 칸 안쪽 아래에 금빛 막대가 차서
    /// 게임 시간으로 줄어들다 끝나면 사라진다(10-08 A9). 다시 걸리면 꽉 찬 채로 처음부터.
    /// </summary>
    public void ShowSkillBuff(SkillType skill, float duration)
    {
        int i = skill == SkillType.E ? 0 : skill == SkillType.R ? 1 : -1;
        if (i < 0 || duration <= 0f) return;
        if (_buffFill[i] == null) _buffFill[i] = MakeBuffBar(i == 0 ? _eFrameImg : _rFrameImg);
        if (_buffFill[i] == null) return;
        _buffLeft[i] = _buffTotal[i] = duration;
        _buffFill[i].anchorMax = Vector2.one;
        _buffFill[i].parent.gameObject.SetActive(true);
    }

    // ── Private Methods ──

    /// <summary>Awake 끝 — ApplySlotSkins가 납품 테를 깐 다음에 한 번.</summary>
    private void ApplyThinSkin()
    {
        ApplyThinSkillFrames();
        ApplyThinWeaponCells();
        ApplyThinHp();
        ApplyThinTextStyles();
        _thinFontsApplied = UISkin.ThemeFonts != null;
    }

    /// <summary>글자 일괄(Medium) → 쿨다운 숫자(Light). 글꼴이 아직 안 올라왔으면 기본 글꼴로 돌고, 올라오면 TickThinFonts가 한 번 더 바른다.</summary>
    private void ApplyThinTextStyles()
    {
        ApplyThinFonts();          // 일괄(Medium) 먼저 — 아래가 필요한 곳만 다시 정한다
        skillQ?.StyleThin();
        skillE?.StyleThin();
        if (_rCooldownTxt != null) UITheme.StyleThinText(_rCooldownTxt, UIWeight.Light, 28f, UITheme.Thin.Ink, shadow: true);
        if (WuwaHud) RestyleWuwaCooldownTexts();   // 명조형 원형 칸 숫자(Medium 20 · Q 22)
    }

    /// <summary>
    /// 매 프레임(Update) — 글꼴(Addressables)이 HUD Awake보다 늦게 올라오면 그때 한 번 더 바른다(10-08 리뷰 I3: @UIRoot HUD는 앱 부트 때
    /// 생겨 Awake 시점엔 늘 글꼴이 없었다). 다 바른 뒤엔 bool 하나만 본다.
    /// </summary>
    private void TickThinFonts()
    {
        if (_thinFontsApplied || UISkin.ThemeFonts == null) return;
        _thinFontsApplied = true;
        ApplyThinTextStyles();
    }

    /// <summary>
    /// Q(유물칸) · E · R · 물약 — 납품 테를 얇게 깎아 넘겨받는다. Q 유물칸은 유물 컨셉 색(붉은 주황)을 그대로 두고 선만 깎으며,
    /// 준비된 동안 테가 숨쉰다(사용자 10-08 「유물 컨셉 UI는 남겨야 해」).
    /// </summary>
    private void ApplyThinSkillFrames()
    {
        _qThin = UIThinFrame.Adopt(_qFrameImg, UISpriteMaster.Thinned(relicSlotFrameSprite, keepColor: true),
                                   UISpriteMaster.Thinned(relicSlotActiveSprite, keepColor: true), breathe: true, keepColor: true);
        _eThin = UIThinFrame.Adopt(_eFrameImg, UISpriteMaster.Thinned(skillFrameSprite),
                                   UISpriteMaster.Thinned(skillFrameReadySprite), breathe: false);
        _rThin = UIThinFrame.Adopt(_rFrameImg, UISpriteMaster.Thinned(skillFrameSprite),
                                   UISpriteMaster.Thinned(skillFrameReadySprite), breathe: false);
        var potion = FindChildRecursive(transform, "HUD_Active_01");
        var potionFrame = potion != null ? potion.Find("SkinFrame") : null;
        if (potionFrame != null && potionFrame.TryGetComponent<Image>(out var pImg))
            _potionThin = UIThinFrame.Adopt(pImg, UISpriteMaster.Thinned(skillFrameSprite), null, breathe: false);

        // 처음 상태는 연출 없이 — 런 시작 순간 칸들이 한꺼번에 그려지지 않게.
        // Q는 Idle로 시작 — 게이지 · 쿨다운이 갖춰지는 순간 RefreshQFrame이 유물칸을 그리며 켠다(그게 「준비됨」 연출이다).
        _eThin?.SetState(ThinFrameState.Ready, animate: false);
        _rThin?.SetState(ThinFrameState.Ready, animate: false);
    }

    /// <summary>무기 두 칸 — 근 · 원 장식 테를 얇게 깎아 넘겨받는다. 든 칸 = 금 · 나머지 = 잉크(0.18초 교차 페이드 · 배율 튐 없음).</summary>
    private void ApplyThinWeaponCells()
    {
        for (int i = 0; i < _weaponThin.Length; i++)
        {
            var slot  = i == 0 ? slot0 : slot1;
            var frame = slot?.CellFrame;
            if (frame == null || frame.sprite == null) continue;
            slot.ApplyThin();
            _weaponThin[i] = UIThinFrame.Adopt(frame, UISpriteMaster.Thinned(frame.sprite), null, breathe: false);
        }
        RefreshWeaponFrames();
    }

    /// <summary>체력 — 스킨 경로 그대로, 테만 얇게(잉크 85%). 회복 띠 하나를 미리 깐다.</summary>
    private void ApplyThinHp()
    {
        if (_hpFrameImg != null && hpFrameSprite != null)
        {
            _hpFrameImg.sprite = UISpriteMaster.Thinned(hpFrameSprite);
            _hpFrameImg.color  = UITheme.Thin.InkA(0.85f);
        }
        var fillArea = hpSlider != null && hpSlider.fillRect != null ? hpSlider.fillRect.parent as RectTransform : null;
        var host = fillArea != null ? fillArea : _hpBar;
        if (host == null) return;
        _hpSweep = ShopUIStyle.MakeImage(host, "HealSweep", new Color(1f, 1f, 1f, 0f));
        _hpSweep.sprite = UITheme.SoftBand;
        var srt = _hpSweep.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(48f, 0f);
    }

    /// <summary>매 프레임(UpdateHpAnimation 끝) — 30% 이하 체력 테 맥박 · 회복 띠 이동. 힙 할당 없음.</summary>
    private void TickThinHp(float ratio)
    {
        if (WuwaHud && _wHpTrack != null)   // 명조형 — 장식 테가 없으니 30% 이하 맥박은 바탕이 붉게 숨쉰다
            _wHpTrack.color = ratio <= LowHpRatio
                ? Color.Lerp(WTrack, UITheme.Thin.WithA(UITheme.Thin.HpLow, 0.55f), 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / LowHpPulseSec))
                : WTrack;
        if (_hpFrameImg != null)
        {
            var calm = UITheme.Thin.InkA(0.85f);
            _hpFrameImg.color = ratio <= LowHpRatio
                ? Color.Lerp(calm, UITheme.Thin.HpLow, 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / LowHpPulseSec))
                : calm;
        }
        if (_hpSweep == null || _hpSweepT < 0f) return;
        _hpSweepT += Time.unscaledDeltaTime / HealSweepSec;
        float k = Mathf.Clamp01(_hpSweepT);
        var parent = (RectTransform)_hpSweep.rectTransform.parent;
        _hpSweep.rectTransform.anchoredPosition = new Vector2(parent.rect.width * k, 0f);
        _hpSweep.color = new Color(1f, 1f, 1f, 0.55f * Mathf.Sin(k * Mathf.PI));
        if (_hpSweepT >= 1f) { _hpSweepT = -1f; _hpSweep.color = new Color(1f, 1f, 1f, 0f); }
    }

    /// <summary>HUD 안 모든 글자 = Noto Medium(크기 · 색 유지 · 굵게 없음 · 소프트 그림자). 나중에 생기는 글자는 GetSafeFont가 맡는다.</summary>
    private void ApplyThinFonts()
    {
        var medium = UITheme.Font(UIWeight.Medium);
        if (medium == null) return;
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            if (t != null && t.font != medium) UITheme.StyleThinText(t, UIWeight.Medium, t.fontSize, t.color, shadow: true);
    }

    /// <summary>쓸 수 있게 된 순간 = 테가 그려지며 금빛(켜짐) · 쓴 순간 = 눌림 + 잉크 테(부채꼴 · 숫자는 원래 경로).</summary>
    private static void SetThinReady(UIThinFrame frame, bool ready)
    {
        if (frame == null) return;
        var next = ready ? ThinFrameState.Ready : ThinFrameState.Idle;
        if (frame.State == next) return;
        if (!ready) frame.PlayPress();
        frame.SetState(next, animate: true);
    }

    /// <summary>칸(테의 부모) 안쪽 아래에 잉크 바탕 + 금빛 채움 막대를 깐다. 채움 RectTransform을 돌려준다(폭 = anchorMax.x).</summary>
    private static RectTransform MakeBuffBar(Image frame)
    {
        var slot = frame != null ? frame.transform.parent as RectTransform : null;
        if (slot == null) return null;
        var track = ShopUIStyle.MakeImage(slot, "SkillBuffBar", UITheme.Thin.InkA(0.25f));
        track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot     = new Vector2(0.5f, 0f);
        trt.offsetMin = new Vector2(BuffBarInset, BuffBarInset);
        trt.offsetMax = new Vector2(-BuffBarInset, BuffBarInset + BuffBarHeight);
        trt.SetAsLastSibling();   // 쿨다운 베일 · 테 위
        var fill = ShopUIStyle.MakeImage(trt, "Fill", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.95f)).rectTransform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
        track.gameObject.SetActive(false);
        return fill;
    }

    /// <summary>매 프레임(Update) — 강화 막대를 게임 시간으로 줄이고 끝나면 숨긴다(강화도 게임 시간으로 끝난다). 힙 할당 없음.</summary>
    private void TickSkillBuffs(float dt)
    {
        for (int i = 0; i < _buffFill.Length; i++)
        {
            var fill = _buffFill[i];
            if (fill == null || _buffTotal[i] <= 0f) continue;
            _buffLeft[i] = Mathf.Max(0f, _buffLeft[i] - dt);
            fill.anchorMax = new Vector2(_buffLeft[i] / _buffTotal[i], 1f);
            if (_buffLeft[i] > 0f) continue;
            _buffTotal[i] = 0f;
            fill.parent.gameObject.SetActive(false);
        }
        TickRActive();
    }

    /// <summary>
    /// R 「켜짐」(11-05 사용자 「쿨타임이 속성 검이 끝난 후로 센다면 활성화 UI가 좋아야」) — 강화 신호가 살아 있고 재사용 대기가 아니면
    /// 칸 전체 금빛 부채꼴이 남은 시간만큼 줄고 · 금빛 남은 초(재사용 숫자와 같은 칸 · 같은 글). 끝나면 숨기고 · 재사용이 오면 예전 베일로.
    /// </summary>
    private void TickRActive()
    {
        // 대검 속성 검은 재사용을 버프가 꺼진 뒤로 미룬다(GiantSwordBuff.DeferCooldown) — 켜진 동안 R 추적기 = 0이라 대기가 아니다
        bool on = !_rOnCd && _buffTotal[1] > 0f && _buffLeft[1] > 0f;
        if (!on) { if (_rActiveLook) EndRActiveLook(); return; }
        if (_rCooldownBg == null) return;
        if (!_rActiveLook)
        {
            _rActiveLook = true;
            _rActiveTenths = -1;
            _rCooldownBg.SetActive(true);
            // 속성 검이면 그 속성의 밝은 색(칼날 빛과 같은 계열) · 아니면 금빛 — 켜질 때 한 번만 읽는다
            var el = GiantSwordBuff.ElementOf(GameRunBootstrapper.Instance?.Run?.Player);
            Color tone = el.HasValue ? ElementPalette.Bright(el.Value) : UITheme.Thin.Gold;
            if (_rCooldownFill != null) { _rFillBase = _rCooldownFill.color; _rCooldownFill.color = RActiveTone(tone); }
            if (_rCooldownTxt  != null) _rTxtBase = _rCooldownTxt.color;   // 숫자는 흰 잉크 그대로 — 금빛 부채꼴 위 금빛 숫자는 읽히지 않았다(11-05 실측 사진)
        }
        if (_rCooldownFill != null) _rCooldownFill.fillAmount = _buffLeft[1] / _buffTotal[1];
        int tenths = Mathf.CeilToInt(_buffLeft[1] * 10f);
        if (tenths == _rActiveTenths) return;
        _rActiveTenths = tenths;
        SetCooldownLabel(_rCooldownTxt, _buffLeft[1]);
    }

    /// <summary>R 「켜짐」을 걷는다 — 색을 돌려놓고, 재사용 대기가 아니면 칸 덮개도 숨긴다(대기면 SetSkillCooldown이 이어 그린다).</summary>
    private void EndRActiveLook()
    {
        _rActiveLook = false;
        if (_rCooldownFill != null) _rCooldownFill.color = _rFillBase;
        if (_rCooldownTxt  != null) _rCooldownTxt.color  = _rTxtBase;
        if (_rOnCd || _rCooldownBg == null) return;   // 대기면 SetSkillCooldown이 베일 · 숫자를 그린다
        _rCooldownBg.SetActive(false);
        if (_rCooldownTxt != null) _rCooldownTxt.text = string.Empty;
    }
}
