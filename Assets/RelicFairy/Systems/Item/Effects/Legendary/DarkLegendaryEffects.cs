using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── 어둠 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 심연 잠식 = 위기 · 그림자 분신 = 기동 · 어둠의 낫 = 처치. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── D-1. 심연 잠식 (위기) ─────────────────────────────────────────────
/// <summary>
/// 체력이 낮을수록 자주 — 체력 가득 8초(value2) → 체력 30% 아래 3초마다 플레이어 둘레 3 m에 어둠 파동 4번(공격력 × value = 50%).
/// 맞을 적이 없으면 시계를 멈춘다.
/// </summary>
public sealed class DarkLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int   WaveTicks = 4;
    private const float Radius = 3f, FastPeriod = 3f, FastAtHp = 0.3f;

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「HPBelow40」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "HPBelow40";

    public DarkLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer = 0f;
        _cts = new CancellationTokenSource();
    }

    public override void OnDeactivate()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null || _cts == null) return;
        Vector3 pos = ctx.Player.transform.position;
        if (CombatQuery.GetNearbyDamageables(pos, Radius, Self(ctx), 1, _buf) == 0) return;   // 맞을 적이 없으면 시계를 멈춘다
        // 체력이 낮을수록 주기가 짧다 — 가득 8초 → 30%에서 3초(그 아래는 3초)
        float slow   = LegendaryTuning.Pick(NewRow, _value2, 8f);
        float period = Mathf.Lerp(FastPeriod, slow, Mathf.InverseLerp(FastAtHp, 1f, ctx.HpRatio));
        _timer += deltaTime;
        if (_timer < period) return;
        _timer = 0f;
        var cat = LegendaryRuntime.Catalog;
        LegendaryRuntime.SpawnVfx(cat?.darkAoeVfx, pos, Quaternion.identity, 4f, Radius * 0.286f, alpha: 0.18f, yScale: 0.1f, follow: ctx.Player.transform);
        WaveTickAsync(ctx, _cts.Token).Forget();
    }

    private async UniTaskVoid WaveTickAsync(ItemEffectContext ctx, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.5f) * LegendaryTuning.TierMul("DARK");
                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, Radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Dark);
                    // 타격 이펙트는 파동 첫 틱만(10-11 이펙트 톤 2차 V-10)
                    if (t == 0) LegendaryRuntime.SpawnVfx(cat?.darkAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                }
                if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── D-2. 그림자 분신 (기동) ───────────────────────────────────────────
