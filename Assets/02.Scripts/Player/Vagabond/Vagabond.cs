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

    private bool canDodge = true;          // 대시 가능 여부
    public float dashSpeed = 10f;          // 대시 속도
    public float dashDuration = 0.2f;      // 대시 지속 시간
    public float dodgeCooldown = 2f;       // 대시 쿨타임
    private Coroutine dodgeCoroutine;      // 대시 코루틴을 추적하기 위한 변수


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

        Managers.Input.KeyAction -= OnInput;
        Managers.Input.KeyAction += OnInput;
    }

    private void OnDisable() 
    {
       
    }

    #endregion

    #region 업데이트, 상시 인풋
    protected override void Update() 
    {
        Managers.Input.KeyAction += OnInput; //캐릭터 오브젝트 생성 툴 사용시 삭제

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

        // 키보드 Shift 입력 (기본 회피)
        if (Input.GetMouseButtonDown(1))
        {
            ProcessDodge();
        }

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

    }

    #endregion

    // 특정 상태일 때 입력을 처리하지 않도록 설정
    private readonly Define.State[] BusyStates = {
        Define.State.NormalAttack_01,
        Define.State.NormalAttack_02,
        Define.State.NormalAttack_03,
        Define.State.NormalSkile_01,
        Define.State.UltimateSkile_01,
        Define.State.Dodge
    };

    private bool CanProcessInput()
    {
        return !Array.Exists(BusyStates, state => State == state);
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

    #region 마우스 좌클릭 공격 관련 코드
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

    #region E 스킬 코드
    private void ProcessSkile()
    {
        Debug.Log("E");
        ChangeState(Define.State.NormalSkile_01);
    }
    #endregion 

    #region Q 스킬 코드
    private void ProcessUltimateSkile()
    {
        Debug.Log("Q");
        ChangeState(Define.State.UltimateSkile_01);
    }
    #endregion 

    #region 대시 코드

    private void ProcessDodge()
    {
        if (canDodge)
        {
            dodgeCoroutine = StartCoroutine(DashCoroutine());
        }
    }

    private IEnumerator DashCoroutine()
    {
        canDodge = false;  // 대시 가능 여부를 false로 설정
        ChangeState(Define.State.Dodge);  // 상태를 Dodge로 변경

        Vector3 dashDirection = moveDirection != Vector3.zero ? moveDirection : transform.forward;  // 대시 방향 설정
        float startTime = Time.time;

        // 대시 지속 시간 동안 캐릭터 이동
        while (Time.time < startTime + dashDuration)
        {
            rb.velocity = dashDirection * dashSpeed;
            yield return null;
        }

        rb.velocity = Vector3.zero;  // 대시 후 속도 초기화
        yield return new WaitForSeconds(dodgeCooldown);  // 대시 쿨타임 대기

        canDodge = true;  // 다시 대시 가능하도록 설정
    }

    #endregion

    #region 애니메이션 이벤트 처리

    private void OnEndEvent()
    {
        // 대시 후 이동 입력 확인
        if (moveDirection.magnitude > 0)
        {
            // 이동 방향이 있으면 달리기 상태로 전환 (Shift 누를 시 달리기)
            ChangeState(Input.GetKey(KeyCode.LeftShift) ? Define.State.Runing : Define.State.Moving);
        }
        else
        {
            // 이동 입력이 없으면 Idle 상태로 전환
            ChangeState(Define.State.Idle);
        }
    }

    public void SpawnShinySlashEffect()
    {
        // 플레이어 위치 기준으로 Z축 1만큼 앞에 생성
        Vector3 spawnPosition = playerTransform.position + playerTransform.forward * 1f;

        // 플레이어의 정면 방향으로 회전값 설정
        Quaternion spawnRotation = Quaternion.LookRotation(transform.forward);

        // ObjectPoolerManager를 통해 이펙트 생성
        Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);
    }


    #endregion
}
