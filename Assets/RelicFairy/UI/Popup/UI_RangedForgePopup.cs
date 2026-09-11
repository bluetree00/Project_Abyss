using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보조무기(원거리) 선택 팝업 — <b>캐러셀</b>.
///
/// 기획 피벗: 주무기는 무형검(근접)이 기본 지급이라 여기서는 보조 원거리만 고른다.
///
/// 목록으로 쌓지 않고 <b>한 번에 무기 하나를 크게</b> 보여주고 좌우로 넘긴다 —
/// 무기 하나에 시선이 집중되고, 넘기는 손맛이 생기며, 큰 아이콘/큰 글씨로 정보가 잘 읽힌다.
/// 잠금 무기(석궁=특전)는 카드에 표시하되 확정만 막는다.
///
/// 슬라이드는 위치가 아니라 알파+스케일 크로스페이드(레이아웃과 안 싸운다). 시간정지 중이라 unscaled.
/// 가독성 머티리얼은 UI_RelicInfoPopup과 동일(얇은 폰트 두께 보정).
/// </summary>
public class UI_RangedForgePopup : UI_Popup
{
    // ── Constants ─────────────────────────────────────────────
    // 아트가 있으면 패널은 bg.png 비율(1289:1338 = 0.9634)을 지켜야 테두리와 상·하단 문장이 찌그러지지 않는다.
    // 높이는 "내용이 필요로 하는 세로(≈590) + 아트 프레임 여백(위 72 / 아래 52)"로 잡았다 —
    // 목업 비율만 따르면 프레임 여백만큼 내용이 눌려 이름·스탯이 겹친다(FixHeight 주석 참고).
    // 크기는 완성본 목업(전체 샷.png)을 재서 환산했다 — bg 실측 890×924, 패널 747 → 배율 0.826.
    // 세로 예산: 카드 내용(홀더198+이름34+배지20+태그22+스탯104+간격40+여백12 = 430)
    //          + 제목38 + 부제20 + 도트16 + 구분장식37 + 버튼45 + 행간격60 + 프레임여백(77+52) = 775.
    // 모자라면 카드 하단(스탯)이 도트·구분장식 위로 흘러넘친다.
    private const float SkinPanelWidth  = 747f;
    private const float SkinPanelHeight = 775f;
    private const float PanelWidth  = 620f;
    private const float PanelHeight = 640f;   // 카드 내용(아이콘+4줄 스탯)이 눌리지 않을 최소치
    private const float SlideTime   = 0.18f;

    // 아이콘 홀더 — 채움(356) 기준. 테두리·외곽선은 원본 비율대로 조금씩 크다.
    private const float HolderSize        = 198f;   // 목업 240 × 환산 0.826
    private const float HolderFrameScale  = 359f / 356f;
    private const float HolderOutlineScale = 373f / 356f;

    private const float ArrowW = 45f, ArrowH = 110f;   // 목업 55×133 × 환산 0.826
    private const float DividerW = 280f, DividerH = 37f;
    private const float EquipW = 137f, EquipH = 45f;   // 목업 166×55 × 환산 0.826 — 예전 160×52는 17% 컸다

    private static readonly Color PanelBg   = new(0.07f, 0.06f, 0.10f, 0.97f);
    private static readonly Color PanelLine = new(0.55f, 0.72f, 0.95f, 1f);
    private static readonly Color Accent    = new(0.62f, 0.80f, 1f, 1f);

    private static readonly Color TitleColor = new(0.82f, 0.90f, 1f, 1f);
    private static readonly Color BodyColor  = new(0.88f, 0.88f, 0.84f, 1f);
    private static readonly Color SubColor   = new(0.62f, 0.64f, 0.70f, 1f);
    private static readonly Color LockColor  = new(0.52f, 0.42f, 0.40f, 1f);
    private static readonly Color ArrowOn    = new(0.75f, 0.85f, 1f, 1f);
    private static readonly Color ArrowOff   = new(0.35f, 0.37f, 0.42f, 0.5f);
    private static readonly Color DotOn      = new(0.70f, 0.84f, 1f, 1f);
    private static readonly Color DotOff     = new(0.40f, 0.42f, 0.48f, 1f);
    private const string NumberHex = "#8FC7FF";

