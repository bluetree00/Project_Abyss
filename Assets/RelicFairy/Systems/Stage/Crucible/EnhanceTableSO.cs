// EnhanceTableSO.cs
using System;
using UnityEngine;

/// <summary>
/// 재련소 무기 강화/승급의 정적 데이터(밸런싱). 런타임 상태 저장 금지.
/// 단계별 성공률/하락폭/재료비 + 등급별 상한 + 레벨당 공격 배율 + 승급 전설 정의.
/// 수치는 밸런싱 시작점(설계 #1 §4-1) — 인스펙터/에셋에서 조정.
/// </summary>
[CreateAssetMenu(fileName = "EnhanceTable", menuName = "Stage/Enhance Table")]
public class EnhanceTableSO : ScriptableObject
{
    /// <summary>현재 단계(level)에서 다음 단계로의 성공률/실패하락/재료비.</summary>
    [Serializable]
    public struct LevelStep
    {
        [Range(0f, 1f)] public float successRate; // level → level+1 성공 확률
        public int dropAmount;                     // 실패 시 하락폭
        public int cost;                           // 강화재료 비용
    }

    /// <summary>승급 전설 분기 정의.</summary>
    [Serializable]
    public struct LegendDef
    {
        public string     legendId;       // excalibur / galatine / arondight
        public string     displayName;    // 엑스칼리버 / 갈라틴 / 아론다이트
        public WeaponType weaponFilter;   // None = 모든 검 허용, 그 외 = 해당 타입만
        public float      attackBonusMult;// 승급 추가 배수(1.25 = +25%)
        public int        promoteCost;    // 승급 강화재료 대량 비용
    }

    [Header("단계 곡선 (index = 현재 단계, level→level+1)")]
    [SerializeField] private LevelStep[] _steps;

    [Header("레벨당 공격 증가율 (선형, +8%/단계)")]
    [SerializeField] private float _attackPctPerLevel = 0.08f;

    [Header("등급별 강화 상한")]
    [SerializeField] private int _maxCommon    = 6;
    [SerializeField] private int _maxRare      = 9;
    [SerializeField] private int _maxEpic      = 12;
    [SerializeField] private int _maxLegendary = 15;

    [Header("진화 1회당 상한 확장 (진화 = 강화의 끝이 아니라 다음 구간의 문)")]
    [SerializeField] private int _maxPerEvolution = 6;

    [Header("마스터리(진화 후 추가 구간) 1단계당 스킬 확장")]
    [Tooltip("스킬 피해 % 가산 (0.06 = +6%/단계)")]
    [SerializeField] private float _masterySkillDamagePerLevel = 0.06f;
    [Tooltip("스킬 쿨다운 감소 % (0.03 = -3%/단계)")]
    [SerializeField] private float _masterySkillCdrPerLevel = 0.03f;

    [Header("승급 전설 (택1 분기)")]
    [SerializeField] private LegendDef[] _legends;

    public LegendDef[] Legends => _legends ?? Array.Empty<LegendDef>();

    /// <summary>current level에서 성공률. 범위 밖이면 마지막 스텝값(없으면 0).</summary>
    public float SuccessAt(int level)
    {
        if (_steps == null || _steps.Length == 0) return level <= 0 ? 1f : 0f;
        int i = Mathf.Clamp(level, 0, _steps.Length - 1);
        return _steps[i].successRate;
    }

    public int DropAt(int level)
    {
        if (_steps == null || _steps.Length == 0) return 1;
        int i = Mathf.Clamp(level, 0, _steps.Length - 1);
        return Mathf.Max(0, _steps[i].dropAmount);
    }

    public int CostAt(int level)
    {
        if (_steps == null || _steps.Length == 0) return 1;
        int i = Mathf.Clamp(level, 0, _steps.Length - 1);
        return Mathf.Max(0, _steps[i].cost);
    }

    /// <summary>등급별 강화 상한(진화 미반영 기본 구간).</summary>
    public int MaxFor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common    => _maxCommon,
        ItemRarity.Rare      => _maxRare,
        ItemRarity.Epic      => _maxEpic,
        ItemRarity.Legendary => _maxLegendary,
        _                    => _maxCommon,
    };

    /// <summary>진화 단계까지 반영한 실제 강화 상한. 진화할 때마다 구간이 한 뼘씩 열린다.</summary>
    public int MaxFor(ItemRarity rarity, int evolutionStage)
        => MaxFor(rarity) + Mathf.Max(0, evolutionStage) * Mathf.Max(0, _maxPerEvolution);

    /// <summary>진화 구간(마스터리)에서 올린 단계 수. 미진화면 0.</summary>
    public int MasteryLevel(ItemRarity rarity, int evolutionStage, int enhanceLevel)
        => evolutionStage <= 0 ? 0 : Mathf.Max(0, enhanceLevel - MaxFor(rarity));

    /// <summary>마스터리 단계 → 스킬 피해 % 가산(0.36 = +36%).</summary>
    public float MasterySkillDamage(int masteryLevel)
        => Mathf.Max(0, masteryLevel) * _masterySkillDamagePerLevel;

    /// <summary>마스터리 단계 → 스킬 쿨다운 감소(0.18 = -18%).</summary>
    public float MasterySkillCdr(int masteryLevel)
        => Mathf.Max(0, masteryLevel) * _masterySkillCdrPerLevel;

    /// <summary>강화단계 + 승급 전설 → 공격 배율. baseAttackRaw 에 곱해 유효 공격력을 낸다.</summary>
    public float AttackMult(int level, string legendId)
    {
        float mult = 1f + _attackPctPerLevel * Mathf.Max(0, level);
        if (!string.IsNullOrEmpty(legendId) && TryGetLegend(legendId, out var legend))
            mult *= legend.attackBonusMult > 0f ? legend.attackBonusMult : 1f;
        return mult;
    }

    public bool TryGetLegend(string legendId, out LegendDef legend)
    {
        legend = default;
        if (string.IsNullOrEmpty(legendId) || _legends == null) return false;
        for (int i = 0; i < _legends.Length; i++)
        {
            if (string.Equals(_legends[i].legendId, legendId, StringComparison.OrdinalIgnoreCase))
            {
                legend = _legends[i];
                return true;
            }
        }
        return false;
    }
}
