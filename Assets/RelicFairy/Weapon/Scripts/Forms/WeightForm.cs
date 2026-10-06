using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 마르미아도아즈(대검) 형 「무게」 — 필살. 기획 장비개선 §4-3.
/// - 쌓기: 평타가 맞은 휘두름마다 +1(최대 4). 피격돼도 잃지 않고, 5초 동안 휘두르지 않으면 0.
/// - 발동: 가득이면 콤보 마지막 타가 「내려찍기」 — 반드시 치명타 + 반경 3.5 m 충격파(공격력 100%) + 일반 몹 경직 0.8초.
/// - 확정 치명은 가득으로 마지막 타 단계에 들어설 때만 걸고(ArmForceCrit — 다음 근접 타 1회), 콤보가 끊기면 푼다.
///   마지막 타가 가득을 채운 순간에는 걸지 않는다(같은 휘두름의 다음 타로 샌다). 다른 장치가 이미 건 확정 치명은 건드리지 않는다.
/// 변주: Ⅱ 균열 / 여진 · Ⅲ 무거운 손 / 짓누름(Task 4).
/// </summary>
public sealed class WeightForm : WeaponFormBase
{
    public const string Fissure = "fissure", Aftershock = "aftershock", HeavyHand = "heavy_hand", Crush = "crush";
    private const float StunSeconds = 0.8f, SlamForward = 1.5f;

    // 10-07 예산 실측(단일 표적): 여진 100% · 짓누름 +20%로 Ⅲ +39%(목표 +25% 이내) → 여진 피해 절반 · 짓누름 +12%
    private const float AftershockDelay = 0.4f, AftershockScale = 1.4f, AftershockRatio = 0.5f,
                        FissureSeconds = 2f, FissureInterval = 0.5f, FissureRatio = 0.3f,
                        CrushAmp = 0.12f, CrushSeconds = 2f, FissureVfxScale = 0.3f;

    private readonly List<MonsterBase> _buf = new(16);
    private float   _lastSwingAt = -999f;
    private bool    _armed, _wasFinisherStep;
    private float   _aftershockAt = -1f, _fissureUntil = -1f, _fissureNext;
    private Vector3 _slamAt;

    public WeightForm(PlayerController player, WeaponData weapon) : base(player, weapon) { }

    public override void OnMeleeHit(in HitInfo hit, bool newSwing, bool finisherStep)
    {
        if (!newSwing) return;
        _lastSwingAt = Time.time;
        if (finisherStep && IsFull) { Slam(); return; }
        SetStacks(Stacks + 1);
    }

    /// <summary>
    /// 무거운 손 — 무게가 가득일 때 E · R 적중은 치명 배율만큼 더 준다(치명 표시).
    /// 확정 치명 훅(ArmForceCrit)은 근접 평타 전용이라 스킬에 걸리지 않는다 → 추가 피해로 같은 몫을 준다.
    /// </summary>
    public override void OnSkillHit(in HitInfo hit)
    {
        if (!Has(HeavyHand) || !IsFull || hit.IsCritical || hit.Target == null) return;
        if (!hit.Target.TryGetComponent(out MonsterBase mb) || mb.IsDead) return;
        float mult  = Weapon.critDamage + Player.RuntimeStats.CritDamageBonus;
        float bonus = hit.Damage * Mathf.Max(0f, mult - 1f);
        if (bonus <= 0f) return;
        CombatQuery.DealSynergyDamage(mb, bonus, Player.gameObject, 1f, isCrit: true);
        Debug.Log($"[WeaponForm] 무거운 손 — +{bonus:0}");
    }

    public override void Tick(float dt)
    {
        if (_aftershockAt > 0f && Time.time >= _aftershockAt)
        {
            _aftershockAt = -1f;
            int n = FormStrike.Radius(Player, _slamAt, Def.TriggerRange * AftershockScale, Atk * Def.TriggerAtkRatio * AftershockRatio, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale * AftershockScale);
            Debug.Log($"[WeaponForm] 여진 — 맞힘 {n}");
        }
        if (_fissureUntil > 0f && Time.time >= _fissureNext)
        {
            _fissureNext += FissureInterval;
            int n = FormStrike.Radius(Player, _slamAt, Def.TriggerRange, Atk * FissureRatio, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale * FissureVfxScale);   // 갈라진 땅이 남아 있음을 보이게
            Debug.Log($"[WeaponForm] 균열 — 맞힘 {n}");
            if (Time.time >= _fissureUntil) _fissureUntil = -1f;
        }
        int  step         = Player.Combo.CurrentComboStep;
        bool finisherNext = step >= Mathf.Max(1, Weapon.groundEndCount) - 1;
        bool entered      = finisherNext && !_wasFinisherStep;
        _wasFinisherStep  = finisherNext;
        if (entered && IsFull && !_armed && !Player.RuntimeStats.ForceCritArmed)
        {
            Player.RuntimeStats.ArmForceCrit(0f);   // 0 = 무기 치명 배율
            _armed = true;
        }
        else if (_armed && !finisherNext)
        {
            Disarm();   // 마지막 타를 안 쓰고 콤보가 끊겼다 — 다음 평타로 새지 않게
        }
        if (Stacks > 0 && Time.time - _lastSwingAt > Def.IdleSeconds)
        {
            SetStacks(0);
            if (_armed) Disarm();
        }
    }

    public override void Dispose()
    {
        if (_armed) Disarm();
    }

    private void Disarm()
    {
        Player.RuntimeStats.ConsumeForceCrit(out _);
        _armed = false;
    }

    private void Slam()
    {
        _armed  = false;   // 확정 치명은 이 타가 이미 썼다
        _slamAt = Player.transform.position + Player.AimForward * SlamForward;
        int hits = FormStrike.Radius(Player, _slamAt, Def.TriggerRange, Atk * Def.TriggerAtkRatio, _buf, StunSeconds,
                                     Has(Crush) ? CrushAmp : 0f, CrushSeconds);
        if (Has(Crush)) Debug.Log($"[WeaponForm] 짓누름 — 받피증 {hits}체");
        FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale);
        if (Has(Aftershock)) _aftershockAt = Time.time + AftershockDelay;
        if (Has(Fissure)) { _fissureUntil = Time.time + FissureSeconds; _fissureNext = Time.time + FissureInterval; }
        SetStacks(0);
        Debug.Log($"[WeaponForm] 내려찍기 — 맞힘 {hits}");
        RaiseTriggered(Def.TriggerName);
    }
}
