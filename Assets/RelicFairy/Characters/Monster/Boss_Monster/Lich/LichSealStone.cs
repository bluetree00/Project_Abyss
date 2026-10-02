using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 봉인 의식의 봉인석 — 플레이어가 세 번 치면 점화(청록 → 금색)되고, 리치에게 금빛 사슬이 이어진다.
/// 붕괴 컷신에서는 사슬이 보라로 역류하고, 빛이 꺼지고, 터진다.
///
/// 결계 해골처럼 몬스터가 아니다 — <see cref="IDamageable"/> + MonsterHit 레이어라 무기 판정·조준 보정에 그대로 잡힌다
/// (죽음의 기사 영혼 기둥과 같은 방식). 피해량은 보지 않고 <b>타격 횟수</b>만 센다.
///
/// 모양: 모델 칸(<see cref="LichVfxSlot.SealStoneModel"/>)이 있으면 룬석 메시 — 룬 발광(_EmissionColor)이 타격마다 밝아진다.
/// 없으면 기본 도형 기둥. 바닥 = 청록 테두리 원(<see cref="LichVfxSlot.SealStoneIdle"/>), 사슬 = <see cref="LichChainLine"/>.
/// </summary>
public sealed class LichSealStone : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string HitLayerName = "MonsterHit";
    private const int    HitsToIgnite = 3;
    private const float  HitInterval  = 0.15f;   // 한 번의 휘두름이 여러 번 맞아도 1타로 센다
    private const float  Radius       = 0.9f;
    private const float  Height       = 2.6f;
    private const float  ChainWidth   = 0.8f;
    private const float  AnchorHeight = 1.5f;    // 사슬이 리치 몸에 닿는 높이
    private const float  FlashSeconds = 0.08f;
    private const float  IdleGlow     = 1.2f;
    private const float  StepGlow     = 1.4f;    // 타격마다 더해지는 룬 밝기
    private const float  IgniteGlow   = 4.5f;
    private const float  FlashGlow    = 6f;
    private const float  IgniteVfxAlpha  = 0.5f;    // 점화 기둥(전설 아이템 기둥, 반복) 입자 알파 — 원본은 광선 · 빛구가 화면을 3초 넘게 희게 덮었다(10-01 f5)
    private const float  IgniteMaxScreen = 0.12f;   // 화면을 보는 입자 한 장의 크기 상한(화면 높이 비율) — 넷이 같이 켜져도 화면을 덮지 않게
    private const float  AppearSeconds   = 0.6f;    // 디졸브로 솟는다 · 스러진다(10-03 — 툭 생기고 툭 꺼졌다)
    private const float  VanishSeconds   = 0.6f;
    private const float  PipsLift        = 0.8f;    // 남은 타격 표식 — 돌 꼭대기 위(m)

    private static readonly int   EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color DarkColor  = new Color(0.12f, 0.12f, 0.16f);

    // ── Private ───────────────────────────────────────────────────
    private GameObject    _visual;       // 기본 도형(모델이 없을 때)
    private GameObject    _model;
    private Renderer[]    _modelRenderers;
    private GameObject    _marker;       // 바닥 표식(기본 도형 폴백)
    private GameObject    _base;         // 바닥 청록 원(이펙트)
    private GameObject    _igniteVfx;
    private LichChainLine _chain;
    private Transform     _anchor;
    private Action<LichSealStone> _onIgnited;
    private MaterialPropertyBlock _mpb;
    private int   _hits;
    private float _lastHitTime = float.NegativeInfinity;
    private Color _currentColor;
    private float _currentGlow;
    private Collider    _collider;
    private LichHitPips _pips;       // 남은 타격 표식(10-03)
    private bool        _leaving;    // 디졸브로 스러지는 중

    // ── Properties ────────────────────────────────────────────────
    public bool IsIgnited { get; private set; }

    private Vector3 ChainStart => transform.position + Vector3.up * Height;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDestroy()
    {
        // 사슬·이펙트는 부모가 없는 월드 오브젝트라 함께 거둔다.
        if (_chain != null) _chain.Dispose();
        _chain = null;
        LichVfx.Stop(ref _base);
        LichVfx.Stop(ref _igniteVfx);
        EnemyCompassHud.RemoveGoal(transform);
    }

    // ── Public Methods ────────────────────────────────────────────
    public static LichSealStone Create(Vector3 groundPos, Transform anchor, Action<LichSealStone> onIgnited)
    {
        var go = new GameObject("LichSealStone");
        go.transform.position = groundPos;
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = Radius;
        col.height = Height;
        col.center = Vector3.up * (Height * 0.5f);

        var stone = go.AddComponent<LichSealStone>();
        stone._anchor       = anchor;
        stone._onIgnited    = onIgnited;
        stone._currentColor = PatternGuideHelper.Breakable;
        stone._currentGlow  = IdleGlow;
        stone._collider     = col;

        // 제단 중심(리치)을 등지고 바깥을 보게 세운다.
        Vector3 outward = anchor != null ? groundPos - anchor.position : Vector3.forward;
        outward.y = 0f;
        var rot = outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward) : Quaternion.identity;

        stone._model = LichVfx.InstantiateModel(LichVfxSlot.SealStoneModel, groundPos, rot, go.transform);
        if (stone._model != null)
        {
            stone._modelRenderers = stone._model.GetComponentsInChildren<Renderer>(true);
            DissolveEffect.PlayAppear(stone._model, AppearSeconds);   // 디졸브로 솟는다(10-03) — 룬 발광은 디졸브 동안 꺼졌다 돌아온다
        }
        else
        {
            stone._visual = PatternGuideHelper.Pillar(groundPos, Radius * 0.8f, Height, PatternGuideHelper.Breakable);
            stone._visual.transform.SetParent(go.transform, true);
        }

        // 바닥 표식 — 제단 반대편에서도 어디를 쳐야 하는지 보이게
        stone._base = LichVfx.PlayLoop(LichVfxSlot.SealStoneIdle, groundPos + Vector3.up * 0.05f, Quaternion.identity);
        if (stone._base == null)
        {
            stone._marker = PatternGuideHelper.Disc(groundPos, Radius + 0.8f, PatternGuideHelper.Breakable);
            stone._marker.transform.SetParent(go.transform, true);
        }

        // 남은 타격 표식 · 화면 밖 방향(적 나침반 목표) — 게임 거리에선 룬 밝기로 몇 번 남았는지 · 어디 있는지 안 읽혔다(10-03).
        stone._pips = LichHitPips.Create(go.transform, Height + PipsLift, HitsToIgnite);
        EnemyCompassHud.AddGoal(go.transform);

        stone.ApplyLook();
        return stone;
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsIgnited) return;
        if (Time.time - _lastHitTime < HitInterval) return;
        _lastHitTime = Time.time;

        _hits++;
        if (_pips != null) _pips.Set(_hits);
        Vector3 hitPos = transform.position + Vector3.up * (Height * 0.6f);
        LichVfx.Play(LichVfxSlot.SealStoneBurst, hitPos, Quaternion.identity, 0.5f);
        LichSfx.Play(LichSfxSlot.SealStoneHit, hitPos);

        if (_hits >= HitsToIgnite)
        {
            Ignite();
            return;
        }
        _currentGlow = IdleGlow + StepGlow * _hits;   // 룬이 한 칸씩 밝아진다
        FlashAsync(destroyCancellationToken).Forget();
    }

    /// <summary>사슬 색을 바꾼다(역류 연출).</summary>
    public void SetChainColor(Color color)
    {
        if (_chain != null) _chain.SetColor(color);
    }

    /// <summary>사슬을 팽팽하게 당겨 번쩍인다.</summary>
    public void PulseChain()
    {
        if (_chain == null) return;
        _chain.Tension(1f);
        _chain.Flash(0.3f);
    }

    /// <summary>빛이 꺼진다 — 봉인이 버티지 못한다.</summary>
    public void Darken()
    {
        LichVfx.Stop(ref _igniteVfx, 0.4f);
        _currentColor = DarkColor;
        _currentGlow  = 0f;
        ApplyLook();
    }

    /// <summary>사슬만 걷어낸다 — 리치가 찢어 버린다.</summary>
    public void BreakChain()
    {
        if (_chain != null) _chain.Dispose();
        _chain = null;
    }

    /// <summary>터진다 — 섬광 후 디졸브로 스러진다.</summary>
    public void Explode()
    {
        Vector3 mid = transform.position + Vector3.up * (Height * 0.5f);
        LichVfx.Play(LichVfxSlot.SealStoneBurst, mid, Quaternion.identity, 1.6f);
        LichVfx.Play(LichVfxSlot.PhaseBurst, mid, Quaternion.identity, 0.5f);
        if (!LichVfx.Has(LichVfxSlot.SealStoneBurst))
            PatternGuideHelper.Sphere(mid, 2.2f, PatternGuideHelper.Reversed, lifetime: 0.35f);
        Dismiss();
    }

    /// <summary>디졸브로 스러진 뒤 파괴된다(10-03 — 툭 꺼지지 않게). 사슬 · 바닥 이펙트 · 표식은 바로 걷고, 그동안 맞지 않는다.</summary>
    public void Dismiss()
    {
        if (_leaving) return;
        _leaving = true;
        if (_collider != null) _collider.enabled = false;
        if (_pips != null) _pips.Show(false);
        EnemyCompassHud.RemoveGoal(transform);
        BreakChain();
        LichVfx.Stop(ref _base, 0.3f);
        LichVfx.Stop(ref _igniteVfx, 0.3f);
        PatternGuideHelper.SafeDestroy(ref _marker);
        PatternGuideHelper.SafeDestroy(ref _visual);   // 기본 도형(투명 가이드)은 디졸브 대상이 아니다
        LichPatternUtil.DissolveAndDestroy(_model, gameObject, VanishSeconds);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Ignite()
    {
        IsIgnited     = true;
        _currentColor = PatternGuideHelper.PlayerSeal;
        _currentGlow  = IgniteGlow;
        ApplyLook();

        PatternGuideHelper.SafeDestroy(ref _marker);
        LichVfx.Stop(ref _base, 0.3f);
        if (_pips != null) _pips.Show(false);
        EnemyCompassHud.RemoveGoal(transform);   // 점화 — 더는 가리키지 않는다
        _igniteVfx = LichVfx.PlayLoopTinted(LichVfxSlot.SealStoneIgnite, transform.position, Quaternion.identity, 1f,
                                            new Color(1f, 1f, 1f, IgniteVfxAlpha));
        CapBillboards(_igniteVfx, IgniteMaxScreen);

        if (_anchor != null)
        {
            _chain = LichChainLine.Create(ChainStart, _anchor.position + Vector3.up * AnchorHeight, ChainWidth, PatternGuideHelper.PlayerSeal);
            _chain.Follow(transform, Vector3.up * Height, _anchor, Vector3.up * AnchorHeight);
            _chain.Slack = 0.02f;
            _chain.Tension(1f);
            _chain.Flash(0.4f);
        }

        _onIgnited?.Invoke(this);
    }

    /// <summary>화면을 보는 입자(빌보드)의 화면 크기 상한 — 카메라 가까이 선 봉인석의 빛구가 화면을 덮지 않게.</summary>
    private static void CapBillboards(GameObject vfx, float maxScreen)
    {
        if (vfx == null) return;
        foreach (var r in vfx.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (r.renderMode == ParticleSystemRenderMode.Billboard) r.maxParticleSize = maxScreen;
    }

    private void ApplyLook() => ApplyLook(_currentColor, _currentGlow);

    private void ApplyLook(Color color, float glow)
    {
        if (_visual != null) PatternGuideHelper.SetColor(_visual, color);
        if (_modelRenderers == null) return;

        _mpb ??= new MaterialPropertyBlock();
        Color emission = color * glow;
        emission.a = 1f;
        foreach (var r in _modelRenderers)
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
        ApplyLook();
    }
}
}
