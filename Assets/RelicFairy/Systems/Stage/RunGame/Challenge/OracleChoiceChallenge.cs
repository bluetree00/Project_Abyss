using UnityEngine;

/// <summary>
/// 신탁의 갈림길 — 비전투 이벤트방. 세 문 중 하나만 선택(되돌릴 수 없음), 나머지는 봉인된다.
///   · 연료문(청): 원석 보너스 + Silver 보상 — 룬재련 자원 편중
///   · 보물문(금): Gold 보상 — 아이템 편중
///   · 운명문(보라): 결정적 가중 롤(Fail~Platinum) — 도박형
/// 어포던스 원칙: 각 문 라벨로 무엇을 주는지 읽힌다. 선택의 무게(비가역).
/// </summary>
public sealed class OracleChoiceChallenge : WorldInteractionChallenge
{
    private const string TagFuel = "fuel";
    private const string TagItem = "item";
    private const string TagFate = "fate";
    private const int    FuelBonus = 12;   // 원석 보너스(연료문)

    private int _seed;

    /// <summary>SetupEventRoom이 호출. seed는 방 결정적 RNG 파생(운명문 리로드 동일).</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table, GameObject e1, GameObject e2, int seed)
    {
        InitBase(run, table, e1, e2);
        _seed = seed;
    }

    protected override void PlaceMarkers(Transform player)
    {
        Vector3 fwd = player.forward, right = player.right, at = player.position;
        AddMarker(at + fwd * 4.2f - right * 3.4f, "연료문", new Color(0.45f, 0.7f, 0.95f),
                  "<color=#73B3F2>[F]</color> 원석의 길", TagFuel);
        AddMarker(at + fwd * 4.6f,                "보물문", new Color(0.95f, 0.82f, 0.4f),
                  "<color=#F2D26A>[F]</color> 재화의 길", TagItem);
        AddMarker(at + fwd * 4.2f + right * 3.4f, "운명문", new Color(0.72f, 0.55f, 0.9f),
                  "<color=#B892E6>[F]</color> 운명의 길", TagFate);
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null) return;
        string tag = (string)m.Tag;

        // 나머지 문 봉인 표시
        for (int i = 0; i < AllMarkers.Count; i++)
            if (i != markerIndex) SetLabel(i, "봉인됨", new Color(0.45f, 0.47f, 0.52f));

        ChallengeGrade grade;
        if (tag == TagFuel)
        {
            if (Run?.FuelBank != null) Run.FuelBank.Add(FuelKind.RuneOre, FuelBonus);
            grade = ChallengeGrade.Silver;
            Notice($"연료문 — 원석 <color=#73B3F2>+{FuelBonus}</color> · {GradeName(grade)}");
        }
        else if (tag == TagItem)
        {
            grade = ChallengeGrade.Gold;
            Notice($"보물문 — <color=#F2D26A>{GradeName(grade)}</color> 보상");
        }
        else // Fate
        {
            grade = RollFate(new System.Random(_seed));
            Notice($"운명문 — <color=#B892E6>{GradeName(grade)}</color>!");
        }

        SetLabel(markerIndex, "열림", new Color(1f, 0.95f, 0.7f));
        FinishWith(grade, m.Pos);
    }

    // 가중 롤: Fail 12 / Bronze 22 / Silver 30 / Gold 24 / Platinum 12 (도박형, 상한 여지)
    private static ChallengeGrade RollFate(System.Random rng)
    {
        int r = rng.Next(100);
        if (r < 12) return ChallengeGrade.Fail;
        if (r < 34) return ChallengeGrade.Bronze;
        if (r < 64) return ChallengeGrade.Silver;
        if (r < 88) return ChallengeGrade.Gold;
        return ChallengeGrade.Platinum;
    }
}
