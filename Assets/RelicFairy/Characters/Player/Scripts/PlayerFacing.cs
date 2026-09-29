using UnityEngine;

/// <summary>
/// 플레이어 회전(facing)의 <b>단일 적용 주체</b>. PlayerController가 소유하고 FixedUpdate에서 <see cref="Apply"/>한다.
///
/// 회전을 바꾸고 싶은 쪽(이동·공격·스킬·보스 패턴)은 목표만 남기고, Rigidbody 회전은 여기서만 바꾼다.
/// Update에서 회전을 직접 대입하면 Rigidbody Interpolate 보간과 타이밍이 어긋나 회전 각도에서 진동이 생긴다.
///
///   · <see cref="Request"/>     : 즉시(1회) 회전. 스킬/회피/조준 등. 진행 중인 이동 슬루를 취소한다.
///   · <see cref="RequestSlew"/> : 이동 회전. 목표 Yaw로 일정 각속도 적분(프레임률 독립).
///   · 둘 다 없을 때             : 마지막 목표를 매 스텝 다시 확정(접촉 토크로 몸이 돌아가는 것 차단).
/// </summary>
public sealed class PlayerFacing
{
    // ── Constants ─────────────────────────────────────────────────
    // 슬루 ease-out — 목표까지 남은 각이 이 값(도) 이하면 각속도를 부드럽게 줄여 짧은 회전·마무리를 매끄럽게 한다.
    // ease-in은 두지 않는다(시작은 전속력) → 방향전환 반응성/선회감 제거 유지, 끝만 부드럽게 안착.
    //
    // 이 꼬리는 각도 폭이 고정이라(SmoothStep이 t를 clamp하므로 남은 각 > EaseOutAngle 구간은 이미 전속)
    // 큰 회전일수록 비중이 커지지는 않는다. 실제 손실원은 EaseFloor였다 — 0.18이면 마지막 ~9°를
    // 130°/s로 기어서 그 구간만 0.073s를 먹었고, 180° 반전이 0.36s가 되어 속도 반전(≈0.15s)보다 크게 느렸다.
    // 밴드를 25°로 좁히고 바닥을 0.5로 올려 180° 반전을 0.27s로 당긴다(ease 없는 이론 하한 0.25s).
    // 각속도 상한(turnSpeedDegPerSec 720)은 건드리지 않는다 — 전환의 시각 표현이 회전뿐이라
    // 각속도를 올리면 "휙 도는" 인상이 강해진다.
    private const float SlewEaseOutAngle = 25f;
    private const float SlewEaseFloor    = 0.5f; // 목표 직전 정체 방지용 최저 속도비

    // ── Private ───────────────────────────────────────────────────
    // 기본값은 (0,0,0,0) 영 쿼터니언 — Reset 전에는 AimForward가 방향을 못 내고 transform.forward로 폴백한다.
    private Quaternion _target;
    private bool       _dirty;

    // 이동 회전(슬루) — 목표 Yaw를 향해 일정 각속도로 FixedUpdate에서 적분한다.
    // (Update에서 step을 계산하면 Rigidbody.rotation이 물리 스텝에서만 갱신돼 고FPS에서 회전이 느려지는 프레임률 의존 발생 → FixedUpdate 적분으로 해소)
    private bool  _slewActive;
    private float _slewTargetYaw;
    private float _slewDegPerSec;

    // 잘못된 회전값을 이미 알렸는가(로그 1회용).
    private bool _warnedInvalid;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>회전 목표를 현재 자세로 맞춘다(초기화 시 1회).</summary>
    public void Reset(Quaternion current) => _target = IsUsable(current) ? current : Quaternion.identity;

