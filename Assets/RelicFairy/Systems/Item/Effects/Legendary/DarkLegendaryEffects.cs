using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 어둠 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 심연 잠식 = 위기 · 그림자 분신 = 기동 · 어둠의 낫 = 처치. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── D-1. 보복의 심연 (위기 · 옛 심연 잠식) ───────────────────────────────
/// <summary>
/// 10-24 개편 — 받은 피해의 value2(60%)를 「어둠」으로 모은다(체력 50% 아래면 ×2 · 상한 = 최대 체력 × value3 = 30%).
/// 어둠이 있으면 다음 적중이 전부 실어 보낸다 — 그 적과 반경 2.5 m 적에게 모인 양 × value(3) + 0.3 × 존 단계. 회복은 없다(되갚기).
/// 각성(어둠 존 4단계) = 보낼 때 그 적 받는 피해 +20% 4초. HUD = LegendaryHud "dark".
/// </summary>
public sealed class DarkLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const float ReleaseRadius = 2.5f, LowHp = 0.5f, AwakeAmp = 0.2f, AwakeSeconds = 4f, AuraGap = 0.5f, MultPerTier = 0.3f;
    private const string HudKey = "dark";

    private readonly List<GameObject> _buf = new();
    private float _stored, _auraAt;

    /// <summary>새 행(발동 「OnHurtStore」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHurtStore";

    public DarkLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _stored = 0f;
        Publish(ctx);
    }

    public override void OnDeactivate() => LegendaryHud.Clear(HudKey);

    private float Cap(ItemEffectContext ctx) => ctx?.Stats != null ? ctx.Stats.MaxHp * LegendaryTuning.Pick(NewRow, _value3, 0.3f) : 0f;

    private void Publish(ItemEffectContext ctx) => LegendaryHud.Set(HudKey, Mathf.RoundToInt(_stored), Mathf.RoundToInt(Cap(ctx)));

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || ctx?.Stats == null) return;
        ctx.RefreshHpRatio();
        float rate = LegendaryTuning.Pick(NewRow, _value2, 0.6f) * (ctx.HpRatio < LowHp ? 2f : 1f);
        _stored = Mathf.Min(Cap(ctx), _stored + report.DamageDealt * rate);
        Publish(ctx);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_stored <= 0f || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        float dmg = _stored * (LegendaryTuning.Pick(NewRow, _value, 3f) + MultPerTier * LegendaryTuning.Tier("DARK"));
        _stored = 0f;
        Publish(ctx);
        var target = report.Target;
        Vector3 at = target.transform.position;
        CombatQuery.DealSynergyDamage(target, dmg, Self(ctx), element: RuneElement.Dark);
        int n = CombatQuery.GetNearbyDamageables(at, ReleaseRadius, target, 16, _buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Dark);
        if (LegendaryTuning.Awakened("DARK"))   // 각성 — 받는 피해 +20%
        {
            var mb = target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
            if (mb != null && !mb.IsDead) mb.ApplyDamageTakenAmp(AwakeAmp, AwakeSeconds, "abyss");
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, at, ReleaseRadius);   // 되갚음 범위 = 판정
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_stored <= 0f || ctx?.Player == null || Time.time < _auraAt) return;
        _auraAt = Time.time + AuraGap;
        ElementVfxPlayer.AttachStatus(RuneElement.Dark, ctx.Player.transform, AuraGap + 0.3f);   // 모인 동안 몸에 어둠 기운
    }
}

