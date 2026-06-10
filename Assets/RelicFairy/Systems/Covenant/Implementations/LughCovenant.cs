using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 루의 서약 — 모든 기술의 신과 맺은 만능 계약
///
/// [Basic]    방 입장마다 무작위 스탯 1종 +15% (방 종료 시 소멸)
/// [Enhanced] +25%, 2종 동시
/// [Evolved]  방마다 획득한 스탯 버프가 소멸하지 않고 런 내 영구 누적
/// </summary>
public sealed class LughCovenant : CovenantBase
{
    private const int V_BONUS_VALUE = 0;
    private const int V_BONUS_COUNT = 1;

    public override string CovenantId => CovenantFactory.Lugh;

    // ── 런타임 상태 ──────────────────────────────────────
    private readonly List<StatModifier> _roomModifiers      = new();
    private readonly List<StatModifier> _permanentModifiers = new();

    private float BonusValue => V(V_BONUS_VALUE,  0.15f);
    private int   BonusCount => VI(V_BONUS_COUNT, 1);
    private bool  IsEvolved  => Stage == CovenantStage.Evolved;

    private static readonly StatType[] RandomPool =
    {
        StatType.AttackPower, StatType.Defense, StatType.MaxHp,
        StatType.MoveSpeed,   StatType.AttackSpeed,
    };

    // ── 스탯 레이어 기여 ─────────────────────────────────
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        foreach (var m in _roomModifiers)      yield return m;
        foreach (var m in _permanentModifiers) yield return m;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnRoomEnter()
    {
        _roomModifiers.Clear();

        for (int i = 0; i < BonusCount; i++)
        {
            var type = RandomPool[Random.Range(0, RandomPool.Length)];
            _roomModifiers.Add(new StatModifier(type, BonusValue));
        }

        RefreshStats();
    }

    public override void OnRoomClear()
    {
        if (IsEvolved)
            _permanentModifiers.AddRange(_roomModifiers);

        _roomModifiers.Clear();
        RefreshStats();
    }
}
