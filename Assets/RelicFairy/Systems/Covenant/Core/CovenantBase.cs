using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 서약 구현체의 추상 기반 클래스.
/// 4개 인터페이스를 모두 구현하되 기본값은 무동작(no-op)이다.
/// 구현체는 필요한 메서드만 override한다.
/// </summary>
public abstract class CovenantBase
    : ICovenantStatProvider,
      ICovenantEventListener,
      ICovenantDamagePipeline,
      ICovenantMechanicModifier
{
    // ── 아이덴티티 ─────────────────────────────────────
    public abstract string CovenantId { get; }
    public CovenantStage Stage { get; set; } = CovenantStage.Basic;

    // ── 표시 데이터 (UI용) ──────────────────────────────
    public virtual string DisplayName         => CovenantId;
    public virtual string LoreText            => string.Empty;
    public virtual string BasicDescription    => string.Empty;
    public virtual string EnhancedDescription => string.Empty;
    public virtual string EvolvedDescription  => string.Empty;
    public virtual UnityEngine.Sprite Icon    => null;

    protected CovenantContext Ctx  { get; private set; }
    public    CovenantDataSO  Data { get; private set; }

    // ── 생명주기 ────────────────────────────────────────
    public virtual void Initialize(CovenantContext ctx)
    {
        Ctx  = ctx ?? throw new ArgumentNullException(nameof(ctx));
        Data = ctx.DataTable?.Get(CovenantId);
    }

    /// <summary>
    /// 스테이지별 float 수치 반환.
    /// 우선순위: 서버(CovenantDataManager) → SO(CovenantDataSO) → fallback.
    /// </summary>
    protected float V(int index, float fallback = 0f)
    {
        var serverMgr = Managers.CovenantData;
        if (serverMgr != null && serverMgr.IsInitialized && serverMgr.HasData(CovenantId))
            return serverMgr.Get(CovenantId, Stage, index, fallback);

        return Data != null ? Data.Get(Stage, index, fallback) : fallback;
    }

    /// <summary>
    /// 스테이지별 int 수치 반환.
    /// 우선순위: 서버(CovenantDataManager) → SO(CovenantDataSO) → fallback.
    /// </summary>
    protected int VI(int index, int fallback = 0)
    {
        var serverMgr = Managers.CovenantData;
        if (serverMgr != null && serverMgr.IsInitialized && serverMgr.HasData(CovenantId))
            return serverMgr.GetInt(CovenantId, Stage, index, fallback);

        return Data != null ? Data.GetInt(Stage, index, fallback) : fallback;
    }

    public virtual void Dispose() { }

    // ── ICovenantStatProvider ───────────────────────────
    public virtual IEnumerable<StatModifier> GetStatModifiers()
        => Array.Empty<StatModifier>();

    // ── ICovenantEventListener ──────────────────────────
    public virtual void OnRoomEnter()                              { }
    public virtual void OnRoomClear()                              { }
    public virtual void OnKill(GameObject target)                  { }
    public virtual void OnAttackHit(GameObject target, float dmg)  { }
    public virtual void OnTakeDamage(float damage)                 { }
    public virtual void OnSkillUse(SkillType skill)                { }
    public virtual void Tick(float deltaTime)                      { }

    // ── ICovenantDamagePipeline ─────────────────────────
    public virtual void ModifyOutgoingDamage(ref float damage, CombatContext ctx) { }
    public virtual void ModifyIncomingDamage(ref float damage, CombatContext ctx) { }
    public virtual bool TryPreventDeath() => false;

    // ── ICovenantMechanicModifier ───────────────────────
    public virtual void OnBoundToPlayer(PlayerController player)                         { }
    public virtual void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx)   { }
    public virtual bool OverrideSkillCost(SkillType skill, ref SkillCostContext ctx)     => false;
}
