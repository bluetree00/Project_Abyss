using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Cinemachine;  // Cinemachine 네임스페이스 추가

public class Vagabond : BaseController
{
    [SerializeField] private CinemachineFreeLook cinemachineCamera;  // 시네머신 카메라 참조

    // 연속 공격 관련 변수 추가
    private int comboStep = 0;               // 현재 콤보 단계
    private float comboTimer = 0;            // 콤보 타이머
    [SerializeField] private float comboDelay = 1.0f;  // 콤보 유지 시간
    private bool isAttacking = false;        // 현재 공격 중인지 확인
    private bool comboInputReceived = false; // 연속 공격 입력 여부

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

    protected override void Update() 
    {
        base.Update();
        UpdateMovement();

        HandleComboTimer();  // 콤보 타이머 관리
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
    }

    // 공격 처리
    private void ProcessAttack()
    {
        if (isAttacking)
        {
            comboInputReceived = true;  // 이미 공격 중이면 콤보 입력만 처리
            return;
        }

        StartComboAttack();
    }

    // 콤보 공격 시작
    private void StartComboAttack()
    {
        comboStep++;
        comboStep = Mathf.Clamp(comboStep, 1, 3);  // 콤보는 최대 3단계까지

        comboTimer = comboDelay;   // 콤보 타이머 초기화
        isAttacking = true;
        comboInputReceived = false;

        // 애니메이터 트리거로 콤보 단계에 맞는 공격 실행
        Debug.Log("공격 " + comboStep + " 시작");
        State = (Define.State)((int)Define.State.NormalAttack_01 + comboStep - 1);  // 각 콤보 단계에 맞는 상태 설정
    }

    // 공격 애니메이션 끝날 때 호출될 함수 (애니메이션 이벤트로 호출)
    public void OnAttackEnd()
    {
        isAttacking = false;

        if (comboInputReceived && comboStep < 3)
        {
            StartComboAttack();  // 연속 공격 이어가기
        }
        else
        {
            ResetCombo();  // 콤보 초기화
        }
    }

    // 콤보 타이머 관리
    private void HandleComboTimer()
    {
        if (comboStep > 0)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f)
            {
                ResetCombo();  // 콤보 시간이 지나면 초기화
            }
        }
    }

    // 콤보 초기화
    private void ResetCombo()
    {
        comboStep = 0;
        isAttacking = false;
        comboTimer = 0;
        comboInputReceived = false;
    }

    // 특정 상태일 때 입력을 처리하지 않도록 설정
    private bool CanProcessInput()
    {
        // 공격 중일 때는 이동 입력만 처리
        if (isAttacking)
            return false;

        return true;
    }

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
        if (comboStep > 0)
            return;  // 공격 중일 때는 이동 상태 업데이트를 중단

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
}
