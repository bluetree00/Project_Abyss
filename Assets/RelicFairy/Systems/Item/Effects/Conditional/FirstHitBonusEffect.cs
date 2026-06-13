using UnityEngine;

/// <summary>
/// 단발 강화 조건 아이템 — 조건 충족 후 "첫 공격 1회"에만 피해 보너스(지속 버프 아님).
/// ItemEffectManager의 다음-공격-강화 버퍼(QueueNextAttackBonus)를 사용한다.
///
/// trigger:
///  • Stationary        : duration초 이상 정지 후 1회 큐(이동 시 재무장). 정지의 힘/폭발.
///  • FirstAttackInRoom : 방 진입 시 1회 큐. 첫 타격.
///
/// value = 피해 보너스(0.2 = +20%). duration = 정지 요구 시간.
/// </summary>
public sealed class FirstHitBonusEffect : ItemEffectBase
{
    private float _idleTime;
    private bool  _armed;   // Stationary: 정지 도달 후 큐 완료 표식(이동 시 해제)

    public FirstHitBonusEffect(ItemEffectSlot s) : base(s) { }

    /// <summary>이벤트 훅/틱 수신 위해 활성 고정. 정적 스탯엔 기여하지 않는다.</summary>
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        if (_trigger == "FirstAttackInRoom") Queue();
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_trigger != "Stationary") return;

        bool moving = ctx?.Player != null && ctx.Player.MoveDirection.sqrMagnitude > 0.01f;
        if (moving) { _idleTime = 0f; _armed = false; return; }

        _idleTime += deltaTime;
        float need = _duration > 0f ? _duration : 0.5f;
        if (_idleTime >= need && !_armed) { Queue(); _armed = true; }
    }

    private void Queue()
        => GameRunBootstrapper.Instance?.Run?.EffectManager?.QueueNextAttackBonus(_value);
}
