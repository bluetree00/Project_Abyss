using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
public enum LichMovementState
{
    IdleHover,       // 제자리 호버링 — 플레이어 응시
    AdvanceFloat,    // 플레이어 방향 전진
    RetreatFloat,    // 플레이어 반대 방향 후퇴
    CircleStrafe,    // 플레이어 주변 선회
    DashClose,       // 고속 돌진 접근
    AltitudeRise,    // 상승 (DeathRay·FogOfDeath 준비)
    AltitudeDescend, // 기본 고도 복귀
    Teleport,        // 순간이동 (완료 후 IdleHover로 전환)
}

/// <summary>
/// 리치 보스 공중 이동 컨트롤러.
///
/// NavMesh를 사용하지 않고 Transform을 직접 SmoothDamp로 제어한다.
/// BossPatternRunner(Layer 3)와 병렬로 동작하며,
/// 패턴 SO의 Enter()에서 RequestMovementState()로 이동 상태를 선점하고
/// SetLocked(true)로 자율 판단을 잠근다.
/// 패턴이 끝나면 SetLocked(false)로 AutoDecide를 재개한다.
/// </summary>
public class LichMovementController : MonoBehaviour
{
    [Header("부유 이동")]
    [SerializeField] private float _floatSpeed      = 2.5f;
    [SerializeField] private float _smoothTime      = 0.25f;

    [Header("선회")]
    [SerializeField] private float _strafeAngularSpeed = 50f;  // 도/초

    [Header("돌진")]
    [SerializeField] private float _dashSpeed       = 10f;
    [SerializeField] private float _dashDuration    = 0.4f;

    [Header("고도")]
    [SerializeField] private float _baseAltitude   = 2.5f;
    [SerializeField] private float _riseAltitude   = 4.5f;
    [SerializeField] private float _altSmoothTime  = 0.6f;

    [Header("호버 진동")]
    [SerializeField] private float _hoverAmplitude = 0.12f;
    [SerializeField] private float _hoverPeriod    = 2.0f;

    [Header("회전")]
    [SerializeField] private float _rotateSpeed    = 8f;

    [Header("자율 판단")]
    [SerializeField] private float _decideInterval = 0.3f;

    // ── 런타임 상태 ────────────────────────────────────────
    private LichMovementState _state        = LichMovementState.IdleHover;
    private bool              _isLocked;
    private float             _stateTimer;
    private float             _decideTimer;
    private float             _dashTimer;
    private float             _strafeAngle;
    private float             _strafeRadius = 5f;
    private Vector3           _xzVelocity;
    private float             _yVelocity;
    private float             _targetAltitude;

    // ── 의존성 ─────────────────────────────────────────────
    private Transform         _transform;
    private LichBlackboard    _bb;
    private Transform         _cachedPlayer;

    public LichMovementState CurrentState => _state;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Init(LichBlackboard bb)
    {
        _transform      = transform;
        _bb             = bb;
        _targetAltitude = _baseAltitude;
    }

