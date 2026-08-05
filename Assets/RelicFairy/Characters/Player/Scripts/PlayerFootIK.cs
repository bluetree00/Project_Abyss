using UnityEngine;

/// <summary>
/// 발을 매 프레임 지면에 맞춰 배치하는 Foot IK.
///
/// 캡슐 콜라이더는 <b>하나의 고정 높이</b>만 정한다. 그런데 실측해 보면 시각적 발 높이는
/// 애니메이션 프레임마다 -0.03 ~ +0.26m 로 변한다(정지 자세 · 무게중심 이동 · 달리기 체공).
/// 그래서 캡슐 오프셋을 어떤 값으로 잡아도 절반의 포즈에서는 뜨거나 파묻힌다.
/// 발을 개별적으로 지면에 붙이는 것은 IK 말고는 방법이 없다.
///
/// 처리 순서가 중요하다.
///   1) 발 아래로 레이를 쏴 각 발의 목표 높이를 구한다
///   2) 더 낮은 쪽에 맞춰 골반을 내린다 — 골반을 먼저 내려야 반대쪽 다리가 펴져 닿는다
///   3) 그 위에서 발 IK 목표를 건다
/// 골반을 나중에 옮기면 발 목표가 어긋나 다리가 끊겨 보인다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerFootIK : MonoBehaviour
{
    // 발 위 이 높이에서 아래로 레이를 쏜다. 발이 지면에 파묻힌 프레임도 잡아야 하므로 넉넉히.
    private const float RayStartHeight = 0.5f;
    private const float RayLength      = 1.2f;

    // 이 가중치를 넘어야 '딛고 있는 발'로 보고 골반 높이 계산에 넣는다.
    private const float PlantThreshold = 0.35f;

    [Header("Ground")]
    [Tooltip("끄면 IK 를 일절 적용하지 않는다. 문제 발생 시 즉시 무력화용.")]
    [SerializeField] private bool enableIK = true;

    [Tooltip("지면으로 인정할 레이어. 비워두면 Ground 레이어를 찾아 쓴다.")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Tuning")]
    [Tooltip("발바닥을 지면에서 띄울 높이(신발 두께 보정). 발이 파묻히면 올린다.")]
    [SerializeField, Range(0f, 0.2f)] private float footOffset = 0.02f;

    [Tooltip("골반을 내릴 수 있는 최대 거리. 계단·경사에서 두 발 높이차를 흡수한다.")]
    [SerializeField, Range(0f, 0.6f)] private float maxPelvisDrop = 0.35f;

    [Tooltip("발 목표 높이가 이보다 멀면 무시한다(허공/절벽에서 다리가 늘어나는 것 방지).")]
    [SerializeField, Range(0.1f, 1f)] private float maxFootReach = 0.5f;

    [Tooltip("이 높이 이상 발이 뜨면 IK 가중치를 0으로 본다(= 딛는 발이 아니다).\n" +
             "걷기·달리기에서 앞으로 나가는 발까지 지면에 고정하면 다리가 끌려 애니메이션이 망가진다.\n" +
             "정석은 클립마다 IK 커브를 저작하는 것이고, 이 값은 커브 없이 쓰는 대체 수단이다.")]
    [SerializeField, Range(0.02f, 0.4f)] private float footPlantRange = 0.12f;

    [Tooltip("발이 이 속도(m/s)보다 빠르게 수평 이동하면 딛는 발로 보지 않는다.\n" +
             "높이만으로 판별하면 낮게 뜬 채 앞으로 미끄러지는 스윙 발까지 못 박혀\n" +
             "그 다리가 끌리고 '한 다리로 기어가는' 모습이 된다.")]
    [SerializeField, Range(0.2f, 4f)] private float footSwingSpeed = 1.2f;

    [Tooltip("IK 목표 위치 추종 속도. 이 보간이 없으면 계단 모서리에서 목표가 한 단만큼\n" +
             "순간 이동해 발이 튕겨 나갔다 돌아온다.")]
    [SerializeField, Range(1f, 40f)] private float positionLerpSpeed = 15f;

    [Tooltip("IK 가중치 전환 속도. 낮을수록 부드럽지만 늦게 붙는다.")]
    [SerializeField, Range(1f, 30f)] private float weightLerpSpeed = 14f;

    [Tooltip("골반 이동 보간 속도. 낮을수록 부드럽지만 계단에서 늦게 따라온다.")]
    [SerializeField, Range(1f, 30f)] private float pelvisLerpSpeed = 12f;

    private Animator _animator;
    private PlayerController _controller;

    private float _pelvisOffset;
    private float _leftWeight;
    private float _rightWeight;

    // 발별 상태 — [0]=왼발, [1]=오른발.
    // 목표 위치는 보간해서 따라가고(계단 모서리에서 튕김 방지),
    // 직전 위치는 수평 속도를 구해 스윙 발을 가려내는 데 쓴다.
    private readonly Vector3[] _smoothedTarget = new Vector3[2];
    private readonly Vector3[] _prevFootPos    = new Vector3[2];
    private readonly bool[]    _targetPrimed   = new bool[2];

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        TryGetComponent(out _controller);

        if (groundLayer.value == 0)
        {
            int ground = LayerMask.NameToLayer("Ground");
            groundLayer = ground >= 0 ? 1 << ground : ~0;
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        // OnAnimatorIK 는 IK Pass 가 켜진 레이어마다 호출된다.
        // 레이어를 가리지 않으면 bodyPosition 보정과 가중치 보간이 프레임당 여러 번 누적돼
        // 골반이 계속 내려가고 다리가 무너진다. 베이스 레이어에서만 처리한다.
        if (layerIndex != 0) return;
        if (_animator == null || !_animator.isHuman) return;

        if (!enableIK)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            return;
        }

        // 공중에서는 IK 를 풀어야 한다. 안 그러면 점프·낙하 중에 발이 지면에 붙어 다리가 늘어난다.
        bool grounded = _controller == null || _controller.IsGrounded();
        float targetWeight = grounded ? 1f : 0f;

        bool hasLeft  = TrySolveFoot(HumanBodyBones.LeftFoot,  AvatarIKGoal.LeftFoot,  0,
                                     out Vector3 leftPos,  out Quaternion leftRot,
                                     out float leftGroundY,  out float leftLift,  out float leftSpeed);
        bool hasRight = TrySolveFoot(HumanBodyBones.RightFoot, AvatarIKGoal.RightFoot, 1,
                                     out Vector3 rightPos, out Quaternion rightRot,
                                     out float rightGroundY, out float rightLift, out float rightSpeed);

        // 딛는 발만 붙인다. 판별에 두 가지를 쓴다.
        //   높이 — 걷기·달리기에서 앞으로 나가는 발은 지면에서 뜬다
        //   수평속도 — 낮게 뜬 채 앞으로 미끄러지는 구간까지 걸러낸다.
        //             높이만 보면 그 발도 못 박혀 다리가 끌리고 '한 다리로 기어가는' 모습이 된다.
        float leftPlant  = hasLeft  ? PlantWeight(leftLift,  leftSpeed)  : 0f;
        float rightPlant = hasRight ? PlantWeight(rightLift, rightSpeed) : 0f;

        // 1) 골반 — 기준은 "지면 높이 대 캐릭터 루트"다. 애니메이션이 든 발 높이를 기준으로 삼으면
        //    달리기 체공 구간처럼 두 발이 다 떠 있을 때 큰 낙차가 나와 골반이 급강하하고 다리가 무너진다.
        //    평지에서는 두 발 밑 지면이 루트와 같은 높이라 보정이 0 이 된다.
        //
        //    반드시 '딛고 있는 발'만 본다. 계단을 오를 때 스윙 중인 뒷발은 아래 단 위에 있는데,
        //    그 낮은 지면을 골반이 따라가면 정작 딛고 있는 윗발 쪽 다리가 과하게 굽는다
        //    — 한 단마다 다리가 한 번씩 휘는 증상이 이것이다.
        bool leftPlanted  = leftPlant  > PlantThreshold;
        bool rightPlanted = rightPlant > PlantThreshold;

        float drop = 0f;
        if (grounded && (leftPlanted || rightPlanted))
        {
            float rootY = transform.position.y;
            float lowestGround = Mathf.Min(leftPlanted  ? leftGroundY  : float.MaxValue,
                                           rightPlanted ? rightGroundY : float.MaxValue);
            drop = Mathf.Clamp(lowestGround - rootY, -maxPelvisDrop, 0f);
        }
        _pelvisOffset = Mathf.Lerp(_pelvisOffset, drop, Time.deltaTime * pelvisLerpSpeed);
        if (Mathf.Abs(_pelvisOffset) > 0.001f)
            _animator.bodyPosition += Vector3.up * _pelvisOffset;

        // 2) 발 — 골반을 옮긴 뒤에 목표를 건다.
        ApplyFoot(AvatarIKGoal.LeftFoot,  hasLeft,  leftPos,  leftRot,  targetWeight * leftPlant,  ref _leftWeight);
        ApplyFoot(AvatarIKGoal.RightFoot, hasRight, rightPos, rightRot, targetWeight * rightPlant, ref _rightWeight);
    }

    /// <summary>
    /// 발 아래 지면을 찾아 IK 목표 위치·회전, 지면 월드 높이, 그리고
    /// '현재 발이 지면에서 뜬 높이'(lift)를 구한다. lift 는 딛는 발 판별에 쓴다.
    /// </summary>
    private bool TrySolveFoot(HumanBodyBones bone, AvatarIKGoal goal, int index,
                              out Vector3 pos, out Quaternion rot,
                              out float groundY, out float lift, out float horizSpeed)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        groundY = 0f;
        lift = float.MaxValue;
        horizSpeed = 0f;

        var footT = _animator.GetBoneTransform(bone);
        if (footT == null) return false;

        // 발의 수평 속도 — 스윙 발 판별용. 첫 프레임은 0 으로 둔다.
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 cur = footT.position;
        if (_targetPrimed[index])
        {
            Vector3 d = cur - _prevFootPos[index];
            horizSpeed = new Vector2(d.x, d.z).magnitude / dt;
        }
        _prevFootPos[index] = cur;

        Vector3 origin = footT.position + Vector3.up * RayStartHeight;
        if (!Physics.Raycast(origin, Vector3.down, out var hit, RayLength, groundLayer,
                             QueryTriggerInteraction.Ignore))
            return false;

        groundY = hit.point.y;

        // 루트에서 너무 먼 지면은 무시 — 절벽 끝에서 다리가 아래로 늘어나는 것을 막는다.
        if (Mathf.Abs(groundY - transform.position.y) > maxFootReach) return false;

        // 현재 발이 목표면보다 얼마나 위에 있는지 — 0 이면 딛고 있고, 크면 스윙 중이다.
        lift = Mathf.Max(0f, footT.position.y - (hit.point.y + footOffset));

        // 발바닥을 지면 법선 방향으로 살짝 띄운다(신발 두께 보정).
        Vector3 rawTarget = hit.point + hit.normal * footOffset;

        // 목표 위치를 보간해 따라간다.
        // 계단 모서리를 넘는 순간 레이가 다른 면을 맞아 목표가 한 단만큼 순간 이동하는데,
        // 이 보간이 없으면 가중치는 이미 1이라 발이 그대로 튕겨 나갔다 돌아온다.
        if (!_targetPrimed[index])
        {
            _smoothedTarget[index] = rawTarget;
            _targetPrimed[index] = true;
        }
        else
        {
            _smoothedTarget[index] = Vector3.Lerp(_smoothedTarget[index], rawTarget,
                                                  dt * positionLerpSpeed);
        }
        pos = _smoothedTarget[index];

        // 지면 기울기만큼 '기울이기'만 한다. 기준은 반드시 IK 목표 회전이어야 한다.
        //
        // footT.rotation(본의 회전)을 기준으로 쓰면 안 된다. SetIKRotation 이 기대하는 것은
        // 아바타의 IK 목표 프레임이고, 리그마다 본의 축 방향이 달라 둘은 일치하지 않는다.
        // 그러면 평지(법선=up, 회전분 항등)에서도 잘못된 프레임이 그대로 들어가 발목이 비틀린다.
        //
        // 캐릭터 전방으로 LookRotation 을 거는 방식도 안 된다. 사람 걸음은 발이 바깥으로
        // 벌어져 있어(toe-out) 발끝을 정면으로 강제하면 발목이 안쪽으로 돌아간다.
        //
        // GetIKRotation 을 기준으로 법선 차이만 곱하면, 평지에서는 아무 변화가 없고
        // 경사에서만 그 기울기만큼 더해진다.
        rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * _animator.GetIKRotation(goal);
        return true;
    }

    /// <summary>딛는 발 정도(0~1). 높이와 수평 속도를 함께 본다.</summary>
    private float PlantWeight(float lift, float horizSpeed)
    {
        float byHeight = 1f - Mathf.Clamp01(lift / footPlantRange);
        float bySpeed  = 1f - Mathf.Clamp01(horizSpeed / footSwingSpeed);
        return byHeight * bySpeed;
    }

    private void ApplyFoot(AvatarIKGoal goal, bool hasTarget, Vector3 pos, Quaternion rot,
                           float targetWeight, ref float weight)
    {
        float want = hasTarget ? targetWeight : 0f;
        weight = Mathf.Lerp(weight, want, Time.deltaTime * weightLerpSpeed);

        _animator.SetIKPositionWeight(goal, weight);
        _animator.SetIKRotationWeight(goal, weight);
        if (weight <= 0.001f) return;

        _animator.SetIKPosition(goal, pos);
        _animator.SetIKRotation(goal, rot);
    }
}
