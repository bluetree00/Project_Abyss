using UnityEngine;

/// <summary>
/// 시기별 레벨디자인 수치 한 표(10-01, 순환 개정 v3 §4-2 + 보상 차등) — 봉인기 · 해방기(첫 리치 붕괴 뒤) · 악몽 모드.
/// 봉인기는 지금 수치 그대로(×1). 해방기부터 몬스터가 세지고 많아지는 만큼 보상(룬 등급 · 연료 · 골드)도 오른다.
/// 보스는 여기서 세지지 않는다 — 보스의 시기 차이는 페이지(해방 2페이지 · 악몽 강화)가 맡는다.
///
/// 꽂히는 곳: 몬스터 스탯 = <c>GameRunSession.CurrentDifficultyScale</c> · 수량 = <c>CurrentMonsterCountScale</c> ·
/// 정예 확률 = <c>RunSequencer.RollKind</c> · 룬 등급 가중 · 연료 = <c>RoomRewardTable.For</c> · 골드 = <c>GameRunSession.AddGold</c> ·
/// 이벤트방 놀이 제한 시간 = <c>EventMinigame</c>.
/// 몬스터 특성(v3 §4-3)은 다음 단계 — 여기엔 숫자만 있다.
/// </summary>
public static class EraBalance
{
    public readonly struct Spec
    {
        /// <summary>일반 · 정예 몬스터 스탯(HP · 공격) — 챕터 배율 × 심연 깊이 배율에 곱한다.</summary>
        public readonly float StatScale;
        /// <summary>웨이브 마릿수 — 챕터 수량 배율에 곱한다.</summary>
        public readonly float CountScale;
        /// <summary>정예 방 확률에 더한다.</summary>
        public readonly float EliteBonus;
        /// <summary>룬 등급 가중을 위로 옮기는 단계(0 = 그대로).</summary>
        public readonly int   RarityStep;
        /// <summary>방 클리어 연료(원석 · 강화재료).</summary>
        public readonly float FuelScale;
        /// <summary>골드 획득.</summary>
        public readonly float GoldScale;
        /// <summary>이벤트방 놀이 제한 시간.</summary>
        public readonly float EventTimeScale;

        public Spec(float stat, float count, float elite, int rarityStep, float fuel, float gold, float eventTime)
        {
            StatScale = stat; CountScale = count; EliteBonus = elite; RarityStep = rarityStep;
            FuelScale = fuel; GoldScale = gold; EventTimeScale = eventTime;
        }
    }

    // 가로로 읽으면 봉인기(지금 그대로) → 해방기(같은 빌드로 한 번 더 오를 수 있는 선) → 악몽(성장이 받쳐 주는 도전).
    private static readonly Spec SealedSpec    = new(stat: 1.0f, count: 1.0f, elite: 0.00f, rarityStep: 0, fuel: 1.00f, gold: 1.0f, eventTime: 1.00f);
    private static readonly Spec LiberatedSpec = new(stat: 1.2f, count: 1.1f, elite: 0.05f, rarityStep: 1, fuel: 1.25f, gold: 1.2f, eventTime: 1.00f);
    private static readonly Spec NightmareSpec = new(stat: 1.4f, count: 1.2f, elite: 0.10f, rarityStep: 2, fuel: 1.50f, gold: 1.4f, eventTime: 0.85f);

    // 룬 등급 가중 옮기기 — 단계마다 일반에서 9점을 떼어 레어 45% · 에픽 40% · 전설 15%로,
    // 일반이 없는 표(정예)는 레어에서 8점을 떼어 에픽 · 전설 반반으로.
    private const float CommonTakePerStep = 9f;
    private const float RareTakePerStep   = 8f;

    public static Spec Current => For(StoryProgress.Era);

    public static Spec For(StoryEra era) => era switch
    {
        StoryEra.Liberated     => LiberatedSpec,
        StoryEra.NightmareMode => NightmareSpec,
        _                      => SealedSpec,
    };

    /// <summary>룬 등급 가중(일반 · 레어 · 에픽 · 전설)을 시기 단계만큼 위로 옮긴다. 합은 그대로.</summary>
    public static (float common, float rare, float epic, float legendary) ShiftRarity(float common, float rare, float epic, float legendary, int step)
    {
        if (step <= 0) return (common, rare, epic, legendary);
        if (common > 0f)
        {
            float take = Mathf.Min(common, CommonTakePerStep * step);
            return (common - take, rare + take * 0.45f, epic + take * 0.40f, legendary + take * 0.15f);
        }
        float t = Mathf.Min(rare, RareTakePerStep * step);
        return (common, rare - t, epic + t * 0.5f, legendary + t * 0.5f);
    }

    /// <summary>로그 한 줄 — 런 시작 · 확인 메뉴.</summary>
    public static string Describe(StoryEra era)
    {
        var s = For(era);
        return $"{era} — 스탯×{s.StatScale:0.##} · 수량×{s.CountScale:0.##} · 정예+{s.EliteBonus:0.##} · 룬 등급 +{s.RarityStep}단 · 연료×{s.FuelScale:0.##} · 골드×{s.GoldScale:0.##} · 놀이 시간×{s.EventTimeScale:0.##}";
    }
}
