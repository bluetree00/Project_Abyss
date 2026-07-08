using UnityEngine;

/// <summary>
/// 챌린지 성과등급 + 챕터 → 보상 매핑(전투·상호작용 공유). 상호작용은 천장을 전투의 일정 비율로 클램프(의도된 예외 §6-3).
/// [CreateAssetMenu]로 .asset 생성 후 authoring. 에셋 미설정 시 코드 폴백(DefaultFor)이 동작해 무-에셋에서도 안전.
/// 벨류 원칙: Fail도 0/파괴가 아님(하향 연료 + 즉시가치).
/// </summary>
[CreateAssetMenu(fileName = "ChallengeRewardTable", menuName = "RelicFairy/Challenge/Reward Table")]
public sealed class ChallengeRewardTable : ScriptableObject
{
    [System.Serializable]
    private struct GradeReward
    {
        public ChallengeGrade grade;
        public ItemRarity     baseRarity;
        [Min(0)] public int   rewardCount;
        public FuelKind       fuelKind;
        [Min(0)] public int   fuelAmount;
        public bool           includeInstantValue;
    }

    [Header("등급별 보상(전투 기준)")]
    [SerializeField] private GradeReward[] grades;

    [Header("챕터 연료 스케일 (chapter-1 인덱스)")]
    [SerializeField] private float[] chapterFuelScale = { 1f, 1.3f, 1.7f, 2.2f };

    [Header("상호작용 천장 클램프 (전투의 비율)")]
    [SerializeField, Range(0.3f, 1f)] private float interactionCeilingRatio = 0.65f;

    /// <summary>등급+챕터 → 보상. isInteraction이면 개수/연료를 천장 비율로 클램프.</summary>
    public ChallengeReward For(ChallengeGrade grade, int chapter, bool isInteraction)
    {
        var gr = FindGrade(grade);
        float chapScale = ChapterScale(chapter);
        float ratio = isInteraction ? interactionCeilingRatio : 1f;

        return new ChallengeReward
        {
            baseRarity          = gr.baseRarity,
            rewardCount         = Mathf.Max(1, Mathf.RoundToInt(gr.rewardCount * ratio)),
            fuelKind            = gr.fuelKind,
            fuelAmount          = Mathf.Max(0, Mathf.RoundToInt(gr.fuelAmount * chapScale * ratio)),
            includeInstantValue = gr.includeInstantValue,
        };
    }

    /// <summary>SO 인스턴스 없이 grade+chapter로 기본 보상 계산(RoomClearGate MVP 폴백). 에셋 authoring 시 For() 우선.</summary>
    public static ChallengeReward DefaultReward(ChallengeGrade grade, int chapter, bool isInteraction)
    {
        var gr = DefaultFor(grade);
        float chapScale = DefaultChapterScale(chapter);
        float ratio = isInteraction ? 0.65f : 1f;
        return new ChallengeReward
        {
            baseRarity          = gr.baseRarity,
            rewardCount         = Mathf.Max(1, Mathf.RoundToInt(gr.rewardCount * ratio)),
            fuelKind            = gr.fuelKind,
            fuelAmount          = Mathf.Max(0, Mathf.RoundToInt(gr.fuelAmount * chapScale * ratio)),
            includeInstantValue = gr.includeInstantValue,
        };
    }

    private static float DefaultChapterScale(int chapter) => chapter switch
    {
        <= 1 => 1f,
        2    => 1.3f,
        3    => 1.7f,
        _    => 2.2f,
    };

    // ── 내부 ─────────────────────────────────────────────
    private GradeReward FindGrade(ChallengeGrade grade)
    {
        if (grades != null)
            for (int i = 0; i < grades.Length; i++)
                if (grades[i].grade == grade) return grades[i];
        return DefaultFor(grade);
    }

    private static GradeReward DefaultFor(ChallengeGrade grade) => grade switch
    {
        ChallengeGrade.Platinum => Make(grade, ItemRarity.Legendary, 2, 40),
        ChallengeGrade.Gold     => Make(grade, ItemRarity.Epic,      2, 28),
        ChallengeGrade.Silver   => Make(grade, ItemRarity.Rare,      1, 18),
        ChallengeGrade.Bronze   => Make(grade, ItemRarity.Common,    1, 10),
        _                       => Make(ChallengeGrade.Fail, ItemRarity.Common, 1, 4),
    };

    private static GradeReward Make(ChallengeGrade grade, ItemRarity rarity, int count, int fuel) => new GradeReward
    {
        grade = grade, baseRarity = rarity, rewardCount = count,
        fuelKind = FuelKind.EnhanceMaterial, fuelAmount = fuel, includeInstantValue = true,
    };

    private float ChapterScale(int chapter)
    {
        if (chapterFuelScale == null || chapterFuelScale.Length == 0) return 1f;
        int idx = Mathf.Clamp(chapter - 1, 0, chapterFuelScale.Length - 1);
        return chapterFuelScale[idx];
    }
}
