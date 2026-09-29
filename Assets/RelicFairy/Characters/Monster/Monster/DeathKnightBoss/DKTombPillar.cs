using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 원탁의 무덤(2페이지 간판 KL-S)의 무덤 기둥 — 체력이 다하면 부서지고, 부서진 자리에 안전지대가 생긴다.
///
/// 영혼 기둥(<see cref="DKSoulPillar"/>)은 씬에 놓인 기둥 오브젝트를 이름으로 찾아 붙지만, 무덤 기둥은 스스로 선다(이름 검색 없음).
/// 몬스터가 아니다 — <see cref="IDamageable"/> + MonsterHit 레이어라 근접 판정 · 투사체 · 조준 보정에 그대로 잡힌다(리치 봉인석과 같은 방식).
/// 받은 피해는 기사에게 넘기지 않는다(기사는 채널링 동안 무적). 청록 = 칠 수 있음(예고 색 규약).
/// </summary>
public sealed class DKTombPillar : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string HitLayerName = "MonsterHit";
    private const float  FlashSeconds = 0.08f;
    private const float  BaseRingPad  = 0.6f;

    // ── Private ───────────────────────────────────────────────────
    private float      _hp;
    private float      _height;
    private GameObject _visual;
    private GameObject _baseRing;
    private GameObject _vfx;
    private GameObject _breakVfxPrefab;
    private float      _breakVfxScale;
    private AudioClip  _breakSfx;
    private Action<DKTombPillar> _onBroken;

    // ── Properties ────────────────────────────────────────────────
    public bool IsBroken { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDestroy()
    {
        // 기둥 · 바닥 표식은 자식이라 함께 사라진다. 풀 이펙트만 돌려준다.
        if (_vfx != null)
        {
            _vfx.transform.SetParent(null, true);
            BossEffectPool.Release(_vfx);
            _vfx = null;
        }
    }

    // ── Public Methods ────────────────────────────────────────────
    public static DKTombPillar Create(Vector3 groundPos, float hp, float radius, float height,
                                      GameObject vfxPrefab, float vfxScale,
                                      GameObject breakVfxPrefab, float breakVfxScale, AudioClip breakSfx,
                                      Action<DKTombPillar> onBroken)
    {
        var go = new GameObject("DKTombPillar");
        go.transform.position = groundPos;
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = radius;
        col.height = height;
        col.center = Vector3.up * (height * 0.5f);

        var pillar = go.AddComponent<DKTombPillar>();
        pillar._hp             = Mathf.Max(1f, hp);
        pillar._height         = height;
        pillar._breakVfxPrefab = breakVfxPrefab;
        pillar._breakVfxScale  = breakVfxScale;
        pillar._breakSfx       = breakSfx;
        pillar._onBroken       = onBroken;

        pillar._visual = PatternGuideHelper.Pillar(groundPos, radius * 0.85f, height, PatternGuideHelper.Breakable);
        pillar._visual.transform.SetParent(go.transform, true);
        pillar._baseRing = PatternGuideHelper.Disc(groundPos, radius + BaseRingPad, PatternGuideHelper.Breakable);
        pillar._baseRing.transform.SetParent(go.transform, true);

        if (vfxPrefab != null)
        {
            pillar._vfx = BossEffectPool.Spawn(vfxPrefab, groundPos, Quaternion.identity);
            if (pillar._vfx != null) pillar._vfx.transform.localScale = Vector3.one * vfxScale;
        }
        return pillar;
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsBroken || amount <= 0f) return;
        _hp -= amount;
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * (_height * 0.85f), amount, isCrit);

        if (_hp <= 0f)
        {
            Break();
            return;
        }
        FlashAsync(destroyCancellationToken).Forget();
    }

    /// <summary>패턴이 끝났다 — 부수지 않고 치운다.</summary>
    public void Dismiss()
    {
        if (this != null) Destroy(gameObject);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Break()
    {
        IsBroken = true;
        Vector3 pos = transform.position;
        if (_breakVfxPrefab != null)
        {
            var fx = BossEffectPool.SpawnOneShot(_breakVfxPrefab, pos, Quaternion.identity, fallbackLifetime: 3f);
            if (fx != null) fx.transform.localScale = Vector3.one * _breakVfxScale;
        }
        if (_breakSfx != null) Managers.Sound?.PlayEffectAt(_breakSfx, pos);
        _onBroken?.Invoke(this);
        Destroy(gameObject);
    }

    private async UniTaskVoid FlashAsync(CancellationToken ct)
    {
        PatternGuideHelper.SetColor(_visual, Color.white);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(FlashSeconds), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!IsBroken) PatternGuideHelper.SetColor(_visual, PatternGuideHelper.Breakable);
    }
}
}
