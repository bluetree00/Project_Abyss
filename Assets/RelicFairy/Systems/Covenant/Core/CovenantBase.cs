using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

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

    /// <summary>서약 카테고리(기획서 4분류). 선택 UI/메타데이터용 — 구현체가 override.</summary>
    public virtual CovenantCategory Category => CovenantCategory.ActionConditional;

    // ── 표시 데이터 (UI용) ──────────────────────────────
    public virtual string DisplayName         => CovenantId;
    public virtual string LoreText            => string.Empty;
    public virtual string BasicDescription    => string.Empty;
    public virtual string EnhancedDescription => string.Empty;
    public virtual string EvolvedDescription  => string.Empty;

    /// <summary>발동 조건(원인). 조립 서약만 채운다 — HUD가 원인/결과를 줄 나눠 표시하는 데 쓴다.</summary>
    public virtual string CauseText  => null;
    /// <summary>발동 결과(효과). <see cref="CauseText"/>와 짝. 둘 다 있어야 분리 표시된다.</summary>
    public virtual string EffectText => null;
    public virtual UnityEngine.Sprite Icon    => null;

    protected CovenantContext Ctx  { get; private set; }

    // ── 생명주기 ────────────────────────────────────────
    public virtual void Initialize(CovenantContext ctx)
    {
        Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    public virtual void Dispose() { }

    // ── 버프창 표시(옵트인) ──────────────────────────────
    /// <summary>
    /// 현재 "발동/지속 상태"를 버프창 항목으로 노출할지. 기본=노출 안 함.
    /// 보유 서약 자체는 전용 <see cref="CovenantPanelView"/>에 이미 상시 표시되므로 버프창 중복을 피한다.
    /// 일시적 발동/지속 상태(예: 일정시간 강화·게이지)가 있는 서약만 override해 true + <see cref="BuffViewItem"/> 반환.
    /// (관례는 유물과 동일 — 상시 보유는 패널, 일시 상태만 버프창.) 읽기 전용 — 동작/밸런스 무변경.
    /// </summary>
    public virtual bool TryGetBuffView(out BuffViewItem item) { item = default; return false; }

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
    public virtual void OnWeaponSwap(WeaponData prev, WeaponData next) { }
    public virtual void Tick(float deltaTime)                      { }

    // ── ICovenantDamagePipeline ─────────────────────────
    public virtual void ModifyOutgoingDamage(ref float damage, CombatContext ctx) { }
    public virtual void ModifyIncomingDamage(ref float damage, CombatContext ctx) { }
    public virtual bool TryPreventDeath() => false;

    /// <summary>
    /// 치명타 산출 오버라이드(갤러해드). true 반환 시 CombatCalculator.RollCrit이 일반 굴림을 대체한다.
    /// forceCrit=true면 확정 치명타, 아니면 치명타 억제 + 최소피해 하한(최대피해×minFloorRatio).
    /// </summary>
    public virtual bool TryProvideCritOverride(WeaponData weapon, out bool forceCrit, out float minFloorRatio)
    { forceCrit = false; minFloorRatio = 0f; return false; }

    // ── ICovenantMechanicModifier ───────────────────────
    public virtual void OnBoundToPlayer(PlayerController player)                         { }
    public virtual void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx)   { }
    public virtual bool OverrideSkillCost(SkillType skill, ref SkillCostContext ctx)     => false;

    // ── 효과 헬퍼 (PR-C0) ────────────────────────────────
    // 단계 차등 규약: 수치는 V(idx)/VI(idx)로 스테이지별 자동 스케일(Basic/Enhanced/Evolved 배열).
    //                신능력은 `Stage >= CovenantStage.Enhanced/Evolved` 게이트로 코드에서 분기.
    // 액티브 효과는 아래 헬퍼로 "무엇을/언제"만 작성 — AOE/VFX/소환 "어떻게"는 재사용.

    /// <summary>플레이어 현재 위치(없으면 원점).</summary>
    protected Vector3 PlayerPos => Ctx?.Player != null ? Ctx.Player.transform.position : Vector3.zero;

    /// <summary>스탯 레이어 즉시 재적용(조건/버프 변경 후). null-safe.</summary>
    protected void RefreshStats()
    {
        if (Ctx?.Stats != null && Ctx.Session?.CovenantHandler != null)
            Ctx.Stats.RefreshCovenants(Ctx.Session.CovenantHandler);
    }

    /// <summary>
    /// 반경 내 적에게 플레이어 공격력 기반 피해(multiplier=V(idx) 배수). 플레이어 자신 제외. 반환=피격 수.
    /// (OnSkillEffects의 AOE 패턴을 공통화 — instigator=player라 IsPlayerInstigator/서약 OnKill과 정합)
    /// </summary>
    protected int DealAoe(Vector3 center, float radius, float multiplier, float knockback = 0.3f)
    {
        if (Ctx?.Player == null || Ctx.Stats == null) return 0;

        // [가이드라인 비주얼] 서약 광역 발동 표시(통지만)
        GuidelineVisual.AoeBurst(center, radius, GuidelineVisual.ToastKind.Covenant);
        GuidelineVisual.Toast(center + Vector3.up * 1.6f, DisplayName, GuidelineVisual.ToastKind.Covenant);

        var weaponData = Ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        float dmg = DamageFormula.Calculate(multiplier, Ctx.Stats.GetEffectiveAttack(kind));

        int hits = 0;
        var cols = Physics.OverlapSphere(center, radius);
        foreach (var col in cols)
        {
            if (col.gameObject == Ctx.Player.gameObject) continue;
            if (col.TryGetComponent<IDamageable>(out var d))
            {
                d.TakeDamage(dmg, Ctx.Player.gameObject, knockback);
                hits++;
            }
        }
        return hits;
    }

    /// <summary>일회성 VFX(ObjectPooler Effect 풀, Addressable 키). 자산 없으면 무동작.</summary>
    protected static void Vfx(string key, Vector3 pos, float scale = 1f)
    {
        GuidelineVisual.Toast(pos + Vector3.up * 1.2f, key, GuidelineVisual.ToastKind.Covenant);   // [가이드라인 비주얼]
        ItemEffectVfxHelper.SpawnOneShotAt(key, pos, scale);
    }

    /// <summary>대상에 부착되는 지속 VFX(duration초 후 제거).</summary>
    protected static void VfxLoop(string key, Transform parent, float duration, float scale = 1f)
        => ItemEffectVfxHelper.AttachLoopVfx(key, parent, duration, scale).Forget();

    /// <summary>
    /// 수명 있는 풀 액터(장판/오라/소환물 공통) 스폰 — duration초 후 자동 Despawn + onExpire 콜백.
    /// 공격 AI를 가진 아군 소환물(예: Solomon 유령)은 별도 프리팹+행동 컴포넌트가 필요(PR-C1).
    /// </summary>
    protected static void SpawnTimedActor(string key, Vector3 pos, float duration, Action onExpire = null)
        => SpawnTimedActorAsync(key, pos, duration, onExpire).Forget();

    private static async UniTaskVoid SpawnTimedActorAsync(string key, Vector3 pos, float duration, Action onExpire)
    {
        GuidelineVisual.Toast(pos + Vector3.up * 1.4f, key, GuidelineVisual.ToastKind.Covenant);   // [가이드라인 비주얼]
        if (string.IsNullOrEmpty(key) || Managers.ObjectPooler == null) return;

        var go = await Managers.ObjectPooler.SpawnAsync(
            key, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.identity);
        if (go == null) return;

        if (duration > 0f)
        {
            await UniTask.Delay((int)(duration * 1000));
            if (go != null) Managers.ObjectPooler.Despawn(go);
        }
        onExpire?.Invoke();
    }
}
