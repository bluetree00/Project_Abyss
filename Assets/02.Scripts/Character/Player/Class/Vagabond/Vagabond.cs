using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.VagabondStates;
using Unity.VisualScripting;

/// <summary>
/// Vagabond 플레이어 캐릭터 클래스.
/// PlayerCharacter를 상속하며, Vagabond 전용 상태 머신과 상태들을 관리한다.
/// 입력, 상태 전환, 공격 처리, 무기 변경, 카메라 설정 등 Vagabond 특화된 기능 구현.
/// </summary>
public class Vagabond : PlayerCharacter
{
    //============================================================
    // 상태 머신 및 상태 캐시
    //============================================================

    /// <summary>
    /// Vagabond 전용 상태 머신 인스턴스 (PlayerCharacter와 별도 유지).
    /// </summary>
    protected new StateMachine<Vagabond> stateMachine = new StateMachine<Vagabond>();

    /// <summary>
    /// 외부에서 상태 머신에 접근 가능하도록 프로퍼티 제공.
    /// </summary>
    public new StateMachine<Vagabond> StateMachine => stateMachine;

    /// <summary>
    /// 타입별로 상태 인스턴스를 미리 생성하여 캐싱해둠.
    /// 상태 생성 비용 절감 및 재사용 목적.
    /// </summary>
    private Dictionary<Type, State<Vagabond>> cachedStates = new();

    //============================================================
    // 초기화 및 상태 세팅
    //============================================================

    /// <summary>
    /// MonoBehaviour Start 메서드 비동기 오버라이드.
    /// 초기화 후 상태 캐시 생성 및 초기 상태(Idle) 세팅.
    /// </summary>
    private async void Start()
    {
        await InitAsync();
        CacheStates();
        stateMachine.Setup(this, GetState<VagabondIdleState>());
    }

    /// <summary>
    /// 비동기 초기화 확장 (부모 클래스 호출).
    /// 추가 초기화가 필요한 경우 여기에 작성.
    /// </summary>
    protected override async Task InitAsync()
    {
        await base.InitAsync();
    }

    /// <summary>
    /// 능력치 및 공격 등 능력 초기화 (부모 클래스 호출).
    /// </summary>
    protected override void InitAbilities()
    {
        base.InitAbilities();
    }

    /// <summary>
    /// Vagabond 전용 상태 인스턴스를 생성하여 캐시 딕셔너리에 저장.
    /// </summary>
    private void CacheStates()
    {
        cachedStates[typeof(VagabondIdleState)] = new VagabondIdleState();
        cachedStates[typeof(VagabondMoveBlendState)] = new VagabondMoveBlendState();
        cachedStates[typeof(VagabondInAirState)] = new VagabondInAirState();
        cachedStates[typeof(VagabondJumpStartState)] = new VagabondJumpStartState();
        cachedStates[typeof(VagabondSkillState)] = new VagabondSkillState();
        cachedStates[typeof(VagabondUltimateState)] = new VagabondUltimateState();
        cachedStates[typeof(VagabondDodgeState)] = new VagabondDodgeState();
        cachedStates[typeof(VagabondChangeWeaponState)] = new VagabondChangeWeaponState();
        cachedStates[typeof(VagabondComboAttackState)] = new VagabondComboAttackState();
        cachedStates[typeof(VagabondChargeStartState)] = new VagabondChargeStartState();
        cachedStates[typeof(VagabondChargeHoldingState)] = new VagabondChargeHoldingState();
        cachedStates[typeof(VagabondChargedAttackState)] = new VagabondChargedAttackState();
        cachedStates[typeof(VagabondChargeCancelState)] = new VagabondChargeCancelState();
    }

    /// <summary>
    /// 타입으로 캐시된 상태 인스턴스를 반환하는 제네릭 헬퍼 함수.
    /// </summary>
    public T GetState<T>() where T : State<Vagabond> => cachedStates[typeof(T)] as T;

    //============================================================
    // 카메라 뷰 설정
    //============================================================

    /// <summary>
    /// Vagabond 전용 카메라 세팅 (CinemachineFreeLook).
    /// 높이, 반경, 초기 축 위치 및 회전 속도 설정.
    /// </summary>
    protected override void ConfigureCameraView()
    {
        float height = 5f;
        float radius = 2f;

        for (int i = 0; i < 3; i++)
        {
            cinemachineCamera.m_Orbits[i].m_Height = height;
            cinemachineCamera.m_Orbits[i].m_Radius = radius;
        }

        cinemachineCamera.m_XAxis.Value = 0.5f;
        cinemachineCamera.m_YAxis.Value = 0.6f;
        cinemachineCamera.m_XAxis.m_MaxSpeed = 0f;
        cinemachineCamera.m_YAxis.m_MaxSpeed = 0f;
    }

