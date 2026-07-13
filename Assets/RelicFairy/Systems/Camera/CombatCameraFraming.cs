using System.Collections.Generic;
using Cinemachine;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 전투 중 카메라 자동 줌아웃 — Returnal / Risk of Rain 2 방식.
///
/// 리서치 배경: 3인칭 로그라이트는 '몰입(낮고 가까운 카메라)'과 '다수 적 가독성(넓은 시야)'이 정면충돌한다.
/// 정석 해법은 <b>둘 중 하나를 고르는 게 아니라, 상황에 따라 카메라를 넓히는 것</b>이다.
///   · 평시(탐색)  — 베이스 구도 그대로(명조식 어깨 뒤, 몰입)
///   · 교전(적 다수) — 궤도(높이·거리)와 FOV를 넓혀 전장을 보여준다
///
/// 베이스 구도는 <b>씬/프리팹에 저작된 값</b>을 그대로 캡처해 쓴다(에셋 원본을 안 건드림).
/// 보스 시점·탑다운·패닝 등 연출이 궤도를 점유 중이면(<see cref="GameCameraController.IsOrbitOverridden"/>)
/// <b>양보하고 아무것도 하지 않는다</b> — 연출이 저장/복원하는 궤도를 덮어써서 깨뜨리지 않기 위해.
///
/// PlayerController가 런타임에 자동 부착한다(DodgePresentation과 동일 패턴).
/// </summary>
[DisallowMultipleComponent]
public class CombatCameraFraming : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int   MaxQuery = 16;   // 적 질의 상한(가독성 판단엔 이 정도면 충분)

    // ── SerializeField ────────────────────────────────────────────
    [Header("교전 판정")]
    [Tooltip("이 반경(m) 안의 적을 센다. 화면에 들어올 만한 거리로 잡는다.")]
    [SerializeField] private float combatRadius = 13f;
    [Tooltip("이 수 이상이면 최대치까지 줌아웃. 1이면 적 하나만 있어도 최대.")]
    [SerializeField] private int   fullIntensityCount = 4;

    // 기본 구도가 이미 '전장을 충분히 보여주는' 값으로 잡혀 있으므로,
    // 교전 확장은 <b>기능(가독성)이 아니라 연출</b>이다 — 전투가 붙었다는 미세한 카메라 반응만 준다.
    // 크게 잡으면 전투마다 화면이 출렁여 오히려 피로하다.
    [Header("교전 시 확장량 (연출용 — 체감되되 과하지 않게)")]
    [Tooltip("추가 높이(m).")]
    [SerializeField] private float extraHeight = 0.6f;
    [Tooltip("추가 거리(m). 기본 거리 대비 약 10% — 눈에 띄되 시야가 확 바뀌지는 않는 정도.")]
    [SerializeField] private float extraRadius = 0.8f;
    [Tooltip("추가 화각(도). 4도면 '살짝 뒤로 빠지는' 반응이 확실히 느껴진다.")]
    [SerializeField] private float extraFov = 4f;

    [Header("보간")]
    [Tooltip("넓힐 때 속도. 전투가 붙는 순간 카메라가 '반응'한다는 느낌이 나도록 빠르게.")]
    [SerializeField] private float zoomOutSpeed = 2.5f;
    [Tooltip("좁힐 때 속도. 천천히 복귀해 여운을 남긴다(급복귀는 멀미를 유발).")]
    [SerializeField] private float zoomInSpeed = 0.8f;
    [Tooltip("적 질의 주기(초). 매 프레임 물리 질의를 돌리지 않는다.")]
    [SerializeField] private float queryInterval = 0.25f;

    // ── Private ───────────────────────────────────────────────────
    private PlayerController     _player;
    private CinemachineFreeLook  _cam;

    // 저작된 베이스 구도(씬/프리팹 값) — 여기에 확장량을 더해 목표를 만든다.
    private CinemachineFreeLook.Orbit[] _baseOrbits;
    private float _baseFov;
    private bool  _captured;

    private readonly List<MonsterBase> _buffer = new(MaxQuery);
    private float _queryTimer;
    private float _intensity;         // 0=평시, 1=최대 교전 (현재값 — 매 프레임 목표로 수렴)
    private float _targetIntensity;   // 질의 주기마다 갱신되는 목표값

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake() => _player = GetComponent<PlayerController>();

    private void LateUpdate()
    {
        if (!EnsureCamera()) return;

        // 연출(보스/탑다운/패닝)이 궤도를 쓰는 중이면 양보 — 손대지 않는다.
        var gcc = GameCameraController.Instance;
        if (gcc != null && gcc.IsOrbitOverridden)
        {
            _intensity = 0f;   // 연출 종료 후 평시 구도부터 다시 시작
            return;
        }

        UpdateIntensity();
        ApplyFraming();
    }

    // ── Private Methods ───────────────────────────────────────────
    private bool EnsureCamera()
    {
        if (_player == null) return false;

        if (_cam == null)
        {
            _cam = _player.CinemachineCamera;
            if (_cam == null) return false;
            _captured = false;   // 카메라가 바뀌면 베이스를 다시 캡처
        }

        if (!_captured)
        {
            if (_cam.m_Orbits == null || _cam.m_Orbits.Length < 3) return false;

            _baseOrbits = new[]
            {
                _cam.m_Orbits[0],
                _cam.m_Orbits[1],
                _cam.m_Orbits[2],
            };
            _baseFov  = _cam.m_Lens.FieldOfView;   // m_CommonLens=1 이라 이 한 곳이 전 리그에 적용된다
            _captured = true;

            // 진단 — 프리팹/씬에 저작된 값이 런타임에 실제로 들어왔는지 확인.
            // 다른 무언가가 궤도를 덮어쓰고 있으면 여기 찍히는 값이 저작값과 다르다.
            Debug.Log($"[카메라] 베이스 캡처 | " +
                      $"Top H{_baseOrbits[0].m_Height}/R{_baseOrbits[0].m_Radius}  " +
                      $"Mid H{_baseOrbits[1].m_Height}/R{_baseOrbits[1].m_Radius}  " +
                      $"Bot H{_baseOrbits[2].m_Height}/R{_baseOrbits[2].m_Radius}  FOV {_baseFov}");
        }
        return true;
    }

    // 주변 적 수 → 목표 강도. 넓힐 땐 빠르게, 좁힐 땐 천천히(급격한 복귀는 멀미를 유발).
    private void UpdateIntensity()
    {
        _queryTimer -= Time.unscaledDeltaTime;

        if (_queryTimer <= 0f)
        {
            _queryTimer = queryInterval;

            int n = CombatQuery.GetNearbyEnemies(transform.position, combatRadius,
                                                 gameObject, MaxQuery, _buffer);
            _targetIntensity = fullIntensityCount > 0
                ? Mathf.Clamp01((float)n / fullIntensityCount)
                : (n > 0 ? 1f : 0f);
        }

        // 슬로모(저스트 회피) 중에도 카메라는 실제 시간 기준으로 반응해야 한다.
        float speed = _targetIntensity > _intensity ? zoomOutSpeed : zoomInSpeed;
        _intensity = Mathf.MoveTowards(_intensity, _targetIntensity, speed * Time.unscaledDeltaTime);
    }

    private void ApplyFraming()
    {
        float h = extraHeight * _intensity;
        float r = extraRadius * _intensity;

        for (int i = 0; i < 3; i++)
        {
            _cam.m_Orbits[i] = new CinemachineFreeLook.Orbit
            {
                m_Height = _baseOrbits[i].m_Height + h,
                m_Radius = _baseOrbits[i].m_Radius + r,
            };
        }

        _cam.m_Lens.FieldOfView = _baseFov + extraFov * _intensity;
    }
}
