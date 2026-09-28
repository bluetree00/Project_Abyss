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

    // ── 방향성 펀치 ─────────────────────────────────────────────────────
    // 무방향 트라우마와 '같은 합성 지점'에 얹는다. 별도 Impulse 시스템을 두면
    // 두 시스템이 각자 카메라를 흔들어 이중 흔들림이 되므로, 소유자를 이 확장 하나로 유지한다.
    private Vector3 _punchDir;        // 월드 기준 타격 방향(정규화)
    private float   _punchAmount;     // 0..1
    private float   _punchMaxAngle = 3f;
    private float   _punchDecay = 8f;

    // FreeLook은 리그 카메라 3개를 매 프레임 갱신하며 리그마다 이 콜백을 부른다 → 감쇠를 프레임당 1회로 묶는다.
    // 안 묶으면 감쇠가 3배로 돌아 흔들림이 설계 길이의 약 1/3에서 끝났다(강공 0.35초 → 실측 0.12초, 09-25).
    private int     _lastDecayFrame = -1;

    /// <summary>쉐이크 트라우마를 추가(기존 값과 max). 새 흔들림이 지금 흔들림 이상일 때만 각도·주파수·감쇠를 바꾼다.
    /// 예전엔 세기만 max로 남기고 감쇠를 무조건 덮어써서, 보스 강공(0.45초) 도중 약한 피격 흔들림(0.1초) 한 번이면
    /// 보스 흔들림이 0.12초 만에 꺼졌다(09-25). 약한 흔들림은 큰 흔들림에 묻혀야 한다.</summary>
    public void AddTrauma(float amount, float maxAngle, float frequency, float decayPerSecond)
    {
        if (amount < _trauma) return;
        _maxAngle       = maxAngle;
        _frequency      = frequency;
        _decayPerSecond = Mathf.Max(0.01f, decayPerSecond);
        _trauma         = Mathf.Clamp01(amount);
    }

    /// <summary>방향성 펀치를 추가 — 타격이 온 방향으로 카메라를 밀어낸다.
    /// worldDir 이 0이면 무시(무방향 트라우마만 남음).</summary>
    public void AddDirectionalPunch(Vector3 worldDir, float amount, float maxAngle, float decayPerSecond)
    {
        if (worldDir.sqrMagnitude < 0.0001f) return;
        if (amount < _punchAmount) return;   // AddTrauma와 같은 규칙 — 약한 펀치가 센 펀치의 방향·감쇠를 뺏지 않는다
        _punchDir      = worldDir.normalized;
        _punchMaxAngle = maxAngle;
        _punchDecay    = Mathf.Max(0.01f, decayPerSecond);
        _punchAmount   = Mathf.Clamp01(amount);
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (_trauma <= 0f && _punchAmount <= 0f) return;   // strict no-op — 휴지 시 카메라 미관여

        // 완전 정지(Pause = timeScale 0)일 때만 쉐이크 동결 — 감쇠/보정 모두 보류해 일시정지 중 화면 흔들림 0.
        // 히트스톱(0.1)·슬로모(0.25) 등 저timeScale은 동결 대상 아님(아래 unscaled 감쇠로 흔들림 지속, 의도).
        if (Time.timeScale <= 0f) return;

        // 감쇠는 unscaledDeltaTime — 저timeScale(히트스톱/슬로모)에서도 실시간으로 줄어 trauma 고착 방지.
        // deltaTime > 0f 가드는 에디터 비재생 프레임(deltaTime=-1) 보호용으로 유지.
        if (deltaTime > 0f && _lastDecayFrame != Time.frameCount)
        {
            _lastDecayFrame = Time.frameCount;
            _trauma      = Mathf.Max(0f, _trauma      - _decayPerSecond * Time.unscaledDeltaTime);
            _punchAmount = Mathf.Max(0f, _punchAmount - _punchDecay     * Time.unscaledDeltaTime);
        }

        float pitch = 0f, yaw = 0f, roll = 0f;

        if (_trauma > 0f)
        {
            float shake = _trauma * _trauma;                 // trauma²
            float t = Time.unscaledTime * _frequency;        // 히트스톱(timeScale↓) 중에도 흔들리도록 unscaled
            pitch += (Mathf.PerlinNoise(0f,     t) * 2f - 1f) * _maxAngle * shake;
            yaw   += (Mathf.PerlinNoise(11.3f,  t) * 2f - 1f) * _maxAngle * shake;
            roll  += (Mathf.PerlinNoise(23.7f,  t) * 2f - 1f) * _maxAngle * 0.5f * shake;
        }

        // 방향성 펀치 — 타격 방향을 카메라 로컬 축으로 투영해 그 방향으로 화면을 밀어낸다.
        // 무방향 노이즈와 같은 회전 보정에 합산되므로 흔들림 소스는 끝까지 하나다.
        if (_punchAmount > 0f)
        {
            float punch = _punchAmount * _punchAmount;
            Quaternion camRot = state.CorrectedOrientation;
            float lateral  = Vector3.Dot(_punchDir, camRot * Vector3.right);
            float vertical = Vector3.Dot(_punchDir, camRot * Vector3.up);
            yaw   += lateral  * _punchMaxAngle * punch;
            pitch -= vertical * _punchMaxAngle * punch;
        }

        state.OrientationCorrection *= Quaternion.Euler(pitch, yaw, roll);
    }
}
