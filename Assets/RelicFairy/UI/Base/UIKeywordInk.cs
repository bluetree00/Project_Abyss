using System.Text;
using System.Text.RegularExpressions;
using TMPro;

/// <summary>
/// 설명 글에서 <b>숫자(수치 · 단위)</b>만 골라 색을 입힌다 — 한 색 글자는 「얼마나」가 한눈에 안 읽힌다(09-29 사용자 「키워드 부분은 색을 바꿔 가독성을」).
/// <para>줄바꿈 어절 처리(<see cref="UIKoreanWrap.Words"/>)를 먼저 하고 그 결과에 입힌다 — Words는 태그가 든 글을 건드리지 않는다.
/// 태그 안에는 숫자가 없으므로(nobr · zwsp) 태그를 깨지 않는다.</para>
/// </summary>
public static class UIKeywordInk
{
    // ── Constants ────────────────────────────────────────
    /// <summary>어두운 판 위 — 금.</summary>
    public const string OnDark = "#E8BA54";
    /// <summary>밝은 양피지 위 — 짙은 적갈(원색 아님).</summary>
    public const string OnParchment = "#8A3418";

    /// <summary>
    /// 효과 설명 글의 「조용한」 수치 잉크(10-15 · <see cref="Values"/>) — 금은 「준비 · 지금」 전용이라(톤 v2) 바탕 잉크(#ECE6D8)보다
    /// 한 단계 밝은 따뜻한 흰색. 차이는 굵기(Noto 500)가 주로 낸다.
    /// </summary>
    public const string QuietOnDark = "#FFF6E2";
    /// <summary>양피지 위 수치 잉크 — 상점 「수치 잉크」 · 서약 카드와 같은 #5E1A0A.</summary>
    public const string QuietOnParchment = "#5E1A0A";

    // ── Static ───────────────────────────────────────────
    // 부호 · 곱 기호가 붙은 수 + 단위. 앞 글자가 낱말(한글 · 영문)이면 그 안의 숫자라 건드리지 않는다.
    private static readonly Regex s_number =
        new(@"(?<![\w#])([+\-−×]?\d+(?:\.\d+)?(?:%p|%|초|m|회|체|중첩|칸|배)?)", RegexOptions.Compiled);

    private static readonly Regex s_tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly StringBuilder s_sb = new(160);
    private static string s_mediumTag;

    // ── Public Methods ───────────────────────────────────

    /// <summary>
    /// 효과 설명 글 수치 강조(10-15 사용자 「특정 효과들이 있는 설명 텍스트에는 기능을 보기좋게 텍스트 강조 — 과하지 않게」).
    /// 수치만 Noto 500(설계 「숫자만 500」) + 조용한 잉크 — 금 · 가짜 굵게 · 크기 변화 없음. 태그 안과 이미 색이 칠해진 구간(&lt;color&gt;…&lt;/color&gt;)은 그대로 둔다
    /// (<see cref="Numbers"/>는 &lt;size=55%&gt; 같은 태그 안 숫자까지 감싸 태그를 깬다). 500 글꼴이 아직 안 올라왔으면 색만.
    /// </summary>
    public static string Values(string text, bool onParchment = false)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string hex  = onParchment ? QuietOnParchment : QuietOnDark;
        string open = MediumFontTag();
        string pre  = open != null ? $"{open}<color={hex}>" : $"<color={hex}>";
        string post = open != null ? "</color></font>" : "</color>";
        s_sb.Clear();
        int depth = 0, at = 0;
        foreach (Match m in s_tag.Matches(text))
        {
            AppendPlain(text, at, m.Index - at, depth > 0, pre, post);
            string tag = m.Value;
            if (tag.StartsWith("<color", System.StringComparison.OrdinalIgnoreCase)) depth++;
            else if (tag.StartsWith("</color", System.StringComparison.OrdinalIgnoreCase) && depth > 0) depth--;
            s_sb.Append(tag);
            at = m.Index + m.Length;
        }
        AppendPlain(text, at, text.Length - at, depth > 0, pre, post);
        return s_sb.ToString();
    }

    /// <summary>수치에 색(+굵게)을 입힌다. 빈 글이면 그대로.</summary>
    public static string Numbers(string text, string hex)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return s_number.Replace(text, m => $"<color={hex}><b>{m.Value}</b></color>");
    }

    /// <summary>어절 줄바꿈 + 수치 색 — 설명 한 덩어리를 넣을 때 쓴다.</summary>
    public static string Words(string text, string hex) => Numbers(UIKoreanWrap.Words(text), hex);

    // ── Private Methods ──────────────────────────────────

    private static void AppendPlain(string text, int start, int length, bool colored, string pre, string post)
    {
        if (length <= 0) return;
        string part = text.Substring(start, length);
        if (colored) { s_sb.Append(part); return; }
        int last = 0;
        foreach (Match m in s_number.Matches(part))
        {
            s_sb.Append(part, last, m.Index - last).Append(pre).Append(m.Value).Append(post);
            last = m.Index + m.Length;
        }
        s_sb.Append(part, last, part.Length - last);
    }

    /// <summary>
    /// Noto 500 글꼴 태그(UI_AssembleCard와 같은 길) — TMP는 &lt;font="이름"&gt;을 MaterialReferenceManager에서 먼저 찾으므로 한 번 등록한다.
    /// 글꼴이 아직 안 올라왔으면 null(색만 입힌다).
    /// </summary>
    private static string MediumFontTag()
    {
        if (s_mediumTag != null) return s_mediumTag;
        var f = UISkin.ThemeFonts != null ? UISkin.ThemeFonts.Medium : null;
        if (f == null) return null;
        try { MaterialReferenceManager.AddFontAsset(f); }
        catch (System.ArgumentException) { }   // 같은 재질이 다른 경로로 먼저 등록됨 — 글꼴 등록은 이미 끝났다
        s_mediumTag = $"<font=\"{f.name}\">";
        return s_mediumTag;
    }
}