    // ── Nested ────────────────────────────────────────────────
    public struct Entry
    {
        public WeaponSO Weapon;
        public bool     Locked;
        public string   LockReason;
    }

    // ── Private ───────────────────────────────────────────────
    [SerializeField] private RectTransform _stage;        // 무대(레이아웃 자식, 고정) — 슬라이드로 만지지 않는다
    [SerializeField] private RectTransform _slider;       // 무대 안에서 좌우로 밀려 들어오는 카드 컨테이너(레이아웃 무관)
    [SerializeField] private Image         _stageGlow;    // 무대 배경 — 무기 테마색으로 은은하게 물든다
    [SerializeField] private CanvasGroup   _cardGroup;    // 현재 카드 알파/스케일
    [SerializeField] private Image         _icon;
    [SerializeField] private TMP_Text      _name;
    [SerializeField] private TMP_Text      _badge;
    [SerializeField] private TMP_Text      _tag;
    [SerializeField] private TMP_Text      _stats;
    [SerializeField] private Button        _prev, _next;
    [SerializeField] private RectTransform _dots;
    [SerializeField] private Button        _confirm;
    [SerializeField] private Image         _confirmBg;
    [SerializeField] private TMP_Text      _confirmLabel;

    private WeaponForgeSkinSO _skin;

    private readonly List<Entry> _entries = new();
    private readonly List<Image> _dotImgs = new();
    private readonly Dictionary<Button, TMP_Text> _arrowGlyphs = new();   // SetArrow가 색을 바꾼다(버튼 2개)
    private readonly Dictionary<Button, Image>    _arrowArts   = new();   // 아트 화살표는 글리프 대신 이 이미지를 흐린다
    private readonly StringBuilder _sb = new();
    private static Material s_textMat;

    private int _index;
    private int _slideGen;
    private int _slideDir;   // 넘긴 방향(+1 오른쪽 / -1 왼쪽) — 카드가 그쪽에서 밀려 들어온다
    private UniTaskCompletionSource<WeaponSO> _tcs;

    public override bool BlocksGameplay => true;

