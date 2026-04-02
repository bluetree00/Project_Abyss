using UnityEngine;

/// <summary>
/// 광전사 특성2: 공격 시 1초간 공격 속도 5% 증가, 최대 5회 중첩
/// ITickablePassive 구현 — 시간 경과 시 스택 만료 처리.
/// </summary>
public class BerserkerFrenzyPassive : CharacterPassiveBase, ITickablePassive
{
    private readonly float _attackSpeedPerStack;
    private readonly int   _maxStacks;
    private readonly float _stackDuration;

    private int   _currentStacks;
    private float _timer;
    private PlayerController _cachedCtrl;

    public BerserkerFrenzyPassive(float attackSpeedPerStack = 0.05f, int maxStacks = 5, float stackDuration = 1f)
    {
        _attackSpeedPerStack = attackSpeedPerStack;
        _maxStacks           = maxStacks;
        _stackDuration       = stackDuration;
    }

    public override string         PassiveName => "광란";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        _cachedCtrl = ctrl;
        _currentStacks = Mathf.Min(_currentStacks + 1, _maxStacks);
        _timer = _stackDuration;
        ApplySpeedBonus(ctrl);
    }

    public void Tick(float deltaTime)
    {
        if (_currentStacks <= 0) return;

        _timer -= deltaTime;
        if (_timer <= 0f)
        {
            _currentStacks = 0;
            _timer = 0f;
            if (_cachedCtrl != null)
                ApplySpeedBonus(_cachedCtrl);
        }
    }

    private void ApplySpeedBonus(PlayerController ctrl)
    {
        float bonus = _currentStacks * _attackSpeedPerStack;
        ctrl.RuntimeStats.SetBonusAttackSpeed(bonus);
    }
}
