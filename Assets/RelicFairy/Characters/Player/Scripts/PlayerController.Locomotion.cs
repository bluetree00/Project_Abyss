using UnityEngine;

// PlayerController — 이동 배율·공유 값 · 회전 · 조준 · 벽 접촉 · 이동 애니 파라미터
public sealed partial class PlayerController
{
    // ── Properties ────────────────────────────────────────────────
    /// <summary>
    /// 최종 이동 입력 배율 = 행동 잠금 채널(<see cref="SetMoveScale"/>) × 슬로우 채널(상태이상).
    /// 두 사유를 곱으로 합쳐, 한쪽이 풀려도 다른 쪽 제한이 사라지지 않는다.
    /// </summary>
    public float MoveScale => _actionMoveScale * Status.SlowScale * Status.HasteScale;   // 10-29 잠깐 빨라짐 채널

    /// <summary>현재 수평 실속도를 runMax 기준 0~1로 정규화. 애니 MoveSpeed 구동용(실속도라 가속·감속 반영 + 발미끄러짐 방지).</summary>
    public float HorizontalSpeed01
    {
        get
        {
            var cd = CharacterData;
            if (cd == null) return 0f;
            float runMax = cd.baseRunSpeed > 0.01f ? cd.baseRunSpeed : cd.baseMoveSpeed;
            if (runMax < 0.01f) return 0f;
            Vector3 v = Rigid.linearVelocity;
            float mag = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            return Mathf.Clamp01(mag / runMax);
        }
    }

    /// <summary>스텝 오르는 중인지(공중/낙하 상태 억제용).</summary>
    public bool IsStepClimbing => Time.time < _stepClimbUntil;

    /// <summary>
    /// 공격/스킬 등 Act 상태가 캐릭터 facing(회전)을 소유 중인지 여부.
    /// true면 이동 회전(DefaultMoveAbility)이 회전을 양보해 facing 경합을 막는다.
    /// (None 외의 모든 Act 상태 = 조준/공격이 회전을 주도)
    /// </summary>
    public bool IsActionControllingFacing => _actSM != null && _actSM.CurrentId != ActState.None;

    /// <summary>지금 <b>겨냥한</b> 정면(수평). 투사체·판정은 이 값을 써야 한다(<see cref="PlayerFacing.AimForward"/>).</summary>
    public Vector3 AimForward => _facing.AimForward(transform);

    private static readonly int MoveBlendHash = Animator.StringToHash("MoveBlend");

    // ── Public Methods: 이동 배율 · 공유 값 ───────────────────────
    /// <summary>행동 잠금 채널(공격·스킬·잡기)의 이동 배율을 지정한다. 0=정지, 1=평소.</summary>
    public void SetMoveScale(float s) => _actionMoveScale = Mathf.Clamp01(s);

    /// <summary>대시(우클릭) 종료 시 다음 이동을 달리기로 시작하도록 요청.</summary>
    public void RequestRunAfterDash() => _runAfterDash = true;
    /// <summary>대시 후 달리기 요청을 소비(읽고 클리어).</summary>
    public bool ConsumeRunAfterDash() { bool v = _runAfterDash; _runAfterDash = false; return v; }
    /// <summary>대시 후 달리기 요청 클리어(정지/Idle 시).</summary>
    public void ClearRunAfterDash() => _runAfterDash = false;

    /// <summary>회피 종료 등에서 다음 로코모션 크로스페이드를 길게 잡도록 예약한다.</summary>
    public void RequestLocoBlend(float duration) => _pendingLocoBlend = duration;

    /// <summary>예약된 크로스페이드 길이를 소비한다(1회). 없으면 기본값 반환.</summary>
    public float ConsumeLocoBlend(float defaultDuration)
    {
        if (_pendingLocoBlend < 0f) return defaultDuration;
        float d = _pendingLocoBlend;
        _pendingLocoBlend = -1f;
        return d;
    }

    /// <summary>
    /// 이동 블렌드 트리로 크로스페이드한다. 이미 이동 블렌드에 머물러 있으면(전환 중이 아님) 건너뛴다 —
    /// 대기 ↔ 이동 상태가 바뀔 때마다 같은 상태로 다시 크로스페이드해 보행 주기가 처음으로 돌아가 발이 툭 튀었다(10-09 출시 점검).
    /// </summary>
    public void CrossFadeMoveBlend(float duration)
    {
        if (Anim == null) return;
        if (!Anim.IsInTransition(0) && Anim.GetCurrentAnimatorStateInfo(0).shortNameHash == MoveBlendHash) return;
        Anim.CrossFadeInFixedTime(MoveBlendHash, duration);
    }

