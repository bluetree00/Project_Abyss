using System;
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
    AltitudeRise,    // 상승 (DarkRain 준비)
    AltitudeDescend, // 기본 고도 복귀
    Teleport,        // 순간이동 (완료 후 IdleHover로 전환)
}

/// <summary>
/// 이동 상태 하나를 정의하는 항목.
/// 조건을 충족할 때 가중치 랜덤으로 선택되며, minDuration~maxDuration 동안 유지된다.
/// </summary>
[Serializable]
public class LichMovementEntry
{
    public LichMovementState state;
    [Tooltip("최소 플레이어 거리 (m). -1 = 제한 없음")]
    public float minDist = -1f;
    [Tooltip("최대 플레이어 거리 (m). -1 = 제한 없음")]
    public float maxDist = -1f;
    [Tooltip("Phase 2 이후만 허용")]
    public bool  phase2Only;
    [Tooltip("이 상태를 유지할 최소 시간 (초)")]
    public float minDuration = 1.5f;
    [Tooltip("이 상태를 유지할 최대 시간 (초)")]
    public float maxDuration = 3.5f;
    [Tooltip("선택 가중치. 높을수록 선택 확률 증가")]
    public float weight = 1f;

    public bool Evaluate(float dist, bool isPhase2)
    {
        if (phase2Only && !isPhase2) return false;
        if (minDist >= 0f && dist < minDist) return false;
        if (maxDist >= 0f && dist > maxDist) return false;
        return true;
    }
}

/// <summary>
/// 리치 보스 공중 이동 컨트롤러.
///
/// ── 이동 선택 방식 ──────────────────────────────────────────────
///  매 틱 경쟁하는 Utility AI 스코어러 대신 패턴 방식.
///  _movementEntries 중 조건을 충족하는 항목을 가중치 랜덤으로 선택하고,
///  해당 항목의 유지 시간(minDuration~maxDuration)이 만료되면 재평가한다.
///  패턴 공격 SO는 RequestMovementState()로 이동 상태를 선점할 수 있다.
///
/// ── Dark Souls / Elden Ring UX 원칙 ─────────────────────────────
///  • 공격 후 취약 구간: 패턴 종료 직후 이동 속도 감소
///  • 전투 선호 거리: AdvanceFloat가 _preferredRange 앞에서 멈춤
/// </summary>
public class LichMovementController : MonoBehaviour
{
    [Header("부유 이동")]
    [SerializeField] private float _floatSpeed  = 2.5f;
    [SerializeField] private float _smoothTime  = 0.25f;

    [Header("선회")]
    [SerializeField] private float _strafeAngularSpeed = 50f;

    [Header("돌진")]
    [SerializeField] private float _dashSpeed    = 10f;
    [SerializeField] private float _dashDuration = 0.4f;

    [Header("고도")]
    [SerializeField] private float _baseAltitude  = 2.5f;
    [SerializeField] private float _riseAltitude  = 4.5f;
    [SerializeField] private float _altSmoothTime = 0.6f;

    [Header("호버 진동")]
    [SerializeField] private float _hoverAmplitude = 0.12f;
    [SerializeField] private float _hoverPeriod    = 2.0f;

    [Header("회전")]
    [SerializeField] private float _rotateSpeed        = 8f;
    [Tooltip("패턴 종료 후 플레이어를 향해 천천히 돌아오는 구간의 회전 속도.")]
    [SerializeField] private float _returnFaceSpeed    = 1.8f;
    [Tooltip("패턴 종료 후 느린 복귀 회전 지속 시간(초).")]
    [SerializeField] private float _returnFaceDuration = 0.9f;
    [Tooltip("IdleHover 중 시선이 틀어지는 최대 각도(도).")]
    [SerializeField] private float _idleGazeVariance   = 22f;
    [Tooltip("IdleHover 중 시선 방향 변경 간격(초).")]
    [SerializeField] private float _idleGazeInterval   = 2.5f;

    [Header("이동 패턴")]
    [Tooltip("조건에 맞는 항목 중 가중치 랜덤 선택. 유지 시간 만료 시 재평가.")]
    [SerializeField] private LichMovementEntry[] _movementEntries;

    [Header("공격 후 취약 구간 (Punish Window)")]
    [Tooltip("패턴 종료 직후 이동 둔화 지속 시간(초). 플레이어 반격 기회.")]
    [SerializeField] private float _postAttackDuration  = 1.8f;
    [Tooltip("취약 구간 중 이동 속도 배율.")]
    [SerializeField] private float _postAttackSpeedMult = 0.30f;