    // ── Init ──────────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        _skin = UISkin.WeaponForge;
        BuildLayout();
    }

    // ── Public API ────────────────────────────────────────────
    public void Setup(IReadOnlyList<Entry> entries)
    {
        _entries.Clear();
        if (entries != null)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Weapon != null) _entries.Add(entries[i]);

        _index = 0;
        _tcs = new UniTaskCompletionSource<WeaponSO>();

        BuildDots();
        ShowCard(_index, instant: true);
    }

    public UniTask<WeaponSO> WaitForChoiceAsync() => _tcs.Task;

    /// <summary>
    /// 정상 경로(Complete) 없이 파괴돼도 대기를 끝낸다 — 씬 전환·CloseAllPopupUI 등.
    /// 이게 없으면 _tcs가 영구 미완료라 WaitForChoiceAsync가 무한 대기하고 모루가 영구히 잠긴다.
    /// null = Cancel과 같은 값이라 안전한 기본값이다. Complete가 먼저면 TrySetResult가 무시된다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        _tcs?.TrySetResult(null);
    }

    // ── 캐러셀 ────────────────────────────────────────────────
    private void Step(int dir)
    {
        int next = _index + dir;
        if (next < 0 || next >= _entries.Count) return;
        _index = next;
        _slideDir = dir;
        ShowCard(_index, instant: false);
    }

    private void ShowCard(int i, bool instant)
    {
        if (i < 0 || i >= _entries.Count) return;
        var e = _entries[i];
        var w = e.Weapon;

        // 무기 테마색(없으면 기본 청색). 잠금이면 색을 죽인다.
        Color theme = w.uiThemeColor.a > 0.01f ? w.uiThemeColor : Accent;
        Color accent = e.Locked ? LockColor : theme;

        _icon.sprite  = w.icon;
        _icon.enabled = w.icon != null;   // 아이콘이 없는 무기는 흰 네모 대신 빈 홀더로
        _icon.color  = w.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

        _name.text  = w.displayName;
        _name.color = e.Locked ? LockColor : accent;

        _badge.gameObject.SetActive(e.Locked);
        if (e.Locked) _badge.text = "■ " + (string.IsNullOrEmpty(e.LockReason) ? "잠금" : e.LockReason);

        _tag.text   = w.tagline ?? string.Empty;
        _tag.color  = e.Locked ? LockColor : SubColor;

        _stats.text = e.Locked ? "<color=#8A6B6B>특전으로 해금되는 무기</color>" : StatBlock(w);

        // ⚠️ 코드로 구운 스프라이트(UIProceduralSprites)는 <b>프리팹에 직렬화되지 않는다</b> —
        //    베이크된 프리팹에서는 null로 되살아나 각진 색판으로 보였다(2026-09-10 게임 화면의 초록 판때기).
        //    그래서 짓는 곳이 아니라 <b>쓰는 곳</b>에서 한 번 붙인다.
        if (_stageGlow.sprite == null)
        {
            _stageGlow.sprite = UIProceduralSprites.RoundedRect(radius: 36f, feather: 28f, size: 160);
            _stageGlow.type   = Image.Type.Sliced;
        }

        // 테마색으로 톤 통일 — 무대 배경 은은하게 물들이고, 확정 버튼도 이 색을 따른다.
        _stageGlow.color = e.Locked
            ? new Color(LockColor.r, LockColor.g, LockColor.b, 0.05f)
            : new Color(accent.r, accent.g, accent.b, 0.08f);

        // 화살표 활성/비활성 — 양 끝에서 못 넘어감을 색으로 알린다.
        SetArrow(_prev, i > 0);
        SetArrow(_next, i < _entries.Count - 1);
        UpdateDots(i);
        RefreshConfirm(e, theme);

        if (instant)
        {
            _cardGroup.alpha = 1f;
            _slider.localScale = Vector3.one;
            _slider.anchoredPosition = Vector2.zero;
            return;
        }
        SlideInAsync(++_slideGen, _slideDir).Forget();
    }

    /// <summary>
    /// 넘긴 방향에서 카드가 밀려 들어온다 — 오른쪽으로 넘기면 오른쪽에서 슬라이드.
    /// 위치+알파+스케일을 함께 움직여 "물리적으로 넘어왔다"는 방향감을 준다(레이아웃과 안 싸우게
    /// anchoredPosition만 만진다). 시간정지 중이라 unscaled.
    /// </summary>
    private async UniTaskVoid SlideInAsync(int gen, int dir)
    {
        float from = dir >= 0 ? 90f : -90f;   // 오른쪽 넘김 → 오른쪽(+x)에서 진입
        float t = 0f;
        while (t < 1f)
        {
            if (gen != _slideGen) return;
            t += Time.unscaledDeltaTime / SlideTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);   // EaseOutCubic

            _cardGroup.alpha         = e;
            _slider.localScale       = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            _slider.anchoredPosition = new Vector2(Mathf.Lerp(from, 0f, e), 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _slider.anchoredPosition = Vector2.zero;
    }

    private string StatBlock(WeaponSO w)
    {
        _sb.Clear();
        Row("공격력", w.baseAttack.ToString("0"));
        Row("연사",   w.attackSpeed.ToString("0.0") + " /초");
        Row("사거리", w.attackRange.ToString("0") + " m");
        Row("치명타", w.critChance.ToString("0") + "%");
        return _sb.ToString();
    }

    private void Row(string label, string value)
    {
        if (_sb.Length > 0) _sb.Append('\n');
        // 라벨 폭을 스페이스로 대충 맞춰 값을 정렬(모노 느낌). 숫자만 강조.
        _sb.Append("<color=#9A9C9F>").Append(label).Append("</color>   ")
           .Append("<color=").Append(NumberHex).Append('>').Append(value).Append("</color>");
    }

    // ── 상태 표시 ─────────────────────────────────────────────
    private void SetArrow(Button btn, bool on)
    {
        if (btn == null) return;
        btn.interactable = on;

        // 아트 화살표는 알파로, 글리프 화살표는 색으로 비활성을 알린다.
        if (_arrowArts.TryGetValue(btn, out var art) && art != null)
            art.color = on ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        else if (_arrowGlyphs.TryGetValue(btn, out var glyph) && glyph != null)
            glyph.color = on ? ArrowOn : ArrowOff;
    }

    private void UpdateDots(int active)
    {
        for (int i = 0; i < _dotImgs.Count; i++)
            _dotImgs[i].color = i == active ? DotOn : DotOff;
    }

    private void RefreshConfirm(Entry e, Color theme)
    {
        bool ok = !e.Locked;
        if (_confirm != null) _confirm.interactable = ok;
        if (_confirmLabel != null)
            _confirmLabel.color = ok ? TitleColor : new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.35f);
        // 확정 버튼: 아트가 있으면 테마색을 곱하지 않는다(황동 명판이 파랗게 물든다) — 알파로만 잠금을 알린다.
        if (_confirmBg != null)
            _confirmBg.color = _skin?.equipButton != null
                ? (ok ? Color.white : new Color(1f, 1f, 1f, 0.35f))
                : (ok ? new Color(theme.r * 0.32f, theme.g * 0.32f, theme.b * 0.40f, 1f)
                      : new Color(0.14f, 0.15f, 0.18f, 1f));
    }

    private void Confirm()
    {
        if (_index < 0 || _index >= _entries.Count)
        {
            Debug.LogWarning($"[RangedForge] 장착 불가 — 표시 중인 후보가 없다(index={_index}, 후보 {_entries.Count}개).");
            return;
        }

        var e = _entries[_index];
        // 잠금 무기(석궁=특전)는 버튼 자체가 비활성이라 여기까지 오지 않는 게 정상이다.
        // 그래도 '눌리지 않는다'는 신고가 오면 원인이 잠금인지 아닌지 바로 갈리도록 남긴다.
        if (e.Locked)
        {
            Debug.LogWarning($"[RangedForge] 장착 불가 — '{e.Weapon?.displayName}'는 잠금({e.LockReason}). 좌우로 넘겨 해금된 무기를 고를 것.");
            return;
        }

        Complete(e.Weapon);
    }

    private void Cancel() => Complete(null);

    private void Complete(WeaponSO result)
    {
        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _tcs?.TrySetResult(result);
        ClosePopupUI();
    }

    // ── 레이아웃 ───────────────────────────────────────────────
    private void BuildLayout()
    {
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        var root = (RectTransform)transform;

        // 프리팹 루트는 화면 한가운데 100×100이다 — 다른 팝업들이 첫 줄에서 루트부터 펴는 이유가 이것이다.
        // 안 펴면 아래 암막이 그 100×100만 덮어 뒤의 월드·HUD가 그대로 살아 있다.
        Stretch(root);

        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.8f));   // 팝업 공통 암막(ShopUIStyle.Veil과 동일)
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        bool skinned = _skin?.panelBackground != null;

        var panel = NewImage("Panel", root, skinned ? Color.white : PanelBg);
        // 세로 레이아웃 그룹이 배치하는 판이라 글꼴은 키우지 않는다 —
        // 상자가 안 커지는데 글자만 키우면 넘친다.
        panel.gameObject.AddComponent<UIWindowFitter>()
             .Configure(scaleFonts: false, maxScale: UIWindowFitter.ContentScreen, scaleTransform: true);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = skinned ? new Vector2(SkinPanelWidth, SkinPanelHeight)
                                : new Vector2(PanelWidth, PanelHeight);

        // 아트에 테두리가 이미 그려져 있다 — Outline을 겹치면 이중 테두리가 된다.
        if (skinned) ShopUIStyle.Skin(panel, _skin.panelBackground);
        else
        {
            var line = panel.gameObject.AddComponent<Outline>();
            line.effectColor = PanelLine;
            line.effectDistance = new Vector2(2f, -2f);
        }

        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        // 아트 여백은 bg.png의 프레임 안쪽 비율(위 0.100 / 아래 0.067 / 좌우 얇은 금선)에서 뽑았다.
        // 합성본은 제목 위 0.17(775 기준 130)이지만 <b>합성본엔 스탯 4줄이 없다</b> —
        // 코드가 더한 이름·설명·스탯을 담으면 카드가 무대를 97px 넘겨 도트·구분선 위로 흘렀다(2026-09-10 게임 화면).
        // 아트 프레임 안쪽은 위 0.067(52)까지라 96/92는 여전히 프레임 안이다. 그만큼 무대가 넓어진다.
        v.padding = skinned ? new RectOffset(30, 30, 96, 92) : new RectOffset(28, 28, 24, 22);
        v.spacing = 12f;
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;

        // 제목 + 안내
        var title = NewText("Title", prt, 30f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = "보조 무기 — 원거리";
        var sub = NewText("Sub", prt, 16f, SubColor, FontStyles.Normal, TextAlignmentOptions.Center);
        sub.text = "주무기 <color=#CFC0A0>무명의 형상</color>은 이미 손에 있다. 곁에 둘 하나를 고른다.";

        BuildViewer(prt);   // ◀  [카드]  ▶
        BuildDotsRow(prt);
        BuildDivider(prt);
        BuildButtons(prt);
    }

    /// <summary>이름·스탯과 버튼 사이 구분 장식. 아트가 없으면 줄 자체를 만들지 않는다.</summary>
    private void BuildDivider(RectTransform parent)
    {
        if (_skin?.divider == null) return;

        var row = NewRect("Divider", parent);
        FixHeight(row.gameObject, DividerH);

        var img = NewImage("Art", row, Color.white);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(DividerW, DividerH);
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, _skin.divider);
    }

    /// <summary>◀ 화살표 · 카드 무대 · ▶ 화살표 — 가로 3분할.</summary>
    private void BuildViewer(RectTransform parent)
    {
        var row = NewRect("Viewer", parent);
        row.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        // 합성본에서 화살표는 판 가장자리(0.047)가 아니라 안쪽(0.118)에 선다 — 747 기준 좌우 53.
        h.padding = new RectOffset(53, 53, 0, 0);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _prev = BuildArrow(row, "◀", -1, _skin?.arrowLeft);

        // 무대 — 레이아웃이 폭을 정하는 자식. 이건 절대 슬라이드로 만지지 않는다.
        var stage = NewRect("Stage", row.transform);
        stage.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _stage = stage;

        // 슬라이더 — 무대를 꽉 채운 오버레이. 레이아웃 그룹의 자식이 아니라(스트레치) 위치를
        // 마음대로 옮겨도 매 프레임 되돌려지지 않는다. 슬라이드/스케일/알파를 전부 이 위에 건다.
        _slider = NewRect("Slider", stage);
        Stretch(_slider);
        _cardGroup = _slider.gameObject.AddComponent<CanvasGroup>();

        // 무대 배경 — 무기 테마색으로 은은하게 물드는 판(카드보다 뒤).
        _stageGlow = NewImage("Glow", _slider, new Color(1f, 1f, 1f, 0f));
        Stretch(_stageGlow.rectTransform);
        // 각진 사각형이면 알파가 낮아도 '색판'으로 읽힌다(게임 화면에서 초록 판때기로 보였다).
        // 모서리를 둥글리고 가장자리를 흐려 <b>물든 자국</b>처럼 보이게 한다.
        // ⚠️ 9-slice 경계 = 반경 + 소프트다. 기본 96px 스프라이트에 36+28=64를 쓰면 좌우 경계가 서로 겹쳐
        //    슬라이스가 깨진다(가장자리가 안 흐려졌다). 경계 128이 들어가도록 판을 160으로 굽는다.
        _stageGlow.sprite = UIProceduralSprites.RoundedRect(radius: 36f, feather: 28f, size: 160);
        _stageGlow.type   = Image.Type.Sliced;
        _stageGlow.raycastTarget = false;
        // 배경 판이라 세로 레이아웃의 한 칸을 차지하면 안 된다 — 카드 내용이 그만큼 밀려 눌린다.
        _stageGlow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        BuildCardBody(_slider);

        _next = BuildArrow(row, "▶", +1, _skin?.arrowRight);
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — 계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.
    ///
    /// <para>화살표 두 개는 <b>형제 이름이 같다</b>(둘 다 "Arrow"). 순서로 가른다 —
    /// 빌더가 왼쪽(-1)을 먼저 만들므로 프리팹에서도 그 순서로 굳어 있다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _skin = UISkin.WeaponForge;

        _arrowGlyphs.Clear();
        _arrowArts.Clear();

        var row = _stage != null ? _stage.parent : null;
        int dir = -1;
        if (row != null)
        {
            foreach (Transform child in row)
            {
                if (child.name != "Arrow") continue;
                if (!child.TryGetComponent<Button>(out var arrow)) continue;

                int captured = dir;
                arrow.onClick.RemoveAllListeners();
                arrow.onClick.AddListener(() => Step(captured));

                var glyph = child.Find("G")?.GetComponent<TMP_Text>();
                if (glyph != null) _arrowGlyphs[arrow] = glyph;
                var art = child.Find("Art")?.GetComponent<Image>();
                if (art != null) _arrowArts[arrow] = art;

                if (captured < 0) _prev = arrow; else _next = arrow;
                dir = 1;
            }
        }

        if (_confirm != null)
        {
            _confirm.onClick.RemoveAllListeners();
            _confirm.onClick.AddListener(Confirm);
        }

        var cancel = transform.Find("Panel/Buttons/Btn_Cancel")?.GetComponent<Button>();
        if (cancel != null)
        {
            cancel.onClick.RemoveAllListeners();
            cancel.onClick.AddListener(Cancel);
        }
    }

    /// <summary>
    /// 화살표 버튼. 아트가 있으면 판정면(box)은 투명하게 두고 <b>자식 이미지</b>에 아트를 얹는다 —
    /// box는 레이아웃이 세로로 늘리는 칸이라 거기 아트를 직접 넣으면 화살표가 세로로 늘어난다.
    /// 비활성 표시는 아트면 알파, 아니면 글리프 색으로 한다(SetArrow).
    /// </summary>
    private Button BuildArrow(RectTransform parent, string glyph, int dir, Sprite art)
    {
        var box = NewImage("Arrow", parent, new Color(1f, 1f, 1f, art != null ? 0f : 0.04f));
        var le = box.gameObject.AddComponent<LayoutElement>();
        float w = art != null ? ArrowW + 10f : 52f;
        le.preferredWidth = w; le.minWidth = w;

        var btn = box.gameObject.AddComponent<Button>();
        btn.targetGraphic = box;
        btn.onClick.AddListener(() => Step(dir));

        var t = NewText("G", box.rectTransform, 34f, ArrowOn, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = glyph;
        Stretch(t.rectTransform);
        _arrowGlyphs[btn] = t;

        if (art != null)
        {
            t.gameObject.SetActive(false);   // 아트에 화살촉이 그려져 있어 글리프는 중복이다

            var img = NewImage("Art", box.rectTransform, Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ArrowW, ArrowH);
            img.raycastTarget = false;
            ShopUIStyle.Skin(img, art);
            _arrowArts[btn] = img;
        }

        return btn;
    }

    private void BuildCardBody(RectTransform stage)
    {
        var v = stage.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.padding = new RectOffset(6, 6, 6, 6);
        v.childAlignment = TextAnchor.MiddleCenter;   // 합성본은 홀더가 무대 세로 중앙(0.54)에 온다
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;

        // 무대에 들어갈 세로 예산이 카드 내용보다 작으면 VerticalLayoutGroup이 자식을 최소높이까지
        // 눌러버린다. TMP의 최소높이는 0이라 글자는 그대로 그려지면서 칸만 사라져 서로 겹쳐 보였다
        // (이름·설명·스탯이 한 덩어리로 뭉치던 원인). 그래서 각 줄에 높이를 못 박는다.
        bool hasHolder = _skin?.holderFill != null;
        var iconBox = NewRect("IconBox", stage);
        // 글자 줄은 높이를 못 박지만(눌리면 겹친다) <b>아이콘 칸만은 줄어들 수 있어야 한다</b> —
        // 무대가 좁을 때 누군가는 양보해야 하고, 양보해도 정보가 사라지지 않는 것은 아이콘뿐이다.
        // min < preferred면 VerticalLayoutGroup이 이 칸에서만 모자란 만큼을 뺀다.
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredHeight = hasHolder ? HolderSize : 130f;
        iconLe.minHeight       = 96f;
        iconLe.flexibleHeight  = 0f;

        // 아트 홀더는 정사각형이라 폭 전체를 쓰면 안 된다 — 가운데 정사각 칸을 따로 만든다.
        RectTransform host = iconBox;
        if (hasHolder)
        {
            host = NewRect("Holder", iconBox);
            // 칸을 꽉 채우되 정사각을 지킨다(AspectRatioFitter). 고정 크기로 두면 칸이 줄어도
            // 홀더는 그대로라 아래 글자와 겹친다.
            Stretch(host);
            var fit = host.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;

            var fill = NewImage("Fill", host, Color.white);
            Stretch(fill.rectTransform);
            fill.raycastTarget = false;
            ShopUIStyle.Skin(fill, _skin.holderFill);
        }

        _icon = NewImage("Icon", host, Color.white);
        _icon.enabled = false;   // 항목이 적용되기 전엔 그리지 않는다 — 빈 Image는 흰 네모로 뜬다
        // 홀더 안에서는 테두리를 먹지 않도록 안쪽으로 들여넣는다.
        if (hasHolder) ShopUIStyle.Stretch(_icon.rectTransform, 22f);
        else           Stretch(_icon.rectTransform);
        _icon.preserveAspect = true;
        // 카드는 표시 전용이다 — 누를 수 있는 건 좌우 화살표와 하단 버튼뿐.
        // 레이캐스트를 켜두면 카드가 조금만 넘쳐도 그 아래 버튼을 덮어 '안 눌리는' 상태가 된다.
        _icon.raycastTarget = false;

        // 테두리·외곽선은 아이콘 위로 지나가야 액자가 된다(원본 비율대로 조금씩 크다).
        // 채움이 없으면 칸 크기가 홀더 기준이 아니라서 겹쳐봐야 어긋난다 — 세트로만 얹는다.
        if (hasHolder)
        {
            AddHolderOverlay(host, "Frame",   _skin.holderFrame,   HolderFrameScale);
            AddHolderOverlay(host, "Outline", _skin.holderOutline, HolderOutlineScale);
        }

        // 이름 + 잠금 배지
        _name = NewText("Name", stage, 26f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _name.textWrappingMode = TextWrappingModes.NoWrap;
        _name.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_name.gameObject, 34f);

        _badge = NewText("Badge", stage, 15f, LockColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _badge.textWrappingMode = TextWrappingModes.NoWrap;
        FixHeight(_badge.gameObject, 20f);

        // 한 줄 설명
        _tag = NewText("Tag", stage, 14f, SubColor, FontStyles.Italic, TextAlignmentOptions.Center);
        _tag.textWrappingMode = TextWrappingModes.NoWrap;
        _tag.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_tag.gameObject, 22f);

        // 스탯 블록(4줄 세로 정렬)
        _stats = NewText("Stats", stage, 16f, BodyColor, FontStyles.Normal, TextAlignmentOptions.Center);
        _stats.lineSpacing = 6f;
        FixHeight(_stats.gameObject, 92f);   // 4줄 × (16 + 줄간격 6) = 88 + 여유
    }

    /// <summary>부모 한가운데 고정 크기 칸을 만든다(레이아웃이 늘리지 못하게).</summary>
    private static void CenterBox(RectTransform rt, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>홀더 위에 겹치는 테두리/외곽선. 아트가 없으면 만들지 않는다.</summary>
    private static void AddHolderOverlay(RectTransform host, string name, Sprite art, float scale)
    {
        if (art == null) return;

        var img = NewImage(name, host, Color.white);
        // 홀더보다 scale배 크게 — <b>앵커를 0~1 밖으로</b> 벌려 홀더 크기가 바뀌어도 비율이 유지된다.
        // 고정 크기(CenterBox)로 두면 홀더가 줄어들 때 테두리만 옛 크기로 남아 액자가 어긋난다.
        float over = (scale - 1f) * 0.5f;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(-over, -over);
        rt.anchorMax = new Vector2(1f + over, 1f + over);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, art);
    }

    /// <summary>레이아웃 그룹이 눌러도 줄지 않도록 높이를 고정한다(min=preferred, flexible=0).</summary>
    private static void FixHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight       = height;
        le.preferredHeight = height;
        le.flexibleHeight  = 0f;
    }

    private void BuildDotsRow(RectTransform parent)
    {
        _dots = NewRect("Dots", parent);
        _dots.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        var h = _dots.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;
    }

    private void BuildDots()
    {
        for (int i = 0; i < _dotImgs.Count; i++) Destroy(_dotImgs[i].gameObject);
        _dotImgs.Clear();

        for (int i = 0; i < _entries.Count; i++)
        {
            var dot = NewImage("Dot", _dots, DotOff);
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 10f; le.preferredHeight = 10f;
            le.minWidth = 10f; le.minHeight = 10f;
            _dotImgs.Add(dot);
        }
    }

    private void BuildButtons(RectTransform parent)
    {
        bool skinned = _skin?.equipButton != null;

        var row = NewRect("Buttons", parent);
        FixHeight(row.gameObject, skinned ? EquipH : 56f);   // 남는 세로를 버튼이 먹어 커지지 않게 고정
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        // 아트 버튼은 원본 비율이 있어 늘리면 구워진 글자가 깨진다 — 폭을 각자 고정한다.
        h.childControlWidth = true;  h.childForceExpandWidth  = !skinned;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _confirmBg = NewImage("Btn_Confirm", row, skinned ? Color.white : new Color(0.16f, 0.30f, 0.44f, 1f));
        _confirm = _confirmBg.gameObject.AddComponent<Button>();
        _confirm.targetGraphic = _confirmBg;
        _confirm.onClick.AddListener(Confirm);

        _confirmLabel = NewText("L", _confirmBg.rectTransform, 22f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _confirmLabel.text = "확정";
        Stretch(_confirmLabel.rectTransform);

        if (skinned)
        {
            ShopUIStyle.Skin(_confirmBg, _skin.equipButton);
            FixWidth(_confirmBg.gameObject, EquipW);
            // 아트에 "장착하기"가 구워져 있다 — 코드 라벨을 남기면 두 벌이 겹친다.
            _confirmLabel.gameObject.SetActive(false);

            if (_skin.equipGlyph != null)
            {
                var glyph = NewImage("Glyph", _confirmBg.rectTransform, Color.white);
                var grt = glyph.rectTransform;
                grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
                grt.pivot = new Vector2(0f, 0.5f);
                grt.sizeDelta = new Vector2(22f, 22f);
                grt.anchoredPosition = new Vector2(24f, 0f);
                glyph.raycastTarget = false;
                glyph.preserveAspect = true;
                ShopUIStyle.Skin(glyph, _skin.equipGlyph);
            }
        }
        else
        {
            var cl = _confirmBg.gameObject.AddComponent<Outline>();
            cl.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
            cl.effectDistance = new Vector2(1.5f, -1.5f);
        }

        // 취소 — 완성본엔 없지만 이 팝업은 ESC로도 못 닫아서(CloseOnEscape=false) 유일한 탈출구다.
        // 전용 아트가 없으므로 주 버튼보다 작고 수수하게 둔다.
        var cancelBg = NewImage("Btn_Cancel", row, new Color(0.16f, 0.16f, 0.19f, 0.92f));
        var cancel = cancelBg.gameObject.AddComponent<Button>();
        cancel.targetGraphic = cancelBg;
        cancel.onClick.AddListener(Cancel);
        if (skinned) FixWidth(cancelBg.gameObject, 110f);
        var t = NewText("L", cancelBg.rectTransform, skinned ? 18f : 22f, BodyColor, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = "취소";
        Stretch(t.rectTransform);
    }

    /// <summary>레이아웃 그룹이 늘리지 못하게 폭을 고정한다(min=preferred, flexible=0).</summary>
    private static void FixWidth(GameObject go, float width)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth       = width;
        le.preferredWidth = width;
        le.flexibleWidth  = 0f;
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private TMP_Text NewText(string name, Transform parent, float size, Color color,
                             FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        // 글자가 판 밖으로 나가지 않게 하는 안전망 — TMP 기본 넘침은 잘라내지 않고 <b>바깥에 그린다</b>.
        // 최대를 설계 크기로 묶으므로 커지지는 않고, 안 들어갈 때만 줄어든다.
        t.enableAutoSizing = true;
        t.fontSizeMax      = size;
        t.fontSizeMin      = Mathf.Max(9f, size * 0.55f);
        ApplyReadableMaterial(t);
        return t;
    }

    private static void ApplyReadableMaterial(TMP_Text t)
    {
        if (t.font == null) return;
        if (s_textMat == null)
        {
            var src = t.fontSharedMaterial;
            if (src == null) return;
            s_textMat = new Material(src) { name = src.name + " (RangedForge)" };
            s_textMat.EnableKeyword("OUTLINE_ON");
            s_textMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.07f);
            s_textMat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.03f, 0.03f, 0.05f, 1f));
            s_textMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.06f);
        }
        t.fontSharedMaterial = s_textMat;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
