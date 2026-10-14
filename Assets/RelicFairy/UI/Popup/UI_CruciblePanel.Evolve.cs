using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 진화 선택 카드(10-08 · c3 시안 02) — 형이 주인공이고 수치는 뒷줄: 성격 · 계열 칩 → 무기 줄(그림 · 이름 · E · R) → 형 이름 · 한 줄 →
/// 작동 그림 3컷(① 쌓임 → ② 가득 → ③ 발동) → 규칙 한 줄 → 잃는 것 → 수치 칩 4개(지금 무기 대비) → 바닥 글.
/// 고르기 → 확정 흐름 · 이름(_evolveConfirm · PickEvolveCard)은 그대로다(9c 실측 장비 형/7이 부른다). 톤 v2 — 얇은 테 · Noto 본문.
/// 10-11 v2 — 글자 15 · 17 · 20 · 24 · 32 다섯 단계(작은 글 12~14 없앰) · 성격 칩은 무채 글자 + 모양(◇ 빠른 · ◆ 묵직한) ·
/// 보조 문장 3줄(규칙 · 수치 머리 · 바닥 글) → 규칙 한 줄(바닥 글은 잠겼을 때만) · 그림 뒤 빛 없음.
/// </summary>
public sealed partial class UI_CruciblePanel
{
    private const float EvTitleY = 40f, EvSubY = 94f, EvBtnY = 834f, EvHintY = 906f;
    private const float EvCutW = 184f, EvCutH = 196f, EvCutY = 280f, EvCutGap = 20f, EvDrawH = 110f;

    private UIThinFrame _evolveConfirmFrame;

    private sealed class EvolveCard
    {
        public RectTransform root, personaBox, familyBox, lostBox;
        public CanvasGroup   group;
        public UIThinFrame   frame;
        public Button        button;
        public Image         icon, iconGlow;
        public TMP_Text      persona, family, lostChip, name, skills, formHead, formLine, rule, lost, statsHead, foot;
        public readonly RectTransform[] draw = new RectTransform[3];
        public readonly TMP_Text[] cutPips = new TMP_Text[3], cutLine1 = new TMP_Text[3], cutLine2 = new TMP_Text[3];
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
    /// 진화 선택 화면 — 제목 · 카드 두 장 · [취소] [「○○」로 벼린다]. 되돌릴 수 없는 선택이라 카드는 고르기만 하고 확정은 버튼이 한다.
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
        _evolveSub = ThinText(d, "Sub", 17f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.75f));
        Place(_evolveSub.rectTransform, 0f, EvSubY, MockW, 28f);

        for (int k = 0; k < _evolveCards.Length; k++) _evolveCards[k] = BuildEvolveCard(d, k);

        var cancel = ThinButton(d, "EvolveCancel", "취소", primary: false, new Vector2(200f, 60f), out _, out _);
        Place((RectTransform)cancel.transform, 468f, EvBtnY, 200f, 60f);
        cancel.onClick.AddListener(HideEvolvePanel);

        _evolveConfirm = ThinButton(d, "EvolveConfirm", "", primary: true, new Vector2(440f, 60f), out _evolveConfirmLbl, out _evolveConfirmFrame);
        Place((RectTransform)_evolveConfirm.transform, 692f, EvBtnY, 440f, 60f);
        _evolveConfirm.onClick.AddListener(() => { if (_evolvePick >= 0) OnBranchClicked(_evolvePick); });

