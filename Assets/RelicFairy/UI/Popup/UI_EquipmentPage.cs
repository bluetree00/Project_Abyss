using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일시정지 「장비」 쪽(10-13 — 4b UI 요청 8) — 무기 두 칸의 한글 이름 · 강화 · 형 · 공격 · 공속 · 스킬 단계 표(지금 칸) · R/E 스킬.
/// 옛 정보 책(<see cref="CharacterInfoPageView"/>)은 @UIRoot에서 걷혀 어디에도 안 보였다 — 「되찾은 기억」 쪽과 같은 틀(같은 캔버스 · 같은 정지)로 연다.
/// 단계 표와 변화 글은 재련소와 같은 <see cref="SkillTierResolver"/> · <see cref="UI_CruciblePanel.TierChangeBody"/>를 쓴다. 톤 v2(잉크 · 금 · 흐림).
/// </summary>
public sealed class UI_EquipmentPage : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float PanelW = 1180f, PanelH = 760f;
    private const float Pad    = 44f, ColGap = 40f;
    private const float BtnW   = 260f, BtnH = 60f;
    private const float TitleSize = 32f, NameSize = 26f, BodySize = 19f;

    private static readonly string GoldHex = "#" + ColorUtility.ToHtmlStringRGB(UITheme.Thin.Gold);
    private static readonly string InkHex  = "#" + ColorUtility.ToHtmlStringRGB(UITheme.Thin.Ink);
    private static readonly string DimHex  = "#" + ColorUtility.ToHtmlStringRGBA(UITheme.Thin.InkA(0.55f));
    private static readonly string FaintHex = "#" + ColorUtility.ToHtmlStringRGBA(UITheme.Thin.InkA(0.35f));

    // ── Private ──────────────────────────────────────────────
    private RectTransform _panel;
    private TMP_Text[]    _cols = new TMP_Text[2];
    private Action        _onBack;

    // ── Public ───────────────────────────────────────────────
    public static UI_EquipmentPage Create(Transform canvasRoot, Action onBack)
    {
        var go = new GameObject("EquipmentPage", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var page = go.AddComponent<UI_EquipmentPage>();
        page._onBack = onBack;
        page.Build();
        go.SetActive(false);
        return page;
    }

    public void Open()
    {
        gameObject.SetActive(true);
        var wm = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager;
        _cols[0].text = WeaponBlock("무기 1", wm != null ? wm.Weapon0Data : null);
        _cols[1].text = WeaponBlock("무기 2", wm != null ? wm.Weapon1Data : null);
    }

    public void Close() => gameObject.SetActive(false);

    // ── Presenter ────────────────────────────────────────────

    /// <summary>무기 한 칸 — 이름 · 강화/형(원거리는 파츠 합) · 공격 · 공속 · 스킬 단계 표 · R/E 스킬.</summary>
    private static string WeaponBlock(string slot, WeaponData w)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<size=70%><color=").Append(DimHex).Append('>').Append(slot).Append("</color></size>\n");
        if (w == null) return sb.Append("<color=").Append(FaintHex).Append(">비어 있음</color>").ToString();

        sb.Append("<size=").Append(NameSize / BodySize * 100f).Append("%><color=").Append(InkHex).Append('>').Append(w.displayName).Append("</color></size>\n");
        if (SkillTierResolver.IsRanged(w))
            sb.Append(Dim("파츠 합 ")).Append(Gold((RangedPartsState.Current?.TotalLevel ?? 0).ToString()));
        else
            sb.Append(Dim("강화 ")).Append(Gold($"+{w.enhanceLevel}")).Append("   ").Append(Dim("형 "))
              .Append(w.HasForm ? Gold($"「{w.form.DisplayName}」") : Faint("없음"));
        sb.Append('\n').Append(Dim("공격력 ")).Append(Ink(w.baseAttack.ToString())).Append("   ")
          .Append(Dim("공속 ")).Append(Ink($"×{w.attackSpeed:0.0}")).Append("\n\n");

        sb.Append(Dim("스킬 단계")).Append('\n');
        int now = SkillTierResolver.Resolve(w);
        var ms  = SkillTierResolver.Milestones(w);
        bool ranged = SkillTierResolver.IsRanged(w);
        for (int tier = SkillTierResolver.MinTier; tier <= SkillTierResolver.MaxTier; tier++)
        {
            int idx = tier - SkillTierResolver.MinTier - 1;
            string cond = idx < 0 ? "기본" : idx < ms.Length ? (ranged ? $"파츠 합 {ms[idx]}" : $"+{ms[idx]}") : "—";
            string body = idx < 0 ? null : UI_CruciblePanel.TierChangeBody(w, tier);
            string row  = $"{tier}단계  {cond}{(body != null ? $" — {body}" : "")}";
            sb.Append(tier == now ? Gold($"◆ {row}  · 지금") : tier < now ? Ink($"◆ {row}") : Faint($"◇ {row}")).Append('\n');
        }

        AppendSkill(sb, "R", w.skillQ, w.skillQCooldown);   // R 스킬(필드명 skillQ는 레거시 — Q는 유물 칸)
        AppendSkill(sb, "E", w.skillE, w.skillECooldown);
        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendSkill(System.Text.StringBuilder sb, string key, SkillSO skill, float cooldown)
    {
        if (skill == null) return;
        sb.Append('\n').Append(Ink($"[{key}] {skill.skillName}")).Append("  ").Append(Dim($"{cooldown:0.0#}초")).Append('\n');
        if (!string.IsNullOrEmpty(skill.description)) sb.Append(Dim(skill.description)).Append('\n');
    }

    private static string Gold(string s)  => $"<color={GoldHex}>{s}</color>";
    private static string Ink(string s)   => $"<color={InkHex}>{s}</color>";
    private static string Dim(string s)   => $"<color={DimHex}>{s}</color>";
    private static string Faint(string s) => $"<color={FaintHex}>{s}</color>";

    // ── View ─────────────────────────────────────────────────

    private void Build()
    {
        var panel = NewImage("Panel", transform, UITheme.Window);
        _panel = panel.rectTransform;
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(PanelW, PanelH);
        UITheme.StylePanel(panel, UITheme.Window);
        ShopUIStyle.Skin(panel, UISkin.RelicPartDraft?.background, sliced: true);   // 「되찾은 기억」 쪽과 같은 남색 판(있으면)

        var title = NewText("Title", _panel, "장비", TitleSize, UITheme.Gold, TextAlignmentOptions.Center, UIWeight.Light);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(PanelW - 80f, 44f));

        float colW = (PanelW - Pad * 2f - ColGap) * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            var t = NewText($"Weapon{i}", _panel, string.Empty, BodySize, UITheme.Thin.Ink, TextAlignmentOptions.TopLeft, UIWeight.Regular);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.lineSpacing = 6f;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(Pad + i * (colW + ColGap), -96f);
            rt.sizeDelta = new Vector2(colW, PanelH - 96f - BtnH - 52f);
            t.overflowMode = TextOverflowModes.Ellipsis;
            _cols[i] = t;
        }

        var btn = NewImage("Btn_Back", _panel, UITheme.SecondaryTint);
        var brt = btn.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 26f);
        brt.sizeDelta = new Vector2(BtnW, BtnH);
        UITheme.StyleButton(btn, UITheme.SecondaryTint);
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        b.onClick.AddListener(() => _onBack?.Invoke());
        btn.gameObject.AddComponent<UIButtonFeedback>();
        var lbl = NewText("Label", brt, "돌아가기", 24f, UITheme.Thin.Ink, TextAlignmentOptions.Center, UIWeight.Medium);
        var lrt = lbl.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, UIWeight weight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        UITheme.StyleThinText(tmp, weight, size, color);
        return tmp;
    }
}
