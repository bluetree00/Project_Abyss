using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스킬 정의 SO — UI 표시 + 쿨다운 정보
/// 추후 ability(실행 데이터), 애니메이션 매핑 추가 예정
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "Game/SkillSO")]
public class SkillSO : ScriptableObject
{
    [Header("기본 정보")]
    public string skillName;
    [TextArea] public string description;
    [Tooltip("1단계(기본) 아이콘. 단계별 아이콘이 없으면 모든 단계가 이것을 쓴다.")]
    public Sprite icon;

    [Header("단계별 아이콘 (강화/진화로 기능이 바뀐다)")]
    [Tooltip("2단계 아이콘. 비우면 1단계 것을 쓴다.")]
    public Sprite iconTier2;
    [Tooltip("3단계 아이콘. 비우면 2단계 → 1단계 순으로 내려간다.")]
    public Sprite iconTier3;

    [Header("단계 변화 — 재련소 「다음 목표」에 미리 보인다")]
    [Tooltip("2단계가 되면 바뀌는 것(짧게, 예: 「추가 베기 3회」). 비우면 표시하지 않는다.")]
    [SerializeField] private string _tier2Change;
    [Tooltip("3단계가 되면 바뀌는 것(짧게).")]
    [SerializeField] private string _tier3Change;

    [Serializable]
    public struct EngravingDef
    {
        [Tooltip("무기 데이터(WeaponData.engravings)에 기록되는 키. 행동 SO가 이 키로 분기한다.")]
        public string id;
        [Tooltip("이 단계에 오를 때 고른다(2 또는 3). 같은 단계의 각인끼리 둘 중 하나다.")]
        public int    tier;
        public string displayName;
        [TextArea] public string description;
    }

    [Header("각인 — 단계에 오를 때 같은 단계 각인 중 하나를 고른다 (09-25 시범: 환영베기)")]
    [Tooltip("비어 있으면 그 스킬은 예전처럼 단계 분기가 자동으로 열린다.")]
    [SerializeField] private EngravingDef[] _engravings = Array.Empty<EngravingDef>();

    [Header("쿨다운")]
    public float cooldown = 5f;

    [Header("실행 행동")]
    [Tooltip("스킬의 실행 로직을 정의하는 SO. null이면 기본 애니메이션 재생만 수행")]
    public SkillBehaviorSO behavior;

    /// <summary>
    /// 단계(1~3)에 맞는 아이콘. 강화·진화로 스킬 기능이 바뀌므로 그림도 같이 바뀌어야 한다(09-21 사용자 요청).
    /// 해당 단계 그림이 없으면 아래 단계로 내려가 <see cref="icon"/>까지 폴백한다 — 비어 있어도 HUD가 빈칸이 되지 않는다.
    /// </summary>
    /// <summary>이 스킬의 각인 정의 전체.</summary>
    public IReadOnlyList<EngravingDef> Engravings => _engravings ?? Array.Empty<EngravingDef>();

    /// <summary>그 단계(2·3)에 오르면 바뀌는 것 — 재련소가 다음 단계 미리보기로 쓴다. 없으면 null.</summary>
    public string ChangeAtTier(int tier)
    {
        string s = tier == 2 ? _tier2Change : tier == 3 ? _tier3Change : null;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public Sprite IconForTier(int tier)
    {
        if (tier >= 3 && iconTier3 != null) return iconTier3;
        if (tier >= 2 && iconTier2 != null) return iconTier2;
        return icon;
    }
}
