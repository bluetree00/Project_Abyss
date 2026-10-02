using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 봉인 조각 — 악몽기 N2(완전설계 §4-4). 봉인기에 깨진 봉인석 조각이 제단 네 방위 위에 떠서 리치의 무기가 된다.
/// 청록(끊을 수 있음) — 두 번 치면 <see cref="DisableSeconds"/>초 동안 꺼진다(꺼진 조각은 광선을 쏘지 않는다).
/// 봉인석(<see cref="LichSealStone"/>)과 같은 방식: <see cref="IDamageable"/> + MonsterHit 레이어, 피해량 대신 타격 횟수.
/// 한 번 떠오르면 T3(최후의 원)나 전투가 끝날 때까지 남는다 — <see cref="ClearAll"/>.
/// </summary>
public sealed class LichSealShard : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string HitLayerName   = "MonsterHit";
    private const int    HitsToDisable  = 2;
    private const float  HitInterval    = 0.15f;
    private const float  DisableSeconds = 5f;
    private const float  Radius         = 0.9f;
    private const float  Height         = 2.2f;
    private const float  ModelScale     = 1.1f;   // 0.7은 20 m 밖에서 안 읽혔다(09-19 실측)
    private const float  FloatHeight    = 1.2f;   // 근접으로 닿는 높이
    private const float  BobAmplitude   = 0.25f;
    private const float  RiseSeconds    = 1.2f;
    private const float  RiseDepth      = 20f;
    private const float  ActiveGlow     = 3f;
    private const float  ChargeGlow     = 7f;
    private const float  ShatterSeconds = 0.6f;   // 디졸브로 스러진다(10-03)
    private const float  PipsLift       = 0.7f;   // 남은 타격 표식 — 조각 위(m)

    private static readonly int   EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color OffColor   = new(0.15f, 0.15f, 0.2f);
    private static readonly List<LichSealShard> s_live = new();

    // ── Private ───────────────────────────────────────────────────
    private GameObject    _model;
    private GameObject    _fallback;
    private Renderer[]    _renderers;
    private MaterialPropertyBlock _mpb;
    private Vector3       _home;
    private int           _hits;
    private float         _lastHitTime = float.NegativeInfinity;
    private float         _disabledUntil = -1f;
    private float         _glow = ActiveGlow;
    private bool          _rising = true;   // 솟아오르는 동안은 흔들지 않는다
    private bool          _gone;            // 스러지는 중 — 멈춘다
    private LichHitPips   _pips;            // 남은 타격 표식(10-03)

    // ── Properties ────────────────────────────────────────────────
    public static IReadOnlyList<LichSealShard> Live => s_live;
    public bool    IsDisabled => Time.time < _disabledUntil;
    public Vector3 BeamOrigin => _home + Vector3.up * (Height * 0.5f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_live.Clear();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (_rising || _gone) return;
        // 천천히 떠 있다(꺼지면 가라앉은 채).
        float bob = IsDisabled ? -0.3f : Mathf.Sin(Time.time * 1.7f + _home.x) * BobAmplitude;
        var p = transform.position;
        p.y = _home.y + bob;
        transform.position = p;
        if (!IsDisabled && _glow <= 0.01f)   // 소등이 끝나면 다시 켠다
        {
            ApplyLook(PatternGuideHelper.Breakable, ActiveGlow);
            if (_pips != null) { _pips.Set(0); _pips.Show(true); }
        }
    }

    private void OnDestroy() => s_live.Remove(this);

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>제단 네 방위에 조각이 없으면 띄운다(이미 있으면 그대로). 심연에서 솟아오른다. 새로 띄웠으면 true.</summary>
    public static bool EnsureSpawned(Vector3 center, float distance)
    {
        s_live.RemoveAll(s => s == null);
        if (s_live.Count > 0) return false;
        for (int i = 0; i < 4; i++)
        {
            // 북동 · 남동 · 남서 · 북서 — 시계 방향 순서(광선 순서 = 예고)
            Vector3 dir = Quaternion.Euler(0f, 45f + 90f * i, 0f) * Vector3.forward;
            s_live.Add(Create(center + dir * distance + Vector3.up * FloatHeight, center));
        }
        return true;
    }

    /// <summary>조각을 모두 거둔다 — T3 · 전투 종료.</summary>
    public static void ClearAll()
    {
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] != null) s_live[i].Shatter();
        s_live.Clear();
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsDisabled) return;
        if (Time.time - _lastHitTime < HitInterval) return;
        _lastHitTime = Time.time;
        _hits++;
        if (_pips != null) _pips.Set(_hits);
        Vector3 at = transform.position + Vector3.up * (Height * 0.5f);
        LichVfx.Play(LichVfxSlot.SealStoneBurst, at, Quaternion.identity, 0.4f);
        LichSfx.Play(LichSfxSlot.SealStoneHit, at);
        if (_hits < HitsToDisable) return;

        _hits          = 0;
        _disabledUntil = Time.time + DisableSeconds;
        ApplyLook(OffColor, 0f);
        if (_pips != null) _pips.Show(false);
        LichVfx.Play(LichVfxSlot.WardBreak, at, Quaternion.identity, 0.5f);
        UI_BossBark.Show("봉인 조각 소등 — 5초", BossBarkType.PatternAnnounce);
    }

    /// <summary>광선 직전 — 조각이 달아오른다.</summary>
    public void Charge()
    {
        if (IsDisabled) return;
        ApplyLook(PatternGuideHelper.Breakable, ChargeGlow);
    }

    /// <summary>광선을 쏜 뒤 — 평소 밝기로.</summary>
    public void Settle()
    {
        if (IsDisabled) return;
        ApplyLook(PatternGuideHelper.Breakable, ActiveGlow);
    }

    // ── Private Methods ───────────────────────────────────────────
    private static LichSealShard Create(Vector3 home, Vector3 center)
    {
        var go = new GameObject("LichSealShard");
        go.transform.position = home - Vector3.up * RiseDepth;
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = Radius;
        col.height = Height;
        col.center = Vector3.up * (Height * 0.5f);

        var shard = go.AddComponent<LichSealShard>();
        shard._home = home;

        Vector3 inward = center - home;
        inward.y = 0f;
        var rot = inward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(inward) : Quaternion.identity;
        shard._model = LichVfx.InstantiateModel(LichVfxSlot.SealStoneModel, go.transform.position, rot, go.transform);
        if (shard._model != null)
        {
            shard._model.transform.localScale *= ModelScale;
            shard._renderers = shard._model.GetComponentsInChildren<Renderer>(true);
            DissolveEffect.PlayAppear(shard._model, RiseSeconds);   // 솟는 동안 디졸브로 드러난다(10-03)
        }
        else
        {
            shard._fallback = PatternGuideHelper.Pillar(go.transform.position, Radius * 0.7f, Height, PatternGuideHelper.Breakable);
            shard._fallback.transform.SetParent(go.transform, true);
        }
        shard.ApplyLook(PatternGuideHelper.Breakable, ActiveGlow);
        shard._pips = LichHitPips.Create(go.transform, Height + PipsLift, HitsToDisable);
        shard.RiseAsync(shard.destroyCancellationToken).Forget();
        return shard;
    }

    private async UniTaskVoid RiseAsync(CancellationToken ct)
    {
        Vector3 from = transform.position;
        LichVfx.Play(LichVfxSlot.SealBurst, _home, Quaternion.identity, 0.6f);
        float t = 0f;
        try
        {
            while (t < RiseSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / RiseSeconds);
                float e = 1f - (1f - k) * (1f - k);
                transform.position = Vector3.LerpUnclamped(from, _home, e);
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { }
        _rising = false;
    }

    private void Shatter()
    {
        Vector3 at = transform.position + Vector3.up * (Height * 0.5f);
        LichVfx.Play(LichVfxSlot.SealStoneBurst, at, Quaternion.identity, 1.2f);
        // 디졸브로 스러진 뒤 파괴(10-03 — 툭 꺼지지 않게). 그동안 맞지 않고 표식은 걷는다.
        _gone = true;
        if (TryGetComponent<Collider>(out var col)) col.enabled = false;
        if (_pips != null) _pips.Show(false);
        if (_fallback != null) PatternGuideHelper.SafeDestroy(ref _fallback);   // 기본 도형(투명 가이드)은 디졸브 대상이 아니다
        LichPatternUtil.DissolveAndDestroy(_model, gameObject, ShatterSeconds);
    }

    private void ApplyLook(Color color, float glow)
    {
        _glow = glow;
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
}
}
