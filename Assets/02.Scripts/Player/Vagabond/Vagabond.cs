using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Cinemachine;  // Cinemachine 네임스페이스 추가

public class Vagabond : BaseController
{
    [SerializeField] private CinemachineFreeLook cinemachineCamera;  // 시네머신 카메라 참조

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
    }

    //입력 관리
    private void OnInput()
    {
        CheckMovementInput();

        // 공격 발생 입력
        if(Input.GetMouseButtonDown(0))
        {
            Debug.Log("공격 시작");
            State = Define.State.NormalAttack_01;
        }
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
        if (moveDirection.magnitude > 0)
        {
            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (State == Define.State.Runing)
                    return;
                State = Define.State.Runing;
            }
            else
            {
                if (State == Define.State.Moving)
                    return;
                State = Define.State.Moving;
            }
        }
        else
        {
            if (State == Define.State.Idle)
                return;
            if (State == Define.State.NormalAttack_01)
                return;
            State = Define.State.Idle;
        }
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

    
    //기본 상태 전환
    private void OnIdle()
    {
        State = Define.State.Idle;
    }



    
}
