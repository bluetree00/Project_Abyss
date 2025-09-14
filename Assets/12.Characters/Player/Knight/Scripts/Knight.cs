using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.VisualScripting;
using Cysharp.Threading.Tasks;
using Unity.Collections;

// 입력 버퍼
using Game.Inputs;

/// <summary>
/// Vagabond 플레이어 캐릭터 클래스.
/// PlayerController를 상속하며, Vagabond 전용 상태 머신과 상태들을 관리한다.
/// 입력, 상태 전환, 공격 처리, 무기 변경, 카메라 설정 등 Vagabond 특화된 기능 구현.
/// </summary>
public class Knight : PlayerController, IPlayerStateChanger
{

    // FSM 상태 키(enum)와 상태 인스턴스를 매핑하는 딕셔너리
    private Dictionary<PlayerState, IPlayerState> fsmStates = new();

    // 현재 상태의 키
    private PlayerState currentStateKey;

    // 현재 활성화된 상태 인스턴스
    private IPlayerState currentState;

    // 외부에서 상태 접근 허용 (읽기 전용)
    public IPlayerState CurrentState => currentState;

    // 디버그용 현재 상태 노출
    [SerializeField, ReadOnly]
    private PlayerState debugCurrentState;

    private void RegisterState(PlayerState key, IPlayerState state)
    {
        // 상태에 이 컨트롤러와 상태 변경 요청 인터페이스를 전달하여 초기화
        state.Init(this, this);

        // 딕셔너리에 상태 등록
        fsmStates[key] = state;
    }

    // 상태 변경 요청 처리 메서드, 같은 상태 요청 시 무시
    public void RequestStateChange(PlayerState newState)
    {
        if (currentStateKey == newState)
            return; // 이미 같은 상태면 변경하지 않음

        currentState?.Exit();    // 현재 상태가 있으면 종료 처리
        currentStateKey = newState;   // 상태 키 변경
        debugCurrentState = newState; // <- 인스펙터용 상태 업데이트
        currentState = fsmStates[newState]; // 새로운 상태 할당
        currentState.Enter();    // 새로운 상태 진입 처리
    }




    private async void Start()
    {
        await InitAsync();
        CacheStates();

    }

    /// <summary>
    /// 비동기 초기화 확장 (부모 클래스 호출).
    /// 추가 초기화가 필요한 경우 여기에 작성.
    /// </summary>
    protected override async UniTask InitAsync()
    {
        await base.InitAsync();    // 부모 클래스 초기화 수행
        InitializeFSM();           // FSM 상태들을 등록하고 초기화
        RequestStateChange(PlayerState.Idle); // 초기 상태를 Idle로 설정

    }



    // FSM 상태별 인스턴스를 생성 및 등록하는 메서드
    private void InitializeFSM()
    {
        RegisterState(PlayerState.Idle, new PlayerIdleState());
        RegisterState(PlayerState.Move, new PlayerMoveState());
        RegisterState(PlayerState.Dodge, new PlayerDodgeState());
        RegisterState(PlayerState.Attack, new PlayerAttackState());
        RegisterState(PlayerState.AttackReady, new PlayerAttackReady());
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
    /// 
    /// 현재 방식을 템플릿 매서드 방식으로 개편 할 예정
    private void CacheStates()
    {

    }


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

    public bool CanProcessInput() => !isInputLocked && !(stateMachine.CurrentState?.BlocksInput ?? false);

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
        forward.y = right.y = 0;
        moveDirection = (forward.normalized * input.y + right.normalized * input.x).normalized;
    }

    /// <summary>
    /// 이동 방향에 따라 상태 전환 (이동 중이면 MoveBlend 상태로).
    /// </summary>
    private void UpdateMovement()
    {
        if (!CanProcessInput()) return;

        if (moveDirection.magnitude > 0.01f)
            RequestStateChange(PlayerState.Move);
    }

    /// <summary>
    /// 매 프레임 호출되는 Unity Update 함수.
    /// 기본 Update 호출 후 입력 처리, 상태 업데이트, 공중 상태 처리 수행.
    /// </summary>
    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;

        base.Update();

        CheckMovementInput();
        UpdateMovement();


