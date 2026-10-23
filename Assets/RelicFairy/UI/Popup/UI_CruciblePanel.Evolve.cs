using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 진화 선택 카드(10-08 · c3 시안 02) — 형이 주인공이고 수치는 뒷줄: 무기 줄(그림 · 성격 · 계열 · 이름 · E · R) → 형 이름 · 한 줄 →
/// 「이렇게 싸운다」 세 줄(① 쌓기 → ② 가득 → ③ 발동) → 규칙 한 줄 → 잃는 것 → 수치 칸 4개(지금 무기 대비) → 바닥 글.
/// 10-15 사용자 「무기 형 선택 디자인과 레이아웃이 더 보기 편하고 간단한 구조로」 — 작동 그림 3컷(작은 도해 + 두 줄 글 × 3) · 칩 둘을 걷고
/// 글 세 줄로 · 글자는 화면 15px 이상(덮개가 판에 맞춰 0.88배라 목업 17 이상 — 예전 15 = 화면 13.2px).
/// 고르기 → 확정 흐름 · 이름(_evolveConfirm · PickEvolveCard)은 그대로다(9c 실측 장비 형/7이 부른다). 톤 v2 — 얇은 테 · Noto 본문.
/// 10-11 v2 — 글자 15 · 17 · 20 · 24 · 32 다섯 단계(작은 글 12~14 없앰) · 성격 칩은 무채 글자 + 모양(◇ 빠른 · ◆ 묵직한) ·
/// 보조 문장 3줄(규칙 · 수치 머리 · 바닥 글) → 규칙 한 줄(바닥 글은 잠겼을 때만) · 그림 뒤 빛 없음.
/// </summary>
public sealed partial class UI_CruciblePanel
{
    private const float EvTitleY = 40f, EvSubY = 94f, EvBtnY = 834f, EvHintY = 906f;
    private const float EvCarryY = 938f;   // 10-18 — 이어지는 것 한 줄 자리(지금은 비어 있고 꺼 둔다)
    private const float EvStepY = 284f, EvStepH = 44f;   // 「이렇게 싸운다」 세 줄

    private UIThinFrame _evolveConfirmFrame;
    // 10-18 ⑧ 환영 이식 — 진화하면 무엇이 이어지나 한 줄(무형검 증강 → 등 뒤 환영 · 비어 있는 칼집 → 새 무기 증강). 이어질 것이 없으면 끈다
    private TMP_Text    _evolveCarry;

    private sealed class EvolveCard
    {
        public RectTransform root, lostBox;
        public CanvasGroup   group;
        public UIThinFrame   frame;
        public Button        button;
        public Image         icon, iconGlow;
        public TMP_Text      meta, lostChip, name, skills, formHead, formLine, rule, lost, foot;
        public readonly TMP_Text[] steps = new TMP_Text[3];
        public readonly TMP_Text[] statValue = new TMP_Text[4];
    }

    /// <summary>
    /// v3 판(1560×880) 안에 덮개를 맞춘다 — 암막은 판을 꼭 덮고, 내용은 목업(1600×1000) 그릇에 옮겨 같은 비율로 판 높이에 맞춘다.
    /// 목업 비율 배치(UIProportional)라 판 크기로 바로 늘이면 세로만 줄어 카드 글자가 넘친다. v3가 아니면 아무것도 안 한다.
    /// </summary>
    private void FitOverlayToV3(GameObject panel)
    {
        if (!_v3Ready || _v3Root == null || panel == null) return;
        var dim = (RectTransform)panel.transform;
        if (dim.parent != _v3Root)
        {
            dim.SetParent(_v3Root, false);
            ShopUIStyle.Stretch(dim);
        }
        if (dim.Find("V3Fit") != null) return;
        var kids = new System.Collections.Generic.List<Transform>(dim.childCount);
        foreach (Transform c in dim) kids.Add(c);
        var fit = (RectTransform)ShopUIStyle.MakeRect(dim, "V3Fit").transform;
        fit.anchorMin = fit.anchorMax = fit.pivot = new Vector2(0.5f, 0.5f);
        fit.anchoredPosition = Vector2.zero;
        fit.sizeDelta = new Vector2(MockW, MockH);
        float s = Mathf.Min(V3W / MockW, V3H / MockH);
        fit.localScale = new Vector3(s, s, 1f);
        foreach (var c in kids) c.SetParent(fit, false);   // 원래 순서대로(그리기 순서 그대로)
    }

