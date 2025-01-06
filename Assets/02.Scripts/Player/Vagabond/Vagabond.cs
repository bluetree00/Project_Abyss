using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Cinemachine;
using System.ComponentModel;
using Unity.VisualScripting;  // Cinemachine 네임스페이스 추가

public class Vagabond : BaseController
{
    #region 기본 초기화, 생성자, 소멸자
    [SerializeField] private CinemachineFreeLook cinemachineCamera;  // 시네머신 카메라 참조
    private Coroutine dodgeCoroutine;      // 대시 코루틴을 추적하기 위한 변수
    private bool isInputLocked = false;
    private float inputLockDuration = 2f; // 입력을 무시할 시간 (초)
    protected override void Init()
    {
        
        base.Init(); // 부모 클래스의 초기화 코드 호출
        currentWeapon = Managers.Weapon.GetCurrentWeaponData(); // 현재 무기 데이터를 가져옴
        //Managers.UI.ShowSceneUI<UI_Inven>();
    }

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

    #endregion

    #region 업데이트, 상시 인풋
    protected override void Update() 
    {
        Managers.Input.KeyAction += OnInput; //캐릭터 오브젝트 생성 툴 사용시 삭제

        base.Update();
        CheckMovementInput();
        UpdateMovement();
        FreezeRotation();
       
        // 공격 콤보 시간이 다 지나면 초기화
        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0)
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
        
        // CheckMovementInput();

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


        //------------------------키 중복 체크해야함------------------------
        // 키보드 1 입력 (무기 교체)
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (isInputLocked)
            return;

            Managers.Weapon.ChangeWeapon(1);
            currentWeapon = Managers.Weapon.GetCurrentWeaponData();
            ChangeState(Define.State.currentWeaponIdle);
            StartCoroutine(LockInput());
        }

        // 키보드 2 입력 (무기 교체)
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (isInputLocked)
            return;

            Managers.Weapon.ChangeWeapon(2);
            currentWeapon = Managers.Weapon.GetCurrentWeaponData();
            ChangeState(Define.State.currentWeaponIdle);
            StartCoroutine(LockInput());
        }

        // 키보드 G 입력 (무기 설정)
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (isInputLocked)
            return;

            Managers.Weapon.SetWeapon("basic_Knight_02");
            StartCoroutine(LockInput());
        }   

        if (Input.GetKeyDown(KeyCode.F1))
        {
            Managers.Weapon.RemoveWeapon(1);
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            Managers.Weapon.RemoveWeapon(2);
        }
        //-----------------------------------------------------------------
    }

    private IEnumerator LockInput()
    {
        isInputLocked = true;
        yield return new WaitForSeconds(inputLockDuration);
        isInputLocked = false;
    }

    #endregion

    // 특정 상태일 때 입력을 처리하지 않도록 설정
    private readonly Define.State[] BusyStates = {
        Define.State.NormalAttack_01,
        Define.State.NormalAttack_02,
        Define.State.NormalAttack_03,
        Define.State.NormalSkill_01,
        Define.State.UltimateSkill_01,
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
            // ChangeState(Define.State.Idle);
            ChangeState(Define.State.currentWeaponIdle);
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
        Move(moveDirection, characterData.baseMoveSpeed);
    }

    //Runing 상태
    protected override void UpdateRuning()
    {
        Move(moveDirection, characterData.baseRunSpeed);
    }

    #endregion

    #region 마우스 좌클릭 공격 관련 코드
    private void ProcessAttack()
    {
        characterData.comboTimer = characterData.comboDuration; // 콤보 타이머 초기화
        characterData.attackComboStep++; // 콤보 스택 증가

        if (characterData.attackComboStep == 1)
        {
            Debug.Log("첫 번째 공격");
            ChangeState(Define.State.NormalAttack_01);
        //    Managers.UI.ShowAugmentChoiceUI(null);
          
        }
        else if (characterData.attackComboStep == 2)
        {
            Debug.Log("두 번째 공격");
            ChangeState(Define.State.NormalAttack_02);
        }
        else if (characterData.attackComboStep == 3)
        {
            Debug.Log("세 번째 공격");
            ChangeState(Define.State.NormalAttack_03);
            characterData.attackComboStep = 0; // 마지막 공격 후 초기화
            characterData.comboTimer = 0;
        }
    }

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        // ChangeState(Define.State.currentWeaponIdle);
        ChangeState(Define.State.Idle);
    }

    #endregion

    #region E 스킬 코드
    private void ProcessSkile()
    {
        Debug.Log("E");
        ChangeState(Define.State.NormalSkill_01);
    }
    #endregion 

    #region Q 스킬 코드
    private void ProcessUltimateSkile()
    {
        Debug.Log("Q");
        ChangeState(Define.State.UltimateSkill_01);
    }
    #endregion 

    #region 대시 코드

    private void ProcessDodge()
    {
        if (characterData.canDodge)
        {
            dodgeCoroutine = StartCoroutine(DashCoroutine());
        }
    }

    private IEnumerator DashCoroutine()
    {
        characterData.canDodge = false;  // 대시 가능 여부를 false로 설정
        ChangeState(Define.State.Dodge);  // 상태를 Dodge로 변경

        float startTime = Time.time;

        while (Time.time < startTime + characterData.dashDuration)
        {
            CheckMovementInput();  // 대시 중에도 입력 방향을 계속 갱신

            // 현재 입력 방향(moveDirection)으로 대시
            Vector3 dashDirection = moveDirection != Vector3.zero ? moveDirection : transform.forward;
            rb.velocity = dashDirection * characterData.dashSpeed;

            // 대시 방향으로 캐릭터 회전
            if (dashDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(dashDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f); // 부드럽게 회전
            }

            yield return null;
        }

        rb.velocity = Vector3.zero;  // 대시 후 속도 초기화
        yield return new WaitForSeconds(characterData.dodgeCooldown);  // 대시 쿨타임 대기

        characterData.canDodge = true;  // 다시 대시 가능하도록 설정
    }





    #endregion
