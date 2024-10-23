using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterBaseController : MonoBehaviour
{
    #region  기본 초기화
    [SerializeField]
    protected Define.MonsterState _state = Define.MonsterState.Idle;

    [SerializeField]
    protected Vector3 _destPos;

    [SerializeField]
    protected GameObject _lockTarget;

    [SerializeField]
    protected Rigidbody rb;  // Rigidbody 참조

     [SerializeField]
    protected int Maxhp;  // 기본 hp

    [SerializeField]
    protected int hp;  // 기본 hp

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

    #endregion


    // 상태 업데이트는 각 파생 클래스에서 구현하도록 가상 함수로 선언
    protected virtual void UpdateMovement() {}


    //상태별 애니메이션 설정
    protected virtual Define.MonsterState State
    {
        get { return _state; }
        set
        {
            _state = value;

            Animator anim = GetComponent<Animator>();
            switch(_state)
            {
                // 플레이어 기본 움직임 상태
                case Define.MonsterState.Idle:
                   anim.CrossFade("Idle", 0.2f);
                   break;
                case Define.MonsterState.Moving:
                   anim.CrossFade("Moving", 0.1f);
                   break;
                case Define.MonsterState.Runing:
                   anim.CrossFade("Runing", 0.3f);
                   break;
                case Define.MonsterState.Dodge:
                    anim.CrossFade("Dodge", 0.1f, -1);
                   break;
                case Define.MonsterState.Die:
                    anim.CrossFade("Die", 0.1f);
                   break;
                case Define.MonsterState.NormalAttack_01:
                    anim.CrossFade("NormalAttack_01", 0.1f);
                   break;
                case Define.MonsterState.NormalAttack_02:
                    anim.CrossFade("NormalAttack_02", 0.1f);
                   break;
                case Define.MonsterState.NormalAttack_03:
                    anim.CrossFade("NormalAttack_03", 0.1f);
                   break;
                case Define.MonsterState.NormalSkile_01:
                    anim.CrossFade("NormalSkile_01", 0.1f);
                   break;
                case Define.MonsterState.UltimateSkile_01:
                    anim.CrossFade("UltimateSkile_01", 0.1f);
                   break;

               
            }
        }
    }

     //상태별 업데이트 패턴
    protected virtual void Update()
    {
        switch (_state)
        {
            case Define.MonsterState.Idle:
                UpdateIdle();
                break;
            case Define.MonsterState.Moving:
                UpdateMoving();
                break;
             case Define.MonsterState.Runing:
                UpdateRuning();
                break;
            case Define.MonsterState.Dodge:
                UpdateDodge();
                break;
            case Define.MonsterState.NormalAttack_01:
                UpdateNormalAttack_01();
                break;
            case Define.MonsterState.NormalAttack_02:
                UpdateNormalAttack_02();
                break;
            case Define.MonsterState.NormalAttack_03:
                UpdateNormalAttack_03();
                break;
            case Define.MonsterState.NormalSkile_01:
                UpdateNormalSkile_01();
                break;
            case Define.MonsterState.UltimateSkile_01:
                UpdateUltimateSkile_01();
                break;
           
        }
    }

    protected virtual void UpdateIdle(){}  // Idle 상태에서의 로직
    protected virtual void UpdateMoving(){}  // Moving 상태에서의 로직
    protected virtual void UpdateRuning(){}  // Runing 상태에서의 로직
    protected virtual void UpdateDodge(){}  // Dodge 상태에서의 로직
    protected virtual void UpdateNormalAttack_01(){}  // NormalAttack_01 상태에서의 로직
    protected virtual void UpdateNormalAttack_02(){}  // NormalAttack_02 상태에서의 로직
    protected virtual void UpdateNormalAttack_03(){}  // NormalAttack_03 상태에서의 로직
    protected virtual void UpdateNormalSkile_01(){}  // NormalSkile_01 상태에서의 로직
    protected virtual void UpdateUltimateSkile_01(){}  // UltimateSkile_01 상태에서의 로직

}
