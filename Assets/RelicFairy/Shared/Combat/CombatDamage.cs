using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// <b>주 피해(Primary) 단일 파이프라인.</b>
///
/// 판정 '형상'(콜라이더 / 코드 쿼리 / 투사체)은 호출자가 자유롭게 정하고,
/// 피해 '처리'는 전부 여기 한 곳을 통과한다. 예전엔 ColliderInstance·BasicArrow·스킬·유물이
/// 각자 다른 수준으로 처리해서, 스킬엔 크리티컬도 아이템 효과도 타격감도 안 붙는 문제가 있었다.
///
/// 처리 순서(원본 ColliderInstance.ApplyDamage 그대로):
///   ① 스킬 피해%(SkillDamageBonus) → ② 아이템 사전보정 → ③ 서약 출력변조
///   → ④ 아이템 저스트가드/섬광 → ⑤ 크리티컬 굴림 → ⑥ 방어무시(광기의 파동)
///   → ⑦ TakeDamage → ⑧ 타격감(RaiseHit) → ⑨ 아이템 사후(흡혈/독/빙결)
///   → ⑩ 아이템 형태변형 → ⑪ 패시브·서약 OnAttackHit → ⑫ 히트 VFX
///
/// <b>⚠️ 2차 피해(Secondary)는 여기로 오면 안 된다.</b>
/// ⑪에서 서약 OnAttackHit을 부르므로, 서약이 주는 피해를 여기로 넣으면 <b>무한 재귀</b>한다.
/// 서약 AoE·화상 DoT·시너지·처형은 <see cref="CombatQuery.DealSynergyDamage"/> /
/// MonsterBase.TakeSynergyDamage(경량 경로)를 계속 사용한다.
/// 이 규약이 깨져도 게임이 멈추지 않도록 <see cref="MaxDepth"/> 깊이 제한이 최후의 안전망으로 깔려 있다.
/// </summary>
public static class CombatDamage
{
    // hitEffectKey 미할당 시 사용할 공용 히트 VFX.
    private const string FallbackHitEffectKey = "HitEffect_02";

    /// <summary>
    /// Deal() 중첩 허용 깊이. 중첩 자체는 <b>정상</b>이다 — ⑪의 패시브/서약이 다시 주 피해를 내는
    /// 설계가 들어오면 깊이 2~3이 정상 동작이 된다. 막으려는 건 자기 자신으로 되돌아오는
    /// <b>순환</b>뿐이라, 실제 설계가 쓸 만한 깊이보다 넉넉히 잡고 그 위에서만 끊는다.
    /// </summary>
    private const int MaxDepth = 4;

    // 현재 Deal() 중첩 깊이. 전투는 메인 스레드 전용이라 static 카운터로 충분하다.
    private static int s_depth;
    private static bool s_depthWarned;   // 폭주 시 콘솔이 같은 경고로 뒤덮이지 않게 1회만 경고

