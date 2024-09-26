using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Vagabond : BaseController
{
    private void OnEnable() 
    {
        Managers.Input.KeyAction += OnKeyboard;
    }

    private void OnDisable() 
    {
        Managers.Input.KeyAction -= OnKeyboard;
    }

    protected override void Update() 
    {
        // 매 프레임마다 움직임을 업데이트
        base.Update();
        UpdateMovement();
    }

    // 키보드 입력 처리 메서드
    private void OnKeyboard()
    {
        CheckMovementInput();
    }

    private void CheckMovementInput()
    {
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        // 방향 벡터를 계산
        moveDirection = new Vector3(horizontalInput, 0f, verticalInput).normalized;

    }

    //움직임 상태 전환
    protected override void UpdateMovement()
    {
        // moveDirection의 크기가 0보다 크면 이동
        if (moveDirection.magnitude > 0)
        {
            if (Input.GetKey(KeyCode.LeftShift)) // 달리기 입력
            {
                State = Define.State.Runing;
               // Move(moveDirection, runSpeed);
            }
            else
            {
                State = Define.State.Moving;
                //Move(moveDirection, moveSpeed);
            }
        }
        else
        {
            State = Define.State.Idle;
        }
    }

    protected override void UpdateMoving()
    {
        Move(moveDirection, moveSpeed);
    }  
    protected override void UpdateRuning()
    {
        Move(moveDirection, runSpeed);
    }  
}