    /// <summary>
    /// 입력 액션 바인딩 오버라이드 (기본 입력 바인딩 호출).
    /// 필요시 Vagabond 특화 입력 처리 추가 가능.
    /// </summary>
    protected override void BindInputActions()
    {
        base.BindInputActions();
    }

    //============================================================
    // 상태 전환 함수 모음 인터페이스 어빌리티에서 호출하여 캐릭터 상태 관리
    //============================================================

    /// <summary>
    /// 대기 상태로 전환 (Idle).
    /// </summary>
    public override void GoToIdleState() => stateMachine.ChangeState(GetState<VagabondIdleState>());

    /// <summary>
    /// 공격 콤보 상태로 전환, 콤보 단계 인덱스 세팅.
    /// </summary>
    public override void GoToComboAttackState()
    {
        var comboState = GetState<VagabondComboAttackState>();
        comboState.SetComboIndex(characterData.attackComboStep);
        stateMachine.ChangeState(comboState);
    }

    /// <summary>
    /// 회피 상태 전환.
    /// </summary>
    public override void GotoDodgeState() => stateMachine.ChangeState(GetState<VagabondDodgeState>());

    /// <summary>
    /// 강공격 차지 시작 상태 전환.
    /// </summary>
    public override void GoToHeavyAttackChargeStartState() => stateMachine.ChangeState(GetState<VagabondChargeStartState>());

    /// <summary>
    /// 강공격 차지 유지 상태 전환.
    /// </summary>
    public override void GoToHeavyAttackChargeHoldingState() => stateMachine.ChangeState(GetState<VagabondChargeHoldingState>());

    /// <summary>
    /// 강공격 차지 완료 후 공격 상태 전환.
    /// </summary>
    public override void GoToHeavyAttackChargedAttackState() => stateMachine.ChangeState(GetState<VagabondChargedAttackState>());

    /// <summary>
    /// 강공격 차지 취소 상태 전환.
    /// </summary>
    public override void GoToHeavyAttackChargeCancelState() => stateMachine.ChangeState(GetState<VagabondChargeCancelState>());

    /// <summary>
    /// 스킬 공격 상태 전환.
    /// </summary>
    protected override void OnSkillAttack() => stateMachine.ChangeState(GetState<VagabondSkillState>());

    /// <summary>
    /// 궁극기 공격 상태 전환.
    /// </summary>
    protected override void OnUltimateAttack() => stateMachine.ChangeState(GetState<VagabondUltimateState>());

    /// <summary>
    /// 점프 처리: 점프 불가 시 무시, 가능하면 점프 시작 상태로 전환.
    /// </summary>
    protected override void ProcessJump()
    {
        if (IsJumping() || !IsGrounded()) return;
        stateMachine.ChangeState(GetState<VagabondJumpStartState>());
    }

    //============================================================
    // 입력 처리
    //============================================================

    /// <summary>
    /// 플레이어 움직임 입력을 읽고 이동 방향 계산.
    /// 입력 불가 시 방향 벡터를 0으로 초기화.
    /// </summary>
    private void CheckMovementInput()
    {
        if (!CanProcessInput())
        {
            moveDirection = Vector3.zero;
            return;
        }

        Vector2 input = inputActions.Player.Move.ReadValue<Vector2>();
        Vector3 forward = cinemachineCamera.transform.forward;
        Vector3 right = cinemachineCamera.transform.right;

        // 수평 방향으로만 이동하도록 y축 성분 0으로 설정
        forward.y = right.y = 0;
        moveDirection = (forward.normalized * input.y + right.normalized * input.x).normalized;
    }

    /// <summary>
    /// 이동 방향에 따라 상태 전환 (이동 중이면 MoveBlend 상태로).
    /// </summary>
    private void UpdateMovement()
    {
        if (!CanProcessInput()) return;

        if (moveDirection.magnitude > 0.01f && !(stateMachine.CurrentState is VagabondMoveBlendState))
            stateMachine.ChangeState(GetState<VagabondMoveBlendState>());
    }

    /// <summary>
    /// 매 프레임 호출되는 Unity Update 함수.
    /// 기본 Update 호출 후 입력 처리, 상태 업데이트, 공중 상태 처리 수행.
    /// </summary>
    protected override void Update()
    {
        base.Update();

        CheckMovementInput();
        UpdateMovement();

        stateMachine.Update();

        HandleAirState();
    }

    /// <summary>
    /// 현재 공중 상태를 확인하고, 상태 머신에 반영.
    /// InAir 상태면 VagabondInAirState로 전환.
    /// 착지 상태면 이동 또는 대기 상태로 전환.
    /// </summary>
    private void HandleAirState()
    {
        if (CurrentAirState == AirState.InAir && !(stateMachine.CurrentState is VagabondInAirState))
        {
            stateMachine.ChangeState(GetState<VagabondInAirState>());
        }
        else if (CurrentAirState == AirState.None)
        {
            if (moveDirection.magnitude > 0.01f)
            {
                if (!(stateMachine.CurrentState is VagabondMoveBlendState))
                    stateMachine.ChangeState(GetState<VagabondMoveBlendState>());
            }
            else
            {
                if (!(stateMachine.CurrentState is VagabondIdleState))
                    stateMachine.ChangeState(GetState<VagabondIdleState>());
            }
        }
    }