    /// <summary>즉시(1회) 회전 지정. 실제 적용은 다음 <see cref="Apply"/>에서 Rigidbody.MoveRotation으로 수행.</summary>
    public void Request(Quaternion rot)
    {
        // 단위길이가 아니거나 NaN인 값은 <b>받지 않는다</b>. 한 번 들어오면 유휴 분기가 매 물리 스텝 다시 적용해
        // "Rotation quaternions must be unit length" 경고가 끝없이 나오고, 그동안 facing이 고장난 채로 남는다
        // (09-21 보스 세션 보고 — 한 로그에 65회). 경고의 스택에 요청한 쪽이 찍히므로 원인을 바로 찾을 수 있다.
        if (!IsUsable(rot)) { WarnInvalidOnce(nameof(Request), rot.ToString("F3")); return; }
        _target = rot;
        _dirty  = true;
    }

    /// <summary>이동 회전 목표를 지정한다. 목표 Yaw로 degPerSec 각속도로 적분 → 프레임률 독립.</summary>
    public void RequestSlew(float targetYaw, float degPerSec)
    {
        // NaN/무한 yaw는 Quaternion.Euler를 통해 그대로 _target을 오염시킨다 — 같은 이유로 문 앞에서 막는다.
        if (!IsFinite(targetYaw) || !IsFinite(degPerSec))
        {
            WarnInvalidOnce(nameof(RequestSlew), $"yaw {targetYaw} · degPerSec {degPerSec}");
            return;
        }
        _slewTargetYaw = targetYaw;
        _slewDegPerSec = degPerSec;
        _slewActive    = true;
    }

    /// <summary>이동 회전 슬루를 정지한다(정지/행동 양보 시). 현재 facing을 그대로 유지.</summary>
    public void StopSlew() => _slewActive = false;

    /// <summary>
    /// 지금 <b>겨냥한</b> 정면(수평). 투사체·판정은 이 값을 써야 한다.
    ///
    /// 회전은 <see cref="Request"/>가 목표만 적어두고 FixedUpdate에서 적용된 뒤 Interpolate 보간까지 거친다.
    /// 그래서 <c>transform.forward</c>는 명령보다 최대 한 물리 스텝(기본 0.02s) 뒤처진다.
    /// 공격이 느릴 때는 그 사이 물리가 여러 번 돌아 티가 안 나지만, <b>공격속도가 빨라지면
    /// 조준 요청과 발사 사이가 물리 틱보다 짧아져 화살이 이전 방향으로 나간다.</b>
    ///
    /// 이동 회전(슬루) 중에는 목표가 '앞으로 돌아갈 각도'라 조준이 아니므로, 그때는 실제 각도를 그대로 쓴다.
    /// </summary>
    public Vector3 AimForward(Transform body)
    {
        // 직접 지정(조준)이 아직 적용 대기 중이면 그게 의도다 — 이동 슬루보다 우선한다.
        // Request는 슬루를 그 자리에서 끄지 않고 Apply가 끄므로, 이 우선순위가
        // 없으면 '이동 중 공격'에서 다시 옛 방향으로 새어나간다.
        if (!_dirty && _slewActive) return body.forward;

        Vector3 f = _target * Vector3.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : body.forward;
    }

