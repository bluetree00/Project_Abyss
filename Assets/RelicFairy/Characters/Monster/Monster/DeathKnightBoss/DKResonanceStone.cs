using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 공명 성흔석 — 유리벽 너머에 서 있는 죽음의 기사(원거리 보스)를 근접 빌드도 칠 수 있게 하는 상시 오브젝트.
/// (09-26 사용자 결정 A안 · 기획서 RelicFairy_죽음의기사_근접기회_설계_20260926.md)
///
/// · 유리 앞(플레이어 쪽)에 3개 — 기사에서 플레이어 쪽을 보는 방향 기준 −30°·0°·+30°, 유리 콜라이더에서 <see cref="FrontOffset"/>m 바깥.
/// · <b>근접 타격만 받는다</b> — 콜라이더를 Player 레이어에 둔다. 근접 판정(ColliderInstance: MonsterHit|Player)만 이 레이어를 보고,
///   투사체·광역 질의(MonsterBase.HitLayerMask = MonsterHit)는 안 보므로 기사를 향한 화살을 가로막지 않는다.
/// · 받은 피해 × <see cref="TransferRatio"/>를 유리 너머 기사에게 전한다(기사 방어·무적 그대로) — 성흔석에서 기사까지 빛줄기가 번쩍인다.
/// · 과열: <see cref="OverheatWindow"/>초 안에 <see cref="OverheatHits"/>타를 넘으면 <see cref="CooldownSeconds"/>초 식는다 — 한 자리에 붙어
///   치지 말고 옮겨 치게(기사 공격은 플레이어 칸·행·열을 노린다).
/// · 부서지지 않는다. 청록 = 칠 수 있음(해방 페이지 설계 §7 색 규칙). 모양 = 리치 봉인석 룬석 모델 재활용.
/// · 기사가 죽거나 사라지면 함께 사라진다.
/// </summary>
public sealed class DKResonanceStone : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string ModelKey        = "Lich/VFX/SealStoneModel";
    private const string MeleeOnlyLayer  = "Player";
    private const float  TransferRatio   = 0.8f;
    private const int    OverheatHits    = 6;
    private const float  OverheatWindow  = 3f;
    private const float  CooldownSeconds = 2f;
    private const float  Radius          = 0.9f;
    private const float  Height          = 2.6f;
    private const float  FrontOffset     = 2.5f;
    private const float  SpreadDeg       = 30f;
    private const float  Clearance       = 0.3f;   // 석상 몸과 기둥 · 촛대 사이 최소 틈
    private const float  ClearFromY      = 0.8f;   // 이 높이 아래(바닥 · 계단)는 겹침으로 안 본다
    private const float  NudgeStepDeg    = 3f;     // 막히면 가운데 쪽으로 이만큼씩 돌린다
    private const float  MinSideDeg      = 12f;    // 옆 석상은 이 각도까지만 — 가운데 석상과 붙지 않게
    private const float  BeamWidth       = 0.35f;
    private const float  BeamSeconds     = 0.12f;
    private const float  FlashSeconds    = 0.08f;
    private const float  IdleGlow        = 1.6f;
    private const float  FlashGlow       = 6f;
    private const float  CoolGlow        = 0.35f;
    private const float  FadeSeconds     = 0.6f;    // 서고 사라질 때 디졸브(10-12 디졸브 원칙)

    private static readonly int   EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color HotColor   = new Color(1f, 0.32f, 0.12f);
    private static readonly Collider[] s_overlap = new Collider[8];

    // ── Private ───────────────────────────────────────────────────
    private DeathKnightBossMonster _boss;
    private Renderer[]             _renderers;
    private GameObject             _fallback;
    private MaterialPropertyBlock  _mpb;
    private readonly Queue<float>  _recentHits = new();
    private float                  _coolUntil = float.NegativeInfinity;
    private CapsuleCollider        _capsule;
    private bool                   _leaving;

    // ── Properties ────────────────────────────────────────────────
    public bool IsCooling => Time.time < _coolUntil;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (_leaving) return;
        if (_boss == null || _boss.IsDead || !_boss.gameObject.activeInHierarchy)
        {
            Leave();
            return;
        }
        // 식는 중 → 다 식으면 청록으로 되돌린다(프레임마다 색을 쓰지 않게 전환 순간만)
        if (_coolUntil > 0f && Time.time >= _coolUntil)
        {
            _coolUntil = 0f;
            ApplyLook(PatternGuideHelper.Breakable, IdleGlow);
        }
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 기사 전투가 시작될 때(유리벽이 켜진 뒤) 부른다. 유리 콜라이더까지의 거리를 세 방향으로 재 그 바깥에 세운다.
    /// </summary>
    public static async UniTaskVoid SpawnSetAsync(DeathKnightBossMonster boss, GameObject[] glassObjects,
                                                  Vector3 playerSide, CancellationToken ct)
    {
        if (boss == null || glassObjects == null) return;
        var glass = new List<Collider>();
        foreach (var g in glassObjects)
            if (g != null) glass.AddRange(g.GetComponentsInChildren<Collider>(true));
        if (glass.Count == 0) return;

        Vector3 origin = boss.transform.position;
        Vector3 toward = playerSide - origin; toward.y = 0f;
        if (toward.sqrMagnitude < 0.01f) toward = boss.transform.forward;
        toward.Normalize();

        Transform parent = boss.transform.parent;
        foreach (float deg in new[] { -SpreadDeg, 0f, SpreadDeg })
        {
            if (!TryPlace(glass, origin, toward, deg, out Vector3 pos, out Vector3 dir)) continue;
            try
            {
                await CreateAsync(boss, pos, dir, parent, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_boss == null || _boss.IsDead || amount <= 0f) return;
        if (IsCooling) return;

        float now = Time.time;
        _recentHits.Enqueue(now);
        while (_recentHits.Count > 0 && now - _recentHits.Peek() > OverheatWindow) _recentHits.Dequeue();

        _boss.TakeDamage(amount * TransferRatio, instigator, 0f, isCrit);
        FlashBeam();

        if (_recentHits.Count > OverheatHits)
        {
            _recentHits.Clear();
            _coolUntil = now + CooldownSeconds;
            ApplyLook(HotColor, CoolGlow);
            return;
        }
        FlashAsync(destroyCancellationToken).Forget();
    }

    // ── Private Methods ───────────────────────────────────────────
    private static async UniTask CreateAsync(DeathKnightBossMonster boss, Vector3 groundPos, Vector3 outward,
                                             Transform parent, CancellationToken ct)
    {
        var go = new GameObject("DKResonanceStone");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(groundPos, Quaternion.LookRotation(outward));
        int layer = LayerMask.NameToLayer(MeleeOnlyLayer);
        if (layer >= 0) go.layer = layer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = Radius;
        col.height = Height;
        col.center = Vector3.up * (Height * 0.5f);

        var stone = go.AddComponent<DKResonanceStone>();
        stone._boss    = boss;
        stone._capsule = col;

        GameObject model = null;
        var am = Managers.AddressableManager;
        if (am != null)
            model = await am.InstantiateAsync(ModelKey, go.transform);
        ct.ThrowIfCancellationRequested();
        if (stone == null) { if (model != null) Destroy(model); return; }   // 기다리는 동안 전투가 끝났다

        if (model != null)
        {
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // 모델 쪽 콜라이더가 있으면 끈다 — 근접 판정은 루트 캡슐 하나로(레이어가 달라 판정이 새지 않게)
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            stone._renderers = model.GetComponentsInChildren<Renderer>(true);
        }
        else
        {
            stone._fallback = PatternGuideHelper.Pillar(groundPos, Radius * 0.8f, Height, PatternGuideHelper.Breakable);
            stone._fallback.transform.SetParent(go.transform, true);
        }
        stone.ApplyLook(PatternGuideHelper.Breakable, IdleGlow);
        DissolveEffect.PlayAppear(go, FadeSeconds);
    }

    /// <summary>
    /// 석상 자리 — 기사에서 <paramref name="deg"/>° 방향 유리 바깥. 기둥 · 촛대와 겹치면 가운데(0°) 쪽으로 조금씩 돌려 본다
    /// (10-07 사용자: 왼쪽 석상이 기둥과 겹침 — 실측: 왼쪽 기둥 받침이 석상 축에서 0.06 m). 다 막히면 원래 자리.
    /// </summary>
    private static bool TryPlace(List<Collider> glass, Vector3 origin, Vector3 toward, float deg, out Vector3 pos, out Vector3 dir)
    {
        pos = default; dir = default;
        bool   found   = false;
        string blocker = null;
        float  sign    = Mathf.Sign(deg);
        for (float a = deg; ; a -= sign * NudgeStepDeg)
        {
            Vector3 d = Quaternion.Euler(0f, a, 0f) * toward;
            if (TryGlassDistance(glass, origin, d, out float dist))
            {
                Vector3 p = origin + d * (dist + FrontOffset);
                p.y = GroundY(p, origin.y);
                if (!found) { pos = p; dir = d; found = true; }
                string hit = Blocker(p, d);
                if (hit == null)
                {
                    if (blocker != null) Debug.Log($"[DK] 공명 성흔석 {deg:0}° 자리가 {blocker}와 겹쳐 {a:0}°로 옮김");
                    pos = p; dir = d;
                    return true;
                }
                blocker ??= hit;
            }
            if (deg == 0f || Mathf.Abs(a) - NudgeStepDeg < MinSideDeg) break;
        }
        if (blocker != null) Debug.LogWarning($"[DK] 공명 성흔석 {deg:0}° — 빈자리 없음({blocker}) · 원래 자리에 둔다");
        return found;
    }

    /// <summary>석상 몸(바닥 · 계단 높이 위)이 기둥 · 촛대 같은 지형과 겹치면 그 이름, 아니면 null.</summary>
    private static string Blocker(Vector3 groundPos, Vector3 outward)
    {
        float   half   = (Height - ClearFromY) * 0.5f;
        Vector3 center = groundPos + Vector3.up * (ClearFromY + half);
        Vector3 ext    = new Vector3(Radius + Clearance, half, Radius + Clearance);
        int     mask   = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "UI", "Ignore Raycast", "Ground");
        int n = Physics.OverlapBoxNonAlloc(center, ext, s_overlap, Quaternion.LookRotation(outward), mask, QueryTriggerInteraction.Ignore);
        return n > 0 ? s_overlap[0].name : null;
    }

    private static bool TryGlassDistance(List<Collider> glass, Vector3 origin, Vector3 dir, out float dist)
    {
        dist = float.MaxValue;
        var ray = new Ray(origin + Vector3.up * 1f, dir);
        foreach (var c in glass)
        {
            if (c == null) continue;
            if (c.Raycast(ray, out var hit, 60f) && hit.distance < dist) dist = hit.distance;
        }
        return dist < float.MaxValue;
    }

    private static float GroundY(Vector3 p, float fallback)
        => Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y : fallback;

    // 전투가 끝났다 — 디졸브로 거둔다(예전엔 바로 사라졌다). 그동안 맞지 않는다
    private void Leave()
    {
        _leaving = true;
        if (_capsule != null) _capsule.enabled = false;
        var go = gameObject;
        DissolveEffect.PlayDisappear(go, FadeSeconds, () => { if (go != null) Destroy(go); });
    }

    private void FlashBeam()
    {
        Vector3 from = transform.position + Vector3.up * (Height * 0.8f);
        Vector3 to   = _boss.transform.position + Vector3.up * 1.5f;
        var beam = LichChainLine.Create(from, to, BeamWidth, PatternGuideHelper.Breakable);
        beam.Flash(BeamSeconds);
        DisposeLaterAsync(beam, destroyCancellationToken).Forget();
    }

    private static async UniTaskVoid DisposeLaterAsync(LichChainLine beam, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(BeamSeconds), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            // 성흔석이 먼저 사라져도 빛줄기는 거둔다
        }
        beam.Dispose();
    }

    private void ApplyLook(Color color, float glow)
    {
        if (_fallback != null) PatternGuideHelper.SetColor(_fallback, color);
        if (_renderers == null) return;
        _mpb ??= new MaterialPropertyBlock();
        Color emission = color * glow;
        emission.a = 1f;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionId, emission);
            r.SetPropertyBlock(_mpb);
        }
    }

    private async UniTaskVoid FlashAsync(CancellationToken ct)
    {
        ApplyLook(Color.white, FlashGlow);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(FlashSeconds), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!IsCooling) ApplyLook(PatternGuideHelper.Breakable, IdleGlow);
    }
}
}