    //============================================================
    // 공격 처리
    //============================================================

    /// <summary>
    /// 공격 입력 시작 시 호출.
    /// 공격 준비 및 차지 공격 관련 변수 초기화.
    /// </summary>
    protected override void OnAttackStarted()
    {
        if (!CanProcessInput())
        {
            attackInputTime = Time.time;
            return;
        }

        attackInputTime = Time.time;
        isInChargingState = false;
        heavyAttackChargeTime = 0f;
        heldDuration = 0f;
    }

    /// <summary>
    /// 공격 입력 해제 시 호출.
    /// 입력 누른 시간에 따라 차지 공격 또는 일반 공격 실행.
    /// 콤보 입력 여부 처리.
    /// </summary>
    protected override void OnAttackReleased()
    {
        if (!CanProcessInput()) return;

        if (attackInputTime == 0f)
        {
            Debug.LogWarning("Attack released without a valid start time. Fallback initialized.");
            return;
        }

        heldDuration = Time.time - attackInputTime;

        if (heldDuration >= heavyAttackChargeThreshold)
        {
            // 차지 공격 실행
            HeavyAttackAbility?.HeavyAttackReleaseChargedAttack(this, heldDuration);
        }
        else
        {
            // 차지 공격이 아니면 기본 공격
            if (!isAttacking)
                LightAttackAbility?.LightAttack(this);
            else
                nextComboQueued = true;
        }

        attackInputTime = 0f;
        heavyAttackChargeTime = 0f;
        heldDuration = 0f;
        isInChargingState = false;

        OnAttackAnimationEnd();
    }

    /// <summary>
    /// 공격 애니메이션 시작 시 호출, 공격 중임을 표시.
    /// </summary>
    public void OnAttackAnimationStart() => isAttacking = true;

    /// <summary>
    /// 공격 애니메이션 종료 시 호출, 공격 중 상태 해제.
    /// </summary>
    public override void OnAttackAnimationEnd() => isAttacking = false;

    /// <summary>
    /// 현재 공격을 취소해야 하는지 판단 (예: 회피 입력 발생).
    /// </summary>
    public bool ShouldCancelAttack() => inputActions.Player.Dodge.triggered;

    /// <summary>
    /// 차지 공격 취소 처리.
    /// 차지 시간 초기화 및 상태 머신을 Idle 상태로 전환.
    /// </summary>
    public void CancelHeavyAttack()
    {
        heavyAttackChargeTime = 0f;
        OnAttackAnimationEnd();
        StateMachine.ChangeState(GetState<VagabondIdleState>());
    }

    //============================================================
    // 공격 이펙트
    //============================================================

    /// <summary>
    /// 현재 장착한 무기 모듈의 공격 이펙트를 생성 (전방 기준).
    /// 애니메이션 이벤트로 호출되어 타이밍과 방향 조절.
    /// </summary>
    public void FrontAttack() => LightAttackAbility?.SpawnEffect(this, Vector3.forward);

    public void SpawnSlashEffect1() => LightAttackAbility?.SpawnEffect(this, Vector3.forward, new Vector3(0, 0, 68));

    public void SpawnSlashEffect2() => LightAttackAbility?.SpawnEffect(this, Vector3.forward, new Vector3(0, 0, 180));

    public void SpawnSlashEffect3() => LightAttackAbility?.SpawnEffect(this, Vector3.forward, new Vector3(0, 360, -60));

    public void SpawnSlashEffect4() => LightAttackAbility?.SpawnEffect(this, Vector3.forward, new Vector3(0, 360, -140));

    //============================================================
    // 무기 변경 처리
    //============================================================

    /// <summary>
    /// 무기 변경 처리.
    /// 입력 잠금 중일 경우 무시.
    /// 무기 매니저가 초기화 안 됐거나 동일 슬롯 인덱스면 무시.
    /// 상태 머신을 Idle 상태로 전환 후 무기 변경 실행.
    /// </summary>
    protected override void ChangeWeapon(int index)
    {
        if (isInputLocked) return;

        if (weaponManagerSO == null)
        {
            Debug.LogError("weaponManagerSO is not initialized.");
            return;
        }

        if (weaponManagerSO.GetCurrentSlotIndex() == index) return;

        stateMachine.ChangeState(GetState<VagabondIdleState>());
        weaponManagerSO.SwitchWeapon(index, anim);
    }
}