    /// <summary>접지 여부 — 접지 모듈(<see cref="IGroundingAbility"/>)이 관리하는 상태를 위임한다.</summary>
    public bool IsGrounded() => Grounding?.IsGrounded ?? true;

    /// <summary>StepClimb 상승 프레임에서 호출 — 짧은 유효시간 동안 IsStepClimbing 유지.</summary>
    public void MarkStepClimbing() => _stepClimbUntil = Time.time + 0.08f;

    public void StopHorizontalMovement()
    {
        Rigid.linearVelocity = new Vector3(0f, Rigid.linearVelocity.y, 0f);
    }

    /// <summary>수평 이동 목표에서 접촉 중인 벽으로 파고드는 성분만 제거한다(<see cref="PlayerWallContact.Clip"/>).</summary>
    public Vector2 ClipMoveTargetToWalls(Vector2 target) => _wallContact.Clip(target);

    // 앞길 재기 — 캡슐(반경 0.27)보다 가는 구(0.2)로 몸 가운데 높이를 훑는다. 가늘게 훑어야 벽에 붙어 선 채 반대쪽으로 갈 때
    // 시작부터 겹친 벽(거리 0)에 막히지 않는다. 멈출 자리 = 구가 닿은 곳 − (캡슐 반경 − 구 반경) − 틈.
    private const float ClearCastRadius = 0.2f, ClearCastHeight = 0.6f, ClearBodyRadius = 0.27f, ClearWallGap = 0.05f;
    private static readonly RaycastHit[] s_clearHits = new RaycastHit[16];

