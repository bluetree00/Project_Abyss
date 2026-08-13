using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── D-1. 심연 잠식 (광역기) ───────────────────────────────────────────
public sealed class DarkLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private const int WaveTicks = 4;

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private CancellationTokenSource _cts;

    public DarkLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer    = 0f;
        _baseTier = 0;
        _baseSet  = false;
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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int extra    = ExtraTiers();
        float radius = 3f + extra * 0.4f;

        Vector3 pos = ctx.Player.transform.position;
        var cat = LegendaryRuntime.Catalog;
        float vfxScale = 0.4f + extra * 0.15f;
        LegendaryRuntime.SpawnVfx(cat?.darkAoeVfx, pos, Quaternion.identity, 4f, vfxScale, alpha: 0.18f, yScale: 0.1f);

        WaveTickAsync(ctx, radius, _cts.Token).Forget();
    }

    private async UniTaskVoid WaveTickAsync(ItemEffectContext ctx, float radius, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;

                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], EffAtk(ctx) * 0.5f, Self(ctx), element: RuneElement.Dark);
                    LegendaryRuntime.SpawnVfx(cat?.darkAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                }
                if (n > 0)
                    Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                float atkMult = ctx.Player?.RuntimeStats?.AttackSpeedMultiplier ?? 1f;
                await UniTask.Delay(Mathf.RoundToInt(1000f / atkMult), cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0) - _baseTier);
}

// ── D-2. 그림자 분신 (단일기) ─────────────────────────────────────────
public sealed class DarkLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private const int BaseClones = 5;
    private CancellationTokenSource _cts;

    public DarkLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer    = 0f;
        _baseTier = 0;
        _baseSet  = false;
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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        Vector3 pos = ctx.Player.transform.position;
        int n = CombatQuery.GetNearbyDamageables(pos, 20f, Self(ctx), 1, _buf);
        if (n == 0) return;

        SpawnClonesAsync(ctx, _buf[0], BaseClones + ExtraTiers(), _cts.Token).Forget();
    }

    private async UniTaskVoid SpawnClonesAsync(ItemEffectContext ctx, GameObject target, int clones, CancellationToken ct)
    {
        try
        {
            float dmg = EffAtk(ctx) * _value;
            Vector3 center = target.transform.position;
            float groundY  = ctx.Player.transform.position.y;
            var cat = LegendaryRuntime.Catalog;

            for (int i = 0; i < clones; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                float yaw    = Random.Range(0f, 360f);
                float radius = Random.Range(1f, 2.5f);
                Vector3 spawnPos = center + Quaternion.Euler(0, yaw, 0) * Vector3.forward * radius;
                spawnPos.y = groundY;
                Vector3 lookDir = center - spawnPos;
                lookDir.y = 0f;
                Quaternion rot = lookDir.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(lookDir.normalized)
                    : Quaternion.identity;

                SpawnShadowClone(ctx.Player.gameObject, spawnPos, rot);

                CombatQuery.DealSynergyDamage(target, dmg, ctx?.Player?.gameObject, element: RuneElement.Dark);
                LegendaryRuntime.SpawnVfx(cat?.darkSingleHitVfx, center, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(250, cancellationToken: ct);
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

        Object.Destroy(clone, 1.2f);
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0) - _baseTier);
}

// ── D-3. 어둠의 낫 (투사체) ───────────────────────────────────────────
public sealed class DarkLegendProjectileEffect : ItemCombatEffectBase
{
    private int _baseTier;
    private int _lastExtra = -1;
    private bool _baseSet;

    public DarkLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _baseTier  = 0;
        _lastExtra = -1;
        _baseSet   = false;
    }

    public override void OnDeactivate() => LegendaryRuntime.Instance?.ClearAgentsOf(this);

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _baseSet = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet)
        {
            _baseTier  = MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0;
            _lastExtra = ExtraTiers();
            _baseSet   = true;
            SpawnScythes(ctx);
            return;
        }
        int extra = ExtraTiers();
        if (extra == _lastExtra) return;
        _lastExtra = extra;
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        SpawnScythes(ctx);
    }

    private void SpawnScythes(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        int extra = ExtraTiers();
        int count = (int)_value2 + extra;
        float dmg = EffAtk(ctx) * _value;

        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count + 45f + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 pos = LegendaryRuntime.GetRandomNavPoint(LegendaryProjectileBase.FlyHeight);

            var go    = new GameObject("DarkScythe");
            var agent = go.AddComponent<DarkScytheAgent>();
            float turnSign = (i % 2 == 0) ? 1f : -1f; // 낫마다 좌/우 곡선 교대
            agent.InitScythe(this, ctx, dmg, 8f, dir, turnSign);
            go.transform.position = pos;
        }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("DARK") ?? 0) - _baseTier);
}