    /// <summary>
    /// 진화 선택 화면 — 제목 · 카드 두 장 · [취소] [「○○」로 진화]. 되돌릴 수 없는 선택이라 카드는 고르기만 하고 확정은 버튼이 한다.
    /// 구운 화면의 옛 내용(이름만 있던 카드 두 장 · 취소)은 끄고 같은 암막 안에 새로 짓는다.
    /// </summary>
    private void EnsureEvolveContent()
    {
        if (_evolveContent != null || _evolvePanel == null) return;
        foreach (Transform c in _evolvePanel.transform) c.gameObject.SetActive(false);
        // 구운 화면의 암막은 옛 반투명(0.90) 그대로다 — 새 내용 밑에서 무대 글자가 비치지 않게 덮어쓴다.
        if (_evolvePanel.TryGetComponent<Image>(out var veil)) veil.color = ChooserVeil;

        _evolveContent = (RectTransform)ShopUIStyle.MakeRect(_evolvePanel.transform, "EvolveContent").transform;
        ShopUIStyle.Stretch(_evolveContent);
        var d = _evolveContent;   // 창(1600×1000)과 같은 크기 — 목업 좌표로 앉힌다

        _evolveTitle = MakeLabel(d, "Title", 32f, FontStyles.Normal, TextAlignmentOptions.Center, UITheme.Thin.Ink);   // DNF는 제목 한 줄 · 금은 확정 버튼에
        _evolveTitle.textWrappingMode = TextWrappingModes.NoWrap;
        Place(_evolveTitle.rectTransform, 0f, EvTitleY, MockW, 52f);
        _evolveSub = ThinText(d, "Sub", 19f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.75f));
        Place(_evolveSub.rectTransform, 0f, EvSubY, MockW, 28f);

        for (int k = 0; k < _evolveCards.Length; k++) _evolveCards[k] = BuildEvolveCard(d, k);

        var cancel = ThinButton(d, "EvolveCancel", "취소", primary: false, new Vector2(200f, 60f), out _, out _);
        Place((RectTransform)cancel.transform, 468f, EvBtnY, 200f, 60f);
        cancel.onClick.AddListener(HideEvolvePanel);

        _evolveConfirm = ThinButton(d, "EvolveConfirm", "", primary: true, new Vector2(440f, 60f), out _evolveConfirmLbl, out _evolveConfirmFrame);
        Place((RectTransform)_evolveConfirm.transform, 692f, EvBtnY, 440f, 60f);
        _evolveConfirm.onClick.AddListener(() => { if (_evolvePick >= 0) OnBranchClicked(_evolvePick); });