    public void OnRecycled()
    {
        _state          = LichMovementState.IdleHover;
        _isLocked       = false;
        _stateTimer     = 0f;
        _decideTimer    = 0f;
        _dashTimer      = 0f;
        _xzVelocity     = Vector3.zero;
        _yVelocity      = 0f;
        _targetAltitude = _baseAltitude;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 외부 API — 패턴 SO에서 호출
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>패턴 SO의 Enter()에서 호출. 이동 상태를 선점한다.</summary>
    public void RequestMovementState(LichMovementState state)
    {
        if (_state == state) return;
        EnterState(state);
    }

    /// <summary>true: AutoDecide 잠금 (패턴 실행 중). false: 재개.</summary>
    public void SetLocked(bool locked) => _isLocked = locked;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 메인 틱 — LichMonster.Update()에서 호출
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Tick(float dt, Transform playerTarget)
    {
        if (_bb == null) return;
        _cachedPlayer = playerTarget;

        // 거리 업데이트
        _bb.DistanceToPlayer = playerTarget != null
            ? Vector3.Distance(_transform.position, playerTarget.position)
            : 999f;

        _stateTimer  += dt;
        _decideTimer += dt;

        // 자율 판단 (잠금 해제 시에만)
        if (!_isLocked && _decideTimer >= _decideInterval && playerTarget != null)
        {
            _decideTimer = 0f;
            var decided = AutoDecideMovement();
            if (decided != _state)
                EnterState(decided);
        }

        // XZ 이동
        ExecuteMovement(dt, playerTarget);

        // Y 고도 + 호버 진동 (항상 적용)
        float hover   = _hoverAmplitude * Mathf.Sin(Time.time * (Mathf.PI * 2f / _hoverPeriod));
        float targetY = _targetAltitude + hover;
        float newY    = Mathf.SmoothDamp(_transform.position.y, targetY, ref _yVelocity, _altSmoothTime);
        var p = _transform.position;
        p.y = newY;
        _transform.position = p;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Utility AI 스코어러
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private LichMovementState AutoDecideMovement()
    {
        float d = _bb.DistanceToPlayer;

        LichMovementState best      = LichMovementState.IdleHover;
        float             bestScore = float.MinValue;

        void Eval(LichMovementState s, float score)
        {
            if (score > bestScore) { bestScore = score; best = s; }
        }

        // IdleHover: 적정 거리 유지 또는 숨 고르기
        {
            float s = 50f;
            if (d > 3f && d < 8f) s += 30f;
            if (_stateTimer > 3.5f && _state == LichMovementState.CircleStrafe) s += 20f;
            Eval(LichMovementState.IdleHover, s);
        }

        // AdvanceFloat: 멀수록 전진 선호
        {
            float s = 35f + Mathf.Clamp01((d - 5f) / 10f) * 65f;
            if (_bb.IsPhase2) s += 15f;
            Eval(LichMovementState.AdvanceFloat, s);
        }

        // RetreatFloat: 너무 가까울 때 긴급 후퇴
        {
            float s = 20f;
            if (d < 2.5f)     s += 90f;
            else if (d < 4f)  s += 35f;
            if (_bb.IsPhase2) s -= 15f;
            Eval(LichMovementState.RetreatFloat, s);
        }

        // CircleStrafe: 적정 거리에서 같은 상태 오래됐을 때
        {
            float s = 25f;
            if (d > 3f && d < 7f)                              s += 35f;
            if (_stateTimer > 2.5f && _state == LichMovementState.IdleHover) s += 30f;
            Eval(LichMovementState.CircleStrafe, s);
        }

        // DashClose: 매우 멀 때만
        {
            float s = 0f;
            if (d > 10f)     s = 80f;
            else if (d > 8f) s = 40f;
            Eval(LichMovementState.DashClose, s);
        }

        // AltitudeRise / Descend / Teleport: AutoDecide에서는 선택 안 함.
        // 패턴 SO의 RequestMovementState()로만 진입.

        return best;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 상태 진입
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void EnterState(LichMovementState newState)
    {
        _state      = newState;
        _stateTimer = 0f;

        switch (newState)
        {
            case LichMovementState.AltitudeRise:
                _targetAltitude = _riseAltitude;
                break;

            case LichMovementState.AltitudeDescend:
                _targetAltitude = _baseAltitude;
                break;

            case LichMovementState.DashClose:
                _dashTimer = 0f;
                break;

            case LichMovementState.CircleStrafe:
                if (_cachedPlayer != null)
                {
                    Vector3 toMe = _transform.position - _cachedPlayer.position;
                    toMe.y = 0f;
                    _strafeRadius = Mathf.Clamp(toMe.magnitude, 3f, 8f);
                    _strafeAngle  = Mathf.Atan2(toMe.z, toMe.x) * Mathf.Rad2Deg;
                }
                break;

            case LichMovementState.Teleport:
                DoTeleport();
                break;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이동 실행
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void ExecuteMovement(float dt, Transform playerTarget)
    {
        if (playerTarget == null) return;

        switch (_state)
        {
            case LichMovementState.IdleHover:
            case LichMovementState.AltitudeRise:
            case LichMovementState.AltitudeDescend:
                SlowStop(dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.AdvanceFloat:
                MoveXZ(playerTarget.position, _floatSpeed, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.RetreatFloat:
                Vector3 away = _transform.position + FlatDir(_transform.position - playerTarget.position) * 2f;
                MoveXZ(away, _floatSpeed, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.CircleStrafe:
                _strafeAngle += _strafeAngularSpeed * dt;
                float rad = _strafeAngle * Mathf.Deg2Rad;
                Vector3 orbitTarget = playerTarget.position
                    + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _strafeRadius;
                MoveXZ(orbitTarget, _floatSpeed * 2f, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.DashClose:
                _dashTimer += dt;
                if (_dashTimer < _dashDuration)
                {
                    MoveXZ(playerTarget.position, _dashSpeed, dt);
                    RotateTowards(playerTarget.position, dt);
                }
                else
                    EnterState(LichMovementState.IdleHover);
                break;

            case LichMovementState.Teleport:
                // DoTeleport()에서 이미 처리. 이후 IdleHover로 자동 전환됨.
                break;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이동 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void MoveXZ(Vector3 worldTarget, float speed, float dt)
    {
        Vector3 cur = _transform.position;
        worldTarget.y = cur.y;
        if ((worldTarget - cur).sqrMagnitude < 0.01f)
        {
            _xzVelocity = Vector3.zero;
            return;
        }
        Vector3 newPos = Vector3.SmoothDamp(cur, worldTarget, ref _xzVelocity, _smoothTime, speed, dt);
        newPos.y = cur.y;
        _transform.position = newPos;
    }

    private void SlowStop(float dt)
    {
        // 현재 위치를 목표로 SmoothDamp → _xzVelocity가 0에 수렴하며 자연스럽게 감속
        Vector3 cur    = _transform.position;
        Vector3 newPos = Vector3.SmoothDamp(cur, cur, ref _xzVelocity, _smoothTime, float.MaxValue, dt);
        newPos.y = cur.y;
        _transform.position = newPos;
    }

    private void RotateTowards(Vector3 target, float dt)
    {
        Vector3 dir = target - _transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        _transform.rotation = Quaternion.Slerp(
            _transform.rotation,
            Quaternion.LookRotation(dir),
            _rotateSpeed * dt);
    }

    private void DoTeleport()
    {
        if (_cachedPlayer == null) return;
        float   angle  = Random.Range(0f, Mathf.PI * 2f);
        float   dist   = Random.Range(5f, 9f);
        Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        Vector3 dest   = _cachedPlayer.position + offset;
        dest.y = _targetAltitude;
        _transform.position = dest;
        _xzVelocity         = Vector3.zero;
        _yVelocity          = 0f;
        EnterState(LichMovementState.IdleHover);
    }

    private static Vector3 FlatDir(Vector3 v)
    {
        v.y = 0f;
        float mag = v.magnitude;
        return mag > 0.001f ? v / mag : Vector3.forward;
    }
}
}