    [Header("전투 위치")]
    [Tooltip("AdvanceFloat가 유지하려는 전투 거리(m).")]
    [SerializeField] private float _preferredRange    = 5.5f;
    [Tooltip("IdleHover 배회 최대 반경(m).")]
    [SerializeField] private float _idleWanderRadius   = 1.8f;
    [Tooltip("IdleHover 배회 목표 갱신 간격(초).")]
    [SerializeField] private float _idleWanderInterval = 3.5f;

    [Header("벽 충돌 방지")]
    [SerializeField] private LayerMask _wallLayer;
    [SerializeField] private float _wallCheckRadius = 0.4f;

    // ── 런타임 상태 ────────────────────────────────────────
    private LichMovementState _state        = LichMovementState.IdleHover;
    private bool              _isLocked;
    private float             _dashTimer;
    private float             _strafeAngle;
    private float             _strafeRadius = 5f;
    private Vector3           _xzVelocity;
    private float             _yVelocity;
    private float             _targetAltitude;
    private float             _groundY; // 스폰 지점 Y — 고도 계산의 기준점
    private float             _returnFaceTimer;
    private float             _idleGazeAngle;
    private float             _idleGazeTimer;
    private float             _postAttackTimer;
    private Vector3           _idleWanderTarget;
    private float             _idleWanderTimer;
    private float             _movementTimer;
    private float             _movementDuration;

    // ── 의존성 ─────────────────────────────────────────────
    private Transform      _transform;
    private LichBlackboard _bb;
    private Transform      _cachedPlayer;

    public LichMovementState CurrentState => _state;

    /// <summary>스폰 지점 Y. 패턴에서 지면 높이 추정에 사용.</summary>
    public float GroundY => _groundY;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Init(LichBlackboard bb)
    {
        _transform        = transform;
        _bb               = bb;
        _groundY          = transform.position.y; // 스폰 지점 Y 기록
        _targetAltitude   = _groundY + _baseAltitude;
        _idleWanderTarget = transform.position;
        _movementDuration = 2f;
    }

