using System;

/// <summary>
/// 설명 글 용어 풀이(11-02 Q22 사용자 「강타 뒤 피해 같은 룬 — 강타가 애초에 뭔지 모르니 이해가 안 된다」).
/// 게임 안에서만 쓰는 말이 설명에 나오면 그 아래에 옅은 한 줄을 붙인다. 지금은 「강타」 하나.
/// 강타 = 발동 계수가 문턱(1.5 · 필살 각인 단계마다 −0.25) 이상인 타격(<see cref="ProcCoefficient"/>) — 대검 · 석궁 공격 · 묵직한 손 · 구르기 뒤 첫 타 룬.
/// </summary>
public static class UIGlossary
{
    private const string HeavyWord = "강타";
    private const string HeavyText = "강타 = 대검 · 석궁 공격처럼 무거운 일격(「묵직한 손」 · 구르기 뒤 첫 타 룬으로도 난다) — 적이 크게 움찔한다";

    /// <summary>「강타」가 나오면 풀이 한 줄을 덧붙인다. 이미 붙었거나 없으면 그대로.
    /// 11-02 Q11 — 지금 언어의 낱말로 찾는다(영어 설명엔 「Heavy Hit」).</summary>
    public static string Append(string text)
    {
        string word = Loc.T(HeavyWord);
        if (string.IsNullOrEmpty(text) || text.IndexOf(word, StringComparison.Ordinal) < 0) return text;
        if (text.IndexOf(word + " =", StringComparison.Ordinal) >= 0) return text;
        return text + "\n<size=82%><color=#9AA0A6>" + Loc.T(HeavyText) + "</color></size>";
    }
}