    // 도메인 리로드 OFF: 2회차 진입 시 예외로 남은 깊이/경고 플래그가 잔류해 Deal이 통째로 막힌다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_depth = 0;
        s_depthWarned = false;
    }

    // 아이템 형태변형 추가타 질의 버퍼(재사용 — alloc 방지)
    private static readonly List<MonsterBase> s_shapeBuf = new();

    /// <summary>주 피해 1회 요청.</summary>
    public struct Request
    {
        /// <summary>피격 대상(IDamageable을 가진 오브젝트).</summary>
        public GameObject Target;
        /// <summary>보정 전 기본 피해.</summary>
        public float BaseDamage;
        /// <summary>공격자(플레이어 등).</summary>
        public GameObject Owner;
        /// <summary>액션 종류 — 근접/스킬 분기를 결정한다.</summary>
        public WeaponActionType ActionType;
        public float KnockbackMultiplier;

        /// <summary>
        /// 방어력 무시 비율(0~1). 0이면 평소대로 방어력이 감산된다.
        ///
        /// ⚠️ <b>다타 스킬에 필수다.</b> 방어력은 <b>감산</b>이라 타격마다 다시 빠진다
        /// (일반 몹 1~6, 보스 20). 총 피해를 20타로 쪼개면 방어력도 20번 빠져서
        /// 타당 피해가 방어력보다 작으면 전부 하한 1로 뭉개진다 — 스킬이 통째로 증발한다.
        /// 한 방 스킬은 0으로 두고, 잘게 쪼개는 스킬만 이 값을 쓴다.
        /// </summary>
        public float DefenseIgnore;

        /// <summary>타격 지점(타격감·히트 VFX). zero면 대상 위치로 대체.</summary>
        public Vector3 HitPoint;
        /// <summary>공격 발원지(타격 방향 폴백). zero면 Owner 위치 사용.</summary>
        public Vector3 SourcePosition;

        /// <summary>히트 VFX 키. 비우면 공용 폴백.</summary>
        public string HitEffectKey;
        /// <summary>히트 VFX 배율. 0 이하면 1.</summary>
        public float HitEffectScale;

        /// <summary>패시브 컨텍스트의 comboStep(콜라이더=attackId, 스킬=0).</summary>
        public int ComboStep;

        /// <summary>
        /// 원거리(투사체) 공격. ActionType이 GroundLight라도 <b>근접으로 취급하지 않는다</b> —
        /// 확정크릿/다음공격강화가 화살로 새는 것을 막고, 근접 전용 아이템 효과(방어무시·다단히트)도 제외한다.
        /// </summary>
        public bool IsRanged;

        /// <summary>히트 VFX를 이 파이프라인에서 스폰하지 않는다(호출자가 자체 VFX를 띄우는 경우).</summary>
        public bool SkipHitVfx;
    }

    public static bool IsMeleeAction(WeaponActionType a) =>
        a == WeaponActionType.GroundLight || a == WeaponActionType.GroundHeavy ||
        a == WeaponActionType.AirLight    || a == WeaponActionType.AirHeavy    ||
        a == WeaponActionType.AirPlunge;

    public static bool IsSkillAction(WeaponActionType a) =>
        a == WeaponActionType.QSkill || a == WeaponActionType.ESkill || a == WeaponActionType.RSkill;

    /// <summary>
    /// 주 피해를 적용한다. 반환값은 실제 적용된 최종 피해(0이면 무효/무피해).
    ///
    /// 깊이 제한만 담당하고 실제 처리는 <see cref="DealInternal"/>에 있다.
    /// 정상 중첩(깊이 1~<see cref="MaxDepth"/>)은 그대로 통과하고, 그 위로 쌓이는 순환만 끊는다.
    /// </summary>
    public static float Deal(in Request req)
    {
        if (s_depth >= MaxDepth)
        {
            if (!s_depthWarned)
            {
                s_depthWarned = true;
                Debug.LogWarning(
                    $"[CombatDamage] Deal() 중첩이 {MaxDepth}를 넘어 중단했다 — 주 피해가 자기 자신을 다시 부르는 순환이 생겼다. " +
                    "2차 피해(서약 AoE·DoT·시너지)는 CombatQuery.DealSynergyDamage / TakeSynergyDamage를 써야 한다.");
            }
            return 0f;
        }

        s_depth++;
        try     { return DealInternal(in req); }
        finally { s_depth--; }   // 중간에 예외가 나도 깊이가 새지 않게 보장
    }

    private static float DealInternal(in Request req)
    {
        var target = req.Target;
        var owner  = req.Owner;
        if (target == null) return 0f;
        if (!target.TryGetComponent<IDamageable>(out var damageable)) return 0f;

        var mgr        = GameRunBootstrapper.Instance?.Run?.EffectManager;
        var weaponData = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager?.CurrentWeaponData;

        var actionType = req.ActionType;

        // ① 스킬 피해 % — Q/E/R 스킬이고 플레이어 공격일 때만 기본 피해에 적용(SkillDamageBonus 소비처)
        float baseDamage = req.BaseDamage;
        if (owner != null
            && IsSkillAction(actionType)
            && owner.TryGetComponent<PlayerController>(out var skillOwner))
        {
            float skillBonus = skillOwner.RuntimeStats != null ? skillOwner.RuntimeStats.SkillDamageBonus : 0f;
            if (skillBonus != 0f) baseDamage *= 1f + skillBonus;
        }

        // ② 아이템 사전 보정
        // 원거리는 근접으로 치지 않는다 — 확정크릿/다음공격강화가 화살로 새면 안 된다.
        bool isMeleeAtk = !req.IsRanged && IsMeleeAction(actionType);
        var pkt = new DamagePacket(baseDamage, owner, target);
        mgr?.OnPreDealDamage(ref pkt, isMeleeAtk);

        float baseFinal = pkt.Negated ? 0f : pkt.FinalDamage;

        bool isPlayerAtk = owner != null && owner.TryGetComponent<PlayerController>(out _);

        // ③ 서약: 출력 피해 변형(아서 등). 플레이어 공격만, 크리티컬 전에 적용.
        if (baseFinal > 0f && isPlayerAtk)
        {
            var covH = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
            if (covH != null)
            {
                var cctx = new CombatContext
                {
                    Target     = target,
                    Damage     = baseFinal,
                    WeaponType = weaponData != null ? weaponData.weaponType : default,
                };
                baseFinal = covH.ModifyOutgoing(baseFinal, cctx);
            }
        }

        // ④ 아이템 타이밍형: 적 windup(예고) 중 적중 시 저스트가드(+피해)·섬광(공격 캔슬)
        var mods = ItemCombatMods.Current;
        MonsterBase tgtMb = isPlayerAtk ? target.GetComponentInParent<MonsterBase>() : null;
        if (tgtMb != null && tgtMb.IsTelegraphingAttack)
        {
            if (mods.justGuardBonus > 0f) baseFinal *= 1f + mods.justGuardBonus;
            if (mods.attackInterrupt)     tgtMb.CancelTelegraphedAttack();
        }

        // ⑤ 크리티컬 굴림 (확정크릿 소비는 근접 일반공격 한정)
        float finalDmg = CombatCalculator.RollCrit(weaponData, baseFinal, out bool isCrit, isMeleeAtk);
        pkt.IsCrit = isCrit;

        // ⑥ 아이템 광기의 파동: 무장된 일반(근접) 공격 1타를 실제 방어무시로 적용.
        bool penetrated = false;
        if (isPlayerAtk && tgtMb != null && finalDmg > 0f && isMeleeAtk)
        {
            var prs = GameRunBootstrapper.Instance?.Run?.Player?.RuntimeStats;
            if (prs != null && prs.ConsumePenetrateNextHit())
            {
                tgtMb.TakeSynergyDamage(finalDmg, owner, 1f, isCrit);   // 방어 완전 무시
                penetrated = true;
            }
        }

        // ⑥-b 다타 스킬 방어무시 — 감산 방어력이 타격마다 다시 빠지는 것을 막는다.
        //     아이템 관통과 같은 경로(TakeSynergyDamage)를 쓰므로 크리·아이템·패시브는 그대로 유효하다.
        if (!penetrated && tgtMb != null && finalDmg > 0f && req.DefenseIgnore > 0f)
        {
            tgtMb.TakeSynergyDamage(finalDmg, owner, Mathf.Clamp01(req.DefenseIgnore), isCrit, DamageKind.Normal);
            penetrated = true;
        }

        // ⑦ 실제 피해 적용
        if (!penetrated)
            damageable.TakeDamage(finalDmg, owner, req.KnockbackMultiplier, isCrit);

        // ⑧ 타격감 (HitFeedbackService 허브 경유 → 구독자 전파)
        Vector3 hitPoint = req.HitPoint.sqrMagnitude > 0.0001f ? req.HitPoint : target.transform.position;
        Vector3 origin   = req.SourcePosition.sqrMagnitude > 0.0001f
            ? req.SourcePosition
            : (owner != null ? owner.transform.position : target.transform.position);

        Vector3 attackDir = target.transform.position - origin;

        var hitInfo = new HitInfo(
            attacker:        owner,
            target:          target,
            hitPoint:        hitPoint,
            attackDirection: attackDir,
            damage:          finalDmg,
            isCritical:      isCrit,
            actionType:      actionType,
            weaponType:      weaponData != null ? weaponData.weaponType : WeaponType.None);

        HitFeedbackService.RaiseHit(hitInfo);

        // ⑨ 아이템 효과: 적중 후 (흡혈, 독, 빙결 등)
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker    = owner,
            Target      = target,
            IsCrit      = isCrit,
            HitPosition = hitPoint,
            ActionType  = actionType,
        };
        mgr?.OnPostDealDamage(report);

        // ⑩ 아이템 형태변형: 다단히트·원형 충격파·스킬 추가 투사체(2차 피해 경로 사용 → 재귀 없음)
        ApplyItemShapeExtras(target, owner, actionType, isMeleeAtk, finalDmg, in mods);

        // ⑪ 캐릭터 패시브 + 서약: 실제 적중 시점
        if (owner != null && owner.TryGetComponent<PlayerController>(out var ownerCtrl) && finalDmg > 0f)
        {
            ownerCtrl.FirePassive(PassiveTrigger.OnAttackHit, new PassiveContext
            {
                target     = target,
                damage     = finalDmg,
                comboStep  = req.ComboStep,
                weaponType = weaponData?.weaponType,
            });

            // 서약: 실제 적중 디스패치. 서약이 주는 2차 피해는 이 경로를 안 타므로 재귀 없음.
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnAttackHit(target, finalDmg);
        }

        // ⑫ 히트 VFX (호출자가 자체 VFX를 띄우면 스킵 — 이중 스폰 방지)
        if (!req.SkipHitVfx)
        {
            string fxKey   = !string.IsNullOrEmpty(req.HitEffectKey) ? req.HitEffectKey : FallbackHitEffectKey;
            float  fxScale = req.HitEffectScale > 0f ? req.HitEffectScale : 1f;
            SpawnHitEffectAsync(owner, hitPoint, fxKey, fxScale).Forget();
        }

        return finalDmg;
    }

    /// <summary>
    /// 아이템 형태변형 추가 판정 — 실제 오버랩/추가 적중으로 동작.
    ///  • 근접: meleeExtraHits회 같은 대상 추가타 + meleeCircle 원형 오버랩.
    ///  • 스킬: skillExtraProjectiles만큼 인근 적에 추가 적중(투사체 근사).
    /// 전부 2차 피해(DealSynergyDamage) 경로 → Deal()로 되돌아오지 않아 재귀 없음.
    /// </summary>
    private static void ApplyItemShapeExtras(GameObject target, GameObject owner,
                                             WeaponActionType actionType, bool isMeleeAtk,
                                             float finalDmg, in ItemCombatModifiers mods)
    {
        if (owner == null || target == null || finalDmg <= 0f) return;
        float ratio = mods.meleeExtraHitRatio > 0f ? mods.meleeExtraHitRatio : 0.8f;

        if (isMeleeAtk)   // 원거리는 근접 전용 효과(다단히트·원형충격파) 제외
        {
            for (int i = 0; i < mods.meleeExtraHits; i++)
                CombatQuery.DealSynergyDamage(target, finalDmg * ratio, owner, 0f, false);

            if (mods.meleeCircle && mods.meleeCircleRadius > 0f)
            {
                int n = CombatQuery.GetNearbyEnemies(owner.transform.position, mods.meleeCircleRadius,
                                                     target, 12, s_shapeBuf);
                for (int i = 0; i < n; i++)
                    CombatQuery.DealSynergyDamage(s_shapeBuf[i], finalDmg * ratio, owner, 0f, false);
            }
        }
        else if (IsSkillAction(actionType) && mods.skillExtraProjectiles > 0)
        {
            int n = CombatQuery.GetNearbyEnemies(target.transform.position, 5f,
                                                 target, mods.skillExtraProjectiles, s_shapeBuf);
            for (int i = 0; i < n; i++)
                CombatQuery.DealSynergyDamage(s_shapeBuf[i], finalDmg, owner, 0f, false);
        }
    }

    // 비동기 로드 도중 공격자가 파괴되면 후속 처리를 안전하게 취소(수명 토큰은 owner에 건다).
    private static async UniTaskVoid SpawnHitEffectAsync(GameObject owner, Vector3 hitPoint,
                                                         string fxKey, float scale)
    {
        GameObject effectObj;
        try
        {
            var spawn = Managers.ObjectPooler
                .SpawnAsync(fxKey, ObjectPoolerManager.PoolType.Effect, hitPoint, Quaternion.identity);

            effectObj = owner != null
                ? await spawn.AttachExternalCancellation(owner.GetCancellationTokenOnDestroy())
                : await spawn;
        }
        catch (OperationCanceledException) { return; }

        if (effectObj == null) return;

        if (effectObj.TryGetComponent<EffectBehaviour>(out var eb))
        {
            eb.Initialize(eb.behaviorSO, null, 1f);   // 자체 수명 관리 — 회수는 EffectBehaviour가 담당

            // ⚠️ 스케일은 Initialize <b>뒤에</b> 적용해야 한다.
            // Initialize가 behaviorSO.startScale로 localScale을 덮어쓰기 때문에,
            // 먼저 적용하면 hitEffectScale이 통째로 무시되고 SO 기본 크기로 나온다.
            effectObj.transform.localScale = Vector3.one * scale;
            return;
        }

        effectObj.transform.localScale = Vector3.one * scale;

        // EffectBehaviour가 없는 프리팹은 <b>스스로 사라지지 못한다</b> — 풀에 반환되지 않고 월드에 영원히 남는다.
        // 히트 이펙트 프리팹이 교체되면서 실제로 이 경로를 타는 이펙트가 필드에 계속 쌓였다.
        //
        // 수명은 인스턴스 자신에게 맡긴다(PooledVfxLifetime). 여기서 타이머를 들고 있으면
        // 그 인스턴스가 먼저 풀로 돌아갔다가 다른 히트로 재사용된 뒤 예전 타이머가 깨어나
        // 남의 이펙트를 꺼버린다 — 컴포넌트가 OnEnable에서 리셋하므로 그 문제가 생기지 않는다.
        if (!effectObj.TryGetComponent<PooledVfxLifetime>(out var life))
            life = effectObj.AddComponent<PooledVfxLifetime>();
        life.SetLife(ResolveVfxLifetime(effectObj));
    }

    /// <summary>히트 VFX 안전 수명 상한(초). 어떤 프리팹이든 이 시간 안에 회수된다.</summary>
    private const float MaxHitVfxLife = 3f;
    private const float MinHitVfxLife = 0.5f;

    /// <summary>파티클 길이에서 필요한 수명을 추정하고 상·하한으로 조인다.</summary>
    private static float ResolveVfxLifetime(GameObject go)
    {
        float longest = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            var main = ps.main;
            longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
        }
        return Mathf.Clamp(longest, MinHitVfxLife, MaxHitVfxLife);
    }
}
