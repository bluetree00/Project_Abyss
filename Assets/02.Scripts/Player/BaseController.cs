using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseController : MonoBehaviour
{
    [SerializeField]
    protected Define.State _state = Define.State.Idle;

    [SerializeField]
    protected Vector3 _destPos;

    [SerializeField]
    protected GameObject _lockTarget;

    [SerializeField]
    protected Rigidbody rb;  // Rigidbody 참조
    

    public float moveSpeed = 5f; // 기본 이동 속도
    public float runSpeed = 8f;  // 기본 달리기 속도
    protected Vector3 moveDirection;  // 이동 방향

    private void Start()
    {
        Init();
    }

    protected virtual void Init()
    {
        rb = GetComponent<Rigidbody>();
    }

    protected virtual void Move(Vector3 direction, float speed)
    {
        //State = Define.State.Moving;

        if (direction.magnitude <= 0) return;
        
        // 방향 벡터 정규화
        Vector3 normalizedDirection = direction.normalized;

        // 이동 처리
        rb.velocity = new Vector3(normalizedDirection.x * speed, rb.velocity.y, normalizedDirection.z * speed);

        // 회전 처리 - 부드럽게 회전하도록 변경
        Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f); // 5f는 회전 속도 계수
    }


    // 상태 업데이트는 각 파생 클래스에서 구현하도록 가상 함수로 선언
    protected virtual void UpdateMovement() {}


    //상태별 애니메이션 설정
    protected virtual Define.State State
    {
        get { return _state; }
        set
        {
            _state = value;

            Animator anim = GetComponent<Animator>();
            switch(_state)
            {
                // 플레이어 기본 움직임 상태
                case Define.State.Idle:
                   anim.CrossFade("Idle", 0.2f);
                   break;
                case Define.State.Moving:
                   anim.CrossFade("Moving", 0.1f);
                   break;
                case Define.State.Runing:
                   anim.CrossFade("Runing", 0.3f);
                   break;
                case Define.State.Dodge:
                    anim.CrossFade("Dodge", 0.1f, -1);
                   break;
                case Define.State.Die:
                    anim.CrossFade("Die", 0.1f);
                   break;
                case Define.State.NormalAttack_01:
                    anim.CrossFade("NormalAttack_01", 0.1f);
                   break;
                case Define.State.NormalAttack_02:
                    anim.CrossFade("NormalAttack_02", 0.1f);
                   break;
                case Define.State.NormalAttack_03:
                    anim.CrossFade("NormalAttack_03", 0.1f);
                   break;

               
            }
        }
    }

     //상태별 업데이트 패턴
    protected virtual void Update()
    {
        switch (_state)
        {
            case Define.State.Idle:
                UpdateIdle();
                break;
            case Define.State.Moving:
                UpdateMoving();
                break;
             case Define.State.Runing:
                UpdateRuning();
                break;
            case Define.State.Dodge:
                UpdateDodge();
                break;
            case Define.State.NormalAttack_01:
                UpdateNormalAttack_01();
                break;
            case Define.State.NormalAttack_02:
                UpdateNormalAttack_02();
                break;
            case Define.State.NormalAttack_03:
                UpdateNormalAttack_03();
                break;
           
        }
    }

    protected virtual void UpdateIdle(){}  // Idle 상태에서의 로직
    protected virtual void UpdateMoving(){}  // Moving 상태에서의 로직
    protected virtual void UpdateRuning(){}  // Runing 상태에서의 로직
    protected virtual void UpdateDodge(){}  // Dodge 상태에서의 로직
    protected virtual void UpdateNormalAttack_01(){}  // Dodge 상태에서의 로직
    protected virtual void UpdateNormalAttack_02(){}  // Dodge 상태에서의 로직
    protected virtual void UpdateNormalAttack_03(){}  // Dodge 상태에서의 로직

}
