using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 마르미아도아즈(대검) 형 「무게」 — 필살. 기획 장비개선 §4-3.
/// - 쌓기: 평타가 맞은 휘두름마다 +1(최대 4). 피격돼도 잃지 않고, 5초 동안 휘두르지 않으면 0.
/// - 발동: 가득이면 콤보 마지막 타가 「내려찍기」 — 반드시 치명타 + 반경 3.5 m 충격파(공격력 × TriggerAtkRatio — 데이터 0.6) + 일반 몹 경직 0.8초.
///   충격파는 주 피해 경로(CombatDamage · 발동 계수 2.5 = 강타)라 치명 굴림 · 적중 룬 · 서약 · 포이즈가 붙는다(10-14 A2 — 예전엔 2차 피해라 아무것도 안 붙었다).
/// - 확정 치명은 가득일 때 마지막 타 슬래시가 나오는 순간 건다(ArmForceCrit — 다음 근접 타 1회, OnSwing).
///   단계에 들어설 때 걸면 대검 3타(03_5)는 베기까지 약 1.6초라 그 사이 다른 피해가 먼저 썼다(10-07 실측 3 12회차).
///   판정 수명(0.5초) 동안 안 쓰이거나 다음 휘두름이 나오면 푼다. 마지막 타가 가득을 채운 순간에는 걸지 않는다.
///   다른 장치가 이미 건 확정 치명은 건드리지 않는다.
/// 변주: Ⅱ 균열 / 여진 · Ⅲ 무거운 손 / 짓누름(Task 4).
/// </summary>
public sealed class WeightForm : WeaponFormBase
{
    public const string Fissure = "fissure", Aftershock = "aftershock", HeavyHand = "heavy_hand", Crush = "crush";
    private const float StunSeconds = 0.8f, SlamForward = 1.5f;
    private const float ArmGrace = 0.5f;   // 콤보가 넘어간 뒤에도 마지막 타 슬래시 판정이 남는 시간(ColliderInstance 수명)

    // 10-07 예산 실측(단일 표적): 여진 100% · 짓누름 +20%로 Ⅲ +39%(목표 +25% 이내) → 여진 피해 절반 · 짓누름 +12% · 10-07 새 대검 모션(02_3 · 02_4 · 03_5)으로 Ⅲ +29% → 짓누름 +6%
    private const float AftershockDelay = 0.4f, AftershockScale = 1.4f, AftershockRatio = 0.5f,
                        FissureSeconds = 2f, FissureInterval = 0.5f, FissureRatio = 0.3f,
                        CrushAmp = 0.06f, CrushSeconds = 2f, FissureVfxScale = 0.3f;

    private const float SlamProcCoef = 2.5f;   // 무게 발동 = 강타(10-14 A2 · 기획 A1 표)

    private readonly List<MonsterBase> _buf = new(16);
    private float   _lastSwingAt = -999f;
    private bool    _armed;
    private float   _disarmAt = -1f;
    private float   _aftershockAt = -1f, _fissureUntil = -1f, _fissureNext;
    private Vector3 _slamAt;

    public WeightForm(PlayerController player, WeaponData weapon) : base(player, weapon) { }

    public override void OnMeleeHit(in HitInfo hit, bool newSwing, bool finisherStep)
    {
        if (!newSwing) return;
        _lastSwingAt = Time.time;
        if (finisherStep && IsFull)
        {
            if (!hit.IsCritical) CritFallback(hit);   // 확정 치명이 이 타에 안 실렸다 — 치명 몫을 더 준다(보장)
            Slam();
            return;
        }
        SetStacks(Stacks + 1);
    }

    /// <summary>
    /// 내려찍기를 낸 타가 치명이 아니었을 때 치명 몫(배율 − 1)을 더 준다. 확정 치명(ArmForceCrit)은 전역 1회 깃발이라
    /// 그 사이 다른 근접 피해가 먼저 쓸 수 있다(10-07 실측 3: 슬래시 순간 걸림 True인데 치명 1/2) — 「반드시 치명」을 결과로 보장한다.
    /// </summary>
    private void CritFallback(in HitInfo hit)
    {
        if (hit.Target == null || !hit.Target.TryGetComponent(out MonsterBase mb) || mb.IsDead) return;
        float mult  = Weapon.critDamage + Player.RuntimeStats.CritDamageBonus;
        float bonus = hit.Damage * Mathf.Max(0f, mult - 1f);
        if (bonus <= 0f) return;
        CombatQuery.DealSynergyDamage(mb, bonus, Player.gameObject, 1f, isCrit: true);
        Log($"내려찍기 치명 보정 — +{bonus:0}");
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
        Log($"무거운 손 — +{bonus:0}");
    }