// ── D-2. 그림자 걸음 (기동 · 옛 그림자 분신) ─────────────────────────────
/// <summary>
/// 10-24 개편 — 저스트 회피 때(쿨 value2 = 8초) 피한 적(없으면 6 m 안 가장 가까운 적) 둘레에 분신 3체가 한 번씩 벤다(공격력 × value = 80% · 존 배율).
/// 그 뒤 1.5초 안 다음 타격 = 배후 일격: 그 적의 등 뒤에 그림자가 서서 베어 그 적이 받는 그 타격을 × value3(2.5)로(존 배율) — 1.5초 멈춤(보스는 받는 피해 +20% · 3초).
/// 순간이동은 하지 않는다(설계 D5 대체안 — 자리 튐 · 카메라 위험). 그동안 무기 끝에 어둠. 각성(어둠 존 4단계) = 쿨 5초.
/// 분신의 칼이 닿을 때(분신 애니의 베기 이벤트) 그 무기 첫 평타 베기가 어두운 보라로 나고 피해도 그때 들어간다(10-29 사용자 「분신 베기 이펙트 넣자」 · 이펙트 = 판정).
/// </summary>
public sealed class DarkLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private const int Clones = 3;
    private const float CloneLife = 1.2f, CloneInSeconds = 0.2f, CloneOutSeconds = 0.4f;   // 툭 생기고 툭 사라지지 않게(10-12 디졸브 ②)
    private const float StrikeTimeout = 0.8f;   // 분신 베기 이벤트가 안 오면 이때 친다(분신이 못 떴거나 클립에 이벤트가 없음)
    private const float SearchRange = 6f, BackWindow = 1.5f, BehindTarget = 0.9f, StunSeconds = 1.5f, BossAmp = 0.2f, BossAmpSeconds = 3f, AwakeCooldown = 5f;
    private float _readyAt;
    private GameObject _backTarget;
    private float _backUntil;
    private int _backFx;
    private CancellationTokenSource _cts;
    private PlayerController _dodgeSource;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnPerfectDodge」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnDodgeShadow";

    public DarkLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _readyAt = 0f;
        _cts = new CancellationTokenSource();
        if (ctx?.Player == null) return;
        _dodgeSource = ctx.Player;
        _dodgeSource.OnPerfectDodge += HandlePerfectDodge;
        PlayerGhostStrike.Warm(ctx.Player);   // 배후 일격 그림자 — 미리 만들어 둔다
    }

    public override void OnDeactivate()
    {
        if (_dodgeSource != null) _dodgeSource.OnPerfectDodge -= HandlePerfectDodge;
        _dodgeSource = null;
        EndBackstab();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void HandlePerfectDodge(float _)
    {
        var ctx = _ctx;
        if (ctx?.Player == null || _cts == null || Time.time < _readyAt) return;
        GameObject target = ctx.Player.PerfectDodgeSource != null ? ctx.Player.PerfectDodgeSource.gameObject : null;
        if (target == null || !target.activeInHierarchy)
        {
            int n = CombatQuery.GetNearbyDamageables(ctx.Player.transform.position, SearchRange, Self(ctx), 1, _buf);
            if (n == 0) return;
            target = _buf[0];
        }
        _readyAt = Time.time + (LegendaryTuning.Awakened("DARK") ? AwakeCooldown : LegendaryTuning.Pick(NewRow, _value2, 8f));
        SpawnClonesAsync(ctx, target, _cts.Token).Forget();

        EndBackstab();
        _backTarget = target;
        _backUntil  = Time.time + BackWindow;
        var wi = ctx.Player.WeaponManager != null ? ctx.Player.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>() : null;
        _backFx = ElementVfxPlayer.AttachStatus(RuneElement.Dark, wi != null && wi.tipPoint != null ? wi.tipPoint : ctx.Player.transform, BackWindow);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_backTarget == null || Time.time > _backUntil || report.DamageDealt <= 0f || ctx?.Player == null) return;
        var target = _backTarget;
        EndBackstab();
        if (!target.activeInHierarchy) return;
        float mult  = LegendaryTuning.Pick(NewRow, _value3, 2.5f) * LegendaryTuning.TierMul("DARK");
        float extra = report.DamageDealt * (report.Target == target ? mult - 1f : mult);   // 그 적이 받는 그 타격 = × 배수
        Backstab(ctx.Player, target, extra);
    }

    /// <summary>그 적의 등 뒤(플레이어 반대편)에 선 그림자가 벤다 — 칼이 닿을 때 피해 · 멈춤(이펙트 = 판정).</summary>
    private static void Backstab(PlayerController p, GameObject target, float dmg)
    {
        Vector3 away = target.transform.position - p.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : p.transform.forward;
        Vector3 stand = target.transform.position + away * BehindTarget;
        stand.y = p.transform.position.y;
        float lead = PlayerGhostStrike.Play(p, stand, -away);
        FormStrike.After(p, lead, () =>
        {
            if (target == null) return;
            CombatQuery.DealSynergyDamage(target, dmg, p.gameObject, element: RuneElement.Dark);
            var mb = target.GetComponentInParent<MonsterBase>();
            if (mb == null || mb.IsDead) return;
            if (mb.Grade == MonsterGrade.Boss) mb.ApplyDamageTakenAmp(BossAmp, BossAmpSeconds, "shadow_step");
            else mb.Status.ApplyCc("stun", StunSeconds);
        });
    }

    private void EndBackstab()
    {
        _backTarget = null;
        ElementVfxPlayer.ReleaseAura(_backFx);
        _backFx = 0;
    }

    private async UniTaskVoid SpawnClonesAsync(ItemEffectContext ctx, GameObject target, CancellationToken ct)
    {
        try
        {
            float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.8f) * LegendaryTuning.TierMul("DARK");
            float groundY = ctx.Player.transform.position.y;

            for (int i = 0; i < Clones; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;
                Vector3 center = target.transform.position;
                float yaw    = i * 360f / Clones + Random.Range(-30f, 30f);
                float radius = Random.Range(1f, 2.5f);
                Vector3 spawnPos = center + Quaternion.Euler(0, yaw, 0) * Vector3.forward * radius;
                spawnPos.y = groundY;
                Vector3 lookDir = center - spawnPos;
                lookDir.y = 0f;
                Quaternion rot = lookDir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(lookDir.normalized) : Quaternion.identity;

                var clone = SpawnShadowClone(ctx.Player.gameObject, spawnPos, rot);
                CloneStrikeAsync(ctx.Player, clone, target, dmg, ct).Forget();   // 피해 · 베기 · 노바는 분신 칼이 닿을 때

                await UniTask.Delay(200, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    // 비활성화 컨테이너 (Awake 지연을 통해 카메라 등록 방지)
    private static GameObject _cloneContainer;
    private static Transform GetCloneContainer()
    {
        if (_cloneContainer == null)
        {
            _cloneContainer = new GameObject("[ShadowCloneSpawn]");
            _cloneContainer.SetActive(false);
            Object.DontDestroyOnLoad(_cloneContainer);
        }
        return _cloneContainer.transform;
    }

    /// <summary>분신을 띄운다 — 띄우지 못했으면(플레이어 디졸브 중) null.</summary>
    private static GameObject SpawnShadowClone(GameObject player, Vector3 pos, Quaternion rot)
    {
        // 플레이어가 디졸브 중이면 복제하지 않는다 — 임시 디졸브 재질을 그대로 베껴, 원본 디졸브가 끝나 재질이 사라지면 분신이 마젠타가 된다.
        // 분신은 그림뿐이다(피해는 CloneStrikeAsync가 분신 칼이 닿을 때 따로 준다)
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            if (DissolveEffect.IsDissolving(r)) return null;
        // 비활성화 컨테이너 아래에서 생성 → Awake 미실행 → Awake 전에 IsShadowClone 설정
        var clone = Object.Instantiate(player, GetCloneContainer());
        clone.name = "ShadowClone";

        // PC를 분신으로 마킹 → 카메라/입력/PlayerManager 등록 생략, WeaponEffectHandler는 유지
        clone.GetComponent<PlayerController>()?.SetAsShadowClone();
        // FallRecoveryController는 분신에 불필요 (PC 의존이지만 PC를 제거하지 않으므로 일반 Destroy)
        var frc = clone.GetComponent<FallRecoveryController>();
        if (frc != null) Object.DestroyImmediate(frc);

        // Rigidbody kinematic + 완전 고정 → 중력/물리 영향 없음
        var rb = clone.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Collider 비활성화 → 충돌 없음
        foreach (var col in clone.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // 어둠 실루엣 색상
        var pb = new MaterialPropertyBlock();
        pb.SetColor("_BaseColor", new Color(0.05f, 0f, 0.15f, 0.9f));
        foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.SetPropertyBlock(pb);
        foreach (var mr in clone.GetComponentsInChildren<MeshRenderer>())
            mr.SetPropertyBlock(pb);

        // 부모에서 분리 후 위치 설정 → 활성화
        clone.transform.SetParent(null);
        clone.transform.SetPositionAndRotation(pos, rot);
        clone.SetActive(true);
        DissolveEffect.PlayAppear(clone, CloneInSeconds);

        // 평타 애니메이션 (Root Motion 비활성화 → 분신 이동으로 인한 카메라 영향 방지)
        var anim = clone.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.CrossFadeInFixedTime("GroundLightAttack_01", 0.05f);
        }

        CloneOutAsync(clone).Forget();
        Object.Destroy(clone, CloneLife + 0.4f);   // 디졸브를 못 해도 남지 않게 — 늦어도 이때 지운다
        return clone;
    }

    /// <summary>
    /// 분신 칼이 닿을 때 — 분신 애니의 베기 이벤트(분신은 무기 데이터가 없어 진짜 베기 · 판정은 안 나간다)를 기다려 어두운 베기 · 피해 · 맞은 자리 노바.
    /// 분신이 없거나 이벤트가 <see cref="StrikeTimeout"/>초 안에 안 오면 그때 친다.
    /// </summary>
    private static async UniTaskVoid CloneStrikeAsync(PlayerController p, GameObject clone, GameObject target, float dmg, CancellationToken ct)
    {
        var recv = clone != null ? clone.GetComponentInChildren<PlayerAnimationEventReceiver>(true) : null;
        bool cut = false;
        System.Action<int> onCut = _ => cut = true;
        if (recv != null) recv.OnEffectStepEarly += onCut;
        try
        {
            float until = Time.time + StrikeTimeout;
            while (!cut && clone != null && Time.time < until) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        catch (System.OperationCanceledException) { return; }
        finally { if (recv != null) recv.OnEffectStepEarly -= onCut; }
        if (p == null || target == null || !target.activeInHierarchy) return;
        if (clone != null) CloneSlash(p, clone.transform);
        CombatQuery.DealSynergyDamage(target, dmg, p.gameObject, element: RuneElement.Dark);
        LegendaryRuntime.SpawnVfx(LegendaryRuntime.Catalog?.darkSingleHitVfx, target.transform.position, Quaternion.identity, 1.5f, 0.19f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83: 노바 ×0.8 = 4.1 m)
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    /// <summary>
    /// 분신 자리에서 그 무기 첫 평타 베기(분신이 휘두르는 동작)를 어두운 보라로 — 카타나 베기 메아리와 같은 모습(<see cref="ShadowPartner.PlayDarkSlash"/>).
    /// 카탈로그 칸(darkCloneVfx)이 차 있으면 그것을. 원거리 무기는 베기가 없어 안 낸다.
    /// </summary>
    private static void CloneSlash(PlayerController p, Transform clone)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.darkCloneVfx != null) { LegendaryRuntime.SpawnVfx(cat.darkCloneVfx, clone.position, clone.rotation, 1.2f, 0.5f); return; }
        var w = p.WeaponManager != null ? p.WeaponManager.CurrentWeaponData : null;
        if (w == null || w.abilitySet == null || w.weaponType.GetAttackStatKind() != AttackStatKind.Melee) return;
        var steps = w.abilitySet.GetAbility(WeaponActionType.GroundLight, 0)?.GetSteps(0);
        if (steps == null) return;
        foreach (var s in steps)
        {
            var e = s.effect;
            if (e == null || string.IsNullOrEmpty(e.payloadKey)) continue;
            Transform socket = clone;
            if (e.socket != WeaponAbilitySO.EffectSocket.Player)
            {
                var mount = Util.FindDeepChild(clone, "WeaponMount");
                if (mount != null) socket = mount;
            }
            ShadowPartner.PlayDarkSlash(p, e.payloadKey, socket.TransformPoint(e.positionOffset), socket.rotation * Quaternion.Euler(e.rotationEuler),
                                        e.scaleMultiplier * GreatswordReach.Basic(p));
            return;
        }
    }

    /// <summary>분신 수명 끝 0.4초 — 디졸브로 사라진 뒤 지운다.</summary>
    private static async UniTaskVoid CloneOutAsync(GameObject clone)
    {
        try { await UniTask.Delay(System.TimeSpan.FromSeconds(CloneLife - CloneOutSeconds), cancellationToken: clone.GetCancellationTokenOnDestroy()); }
        catch (System.OperationCanceledException) { return; }
        DissolveEffect.PlayDisappear(clone, CloneOutSeconds, () => { if (clone != null) Object.Destroy(clone); });
    }

}

// ── D-3. 영혼 수확 (처치 · 옛 어둠의 낫) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 쓰러뜨릴 때마다 영혼 1(최대 value2 = 8 · 방을 넘어 남는다) — 영혼은 둘레를 도는 낫(닿으면 공격력 × value3 = 40% · 같은 적 0.5초 · 존 배율).
/// E · R을 쓰면 낫 전부를 앞으로 날린다 — 낫마다 지나며 닿은 적에게 공격력 × value(80% · 존 배율 · 설계 100%는 실측 95 +46%). 날린 낫은 다시 모아야 한다.
/// 각성(어둠 존 4단계) = 날린 낫이 적을 쓰러뜨리면 영혼 1을 더 돌려받는다. HUD = LegendaryHud "soul".
/// </summary>
public sealed class DarkLegendProjectileEffect : ItemCombatEffectBase
{
    private const float OrbitRadius = 1.8f, OrbitDegPerSec = 150f, ScytheScale = 0.35f, ScytheSpin = 540f, ThrowDistance = 10f;
    private const string HudKey = "soul";

    private LegendOrbitRing _ring;
    private ItemEffectContext _ctx;
    private int _souls;

    /// <summary>새 행(발동 「OnKillSoul」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnKillSoul";

    private int Max => Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 8f)));

    public DarkLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _souls = 0;
        LegendaryHud.Set(HudKey, 0, Max);
    }

    public override void OnDeactivate()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _ring = null;
        LegendaryHud.Clear(HudKey);
    }

    // 고리는 여기서만 맞춘다 — 처치 · 날린 낫 피해 도중(고리 Update 안)에 고리를 늘리지 않게
    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _ctx = ctx;
        if (ctx?.Player == null) return;
        if (_ring == null)
        {
            if (_souls <= 0) return;
            _ring = LegendOrbitRing.Spawn(this, ctx.Player.transform, LegendaryRuntime.Catalog?.darkScytheVfx, ScytheScale, OrbitRadius, OrbitDegPerSec);   // 씬이 바뀌어 고리가 사라져도 영혼은 남는다
            _ring.Spin      = ScytheSpin;
            _ring.OnContact = go => Hit(go, LegendaryTuning.Pick(NewRow, _value3, 0.4f), false);
        }
        int ready = _ring.ReadyCount;
        if (ready < _souls) _ring.Add(_souls - ready);
        LegendaryHud.Set(HudKey, _souls, Max);
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (ctx?.Player == null) return;
        _souls = Mathf.Min(Max, _souls + 1);
    }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if ((skill != SkillType.E && skill != SkillType.R) || ctx?.Player == null || _ring == null) return;
        _ctx = ctx;
        int thrown = _ring.LaunchAllForward(ctx.Player.transform.forward, ThrowDistance, go => Hit(go, LegendaryTuning.Pick(NewRow, _value, 0.8f), true));
        _souls = Mathf.Max(0, _souls - thrown);
    }

    private void Hit(GameObject go, float ratio, bool thrown)
    {
        if (go == null || _ctx?.Player == null) return;
        var mb = go.GetComponentInParent<MonsterBase>();
        bool alive = mb != null && !mb.IsDead;
        CombatQuery.DealSynergyDamage(go, LegendaryTuning.Atk(_ctx) * ratio * LegendaryTuning.TierMul("DARK"), _ctx.Player.gameObject, element: RuneElement.Dark);
        LegendaryRuntime.SpawnVfx(LegendaryRuntime.Catalog?.darkHitVfx, go.transform.position, Quaternion.identity, 2f, 0.5f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83)
        if (thrown && alive && mb.IsDead && LegendaryTuning.Awakened("DARK")) _souls = Mathf.Min(Max, _souls + 1);   // 각성 — 날린 낫이 쓰러뜨리면 하나 더
    }
}
