using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기 진화(파생 분기) 테이블.
///
/// 설계 핵심: <b>진화 대상을 WeaponSO 통째로 가리킨다.</b>
/// WeaponSO가 이미 외형(prefabKey)·무브셋(animationSet/abilitySet)·스킬(Q/E/R)·아이콘·이름·타입·스탯을
/// 전부 소유하므로, "진화 = 다른 WeaponSO가 된다"로 정의하면 필드를 하나씩 오버라이드할 필요가 없다.
///
/// 각 WeaponSO가 자신의 evolution을 가리키므로 트리가 데이터만으로 자란다:
///   무명 무기 ──▶ 카타나 / 대검 / 활 / 석궁 ──▶ 전설(엑스칼리버 …)
/// evolution이 비어 있으면 그 무기가 최종 형태.
/// </summary>
[CreateAssetMenu(fileName = "WeaponEvolution", menuName = "Weapon/Weapon Evolution")]
public class WeaponEvolutionSO : ScriptableObject
{
    /// <summary>진화 분기 1개.</summary>
    [Serializable]
    public class Branch
    {
        [Tooltip("분기 식별자(세이브/복원·UI 선택용). 예: katana, greatsword, excalibur")]
        public string branchId;

        [Tooltip("진화 결과 무기. 외형·무브셋·스킬·아이콘·이름·타입·스탯을 전부 이 SO가 소유한다.")]
        public WeaponSO target;

        [Tooltip("이 분기를 열기 위한 최소 강화 레벨. 0이면 제한 없음.")]
        public int requiredEnhanceLevel;

        [Tooltip("진화 비용(강화재료). 0이면 무료.")]
        public int cost;

        public bool IsValid => target != null && !string.IsNullOrEmpty(branchId);
    }

    [Header("분기 목록 (택1)")]
    [SerializeField] private Branch[] _branches;

    public IReadOnlyList<Branch> Branches => _branches ?? Array.Empty<Branch>();

    public bool TryGet(string branchId, out Branch branch)
    {
        branch = null;
        if (string.IsNullOrEmpty(branchId) || _branches == null) return false;

        for (int i = 0; i < _branches.Length; i++)
        {
            var b = _branches[i];
            if (b != null && b.IsValid && b.branchId == branchId)
            {
                branch = b;
                return true;
            }
        }
        return false;
    }

    /// <summary>해당 강화 레벨에서 열려 있는 분기인지.</summary>
    public static bool IsUnlocked(Branch branch, int enhanceLevel)
        => branch != null && branch.IsValid && enhanceLevel >= branch.requiredEnhanceLevel;
}
