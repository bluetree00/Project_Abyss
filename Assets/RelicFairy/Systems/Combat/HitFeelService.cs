using Cinemachine;
using UnityEngine;

/// <summary>
/// 타격감 통합 서비스 — Hit Stop + Camera Shake.
/// 첫 호출 시 자동으로 DDOL 호스트 GameObject 생성.
/// 카메라 쉐이크는 활성 vcam 에 런타임 부착되는 <see cref="CameraShakeExtension"/> 로 합성된다
/// (Camera.main.transform 직접 조작은 CinemachineBrain 에 덮어쓰여 inert → 폐기).
/// </summary>
public static class HitFeelService
{
    private class Host : MonoBehaviour { }

    private static Host       _host;
    private static CameraShakeExtension _shakeExt;
    private static Coroutine  _stopCo;
    private static readonly object _hitStopOwner = new object();   // TimeScaleArbiter 요청 키
    private static float      _killFreezeUntil;   // 막타 히트스톱 보호 윈도(unscaledTime 기준)
    private static float      _hitStopUntil;      // 진행 중 일반 히트스톱 종료 시각(unscaledTime 기준)

    // 도메인 리로드 OFF 2회차 잔류 방지 — 절대시각(unscaledTime 기준) 필드가 0부터 재시작하는
    // unscaledTime 과 어긋나 HitStop 영구 차단되는 문제 차단(TimeScaleArbiter.ResetStatics 와 동일 패턴).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _host = null;
        _shakeExt = null;
        _stopCo = null;            // 진행 중 Coroutine 핸들 명시 끊기(PlayerLoop 정지로 잔존하진 않으나 방어)
        _killFreezeUntil = 0f;
        _hitStopUntil = 0f;
    }

    // 데미지 비례 히트스톱 노브 — 정지 길이만 데미지에 비례시키고 상한으로 클램프(과한 정지=답답 방지).
    // 깊이(timeScale)·셰이크는 앵커 고정(아래 Light/Crit 값)이며 이번 PR에서 미변경.
    private const float StopBase        = 0.025f;  // 미세 타격의 기준 길이
    private const float StopPerDamage   = 0.0007f; // 데미지 1당 추가 초
    private const float StopMin         = 0.03f;   // 약타 하한
    private const float StopMax         = 0.09f;   // 상한(답답 방지)
    private const float CritStopMult    = 1.6f;    // 크리 길이 배수
    private const float CritStopMax     = 0.11f;   // 크리 상한
    private const float LightStopScale  = 0.1f;    // 비크리 깊이 앵커(기존 Light)
    private const float CritStopScale   = 0.02f;   // 크리 깊이 앵커(기존 Crit)

    // amplitude(m) → trauma 매핑 기준(기존 호출부 진폭 0.04~0.18 을 0~1 로 정규화).
    private const float TraumaPerAmplitude = 1f / 0.18f;
    private const float ShakeMaxAngle      = 4f;
    private const float ShakeFrequency     = 24f;

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>일정 시간 동안 timeScale을 강제 조정한 후 복귀.
    /// 막타(킬) 히트스톱 보호 윈도 동안에는 일반 히트스톱이 덮어쓰지 않는다.</summary>
    public static void HitStop(float scale = 0.05f, float duration = 0.06f)
    {
        if (Time.unscaledTime < _killFreezeUntil) return;   // 킬 freeze 보호 중 → 무시
        float end = Time.unscaledTime + duration;
        // 다단/동시 히트 튐 방지 — 진행 중 스톱이 더 길게 끝나면 새 (짧은) 스톱은 무시(더 긴 쪽 유지).
        if (_stopCo != null && end <= _hitStopUntil) return;
        EnsureHost();
        _hitStopUntil = end;
        if (_stopCo != null) _host.StopCoroutine(_stopCo);
        _stopCo = _host.StartCoroutine(HitStopRoutine(scale, duration));
    }

    /// <summary>막타(처치) 전용 히트스톱 — duration 동안 일반 HitStop 이 끼어들지 못하게 보호.</summary>
    public static void KillImpact(float scale, float duration)
    {
        if (duration <= 0f) return;   // config 0 = off
        EnsureHost();
        _killFreezeUntil = Time.unscaledTime + duration;
        if (_stopCo != null) _host.StopCoroutine(_stopCo);
        _stopCo = _host.StartCoroutine(HitStopRoutine(scale, duration));
    }

    /// <summary>카메라 흔들기. amplitude=흔들 강도(기존 호출 호환), duration=지속(초).</summary>
    public static void CameraShake(float amplitude = 0.08f, float duration = 0.12f)
    {
        if (!EnsureShake()) return;
        float trauma = Mathf.Clamp01(amplitude * TraumaPerAmplitude);
        float decay  = 1f / Mathf.Max(0.05f, duration);
        _shakeExt.AddTrauma(trauma, ShakeMaxAngle, ShakeFrequency, decay);
    }

    /// <summary>데미지 비례 히트 피드백 — 정지 길이를 데미지에 비례시키되 상한으로 클램프.
    /// 깊이(timeScale)·셰이크는 비크리/크리 앵커 고정(기존 Light/Crit 체감 유지).</summary>
    public static void Hit(float damage, bool isCritical)
    {
        if (isCritical)
        {
            HitStop(CritStopScale, CritStopDuration(damage));
            CameraShake(0.18f, 0.18f);
        }
        else
        {
            HitStop(LightStopScale, HitStopDuration(damage));
            CameraShake(0.04f, 0.06f);
        }
    }

    /// <summary>일반 히트 피드백 — 약한 hit stop + 작은 shake.</summary>
    public static void Light()  { HitStop(0.1f,  0.04f); CameraShake(0.04f, 0.06f); }
    /// <summary>강한 히트 (크리티컬 등) — 더 깊은 stop + 큰 shake.</summary>
    public static void Heavy()  { HitStop(0.05f, 0.08f); CameraShake(0.12f, 0.14f); }
    /// <summary>크리티컬 전용.</summary>
    public static void Crit()   { HitStop(0.02f, 0.10f); CameraShake(0.18f, 0.18f); }

    // ── Private ─────────────────────────────────────────────────────
    /// <summary>비크리 정지 길이 — 데미지 비례 후 [StopMin, StopMax] 클램프.</summary>
    private static float HitStopDuration(float damage)
        => Mathf.Clamp(StopBase + Mathf.Max(0f, damage) * StopPerDamage, StopMin, StopMax);

    /// <summary>크리 정지 길이 — 비례 길이에 배수 적용 후 [StopMin, CritStopMax] 클램프.</summary>
    private static float CritStopDuration(float damage)
        => Mathf.Clamp((StopBase + Mathf.Max(0f, damage) * StopPerDamage) * CritStopMult, StopMin, CritStopMax);

    private static void EnsureHost()
    {
        if (_host != null) return;
        var go = new GameObject("[HitFeelHost]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();
    }

    /// <summary>활성 vcam 을 찾아 CameraShakeExtension 부착(1회). vcam 부재/파괴 시 재탐색.</summary>
    private static bool EnsureShake()
    {
        if (_shakeExt != null) return true;

        CinemachineVirtualCameraBase vcam = null;
        var cam = Camera.main;
        if (cam != null && cam.TryGetComponent<CinemachineBrain>(out var brain))
            vcam = brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
        if (vcam == null)
            vcam = Object.FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
        if (vcam == null) return false;

        if (!vcam.TryGetComponent(out _shakeExt))
            _shakeExt = vcam.gameObject.AddComponent<CameraShakeExtension>();
        return _shakeExt != null;
    }

    private static System.Collections.IEnumerator HitStopRoutine(float scale, float duration)
    {
        // 자기 히트스톱 요청만 Acquire/Release — 일시정지/슬로모 owner 가 남아있으면 그 값 유지(무조건 1f 복원 폐기).
        TimeScaleArbiter.Acquire(_hitStopOwner, scale, TimeScaleArbiter.Priority.HitStop);
        yield return new WaitForSecondsRealtime(duration);
        TimeScaleArbiter.Release(_hitStopOwner);
        _stopCo = null;
    }
}
