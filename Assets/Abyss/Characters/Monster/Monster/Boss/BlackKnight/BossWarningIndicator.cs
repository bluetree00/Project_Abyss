using UnityEngine;

/// <summary>
/// BlackKnight 보스 공격 경고 장판.
/// 보스 프리팹에 부착하며, 각 공격 상태에서 Show* / Hide* 를 호출한다.
///
///  ShowCircle        — SpinSlash(회전), OverheadSlash(내려찍기) 용 원형 장판
///  ShowCharge        — ChargeAttack(돌진) 용 화살표 장판
///  ShowLeapTarget    — RainAttack/LeapSlam 공용: 추적→고정 원형 (흰→빨, 반지름 성장)
///  ShowScatterLines  — ScatterShot: 투사체 궤적 선 (개별 방향별 라인)
///  ShowFan           — (예비) 부채꼴 장판
/// </summary>
public class BossWarningIndicator : UnityEngine.MonoBehaviour
{
    private const int CircleSegments = 48;

    // ── 원형 경고 ─────────────────────────────────────────
    private UnityEngine.LineRenderer _circleOutline;
    private UnityEngine.GameObject   _circleFillGO;
    private UnityEngine.Material     _circleFillMat;

    private bool                   _circleActive;
    private float                  _circleTimer;
    private float                  _circleDuration;
    private UnityEngine.Transform  _circleFollow;
    private float                  _circleRadius;
    private float                  _circleColorFloor; // 0=흐리게 시작, 1=즉시 최대 밝기

    // ── 돌진 경고 ─────────────────────────────────────────
    private UnityEngine.LineRenderer _chargeOutline;

    private bool  _chargeActive;
    private float _chargeTimer;
    private float _chargeDuration;

    // ── 낙하 경고 (다중 원) ─────────────────────────────
    private const int MaxDropZones = 6;
    private UnityEngine.LineRenderer[] _dropOutlines;
    private UnityEngine.GameObject[]   _dropFillGOs;
    private UnityEngine.Material[]     _dropFillMats;
    private bool[]                     _dropActive;
    private UnityEngine.Vector3[]      _dropPositions;
    private float                      _dropRadius;
    private float                      _dropTimer;
    private float                      _dropDuration;
    private bool                       _anyDropActive;

    // ── 도약 착지 추적 ──────────────────────────────────
    private UnityEngine.LineRenderer _leapOutline;
    private UnityEngine.GameObject   _leapFillGO;
    private UnityEngine.Material     _leapFillMat;
    private bool                     _leapActive;
    private bool                     _leapLocked;
    private UnityEngine.Transform    _leapFollow;
    private UnityEngine.Vector3      _leapLockedPos;
    private float                    _leapRadius;
    private float                    _leapTrackTimer;
    private float                    _leapTotalTrack;

    // ── 부채꼴 경고 ──────────────────────────────────────
    private const int FanArcSegments = 18;
    private UnityEngine.LineRenderer _fanOutline;
    private bool                     _fanActive;
    private float                    _fanTimer;
    private float                    _fanDuration;
    private UnityEngine.Vector3      _fanOrigin;
    private UnityEngine.Vector3      _fanForward;
    private float                    _fanHalfAngle;
    private float                    _fanRange;

    // ── 산탄 궤적 선 풀 ──────────────────────────────────
    private const int MaxScatterLines = 15;
    private UnityEngine.LineRenderer[] _scatterLineRenderers;
    private bool  _scatterActive;
    private float _scatterTimer;
    private float _scatterDuration;

    // ── 도약 원 시작 반지름 (추적 중 성장) ──────────────
    private float _leapStartRadius;

    // ── 색상 상수 ─────────────────────────────────────────
    private static readonly UnityEngine.Color OutlineFaint  = new UnityEngine.Color(1f, 0.55f, 0f, 0.40f); // 주황
    private static readonly UnityEngine.Color OutlineBright = new UnityEngine.Color(1f, 0.00f, 0f, 1.00f); // 빨강
    private static readonly UnityEngine.Color FillFaint     = new UnityEngine.Color(1f, 0.45f, 0f, 0.10f); // 주황 반투명
    private static readonly UnityEngine.Color FillBright    = new UnityEngine.Color(1f, 0.00f, 0f, 0.32f); // 빨강 반투명