        var hint = ThinText(d, "Hint", 15f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.5f));
        hint.text = "카드를 골라 비교하고, 「벼린다」를 눌러야 바뀐다 · ▲▼는 지금 무기 대비";
        Place(hint.rectTransform, 0f, EvHintY, MockW, 26f);
    }

    private EvolveCard BuildEvolveCard(Transform d, int k)
    {
        var c = new EvolveCard();
        var bg = ShopUIStyle.MakeImage(d, k == 0 ? "EvolveA" : "EvolveB", CardSolid, raycast: true);
        c.root = bg.rectTransform;
        Place(c.root, 128f + k * (EvCardW + EvCardGap), EvCardY, EvCardW, EvCardH);
        c.group = bg.gameObject.AddComponent<CanvasGroup>();
        c.frame = UIThinFrame.Create(c.root, ThinShape.Square, new Vector2(EvCardW, EvCardH));

        c.personaBox = ChipBox(c.root, "Persona", out c.persona, 15f);
        c.familyBox  = ChipBox(c.root, "Family", out c.family, 15f);

        // 무기 줄 — 그림 96 · 이름 32 · E · R
        var iconBox = ShopUIStyle.MakeImage(c.root, "IconBox", new Color(0f, 0f, 0f, 0.35f));
        PlaceIn(iconBox.rectTransform, 24f, 62f, 96f, 96f, EvCardW, EvCardH);
        UIThinBox.Create(iconBox.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.2f), Color.clear, Color.clear);
        c.iconGlow = ShopUIStyle.MakeImage(iconBox.transform, "Glow", Color.clear);
        c.iconGlow.sprite = UI_RuneSelectPopup.SoftDot;
        ShopUIStyle.Stretch(c.iconGlow.rectTransform);
        c.icon = ShopUIStyle.MakeImage(iconBox.transform, "Icon", Color.white);
        c.icon.preserveAspect = true;
        PlaceIn(c.icon.rectTransform, 8f, 8f, 80f, 80f, 96f, 96f);
        c.name = ThinText(c.root, "Name", 24f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        PlaceIn(c.name.rectTransform, 136f, 70f, EvCardW - 160f, 44f, EvCardW, EvCardH);
        c.skills = ThinText(c.root, "Skills", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.65f));
        PlaceIn(c.skills.rectTransform, 136f, 116f, EvCardW - 160f, 26f, EvCardW, EvCardH);

        var div = ShopUIStyle.MakeImage(c.root, "Divider", UITheme.Thin.InkA(0.15f));
        PlaceIn(div.rectTransform, 24f, 172f, EvCardW - 48f, 1f, EvCardW, EvCardH);

        // 형 — 주인공
        c.formHead = ThinText(c.root, "FormHead", 32f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, Color.white);
        c.formHead.richText = true;
        PlaceIn(c.formHead.rectTransform, 24f, 180f, EvCardW - 48f, 56f, EvCardW, EvCardH);
        c.formLine = ThinText(c.root, "FormLine", 20f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        PlaceIn(c.formLine.rectTransform, 24f, 236f, EvCardW - 48f, 30f, EvCardW, EvCardH);

        // 작동 그림 3컷 — 쌓임 → 가득 → 발동
        string[] heads = { "① 쌓임", "② 가득", "③ 발동" };
        for (int i = 0; i < 3; i++)
        {
            float x = 24f + i * (EvCutW + EvCutGap);
            var cut = ShopUIStyle.MakeImage(c.root, $"Cut{i}", new Color(0f, 0f, 0f, 0.3f));
            PlaceIn(cut.rectTransform, x, EvCutY, EvCutW, EvCutH, EvCardW, EvCardH);
            UIThinBox.Create(cut.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.15f), Color.clear, Color.clear);
            var head = ThinText(cut.transform, "Head", 15f, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
            head.text = heads[i];
            PlaceIn(head.rectTransform, 10f, 4f, EvCutW - 20f, 22f, EvCutW, EvCutH);
            var draw = ShopUIStyle.MakeRect(cut.transform, "Draw").GetComponent<RectTransform>();
            draw.anchorMin = draw.anchorMax = draw.pivot = new Vector2(0.5f, 1f);
            draw.anchoredPosition = new Vector2(0f, -26f);
            draw.sizeDelta = new Vector2(EvCutW, EvDrawH);
            c.draw[i] = draw;
            c.cutPips[i] = MakeLabel(cut.transform, "Pips", 15f, FontStyles.Normal, TextAlignmentOptions.Center, Color.white);
            c.cutPips[i].richText = true;
            c.cutPips[i].textWrappingMode = TextWrappingModes.NoWrap;
            PlaceIn(c.cutPips[i].rectTransform, 0f, 134f, EvCutW, 20f, EvCutW, EvCutH);
            c.cutLine1[i] = ThinText(cut.transform, "Line1", 15f, UIWeight.Medium, TextAlignmentOptions.Center, UITheme.Thin.Ink);
            PlaceIn(c.cutLine1[i].rectTransform, 4f, 152f, EvCutW - 8f, 22f, EvCutW, EvCutH);
            c.cutLine2[i] = ThinText(cut.transform, "Line2", 15f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.6f));
            PlaceIn(c.cutLine2[i].rectTransform, 4f, 173f, EvCutW - 8f, 22f, EvCutW, EvCutH);
            if (i < 2)
            {
                var arrow = ThinText(c.root, "Arrow", 15f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.6f));
                arrow.text = "▶";
                PlaceIn(arrow.rectTransform, x + EvCutW, EvCutY + 88f, EvCutGap, 20f, EvCardW, EvCardH);
            }
        }

        c.rule = ThinText(c.root, "Rule", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        PlaceIn(c.rule.rectTransform, 24f, 482f, EvCardW - 48f, 24f, EvCardW, EvCardH);

        c.lostBox = ChipBox(c.root, "LostChip", out c.lostChip, 15f);   // 칩 폭은 채울 때(창이 켜진 뒤) 잰다
        c.lost = ThinText(c.root, "Lost", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        c.lost.richText = true;
        PlaceIn(c.lost.rectTransform, 112f, 512f, EvCardW - 136f, 28f, EvCardW, EvCardH);

        // 수치 머리 줄(「수치 · 지금 무기 대비」)은 걷었다 — ▲▼가 대비를 말하고, 아래 안내 줄이 한 번 적는다(10-11)
        c.statsHead = ThinText(c.root, "StatsHead", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.5f));
        PlaceIn(c.statsHead.rectTransform, 24f, 548f, EvCardW - 48f, 22f, EvCardW, EvCardH);
        c.statsHead.gameObject.SetActive(false);
        for (int i = 0; i < 4; i++)
        {
            var box = ShopUIStyle.MakeRect(c.root, $"Stat{i}").GetComponent<RectTransform>();
            PlaceIn(box, 24f + i * 150f, 572f, 142f, 52f, EvCardW, EvCardH);
            UIThinBox.Create(box, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.InkA(0.18f), Color.clear, new Color(1f, 1f, 1f, 0.02f));
            var label = ThinText(box, "Label", 15f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.55f));
            label.text = EvStatNames[i];
            PlaceIn(label.rectTransform, 8f, 2f, 126f, 22f, 142f, 52f);
            c.statValue[i] = ThinText(box, "Value", 17f, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
            c.statValue[i].richText = true;
            PlaceIn(c.statValue[i].rectTransform, 8f, 24f, 130f, 26f, 142f, 52f);
        }

        c.foot = ThinText(c.root, "Foot", 15f, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.55f));
        c.foot.richText = true;
        PlaceIn(c.foot.rectTransform, 24f, 634f, EvCardW - 48f, 26f, EvCardW, EvCardH);

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
        // 성격은 색이 아니라 모양으로(10-11 · 파랑 · 주황 글자 → 무채) — ◇ 빠른 검 · ◆ 묵직한 검
        bool heavy = f != null ? f.FormId == "weight" : so.weaponType == WeaponType.Greatsword;
        SetChip(card.personaBox, card.persona, $"{(heavy ? "◆" : "◇")} {tag}", UITheme.Thin.InkA(0.8f), 24f, 20f, rightAlign: false);
        SetChip(card.familyBox, card.family, f == null ? null : f.FormId == "weight" ? "필살 계열" : "연격 계열",
                UITheme.Thin.InkA(0.6f), EvCardW - 24f, 22f, rightAlign: true, h: 28f);

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
            card.formLine.text = f.Line;
            for (int i = 0; i < 3; i++)
            {
                ClearChildren(card.draw[i]);
                var (pips, l1, l2) = DrawCut(card.draw[i], i, so, f);
                card.cutPips[i].text  = pips;
                card.cutLine1[i].text = l1;
                card.cutLine2[i].text = l2;
            }
            card.rule.text = f.FormId == "weight"
                ? $"{f.IdleSeconds:0}초간 맞힌 휘두름이 없으면 0 · 충격파 {f.TriggerRange:0.#} m · 일반 몹 0.8초 경직"
                : $"{f.IdleSeconds:0}초간 못 맞히면 1초에 1씩 준다 · 발동하면 0부터";
        }
        else
        {
            card.formHead.text = $"<color=#{hex}>{so.displayName}</color>";
            card.formLine.text = line;
            card.rule.text = "";
        }

        // 잃는 것 — 정직하게(시안 02): 무형검의 E(무형참)는 새 형태로 넘어가지 않는다
        string lostName = current?.skillE != null ? current.skillE.skillName : null;
        bool lostOn = f != null && lostName != null && (so.skillE == null || so.skillE.skillName != lostName);
        SetChip(card.lostBox, card.lostChip, lostOn ? "잃는 것" : null, UITheme.Thin.Danger, 24f, 512f, rightAlign: false, h: 28f);
        string tail = f == null ? "" : f.FormId == "weight" ? "멀리 닿는 평타가 없다" : $"{f.TriggerName}{Iga(f.TriggerName)} 조건부로 멀리 잇는다";
        card.lost.text = lostOn ? $"{lostName} <color=#9A93A6>— {tail}</color>" : "";

        var next = PreviewStats(so, level);
        var now  = current != null ? new[] { current.baseAttack, current.attackSpeed, current.attackRange, current.areaOfEffect } : null;
        card.statsHead.text = current != null ? $"수치 · 지금 무기({current.displayName} +{level}) 대비" : "수치";
        for (int i = 0; i < EvStatNames.Length; i++)
            card.statValue[i].text = $"{StatText(i, next[i])}  {(now != null ? DeltaText(i, next[i] - now[i]) : "")}";

        bool unlocked = WeaponEvolutionSO.IsUnlocked(b, level);
        // 바닥 글은 잠겼을 때만(10-11) — 비용 · 계승은 제목 아래 한 줄과 확정 버튼이 한 번씩 말한다(두 카드에 같은 줄이 겹쳤다)
        card.foot.gameObject.SetActive(!unlocked);
        card.foot.text = unlocked ? "" : $"<color=#FF7A6A>강화 +{b.requiredEnhanceLevel}에서 열린다</color>";
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
        _evolveConfirmLbl.text = b == null ? "<color=#9A98A0>형을 고른다</color>" : $"「{name}」{Ro(name)} 벼린다{cost}";
    }

    /// <summary>「~이 / ~가」 — 받침이 있으면 「이」.</summary>
    private static string Iga(string word)
    {
        if (string.IsNullOrEmpty(word)) return "이";
        char c = word[word.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return "이";
        return (c - 0xAC00) % 28 == 0 ? "가" : "이";
    }

    /// <summary>작동 그림 한 컷(184×110 · 위에서 본 그림) — (칸 글자, 첫 줄, 둘째 줄). 수치는 형 데이터 · 형 코드 상수와 같다.</summary>
    private static (string pips, string l1, string l2) DrawCut(RectTransform p, int i, WeaponSO so, WeaponFormSO f)
    {
        bool weight = f.FormId == "weight";
        var bright  = UITheme.Thin.WithA(f.Tint, 0.95f);
        string hex  = ColorUtility.ToHtmlStringRGB(f.Tint);
        char on = weight ? '●' : '◆', off = weight ? '○' : '◇';
        switch (i)
        {
            case 0:
            {
                int part = weight ? 2 : 3;
                DDot(p, new Vector2(30f, 58f), 10f, UITheme.Thin.Ink);
                DCrescent(p, new Vector2(weight ? 70f : 72f, 58f), weight ? 54f : 40f, 0f, bright);
                DDot(p, new Vector2(110f, 58f), 12f, EnemyDot);
                DText(p, new Vector2(138f, 38f), "+1", 15f, UITheme.Thin.Gold);
                return ($"<color=#{hex}>{new string(on, part)}</color><color=#5A5566>{new string(off, f.MaxStacks - part)}</color>",
                        weight ? "적중한 휘두름 +1" : "평타 적중 +1",
                        weight ? "피격돼도 유지" : "피격하면 0");
            }
            case 1:
            {
                var glow = ShopUIStyle.MakeImage(DRect(p, "Glow", new Vector2(EvCutW * 0.5f, 56f), new Vector2(150f, 110f)), "Img", UITheme.Thin.WithA(f.Tint, 0.35f));
                glow.sprite = UI_RuneSelectPopup.SoftDot;
                ShopUIStyle.Stretch(glow.rectTransform);
                if (so.icon != null)
                {
                    var icon = ShopUIStyle.MakeImage(DRect(p, "Icon", new Vector2(EvCutW * 0.5f, 56f), new Vector2(104f, 84f)), "Img", Color.white);
                    icon.sprite = so.icon;
                    icon.preserveAspect = true;
                    ShopUIStyle.Stretch(icon.rectTransform);
                }
                return ($"<color=#{hex}>{new string(on, f.MaxStacks)}</color>", $"{f.DisplayName} {f.MaxStacks}칸",
                        weight ? "잉걸빛 칼날" : "바람을 두른 칼날");
            }
            default:
            {
                if (weight)
                {
                    var c = new Vector2(EvCutW * 0.5f, 78f);
                    var ring = DRing(p, c, 140f, bright, 2f);
                    ring.rectTransform.parent.localScale = new Vector3(1f, 0.36f, 1f);   // 바닥에 누운 충격파
                    DLine(p, new Vector2(c.x, 14f), new Vector2(c.x, 72f), 4f, bright);
                    DDot(p, c + new Vector2(-48f, 4f), 9f, EnemyDot);
                    DDot(p, c + new Vector2(42f, -6f), 9f, EnemyDot);
                    DText(p, new Vector2(150f, 22f), $"{f.TriggerRange:0.#} m", 13f, UITheme.Thin.InkA(0.7f));
                    return ("", $"「{f.TriggerName}」", "반드시 치명 · 충격파");
                }
                DDash(p, new Vector2(14f, 62f), new Vector2(150f, 62f), 1.5f, bright, 6f, 5f);
                DDot(p, new Vector2(70f, 62f), 9f, EnemyDot);
                DDot(p, new Vector2(110f, 62f), 9f, EnemyDot);
                DCrescent(p, new Vector2(156f, 62f), 36f, 0f, bright);
                DText(p, new Vector2(150f, 26f), $"{f.TriggerRange:0} m", 13f, UITheme.Thin.InkA(0.7f));
                return ("", $"마지막 타 「{f.TriggerName}」", $"{f.TriggerRange:0} m 관통 · {f.TriggerAtkRatio * 100f:0}%");
            }
        }
    }
}
