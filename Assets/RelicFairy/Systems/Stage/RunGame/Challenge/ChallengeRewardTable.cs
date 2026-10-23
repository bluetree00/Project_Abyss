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

    // 10-22 1 · 1.3 · 1.7 · 2.2 → 1 · 1.15 · 1.2 · 1.2 — 강화는 Ch3에서 상한(+21)에 닿아 그 뒤 수입이 그대로 남았다(재련 개편 시뮬 R: 런 끝 130).
    //   Ch1(진화 0.78)은 그대로 두고 후반만 낮춘다 — 방 보상 인하와 합쳐 런 끝 82(기획/RelicFairy_재련개편_설계_20261018.md §7-b).
    private static float DefaultChapterScale(int chapter) => chapter switch
    {
        <= 1 => 1f,
        2    => 1.15f,
        _    => 1.2f,
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
        // 룬은 등급과 무관하게 1개 — 2개일 때 같은 선택 창이 연달아 떠 「보상이 두 번 받아진다」로 읽혔다(10-01 사용자). 등급은 희귀도 하한으로 가른다.
        // 강화재료 ×1.4(10-15 사용자 「재련소 재화를 높여 도박하는 재미」 · 확률은 EnhanceTable D3로 낮췄다): 40 · 28 · 18 · 10 · 4 → 56 · 39 · 25 · 14 · 6
        ChallengeGrade.Platinum => Make(grade, ItemRarity.Legendary, 1, 56),
        ChallengeGrade.Gold     => Make(grade, ItemRarity.Epic,      1, 39),
        ChallengeGrade.Silver   => Make(grade, ItemRarity.Rare,      1, 25),
        ChallengeGrade.Bronze   => Make(grade, ItemRarity.Common,    1, 14),
        _                       => Make(ChallengeGrade.Fail, ItemRarity.Common, 1, 6),
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