    // 낙하 / 도약 전용 색상
    private static readonly UnityEngine.Color LeapTrackColor = new UnityEngine.Color(1f, 1f, 1f, 0.70f); // 흰색
    private static readonly UnityEngine.Color LeapLockColor  = new UnityEngine.Color(1f, 0.05f, 0.05f, 1f); // 진빨강
    private static readonly UnityEngine.Color LeapFillTrack  = new UnityEngine.Color(1f, 1f,  1f,  0.08f);
    private static readonly UnityEngine.Color LeapFillLock   = new UnityEngine.Color(1f, 0f,  0f,  0.28f);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Unity 생명주기
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Awake()
    {
        // 원형 아웃라인
        _circleOutline = CreateLineRenderer("WarnCircle_Outline");
        _circleOutline.positionCount   = CircleSegments + 1;
        _circleOutline.widthMultiplier = 0.15f;

        // 원형 필 (디스크 메시)
        _circleFillGO = new UnityEngine.GameObject("WarnCircle_Fill");
        _circleFillGO.transform.SetParent(transform);
        var mf = _circleFillGO.AddComponent<UnityEngine.MeshFilter>();
        var mr = _circleFillGO.AddComponent<UnityEngine.MeshRenderer>();
        mf.mesh        = GenerateDiscMesh(1f, CircleSegments);
        _circleFillMat = CreateFillMaterial();
        mr.material    = _circleFillMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        // 돌진 아웃라인 (6점 화살표)
        _chargeOutline = CreateLineRenderer("WarnCharge_Outline");
        _chargeOutline.positionCount   = 6;
        _chargeOutline.widthMultiplier = 0.12f;

        // 낙하 경고 (다중 원 풀)
        _dropOutlines  = new UnityEngine.LineRenderer[MaxDropZones];
        _dropFillGOs   = new UnityEngine.GameObject[MaxDropZones];
        _dropFillMats  = new UnityEngine.Material[MaxDropZones];
        _dropActive    = new bool[MaxDropZones];
        _dropPositions = new UnityEngine.Vector3[MaxDropZones];
        for (int i = 0; i < MaxDropZones; i++)
        {
            var lr = CreateLineRenderer($"WarnDrop_Outline_{i}");
            lr.positionCount   = CircleSegments + 1;
            lr.widthMultiplier = 0.15f;
            _dropOutlines[i] = lr;

            var fillGO = new UnityEngine.GameObject($"WarnDrop_Fill_{i}");
            fillGO.transform.SetParent(transform);
            var mf2 = fillGO.AddComponent<UnityEngine.MeshFilter>();
            var mr2 = fillGO.AddComponent<UnityEngine.MeshRenderer>();
            mf2.mesh       = GenerateDiscMesh(1f, CircleSegments);
            var mat        = CreateFillMaterial();
            mr2.material   = mat;
            mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr2.receiveShadows    = false;
            _dropFillGOs[i]  = fillGO;
            _dropFillMats[i] = mat;

            SetLRActive(lr, false);
            fillGO.SetActive(false);
        }

        // 도약 착지 추적 원
        _leapOutline = CreateLineRenderer("WarnLeap_Outline");
        _leapOutline.positionCount   = CircleSegments + 1;
        _leapOutline.widthMultiplier = 0.18f;

        _leapFillGO = new UnityEngine.GameObject("WarnLeap_Fill");
        _leapFillGO.transform.SetParent(transform);
        var lfmf = _leapFillGO.AddComponent<UnityEngine.MeshFilter>();
        var lfmr = _leapFillGO.AddComponent<UnityEngine.MeshRenderer>();
        lfmf.mesh     = GenerateDiscMesh(1f, CircleSegments);
        _leapFillMat  = CreateFillMaterial();
        lfmr.material = _leapFillMat;
        lfmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lfmr.receiveShadows    = false;
        SetLRActive(_leapOutline, false);
        _leapFillGO.SetActive(false);

        // 부채꼴 경고
        _fanOutline = CreateLineRenderer("WarnFan_Outline");
        _fanOutline.positionCount   = FanArcSegments + 3; // origin + arc(N+1) + close
        _fanOutline.widthMultiplier = 0.12f;
        SetLRActive(_fanOutline, false);

        // 산탄 궤적 선 풀
        _scatterLineRenderers = new UnityEngine.LineRenderer[MaxScatterLines];
        for (int i = 0; i < MaxScatterLines; i++)
        {
            var lr = CreateLineRenderer($"WarnScatter_{i}");
            lr.positionCount   = 2;
            lr.widthMultiplier = 0.07f;
            SetLRActive(lr, false);
            _scatterLineRenderers[i] = lr;
        }

        SetLRActive(_circleOutline, false);
        _circleFillGO.SetActive(false);
        SetLRActive(_chargeOutline, false);
    }