    /// <summary>
    /// <paramref name="from"/>에서 <paramref name="dir"/>로 막힘 없이 갈 수 있는 거리(m, 최대 <paramref name="max"/>) — 첫 벽 앞까지.
    /// 바닥 · 트리거 · 맞을 수 있는 적 · 자기 자신은 벽이 아니다(돌진은 적을 꿰뚫는다). 돌진 스킬 끝점 · 회피 대시가 함께 쓴다
    /// (10-14 사용자 「무형검 R · 대시 스킬이 벽을 뚫는다」 — 끝점 Lerp · 22 m/s 대시가 벽을 넘었다).
    /// </summary>
    public float ClearDistanceAhead(Vector3 from, Vector3 dir, float max)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f || max <= 0f) return 0f;
        dir.Normalize();
        int ground = CharacterData != null ? CharacterData.groundLayer.value : 0;
        int n = Physics.SphereCastNonAlloc(from + Vector3.up * ClearCastHeight, ClearCastRadius, dir, s_clearHits,
                                           max + ClearBodyRadius, ~ground, QueryTriggerInteraction.Ignore);
        float stop = max;
        for (int i = 0; i < n; i++)
        {
            var h = s_clearHits[i];
            if (h.collider == null || h.distance <= 0f) continue;   // 거리 0 = 시작부터 겹침(가는 구라 벽에 붙어 선 경우가 아니다)
            var t = h.collider.transform;
            if (t == transform || t.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<IDamageable>() != null) continue;
            float s = h.distance - (ClearBodyRadius - ClearCastRadius) - ClearWallGap;
            if (s < stop) stop = s;
        }
        return Mathf.Max(0f, stop);
    }

    // ── Public Methods: 회전 · 조준 ───────────────────────────────
    /// <summary>즉시(1회) 회전 지정. 스킬/회피/조준 등 한 프레임 스냅 또는 자체 보간 writer용. 진행 중인 이동 회전 슬루를 취소한다.
    /// 실제 적용은 FixedUpdate에서 Rigidbody.MoveRotation으로 수행.</summary>
    public void RequestFacing(Quaternion rot) => _facing.Request(rot);

    /// <summary>이동 회전 목표를 지정한다. 목표 Yaw로 degPerSec 각속도로 FixedUpdate에서 적분 → 프레임률 독립.</summary>
    public void RequestFacingSlew(float targetYaw, float degPerSec) => _facing.RequestSlew(targetYaw, degPerSec);

    /// <summary>이동 회전 슬루를 정지한다(정지/행동 양보 시). 현재 facing을 그대로 유지.</summary>
    public void StopFacingSlew() => _facing.StopSlew();

    /// <summary>
    /// 현재 이동 입력 방향으로 즉시 회전. 입력이 없으면 유지.
    /// </summary>
    public void RotateTowardsInput()
    {
        if (_moveDirection.sqrMagnitude < 0.0001f) return;
        RequestFacing(Quaternion.LookRotation(_moveDirection));
    }

    /// <summary>
    /// 장비 스킬(E · R) 조준(10-08 A1): 마우스 방향 + 마우스 콘 <see cref="SkillAimConeHalfAngle"/>° 안 가장 가까운 적으로 스냅(반경 <see cref="SkillAimRadius"/> m).
    /// 「겨눈 곳으로 간다」 — 회전은 요청만 하므로 같은 프레임의 방향은 <see cref="AimForward"/>로 읽는다.
    /// </summary>
    public Quaternion ComputeSkillAimRotation() => ComputeMouseAimAssistRotation(SkillAimRadius, SkillAimConeHalfAngle, 1f);

    /// <summary>장비 스킬 조준으로 즉시 회전(<see cref="ComputeSkillAimRotation"/>).</summary>
    public void RotateTowardsSkillAim() => RequestFacing(ComputeSkillAimRotation());

    public void RotateTowardsMousePosition()
    {
        if (_aim.TryComputeLookDir(transform, out var lookDir))
            RequestFacing(Quaternion.LookRotation(lookDir));
    }

    /// <summary>마우스가 가리키는 수평 방향만 읽는다(적용하지 않음) — 스킬 도중 천천히 다시 겨눌 때(랜슬롯 Q 연타 · 10-10).</summary>
    public bool TryGetMouseLookDir(out Vector3 dir) => _aim.TryComputeLookDir(transform, out dir);

    /// <summary>
    /// 마우스 + 에임 어시스트 적용 후의 최종 목표 회전을 "계산만" 해서 반환한다.
    /// 호출자에서 즉시 적용하거나 lerp 시작점으로 사용. 적용은 하지 않음.
    /// </summary>
    public Quaternion ComputeMouseAimAssistRotation(float radius, float coneHalfAngleDeg, float strength)
        => _aim.ComputeAimAssistRotation(transform, radius, coneHalfAngleDeg, strength, out _, out _);

    /// <summary>
    /// 위와 동일하되, 콘 안에서 선택된 적(IDamageable)과 그 수평 거리를 함께 반환한다.
    /// 런지(전진)가 좁은 SphereCast 대신 이 OverlapSphere 기반 타겟을 재사용해 인식 안정성을 높이기 위함.
    /// </summary>
    public Quaternion ComputeMouseAimAssistRotation(float radius, float coneHalfAngleDeg, float strength,
                                                    out Transform enemy, out float enemyPlanarDist)
        => _aim.ComputeAimAssistRotation(transform, radius, coneHalfAngleDeg, strength, out enemy, out enemyPlanarDist);

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 이동 방향 애니 파라미터(MoveX/MoveY) — 2D 블렌드 트리가 몸 기준 로컬 속도를 좌표로 쓴다.
    ///
    /// 왜 로컬인가 — facing은 즉시 돌지 않고 turnSpeedDegPerSec로 슬루한다. 그래서 전환 중엔
    /// 항상 '몸 방향 ≠ 진행 방향'이고, 그 각차가 곧 블렌드 좌표다. 이 값이 있어야
    /// 선회 중 몸이 기우는 그림이 나온다 — 없으면 어느 쪽으로 틀든 정면 클립만 돌아 미끄러진다.
    /// </summary>
    private void UpdateLocomotionDirection()
    {
        if (Anim == null || Rigid == null || CharacterData == null) return;

        Vector3 v = Rigid.linearVelocity;
        v.y = 0f;

        // 정규화 기준은 달리기 속도 — 걷기는 자연히 0.625 언저리에 떨어져 걷기 링에 붙는다.
        float runSpeed = CharacterData.baseRunSpeed > 0.01f ? CharacterData.baseRunSpeed : 8f;
        Vector3 local  = transform.InverseTransformDirection(v) / runSpeed;

        Anim.SetFloat(MoveXHash, Mathf.Clamp(local.x, -1f, 1f), MoveDirDamp, Time.deltaTime);
        Anim.SetFloat(MoveYHash, Mathf.Clamp(local.z, -1f, 1f), MoveDirDamp, Time.deltaTime);
    }
}
