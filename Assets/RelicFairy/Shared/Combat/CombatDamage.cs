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

    // 검류 피격(SwordHitImpact = 피)에 겹치는 「검이 닿는」 짧은 흰 섬광(10-19 ⑫ 「블러드 피격은 좋은데 검 자체의 심플한 타격 이펙트가 필요」).
    // Travis Hit_02(장비 형 실측 73) — 무형검 평타 피 ×0.29에서 ×0.56(반경 0.45 m). 피 배율을 그대로 곱하면 대검(×0.67~1.26)은 1~2 m 섬광이라 제곱근으로 키운다.
    private const string SwordBloodKey       = "SwordHitImpact";
    private const string SwordStrikeKey      = "SwordStrikeFlash";
    private const float  SwordStrikeScale    = 0.56f;
    private const float  SwordStrikeBloodRef = 0.29f;

    /// <summary>
    /// Deal() 중첩 허용 깊이. 중첩 자체는 <b>정상</b>이다 — ⑪의 패시브/서약이 다시 주 피해를 내는
    /// 설계가 들어오면 깊이 2~3이 정상 동작이 된다. 막으려는 건 자기 자신으로 되돌아오는
    /// <b>순환</b>뿐이라, 실제 설계가 쓸 만한 깊이보다 넉넉히 잡고 그 위에서만 끊는다.
    /// </summary>
    private const int MaxDepth = 4;

    // 현재 Deal() 중첩 깊이. 전투는 메인 스레드 전용이라 static 카운터로 충분하다.
    private static int s_depth;
    private static bool s_depthWarned;   // 폭주 시 콘솔이 같은 경고로 뒤덮이지 않게 1회만 경고
    private static float s_splitCarry;   // 분열 효과 몫 — 무게를 쌓아 1이 찰 때마다 효과 한 번(⑨~⑪)

    // 도메인 리로드 OFF: 2회차 진입 시 예외로 남은 깊이/경고 플래그가 잔류해 Deal이 통째로 막힌다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_depth = 0;
        s_depthWarned = false;
        s_splitCarry = 0f;
        s_hierarchyScaled.Clear();
        s_hitFxLife.Clear();
    }

    // 아이템 형태변형 추가타 질의 버퍼(재사용 — alloc 방지)
    private static readonly List<MonsterBase> s_shapeBuf = new();
    // 맞음 이펙트 — 자식 입자 배율을 맞춘 풀 인스턴스 · 키별 안전 수명(타격마다 자식 입자를 두 번 훑었다 · 10-29 풀 점검 5)
    private static readonly HashSet<int> s_hierarchyScaled = new();
    private static readonly Dictionary<string, float> s_hitFxLife = new();

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
        /// <summary>스킬의 마무리 일격. 타격 연출 단계(막타)만 바꾼다 — 피해 계산과는 무관(09-25).</summary>
        public bool IsFinisher;

        /// <summary>
        /// 발동 계수(10-14 빌드 시너지 A1) — 확률 효과 · 상태 지속 · 포이즈 · 강타 판정의 무게. 0이면 무기 × 공격 종류 표
        /// (<see cref="ProcCoefficient.Of"/>). 한 방(태양 낙하 · 무게 내려찍기)이나 연타 조각(심판 연타)처럼 표와 다른 것만 정한다.
        /// </summary>
        public float ProcCoef;

        /// <summary>
        /// 분열 무게(10-14 사용자 「분열은 룬 타격 · 시너지 효과가 화살마다 붙어 너무 세다 — 마지막 계산식에 반감」). 0 = 1(한 발 · 근접).
        /// 갈래 화살 = <see cref="CombatSpawner.SplitExtraWeight"/>(2발 50% · 3발 45% …) — 방어를 뺀 최종 피해 · 포이즈가 이만큼이고, 적중 효과(⑨~⑪)는 이 비율로만 붙는다.
        /// </summary>
        public float SplitWeight;

        /// <summary>맞은 적을 멈추게(경직) · 밀어내지(넉백) 않는다 — 피해만(화살, 10-15 사용자 「활 같은 공격은 경직 · 넉백이 없어야」). 정예 포이즈는 깎되 안 깨진다.</summary>
        public bool NoFlinch;

        /// <summary>
        /// 적중 효과(⑨~⑪ — 룬 · 아이템 · 유물 조각 · 패시브 · 서약)를 내지 않는다 — 피해만. 화살 한 발이 효과 상한(<see cref="BasicArrow"/>)을 넘긴 적중(10-15).
        /// </summary>
        public bool NoEffects;

        /// <summary>
        /// 타격감(히트스톱 · 흔들림 · 화면 펄스 — GlobalFeelCoalescer)을 내지 않는다. 피격자 반응 · 적중 효과는 그대로.
        /// 장판 틱처럼 잦은 주 피해용(가웨인 작열 지대 · 낙하 — 10-16 유물 Q 계수).
        /// </summary>
        public bool NoHitFeel;
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
        try
        {
            // 결과창 피해 비중(10-07) — 액션 종류로 출처를 건다(평타 · 스킬 E·R · 유물 Q).
            using (DamageSourceScope.Enter(DamageSourceScope.FromAction(req.ActionType)))
                return DealInternal(in req);
        }
        finally { s_depth--; }   // 중간에 예외가 나도 깊이가 새지 않게 보장
    }

    private static float DealInternal(in Request req)
    {
        var target = req.Target;
        var owner  = req.Owner;
        if (target == null) return 0f;
        // 중계 물체(공명 성흔석) — 맞은 자리는 그대로, 피해 · 효과는 넘겨받는 대상(기사)에게. 한 번만 바꾼다(10-22 A1b).
        float relayRatio = 1f;
        bool  relayed    = false;
        if (target.TryGetComponent<IDamageRelay>(out var relay))
        {
            var to = relay.RelayTarget;
            if (to == null || !relay.TryAcceptHit()) return 0f;   // 식음 — 뒤 단계(아이템 · 서약)도 돌지 않는다
            target     = to;
            relayRatio = relay.RelayRatio;
            relayed    = true;
        }
        if (!target.TryGetComponent<IDamageable>(out var damageable)) return 0f;

        var mgr        = GameRunBootstrapper.Instance?.Run?.EffectManager;
        var weaponData = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager?.CurrentWeaponData;

        var actionType = req.ActionType;

        // ① 스킬 피해 % — Q/E/R 스킬이고 플레이어 공격일 때만 기본 피해에 적용(SkillDamageBonus 소비처)
        float baseDamage = req.BaseDamage * relayRatio;
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

        // 발동 계수 — 비어 있으면 무기 × 공격 종류 표. 플레이어가 아닌 공격(시험 · 몬스터)은 1.
        float procCoef = req.ProcCoef > 0f ? req.ProcCoef
                       : isPlayerAtk ? ProcCoefficient.Of(weaponData != null ? weaponData.weaponType : WeaponType.None, actionType, req.IsRanged)
                       : 1f;
        if (isPlayerAtk && ProcCoefficient.ConsumeHeavyNext()) procCoef = Mathf.Max(procCoef, ProcCoefficient.HeavyThreshold());   // 예약된 강타(덮치는 그림자)
        bool isHeavy = isPlayerAtk && ProcCoefficient.IsHeavy(procCoef);

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

        // ⑤-b 분열 무게 — 크리티컬까지 끝난 피해의 「방어를 뺀 몫」에 곱한다(방어는 감산이라 빼기 전에 곱하면 1로 뭉개진다).
        float splitW = req.SplitWeight > 0f && req.SplitWeight < 1f ? req.SplitWeight : 1f;
        if (splitW < 1f)
        {
            float def = tgtMb != null ? tgtMb.CurrentDefense : 0f;
            finalDmg = finalDmg > def ? def + (finalDmg - def) * splitW : finalDmg;
        }

        // ⑤-c 원거리 무기 → Ch2 ~ 4 보스 절반(10-29 사용자 D1) — 무기 피해만(평타 · E · R · 원거리 파츠 폭발).
        //      유물 Q(행동 Q) · 룬 시너지 · 서약 · 지속 피해는 이 길을 안 타거나 행동이 달라 그대로. 중간 보스(정예)는 빼다(47 제안 · 일반 방 흐름의 짧은 싸움)
        if (isPlayerAtk && tgtMb != null && finalDmg > 0f && IsBossRangedHalf(tgtMb, weaponData, actionType))
            finalDmg *= BossRangedMult;

        // ⑥ 아이템 광기의 파동: 무장된 일반(근접) 공격 1타를 실제 방어무시로 적용.
        bool penetrated = false;
        if (isPlayerAtk && tgtMb != null && finalDmg > 0f && isMeleeAtk)
        {
            var prs = GameRunBootstrapper.Instance?.Run?.Player?.RuntimeStats;
            if (prs != null && prs.ConsumePenetrateNextHit())
            {
                // 방어 완전 무시. 경로만 관통이고 실체는 근접 평타(주 피해)라 kind는 Normal —
                // ⑥-b와 같은 규약이다. 기본값(Synergy)으로 두면 데미지 숫자 색이 시너지로 나오고,
                // 이 타격으로 죽인 몹이 막타 히트스톱을 잃는다(DieState는 Normal 킬에만 건다).
                tgtMb.TakeSynergyDamage(finalDmg, owner, 1f, isCrit, DamageKind.Normal);
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

        // ⑦ 실제 피해 적용 — 정예 포이즈를 발동 계수만큼 깎는다(10-14 A2 · 예전 「1타 = 1 · 스킬 2.5」). 정적 값이라 호출 직후 되돌린다.
        if (!penetrated)
        {
            MonsterPoise.PendingHitWeight = procCoef * splitW;
            MonsterPoise.PendingNoFlinch  = req.NoFlinch;
            try { damageable.TakeDamage(finalDmg, owner, req.NoFlinch || relayed ? 0f : req.KnockbackMultiplier, isCrit); }
            finally { MonsterPoise.PendingHitWeight = MonsterPoise.RestingWeight; MonsterPoise.PendingNoFlinch = false; }
        }

        // ⑧ 타격감 (HitFeedbackService 허브 경유 → 구독자 전파)
        Vector3 hitPoint = req.HitPoint.sqrMagnitude > 0.0001f ? req.HitPoint : target.transform.position;
        // 중계면 타격 이펙트(⑫)만 맞은 자리(성흔석) · 숫자 · 적중 효과 기준점은 넘겨받은 몸 — 룬 광역 · 체인이 유리 너머 기사에 닿게(10-22 A1b)
        Vector3 fxPoint  = hitPoint;
        if (relayed) hitPoint = target.TryGetComponent<Collider>(out var relayBody) ? relayBody.ClosestPoint(hitPoint) : target.transform.position;
        Vector3 origin   = req.SourcePosition.sqrMagnitude > 0.0001f
            ? req.SourcePosition
            : (owner != null ? owner.transform.position : target.transform.position);

        Vector3 attackDir = target.transform.position - origin;

        // 무적 대상 — 피해가 무시된 타격에 「맞은」 반응(빨간 깜빡임·히트스톱·화면 플래시)을 주지 않는다.
        // 대신 막힘 표시 기회를 주고 빠진다(무적인지 모른 채 계속 때리던 문제).
        if (tgtMb != null && tgtMb.IsDamageImmuneNow)
        {
            tgtMb.NotifyBlockedHit(hitPoint);
            // 적중 효과에 보고하지 않는다 — 막힌 타격이 「N번마다」 · 표식 카운터를 올려 무적 보스에 쏘면 쌓였다(10-15 점검)
            return 0f;   // 피해 0 — 막혔다
        }

        var hitInfo = new HitInfo(
            attacker:        owner,
            target:          target,
            hitPoint:        hitPoint,
            attackDirection: attackDir,
            damage:          finalDmg,
            isCritical:      isCrit,
            actionType:      actionType,
            weaponType:      weaponData != null ? weaponData.weaponType : WeaponType.None,
            isFinisher:      req.IsFinisher);

        if (req.NoHitFeel) HitFeedbackService.RaiseHitQuiet(hitInfo);
        else               HitFeedbackService.RaiseHit(hitInfo);

        // ⑨ 아이템 효과: 적중 후 (흡혈, 독, 빙결 등)
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker    = owner,
            Target      = target,
            IsCrit      = isCrit,
            HitPosition = hitPoint,
            ActionType  = actionType,
            IsRanged    = req.IsRanged,
            ProcCoef    = procCoef,
            IsHeavy     = isHeavy && finalDmg > 0f,
        };
        // 분열 갈래는 적중 효과(⑨~⑪)가 무게만큼만 붙는다 — 50%면 갈래 두 발에 한 번. 가운데 화살 · 근접은 늘.
        bool effects = !req.NoEffects && SplitEffectTurn(splitW);
        if (effects) mgr?.OnPostDealDamage(report);

        // ⑩ 아이템 형태변형: 다단히트·원형 충격파·스킬 추가 투사체(2차 피해 경로 사용 → 재귀 없음)
        if (effects) ApplyItemShapeExtras(target, owner, actionType, isMeleeAtk, finalDmg, in mods);

        // ⑪ 캐릭터 패시브 + 서약: 실제 적중 시점
        if (effects && owner != null && owner.TryGetComponent<PlayerController>(out var ownerCtrl) && finalDmg > 0f)
        {
            ownerCtrl.FirePassive(PassiveTrigger.OnAttackHit, new PassiveContext
            {
                target     = target,
                damage     = finalDmg,
                comboStep  = req.ComboStep,
                weaponType = weaponData?.weaponType,
            });

            // 서약: 실제 적중 디스패치. 서약이 주는 2차 피해는 이 경로를 안 타므로 재귀 없음.
            // 원거리 적중이면 디스패치 동안만 표시 — 사격 원인 「겨눔」이 센다(10-14 A5)
            CovenantCauseTracker.CurrentHitRanged = req.IsRanged;
            try { GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnAttackHit(target, finalDmg); }
            finally { CovenantCauseTracker.CurrentHitRanged = false; }
        }

        // ⑫ 히트 VFX (호출자가 자체 VFX를 띄우면 스킵 — 이중 스폰 방지)
        if (!req.SkipHitVfx)
        {
            string fxKey   = !string.IsNullOrEmpty(req.HitEffectKey) ? req.HitEffectKey : FallbackHitEffectKey;
            float  fxScale = req.HitEffectScale > 0f ? req.HitEffectScale : 1f;
            SpawnHitEffectAsync(owner, fxPoint, fxKey, fxScale).Forget();
            if (fxKey == SwordBloodKey)
                SpawnHitEffectAsync(owner, fxPoint, SwordStrikeKey, SwordStrikeScale * Mathf.Sqrt(fxScale / SwordStrikeBloodRef)).Forget();
        }

        return finalDmg;
    }

    /// <summary>분열 화살이 적중 효과를 낼 차례인가 — 무게를 쌓아 1이 찰 때마다 한 번. 무게 1(한 발 · 근접)은 늘 낸다.</summary>
    private static bool SplitEffectTurn(float weight)
    {
        if (weight >= 1f) return true;
        s_splitCarry += weight;
        if (s_splitCarry < 0.999f) return false;
        s_splitCarry -= 1f;
        return true;
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

    /// <summary>
    /// 맞힘 이펙트 배율이 자식 입자까지 듣게 한다(10-11 이펙트 톤 점검). SwordHitImpact · KatanaSlashHit 등 팩 프리팹 입자가
    /// scalingMode Local이라 localScale(hitEffectScale)이 맨 위 입자에만 걸렸다 — 무형검 0.29에도 붉은 리본이 2 m 넘게 퍼졌다.
    /// 몬스터 이펙트(PatternAttackOverrideSO.ApplyVfxHierarchyScaling)와 같은 처리. 풀 인스턴스라 한 번 바꾸면 그대로 남는다.
    /// </summary>
    private static void ScaleWholeHierarchy(GameObject effectObj)
    {
        if (!s_hierarchyScaled.Add(effectObj.GetInstanceID())) return;   // 이 인스턴스는 이미 바꿨다
        foreach (var ps in effectObj.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            if (main.scalingMode != ParticleSystemScalingMode.Hierarchy) main.scalingMode = ParticleSystemScalingMode.Hierarchy;
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
            ScaleWholeHierarchy(effectObj);
            return;
        }

        effectObj.transform.localScale = Vector3.one * scale;
        ScaleWholeHierarchy(effectObj);

        // EffectBehaviour가 없는 프리팹은 <b>스스로 사라지지 못한다</b> — 풀에 반환되지 않고 월드에 영원히 남는다.
        // 히트 이펙트 프리팹이 교체되면서 실제로 이 경로를 타는 이펙트가 필드에 계속 쌓였다.
        //
        // 수명은 인스턴스 자신에게 맡긴다(PooledVfxLifetime). 여기서 타이머를 들고 있으면
        // 그 인스턴스가 먼저 풀로 돌아갔다가 다른 히트로 재사용된 뒤 예전 타이머가 깨어나
        // 남의 이펙트를 꺼버린다 — 컴포넌트가 OnEnable에서 리셋하므로 그 문제가 생기지 않는다.
        if (!effectObj.TryGetComponent<PooledVfxLifetime>(out var life))
            life = effectObj.AddComponent<PooledVfxLifetime>();
        if (!s_hitFxLife.TryGetValue(fxKey, out float seconds)) s_hitFxLife[fxKey] = seconds = ResolveVfxLifetime(effectObj);   // 파티클 길이는 프리팹마다 같다 — 한 번만 잰다
        life.SetLife(seconds);
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

    // ── 원거리 보스 반감(10-29 사용자 D1) ──
    public const float BossRangedMult = 0.5f;
#if UNITY_EDITOR
    /// <summary>실측 — 켜면 반감을 끈다(같은 판에서 반감 전후 피해 비교).</summary>
    public static bool ProbeNoBossRangedHalf;
#endif

    /// <summary>원거리 무기(활 · 석궁)의 무기 피해(유물 Q 빼고)가 Ch2 ~ 4 보스를 맞혔나.</summary>
    private static bool IsBossRangedHalf(MonsterBase target, WeaponData weapon, WeaponActionType action)
    {
#if UNITY_EDITOR
        if (ProbeNoBossRangedHalf) return false;
#endif
        if (target.Grade != MonsterGrade.Boss || weapon == null || action == WeaponActionType.QSkill) return false;
        if (weapon.weaponType.GetAttackStatKind() != AttackStatKind.Ranged) return false;
        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        return run != null && (int)run.CurrentChapter >= (int)ChapterId.Chapter2;
    }
}