    private void OnDestroy()
    {
        if (_circleFillMat != null) Destroy(_circleFillMat);
        if (_leapFillMat   != null) Destroy(_leapFillMat);
        if (_dropFillMats  != null)
            foreach (var m in _dropFillMats)
                if (m != null) Destroy(m);
    }

    private void Update()
    {
        float dt = UnityEngine.Time.deltaTime;

        if (_circleActive)
        {
            _circleTimer -= dt;
            UpdateCircleVisuals();
            if (_circleTimer <= 0f) HideCircle();
        }

        if (_chargeActive)
        {
            _chargeTimer -= dt;
            ApplyChargeColor(1f - UnityEngine.Mathf.Clamp01(_chargeTimer / _chargeDuration));
            if (_chargeTimer <= 0f) HideCharge();
        }

        if (_anyDropActive)
        {
            _dropTimer -= dt;
            float t = 1f - UnityEngine.Mathf.Clamp01(_dropTimer / _dropDuration);
            UpdateDropZoneVisuals(t);
            if (_dropTimer <= 0f) HideDropZones();
        }

        if (_leapActive)
            UpdateLeapVisuals(dt);

        if (_fanActive)
        {
            _fanTimer -= dt;
            float t = 1f - UnityEngine.Mathf.Clamp01(_fanTimer / _fanDuration);
            UpdateFanVisuals(t);
            if (_fanTimer <= 0f) HideFan();
        }

        if (_scatterActive)
        {
            _scatterTimer -= dt;
            float t = 1f - UnityEngine.Mathf.Clamp01(_scatterTimer / _scatterDuration);
            UpdateScatterVisuals(t);
            if (_scatterTimer <= 0f) HideScatterLines();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Public API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>원형 경고 장판 표시. follow 트랜스폼 중심으로 이동한다.</summary>
    /// <param name="colorFloor">색상 밝기 시작값. 0=흐리게→점점 밝아짐, 1=처음부터 최대 밝기.</param>
    public void ShowCircle(UnityEngine.Transform follow, float radius, float duration,
                           float colorFloor = 0f)
    {
        _circleFollow     = follow;
        _circleRadius     = radius;
        _circleDuration   = duration;
        _circleTimer      = duration;
        _circleColorFloor = UnityEngine.Mathf.Clamp01(colorFloor);
        _circleActive     = true;
        SetLRActive(_circleOutline, true);
        _circleFillGO.SetActive(true);
    }

    /// <summary>원형 경고 즉시 숨기기.</summary>
    public void HideCircle()
    {
        _circleActive = false;
        SetLRActive(_circleOutline, false);
        _circleFillGO.SetActive(false);
    }

    /// <summary>표시 중인 원형 장판의 반지름을 실시간 변경.</summary>
    public void UpdateCircleRadius(float radius) => _circleRadius = radius;

    /// <summary>돌진 경로 화살표 경고 표시.</summary>
    public void ShowCharge(UnityEngine.Vector3 start, UnityEngine.Vector3 direction,
                           float length, float width, float duration)
    {
        _chargeDuration = duration;
        _chargeTimer    = duration;
        _chargeActive   = true;
        SetLRActive(_chargeOutline, true);
        SetChargeShape(start, direction, length, width);
        ApplyChargeColor(0f);
    }

    /// <summary>돌진 경고 즉시 숨기기.</summary>
    public void HideCharge()
    {
        _chargeActive = false;
        SetLRActive(_chargeOutline, false);
    }

    // ─────────────────────────────────────────────────────────
    // 낙하 경고 (다중 원)
    // ─────────────────────────────────────────────────────────

    /// <summary>낙하 경고 원 다수를 동시에 표시 (positions.Length ≤ MaxDropZones).</summary>
    public void ShowDropZones(UnityEngine.Vector3[] positions, float radius, float duration)
    {
        HideDropZones();
        if (positions == null || positions.Length == 0) return;

        _dropRadius   = radius;
        _dropDuration = duration;
        _dropTimer    = duration;
        _anyDropActive = true;

        int count = UnityEngine.Mathf.Min(positions.Length, MaxDropZones);
        for (int i = 0; i < count; i++)
        {
            _dropPositions[i] = positions[i];
            _dropActive[i]    = true;
            SetLRActive(_dropOutlines[i], true);
            _dropFillGOs[i].SetActive(true);
        }
    }

    /// <summary>낙하 경고 원 즉시 숨기기.</summary>
    public void HideDropZones()
    {
        _anyDropActive = false;
        for (int i = 0; i < MaxDropZones; i++)
        {
            _dropActive[i] = false;
            SetLRActive(_dropOutlines[i], false);
            if (_dropFillGOs[i] != null) _dropFillGOs[i].SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 도약 착지 추적 원
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 도약 착지 추적 원 표시.
    /// follow 트랜스폼을 trackDuration 동안 추적한 뒤 LockLeapTarget()으로 고정한다.
    /// </summary>
    public void ShowLeapTarget(UnityEngine.Transform follow, float radius, float trackDuration)
    {
        HideLeapTarget();
        _leapFollow      = follow;
        _leapRadius      = radius;
        _leapStartRadius = radius * 0.25f; // 추적 중 성장 — 작게 시작
        _leapTotalTrack  = trackDuration;
        _leapTrackTimer  = trackDuration;
        _leapLocked      = false;
        _leapActive      = true;
        SetLRActive(_leapOutline, true);
        // 보스가 SetRenderersVisible(false)로 Renderer 컴포넌트를 비활성화했을 수 있으므로
        // GameObject.SetActive(true) 만으로는 부족 — 컴포넌트도 명시적으로 활성화한다.
        _leapOutline.enabled = true;
        _leapFillGO.SetActive(true);
        var mr = _leapFillGO.GetComponent<UnityEngine.MeshRenderer>();
        if (mr != null) mr.enabled = true;
    }

    /// <summary>추적을 멈추고 지정 위치에 착지 원을 고정한다.</summary>
    public void LockLeapTarget(UnityEngine.Vector3 worldPos)
    {
        _leapLocked    = true;
        _leapLockedPos = worldPos;
        _leapLockedPos.y = 0.05f;
    }

    /// <summary>도약 착지 원 즉시 숨기기.</summary>
    public void HideLeapTarget()
    {
        _leapActive = false;
        _leapLocked = false;
        SetLRActive(_leapOutline, false);
        if (_leapFillGO != null) _leapFillGO.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // 부채꼴 경고
    // ─────────────────────────────────────────────────────────

    /// <summary>부채꼴 경고 표시.</summary>
    /// <param name="halfAngle">부채꼴 반각 (도). fanAngle = 60 → halfAngle = 30.</param>
    public void ShowFan(UnityEngine.Vector3 origin, UnityEngine.Vector3 forward,
                        float halfAngle, float range, float duration)
    {
        _fanOrigin    = origin;
        _fanForward   = forward.normalized;
        _fanHalfAngle = halfAngle;
        _fanRange     = range;
        _fanDuration  = duration;
        _fanTimer     = duration;
        _fanActive    = true;
        SetLRActive(_fanOutline, true);
        SetFanShape();
    }

    /// <summary>부채꼴 경고 즉시 숨기기.</summary>
    public void HideFan()
    {
        _fanActive = false;
        SetLRActive(_fanOutline, false);
    }

    // ─────────────────────────────────────────────────────────
    // 산탄 궤적 선
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 투사체 궤적 선 표시. 각 방향마다 (origin+dir*startOffset)→(origin+dir*(startOffset+range)) 선분 1개.
    /// startOffset: 투사체 실제 스폰 오프셋 — 경고 라인과 투사체 끝점을 일치시키기 위해 사용.
    /// directions.Length ≤ MaxScatterLines.
    /// </summary>
    public void ShowScatterLines(UnityEngine.Vector3 origin, UnityEngine.Vector3[] directions,
                                 float range, float duration, float startOffset = 0f)
    {
        HideScatterLines();
        if (directions == null || directions.Length == 0) return;

        _scatterDuration = duration;
        _scatterTimer    = duration;
        _scatterActive   = true;

        float y     = origin.y + 0.05f;
        int   count = UnityEngine.Mathf.Min(directions.Length, MaxScatterLines);
        for (int i = 0; i < count; i++)
        {
            float sx  = origin.x + directions[i].x * startOffset;
            float sz  = origin.z + directions[i].z * startOffset;
            var start = new UnityEngine.Vector3(sx, y, sz);
            var end   = new UnityEngine.Vector3(
                sx + directions[i].x * range,
                y,
                sz + directions[i].z * range);
            _scatterLineRenderers[i].SetPosition(0, start);
            _scatterLineRenderers[i].SetPosition(1, end);
            SetLRActive(_scatterLineRenderers[i], true);
        }
    }

    /// <summary>산탄 궤적 선 즉시 숨기기.</summary>
    public void HideScatterLines()
    {
        _scatterActive = false;
        if (_scatterLineRenderers == null) return;
        for (int i = 0; i < MaxScatterLines; i++)
            if (_scatterLineRenderers[i] != null)
                SetLRActive(_scatterLineRenderers[i], false);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 비주얼 갱신
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void UpdateCircleVisuals()
    {
        if (_circleFollow == null) return;

        float timerT = 1f - UnityEngine.Mathf.Clamp01(_circleTimer / _circleDuration);
        float t      = UnityEngine.Mathf.Max(_circleColorFloor, timerT); // floor 이하로 떨어지지 않음
        float pulse  = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (5f + t * 12f)) + 1f) * 0.5f;
        float blend  = UnityEngine.Mathf.Clamp01(t * 0.7f + pulse * 0.3f);

        var outC = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        _circleOutline.startColor = outC;
        _circleOutline.endColor   = outC;

        _circleFillMat.color = UnityEngine.Color.Lerp(FillFaint, FillBright, blend);

        // 보스 위치 추적
        var center = _circleFollow.position;
        center.y += 0.05f;

        for (int i = 0; i <= CircleSegments; i++)
        {
            float a = i * UnityEngine.Mathf.PI * 2f / CircleSegments;
            _circleOutline.SetPosition(i, center + new UnityEngine.Vector3(
                UnityEngine.Mathf.Cos(a) * _circleRadius, 0f,
                UnityEngine.Mathf.Sin(a) * _circleRadius));
        }

        // 디스크 메시는 XZ 평면 — rotation identity 로 지면에 평행
        _circleFillGO.transform.position   = center;
        _circleFillGO.transform.rotation   = UnityEngine.Quaternion.identity;
        _circleFillGO.transform.localScale =
            new UnityEngine.Vector3(_circleRadius, 1f, _circleRadius);
    }

    private void ApplyChargeColor(float t)
    {
        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (8f + t * 14f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.7f + pulse * 0.3f);
        var c = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        _chargeOutline.startColor = c;
        _chargeOutline.endColor   = c;
    }

    private void UpdateDropZoneVisuals(float t)
    {
        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (5f + t * 12f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.7f + pulse * 0.3f);
        var outC = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        var filC = UnityEngine.Color.Lerp(FillFaint,    FillBright,   blend);

        for (int i = 0; i < MaxDropZones; i++)
        {
            if (!_dropActive[i]) continue;

            _dropOutlines[i].startColor = outC;
            _dropOutlines[i].endColor   = outC;
            _dropFillMats[i].color = filC;

            var center = _dropPositions[i];
            center.y = 0.05f;

            for (int j = 0; j <= CircleSegments; j++)
            {
                float a = j * UnityEngine.Mathf.PI * 2f / CircleSegments;
                _dropOutlines[i].SetPosition(j, center + new UnityEngine.Vector3(
                    UnityEngine.Mathf.Cos(a) * _dropRadius, 0f,
                    UnityEngine.Mathf.Sin(a) * _dropRadius));
            }

            _dropFillGOs[i].transform.position   = center;
            _dropFillGOs[i].transform.rotation   = UnityEngine.Quaternion.identity;
            _dropFillGOs[i].transform.localScale  =
                new UnityEngine.Vector3(_dropRadius, 1f, _dropRadius);
        }
    }

    private void UpdateLeapVisuals(float dt)
    {
        // 추적 타이머 감소
        if (!_leapLocked)
        {
            _leapTrackTimer -= dt;
            if (_leapFollow != null)
                _leapLockedPos = _leapFollow.position; // 아직 추적 중 — 위치 갱신

            if (_leapTrackTimer <= 0f)
                LockLeapTarget(_leapLockedPos);
        }

        // 진행도 0=흰색·소, 1=빨강·대 (잠금 상태에서는 즉시 최대)
        float t = _leapLocked
            ? 1f
            : 1f - UnityEngine.Mathf.Clamp01(_leapTrackTimer / _leapTotalTrack);

        // 반지름: _leapStartRadius → _leapRadius 로 성장
        float currentRadius = UnityEngine.Mathf.Lerp(_leapStartRadius, _leapRadius, t);

        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (4f + t * 14f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.8f + pulse * 0.2f);

        var outC = UnityEngine.Color.Lerp(LeapTrackColor, LeapLockColor, blend);
        _leapOutline.startColor = outC;
        _leapOutline.endColor   = outC;
        _leapFillMat.color = UnityEngine.Color.Lerp(LeapFillTrack, LeapFillLock, blend);

        var center = _leapLockedPos;
        center.y = 0.05f;

        for (int i = 0; i <= CircleSegments; i++)
        {
            float a = i * UnityEngine.Mathf.PI * 2f / CircleSegments;
            _leapOutline.SetPosition(i, center + new UnityEngine.Vector3(
                UnityEngine.Mathf.Cos(a) * currentRadius, 0f,
                UnityEngine.Mathf.Sin(a) * currentRadius));
        }

        _leapFillGO.transform.position   = center;
        _leapFillGO.transform.rotation   = UnityEngine.Quaternion.identity;
        _leapFillGO.transform.localScale  =
            new UnityEngine.Vector3(currentRadius, 1f, currentRadius);
    }

    private void UpdateScatterVisuals(float t)
    {
        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (6f + t * 10f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.6f + pulse * 0.4f);
        var c = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);

        for (int i = 0; i < MaxScatterLines; i++)
        {
            if (_scatterLineRenderers[i] != null
                && _scatterLineRenderers[i].gameObject.activeSelf)
            {
                _scatterLineRenderers[i].startColor = c;
                _scatterLineRenderers[i].endColor   = c;
            }
        }
    }

    private void UpdateFanVisuals(float t)
    {
        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (6f + t * 10f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.6f + pulse * 0.4f);
        var c = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        _fanOutline.startColor = c;
        _fanOutline.endColor   = c;
    }

    private void SetFanShape()
    {
        float y     = _fanOrigin.y + 0.05f;
        var   right = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, _fanForward).normalized;

        // 시작: origin
        _fanOutline.SetPosition(0, WithY(_fanOrigin, y));

        // 호 (FanArcSegments + 1 점)
        for (int i = 0; i <= FanArcSegments; i++)
        {
            float pct   = (float)i / FanArcSegments; // 0→1
            float angle = (-_fanHalfAngle + 2f * _fanHalfAngle * pct) * UnityEngine.Mathf.Deg2Rad;
            var   dir   = UnityEngine.Mathf.Cos(angle) * _fanForward
                        + UnityEngine.Mathf.Sin(angle) * right;
            _fanOutline.SetPosition(i + 1, WithY(_fanOrigin + dir * _fanRange, y));
        }

        // 닫기: origin 으로 귀환
        _fanOutline.SetPosition(FanArcSegments + 2, WithY(_fanOrigin, y));
    }

    /// <summary>화살표 모양 — 뒤가 넓고 앞이 뾰족.</summary>
    private void SetChargeShape(UnityEngine.Vector3 start, UnityEngine.Vector3 direction,
                                float length, float width)
    {
        var right  = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, direction).normalized
                     * (width * 0.5f);
        var midPos = start + direction * (length * 0.82f);
        var tip    = start + direction * length;
        float y    = start.y + 0.05f;

        _chargeOutline.SetPosition(0, WithY(start  - right, y));
        _chargeOutline.SetPosition(1, WithY(midPos - right, y));
        _chargeOutline.SetPosition(2, WithY(tip,            y)); // 뾰족한 끝
        _chargeOutline.SetPosition(3, WithY(midPos + right, y));
        _chargeOutline.SetPosition(4, WithY(start  + right, y));
        _chargeOutline.SetPosition(5, WithY(start  - right, y)); // 닫기
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 리소스 생성
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private UnityEngine.LineRenderer CreateLineRenderer(string goName)
    {
        var go = new UnityEngine.GameObject(goName);
        go.transform.SetParent(transform);
        var lr = go.AddComponent<UnityEngine.LineRenderer>();
        lr.useWorldSpace     = true;
        lr.loop              = false;
        lr.numCapVertices    = 4;
        lr.material          = CreateLineMaterial();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        return lr;
    }

    private static UnityEngine.Material CreateLineMaterial()
    {
        string[] candidates = { "Sprites/Default", "Unlit/Transparent", "UI/Default" };
        foreach (string sn in candidates)
        {
            var s = UnityEngine.Shader.Find(sn);
            if (s != null) return new UnityEngine.Material(s);
        }
        return new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
    }

    private static UnityEngine.Material CreateFillMaterial()
    {
        string[] candidates = { "Sprites/Default", "Unlit/Transparent", "UI/Default" };
        foreach (string sn in candidates)
        {
            var s = UnityEngine.Shader.Find(sn);
            if (s != null) return new UnityEngine.Material(s) { color = FillFaint };
        }
        return new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
    }

    /// <summary>XZ 평면 디스크 메시 (radius=1 기준, localScale 로 크기 조정).</summary>
    private static UnityEngine.Mesh GenerateDiscMesh(float radius, int segments)
    {
        var mesh  = new UnityEngine.Mesh { name = "WarnDisc" };
        var verts = new UnityEngine.Vector3[segments + 1];
        var tris  = new int[segments * 3];

        verts[0] = UnityEngine.Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a = i * UnityEngine.Mathf.PI * 2f / segments;
            verts[i + 1] = new UnityEngine.Vector3(
                UnityEngine.Mathf.Cos(a) * radius, 0f,
                UnityEngine.Mathf.Sin(a) * radius);
        }
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % segments + 1;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void SetLRActive(UnityEngine.LineRenderer lr, bool active) =>
        lr.gameObject.SetActive(active);

    private static UnityEngine.Vector3 WithY(UnityEngine.Vector3 v, float y) =>
        new UnityEngine.Vector3(v.x, y, v.z);
}
