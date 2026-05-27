using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 피격자(몬스터) 측 타격 연출 컴포넌트.
/// HitFeedbackService가 IHitReceiver.OnReceiveHit으로 직접 호출한다.
///
/// 블로그 ⑧ 피격자 반응(색) + ② 화면 번쩍임(PointLight) 구현.
/// 피격 애니메이션/넉백은 GetHitState가 기존 처리 — 이 클래스는 시각 펄스만 담당.
///
/// ※ MonsterBase 수정 없음 — 프리팹에 본 컴포넌트만 부착하면 동작.
/// </summary>
[DisallowMultipleComponent]
public class VictimHitFeedback : MonoBehaviour, IHitReceiver
{
    // ── Constants ─────────────────────────────────────────────────
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string       AutoLightName   = "~HitFlashLight";

    // ── Serialized ────────────────────────────────────────────────
    [Header("프로필")]
    [SerializeField] private MonsterHitProfileSO _profile;

    [Header("Flash 대상 (비우면 자식 렌더러 자동 수집)")]
    [SerializeField] private Renderer[] _explicitRenderers;

    [Header("PointLight (비우면 자동 생성)")]
    [SerializeField] private Light _pulseLight;

    // ── Private ───────────────────────────────────────────────────
    private Renderer[]             _renderers;
    private MaterialPropertyBlock  _mpb;
    private CancellationTokenSource _flashCts;
    private CancellationTokenSource _lightCts;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        CollectRenderers();
        EnsureLight();
        _mpb = new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        CancelAll();
        RestoreMaterials();
        if (_pulseLight != null)
        {
            _pulseLight.enabled = false;
            _pulseLight.intensity = 0f;
        }
    }

    private void OnDestroy() => CancelAll();

    // ── Public Methods (IHitReceiver) ─────────────────────────────
    public void OnReceiveHit(in HitInfo info)
    {
        if (_profile == null) return;

        Color flashColor = _profile.FlashColor;
        Color lightColor = _profile.LightColor;

        PlayFlash(flashColor);
        if (_profile.UsePointLight) PlayLightPulse(lightColor);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void CollectRenderers()
    {
        if (_explicitRenderers != null && _explicitRenderers.Length > 0)
        {
            _renderers = _explicitRenderers;
            return;
        }

        var buffer = new List<Renderer>();
        var all = GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in all)
        {
            // SkinnedMesh / Mesh 만 채택. Trail/Line/Particle/Canvas 등은 배제.
            if (r is SkinnedMeshRenderer || r is MeshRenderer)
                buffer.Add(r);
        }
        _renderers = buffer.ToArray();
    }

    private void EnsureLight()
    {
        if (_pulseLight != null) return;
        if (_profile == null || !_profile.UsePointLight) return;

        var lightGo = new GameObject(AutoLightName);
        lightGo.transform.SetParent(transform, worldPositionStays: false);
        lightGo.transform.localPosition = _profile.LightLocalOffset;

        _pulseLight = lightGo.AddComponent<Light>();
        _pulseLight.type      = LightType.Point;
        _pulseLight.range     = _profile.LightRange;
        _pulseLight.intensity = 0f;
        _pulseLight.enabled   = false;
    }

    private void PlayFlash(Color flashColor)
    {
        _flashCts?.Cancel();
        _flashCts?.Dispose();
        _flashCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        _ = FlashRoutineAsync(flashColor, _flashCts.Token);
    }

    private void PlayLightPulse(Color lightColor)
    {
        if (_pulseLight == null) return;

        _lightCts?.Cancel();
        _lightCts?.Dispose();
        _lightCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        _ = LightPulseRoutineAsync(lightColor, _lightCts.Token);
    }

    private async UniTaskVoid FlashRoutineAsync(Color flashColor, CancellationToken ct)
    {
        float duration = _profile.FlashDuration;
        if (duration <= 0f || _renderers == null || _renderers.Length == 0) return;

        float boost = _profile.EmissionBoost;
        var   curve = _profile.FlashCurve;

        float t = 0f;
        try
        {
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();

                float n = Mathf.Clamp01(t / duration);
                float w = curve != null ? curve.Evaluate(n) : 1f - n;

                Color tint     = flashColor;
                tint.a         = 1f;
                Color emission = flashColor * (boost * w);

                _mpb.SetColor(BaseColorId,     Color.Lerp(Color.white, tint, w));
                _mpb.SetColor(EmissionColorId, emission);

                ApplyPropertyBlock(_mpb);

                t += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        finally
        {
            RestoreMaterials();
        }
    }

    private async UniTaskVoid LightPulseRoutineAsync(Color lightColor, CancellationToken ct)
    {
        float duration = _profile.LightDuration;
        if (duration <= 0f || _pulseLight == null) return;

        float peak  = _profile.LightIntensity;
        var   curve = _profile.LightCurve;

        _pulseLight.color   = lightColor;
        _pulseLight.range   = _profile.LightRange;
        _pulseLight.enabled = true;

        float t = 0f;
        try
        {
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();

                float n = Mathf.Clamp01(t / duration);
                float w = curve != null ? curve.Evaluate(n) : 1f - n;
                _pulseLight.intensity = peak * w;

                t += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        finally
        {
            if (_pulseLight != null)
            {
                _pulseLight.intensity = 0f;
                _pulseLight.enabled   = false;
            }
        }
    }

    private void ApplyPropertyBlock(MaterialPropertyBlock mpb)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(mpb);
        }
    }

    private void RestoreMaterials()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(null);
        }
    }

    private void CancelAll()
    {
        _flashCts?.Cancel();
        _flashCts?.Dispose();
        _flashCts = null;

        _lightCts?.Cancel();
        _lightCts?.Dispose();
        _lightCts = null;
    }
}
