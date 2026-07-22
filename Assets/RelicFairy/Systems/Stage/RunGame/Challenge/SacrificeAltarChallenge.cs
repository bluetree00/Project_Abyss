using UnityEngine;

/// <summary>
/// 제물 제단 — 비전투 이벤트방. 물약을 제물로 바친 만큼 확정 등급 보상(리스크-리워드, 도박과 대비되는 결정적).
///   · 제단[F]: 보유 물약 전부를 바침 → 3+개 Platinum / 2개 Gold / 1개 Silver / 0개(바칠 것 없음)=Bronze
///   · 떠남[F]: 아무것도 바치지 않고 통과 → Bronze
/// 회복 자원(물약)을 대가로 확정 보상을 얻는 선택. gamble(운)과 달리 결과가 결정적이다.
/// </summary>
public sealed class SacrificeAltarChallenge : WorldInteractionChallenge
{
    private const string TagAltar = "altar";
    private const string TagLeave = "leave";

    /// <summary>SetupEventRoom이 호출.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table, GameObject e1, GameObject e2)
        => InitBase(run, table, e1, e2);

    protected override void PlaceMarkers(Transform player)
    {
        Vector3 fwd = player.forward, right = player.right, at = player.position;
        AddMarker(at + fwd * 3.8f - right * 2.2f, "제물 제단", new Color(0.95f, 0.55f, 0.45f),
                  "<color=#FF9668>[F]</color> 물약을 바친다", TagAltar);
        AddMarker(at + fwd * 3.8f + right * 2.2f, "떠난다", new Color(0.7f, 0.72f, 0.78f),
                  "<color=#B8C0CC>[F]</color> 그냥 지나친다", TagLeave);
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null) return;

        if ((string)m.Tag == TagLeave)
        {
            Notice("제단을 지나쳤다 — <color=#B8C0CC>브론즈</color>");
            SetLabel(markerIndex, "…", new Color(0.7f, 0.72f, 0.78f));
            FinishWith(ChallengeGrade.Bronze, m.Pos);
            return;
        }

        // 제단: 보유 물약 전부를 제물로.
        var ps = Run?.PlayerState;
        int given = ps != null ? ps.PotionCount : 0;
        for (int i = 0; i < given; i++) ps.TryConsumePotion();

        ChallengeGrade grade = given >= 3 ? ChallengeGrade.Platinum
                             : given == 2 ? ChallengeGrade.Gold
                             : given == 1 ? ChallengeGrade.Silver
                             :              ChallengeGrade.Bronze;

        string msg = given > 0
            ? $"물약 {given}개를 바쳤다 — <color=#8fd3ff>{GradeName(grade)}</color>"
            : "바칠 물약이 없다 — <color=#B8C0CC>브론즈</color>";
        Notice(msg);
        SetLabel(markerIndex, "봉헌!", new Color(1f, 0.75f, 0.4f));
        FinishWith(grade, m.Pos);
    }
}
