using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 무적 표현(10-29 사용자 「무적이면 무적 이펙트나 표현이 있어야 한다 — 기사도 그렇고」) — 보스 공용.
///   · 무적(<see cref="MonsterBase.IsDamageImmuneNow"/>)이 켜지는 순간 테두리가 한 번 또렷하게 번쩍 → 무적 동안 옅은 테두리 빛이 숨 쉬듯 · 꺼지면 0.25초에 걷힌다.
///   · 무적 중 맞히면 <see cref="Blocked"/> — 테두리 짧게 번쩍 + 맑은 막힘 소리(피해 숫자 대신).
/// 테두리 = 몬스터 외곽선 셰이더(_HitFlash · _HitFlashColor — 맞을 때 깜빡임과 같은 값). 무적이 아닐 땐 손대지 않는다(맞음 깜빡임은 VictimHitFeedback 몫).
/// 이펙트 절제 — 새 이펙트 없이 외곽선만 쓴다. <see cref="MonsterBase"/>가 보스 초기화 때 붙인다.
/// </summary>
public sealed class BossImmunityLook : MonoBehaviour
{
    // ── Constants ──
    private static readonly int   HitFlashId      = Shader.PropertyToID("_HitFlash");
    private static readonly int   HitFlashColorId = Shader.PropertyToID("_HitFlashColor");
    private static readonly Color ImmuneColor     = new(0.62f, 0.78f, 1f, 1f);   // 결계 빛(차가운 흰 파랑) — 맞음 깜빡임과 갈리게
    private const float HoldGlow     = 0.35f;
    private const float BreatheAmp   = 0.08f;
    private const float BreatheSpeed = 3f;
    private const float OnFlash      = 1f;     // 켜지는 순간
    private const float OnSettle     = 0.5f;   // 번쩍 → 옅은 빛까지
    private const float OffFade      = 0.25f;
    private const float BlockedPeak  = 0.9f;
    private const float BlockedDecay = 0.15f;
    private const float BlockedSfxGap    = 0.12f;
    private const float BlockedSfxVolume = 0.45f;
    private const float BlockedSfxPitch  = 1.7f;   // 맞는 소리를 높여 「틱」 — 막혔다

    // ── Private ──
    private MonsterBase _monster;
    private Renderer[]  _renderers;
    private MaterialPropertyBlock _mpb;
    private bool  _immune;
    private float _since;
    private float _offAt     = -1f;
    private float _blockedAt = float.NegativeInfinity;
    private float _lastBlockedSfx = float.NegativeInfinity;

    // ── Lifecycle ──
    private void Awake()
    {
        _monster = GetComponent<MonsterBase>();
        _mpb = new MaterialPropertyBlock();
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
        _renderers = list.ToArray();
    }

    private void Update()
    {
        if (_monster == null) return;
        bool now = !_monster.IsDead && _monster.IsDamageImmuneNow;
        if (now != _immune)
        {
            _immune = now;
            if (now) { _since = Time.time; _offAt = -1f; }
            else _offAt = Time.time;
        }

        float glow;
        if (_immune)
        {
            float t = Time.time - _since;
            glow = t < OnSettle ? Mathf.Lerp(OnFlash, HoldGlow, t / OnSettle) : HoldGlow + Mathf.Sin(t * BreatheSpeed) * BreatheAmp;
        }
        else if (_offAt >= 0f)
        {
            float t = (Time.time - _offAt) / OffFade;
            if (t >= 1f) { _offAt = -1f; Apply(0f); return; }
            glow = Mathf.Lerp(HoldGlow, 0f, t);
        }
        else return;   // 무적이 아니면 손대지 않는다

        float b = (Time.time - _blockedAt) / BlockedDecay;
        if (b < 1f) glow = Mathf.Max(glow, Mathf.Lerp(BlockedPeak, glow, b));
        Apply(glow);
    }

    // ── Public Methods ──
    /// <summary>무적 중 맞았다 — 테두리 짧게 번쩍 + 막힘 소리(<see cref="MonsterBase.NotifyBlockedHit"/>).</summary>
    public void Blocked(Vector3 hitPoint)
    {
        _blockedAt = Time.time;
        if (Time.time - _lastBlockedSfx < BlockedSfxGap) return;
        _lastBlockedSfx = Time.time;
        Managers.Sound?.PlayEffectAtAsync(SoundKey.Sfx.MonsterHit1, hitPoint, BlockedSfxVolume, BlockedSfxPitch).Forget();
    }

    // ── Private Methods ──
    private void Apply(float glow)
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HitFlashId, glow);
            _mpb.SetColor(HitFlashColorId, ImmuneColor);
            r.SetPropertyBlock(_mpb);
        }
    }
}
}
