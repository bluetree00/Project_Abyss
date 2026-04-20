using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 타격감 통합 서비스 — Hit Stop + Camera Shake.
/// 첫 호출 시 자동으로 DDOL 호스트 GameObject 생성.
/// </summary>
public static class HitFeelService
{
    private class Host : MonoBehaviour { }

    private static Host       _host;
    private static Camera     _cachedCam;
    private static Vector3    _camOriginalLocalPos;
    private static Coroutine  _shakeCo;
    private static Coroutine  _stopCo;
    private static float      _baseTimeScale = 1f;

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>일정 시간 동안 timeScale을 강제 조정한 후 복귀.</summary>
    public static void HitStop(float scale = 0.05f, float duration = 0.06f)
    {
        EnsureHost();
        if (_stopCo != null) _host.StopCoroutine(_stopCo);
        _stopCo = _host.StartCoroutine(HitStopRoutine(scale, duration));
    }

    /// <summary>카메라 흔들기. amplitude=흔들 폭(m), duration=지속(초).</summary>
    public static void CameraShake(float amplitude = 0.08f, float duration = 0.12f)
    {
        EnsureHost();
        EnsureCamera();
        if (_cachedCam == null) return;

        if (_shakeCo != null) _host.StopCoroutine(_shakeCo);
        _shakeCo = _host.StartCoroutine(ShakeRoutine(amplitude, duration));
    }

    /// <summary>일반 히트 피드백 — 약한 hit stop + 작은 shake.</summary>
    public static void Light()  { HitStop(0.1f,  0.04f); CameraShake(0.04f, 0.08f); }
    /// <summary>강한 히트 (크리티컬 등) — 더 깊은 stop + 큰 shake.</summary>
    public static void Heavy()  { HitStop(0.05f, 0.08f); CameraShake(0.12f, 0.18f); }
    /// <summary>크리티컬 전용.</summary>
    public static void Crit()   { HitStop(0.02f, 0.10f); CameraShake(0.18f, 0.22f); }

    // ── Private ─────────────────────────────────────────────────────
    private static void EnsureHost()
    {
        if (_host != null) return;
        var go = new GameObject("[HitFeelHost]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();
    }

    private static void EnsureCamera()
    {
        if (_cachedCam != null) return;
        _cachedCam = Camera.main;
        if (_cachedCam != null)
            _camOriginalLocalPos = _cachedCam.transform.localPosition;
    }

    private static System.Collections.IEnumerator HitStopRoutine(float scale, float duration)
    {
        float prev = Time.timeScale;
        Time.timeScale = scale;
        // unscaled 시간으로 대기 — timeScale 영향 받지 않음
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = prev > 0f ? prev : 1f;
        _stopCo = null;
    }

    private static System.Collections.IEnumerator ShakeRoutine(float amplitude, float duration)
    {
        // 카메라가 이동하는 게임이라면 매 프레임 originalLocalPos를 갱신
        var cam = _cachedCam.transform;
        Vector3 origin = cam.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float decay = 1f - Mathf.Clamp01(t / duration);
            Vector2 r = UnityEngine.Random.insideUnitCircle * amplitude * decay;
            cam.localPosition = origin + new Vector3(r.x, r.y, 0f);
            yield return null;
        }
        cam.localPosition = origin;
        _shakeCo = null;
    }
}