/*
    #region 특성 (스탯) 처리
        private void OnTriggerEnter(Collider other) {

            //충돌한 오브젝트 태그 확인
            string otherTag = other.tag;

            //태그에 따른 특성 증가(점진적 감소) 처리
            characterData.ApplyBoostByTag(this, otherTag);

            //충돌한 오브젝트 제거
            if (otherTag == "APBoost" || otherTag == "MSBoost")
                Destroy(other.gameObject);
        }
    #endregion
*/
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
            // ChangeState(Define.State.currentWeaponIdle);
            ChangeState(Define.State.Idle);
            
        }
    }

    public void SpawnShinySlashEffect1()
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        // 플레이어의 정면을 기준으로 Z축 방향으로 1만큼 이동
        Vector3 spawnPosition = transform.position + transform.forward * 1f;

        // 플레이어의 회전값을 가져온 후 Z축에 68도 추가
        Quaternion playerRotation = transform.rotation; // 플레이어의 현재 회전
        Quaternion spawnRotation = playerRotation * Quaternion.Euler(0f, 0f, 68f); // 플레이어 회전에 Z축 68도 추가

        // ObjectPoolerManager를 통해 이펙트 생성
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);

        // 이펙트가 생성될 때의 위치와 회전값을 설정
        effectObject.transform.position = spawnPosition;
        effectObject.transform.rotation = spawnRotation; // Z축 회전만 68도 추가된 회전값
    }

    public void SpawnShinySlashEffect2()
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        // 플레이어의 정면을 기준으로 Z축 방향으로 1만큼 이동
        Vector3 spawnPosition = transform.position + transform.forward * 1f;

    
        Quaternion playerRotation = transform.rotation; // 플레이어의 현재 회전
        Quaternion spawnRotation = playerRotation * Quaternion.Euler(0f, 0f, 180f);

        // ObjectPoolerManager를 통해 이펙트 생성
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);

        // 이펙트가 생성될 때의 위치와 회전값을 설정
        effectObject.transform.position = spawnPosition;
        effectObject.transform.rotation = spawnRotation; 
    }

    public void SpawnShinySlashEffect3()
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        // 플레이어의 정면을 기준으로 Z축 방향으로 1만큼 이동
        Vector3 spawnPosition = transform.position + transform.forward * 1f;

     
        Quaternion playerRotation = transform.rotation; // 플레이어의 현재 회전
        Quaternion spawnRotation = playerRotation * Quaternion.Euler(0f, 360f, -60f);

        // ObjectPoolerManager를 통해 이펙트 생성
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);

        // 이펙트가 생성될 때의 위치와 회전값을 설정
        effectObject.transform.position = spawnPosition;
        effectObject.transform.rotation = spawnRotation; 
    }

    public void SpawnShinySlashEffect4()
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        // 플레이어의 정면을 기준으로 Z축 방향으로 1만큼 이동
        Vector3 spawnPosition = transform.position + transform.forward * 1f;

    
        Quaternion playerRotation = transform.rotation; // 플레이어의 현재 회전
        Quaternion spawnRotation = playerRotation * Quaternion.Euler(0f, 360f, -140f);

        // ObjectPoolerManager를 통해 이펙트 생성
        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);

        // 이펙트가 생성될 때의 위치와 회전값을 설정
        effectObject.transform.position = spawnPosition;
        effectObject.transform.rotation = spawnRotation; 
    }

    #endregion
}
