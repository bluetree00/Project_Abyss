using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 히트 시 URP Volume Override 펄스 서비스.
/// 블로그 ③ 크로매틱 + ⑤ 모션 블러 + Bloom(⑧ Flash 번쩍임 시너지).
/// ⑦ Vignette는 UI Canvas_Overlay로 이관 — UI 가독성 보존.
///
/// DDOL 호스트 자동 생성 (HitFeelService 패턴). Volume / VolumeProfile도 런타임 코드 생성 —
/// 에셋 파일 0개, 기존 GameVolumeProfile.asset 무간섭.
///
/// Time.timeScale (Hit-Stop)과 독립적으로 동작하도록 unscaledDeltaTime 사용.
/// </summary>
public static class VolumePulseService
{
    // ── Constants ─────────────────────────────────────────────────
    private const float PeakNormal         = 0.35f;
    private const float PeakCritical       = 0.75f;
    private const float DurationNormal     = 0.12f;
    private const float DurationCritical   = 0.20f;

    // Override 피크 값 — Volume weight 1.0 도달 시의 최대 강도
    private const float ChromaticIntensity = 0.8f;
    private const float MotionBlurIntensity= 0.5f;
    private const float BloomIntensity     = 2.0f;
    private const float BloomThreshold     = 0.9f;

    private const float VolumePriority     = 10f; // ambient Volume 위에 얹되, 긴 쿨다운 연출(보스 연출 등) 아래

    // ── Static ────────────────────────────────────────────────────
    private class Host : MonoBehaviour { }

    private static Host                 _host;
    private static Volume               _volume;
    private static VolumeProfile        _profile;
    private static ChromaticAberration  _chromatic;
    private static MotionBlur           _motionBlur;
    private static Bloom                _bloom;

    private static CancellationTokenSource _pulseCts;
    private static AnimationCurve          _pulseCurve;
    private static bool                    _initialized;

    // ── Bootstrap ─────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        EnsureHost();
        BuildVolume();
        BuildCurve();

        HitFeedbackService.OnHit += OnHit;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>외부에서 수동 펄스 (디버깅 / 크리티컬 이외 커스텀 연출).</summary>
    public static void Pulse(float peak = PeakNormal, float duration = DurationNormal)
    {
        if (!_initialized) Init();
        StartPulse(peak, duration);
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void EnsureHost()
    {
        var go = new GameObject("[VolumePulseHost]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();
    }

    private static void BuildVolume()
    {
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "HitPulseVolumeProfile (Runtime)";

        // ③ Chromatic Aberration
        _chromatic = _profile.Add<ChromaticAberration>(overrides: true);
        _chromatic.active = true;
        _chromatic.intensity.overrideState = true;
        _chromatic.intensity.value = ChromaticIntensity;

        // ⑤ Motion Blur
        _motionBlur = _profile.Add<MotionBlur>(overrides: true);
        _motionBlur.active = true;
        _motionBlur.intensity.overrideState = true;
        _motionBlur.intensity.value = MotionBlurIntensity;

        // Bloom — ⑧ VictimHitFeedback의 Emission Flash 시너지
        _bloom = _profile.Add<Bloom>(overrides: true);
        _bloom.active = true;
        _bloom.intensity.overrideState = true;
        _bloom.intensity.value = BloomIntensity;
        _bloom.threshold.overrideState = true;
        _bloom.threshold.value = BloomThreshold;

        var volumeGo = new GameObject("[HitPulseVolume]");
        volumeGo.transform.SetParent(_host.transform, worldPositionStays: false);

        _volume          = volumeGo.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight   = 0f;
        _volume.sharedProfile = _profile;
    }

    private static void BuildCurve()
    {
        // 블로그 ② 감쇠 그래프 — 초반 급격 → 서서히 감쇠
        _pulseCurve = new AnimationCurve(
            new Keyframe(0f,   0f),
            new Keyframe(0.15f, 1f),
            new Keyframe(1f,   0f)
        );
    }

    // ── Event Handler ─────────────────────────────────────────────
    private static void OnHit(HitInfo info)
    {
        float peak     = info.IsCritical ? PeakCritical     : PeakNormal;
        float duration = info.IsCritical ? DurationCritical : DurationNormal;
        StartPulse(peak, duration);
    }

    private static void StartPulse(float peak, float duration)
    {
        if (_volume == null || _host == null) return;

        _pulseCts?.Cancel();
        _pulseCts?.Dispose();
        _pulseCts = new CancellationTokenSource();

        _ = PulseRoutineAsync(peak, duration, _pulseCts.Token);
    }

    private static async UniTaskVoid PulseRoutineAsync(float peak, float duration, CancellationToken ct)
    {
        if (duration <= 0f) return;

        float t = 0f;
        try
        {
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();

                float n = Mathf.Clamp01(t / duration);
                float w = _pulseCurve.Evaluate(n) * peak;
                if (_volume != null) _volume.weight = w;

                // Hit-Stop(timeScale 변경)과 독립적으로 펄스 진행
                t += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        finally
        {
            if (_volume != null) _volume.weight = 0f;
        }
    }
}