        var hint = ThinText(d, "Hint", 18f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.5f));
        hint.text = "카드를 골라 비교하고, 「진화」 버튼을 눌러야 바뀐다 · ▲▼는 지금 무기 대비";   // 10-18 「벼린다」 → 「진화」(용어 통일)
        Place(hint.rectTransform, 0f, EvHintY, MockW, 26f);

        _evolveCarry = ThinText(d, "CarryLine", 18f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.75f));
        _evolveCarry.richText = true;
        Place(_evolveCarry.rectTransform, 0f, EvCarryY, MockW, 26f);
        _evolveCarry.gameObject.SetActive(false);
    }

    private EvolveCard BuildEvolveCard(Transform d, int k)
    {
        var c = new EvolveCard();
        var bg = ShopUIStyle.MakeImage(d, k == 0 ? "EvolveA" : "EvolveB", CardSolid, raycast: true);
        c.root = bg.rectTransform;
        Place(c.root, 128f + k * (EvCardW + EvCardGap), EvCardY, EvCardW, EvCardH);
        c.group = bg.gameObject.AddComponent<CanvasGroup>();
        c.frame = UIThinFrame.Create(c.root, ThinShape.Square, new Vector2(EvCardW, EvCardH));

        // 무기 줄 — 그림 112 · 성격 · 계열 한 줄 · 이름 30 · E · R
        var iconBox = ShopUIStyle.MakeImage(c.root, "IconBox", new Color(0f, 0f, 0f, 0.35f));
        PlaceIn(iconBox.rectTransform, 24f, 24f, 112f, 112f, EvCardW, EvCardH);
        UIThinBox.Create(iconBox.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.2f), Color.clear, Color.clear);
        c.iconGlow = ShopUIStyle.MakeImage(iconBox.transform, "Glow", Color.clear);
        c.iconGlow.sprite = UI_RuneSelectPopup.SoftDot;
        ShopUIStyle.Stretch(c.iconGlow.rectTransform);
        c.icon = ShopUIStyle.MakeImage(iconBox.transform, "Icon", Color.white);
        c.icon.preserveAspect = true;
        PlaceIn(c.icon.rectTransform, 8f, 8f, 96f, 96f, 112f, 112f);
        c.meta = ThinText(c.root, "Meta", 18f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        PlaceIn(c.meta.rectTransform, 156f, 26f, EvCardW - 180f, 28f, EvCardW, EvCardH);
        c.name = ThinText(c.root, "Name", 30f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        PlaceIn(c.name.rectTransform, 156f, 56f, EvCardW - 180f, 46f, EvCardW, EvCardH);
        c.skills = ThinText(c.root, "Skills", 18f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.7f));
        c.skills.richText = true;
        PlaceIn(c.skills.rectTransform, 156f, 104f, EvCardW - 180f, 30f, EvCardW, EvCardH);

        var div = ShopUIStyle.MakeImage(c.root, "Divider", UITheme.Thin.InkA(0.15f));
        PlaceIn(div.rectTransform, 24f, 154f, EvCardW - 48f, 1f, EvCardW, EvCardH);

        // 형 — 주인공
        c.formHead = ThinText(c.root, "FormHead", 34f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, Color.white);
        c.formHead.richText = true;
        PlaceIn(c.formHead.rectTransform, 24f, 166f, EvCardW - 48f, 58f, EvCardW, EvCardH);
        c.formLine = ThinText(c.root, "FormLine", 22f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        c.formLine.richText = true;
        c.formLine.enableAutoSizing = true;   // 긴 한 줄(「느리지만, 무게 4가 찬 뒤 …」)이 카드 테를 넘었다 — 18(화면 15.8)까지만 줄인다
        c.formLine.fontSizeMax = 22f;
        c.formLine.fontSizeMin = 18f;
        PlaceIn(c.formLine.rectTransform, 24f, 226f, EvCardW - 48f, 34f, EvCardW, EvCardH);

        // 이렇게 싸운다 — ① 쌓기 → ② 가득 → ③ 발동(작동 그림 3컷 대신 글 세 줄)
        for (int i = 0; i < 3; i++)
        {
            c.steps[i] = ThinText(c.root, $"Step{i}", 20f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.85f));
            c.steps[i].richText = true;
            PlaceIn(c.steps[i].rectTransform, 24f, EvStepY + i * EvStepH, EvCardW - 48f, EvStepH, EvCardW, EvCardH);
        }

        c.rule = ThinText(c.root, "Rule", 18f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        c.rule.richText = true;
        PlaceIn(c.rule.rectTransform, 24f, 422f, EvCardW - 48f, 30f, EvCardW, EvCardH);

        c.lostBox = ChipBox(c.root, "LostChip", out c.lostChip, 17f);   // 칩 폭은 채울 때(창이 켜진 뒤) 잰다
        c.lost = ThinText(c.root, "Lost", 19f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        c.lost.richText = true;
        PlaceIn(c.lost.rectTransform, 116f, 464f, EvCardW - 140f, 30f, EvCardW, EvCardH);

        // 수치 칸 4개 — 이름 17 · 값 21(▲▼ = 지금 무기 대비, 아래 안내 줄이 한 번 적는다)
        for (int i = 0; i < 4; i++)
        {
            var box = ShopUIStyle.MakeRect(c.root, $"Stat{i}").GetComponent<RectTransform>();
            PlaceIn(box, 24f + i * 150f, 514f, 142f, 66f, EvCardW, EvCardH);
            UIThinBox.Create(box, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.18f), Color.clear, new Color(1f, 1f, 1f, 0.02f));
            var label = ThinText(box, "Label", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.55f));
            label.text = EvStatNames[i];
            PlaceIn(label.rectTransform, 10f, 4f, 124f, 26f, 142f, 66f);
            c.statValue[i] = ThinText(box, "Value", 21f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
            c.statValue[i].richText = true;
            PlaceIn(c.statValue[i].rectTransform, 10f, 30f, 128f, 32f, 142f, 66f);
        }

        c.foot = ThinText(c.root, "Foot", 18f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.55f));
        c.foot.richText = true;
        PlaceIn(c.foot.rectTransform, 24f, 600f, EvCardW - 48f, 30f, EvCardW, EvCardH);

        c.button = bg.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(c.button);
        int pick = k;
        c.button.onClick.AddListener(() => { ShopUIStyle.PlaySfx(SoundEvent.UiSelect); PickEvolveCard(pick); });
        return c;
    }

    /// <summary>칩 — 얇은 테(색은 SetChip이 칠한다) + 가운데 글자.</summary>
    private static RectTransform ChipBox(RectTransform parent, string name, out TMP_Text text, float size)
    {
        var box = ShopUIStyle.MakeRect(parent, name).GetComponent<RectTransform>();
        UIThinBox.Create(box, "Box", Vector2.zero, Vector2.one, 0f, Color.white, Color.clear, Color.white);
        text = ThinText(box, "Text", size, UIWeight.Medium, TextAlignmentOptions.Center, Color.white);
        ShopUIStyle.Stretch(text.rectTransform);
        return box;
    }

    private static void SetChip(RectTransform box, TMP_Text text, string label, Color color, float x, float y, bool rightAlign, float h = 30f)
    {
        bool on = !string.IsNullOrEmpty(label);
        box.gameObject.SetActive(on);
        if (!on) return;
        text.text  = label;
        text.color = color;
        foreach (var img in box.GetComponentsInChildren<Image>(true))
            img.color = img.name == "Fill" ? UITheme.Thin.WithA(color, 0.08f) : UITheme.Thin.WithA(color, 0.55f);
        float w = text.GetPreferredValues(label).x + 24f;
        PlaceIn(box, rightAlign ? x - w : x, y, w, h, EvCardW, EvCardH);
    }

    private void FillEvolveCard(EvolveCard card, WeaponEvolutionSO.Branch b, WeaponData current)
    {
        bool valid = b != null && b.IsValid;
        card.root.gameObject.SetActive(valid);
        if (!valid) return;

        var so = b.target;
        var f  = so.Form;
        var (tag, tint, line) = Persona(so);
        // 성격은 색이 아니라 모양으로(10-11 · 파랑 · 주황 글자 → 무채) — ◇ 빠른 검 · ◆ 묵직한 검 · 계열은 같은 한 줄(10-15 — 칩 둘을 걷었다)
        bool heavy = f != null ? f.FormId == "weight" : so.weaponType == WeaponType.Greatsword;
        card.meta.text = $"{(heavy ? "◆" : "◇")} {tag}" + (f == null ? "" : f.FormId == "weight" ? " · 필살 계열" : " · 연격 계열");

        card.icon.sprite  = so.icon;
        card.icon.enabled = so.icon != null;
        card.iconGlow.color = Color.clear;   // 그림 뒤 빛 없음 — 후광은 한 화면 한 곳(10-11)
        card.name.text   = so.displayName;

        int level = current?.enhanceLevel ?? 0;
        int startTier = current != null && !SkillTierResolver.IsRanged(current) ? SkillTierResolver.Resolve(current) : SkillTierResolver.MinTier;
        card.skills.text = $"E {(so.skillE != null ? so.skillE.skillName : "—")} · R {(so.skillQ != null ? so.skillQ.skillName : "—")}"
                         + (startTier > SkillTierResolver.MinTier ? $"  <color=#C9A6FF>{startTier}단계로 시작</color>" : "");   // 강화 단계가 이어지므로 지금 무기의 단계 = 시작 단계
        string hex = ColorUtility.ToHtmlStringRGB(f != null ? f.Tint : tint);
        if (f != null)
        {
            card.formHead.text = $"<size=47%><color=#9A93A6>형</color></size> <color=#{hex}>{f.DisplayName}</color>  <size=53%><color=#8C8497>평타가 바뀐다</color></size>";
            card.formLine.text = UIKeywordInk.Values(f.Line);   // 수치만 Noto 500(10-15 설명 글 강조)
            for (int i = 0; i < 3; i++) card.steps[i].text = FormStep(i, f, hex);
            card.rule.text = UIKeywordInk.Values(f.FormId == "weight"   // 수치만 Noto 500(10-15)
                ? $"{f.IdleSeconds:0}초간 맞힌 휘두름이 없으면 0 · 충격파 {f.TriggerRange:0.#} m · 일반 몹 0.8초 경직"
                : $"{f.IdleSeconds:0}초간 못 맞히면 1초에 1씩 준다 · 발동하면 0부터");
        }
        else
        {
            card.formHead.text = $"<color=#{hex}>{so.displayName}</color>";
            card.formLine.text = line;
            card.rule.text = "";
            for (int i = 0; i < 3; i++) card.steps[i].text = "";
        }

        // 잃는 것 — 정직하게(시안 02): 무형검의 E(무형참)는 새 형태로 넘어가지 않는다
        string lostName = current?.skillE != null ? current.skillE.skillName : null;
        bool lostOn = f != null && lostName != null && (so.skillE == null || so.skillE.skillName != lostName);
        SetChip(card.lostBox, card.lostChip, lostOn ? "잃는 것" : null, UITheme.Thin.Danger, 24f, 464f, rightAlign: false, h: 30f);
        string tail = f == null ? "" : f.FormId == "weight" ? "멀리 닿는 평타가 없다" : $"{f.TriggerName}{Iga(f.TriggerName)} 조건부로 멀리 잇는다";
        card.lost.text = lostOn ? $"{lostName} <color=#9A93A6>— {tail}</color>" : "";

        var next = PreviewStats(so, level);
        var now  = current != null ? new[] { current.baseAttack, current.attackSpeed, current.attackRange, current.areaOfEffect } : null;
        for (int i = 0; i < EvStatNames.Length; i++)
            card.statValue[i].text = $"{StatText(i, next[i])}  {(now != null ? DeltaText(i, next[i] - now[i]) : "")}";

        bool unlocked = WeaponEvolutionSO.IsUnlocked(b, level);
        // 바닥 글은 잠겼을 때만(10-11) — 비용 · 계승은 제목 아래 한 줄과 확정 버튼이 한 번씩 말한다(두 카드에 같은 줄이 겹쳤다)
        card.foot.gameObject.SetActive(!unlocked);
        card.foot.text = unlocked ? "" : $"<color=#FF7A6A>재련 {Reforges(b.requiredEnhanceLevel)}회에서 열린다</color>";
        card.button.interactable = unlocked;
    }

    /// <summary>카드 고르기 — 고른 카드는 금 테가 그려지고, 다른 카드는 0.55로 옅어진다. 확정 버튼은 고른 뒤 · 재료가 되면 켜진다.</summary>
    private void PickEvolveCard(int k)
    {
        var branches = CurrentBranches;
        bool ok = k >= 0 && k < branches.Count && branches[k] != null && branches[k].IsValid;
        _evolvePick = ok ? k : -1;
        for (int i = 0; i < _evolveCards.Length; i++)
        {
            var card = _evolveCards[i];
            if (card == null) continue;
            bool picked = i == _evolvePick;
            card.frame.SetState(picked ? ThinFrameState.Ready : ThinFrameState.Idle, animate: true);
            card.name.color = picked ? UITheme.Thin.Gold : UITheme.Thin.Ink;
            float to = _evolvePick < 0 || picked ? 1f : 0.55f;
            if (isActiveAndEnabled) FadeGroupAsync(card.group, to).Forget();
            else card.group.alpha = to;
        }

        var b = ok ? branches[k] : null;
        int fuel = _controller != null ? _controller.FuelAmount : 0;
        bool affordable = b != null && (b.cost <= 0 || fuel >= b.cost);
        if (_evolveConfirm == null) return;
        _evolveConfirm.interactable = affordable;
        _evolveConfirmFrame?.SetState(affordable ? ThinFrameState.Ready : ThinFrameState.Idle, animate: true);
        string name = b?.target != null ? b.target.displayName : "";
        string cost = b == null || b.cost <= 0 ? ""
                    : affordable ? $"  <size=85%>강화재료 {b.cost}</size>" : $"  <size=85%><color=#FF7A6A>강화재료 {b.cost}</color></size>";
        _evolveConfirmLbl.text = b == null ? "<color=#9A98A0>무기를 고른다</color>" : $"「{name}」{Ro(name)} 진화{cost}";   // 10-18 「벼린다」 → 「진화」
        RefreshEvolveCarry(b);
    }

    /// <summary>
    /// 이어지는 것 한 줄(10-18 ⑧) — 무형검 전용 증강(칼집 제외)은 진화한 무기의 등 뒤 환영이 이어서 쓰고(<see cref="MeleeAugmentCatalog.RunsAsPhantom"/>),
    /// 비어 있는 칼집은 고른 검의 전용 증강 N개(I)가 된다(<see cref="MeleeAugmentCatalog.ConvertForEvolution"/>). 카드를 고르기 전엔 「새 무기 증강」.
    /// </summary>
    private void RefreshEvolveCarry(WeaponEvolutionSO.Branch picked)
    {
        if (_evolveCarry == null) return;
        var wd = _controller?.GetSlot(PlayerWeaponManager.Slot0);
        int phantom = 0, sheath = 0, sheathRank = 0;
        if (wd != null && !string.IsNullOrEmpty(wd.augments))
            foreach (var (id, rank) in wd.AugmentList())
            {
                var d = MeleeAugmentCatalog.Get(id);
                if (d == null || d.Weapon != MeleeWeaponKind.Nameless) continue;
                if (d.Id == MeleeAugmentCatalog.NamelessSheath) { sheath += Mathf.RoundToInt(d.ValueAt(rank)); sheathRank = rank; }
                else phantom++;
            }
        string kind = picked?.target == null ? "새 무기"
                    : picked.target.weaponType == WeaponType.Greatsword ? "대검" : "카타나";
        string line = "";
        if (phantom > 0) line = $"무형검 증강 {phantom}개 → <color=#C9D3E6>환영이 이어서 씀</color>";
        if (sheath > 0)  line += (line.Length > 0 ? "  ·  " : "") + $"비어 있는 칼집 {MeleeAugmentDef.RankName(sheathRank)} → {kind} 증강 {sheath}개";
        _evolveCarry.text = line;
        _evolveCarry.gameObject.SetActive(line.Length > 0);
    }

    /// <summary>「~이 / ~가」 — 받침이 있으면 「이」.</summary>
    private static string Iga(string word)
    {
        if (string.IsNullOrEmpty(word)) return "이";
        char c = word[word.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return "이";
        return (c - 0xAC00) % 28 == 0 ? "가" : "이";
    }

    /// <summary>
    /// 「이렇게 싸운다」 한 줄(① 쌓기 · ② 가득 · ③ 발동) — 번호 · 머리말은 형 색, 수치는 Noto 500. 수치는 형 데이터 · 형 코드 상수와 같다
    /// (예전 작동 그림 3컷의 칸 글자 · 두 줄을 한 줄로 모았다 — 10-15).
    /// </summary>
    private static string FormStep(int i, WeaponFormSO f, string hex)
    {
        bool weight = f.FormId == "weight";
        string Head(string n, string w) => $"<color=#{hex}>{n}  {w}</color>   ";
        switch (i)
        {
            case 0:
                return Head("①", "쌓기") + UIKeywordInk.Values(weight ? "적중한 휘두름마다 +1 · 피격돼도 유지" : "평타 적중마다 +1 · 피격하면 0");
            case 1:
            {
                char on = weight ? '●' : '◆';
                return Head("②", "가득") + $"{f.DisplayName} <color=#{hex}>{new string(on, f.MaxStacks)}</color> "
                     + UIKeywordInk.Values($"{f.MaxStacks}칸") + (weight ? " · 잉걸빛 칼날" : " · 바람을 두른 칼날");
            }
            default:
                return Head("③", "발동") + UIKeywordInk.Values(weight
                    ? $"「{f.TriggerName}」 반드시 치명 · 충격파 {f.TriggerRange:0.#} m"
                    : $"마지막 타 「{f.TriggerName}」 · {f.TriggerRange:0} m 관통 · {f.TriggerAtkRatio * 100f:0}%");
        }
    }
}
