using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 패널 (Canvas_Popup, Addressable "UI_RefineryPanel").
///
/// 원석을 넣고 돌려 판을 강화하는 특수 룬(존핵)을 만든다. 로직은 <see cref="RefineryService"/>(런 스코프),
/// 이 패널은 그 표현: 속성 젬 선택 → 돌리기 → 등급 리빌 → 결과/돌발 이벤트 표시 → 보관함으로.
/// 설계: 바탕화면 기획/RelicFairy_기획_정제소_속성응축.md
/// </summary>
public sealed class UI_RefineryPanel : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => true;

    private const float WindowW = 1040f;
    private const float WindowH = 620f;
    private const float AltarSize = 220f;

    private static readonly Color AltarFill   = new(0.10f, 0.08f, 0.14f, 1f);
    private static readonly Color HeatDefault = new(0.92f, 0.64f, 0.29f, 1f);

    private RefineryService _svc;
    private string _selectedElement;
    private bool _busy;
    private bool _built;

    private Transform _root;
    private readonly List<(string id, Image jewel, GameObject go)> _gems = new();
    private Image    _altarGlow;
    private Image    _resultBox;
    private Image    _resultSym;
    private TMP_Text _resultAmt;
    private TMP_Text _rarLine;
    private OddsBarView    _oddsBar;
    private FeverGaugeView _feverGauge;
    private TMP_Text _costText;
    private TMP_Text _hint;
    private TMP_Text _perkText;
    private Image    _spinBtnImg;
    private TMP_Text _spinLabel;
    private GameObject    _eventBanner;
    private RectTransform _eventBannerRT;
    private Image         _eventBannerImg;   // MakeFrame의 inner(채움) — 종류별 색/아트 스왑 대상
    private TMP_Text      _eventText;
    private GameObject    _reforgeBtn;
    private TMP_Text      _reservedText;     // 과열·불티가 '다음 회 예약됨'을 알리는 표시

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        _svc = GameRunBootstrapper.Instance?.Run?.Refinery;
        BuildUI();
        Refresh();
    }

    /// <summary>방 특전은 이 패널을 닫으면 사라진다 — 상시 탭(룬판 버튼)으로 다시 열면 특전 없이 열려야 한다.</summary>
    public override void ClosePopupUI()
    {
        _svc?.ClearRoomPerk();
        base.ClosePopupUI();
    }

    // ── Build ──

    private void BuildUI()
    {
        if (_built) return;
        _built = true;

        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _root = window.transform;

        var title = ShopUIStyle.MakeText(_root, "Title", 26f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(34f, -24f), new Vector2(500f, 34f));
        title.text = "정제소 — 응축 제단";

        var sub = ShopUIStyle.MakeText(_root, "Sub", 14f, FontStyles.Normal,
            TextAlignmentOptions.Left, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(35f, -56f), new Vector2(620f, 22f));
        sub.text = "원석을 넣고 돌린다 — 판을 강화하는 특수 룬 하나가 응결된다";

        BuildGems();
        BuildAltar();
        BuildSide();
        BuildFooter();
        BuildEvent();

        AddClick(veil.gameObject, () => { if (!_busy) ClosePopupUI(); });
    }

    private void BuildGems()
    {
        var order = ElementDef.Order;
        int n = order.Count;
        float gap = 12f, w = 84f;
        float totalW = n * w + (n - 1) * gap;
        float startX = -totalW * 0.5f + w * 0.5f;

        for (int i = 0; i < n; i++)
        {
            string id = order[i];
            var e = ElementDef.GetById(id);
            Color col = ElementDef.IdColor(id, ShopUIStyle.TextDim);

            var card = ShopUIStyle.MakeFrame(_root, $"Gem_{id}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var rt = (RectTransform)card.transform.parent;
            ShopUIStyle.Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(startX + i * (w + gap), -96f), new Vector2(w, 70f));

            var jewel = ShopUIStyle.MakeImage(card.transform, "Jewel", col);
            ShopUIStyle.Anchor(jewel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(30f, 30f));

            var nm = ShopUIStyle.MakeText(card.transform, "Name", 13f, FontStyles.Bold,
                TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            ShopUIStyle.Anchor(nm.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(w, 18f));
            nm.text = e != null ? e.Name : id;

            string cap = id;
            AddClick(rt.gameObject, () => Select(cap));
            _gems.Add((id, jewel, rt.gameObject));
        }
    }

    private void BuildAltar()
    {
        _altarGlow = ShopUIStyle.MakeImage(_root, "Altar", new Color(HeatDefault.r, HeatDefault.g, HeatDefault.b, 0.18f));
        ShopUIStyle.Anchor(_altarGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(AltarSize, AltarSize));

        var inner = ShopUIStyle.MakeImage(_altarGlow.transform, "Core", AltarFill);
        ShopUIStyle.Anchor(inner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(AltarSize - 60f, AltarSize - 60f));

        // 결과 룬 박스(초기 숨김)
        _resultBox = ShopUIStyle.MakeImage(_altarGlow.transform, "Result", AltarFill);
        ShopUIStyle.Anchor(_resultBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(96f, 96f));
        _resultSym = ShopUIStyle.MakeImage(_resultBox.transform, "Sym", Color.white);
        ShopUIStyle.Anchor(_resultSym.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 12f), new Vector2(38f, 38f));
        _resultAmt = ShopUIStyle.MakeText(_resultBox.transform, "Amt", 18f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white);
        ShopUIStyle.Anchor(_resultAmt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 12f), new Vector2(96f, 22f));
        _resultBox.gameObject.SetActive(false);

        _rarLine = ShopUIStyle.MakeText(_root, "RarLine", 16f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_rarLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -108f), new Vector2(700f, 24f));
    }

    /// <summary>우측 상태 컬럼 — 피버(위) / 확률(아래). 정제소 재미의 두 축을 눈에 보이게 세운다.</summary>
    private void BuildSide()
    {
        var right = new Vector2(1f, 0.5f);
        const float colW = 268f;

        _feverGauge = FeverGaugeView.Create(_root, right, right, right,
            new Vector2(-36f, 92f), new Vector2(colW, 78f));

        _oddsBar = OddsBarView.Create(_root, right, right, right,
            new Vector2(-36f, -18f), new Vector2(colW, 128f));
    }

    private void BuildFooter()
    {
        _costText = ShopUIStyle.MakeText(_root, "Cost", 16f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(_costText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-150f, 46f), new Vector2(180f, 26f));

        var spin = ShopUIStyle.MakeFrame(_root, "SpinBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.GoldPillBg, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)spin.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(60f, 42f), new Vector2(200f, 56f));
        _spinBtnImg = spin;
        _spinLabel = ShopUIStyle.MakeText(spin.transform, "Label", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, new Color(0.14f, 0.09f, 0.02f));
        ShopUIStyle.Stretch(_spinLabel.rectTransform);
        _spinLabel.text = "돌리기";
        AddClick(spin.transform.parent.gameObject, OnSpinClicked);

        _hint = ShopUIStyle.MakeText(_root, "Hint", 13f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 16f), new Vector2(700f, 20f));
        _hint.text = "강화할 존(속성)을 하나 고르세요";

        // 방 특전(정제소 방에서만) — 상시 탭에서는 숨김
        _perkText = ShopUIStyle.MakeText(_root, "Perk", 14f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.RarityGlow(ItemRarity.Legendary));
        ShopUIStyle.Anchor(_perkText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(35f, -84f), new Vector2(460f, 22f));
    }

    private void BuildEvent()
    {
        _eventBannerImg = ShopUIStyle.MakeFrame(_root, "EventBanner",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: false);
        _eventBanner   = _eventBannerImg.transform.parent.gameObject;
        _eventBannerRT = (RectTransform)_eventBanner.transform;
        ShopUIStyle.Anchor(_eventBannerRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-90f, 106f), new Vector2(360f, 40f));
        _eventText = ShopUIStyle.MakeText(_eventBanner.transform, "EvTxt", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.RarityGlow(ItemRarity.Epic));
        ShopUIStyle.Stretch(_eventText.rectTransform);

        // 예약 배지 — 과열·불티는 배너가 사라진 뒤에도 다음 회까지 효과가 남는다.
        // 이게 없으면 "아까 뭐가 걸렸더라?"가 되어 이벤트의 값어치가 절반으로 준다.
        _reservedText = ShopUIStyle.MakeText(_root, "Reserved", 12.5f, FontStyles.Bold,
            TextAlignmentOptions.Right, ShopUIStyle.RarityGlow(ItemRarity.Legendary));
        ShopUIStyle.Anchor(_reservedText.rectTransform,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-36f, 148f), new Vector2(268f, 20f));
        _reservedText.richText = true;

        var re = ShopUIStyle.MakeFrame(_root, "ReforgeBtn",
            ShopUIStyle.RarityGlow(ItemRarity.Rare), ShopUIStyle.CardFill, 2f, raycast: true);
        _reforgeBtn = re.transform.parent.gameObject;
        ShopUIStyle.Anchor((RectTransform)_reforgeBtn.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(150f, 106f), new Vector2(140f, 40f));
        var rl = ShopUIStyle.MakeText(re.transform, "Label", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.RarityGlow(ItemRarity.Rare));
        ShopUIStyle.Stretch(rl.rectTransform);
        rl.text = "↻ 다시 굴리기";
        AddClick(_reforgeBtn, OnReforgeClicked);

        _eventBanner.SetActive(false);
        _reforgeBtn.SetActive(false);
    }

    // ── 상태 ──

    private void Select(string elementId)
    {
        if (_busy) return;
        _selectedElement = elementId;
        Color col = ElementDef.IdColor(elementId, HeatDefault);
        for (int i = 0; i < _gems.Count; i++)
        {
            bool on = _gems[i].id == elementId;
            var border = _gems[i].go.GetComponent<Image>();
            if (border != null) border.color = on ? col : ShopUIStyle.CardBorder;
        }
        // 제단 광휘 속성색
        _altarGlow.color = new Color(col.r, col.g, col.b, 0.2f);

        var e = ElementDef.GetById(elementId);
        _hint.text = $"{(e != null ? e.Name : elementId)} 존을 강화할 존핵을 벼립니다 — 돌리세요";
        Refresh();
    }

    private void Refresh()
    {
        if (_svc == null) return;
        var (rare, epic, leg) = _svc.CurrentOdds();
        _oddsBar?.SetOdds(rare, epic, leg, _svc.NextHeat);
        _feverGauge?.SetLevel(_svc.Fever);
        RefreshReserved();

        int cost = _svc.CurrentCost;
        _costText.text = cost <= 0 ? "◆ 무료" : $"◆ 원석 {cost}  (보유 {_svc.OreOwned})";

        // 방 특전 표시(상시 탭이면 비움)
        string perk = _svc.RoomPerkLabel;
        _perkText.text = perk ?? "";

        bool canSpin = _selectedElement != null && _svc.CanAfford && !_busy;
        _spinBtnImg.color = canSpin ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill;
        _spinLabel.color  = canSpin ? new Color(0.14f, 0.09f, 0.02f) : ShopUIStyle.TextDim;
    }

    // ── 돌리기 ──

    private void OnSpinClicked()
    {
        if (_busy || _svc == null || _selectedElement == null) return;
        if (!_svc.CanAfford) { _hint.text = "원석이 부족합니다"; return; }
        SpinAsync().Forget();
    }

    private async UniTaskVoid SpinAsync()
    {
        _busy = true;
        _eventBanner.SetActive(false);
        _reforgeBtn.SetActive(false);
        _resultBox.gameObject.SetActive(false);
        _rarLine.text = "";
        Refresh();

        var outcome = _svc.Craft(_selectedElement);
        if (!outcome.Success) { _hint.text = outcome.FailReason; _busy = false; Refresh(); return; }

        await PlayRevealAsync(outcome);

        _busy = false;
        Refresh();
    }

    private void OnReforgeClicked()
    {
        if (_busy || _svc == null || !_svc.CanReforge) return;
        ReforgeAsync().Forget();
    }

    private async UniTaskVoid ReforgeAsync()
    {
        _busy = true;
        _reforgeBtn.SetActive(false);
        _eventBanner.SetActive(false);
        _resultBox.gameObject.SetActive(false);
        Refresh();

        var outcome = _svc.Reforge(_selectedElement);
        if (outcome.Success) await PlayRevealAsync(outcome);

        _busy = false;
        Refresh();
    }

    private async UniTask PlayRevealAsync(RefineryOutcome outcome)
    {
        // 제단 달아오름
        Color heat = ElementDef.IdColor(_selectedElement, HeatDefault);
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                float k = 0.2f + 0.5f * Mathf.PingPong(t * 3f, 1f);
                _altarGlow.color = new Color(heat.r, heat.g, heat.b, k);
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { return; }

        // 결과 룬
        Color rc = ShopUIStyle.RarityGlow(outcome.Rarity);
        _resultBox.gameObject.SetActive(true);
        _resultBox.color = new Color(rc.r, rc.g, rc.b, 0.28f);
        _resultSym.color = heat;
        _resultAmt.text  = $"+{AmtOf(outcome.Rune)}%";
        _resultAmt.color = rc;

        var e = ElementDef.GetById(_selectedElement);
        string en = e != null ? e.Name : _selectedElement;
        _rarLine.color = rc;
        _rarLine.text = $"{RarLabel(outcome.Rarity)} 존핵 — {en} 존 시너지 +{AmtOf(outcome.Rune)}%";
        _altarGlow.color = new Color(heat.r, heat.g, heat.b, 0.22f);

        Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);

        // 돌발 이벤트
        ShowEvent(outcome.EventKind);
    }

    /// <summary>
    /// 돌발 이벤트 배너. 4종이 <b>서로 다른 종류의 기쁨</b>이라 크기·색으로 구별한다.
    /// 특히 재점화는 이 화면을 다시 돌리게 만드는 유일한 장치라 나머지 셋보다 확실히 크게 띄운다
    /// (존핵은 속성당 사실상 한 번만 챙기면 되는 물건이라, 나쁜 결과를 뒤집을 수 있다는 게 핵심 유인).
    /// </summary>
    private void ShowEvent(RefineryEventKind ev)
    {
        if (ev == RefineryEventKind.None) return;

        var skin = UISkin.Refinery;
        bool  hero = ev == RefineryEventKind.Reignite;
        Color tone = ev switch
        {
            RefineryEventKind.Reignite => new Color(1.00f, 0.54f, 0.23f),   // 되돌리기 — 불씨
            RefineryEventKind.Overheat => new Color(0.88f, 0.42f, 0.16f),   // 예고 — 달아오름
            RefineryEventKind.Spark    => ShopUIStyle.RarityGlow(ItemRarity.Legendary),
            _                          => new Color(0.62f, 0.49f, 0.90f),   // 쌍생 — 분열
        };
        string txt = ev switch
        {
            RefineryEventKind.Reignite => "↻  재점화!\n결과를 한 번 다시 굴릴 수 있다",
            RefineryEventKind.Overheat => "🔥 과열! — 다음 돌리기 상위 확률 2배",
            RefineryEventKind.Spark    => "✦ 불티! — 다음 돌리기 무료",
            _                          => "⧉ 쌍생! — 존핵을 하나 더 얻었다",
        };

        _eventBannerRT.sizeDelta = hero ? new Vector2(460f, 96f) : new Vector2(360f, 44f);
        _eventText.fontSize      = hero ? 19f : 14f;
        _eventText.color         = tone;

        var border = _eventBanner.GetComponent<Image>();
        if (border != null) border.color = tone;

        // 아트가 있으면 종류별 배너로 스왑 — 없으면 위 색/크기 분기가 그대로 남는다.
        ShopUIStyle.Skin(_eventBannerImg,
            hero ? skin?.bannerReignite : skin?.bannerSmall != null && skin.bannerSmall.Length > 0
                 ? skin.bannerSmall[Mathf.Clamp((int)ev - 2, 0, skin.bannerSmall.Length - 1)] : null,
            sliced: true);

        _eventText.text = txt;
        _eventBanner.SetActive(true);
        _reforgeBtn.SetActive(hero && _svc.CanReforge);
    }

    /// <summary>다음 회로 넘어간 효과(과열·불티)를 상시 표시한다. 소진되면 자동으로 사라진다.</summary>
    private void RefreshReserved()
    {
        if (_reservedText == null) return;
        if (_svc == null) { _reservedText.text = ""; return; }

        string s = "";
        if (_svc.NextHeat) s  = "<color=#FF8A3A>🔥 과열 예약</color>";
        if (_svc.NextFree) s += (s.Length > 0 ? "   " : "") + "<color=#FFCB5A>✦ 불티 예약</color>";
        _reservedText.text = s;
    }

    // ── Helpers ──

    private static int AmtOf(RuntimeItemData rune)
    {
        if (rune?.effects != null)
            foreach (var s in rune.effects)
                if (s.effectType == "AmplifyZone") return Mathf.RoundToInt(s.value);
        return 0;
    }

    private static string RarLabel(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => "★ Legendary",
        ItemRarity.Epic      => "Epic",
        _                    => "Rare",
    };

    private static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";
    private static string HexOf(ItemRarity r) => ColorUtility.ToHtmlStringRGB(ShopUIStyle.RarityGlow(r));

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
}
