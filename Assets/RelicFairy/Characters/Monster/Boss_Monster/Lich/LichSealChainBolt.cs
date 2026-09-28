using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// N1 「뒤집힌 봉인술」의 사슬 — 리치 손에서 뻗어 플레이어를 천천히 쫓는 보라 사슬(끝에 보라 탄 · 발밑 청록 고리 = 끊을 수 있음).
/// 한 번 치면 끊긴다(<see cref="IDamageable"/> + MonsterHit 레이어 트리거 — 플레이어 몸을 막지 않는다).
/// 플레이어에 닿으면 <see cref="Hit"/>, 끊기면 <see cref="Broken"/>, 수명이 다하면 조용히 사라진다.
/// </summary>
public sealed class LichSealChainBolt : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string HitLayerName = "MonsterHit";
    private const float  HeadRadius   = 0.7f;
    private const float  HeadHeight   = 1.1f;
    private const float  TouchRadius  = 0.9f;    // 플레이어와 수평 거리
    private const float  RingRadius   = 0.9f;
    private const float  ChainWidth   = 0.35f;
    private const float  SealChainDamage = 0.3f;   // 맞은 느낌만 — 본 효과는 기술 봉인

    private static readonly List<LichSealChainBolt> s_live = new();

    // ── Private ───────────────────────────────────────────────────
    private MonsterContext _ctx;
    private Transform      _target;
    private LichChainLine  _chain;
    private GameObject     _head;
    private GameObject     _ring;
    private Vector3        _dir;
    private float          _speed;
    private float          _turn;
    private float          _dieAt;
    private float          _floorY;
    private bool           _done;

    // ── Properties ────────────────────────────────────────────────
    public static IReadOnlyList<LichSealChainBolt> Live => s_live;

    /// <summary>플레이어에 닿았다(피해가 실제로 들어갔을 때만).</summary>
    public event Action<LichSealChainBolt> Hit;
    /// <summary>플레이어가 끊었다.</summary>
    public event Action<LichSealChainBolt> Broken;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_live.Clear();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (_done) return;
        if (Time.time >= _dieAt || _target == null) { Fade(); return; }

        float dt = Time.deltaTime;
        Vector3 to = _target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            _dir = Vector3.RotateTowards(_dir, to.normalized, _turn * Mathf.Deg2Rad * dt, 0f);

        Vector3 p = transform.position + _dir * (_speed * dt);
        p.y = _floorY + HeadHeight;
        transform.position = p;
        transform.rotation = Quaternion.LookRotation(_dir);
        if (_head != null) _head.transform.SetPositionAndRotation(p, transform.rotation);
        if (_ring != null) _ring.transform.position = new Vector3(p.x, _floorY + 0.03f, p.z);

        Vector3 flat = _target.position - p;
        flat.y = 0f;
        if (flat.magnitude <= TouchRadius && LichPatternUtil.HitPlayer(_ctx, p, SealChainDamage, 0f))
        {
            LichVfx.Play(LichVfxSlot.SealBurst, p, Quaternion.identity, 0.35f);
            LichSfx.Play(LichSfxSlot.ChainPulse, p);
            _done = true;
            Hit?.Invoke(this);
            Dispose();
        }
    }

    private void OnDestroy()
    {
        s_live.Remove(this);
        CleanupVisuals();
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>리치 손(<paramref name="hand"/>)에서 <paramref name="dir"/>로 뻗는 사슬 하나.</summary>
    public static LichSealChainBolt Launch(MonsterContext ctx, Transform hand, Transform target, Vector3 dir,
                                           float speed, float turnDegPerSec, float lifetime, float floorY)
    {
        var go = new GameObject("LichSealChainBolt");
        Vector3 start = hand.position;
        start.y = floorY + HeadHeight;
        go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir));
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col       = go.AddComponent<SphereCollider>();
        col.radius    = HeadRadius;
        col.isTrigger = true;

        var bolt = go.AddComponent<LichSealChainBolt>();
        bolt._ctx    = ctx;
        bolt._target = target;
        bolt._dir    = dir;
        bolt._speed  = speed;
        bolt._turn   = turnDegPerSec;
        bolt._dieAt  = Time.time + lifetime;
        bolt._floorY = floorY;

        bolt._head  = LichVfx.PlayLoopTinted(LichVfxSlot.BoltProjectile, start, go.transform.rotation, 0.6f, PatternGuideHelper.Reversed);
        bolt._ring  = PatternGuideHelper.Disc(new Vector3(start.x, floorY + 0.03f, start.z), RingRadius, PatternGuideHelper.Breakable);
        bolt._chain = LichChainLine.Create(hand.position, start, ChainWidth, PatternGuideHelper.Reversed);
        bolt._chain?.Follow(hand, Vector3.zero, go.transform, Vector3.zero);

        s_live.Add(bolt);
        return bolt;
    }

    /// <summary>모두 거둔다 — 패턴 종료 · 전환 · 전투 종료.</summary>
    public static void ClearAll()
    {
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] != null) s_live[i].Fade();
        s_live.Clear();
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_done) return;
        _done = true;
        Vector3 p = transform.position;
        LichVfx.Play(LichVfxSlot.SealStoneBurst, p, Quaternion.identity, 0.4f);
        LichSfx.Play(LichSfxSlot.SealStoneHit, p);
        Broken?.Invoke(this);
        Dispose();
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Fade()
    {
        _done = true;
        Dispose();
    }

    private void Dispose()
    {
        s_live.Remove(this);
        CleanupVisuals();
        if (this != null) Destroy(gameObject);
    }

    private void CleanupVisuals()
    {
        LichVfx.Stop(ref _head, 0.15f);
        PatternGuideHelper.SafeDestroy(ref _ring);
        if (_chain != null)
        {
            _chain.Dispose();
            _chain = null;
        }
    }
}
}
