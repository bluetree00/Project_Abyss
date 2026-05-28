using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 해골 소환수 — LichSkeletonSummonPattern이 소환하는 일반 몬스터.
/// Lich 프리팹을 기반으로 의상·무기를 OnInitialized에서 비활성화해 뼈대만 표시한다.
/// </summary>
public class LichSkeletonMonster : MonsterBase
{
    // ── 상수 ─────────────────────────────────────────────
    public const string PrefabAddress = "LichSkeleton/LichSkeleton";

    // ── MonsterBase 추상 멤버 ─────────────────────────────
    protected override string ConfigAddress   => "LichSkeleton/LichSkeletonConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.2f;

    // ── Private ────────────────────────────────────────────
    private static readonly HashSet<string> HiddenObjectNames = new()
    {
        "SK_BookOpen Equip",
        "SK_Scythe Equip",
        "Bookss",
        "Clothing",
        "SkirtSeparate",
        "HoodDown",
        "HoodUp",
    };

    // ── Public Methods ────────────────────────────────────
    protected override void OnInitialized()
    {
        // 의상·무기 비활성화 — 뼈대(Phase2 스타일) 노출
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (HiddenObjectNames.Contains(t.name))
                t.gameObject.SetActive(false);
        }
    }
}
}
