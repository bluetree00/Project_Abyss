using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스 조준 계산 — 클릭 지점 캐시, 마우스가 가리키는 수평 방향, 에임 어시스트(콘 안 적 보정).
/// PlayerController가 소유하며, 회전을 <b>계산만</b> 한다(적용은 <see cref="PlayerFacing"/>).
/// </summary>
public sealed class PlayerAim
{
    // ── Private ───────────────────────────────────────────────────
    // 공격 입력 순간 캐시한 클릭 월드 좌표. 다음 방향 계산에서 1회 소비된다.
    private Vector3? _lastClickedPosition;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 공격 입력 순간의 마우스 월드 위치를 캐시한다(탑뷰 포함 모든 카메라 상태).
    /// Ground 레이어 레이캐스트 우선, 미스 시 플레이어 높이의 수평 평면으로 폴백.
    /// </summary>
    public void RecordClick(Transform body)
    {
        Ray atkRay = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(atkRay, out var hit, 200f, LayerMask.GetMask("Ground")))
            _lastClickedPosition = hit.point;
        else
        {
            // 지면 레이어 미스 시 Y=player 높이 평면으로 폴백 (탑뷰 대응)
            var groundPlane = new Plane(Vector3.up, body.position);
            if (groundPlane.Raycast(atkRay, out float atkDist))
                _lastClickedPosition = atkRay.GetPoint(atkDist);
        }
    }

    /// <summary>
    /// 마우스 + 에임 어시스트 적용 후의 최종 목표 회전을 "계산만" 해서 반환한다.
    /// 콘 안에서 선택된 적(IDamageable)과 그 수평 거리를 함께 반환한다 —
    /// 런지(전진)가 좁은 SphereCast 대신 이 OverlapSphere 기반 타겟을 재사용해 인식 안정성을 높이기 위함.
    /// </summary>
    /// <param name="radius">적 탐색 거리(m)</param>
    /// <param name="coneHalfAngleDeg">마우스 방향 콘 반각(도)</param>
    /// <param name="strength">마우스 → 적 방향 블렌드 비율 (0=마우스, 1=적)</param>
    public Quaternion ComputeAimAssistRotation(Transform body, float radius, float coneHalfAngleDeg, float strength,
                                               out Transform enemy, out float enemyPlanarDist)
    {
        enemy = null;
        enemyPlanarDist = 0f;

        if (!TryComputeLookDir(body, out var mouseDir))
            return body.rotation;

        // 보정 비활성 케이스: 마우스 방향 그대로
        if (radius <= 0f || coneHalfAngleDeg <= 0f || strength <= 0f)
            return Quaternion.LookRotation(mouseDir);

        // 콘 안 가장 작은 각도의 적 탐색
        Vector3 origin = body.position;
        float cosThreshold = Mathf.Cos(coneHalfAngleDeg * Mathf.Deg2Rad);
        float bestDot = cosThreshold;
        Vector3 bestDir = mouseDir;
        Transform bestEnemy = null;
        float bestDist = 0f;
        bool found = false;

        var cols = Physics.OverlapSphere(origin, radius);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null) continue;
            if (col.transform == body || col.transform.IsChildOf(body)) continue;

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            var dt = (d as Component) != null ? (d as Component).transform : null;
            if (dt == null) continue;

            Vector3 toEnemy = dt.position - origin;
            toEnemy.y = 0f;
            float sqr = toEnemy.sqrMagnitude;
            if (sqr < 0.01f) continue;

            float dist = Mathf.Sqrt(sqr);
            Vector3 enemyDir = toEnemy / dist;
            float dot = Vector3.Dot(mouseDir, enemyDir);
            if (dot >= bestDot)
            {
                bestDot = dot;
                bestDir = enemyDir;
                bestEnemy = dt;
                bestDist = dist;
                found = true;
            }
        }

        if (found)
        {
            enemy = bestEnemy;
            enemyPlanarDist = bestDist;
        }

        Vector3 finalDir = found
            ? Vector3.Slerp(mouseDir, bestDir, Mathf.Clamp01(strength))
            : mouseDir;

        return finalDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(finalDir)
            : body.rotation;
    }

    /// <summary>
    /// 마우스 / 마지막 클릭 위치에서 수평 방향 벡터를 계산. 실패 시 false.
    /// 1) 입력 시 캐시된 클릭 월드 좌표(1회 소비)
    /// 2) Ground 레이어 콜라이더 레이캐스트
    /// 3) 플레이어 Y 높이의 수학적 수평 평면에 레이 투영 (콜라이더 미스 시 폴백)
    /// </summary>
    public bool TryComputeLookDir(Transform body, out Vector3 lookDir)
    {
        // 1) 입력으로 저장된 클릭 위치 우선 사용
        if (_lastClickedPosition.HasValue)
        {
            Vector3 target = _lastClickedPosition.Value;
            lookDir = target - body.position;
            lookDir.y = 0f;
            _lastClickedPosition = null;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                lookDir.Normalize();
                return true;
            }
        }

        // 2/3) 마우스 → 카메라 레이 → Ground 콜라이더 우선, 미스 시 수평 평면 폴백
        if (Camera.main != null && Mouse.current != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

            // 2) Ground 콜라이더 히트
            if (Physics.Raycast(ray, out var hit, 100f, LayerMask.GetMask("Ground")))
            {
                lookDir = hit.point - body.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    lookDir.Normalize();
                    return true;
                }
            }

            // 3) 플레이어 Y 높이의 수평 평면에 레이 투영 — Ground 콜라이더 누락/미스 대비
            var plane = new Plane(Vector3.up, body.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 point = ray.GetPoint(enter);
                lookDir = point - body.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    lookDir.Normalize();
                    return true;
                }
            }
        }

        lookDir = Vector3.zero;
        return false;
    }
}