        if (characterData.comboTimer > 0)
        {
            characterData.comboTimer -= Time.deltaTime;
            if (characterData.comboTimer <= 0)
                ResetCombo();
        }

    }

    // 매 프레임 호출되며 현재 상태의 Update 로직 실행
    public override void HandleFSM()
    {
        currentState?.Update();
    }



    //============================================================
    // 공격 처리
    //============================================================

    /// <summary>
    /// 공격 입력 시작 시 호출.
    /// 공격 준비 및 차지 공격 관련 변수 초기화.
    /// </summary>
    /// 
    private bool _heavyAutoTriggered;
    [SerializeField] private float tapMaxSec = 0.18f;    // 탭 임계 (선택)
    [SerializeField] private float holdMinSec = 0.25f;   // 홀드 임계 (헤비)
    protected override void OnAttackStarted()
    {
        if (!CanProcessInput()) return;

        characterData.attackInputTime = Time.unscaledTime;
        characterData.heavyAttackChargeTime = 0f;
        isInChargingState = true;
        _heavyAutoTriggered = false;

        // 차지 효과 시작(파티클/사운드 등)
        HeavyAttackAbility?.HeavyAttackStartCharging(this);
    }

    protected override void OnAttackReleased()
    {
        if (!CanProcessInput()) return;

        // 유효 시작이 없으면 무시
        if (characterData.attackInputTime <= 0f) return;

        float held = Time.unscaledTime - characterData.attackInputTime;

        // 소드: 자동발사 이미 Push했다면 중복 방지
        if (!_heavyAutoTriggered && held >= characterData.heavyAttackChargeThreshold)
        {
            InputBuffer.Push(Command.Heavy);
        }
        else
        {
            // 탭 또는 임계 미달 → 라이트
            InputBuffer.Push(Command.Light);
        }

        // 차지 종료(실행은 상태가 결정)
        HeavyAttackAbility?.HeavyAttackUpdateCharging(this, held);
        HeavyAttackAbility?.HeavyAttackCancelCharging(this);

        // 클린업(플래그만 정리, 공격 실행/애니 종료는 상태에게 맡김)
        characterData.attackInputTime = 0f;
        characterData.heavyAttackChargeTime = 0f;
        isInChargingState = false;
    }

    // 차지 중 자동 발사(소드) 예시: Update에서 임계 도달 시 버퍼 Push
    private void CheckSwordHeavyAttackChargingState()
    {
        if (isAttacking) return;
        if (!isInChargingState) return;

        characterData.heavyAttackChargeTime += Time.unscaledDeltaTime;
        HeavyAttackAbility?.HeavyAttackUpdateCharging(this, characterData.heavyAttackChargeTime);

        if (!_heavyAutoTriggered &&
            characterData.heavyAttackChargeTime >= characterData.heavyAttackChargeThreshold)
        {
            InputBuffer.Push(Command.Heavy);
            _heavyAutoTriggered = true;
            isInChargingState = false; // 더 이상 차지 업데이트 안 함
        }
    }



    /// <summary>
    /// 공격 애니메이션 시작 시 호출, 공격 중임을 표시.
    /// </summary>
    public void OnAttackAnimationStart() => isAttacking = true;

    /// <summary>
    /// 공격 애니메이션 종료 시 호출, 공격 중 상태 해제.
    /// </summary>
    public override void OnAttackAnimationEnd() => isAttacking = false;

    private void ResetCombo()
    {
        characterData.attackComboStep = 0;
        characterData.comboTimer = 0;
        nextComboQueued = false;

        // stateMachine.ChangeState(GetState<VagabondIdleState>());
        Debug.Log("Combo reset due to timer expiration.");
    }


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
        characterData.heavyAttackChargeTime = 0f;
        OnAttackAnimationEnd();
        RequestStateChange(PlayerState.Idle);
    }

    //============================================================
    // 공격 이펙트
    //============================================================

    /// <summary>
    /// 현재 장착한 무기 모듈의 공격 이펙트를 생성 (전방 기준). 서버 데이터 기준으로 생성
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

        RequestStateChange(PlayerState.Idle);
        weaponManagerSO.SwitchWeapon(index, anim);
    }
}