    public void OnRecycled()
    {
        _state            = LichMovementState.IdleHover;
        _isLocked         = false;
        _dashTimer        = 0f;
        _returnFaceTimer  = 0f;
        _idleGazeAngle    = 0f;
        _idleGazeTimer    = 0f;
        _postAttackTimer  = 0f;
        _idleWanderTimer  = 0f;
        _idleWanderTarget = _transform != null ? _transform.position : Vector3.zero;
        _xzVelocity       = Vector3.zero;
        _yVelocity        = 0f;
        _targetAltitude   = _groundY + _baseAltitude;
        _movementTimer    = 0f;
        _movementDuration = 2f;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 외부 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>패턴 SO의 Enter()에서 호출. 이동 상태를 선점한다.</summary>
    public void RequestMovementState(LichMovementState state)
    {
        if (_state == state) return;
        EnterState(state);
    }

    /// <summary>true: 패턴 이동 잠금 (이동 재평가 중단). false: 재개.</summary>
    public void SetLocked(bool locked)
    {
        if (_isLocked && !locked)
            _returnFaceTimer = _returnFaceDuration;
        _isLocked = locked;
    }

    /// <summary>
    /// 패턴 종료 시 LichMonster에서 호출.
    /// 취약 구간 타이머를 시작하고 다음 Tick에서 이동 재평가를 트리거한다.
    /// </summary>
    public void NotifyPatternEnded()
    {
        _postAttackTimer  = _postAttackDuration;
        _movementTimer    = _movementDuration; // 다음 Tick에서 즉시 재평가
    }

    /// <summary>Tick이 호출되지 않는 구간(DormantState 등)에서 외부 회전 적용.</summary>
    public void FaceTowards(Vector3 worldPos, float dt)
    {
        if (_transform == null) _transform = transform;
        Vector3 dir = worldPos - _transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        _transform.rotation = Quaternion.Slerp(
            _transform.rotation, Quaternion.LookRotation(dir), _rotateSpeed * dt);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 메인 틱
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Tick(float dt, Transform playerTarget)
    {
        if (_bb == null) return;
        _cachedPlayer = playerTarget;

        _bb.DistanceToPlayer = playerTarget != null
            ? Vector3.Distance(_transform.position, playerTarget.position)
            : 999f;

        if (_returnFaceTimer > 0f) _returnFaceTimer -= dt;
        if (_postAttackTimer  > 0f) _postAttackTimer  -= dt;

        // IdleHover 시선 분산
        if (_state == LichMovementState.IdleHover && !_isLocked && _idleGazeVariance > 0f)
        {
            _idleGazeTimer += dt;
            if (_idleGazeTimer >= _idleGazeInterval)
            {
                _idleGazeTimer = 0f;
                _idleGazeAngle = UnityEngine.Random.Range(-_idleGazeVariance, _idleGazeVariance);
            }
        }
        else
        {
            _idleGazeAngle = Mathf.MoveTowards(_idleGazeAngle, 0f, 90f * dt);
        }

        // 이동 패턴 선택 — 잠기지 않은 상태에서 유지 시간 만료 시 재평가
        if (!_isLocked && playerTarget != null)
        {
            _movementTimer += dt;
            if (_movementTimer >= _movementDuration)
                SelectNextMovement();
        }

        float speedMult = _postAttackTimer > 0f ? _postAttackSpeedMult : 1f;
        ExecuteMovement(dt, playerTarget, speedMult);

        // Y 고도 + 호버 진동
        float hover   = _hoverAmplitude * Mathf.Sin(Time.time * (Mathf.PI * 2f / _hoverPeriod));
        float targetY = _targetAltitude + hover;
        float newY    = Mathf.SmoothDamp(_transform.position.y, targetY, ref _yVelocity, _altSmoothTime);
        var   p       = _transform.position;
        p.y = newY;
        _transform.position = p;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이동 패턴 선택
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SelectNextMovement()
    {
        if (_movementEntries == null || _movementEntries.Length == 0)
        {
            SetMovementState(LichMovementState.IdleHover, 2f, 4f);
            return;
        }

        float dist     = _bb?.DistanceToPlayer ?? 999f;
        bool  isPhase2 = _bb?.IsPhase2 ?? false;

        float total = 0f;
        foreach (var e in _movementEntries)
            if (e.Evaluate(dist, isPhase2)) total += Mathf.Max(0f, e.weight);

        if (total <= 0f) { SetMovementState(LichMovementState.IdleHover, 2f, 4f); return; }

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var e in _movementEntries)
        {
            if (!e.Evaluate(dist, isPhase2)) continue;
            acc += Mathf.Max(0f, e.weight);
            if (roll <= acc)
            {
                SetMovementState(e.state, e.minDuration, e.maxDuration);
                return;
            }
        }

        SetMovementState(LichMovementState.IdleHover, 2f, 4f);
    }

    private void SetMovementState(LichMovementState state, float minDur, float maxDur)
    {
        _movementDuration = UnityEngine.Random.Range(minDur, maxDur);
        _movementTimer    = 0f;
        if (_state != state)
            EnterState(state);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 상태 진입
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void EnterState(LichMovementState newState)
    {
        _state = newState;

        switch (newState)
        {
            case LichMovementState.IdleHover:
                _idleWanderTimer  = _idleWanderInterval;
                _idleWanderTarget = _transform != null ? _transform.position : Vector3.zero;
                break;

            case LichMovementState.AltitudeRise:
                _targetAltitude = _groundY + _riseAltitude;
                break;

            case LichMovementState.AltitudeDescend:
                _targetAltitude = _groundY + _baseAltitude;
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

    private void ExecuteMovement(float dt, Transform playerTarget, float speedMult)
    {
        if (playerTarget == null) return;

        switch (_state)
        {
            case LichMovementState.IdleHover:
                _idleWanderTimer += dt;
                if (_idleWanderTimer >= _idleWanderInterval)
                {
                    _idleWanderTimer = 0f;
                    // 현재 플레이어 기준 각도에서 ±70° 이내로 제한 — 반대편 타겟으로 왔다갔다하는 현상 방지
                    Vector3 toMe = _transform.position - playerTarget.position;
                    toMe.y = 0f;
                    float currentAngle = toMe.sqrMagnitude > 0.001f
                        ? Mathf.Atan2(toMe.z, toMe.x)
                        : 0f;
                    float angle = currentAngle + UnityEngine.Random.Range(-70f, 70f) * Mathf.Deg2Rad;
                    float range = _preferredRange + UnityEngine.Random.Range(-_idleWanderRadius * 0.5f, _idleWanderRadius * 0.5f);
                    Vector3 candidate = playerTarget.position
                        + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * range;
                    _idleWanderTarget = ClampToWall(_transform.position, candidate);
                }
                MoveXZ(_idleWanderTarget, _floatSpeed * 0.4f * speedMult, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.AltitudeRise:
            case LichMovementState.AltitudeDescend:
                SlowStop(dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.AdvanceFloat:
                {
                    Vector3 toPlayer = playerTarget.position - _transform.position;
                    toPlayer.y = 0f;
                    if (toPlayer.magnitude > _preferredRange + 0.5f)
                    {
                        Vector3 advTarget = playerTarget.position - FlatDir(toPlayer) * _preferredRange;
                        MoveXZ(advTarget, _floatSpeed * speedMult, dt);
                    }
                    else
                    {
                        SlowStop(dt);
                    }
                    RotateTowards(playerTarget.position, dt);
                }
                break;

            case LichMovementState.RetreatFloat:
                Vector3 away = _transform.position + FlatDir(_transform.position - playerTarget.position) * 2f;
                MoveXZ(away, _floatSpeed * speedMult, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.CircleStrafe:
                _strafeAngle += _strafeAngularSpeed * dt;
                float rad = _strafeAngle * Mathf.Deg2Rad;
                Vector3 orbitTarget = playerTarget.position
                    + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _strafeRadius;
                MoveXZ(orbitTarget, _floatSpeed * 2f * speedMult, dt);
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
                    EnterState(LichMovementState.IdleHover); // 대시 완료 → IdleHover (movementTimer 계속 진행)
                break;

            case LichMovementState.Teleport:
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

        if (_wallLayer != 0)
        {
            Vector3 delta = newPos - cur;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist > 0.001f &&
                Physics.SphereCast(cur, _wallCheckRadius, delta / dist, out _, dist, _wallLayer))
            {
                _xzVelocity = Vector3.zero;
                return;
            }
        }

        _transform.position = newPos;
    }

    private void SlowStop(float dt)
    {
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

        Quaternion targetRot = Quaternion.LookRotation(dir);
        if (_idleGazeAngle != 0f)
            targetRot *= Quaternion.Euler(0f, _idleGazeAngle, 0f);

        float speed = _returnFaceTimer > 0f ? _returnFaceSpeed : _rotateSpeed;
        _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRot, speed * dt);
    }

    private void DoTeleport()
    {
        if (_cachedPlayer == null) return;
        float   angle  = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float   dist   = UnityEngine.Random.Range(5f, 9f);
        Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        Vector3 dest   = _cachedPlayer.position + offset;
        dest   = ClampToWall(_cachedPlayer.position, dest);
        dest.y = _targetAltitude;
        _transform.position = dest;
        _xzVelocity         = Vector3.zero;
        _yVelocity          = 0f;
        EnterState(LichMovementState.IdleHover);
    }

    private Vector3 ClampToWall(Vector3 from, Vector3 to)
    {
        if (_wallLayer == 0) return to;
        from.y = _transform.position.y;
        to.y   = from.y;
        Vector3 dir  = to - from;
        float   dist = dir.magnitude;
        if (dist < 0.01f) return to;
        if (Physics.SphereCast(from, _wallCheckRadius, dir / dist, out RaycastHit hit, dist, _wallLayer))
            return from + dir / dist * Mathf.Max(0f, hit.distance - _wallCheckRadius);
        return to;
    }

    private static Vector3 FlatDir(Vector3 v)
    {
        v.y = 0f;
        float mag = v.magnitude;
        return mag > 0.001f ? v / mag : Vector3.forward;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        _movementEntries = new LichMovementEntry[]
        {
            // ── Phase 1 + 2 공통 ─────────────────────────────────────────
            new() { state = LichMovementState.IdleHover,    minDist = -1f, maxDist = -1f, minDuration = 1.5f, maxDuration = 2.5f, weight = 1.5f },
            new() { state = LichMovementState.AdvanceFloat, minDist = 7f,  maxDist = -1f, minDuration = 2f,   maxDuration = 4f,   weight = 3f   },
            new() { state = LichMovementState.CircleStrafe, minDist = 3f,  maxDist = 9f,  minDuration = 2f,   maxDuration = 4f,   weight = 2.5f },
            new() { state = LichMovementState.RetreatFloat, minDist = -1f, maxDist = 3f,  minDuration = 1f,   maxDuration = 2f,   weight = 3f   },
            new() { state = LichMovementState.DashClose,    minDist = 12f, maxDist = -1f, minDuration = 1.5f, maxDuration = 2.5f, weight = 2f   },
            // ── Phase 2 전용: 더 공격적인 근접 행동 ─────────────────────
            new() { state = LichMovementState.AdvanceFloat, minDist = 5f,  maxDist = -1f, phase2Only = true, minDuration = 1.5f, maxDuration = 3f,   weight = 2f },
            new() { state = LichMovementState.CircleStrafe, minDist = 2f,  maxDist = 7f,  phase2Only = true, minDuration = 2.5f, maxDuration = 5f,   weight = 2f },
        };
    }
#endif
}
}
