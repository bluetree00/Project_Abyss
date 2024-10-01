using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Cinemachine;  // Cinemachine 네임스페이스 추가

public class Vagabond : BaseController
{
    #region 기본 초기화, 생성자, 소멸자
    [SerializeField] private CinemachineFreeLook cinemachineCamera;  // 시네머신 카메라 참조

    private int attackComboStep = 0;       // 공격 스택 단계
    private float comboTimer = 0.0f;       // 콤보 유지 시간
    public float comboDuration = 3.0f;     // 콤보가 유지되는 시간


    //플레이어의 강제 회전 방지
    void FreezeRotation()
    {
        rb.angularVelocity = Vector3.zero;
    }

    private void OnEnable() 
    {
        
        // 카메라를 동적으로 찾아서 설정
        if (cinemachineCamera == null)
        {
            cinemachineCamera = FindObjectOfType<CinemachineFreeLook>();  // 씬에서 CinemachineFreeLook 카메라를 검색
        }

        // 카메라 대상 초기화
        if (cinemachineCamera != null)
        {
            cinemachineCamera.Follow = this.transform;  // 캐릭터를 카메라의 Follow 대상으로 설정
            cinemachineCamera.LookAt = this.transform;  // 캐릭터를 카메라의 LookAt 대상으로 설정
        }

        Managers.Input.KeyAction += OnInput;
    }

    private void OnDisable() 
    {
       
        Managers.Input.KeyAction -= OnInput;
        
    }

    #endregion

    #region 업데이트, 상시 인풋
    protected override void Update() 
    {
        base.Update();
        UpdateMovement();
        FreezeRotation();
       
        // 공격 콤보 시간이 다 지나면 초기화
        if (comboTimer > 0)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0)
            {
                ResetCombo();
            }
        }

    }

    // 입력 처리
    private void OnInput()
    {
        if (!CanProcessInput())
            return;
        
        CheckMovementInput();

        // 마우스 좌클릭으로 공격 시작
        if (Input.GetMouseButtonDown(0))
        {
            ProcessAttack();
        }

        // 키보드 E 입력 (기본 스킬)
        if (Input.GetKeyDown(KeyCode.E))
        {
            ProcessSkile();
        }

        // 키보드 Q 입력 (기본 궁극기)
        if (Input.GetKeyDown(KeyCode.Q))
        {
            ProcessUltimateSkile();
        }

        // 키보드 SHift 입력 (기본 회피)
        if (Input.GetMouseButtonDown(1))
        {
            ProcessDodge();
        }

    }

    #endregion

    // 특정 상태일 때 입력을 처리하지 않도록 설정

    private readonly Define.State[] BusyStates  = {
    Define.State.NormalAttack_01,
    Define.State.NormalAttack_02,
    Define.State.NormalAttack_03,
    Define.State.NormalSkile_01,
    Define.State.UltimateSkile_01,
    Define.State.Dodge
};

    private bool CanProcessInput()
    {
        return !Array.Exists(BusyStates , state => State == state);
    }


    #region 기본 WASD 이동 관련 코드
    private void CheckMovementInput()
    {
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        // 카메라의 로컬 좌표계를 기준으로 방향 벡터 계산
        Vector3 forward = cinemachineCamera.transform.forward;  // 카메라의 앞쪽 방향
        Vector3 right = cinemachineCamera.transform.right;      // 카메라의 오른쪽 방향

        forward.y = 0;  // 평면 상의 방향으로 제한
        right.y = 0;

        forward.Normalize();
        right.Normalize();

        // 입력값을 카메라 좌표계 기준으로 변환
        moveDirection = (forward * verticalInput + right * horizontalInput).normalized;
    }

    protected override void UpdateMovement()
    {
        if (!CanProcessInput())
            return;
       
        if (moveDirection.magnitude > 0)
        {
            ChangeState(Input.GetKey(KeyCode.LeftShift) ? Define.State.Runing : Define.State.Moving);
        }
        else
        {
            ChangeState(Define.State.Idle);
        }
    }

    private void ChangeState(Define.State newState)
    {
        if (State != newState)
            State = newState;
    }

    //Moving 상태
    protected override void UpdateMoving()
    {
        Move(moveDirection, moveSpeed);
    }

    //Runing 상태
    protected override void UpdateRuning()
    {
        Move(moveDirection, runSpeed);
    }

    #endregion

    #region  마우스 우클릭 공격 관련 코드
    // 공격 처리
    private void ProcessAttack()
    {
        comboTimer = comboDuration; // 콤보 타이머 초기화
        attackComboStep++; // 콤보 스택 증가

        if (attackComboStep == 1)
        {
            Debug.Log("첫 번째 공격");
            ChangeState(Define.State.NormalAttack_01);
        }
        else if (attackComboStep == 2)
        {
            Debug.Log("두 번째 공격");
            ChangeState(Define.State.NormalAttack_02);
        }
        else if (attackComboStep == 3)
        {
            Debug.Log("세 번째 공격");
            ChangeState(Define.State.NormalAttack_03);
            attackComboStep = 0; // 마지막 공격 후 초기화
            comboTimer = 0;
        }
    }


    private void ResetCombo()
    {
        attackComboStep = 0;
        comboTimer = 0;
        ChangeState(Define.State.Idle);
    }
   

    #endregion

    #region  E 스킬 코드
    private void ProcessSkile()
    {
        Debug.Log("E");
        ChangeState(Define.State.NormalSkile_01);
    }
    #endregion 

    #region  Q 스킬 코드
    private void ProcessUltimateSkile()
    {
        Debug.Log("Q");
        ChangeState(Define.State.UltimateSkile_01);
    }
    #endregion 

    #region  Shift 회피 코드

    private void ProcessDodge()
    {
        ChangeState(Define.State.Dodge);
    }

    #endregion 

    #region 사용할 애니메이션 이벤트
    private void OnEndEvent()
    {
        ChangeState(Define.State.Idle);
    }

    #endregion 
}
