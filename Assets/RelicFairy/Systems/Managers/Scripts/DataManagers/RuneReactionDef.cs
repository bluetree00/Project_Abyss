using System.Collections.Generic;

/// <summary>
/// 속성 반응(Reaction) 정의 — <b>인접한 두 속성 존이 동시에 활성일 때</b> 발동하는 조합 효과.
///
/// 배경: 6속성이 각자 사일로(세로 의존만 있고 가로 상호작용 0)라, 랜덤을 얹어도
///       "제일 센 2개 찍기"로 고착된다. 반응은 <b>속성의 가치를 "옆에 뭐가 있느냐"에 종속</b>시켜
///       "제일 센 2개"라는 고정 정답을 없앤다(내 조합 기준 최선의 짝만 존재).
///
/// v1 설계(확정):
///  - 조합    : 보드에서 <b>맞닿은 인접 6쌍</b>만 (먼 짝은 후속)
///  - 발동    : 두 존이 각각 <b>1단계 이상</b>(점유 셀이 1단계 임계 이상)
///  - 강도    : min(두 존 단계) 에 비례 — 둘 다 깊이 갈수록 강함
///  - 거리    : 무관(동일 강도). 새 모델에선 속성이 룬에 박혀 자기 존에만 카운트되므로
///              멀든 가깝든 각 존을 채우는 데 드는 칸 수가 같다 → 거리 보너스는 의미 없음.
///  - 효과    : 6쌍이 각기 <b>다른 스탯</b>을 준다(크리/속공/흡혈/딜/생존) → 조합이 곧 빌드 성격.
///              (전이·폭발 같은 조건부 온히트 라이더는 후속 — 대상 상태 마킹 의존이라 검증이 필요)
///
/// 인접 관계(zone_map 기하): 빛–얼음, 빛–전기, 얼음–어둠, 전기–불, 어둠–풀, 불–풀.
/// 대각 반대(먼 짝): 빛↔풀, 얼음↔불, 전기↔어둠 — v1 미포함.
/// </summary>
public static class RuneReactionDef
{
    public enum StatKind
    {
        CritChance,      // 치명타 확률
        CritDamage,      // 치명타 피해
        AttackSpeed,     // 공격 속도
        DamagePercent,   // 모든 피해 %
        SkillCdr,        // 스킬 쿨타임 감소
        DamageReduction, // 받는 피해 감소
    }

    public sealed class Def
    {
        public readonly string   ZoneA;
        public readonly string   ZoneB;
        public readonly string   Key;           // 고유 식별자(로그·UI)
        public readonly string   DisplayName;
        public readonly string   Description;
        public readonly StatKind Stat;
        public readonly float    ValuePerTier;   // 단계당 가산량(min 단계 × 이 값)

        public Def(string a, string b, string key, string name, string desc, StatKind stat, float perTier)
        {
            ZoneA = a; ZoneB = b; Key = key; DisplayName = name; Description = desc;
            Stat = stat; ValuePerTier = perTier;
        }
    }

    // 인접 6쌍. 값(perTier)은 min단계 1~4에 곱해진다(예: 굴절 = 최대 +40% 치피).
    private static readonly Def[] s_Reactions =
    {
        new("LIGHT",    "ICE",      "refraction",  "굴절",   "느려진 적을 꿰뚫는 빛 — 치명타 피해 증가",   StatKind.CritDamage,      0.10f),
        new("LIGHT",    "ELECTRIC", "photocharge", "방전광", "빛과 번개의 공명 — 치명타 확률 증가",        StatKind.CritChance,      0.04f),
        new("ICE",      "DARK",     "permafrost",  "한파",   "얼어붙은 심연 — 받는 피해 감소",             StatKind.DamageReduction, 0.04f),
        new("ELECTRIC", "FIRE",     "overheat",    "과열",   "타오르는 전류 — 공격 속도 증가",             StatKind.AttackSpeed,     0.05f),
        // 부패(어둠+풀): 흡혈은 밸런스상 폐기됨 → 스킬 쿨감으로 임시 지정. 테마상 '지속피해(DoT) 증폭'이 더 맞으나 전용 채널이 필요해 후속.
        new("DARK",     "GRASS",    "decay",       "부패",   "잠식하는 부패 — 스킬 쿨타임 감소",           StatKind.SkillCdr,        0.03f),
        new("FIRE",     "GRASS",    "wildfire",    "산불",   "번지는 불길 — 모든 피해 증가",               StatKind.DamagePercent,   0.06f),
    };

    public static IReadOnlyList<Def> All => s_Reactions;
}
