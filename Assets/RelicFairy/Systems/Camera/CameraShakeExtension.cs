using Cinemachine;
using UnityEngine;

/// <summary>
/// 런타임 부착형 카메라 쉐이크. CinemachineExtension 으로 활성 vcam 파이프라인의 Finalize 단계에서
/// OrientationCorrection 을 가산한다(= Cinemachine Impulse Listener 와 동일한 합성 지점).
/// 따라서 CinemachineBrain 의 LateUpdate 덮어쓰기에 지워지지 않는다.
///
/// HitFeelService 가 <see cref="AddTrauma"/> 로 구동. trauma 는 시간에 따라 감쇠하고 trauma² 로 진폭을 매핑한다.
/// trauma == 0 이면 즉시 return → 보정 0 → 기존 카메라 거동과 완전 동일(strict no-op).
/// </summary>
[AddComponentMenu("")]
public class CameraShakeExtension : CinemachineExtension
{
    private float _trauma;            // 0..1
    private float _maxAngle = 4f;     // 최대 회전 진폭(도)
    private float _frequency = 24f;   // Perlin 샘플 주파수
    private float _decayPerSecond = 4f;

    /// <summary>쉐이크 트라우마를 추가(기존 값과 max). amount/maxAngle/frequency/decay 동시 설정.</summary>
    public void AddTrauma(float amount, float maxAngle, float frequency, float decayPerSecond)
    {
        _maxAngle       = maxAngle;
        _frequency      = frequency;
        _decayPerSecond = Mathf.Max(0.01f, decayPerSecond);
        _trauma         = Mathf.Clamp01(Mathf.Max(_trauma, amount));
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (_trauma <= 0f) return;   // strict no-op — 휴지 시 카메라 미관여

        // 완전 정지(Pause = timeScale 0)일 때만 쉐이크 동결 — 감쇠/보정 모두 보류해 일시정지 중 화면 흔들림 0.
        // 히트스톱(0.1)·슬로모(0.25) 등 저timeScale은 동결 대상 아님(아래 unscaled 감쇠로 흔들림 지속, 의도).
        if (Time.timeScale <= 0f) return;

        // 감쇠는 unscaledDeltaTime — 저timeScale(히트스톱/슬로모)에서도 실시간으로 줄어 trauma 고착 방지.
        // deltaTime > 0f 가드는 에디터 비재생 프레임(deltaTime=-1) 보호용으로 유지.
        if (deltaTime > 0f)
            _trauma = Mathf.Max(0f, _trauma - _decayPerSecond * Time.unscaledDeltaTime);

        float shake = _trauma * _trauma;                 // trauma²
        float t = Time.unscaledTime * _frequency;        // 히트스톱(timeScale↓) 중에도 흔들리도록 unscaled
        float pitch = (Mathf.PerlinNoise(0f,     t) * 2f - 1f) * _maxAngle * shake;
        float yaw   = (Mathf.PerlinNoise(11.3f,  t) * 2f - 1f) * _maxAngle * shake;
        float roll  = (Mathf.PerlinNoise(23.7f,  t) * 2f - 1f) * _maxAngle * 0.5f * shake;

        state.OrientationCorrection *= Quaternion.Euler(pitch, yaw, roll);
    }
}
