using UnityEngine;

/// <summary>
/// 인트로 무형검 받기 — 날아오는 검 쪽으로 오른손을 뻗었다가, 잡으면 거둔다(휴머노이드 IK).
/// 플레이어 애니메이터 Base Layer는 IK Pass가 켜져 있다(발 IK · PlayerFootIK) — 같은 오브젝트에 잠깐 붙였다가 다 거두면 스스로 떨어진다.
/// 10-16 사용자 「무형검을 얻는 애니메이션이 없어 어색해 — 무형검이 플레이어한테 가는 느낌을」. 받는 몸짓 클립이 없어(Equip01은 자세 전환뿐) 손을 IK로 뻗는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class IntroHandReach : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float BlendSec  = 0.25f;   // 손을 뻗는 / 거두는 시간
    private const float MaxWeight = 0.85f;   // 끝까지 펴지 않는다 — 팔꿈치가 곧게 잠기면 인형처럼 보인다
    private const float ArmReach  = 0.55f;   // 어깨에서 손까지 최대 거리(m)

    // ── Private ──────────────────────────────────────────
    private Animator  _anim;
    private Transform _target;
    private Transform _shoulder;
    private float     _weight;
    private float     _goal;

    // ── Lifecycle ────────────────────────────────────────
    private void Awake()
    {
        _anim     = GetComponent<Animator>();
        _shoulder = _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
    }

    private void Update()
    {
        _weight = Mathf.MoveTowards(_weight, _goal, Time.deltaTime / BlendSec);
        if (_goal <= 0f && _weight <= 0f) Destroy(this);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || _target == null || _shoulder == null) return;
        float w = Mathf.SmoothStep(0f, MaxWeight, _weight);
        Vector3 from = _shoulder.position;
        Vector3 dir  = _target.position - from;
        float   len  = dir.magnitude;
        Vector3 at   = len > 0.001f ? from + dir / len * Mathf.Min(len, ArmReach) : from;
        _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        _anim.SetIKPosition(AvatarIKGoal.RightHand, at);
    }

    // ── Public Methods ───────────────────────────────────
    /// <summary>이 대상 쪽으로 손을 뻗기 시작한다.</summary>
    public void Reach(Transform target)
    {
        _target = target;
        _goal   = 1f;
    }

    /// <summary>손을 거둔다 — 다 거두면 컴포넌트가 스스로 떨어진다.</summary>
    public void Release() => _goal = 0f;
}
