using System.Text.RegularExpressions;

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

    // ── Static ───────────────────────────────────────────
    // 부호 · 곱 기호가 붙은 수 + 단위. 앞 글자가 낱말(한글 · 영문)이면 그 안의 숫자라 건드리지 않는다.
    private static readonly Regex s_number =
        new(@"(?<![\w#])([+\-−×]?\d+(?:\.\d+)?(?:%p|%|초|m|회|체|중첩|칸|배)?)", RegexOptions.Compiled);

    // ── Public Methods ───────────────────────────────────

    /// <summary>수치에 색(+굵게)을 입힌다. 빈 글이면 그대로.</summary>
    public static string Numbers(string text, string hex)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return s_number.Replace(text, m => $"<color={hex}><b>{m.Value}</b></color>");
    }

    /// <summary>어절 줄바꿈 + 수치 색 — 설명 한 덩어리를 넣을 때 쓴다.</summary>
    public static string Words(string text, string hex) => Numbers(UIKoreanWrap.Words(text), hex);
}
