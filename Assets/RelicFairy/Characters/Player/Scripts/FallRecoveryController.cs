using UnityEngine;

/// <summary>
/// 플레이어 낙사 복구 컨트롤러.
/// SafeFloor 이하로 떨어지면 마지막 안전 지점(땅을 밟았던 위치)으로 텔레포트 + HP 감소 + 무적.
///
/// 동작:
///   · Update: 플레이어가 Ground layer 위에 있으면 현재 위치를 _lastSafe 로 갱신
///   · transform.position.y &lt; fallThresholdY → 복구 절차 실행
///     (방이 <see cref="SetFallDepthOverride"/>로 덮어쓰면 '마지막 안전 지점 − 깊이'로 판정)
///
/// 복구 절차:
///   1. RuntimeStats.MaxHp × fallDamageRatio 만큼 HP 감소 (passive 트리거 없이 직접)
///   2. PlayerController.SetInvincible(invincibleSeconds) 로 리스폰 직후 짧게 무적
///   3. 위치를 _lastSafe + respawnOffsetY 로 이동, 속도 리셋
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class FallRecoveryController : MonoBehaviour
{
    // ── 상수 ───────────────────────────────────────────────
    /// <summary>안전 지점의 바닥이 사라졌을 때 바깥으로 넓혀가며 대체 바닥을 찾는 링 반경(m).</summary>
    private static readonly float[] SearchRadii = { 2f, 4f, 7f, 11f, 16f };

    /// <summary>각 링에서 검사할 8방위 (XZ 단위벡터).</summary>
    private static readonly Vector2[] SearchDirs =
    {
        new( 1f,      0f     ), new( 0.707f,  0.707f), new( 0f,  1f     ), new(-0.707f,  0.707f),
        new(-1f,      0f     ), new(-0.707f, -0.707f), new( 0f, -1f     ), new( 0.707f, -0.707f),
    };

    // ── 낙하 깊이 덮어쓰기(방 단위) ─────────────────────────
    // 절대 높이 판정은 바닥이 y≈0인 방에서 5m만 떨어져도 복구돼 '떨어지는 감각'이 없다(리치 아레나 붕괴 바닥).
    // 주인은 Unity 객체 — 파괴되면 == null이 되어 덮어쓰기가 저절로 풀린다(해제를 놓쳐도 남지 않는다).
    private static Object s_depthOwner;
    private static float  s_depthBelowLastSafe;

    [SerializeField, Tooltip("이 Y 이하로 떨어지면 낙사 판정. SafeFloor(=baseY-0.05) 보다 충분히 아래로.")]
    private float fallThresholdY = -5f;

    [SerializeField, Tooltip("지면 체크 레이어. 기본 Ground=3.")]
    private LayerMask groundLayer = 1 << 3;

    [SerializeField, Tooltip("지면 체크 Sphere 반경.")]
    private float groundCheckRadius = 0.35f;

    [SerializeField, Tooltip("지면 체크 Sphere 중심의 Y 오프셋 (발 위쪽).")]
    private float groundCheckCenterY = 0.3f;

    [SerializeField, Tooltip("리스폰 시 _lastSafe 위에 얹어지는 Y 오프셋 (자연스럽게 착지).")]
    private float respawnOffsetY = 0.5f;

    [SerializeField, Range(0f, 1f), Tooltip("낙사 시 최대 HP 대비 피해 비율.")]
    private float fallDamageRatio = 0.1f;

    [SerializeField, Min(0f), Tooltip("낙사 복구 직후 무적 시간(초).")]
    private float invincibleSeconds = 1.0f;

    [SerializeField, Min(1f), Tooltip("리스폰 전 바닥 실존 확인용 레이캐스트 시작 높이(m). 밟고 있던 지형이 사라졌는지 판정한다.")]
    private float groundProbeHeight = 60f;

    private PlayerController _pc;
    private Rigidbody _rb;
    private Vector3 _lastSafe;
    private bool _hasSafe;
    private bool _recovering;

    /// <summary>지금 적용되는 낙사 판정 높이 — 덮어쓰기가 있으면 마지막 안전 지점 기준, 없으면 절대 높이.</summary>
    private float CurrentFallThresholdY =>
        s_depthOwner != null ? _lastSafe.y - s_depthBelowLastSafe : fallThresholdY;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody>();
        _lastSafe = transform.position;
        _hasSafe = true;
    }

    private void Update()
    {
        if (_recovering) return;

        // 1) 낙사 판정
        if (transform.position.y < CurrentFallThresholdY)
        {
            Recover();
            return;
        }

        // 2) 지면 위 여부 확인 → 안전지점 갱신
        if (IsGrounded())
        {
            _lastSafe = transform.position;
            _hasSafe = true;
        }
    }

    private bool IsGrounded()
    {
        Vector3 center = transform.position + new Vector3(0f, groundCheckCenterY, 0f);
        return Physics.CheckSphere(center, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>PitTrigger 등 외부에서 낙사 복구를 즉시 발동한다.</summary>
    public void ForceRecover() => Recover();

    /// <summary>
    /// 낙사 판정을 '마지막으로 밟은 안전 지점보다 <paramref name="depthBelowLastSafe"/> m 아래'로 바꾼다.
    /// 무너지는 전장처럼 떨어지는 감각이 필요한 방이 켜고, 방을 떠날 때 <see cref="ClearFallDepthOverride"/>로 끈다.
    /// 마지막 호출이 이긴다. <paramref name="owner"/>가 파괴되면 저절로 풀린다.
    /// </summary>
    public static void SetFallDepthOverride(Object owner, float depthBelowLastSafe)
    {
        if (owner == null) return;
        s_depthOwner         = owner;
        s_depthBelowLastSafe = Mathf.Max(0f, depthBelowLastSafe);
    }

    /// <summary>덮어쓰기를 끈다 — 건 주인만 끌 수 있다(다른 방이 뒤늦게 부른 해제가 지금 방의 설정을 지우지 않게).</summary>
    public static void ClearFallDepthOverride(Object owner)
    {
        if (owner != null && s_depthOwner == owner) s_depthOwner = null;
    }

    private void Recover()
    {
        _recovering = true;

        // HP 차감 — passive 트리거 없이 직접 (낙사는 특수 원인).
        // 베이스캠프(허브)에선 깎지 않는다 — 떠 있는 광장은 투명 벽이 막지만, 그래도 떨어지면 벌이 아니라 제자리로(재설계 §4).
        bool inHub = BaseCampBootstrapper.Instance != null;
        if (!inHub && _pc != null && _pc.RuntimeStats != null)
        {
            int maxHp = _pc.RuntimeStats.MaxHp;
            int dmg = Mathf.Max(1, Mathf.RoundToInt(maxHp * fallDamageRatio));
            _pc.RuntimeStats.Damage(dmg);

            // 사망 판정은 PlayerController.TakeDamage 안에서만 돌기 때문에, 직접 차감으로 HP가 0이 되면
            // 죽지 않은 채 남아 다음 피격까지 살아 있었다. 낙사로 HP가 바닥나면 여기서 즉시 사망 처리.
            if (_pc.RuntimeStats.Hp <= 0) _pc.NotifyHpDepleted();
        }

        // 리스폰 위치 — 안전 지점 아래에 바닥이 "지금도" 남아 있는지 확인한 뒤 결정한다.
        Vector3 candidate = _hasSafe
            ? _lastSafe
            : new Vector3(transform.position.x, respawnOffsetY, transform.position.z);

        if (!TryResolveRespawn(candidate, out Vector3 target))
        {
            target = candidate + new Vector3(0f, respawnOffsetY, 0f);
            Debug.LogWarning($"[FallRecovery] {candidate} 주변에서 바닥을 찾지 못했다 — 원래 좌표로 복구한다.", this);
        }

        transform.position = target;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // 짧은 무적
        _pc?.SetInvincible(invincibleSeconds);

        _recovering = false;
    }

    /// <summary>
    /// 리스폰 좌표를 실제로 남아 있는 바닥 위로 보정한다.
    ///
    /// 지형이 전투 중 붕괴하는 전장(리치 보스 아레나 등)에서는 _lastSafe가 이미 사라진 타일의
    /// 좌표일 수 있다. 그대로 리스폰하면 허공에서 다시 추락 → 또 리스폰이 반복돼
    /// HP가 바닥날 때까지 빠져나올 수 없다. 바닥이 없으면 주변 링을 넓혀가며 대체 지점을 찾는다.
    /// </summary>
    private bool TryResolveRespawn(Vector3 candidate, out Vector3 result)
    {
        if (TryFindGroundY(candidate, out float y))
        {
            result = new Vector3(candidate.x, y + respawnOffsetY, candidate.z);
            return true;
        }

        for (int r = 0; r < SearchRadii.Length; r++)
        {
            for (int d = 0; d < SearchDirs.Length; d++)
            {
                var probe = new Vector3(candidate.x + SearchDirs[d].x * SearchRadii[r],
                                        candidate.y,
                                        candidate.z + SearchDirs[d].y * SearchRadii[r]);
                if (!TryFindGroundY(probe, out float py)) continue;

                result = new Vector3(probe.x, py + respawnOffsetY, probe.z);
                return true;
            }
        }

        result = candidate;
        return false;
    }

    /// <summary>주어진 XZ 아래로 레이를 쏴 실제 바닥이 있으면 true를 반환하고 그 높이를 넘긴다.</summary>
    private bool TryFindGroundY(Vector3 at, out float groundY)
    {
        var origin = new Vector3(at.x, at.y + groundProbeHeight, at.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f,
                            groundLayer, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        groundY = 0f;
        return false;
    }
}