    public override void Tick(float dt)
    {
        if (_aftershockAt > 0f && Time.time >= _aftershockAt)
        {
            _aftershockAt = -1f;
            int n = FormStrike.Radius(Player, _slamAt, Def.TriggerRange * AftershockScale, Atk * Def.TriggerAtkRatio * AftershockRatio, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale * AftershockScale);
            Log($"여진 — 맞힘 {n}");
        }
        if (_fissureUntil > 0f && Time.time >= _fissureNext)
        {
            _fissureNext += FissureInterval;
            int n = FormStrike.Radius(Player, _slamAt, Def.TriggerRange, Atk * FissureRatio, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale * FissureVfxScale);   // 갈라진 땅이 남아 있음을 보이게
            Log($"균열 — 맞힘 {n}");
            if (Time.time >= _fissureUntil) _fissureUntil = -1f;
        }
        if (!Holding) { _lastSwingAt += dt; return; }  // 원거리를 든 동안 쉬는 시간을 세지 않는다 · 확정 치명도 건드리지 않는다
        if (_armed && Time.time >= _disarmAt) Disarm();   // 마지막 타 슬래시 판정 수명 동안 안 쓰였다(헛침)
        if (Stacks > 0 && Time.time - _lastSwingAt > Def.IdleSeconds)
        {
            SetStacks(0, StackReason.Idle);
            if (_armed) Disarm();
        }
    }

    public override void OnSwing(int comboStep)
    {
        if (comboStep < Mathf.Max(1, Weapon.groundEndCount) - 1)
        {
            if (_armed) Disarm();   // 다음 휘두름 — 그 판정이 닿기 전에 풀어 다음 평타로 새지 않게
            return;
        }
        if (!IsFull || _armed || Player.RuntimeStats.ForceCritArmed) return;
        Player.RuntimeStats.ArmForceCrit(0f);   // 0 = 무기 치명 배율 — 이 슬래시의 첫 적중이 쓴다
        _armed = true;
        _disarmAt = Time.time + ArmGrace;
    }

    public override void Dispose()
    {
        if (_armed) Disarm();
    }

    private void Disarm()
    {
        Player.RuntimeStats.ConsumeForceCrit(out _);
        _armed = false;
        _disarmAt = -1f;
    }

    private void Slam()
    {
        _armed  = false;   // 확정 치명은 이 타가 이미 썼다
        _disarmAt = -1f;
        _slamAt = Player.transform.position + Player.AimForward * SlamForward;
        int hits = SlamHit(Atk * Def.TriggerAtkRatio);
        if (Has(Crush)) Log($"짓누름 — 받피증 {hits}체");
        FormStrike.PlayVfx(Def.TriggerVfxKey, _slamAt, Player.AimForward, Def.TriggerVfxScale);
        if (Has(Aftershock)) _aftershockAt = Time.time + AftershockDelay;
        if (Has(Fissure))
        {
            _fissureUntil = Time.time + FissureSeconds;
            _fissureNext  = Time.time + FissureInterval;
            FormStrike.GroundRing(_slamAt, Def.TriggerRange, FissureSeconds, Def.Tint);   // 판정 3.5 m인데 이펙트는 그 30%라 경계가 안 보였다(10-12 B9)
        }
        SetStacks(0, StackReason.Trigger);
        Log($"내려찍기 — 맞힘 {hits}");
        RaiseTriggered(Def.TriggerName);
    }

    /// <summary>
    /// 내려찍기 충격파 — 반경 안 적마다 주 피해(공격 종류 GroundHeavy · 발동 계수 2.5). GroundHeavy는 형 디스패치(평타 · E · R) 밖이라
    /// 이 적중이 다시 OnMeleeHit로 들어와 내려찍기를 부르지 않는다. 경직 · 짓누름 받피증은 예전 그대로(보스 제외).
    /// </summary>
    private int SlamHit(float damage)
    {
        int n = CombatQuery.GetNearbyEnemies(_slamAt, Def.TriggerRange, Player.gameObject, 32, _buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = _buf[i];
            if (e == null || e.IsDead) continue;
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = e.gameObject,
                BaseDamage          = damage,
                Owner               = Player.gameObject,
                ActionType          = WeaponActionType.GroundHeavy,
                ProcCoef            = SlamProcCoef,
                KnockbackMultiplier = 0.5f,
                HitPoint            = e.transform.position + Vector3.up,
                SourcePosition      = _slamAt,
                SkipHitVfx          = true,   // 내려찍기 이펙트가 이미 판정 반경을 덮는다
            });
            hits++;
            if (e.IsDead || e.Grade == MonsterGrade.Boss) continue;   // 보스는 경직 없이 피해만
            e.ApplyStun(StunSeconds);
            if (Has(Crush)) e.ApplyDamageTakenAmp(CrushAmp, CrushSeconds, "form_crush");
        }
        return hits;
    }
}
