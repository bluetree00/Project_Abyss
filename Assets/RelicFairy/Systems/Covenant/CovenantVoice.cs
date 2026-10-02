using RelicFairy.UI;

/// <summary>
/// 서약서 한 마디(설계서 §6 — 멀린 = 동료 반말 · 그림자) — 대사 CSV 키를 시기 꼬리 · 반복 단계로 골라 자막(비모달)으로.
/// 기억 되찾기의 「돌아온 한 마디」(<see cref="RelicMemoryRecall.SayReturn"/>)와 같은 방식.
/// </summary>
public static class CovenantVoice
{
    public const string FirstWrite = "Covenant_FirstWrite";
    public const string Append     = "Covenant_Append";
    public const string Rewrite    = "Covenant_Rewrite";
    public const string Full       = "Covenant_Full";
    public const string ChainEnd   = "Covenant_ChainEnd";

    public static void Say(string key)
    {
        var lines = Managers.DialogueData?.GetVisitLines(key);
        if (lines == null) return;
        foreach (var l in lines)
        {
            if (l == null || string.IsNullOrEmpty(l.text)) continue;
            UI_BossBark.Show(l.text, BossBarkType.MerlinNarration, l.speaker);
        }
    }
}
