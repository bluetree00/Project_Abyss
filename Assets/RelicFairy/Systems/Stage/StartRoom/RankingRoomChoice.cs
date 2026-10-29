using System;

/// <summary>
/// 거점 「기록의 문」 — 악몽까지 깬 사람이 어느 랭킹방(해방기 · 악몽)으로 갈지 고르는 두 장 창(10-29 2a 요청 · 설계 §5).
/// 던전 입구 창(<see cref="NightmareGateChoice"/>)과 같은 창을 글만 바꿔 쓴다 — 시간을 멈추고 입력을 막고, [Esc] · 바깥이면 들어가지 않는다.
/// 악몽을 아직 안 깼으면 부르는 쪽(2a 기록의 문)이 바로 해방기 방으로 보낸다.
/// </summary>
public static class RankingRoomChoice
{
    private static readonly NightmareGateChoice.Texts Texts = new(
        "어느 기록을 보러 가는가", "해방기 · 악몽 클리어 시간은 따로 쌓인다",
        "해방기 기록", "해방된 세계 클리어 시간\n상위 10 · 내 기록",
        "악몽 기록", "악몽 클리어 시간\n상위 10 · 내 기록",
        "카드를 눌러 고른다 · [Esc] 또는 바깥을 누르면 들어가지 않는다");

    /// <summary>창이 떠 있다(던전 입구 창과 같은 창 — ESC 듣개는 그 표시를 본다).</summary>
    public static bool IsOpen => NightmareGateChoice.IsOpen;

    /// <summary>창을 띄운다 — 고르면 그 시기(해방기 · 악몽), 닫으면 <paramref name="onCancel"/>.</summary>
    public static void Show(Action<StoryEra> onPicked, Action onCancel = null)
        => NightmareGateChoice.Show(Texts, null, nightmare => onPicked?.Invoke(nightmare ? StoryEra.NightmareMode : StoryEra.Liberated), onCancel);
}
