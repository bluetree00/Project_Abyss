using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경량 스프링본.
/// 외부(리타게팅) 애니메이션은 CombatGirl 전용 머리카락 본을 구동하지 못해
/// 머리카락이 바인드 포즈로 굳은 채 몸을 파고드는 문제가 있다.
/// 이 컴포넌트가 머리카락 본 체인을 관성+스프링으로 흔들어 몸 움직임을 따라가게 한다.
///
/// 사용: 머리카락 체인들의 부모(예: Hair_Root)에 부착.
/// boneRoots를 비워두면 이 Transform의 직속 자식들을 각 체인의 시작으로 자동 수집한다.
/// LateUpdate에서 Animator 갱신 이후 본 회전을 보정한다.
/// </summary>
public sealed class HairSpringBones : MonoBehaviour
{
    [Header("체인 시작 본 (비우면 자식 자동 수집)")]
    [SerializeField] private List<Transform> boneRoots = new List<Transform>();

    [Header("스프링")]
    [Tooltip("휴식 포즈로 복귀하는 강도. 클수록 뻣뻣(0~1).")]
    [Range(0f, 1f)][SerializeField] private float stiffness = 0.2f;
    [Tooltip("감쇠. 클수록 흔들림이 빨리 잦아듦(0~1).")]
    [Range(0f, 1f)][SerializeField] private float damping = 0.4f;
    [Tooltip("아래로 처지는 정도(m/s^2 유사).")]
    [SerializeField] private float gravity = 0.3f;
    [Tooltip("휴식 방향에서 허용하는 최대 굽힘 각도(도). 가닥 과도 스윙/교차(꼬임) 방지.")]
    [Range(0f, 180f)][SerializeField] private float maxBendAngle = 60f;
    [Tooltip("한 프레임 최대 시뮬레이션 dt(렉/일시정지 시 폭주 방지).")]
    [SerializeField] private float maxDeltaTime = 0.03f;

    private sealed class Node
    {
        public Transform bone;
        public Quaternion localRest; // 부모 기준 휴식 회전
        public Vector3 boneAxis;     // 자식 방향(본 로컬)
        public float length;         // bone→child 월드 거리
        public Vector3 tip;          // 시뮬레이션된 끝 월드 좌표
        public Vector3 prevTip;
    }

    private readonly List<Node> _nodes = new List<Node>();
    private bool _ready;

    private void Start()
    {
        if (boneRoots == null || boneRoots.Count == 0)
        {
            boneRoots = new List<Transform>();
            foreach (Transform c in transform) boneRoots.Add(c);
        }

        foreach (var root in boneRoots)
            if (root != null) BuildChain(root);

        _ready = _nodes.Count > 0;
    }

    private void BuildChain(Transform bone)
    {
        Transform child = bone.childCount > 0 ? bone.GetChild(0) : null;
        if (child != null)
        {
            var n = new Node
            {
                bone = bone,
                localRest = bone.localRotation,
                boneAxis = child.localPosition.sqrMagnitude > 1e-8f
                    ? child.localPosition.normalized
                    : Vector3.forward,
                length = Vector3.Distance(bone.position, child.position),
            };
            if (n.length > 1e-4f)
            {
                n.tip = child.position;
                n.prevTip = n.tip;
                _nodes.Add(n);
            }
            BuildChain(child);
        }
    }

    private void LateUpdate()
    {
        if (!_ready) return;

        float dt = Mathf.Min(Time.deltaTime, maxDeltaTime);
        if (dt <= 0f) return;

        // 부모→자식 순서(_nodes는 DFS로 부모 먼저). 부모 회전 보정 후 자식이 갱신된 부모를 참조.
        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            if (n.bone == null) continue;

            var parent = n.bone.parent;
            Quaternion natRot = parent != null ? parent.rotation * n.localRest : n.localRest;
            Vector3 naturalDir = (natRot * n.boneAxis).normalized;
            Vector3 naturalTip = n.bone.position + naturalDir * n.length;

            // Verlet 관성
            Vector3 velocity = (n.tip - n.prevTip) * (1f - damping);
            n.prevTip = n.tip;
            n.tip += velocity;

            // 휴식 포즈로 복귀(스프링) + 중력
            n.tip += (naturalTip - n.tip) * stiffness;
            n.tip += Vector3.down * (gravity * dt);

            // 방향 산출
            Vector3 dir = n.tip - n.bone.position;
            if (dir.sqrMagnitude < 1e-10f) dir = naturalDir;
            else dir.Normalize();

            // 과도 스윙 제한 — 휴식 방향에서 maxBendAngle 이내로 클램프 (가닥 교차/꼬임 방지)
            if (maxBendAngle < 179.9f && Vector3.Angle(naturalDir, dir) > maxBendAngle)
                dir = Vector3.RotateTowards(naturalDir, dir, maxBendAngle * Mathf.Deg2Rad, 0f).normalized;

            // 길이 고정 + tip 재설정
            n.tip = n.bone.position + dir * n.length;

            // 휴식 회전에 "굽힘"만 적용 → 롤(비틀림) 고정으로 꼬임 방지
            n.bone.rotation = Quaternion.FromToRotation(naturalDir, dir) * natRot;
        }
    }
}
