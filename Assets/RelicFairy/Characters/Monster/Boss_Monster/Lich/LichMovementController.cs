using System;
using UnityEngine;

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
    Glide,           // 교전 거리 안의 새 자리로 곡선 활강 — 대마법사의 기본 움직임
    Scripted,        // 패턴이 지정한 경로(ScriptMove) — 끝나면 IdleHover
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
    [Tooltip("낫을 든 페이지(2 이상)만 허용")]
    public bool  phase2Only;
    [Tooltip("허용 페이지 비트(1 = P1, 2 = P2, 4 = P3). 0이면 제한 없음")]
    public int   pageMask;
    [Tooltip("이 상태를 유지할 최소 시간 (초)")]
    public float minDuration = 1.5f;
    [Tooltip("이 상태를 유지할 최대 시간 (초)")]
    public float maxDuration = 3.5f;
    [Tooltip("선택 가중치. 높을수록 선택 확률 증가")]
    public float weight = 1f;

    public bool Evaluate(float dist, int page)
    {
        if (phase2Only && page < 2) return false;
        if (pageMask != 0 && (pageMask & (1 << (page - 1))) == 0) return false;
        if (minDist >= 0f && dist < minDist) return false;
        if (maxDist >= 0f && dist > maxDist) return false;
        return true;
    }
}