/// <summary>
/// 저스트 회피를 하면(쿨 value2 = 8초) 피한 적(없으면 가장 가까운 적) 둘레에 분신 3체 — 한 체 공격력 × value(150%).
/// </summary>
public sealed class DarkLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private const int Clones = 3;
    private const float CloneLife = 1.2f, CloneInSeconds = 0.2f, CloneOutSeconds = 0.4f;   // 툭 생기고 툭 사라지지 않게(10-12 디졸브 ②)
    private float _readyAt;
    private CancellationTokenSource _cts;
    private PlayerController _dodgeSource;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnPerfectDodge」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnPerfectDodge";

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
    }

    public override void OnDeactivate()
    {
        if (_dodgeSource != null) _dodgeSource.OnPerfectDodge -= HandlePerfectDodge;
        _dodgeSource = null;
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
            int n = CombatQuery.GetNearbyDamageables(ctx.Player.transform.position, 20f, Self(ctx), 1, _buf);
            if (n == 0) return;
            target = _buf[0];
        }
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 8f);
        SpawnClonesAsync(ctx, target, _cts.Token).Forget();
    }

    private async UniTaskVoid SpawnClonesAsync(ItemEffectContext ctx, GameObject target, CancellationToken ct)
    {
        try
        {
            float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 1.5f) * LegendaryTuning.TierMul("DARK");
            float groundY = ctx.Player.transform.position.y;
            var cat = LegendaryRuntime.Catalog;

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

                SpawnShadowClone(ctx.Player.gameObject, spawnPos, rot);
                CombatQuery.DealSynergyDamage(target, dmg, ctx?.Player?.gameObject, element: RuneElement.Dark);
                LegendaryRuntime.SpawnVfx(cat?.darkSingleHitVfx, center, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

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

    private static void SpawnShadowClone(GameObject player, Vector3 pos, Quaternion rot)
    {
        // 플레이어가 디졸브 중이면 복제하지 않는다 — 임시 디졸브 재질을 그대로 베껴, 원본 디졸브가 끝나 재질이 사라지면 분신이 마젠타가 된다.
        // 분신은 그림뿐이다(피해는 SpawnClonesAsync가 따로 준다)
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            if (DissolveEffect.IsDissolving(r)) return;
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

        // 칼 휘두르기 VFX
        var cat = LegendaryRuntime.Catalog;
        if (cat?.darkCloneVfx != null)
            LegendaryRuntime.SpawnVfx(cat.darkCloneVfx, pos, rot, 1.2f, 0.5f);

        CloneOutAsync(clone).Forget();
        Object.Destroy(clone, CloneLife + 0.4f);   // 디졸브를 못 해도 남지 않게 — 늦어도 이때 지운다
    }

    /// <summary>분신 수명 끝 0.4초 — 디졸브로 사라진 뒤 지운다.</summary>
    private static async UniTaskVoid CloneOutAsync(GameObject clone)
    {
        try { await UniTask.Delay(System.TimeSpan.FromSeconds(CloneLife - CloneOutSeconds), cancellationToken: clone.GetCancellationTokenOnDestroy()); }
        catch (System.OperationCanceledException) { return; }
        DissolveEffect.PlayDisappear(clone, CloneOutSeconds, () => { if (clone != null) Object.Destroy(clone); });
    }

}

// ── D-3. 어둠의 낫 (처치) ─────────────────────────────────────────────
/// <summary>
/// 방에 들어서면 낫 2자루(존 단계마다 +1) — 쓰러뜨릴 때마다 +1(최대 7). 곡선으로 돌며 닿은 적에게 공격력 × value(50%) · 같은 대상 0.4초에 한 번.
/// </summary>
public sealed class DarkLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   StartScythes = 2, MaxScythes = 7;
    private const float HitGap = 0.4f;

    private int _scythes;
    private bool _spawned;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnKill」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnKill";

    public DarkLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _spawned = false;
    }

    public override void OnDeactivate() => LegendaryRuntime.Instance?.ClearAgentsOf(this);

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _spawned = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _ctx = ctx;
        if (_spawned || ctx?.Player == null) return;
        _spawned = true;
        _scythes = 0;
        int start = StartScythes + LegendaryTuning.Tier("DARK");
        for (int i = 0; i < start; i++) SpawnScythe(ctx, i * 360f / start, i);
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (ctx?.Player == null || !_spawned) return;
        if (_scythes < MaxScythes + LegendaryTuning.Tier("DARK")) SpawnScythe(ctx, Random.Range(0f, 360f), _scythes);
    }

    // 개수형 — 존 단계는 자루 수(+1)로 받는다(피해 배율은 붙이지 않는다)
    private float ScytheDamage() => LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.5f);

    private void SpawnScythe(ItemEffectContext ctx, float angle, int index)
    {
        Vector3 dir = Quaternion.Euler(0, angle + 45f, 0) * Vector3.forward;
        Vector3 pos = LegendaryRuntime.GetRandomNavPoint(LegendaryProjectileBase.FlyHeight);
        var go    = new GameObject("DarkScythe");
        var agent = go.AddComponent<DarkScytheAgent>();
        float turnSign = (index % 2 == 0) ? 1f : -1f; // 낫마다 좌/우 곡선 교대
        agent.InitScythe(this, ctx, 0f, 8f, dir, turnSign);
        agent.PerTargetGap = HitGap;
        agent.DamageFn     = ScytheDamage;
        go.transform.position = pos;
        _scythes++;
    }
}