    /// <summary>
    /// 회전 목표를 Rigidbody에 적용. FixedUpdate에서만 호출한다.
    /// MoveRotation은 물리 스텝에 동기화돼 Interpolate 보간과 충돌(진동)하지 않는다.
    /// </summary>
    public void Apply(Rigidbody rb)
    {
        if (rb == null) return;

        // 문 앞(Request/RequestSlew)에서 막지 못한 경로로 값이 망가졌을 때의 마지막 방어선.
        // 망가진 채 두면 MoveRotation이 매 스텝 거부하고 몸이 그 자세에 묶인다 → 현재 자세로 되돌린다.
        if (!IsUsable(_target))
        {
            WarnInvalidOnce(nameof(Apply), _target.ToString("F3"));
            _target = IsUsable(rb.rotation) ? rb.rotation : Quaternion.identity;
        }

        // 즉시 회전 요청이 우선 — 적용 후 이동 슬루를 취소(직접 지정이 이동 회전을 덮어쓴다).
        if (_dirty)
        {
            // MoveRotation: Interpolate 보간과 정합되는 회전 적용(텔레포트 대입은 보간과 어긋나 진동).
            // Y축 회전 freeze가 풀려 있어야 적용된다(X/Z는 freeze 유지로 넘어짐 방지).
            rb.MoveRotation(_target);
            _dirty      = false;
            _slewActive = false;
            return;
        }

        // 이동 회전 슬루 — 물리 스텝마다 fixedDeltaTime으로 적분(프레임률 독립, Rigidbody.rotation stale-read 없음).
        if (_slewActive)
        {
            float cur = rb.rotation.eulerAngles.y;
            // 목표 근처에서만 각속도를 ease-out(시작은 전속력 유지). 짧은 회전은 통째로 부드럽고, 큰 회전은 마지막만 매끄럽게 안착.
            float remaining = Mathf.Abs(Mathf.DeltaAngle(cur, _slewTargetYaw));
            float ease = Mathf.Max(SlewEaseFloor, Mathf.SmoothStep(0f, 1f, remaining / SlewEaseOutAngle));
            float next = Mathf.MoveTowardsAngle(cur, _slewTargetYaw, _slewDegPerSec * ease * Time.fixedDeltaTime);
            // rb.rotation이 이미 망가져 있으면(물리 폭주 등) cur가 NaN이라 next도 NaN이 된다 — 슬루를 접고 현 자세를 지킨다.
            if (!IsFinite(next))
            {
                WarnInvalidOnce("Apply(슬루)", $"현재각 {cur} · 목표각 {_slewTargetYaw}");
                _slewActive = false;
            }
            else
            {
                // 슬루 결과를 목표에 계속 반영해 둔다 — 슬루가 끝나 아래 유지 분기로 넘어갈 때
                // 이어받을 값이 최신이어야 마지막 각도에서 그대로 멈춘다(옛 목표로 튀지 않음).
                _target = Quaternion.Euler(0f, next, 0f);
                rb.MoveRotation(_target);
                return;
            }
        }

        // 유휴 — 마지막으로 명령한 facing을 매 물리 스텝 다시 확정한다.
        //
        // Y축 회전은 제약이 풀려 있다(m_Constraints=80은 X/Z만 freeze). 그래서 오브젝트에 몸이 닿으면
        // 접촉 토크로 몸이 돌아가는데, FreezeRotation()은 물리 스텝 <b>전에</b> 각속도만 0으로 만들 뿐이라
        // 스텝 중에 이미 생긴 회전은 되돌리지 못한다. 예전엔 이 분기가 없어(아무것도 쓰지 않고 빠져나감)
        // 그 회전이 그대로 누적돼, 벽에 몸을 비비면 캐릭터가 조금씩 틀어졌다.
        rb.MoveRotation(_target);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>MoveRotation이 받아 주는 값인가 — NaN이 아니고 길이가 1인 쿼터니언.</summary>
    private static bool IsUsable(Quaternion q)
    {
        float sqr = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        return IsFinite(sqr) && Mathf.Abs(sqr - 1f) < 0.01f;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>
    /// 잘못된 회전값이 들어왔다 — 한 번만 알린다(물리 스텝마다 찍히면 콘솔이 막힌다).
    /// 경고에 딸려 나오는 스택에 <b>값을 넣은 쪽</b>이 찍히므로, 그게 곧 원인 위치다.
    /// </summary>
    private void WarnInvalidOnce(string where, string value)
    {
        if (_warnedInvalid) return;
        _warnedInvalid = true;
        Debug.LogWarning($"[PlayerFacing] {where}에 쓸 수 없는 회전값이 들어와 무시했다 — {value}. 아래 스택이 값을 넣은 쪽이다.");
    }
}