/// <summary>
/// 리치 보스 공중 이동 컨트롤러.
///
/// ── 기본 움직임: 대마법사 ───────────────────────────────────────────
///  리치는 쫓아오지 않는다. 페이지마다 정해진 <b>교전 거리 띠</b> 안에서 자리를 골라 곡선으로 활강하고(Glide),
///  이동 방향으로 몸을 기울인다(뱅킹). 플레이어가 가까이 붙으면 대각선 뒤로 빠르게 빠진다(회피 활강).
///  모든 목적지는 <b>제단 반경 안</b>으로 제한한다 — 떨어져 나간 가장자리 위에서 싸우지 않게.
///
/// ── 선택 방식 ──────────────────────────────────────────────────────
///  _movementEntries 중 조건(거리·페이지)을 충족하는 항목을 가중치 랜덤으로 고르고, 유지 시간이 끝나면 재평가한다.
///  패턴은 RequestMovementState / ScriptMove / SnapTo / SetAltitudeOffset으로 이동을 선점한다.
///
/// ── Dark Souls / Elden Ring UX 원칙 ─────────────────────────────
///  • 공격 후 취약 구간: 패턴 종료 직후 이동 속도 감소
///  • 전투 선호 거리: 페이지별 교전 거리 띠
/// </summary>
public class LichMovementController : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float MinGlideSeconds  = 0.8f;
    private const float MaxGlideSeconds  = 2.4f;
    private const float FloorProbeHeight = 6f;
    private const float FloorProbeDepth  = 40f;

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

    [Header("교전 거리 띠 (페이지별, m)")]
    [Tooltip("봉인기 1페이지 · 악몽기 1페이지 — 거리를 두는 시전자")]
    [SerializeField] private Vector2 _rangeBandCaster = new Vector2(9f, 14f);
    [Tooltip("봉인기 2페이지(사슬에 묶인 낫)")]
    [SerializeField] private Vector2 _rangeBandBound  = new Vector2(6f, 10f);
    [Tooltip("악몽기 2페이지(해방된 낫)")]
    [SerializeField] private Vector2 _rangeBandFree   = new Vector2(4f, 9f);
    [Tooltip("3페이지(코어 20 m)")]
    [SerializeField] private Vector2 _rangeBandCore   = new Vector2(3f, 7f);

    [Header("활강 (Glide)")]
    [SerializeField] private float _glideSpeed       = 4.5f;
    [Tooltip("봉인기 2페이지는 사슬에 끌려 느리다")]
    [SerializeField] private float _boundSpeedMult   = 0.8f;
    [Tooltip("곡선 경로의 옆 부풂(경로 길이 대비)")]
    [SerializeField] private float _glideArcRatio    = 0.25f;
    [Tooltip("자리 고를 때 현재 방위에서 틀 각도 범위(도)")]
    [SerializeField] private Vector2 _glideBearingShift = new Vector2(25f, 70f);
    [Tooltip("이동 방향으로 몸을 기울이는 최대 각도(도)")]
    [SerializeField] private float _bankAngle        = 12f;
    [SerializeField] private float _bankSmooth       = 6f;

    [Header("회피 활강")]
    [Tooltip("이 거리 안으로 들어오면 대각선 뒤로 빠진다(m)")]
    [SerializeField] private float _evadeTriggerDist = 4.5f;
    [SerializeField] private float _evadeSpeedMult   = 1.7f;
    [SerializeField] private float _evadeCooldown    = 2.5f;

    [Header("전장 경계")]
    [Tooltip("제단 중심에서 벗어나지 않는 반경(m) — 1·2페이지")]
    [SerializeField] private float _arenaRadius      = 24f;
    [Tooltip("3페이지 반경(m) — 코어만 남는다")]
    [SerializeField] private float _coreRadius       = 8f;

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
    private float             _altitudeOffset;
    private bool              _holdAltitude; // 패턴이 고도를 직접 쥐는 중(SnapAltitude) — 부드러운 고도 추종을 끈다
    private float             _groundY; // 스폰 지점 Y — 고도 계산의 기준점
    private float             _floorY;  // 실제 바닥 높이 — 지면 판정·소환 위치용
    private float             _returnFaceTimer;
    private float             _idleGazeAngle;
    private float             _idleGazeTimer;
    private float             _postAttackTimer;
    private Vector3           _idleWanderTarget;
    private float             _idleWanderTimer;
    private bool              _faceHeld;        // 패턴이 몸 방향을 쥐었다(HoldFacing)
    private float             _heldYaw;
    private float             _heldTurnSpeed;
    private float             _movementTimer;
    private float             _movementDuration;
    private float             _evadeTimer;
    private float             _bank;

    // 곡선 이동(활강·스크립트 공용)
    private Vector3 _pathStart;
    private Vector3 _pathControl;
    private Vector3 _pathEnd;
    private float   _pathTime;
    private float   _pathDuration;
    private bool    _pathFacePlayer;
    private Vector3 _lastPathPos;

    private Vector3 _arenaCenter;
    private bool    _hasArena;

    // ── 의존성 ─────────────────────────────────────────────
    private Transform      _transform;
    private LichBlackboard _bb;
    private Transform      _cachedPlayer;

    public LichMovementState CurrentState => _state;

    /// <summary>스폰 지점 Y. 공중 고도의 기준점.</summary>
    public float GroundY => _groundY;

    /// <summary>실제 바닥 높이 — 착지·지면 판정·소환 위치에 쓴다.</summary>
    public float FloorY => _floorY;

    /// <summary>제단 중심(바닥 높이). 붕괴형 아레나가 아니면 스폰 지점.</summary>
    public Vector3 ArenaCenter => _arenaCenter;

    /// <summary>지금 페이지에서 움직일 수 있는 반경(m).</summary>
    public float ArenaRadius => (_bb?.Page ?? 1) >= 3 ? _coreRadius : _arenaRadius;

    /// <summary>ScriptMove 경로를 따라가는 중인가.</summary>
    public bool IsScriptMoving => _state == LichMovementState.Scripted;

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
        ResolveArena();
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
        _altitudeOffset   = 0f;
        _holdAltitude     = false;
        _targetAltitude   = _groundY + _baseAltitude;
        _movementTimer    = 0f;
        _movementDuration = 2f;
        _evadeTimer       = 0f;
        _bank             = 0f;
        if (_transform != null) ResolveArena();
    }

    /// <summary>
    /// 고도 기준점·제단 중심을 지금 위치로 다시 잡는다 — 등장 직전(스포너가 자리를 잡은 뒤)에 부른다.
    /// 스포너는 원점에 생성한 뒤 옮기는데, 설정이 캐시된 두 번째 런부터는 Init이 옮기기 전에 끝나
    /// 제단 중심이 원점(2 km 밖)으로 잡혔다(09-18 실측).
    /// </summary>
    public void RebaseToCurrentPosition()
    {
        if (_transform == null) return;
        _groundY          = _transform.position.y;
        _targetAltitude   = _groundY + _baseAltitude;
        _idleWanderTarget = _transform.position;
        ResolveArena();
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
        {
            _returnFaceTimer = _returnFaceDuration;
            _faceHeld        = false;
        }
        _isLocked = locked;
    }

    /// <summary>
    /// 패턴이 몸 방향을 쥔다 — 판정·이펙트 방향과 몸이 같은 곳을 보게(플레이어 쪽으로 돌지 않는다).
    /// 초당 <paramref name="turnDegPerSec"/>로 돈다. 잠금이 풀리면 자동으로 놓는다.
    /// </summary>
    public void HoldFacing(float yaw, float turnDegPerSec = 720f)
    {
        _faceHeld      = true;
        _heldYaw       = yaw;
        _heldTurnSpeed = Mathf.Max(1f, turnDegPerSec);
    }

    /// <summary>몸 방향을 놓는다 — 다시 플레이어를 본다.</summary>
    public void ReleaseFacing() => _faceHeld = false;

    /// <summary>
    /// 패턴 종료 시 LichMonster에서 호출.
    /// 취약 구간 타이머를 시작하고 다음 Tick에서 이동 재평가를 트리거한다.
    /// </summary>
    public void NotifyPatternEnded()
    {
        _postAttackTimer  = _postAttackDuration;
        _movementTimer    = _movementDuration; // 다음 Tick에서 즉시 재평가
        if (_state == LichMovementState.Scripted) EnterState(LichMovementState.IdleHover);
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

    /// <summary>
    /// 패턴이 정한 곳으로 곡선 이동한다(<paramref name="seconds"/>초, <paramref name="arc"/> = 옆 부풂 m).
    /// 목적지는 제단 반경으로 제한된다. 수평만 움직이고 고도는 <see cref="SetAltitudeOffset"/>이 맡는다.
    /// </summary>
    public void ScriptMove(Vector3 destination, float seconds, float arc = 0f, bool facePlayer = true)
    {
        if (_transform == null) return;
        BeginPath(ClampToArena(destination), Mathf.Max(0.05f, seconds), arc, facePlayer);
        _state = LichMovementState.Scripted;
    }

    /// <summary>즉시 그 자리로 옮긴다(순간이동). 고도는 기본 고도 + 현재 오프셋, 진행 중인 경로는 취소.</summary>
    public void SnapTo(Vector3 position, bool keepAltitude = true)
    {
        if (_transform == null) return;
        position   = ClampToArena(position);
        position.y = keepAltitude ? _transform.position.y : _targetAltitude;
        _transform.position = position;
        _xzVelocity = Vector3.zero;
        _yVelocity  = 0f;
        if (_state == LichMovementState.Scripted || _state == LichMovementState.Glide)
            EnterState(LichMovementState.IdleHover);
    }

    /// <summary>기본 고도 위로 더 올라가거나(양수) 내려온다(음수). 패턴이 끝나면 0으로 되돌린다.</summary>
    public void SetAltitudeOffset(float offset)
    {
        _altitudeOffset = offset;
        _targetAltitude = _groundY + _baseAltitude + offset;
    }

    /// <summary>
    /// true면 매 틱의 고도 추종·호버를 멈춘다 — 패턴이 <see cref="SnapAltitude"/>로 고도를 직접 움직일 때.
    /// false로 풀면 지금 높이에서 기본 고도로 부드럽게 돌아간다.
    /// </summary>
    public void HoldAltitude(bool hold)
    {
        _holdAltitude = hold;
        if (!hold) _yVelocity = 0f;
    }

    /// <summary>고도를 부드럽게 따라가지 않고 곧바로 맞춘다(낙하 강타 등).</summary>
    public void SnapAltitude(float worldY)
    {
        if (_transform == null) return;
        var p = _transform.position;
        p.y = worldY;
        _transform.position = p;
        _yVelocity = 0f;
    }

    /// <summary>지금 페이지의 교전 거리 띠(최소, 최대).</summary>
    public Vector2 CurrentRangeBand()
    {
        int page = _bb?.Page ?? 1;
        if (page >= 3) return _rangeBandCore;
        if (page == 2) return (_bb != null && _bb.IsNightmare) ? _rangeBandFree : _rangeBandBound;
        return _rangeBandCaster;
    }

    /// <summary>점을 제단 반경 안으로 끌어온다(수평).</summary>
    public Vector3 ClampToArena(Vector3 point)
    {
        if (!_hasArena) return point;
        Vector3 flat = point - _arenaCenter;
        flat.y = 0f;
        float r = ArenaRadius;
        if (flat.sqrMagnitude > r * r)
        {
            flat = flat.normalized * r;
            point.x = _arenaCenter.x + flat.x;
            point.z = _arenaCenter.z + flat.z;
        }
        return point;
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
        if (_evadeTimer       > 0f) _evadeTimer       -= dt;

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

        // 너무 붙었다 — 대각선 뒤로 빠진다(잠기지 않았고 스크립트 이동 중이 아닐 때).
        if (!_isLocked && playerTarget != null && _state != LichMovementState.Scripted
            && _evadeTimer <= 0f && _bb.DistanceToPlayer < _evadeTriggerDist)
        {
            BeginEvade(playerTarget);
        }

        // 이동 패턴 선택 — 잠기지 않은 상태에서 유지 시간 만료 시 재평가
        if (!_isLocked && playerTarget != null && _state != LichMovementState.Scripted)
        {
            _movementTimer += dt;
            if (_movementTimer >= _movementDuration)
                SelectNextMovement();
        }

        float speedMult = (_postAttackTimer > 0f ? _postAttackSpeedMult : 1f)
                        * (_bb != null ? _bb.MoveSpeedMult : 1f);   // 페이지 이동 배율(T2/T3 ×1.2 — 10-01 연결)
        Vector3 before  = _transform.position;
        ExecuteMovement(dt, playerTarget, speedMult);

        // Y 고도 + 호버 진동
        if (!_holdAltitude)
        {
            float hover   = _hoverAmplitude * Mathf.Sin(Time.time * (Mathf.PI * 2f / _hoverPeriod));
            float targetY = _targetAltitude + hover;
            float newY    = Mathf.SmoothDamp(_transform.position.y, targetY, ref _yVelocity, _altSmoothTime);
            var   p       = _transform.position;
            p.y = newY;
            _transform.position = p;
        }

        ApplyBank(before, dt);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이동 패턴 선택
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SelectNextMovement()
    {
        if (_movementEntries == null || _movementEntries.Length == 0)
        {
            SetMovementState(LichMovementState.Glide, 2f, 3.5f);
            return;
        }

        float dist = _bb?.DistanceToPlayer ?? 999f;
        int   page = _bb?.Page ?? 1;

        float total = 0f;
        foreach (var e in _movementEntries)
            if (e.Evaluate(dist, page)) total += Mathf.Max(0f, e.weight);

        if (total <= 0f) { SetMovementState(LichMovementState.Glide, 2f, 3.5f); return; }

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var e in _movementEntries)
        {
            if (!e.Evaluate(dist, page)) continue;
            acc += Mathf.Max(0f, e.weight);
            if (roll <= acc)
            {
                SetMovementState(e.state, e.minDuration, e.maxDuration);
                return;
            }
        }

        SetMovementState(LichMovementState.Glide, 2f, 3.5f);
    }

    private void SetMovementState(LichMovementState state, float minDur, float maxDur)
    {
        _movementDuration = UnityEngine.Random.Range(minDur, maxDur);
        _movementTimer    = 0f;
        // 활강은 같은 상태여도 매번 새 자리를 고른다.
        if (_state != state || state == LichMovementState.Glide)
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
                SetAltitudeOffset(_riseAltitude - _baseAltitude);
                break;

            case LichMovementState.AltitudeDescend:
                SetAltitudeOffset(0f);
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

            case LichMovementState.Glide:
                BeginGlide();
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
                // 패턴이 잠근 동안은 떠돌지 않는다 — 판정 원점·예고가 몸과 어긋나지 않게(09-19 감사).
                if (_isLocked)
                {
                    SlowStop(dt);
                    RotateTowards(playerTarget.position, dt);
                    break;
                }
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
                    float range = Mathf.Clamp(toMe.magnitude, CurrentRangeBand().x, CurrentRangeBand().y)
                                + UnityEngine.Random.Range(-_idleWanderRadius * 0.5f, _idleWanderRadius * 0.5f);
                    Vector3 candidate = playerTarget.position
                        + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * range;
                    _idleWanderTarget = ClampToArena(ClampToWall(_transform.position, candidate));
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
                        Vector3 advTarget = ClampToArena(playerTarget.position - FlatDir(toPlayer) * _preferredRange);
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
                Vector3 away = ClampToArena(_transform.position + FlatDir(_transform.position - playerTarget.position) * 2f);
                MoveXZ(away, _floatSpeed * speedMult, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.CircleStrafe:
                _strafeAngle += _strafeAngularSpeed * dt;
                float rad = _strafeAngle * Mathf.Deg2Rad;
                Vector3 orbitTarget = ClampToArena(playerTarget.position
                    + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _strafeRadius);
                MoveXZ(orbitTarget, _floatSpeed * 2f * speedMult, dt);
                RotateTowards(playerTarget.position, dt);
                break;

            case LichMovementState.DashClose:
                _dashTimer += dt;
                if (_dashTimer < _dashDuration)
                {
                    MoveXZ(ClampToArena(playerTarget.position), _dashSpeed, dt);
                    RotateTowards(playerTarget.position, dt);
                }
                else
                    EnterState(LichMovementState.IdleHover); // 대시 완료 → IdleHover (movementTimer 계속 진행)
                break;

            case LichMovementState.Glide:
            case LichMovementState.Scripted:
                // 취약 구간의 둔화는 활강에만 — 패턴 스크립트 이동은 정해진 시간을 지킨다.
                FollowPath(dt, _state == LichMovementState.Glide ? speedMult : 1f, playerTarget);
                break;

            case LichMovementState.Teleport:
                break;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 활강 · 곡선 경로
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>교전 거리 띠 안에서 방위를 틀어 새 자리를 고르고 곡선으로 날아간다.</summary>
    private void BeginGlide()
    {
        if (_cachedPlayer == null || _transform == null)
        {
            _state = LichMovementState.IdleHover;
            return;
        }

        Vector2 band   = CurrentRangeBand();
        Vector3 player = _cachedPlayer.position;
        Vector3 toMe   = _transform.position - player;
        toMe.y = 0f;
        float bearing  = toMe.sqrMagnitude > 0.01f ? Mathf.Atan2(toMe.z, toMe.x) : UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        // 제단 안쪽 여유가 큰 쪽으로 틀 확률을 높인다 — 가장자리로 몰려 같은 자리를 왕복하지 않게.
        float shift = UnityEngine.Random.Range(_glideBearingShift.x, _glideBearingShift.y) * Mathf.Deg2Rad;
        float sign  = PickBearingSign(player, bearing, shift, (band.x + band.y) * 0.5f);
        float angle = bearing + sign * shift;
        float dist  = UnityEngine.Random.Range(band.x, band.y);

        Vector3 dest = player + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        dest = ClampToArena(ClampToWall(_transform.position, dest));

        float speed  = _glideSpeed * (IsBoundPage() ? _boundSpeedMult : 1f);
        float length = Vector3.Distance(Flat(_transform.position), Flat(dest));
        float secs   = Mathf.Clamp(length / Mathf.Max(0.1f, speed), MinGlideSeconds, MaxGlideSeconds);
        BeginPath(dest, secs, length * _glideArcRatio * sign, facePlayer: true);
    }

    /// <summary>너무 붙었을 때 — 대각선 뒤, 교전 거리 최소선까지 빠르게.</summary>
    private void BeginEvade(Transform playerTarget)
    {
        Vector3 away = FlatDir(_transform.position - playerTarget.position);
        float   side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        Vector3 dir  = (Quaternion.Euler(0f, 40f * side, 0f) * away).normalized;
        Vector3 dest = playerTarget.position + dir * (CurrentRangeBand().x + 1f);
        dest = ClampToArena(ClampToWall(_transform.position, dest));

        float speed  = _glideSpeed * _evadeSpeedMult * (IsBoundPage() ? _boundSpeedMult : 1f);
        float length = Vector3.Distance(Flat(_transform.position), Flat(dest));
        BeginPath(dest, Mathf.Clamp(length / speed, 0.4f, 1.2f), length * 0.15f * side, facePlayer: true);
        _state         = LichMovementState.Glide;
        _evadeTimer    = _evadeCooldown;
        _movementTimer = 0f;
    }

    private void BeginPath(Vector3 dest, float seconds, float arc, bool facePlayer)
    {
        _pathStart      = Flat(_transform.position);
        _pathEnd        = Flat(dest);
        Vector3 mid     = (_pathStart + _pathEnd) * 0.5f;
        Vector3 along   = _pathEnd - _pathStart;
        Vector3 side    = along.sqrMagnitude > 0.001f ? Vector3.Cross(Vector3.up, along.normalized) : Vector3.zero;
        _pathControl    = mid + side * arc;
        _pathTime       = 0f;
        _pathDuration   = seconds;
        _pathFacePlayer = facePlayer;
        _lastPathPos    = _pathStart;
        _xzVelocity     = Vector3.zero;
    }

    private void FollowPath(float dt, float speedMult, Transform playerTarget)
    {
        _pathTime += dt * speedMult;
        float t = Mathf.Clamp01(_pathTime / _pathDuration);
        float e = t * t * (3f - 2f * t);   // 부드럽게 출발·도착

        Vector3 a   = Vector3.Lerp(_pathStart, _pathControl, e);
        Vector3 b   = Vector3.Lerp(_pathControl, _pathEnd, e);
        Vector3 pos = Vector3.Lerp(a, b, e);

        if (_wallLayer != 0)
        {
            Vector3 delta = pos - _lastPathPos;
            float   d     = delta.magnitude;
            Vector3 from  = new Vector3(_lastPathPos.x, _transform.position.y, _lastPathPos.z);
            if (d > 0.001f && Physics.SphereCast(from, _wallCheckRadius, delta / d, out _, d, _wallLayer))
            {
                EnterState(LichMovementState.IdleHover);   // 벽에 막히면 그 자리에서 멈춘다
                return;
            }
        }

        _lastPathPos = pos;
        var p = _transform.position;
        p.x = pos.x;
        p.z = pos.z;
        _transform.position = p;

        if (_pathFacePlayer && playerTarget != null)
            RotateTowards(playerTarget.position, dt);
        else
            RotateAlong(pos - Flat(_transform.position), dt);

        if (t >= 1f)
            EnterState(LichMovementState.IdleHover);
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
        if (TurnToHeldFacing(dt)) return;
        Vector3 dir = target - _transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        if (_idleGazeAngle != 0f)
            targetRot *= Quaternion.Euler(0f, _idleGazeAngle, 0f);

        float speed = _returnFaceTimer > 0f ? _returnFaceSpeed : _rotateSpeed;
        _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRot * Quaternion.Euler(0f, 0f, _bank), speed * dt);
    }

    /// <summary>몸 방향을 쥔 동안은 그 방향으로만 돈다. 쥐었으면 true.</summary>
    private bool TurnToHeldFacing(float dt)
    {
        if (!_faceHeld) return false;
        Quaternion target = Quaternion.Euler(0f, _heldYaw, 0f) * Quaternion.Euler(0f, 0f, _bank);
        _transform.rotation = Quaternion.RotateTowards(_transform.rotation, target, _heldTurnSpeed * dt);
        return true;
    }

    private void RotateAlong(Vector3 moveDir, float dt)
    {
        if (TurnToHeldFacing(dt)) return;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f) return;
        _transform.rotation = Quaternion.Slerp(
            _transform.rotation, Quaternion.LookRotation(moveDir) * Quaternion.Euler(0f, 0f, _bank), _rotateSpeed * dt);
    }

    /// <summary>옆으로 미끄러지는 방향으로 몸을 기울인다 — 이번 프레임 수평 이동의 오른쪽 성분이 기준.</summary>
    private void ApplyBank(Vector3 before, float dt)
    {
        if (_bankAngle <= 0f || dt <= 0f) return;
        Vector3 vel     = (_transform.position - before) / dt;
        vel.y           = 0f;
        float   lateral = Vector3.Dot(vel, _transform.right);
        float   target  = Mathf.Clamp(-lateral / Mathf.Max(0.1f, _glideSpeed), -1f, 1f) * _bankAngle;
        _bank = Mathf.Lerp(_bank, target, 1f - Mathf.Exp(-_bankSmooth * dt));
    }

    private float PickBearingSign(Vector3 player, float bearing, float shift, float dist)
    {
        if (!_hasArena) return UnityEngine.Random.value < 0.5f ? -1f : 1f;

        Vector3 plus  = player + new Vector3(Mathf.Cos(bearing + shift), 0f, Mathf.Sin(bearing + shift)) * dist;
        Vector3 minus = player + new Vector3(Mathf.Cos(bearing - shift), 0f, Mathf.Sin(bearing - shift)) * dist;
        float plusRoom  = ArenaRadius - Vector3.Distance(Flat(plus),  Flat(_arenaCenter));
        float minusRoom = ArenaRadius - Vector3.Distance(Flat(minus), Flat(_arenaCenter));
        float pPlus     = Mathf.Clamp01(0.5f + (plusRoom - minusRoom) * 0.05f);
        return UnityEngine.Random.value < pPlus ? 1f : -1f;
    }

    private bool IsBoundPage() => _bb != null && _bb.Page == 2 && !_bb.IsNightmare;

    private void DoTeleport()
    {
        if (_cachedPlayer == null) return;
        float   angle  = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float   dist   = UnityEngine.Random.Range(5f, 9f);
        Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        Vector3 dest   = _cachedPlayer.position + offset;
        dest   = ClampToArena(ClampToWall(_cachedPlayer.position, dest));
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

    /// <summary>제단 중심과 바닥 높이를 정한다 — 붕괴형 아레나 위면 그 격자, 아니면 스폰 지점 아래 바닥.</summary>
    private void ResolveArena()
    {
        Vector3 pos  = _transform.position;
        var     grid = ArenaTileGrid.Active;
        _hasArena = grid != null && grid.TryGetCell(pos, out _) && grid.TryGetWorldCenter(out _arenaCenter);

        _floorY = Physics.Raycast(pos + Vector3.up * FloorProbeHeight, Vector3.down, out var hit,
                                  FloorProbeDepth, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : (_hasArena ? _arenaCenter.y : pos.y);

        if (!_hasArena)
            _arenaCenter = new Vector3(pos.x, _floorY, pos.z);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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
            // ── 공통: 대마법사의 활강이 기본 ──────────────────────────────
            new() { state = LichMovementState.Glide,        minDist = -1f, maxDist = -1f, minDuration = 2f,   maxDuration = 3.5f, weight = 5f   },
            new() { state = LichMovementState.IdleHover,    minDist = -1f, maxDist = -1f, minDuration = 1.2f, maxDuration = 2f,   weight = 1f   },
            new() { state = LichMovementState.CircleStrafe, minDist = 5f,  maxDist = 12f, minDuration = 2f,   maxDuration = 3f,   weight = 1f   },
            // ── 낫 페이지: 더 공격적으로 붙는다 ───────────────────────────
            new() { state = LichMovementState.AdvanceFloat, minDist = 7f,  maxDist = -1f, phase2Only = true, minDuration = 1.5f, maxDuration = 3f, weight = 2f },
            new() { state = LichMovementState.DashClose,    minDist = 11f, maxDist = -1f, phase2Only = true, minDuration = 1.5f, maxDuration = 2.5f, weight = 1.5f },
        };
    }
#endif
}
}
