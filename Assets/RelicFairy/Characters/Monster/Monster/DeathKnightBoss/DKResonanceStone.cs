using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 공명 성흔석 — 유리벽 너머에 서 있는 죽음의 기사(원거리 보스)를 근접 빌드도 칠 수 있게 하는 상시 오브젝트.
/// (09-26 사용자 결정 A안 · 기획서 RelicFairy_죽음의기사_근접기회_설계_20260926.md)
///
/// · 유리 앞(플레이어 쪽)에 3개 — 기사에서 플레이어 쪽을 보는 방향 기준 −30°·0°·+30°, 유리 콜라이더에서 <see cref="FrontOffset"/>m 바깥.
/// · 콜라이더는 Player 레이어 — 근접 판정(ColliderInstance: MonsterHit|Player)과 화살(모든 층을 훑는다)이 맞히고,
///   광역 질의는 중계 등록부(<see cref="DamageRelays"/>)로 기사 몸처럼 본다. 추적 화살도 가까우면 성흔석을 쫓는다(11-05 #4).
/// · 받은 피해를 그대로(<see cref="TransferRatio"/>) 유리 너머 기사에게 전한다(기사 방어·무적 그대로) — 성흔석에서 기사까지 빛줄기가 번쩍인다.
/// · 과열(3초 12타 → 1.5초 식음)은 11-05 사용자 「돌이 모든 타격을 그대로 전달해야 한다」로 없앴다 — 화살 연사 타격이 식는 동안 버려졌다.
/// · 부서지지 않는다. 청록 = 칠 수 있음(해방 페이지 설계 §7 색 규칙). 모양 = 리치 봉인석 룬석 모델 재활용.
/// · 기사가 죽거나 사라지면 함께 사라진다.
/// </summary>
public sealed class DKResonanceStone : MonoBehaviour, IDamageable, IDamageRelay
{
    // ── Constants ─────────────────────────────────────────────────
    private const string ModelKey        = "Lich/VFX/SealStoneModel";
    private const string MeleeOnlyLayer  = "Player";
    private const float  TransferRatio   = 1f;    // 11-05 #4 — 모든 타격 그대로(옛 0.8)
    private const float  Radius          = 0.9f;
    private const float  Height          = 2.6f;
    private const float  FrontOffset     = 2.5f;
    private const float  SpreadDeg       = 30f;
    private const float  Clearance       = 0.3f;   // 석상 몸과 기둥 · 촛대 사이 최소 틈
    private const float  ClearFromY      = 0.8f;   // 이 높이 아래(바닥 · 계단)는 겹침으로 안 본다
    private const float  NudgeStepDeg    = 3f;     // 막히면 가운데 쪽으로 이만큼씩 돌린다
    private const float  MinSideDeg      = 12f;    // 옆 석상은 이 각도까지만 — 가운데 석상과 붙지 않게
    private const float  PlayerClear     = 0.6f;   // 석상 몸과 플레이어 사이 — 이보다 가까우면 플레이어를 밖으로 옮긴다(10-22 A0)
    private const float  BeamWidth       = 0.35f;
    private const float  BeamSeconds     = 0.12f;
    private const float  FlashSeconds    = 0.08f;
    private const float  IdleGlow        = 1.6f;
    private const float  FlashGlow       = 6f;
    private const float  FadeSeconds     = 0.6f;    // 서고 사라질 때 디졸브(10-12 디졸브 원칙)

    private static readonly int   EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Collider[] s_overlap = new Collider[8];

    // ── Private ───────────────────────────────────────────────────
    private DeathKnightBossMonster _boss;
    private Renderer[]             _renderers;
    private GameObject             _fallback;
    private MaterialPropertyBlock  _mpb;
    private float                  _nextFlashAt;   // 빛줄기 · 번쩍임은 BeamSeconds에 한 번
    private CapsuleCollider        _capsule;
    private bool                   _leaving;

    // ── Properties ────────────────────────────────────────────────
    public bool IsCooling => false;   // 과열 없앰(11-05 #4) — 실측 도구 두 곳(Ch3BossCheck · LevelSimBuildProbe)이 읽는다
    public GameObject RelayTarget => _boss != null && !_boss.IsDead && !_leaving ? _boss.gameObject : null;
    public float RelayRatio => TransferRatio;
    public Collider RelayBody => _capsule;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnEnable()  => DamageRelays.Register(this);    // 광역 조회가 성흔석을 기사 몸으로 본다(10-22 A1b)

    private void Update()
    {
        if (_leaving) return;
        if (_boss == null || _boss.IsDead || !_boss.gameObject.activeInHierarchy)
        {
            Leave();
            return;
        }
    }

    private void OnDisable() => DamageRelays.Unregister(this);

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
        var player = GameRunBootstrapper.Instance?.Run?.Player ?? IntroBootstrapper.Instance?.Player;
        foreach (float deg in new[] { -SpreadDeg, 0f, SpreadDeg })
        {
            if (!TryPlace(glass, origin, toward, deg, out Vector3 pos, out Vector3 dir)) continue;
            EvictPlayer(player, pos, dir);
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

    /// <summary>
    /// 근접 주 타격은 <see cref="CombatDamage"/>가 <see cref="TryAcceptHit"/> 뒤 기사에게 직접 넣는다(10-22 A1b — 상태 · 룬 효과까지 기사에게).
    /// 이 길은 CombatDamage를 거치지 않고 부르는 경우의 폴백이다.
    /// </summary>
    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (amount <= 0f || !TryAcceptHit()) return;
        _boss.TakeDamage(amount * TransferRatio, instigator, 0f, isCrit);
    }

    /// <summary>주 타격 한 번 — 기사가 있으면 늘 받는다(11-05 #4 과열 없앰). 빛줄기 · 번쩍임은 <see cref="BeamSeconds"/>에 한 번(화살 연사).</summary>
    public bool TryAcceptHit()
    {
        if (_boss == null || _boss.IsDead || _leaving) return false;
        if (Time.time >= _nextFlashAt)
        {
            _nextFlashAt = Time.time + BeamSeconds;
            FlashBeam();
            FlashAsync(destroyCancellationToken).Forget();
        }
        return true;
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
        for (int i = 0; i < n; i++)   // 플레이어 몸은 지형이 아니다 — 예전엔 플레이어를 기둥처럼 보고 「빈자리 없음 → 원래 자리」로 몸 위에 세웠다(10-22 A0)
            if (s_overlap[i].GetComponentInParent<PlayerController>() == null) return s_overlap[i].name;
        return null;
    }

    /// <summary>석상 자리에 플레이어가 서 있으면 석상 바깥 빈 바닥으로 옮긴다 — 근접 판정용 캡슐이 몸 위에 생겨 갇혔다(10-22 A0 · 빌드).</summary>
    private static void EvictPlayer(PlayerController player, Vector3 stonePos, Vector3 outward)
    {
        if (player == null) return;
        Vector3 off = player.transform.position - stonePos; off.y = 0f;
        float need = Radius + PlayerClear;
        if (off.magnitude >= need) return;
        Vector3 away = off.sqrMagnitude > 0.0004f ? off.normalized : outward;   // 한가운데면 유리 반대쪽(플레이어 쪽)으로
        Vector3 to = stonePos + away * (need + 0.3f);
        to.y = player.transform.position.y;
        if (NavMesh.SamplePosition(to, out var hit, 1.5f, NavMesh.AllAreas)) to = hit.position;
        player.transform.position = to;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
        Debug.Log($"[DK] 공명 성흔석 자리에 플레이어가 있어 {Vector3.Distance(stonePos, to):0.0} m 밖으로 옮김");
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
