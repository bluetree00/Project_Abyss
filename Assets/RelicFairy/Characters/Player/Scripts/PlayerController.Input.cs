using UnityEngine;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;
using Game.Inputs;

// PlayerController — 입력 차단 채널 · 바인딩 · 명령 라우팅 · 이동 기준
public sealed partial class PlayerController
{
    // ── Public Methods: 입력 차단 채널 ────────────────────────────
    /// <summary>
    /// 플레이어 입력 전체를 활성/비활성화한다(컷신·연출 채널).
    /// 보스 등장 연출 등 컷씬 구간에서 false로 호출해 행동을 막는다.
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        // 컷신이 입력 액션 생성(비동기 초기화) 전에 차단을 걸 수 있다.
        // 의도를 플래그로 남겨두지 않으면 InitInputActions()의 Enable()이 차단을 덮어써 조작이 되살아난다.
        _inputDisabledExternally = !enabled;
        ApplyInputState();
    }

    /// <summary>
    /// UI 차단(BlocksGameplay 팝업) 전용 채널. <see cref="SetInputEnabled"/>와 <b>독립</b>이다.
    ///
    /// 하나의 bool을 공유하면, 컷신이 입력을 끈 뒤 그 안에서 띄운 차단형 대사 팝업이 닫히는 순간
    /// UIManager가 무조건 입력을 되살려 컷신 내내 이동·회전·공격이 가능해진다(인트로 연출 조작 버그).
    /// 두 채널을 분리해 각자 자기 사유만 해제하게 한다.
    /// </summary>
    public void SetUiBlocked(bool blocked)
    {
        _inputBlockedByUI = blocked;
        ApplyInputState();
    }

    /// <summary>
    /// 컨트롤러 전체를 잠시 멈춘다(Update 정지 — 입력·상태머신·상태이상 틱 모두). 등장 연출용.
    ///
    /// 예전엔 <c>inputReady</c> 하나가 '입력 초기화 완료'와 '연출 정지'를 겸했다. 그래서 초기화가 연출보다
    /// 늦게 끝나는 경로(캐릭터 데이터를 Addressables로 불러오는 경우)에서는 초기화가 연출의 정지를
    /// 풀어버릴 수 있었다. 두 의미를 별도 채널로 나눈다.
    /// </summary>
    public void SetControlSuspended(bool suspended) => _controlSuspended = suspended;

    // ── Private Methods: 초기화 · 바인딩 ──────────────────────────
    /// <summary>두 차단 사유(컷신/UI)를 합쳐 실제 InputAction 활성 상태에 반영한다.</summary>
    private void ApplyInputState()
    {
        bool enabled = !_dead && !_inputDisabledExternally && !_inputBlockedByUI;   // 사망 뒤엔 누가 다시 켜도 막는다(10-09 출시 점검)

        // 입력을 끊으면 이동 방향 갱신도 멈춘다 → 마지막 입력값이 그대로 남아
        // 컷신 내내 달리는 자세로 이동한다. 차단 시 즉시 0으로 비운다.
        if (!enabled) _moveDirection = Vector3.zero;

        if (_inputActions == null) return;
        if (enabled) _inputActions.Player.Enable();
        else         _inputActions.Player.Disable();
    }

    private void InitInputActions()
    {
        if (IsShadowClone) return;
        if (_inputActions != null)
        {
            _inputActions.Player.Disable();
            _inputActions.Disable();
            _inputActions.Dispose();
        }
        _inputActions = new PlayerInputActions();
        _inputActions.Enable();
        // 초기화 이전에 컷신/UI가 걸어둔 차단을 존중한다(이게 없으면 컷신 중 조작이 되살아난다).
        if (_inputDisabledExternally || _inputBlockedByUI) _inputActions.Player.Disable();
        _inputInitialized = true;
    }

    /// <summary>스킬 키를 누르고 있는가 — 누르고 있기 차지(궁수의 집중, 10-08 C4 · 결정 S4).</summary>
    public bool IsSkillButtonHeld(SkillType slot)
    {
        if (_inputActions == null) return false;
        return slot switch
        {
            SkillType.Q => _inputActions.Player.QSkill.IsPressed(),
            SkillType.E => _inputActions.Player.ESkill.IsPressed(),
            SkillType.R => _inputActions.Player.RSkill.IsPressed(),
            _           => false,
        };
    }

    private void BindInputActions()
    {
        if (!_inputInitialized) return;

        _inputActions.Player.Attack.started += ctx =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // 스킬 중에는 공격 입력 무시
            bool inSkill = _actSM.CurrentId == ActState.QSkill
                        || _actSM.CurrentId == ActState.ESkill
                        || _actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;

            if (CanAttack())
                _attackPolicy?.OnStarted(this);
            else
                Debug.Log("[Input] Attack started ignored - no weapon");

            // 탑뷰 포함 모든 카메라 상태에서 마우스 월드 위치 계산
            _aim.RecordClick(transform);
        };

        _inputActions.Player.Attack.canceled += _ =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            bool inSkill = _actSM.CurrentId == ActState.QSkill
                        || _actSM.CurrentId == ActState.ESkill
                        || _actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;
            _attackPolicy?.OnCanceled(this);
        };

        _inputActions.Player.Dodge.performed += _ => InputBuffer.Push(Command.Dodge);
        _inputActions.Player.QSkill.performed += _ => InputBuffer.Push(Command.QSkill);
        _inputActions.Player.ESkill.performed += _ => InputBuffer.Push(Command.ESkill);
        _inputActions.Player.RSkill.performed += _ => InputBuffer.Push(Command.RSkill);

        // [점프 폐기] 자유 점프 제거 — 스페이스 입력을 점프에 연결하지 않는다. 공중 상태(낙하·넉백)는 유지.
        // [달리기 버튼 미사용] Run 액션은 읽는 곳이 없어 바인딩하지 않는다 — 걷기→달리기는 이동 지속 시간으로 램프된다.
        _inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        _inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
        _inputActions.Player.PuzzleToggle.performed += _ => TogglePuzzleGrid();

        // 포션(C) — New Input System 액션. 레거시 Input.GetKeyDown은 이 프로젝트(Both 모드에서
        // New Input System 활성)에서 안 잡혀 포션이 아예 눌리지 않았다. 시간정지 중엔 무시.
        _inputActions.Player.Potion.performed += _ =>
        {
            if (Time.timeScale > 0f)
                GameRunBootstrapper.Instance?.Run?.TryUsePotion();
        };
    }

    /// <summary>
    /// 룬판 토글 — 이 경로가 <b>유일한 토글 경로</b>다(PuzzleToggle = Tab).
    /// 과거 UIRootBootstrapper.Update()도 같은 Tab을 폴링해 같은 프레임에 이중 토글 → 상쇄되어
    /// 런 중엔 룬판이 열리지 않았다. 그쪽 폴링은 제거했다.
    /// IsRunning 가드는 걸지 않는다 — 베이스캠프(허브)는 Phase가 Running이 아니라 룬판이 막혀버린다.
    /// </summary>
    private void TogglePuzzleGrid()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;

        var panel = UI_GridPanel.Instance;
        if (panel != null && panel.IsOpen)
            panel.Close();
        else
            panel?.Open();
    }

    private void ChangeWeapon(int index)
    {
        // 스킬 중 교체 금지 — 쿨다운 · 피해 · 동작 · 파츠가 바뀐 무기 것으로 섞였다(10-09 출시 점검)
        if (_dead || IsGrabbed || IsInSkillState()) return;
        // 시간 정지(팝업 · 룬판) 중엔 무시 — 포션과 같다(10-14). 멈춘 동안 HUD만 바뀌고 손의 무기 디졸브는 멈춰 서로 달라 보였다.
        if (Time.timeScale <= 0f) return;
        if (WeaponManager != null)
            WeaponManager.SwitchToSlotAsync(index).Forget();
    }

    // ── Private Methods: 명령 라우팅 · 이동 기준 ──────────────────
    /// <summary>
    /// 버퍼된 입력 명령을 행동/이동 상태 전환으로 바꾼다. 우선순위: Q → E → R → 회피 → 약공격.
    /// </summary>
    private void RouteInputs()
    {
        // 사망 · 컷신/UI 입력 차단 · 잡힘 동안엔 명령을 실행하지 않고 버린다 — 막히기 직전 0.4초에 누른 입력이 잠금 시작에 터졌고,
        // 사망 뒤 다른 코드가 입력을 다시 켜면 죽은 몸으로 움직였다(10-09 출시 점검)
        if (_dead || _inputDisabledExternally || _inputBlockedByUI || IsGrabbed)
        {
            InputBuffer.Clear();
            return;
        }
        bool isInSkill = IsInSkillState();
        bool isDodging = _locoSM.CurrentId == LocoState.Dodge;
        // 마지막 타 뒤 후딜 — 공격 · 스킬로 끊고 바로 잇는다(10-18 사용자 「후딜이 의미 없이 길다」 · 예전엔 회피만)
        bool chainFromSkill = isInSkill && SkillRecovering;
        bool isInAct   = _actSM.CurrentId != ActState.None || isDodging;

        // 빈 슬롯(예: 무형검은 skillE/skillQ 모두 없음)으로 전환하면 스킬 없는 상태에 들어가
        // 진행 중이던 공격 모션만 끊기고 아무것도 안 나간다 → HasSkillInSlot으로 입력 자체를 막는다.
        // 스킬 중 누른 다음 스킬은 버리지 않고 버퍼(0.4초)에 남긴다 — 지금 스킬이 끝나면 바로 이어진다(10-08 A2).
        // 회피 중 누른 스킬은 버퍼(0.4초)에 남겼다가 회피가 끝나면 나간다 — 회피 중에 시작하면 회피 종료가 이동 잠금을 풀고
        // 대기 동작으로 덮어 스킬 동작이 끊긴 채 걸어 다녔다(10-09 출시 점검)
        if ((!isInSkill || chainFromSkill) && !isDodging && InputBuffer.TryConsume(Command.QSkill))
        {
            RFLog.D($"[Input] Q pressed: CanAttack={CanAttack()}, isInSkill={isInSkill}, actState={_actSM.CurrentId}");
            if (CanAttack() && (!isInSkill || chainFromSkill) && CanUseSkillNow(SkillType.Q)) _actSM.Change(ActState.QSkill);
            return;
        }
        if ((!isInSkill || chainFromSkill) && !isDodging && InputBuffer.TryConsume(Command.ESkill))
        {
            if (CanAttack() && (!isInSkill || chainFromSkill) && CanUseSkillNow(SkillType.E)) _actSM.Change(ActState.ESkill);
            else if (!CooldownTracker.IsReady(SkillType.E)) SkillRejected?.Invoke(SkillType.E);   // HUD 거절 표시(A9)
            return;
        }
        if ((!isInSkill || chainFromSkill) && !isDodging && InputBuffer.TryConsume(Command.RSkill))
        {
            if (CanAttack() && (!isInSkill || chainFromSkill) && CanUseSkillNow(SkillType.R)) _actSM.Change(ActState.RSkill);
            else if (!CooldownTracker.IsReady(SkillType.R)) SkillRejected?.Invoke(SkillType.R);
            return;
        }

        // 스킬 중에는 공격 관련 입력 소비하고 무시 — 후딜이면 끊고 바로 평타
        if (isInSkill)
        {
            if (chainFromSkill && InputBuffer.TryConsume(Command.Light))
            {
                _actSM.Change(ActState.None);
                if (CanAttack()) _actSM.Change(ActState.AttackReady);
                return;
            }
            InputBuffer.TryConsume(Command.Light);
        }

        // 스킬 중 회피: 후딜(피해 뒤)이면 끊고 회피, 그 전이면 버퍼(0.4초)에 남긴다 — 예전엔 버렸다(10-08 A2 · 결정 S2)
        if (isInSkill && !SkillRecovering) return;
        if (InputBuffer.TryConsume(Command.Dodge))
        {
            if (IsLaunched) return; // 날아가는 중엔 회피로 탈출 불가

            // 대시 게이트 3중:
            //  ① !isDodging      — 대시 도중엔 재대시 불가
            //  ② DodgeCooldownEnd — 대시가 끝난 뒤 짧은 텀(dodgeCooldown) 동안 불가 (즉시 연타 방지)
            //  ③ 스태미너         — 자원이 있어야 발동 (소모는 여기서 확정)
            if (!isDodging
                && Time.time >= DodgeCooldownEnd
                && TryConsumeDodgeStamina())
            {
                if (isInAct) _actSM.Change(ActState.None);
                _locoSM.Change(LocoState.Dodge);
            }
            return;
        }

        if (isInAct) return;

        // [강공격 봉인] 강공격/차지 커맨드는 생산자(무기 입력 정책)에서 제거됐다 —
        // 여기서 걸러낼 것도 남아 있지 않으므로 약공격 한 갈래만 남는다.
        if (InputBuffer.TryConsume(Command.Light))
        {
            if (CanAttack()) _actSM.Change(ActState.AttackReady);
            return;
        }
    }

    /// <summary>
    /// 대시 스태미너를 소모 시도한다. 부족하면 false → 대시 불발(쿨타임 대신 자원이 게이트).
    /// </summary>
    private bool TryConsumeDodgeStamina()
    {
        if (Stamina == null || characterData == null || RuntimeStats == null) return true;

        // 악몽 규칙(뿌리의 속박)이 소모를 늘린다 — 공용 SO 값은 건드리지 않고 읽는 자리에서 곱한다.
        return Stamina.TryConsume(characterData.dodgeStaminaCost * NightmareRules.DodgeStaminaMultiplier,
                                  RuntimeStats.MaxStamina, characterData.staminaRegenDelay);
    }

    /// <summary>
    /// 카메라 기준 이동 방향 계산 → 이동 방향 갱신.
    /// 공중 상태에서는 지상 방향 갱신을 생략한다.
    /// </summary>
    private void CheckMovementInput()
    {
        if (_locoSM?.CurrentId == LocoState.Air) return;
        if (_inputActions == null) return;

        var input = _inputActions.Player.Move.ReadValue<Vector2>();

        // 이동 기준 = 카메라 수평 heading(FreeLook m_XAxis, BindingMode=WorldSpace라 월드 yaw와 동일).
        // 라이브 카메라 transform이 아니라 heading 값만 쓰므로, 시작 연출(오버헤드/투어)로 카메라가
        // 눕거나 거의 수직이 돼도(피치 변화) 조작이 어긋나지 않는다 — 기존 월드축 고정의 의도를 유지.
        // heading이 0이면 월드축과 완전히 동일하므로 기존 구간(던전 등)의 조작감은 변하지 않는다.
        float camYaw = cinemachineCamera != null ? cinemachineCamera.m_XAxis.Value : 0f;

        // [스냅 재정렬] 카메라 heading이 한 프레임에 크게 튀면(방 전환의 SetHeadingImmediate 등 불연속 스냅)
        // 키를 계속 누르고 있어도 이동 기준을 즉시 새 heading으로 재정렬한다 — 방이 90° 회전하면 '화면 위'가
        // 바뀌므로 기준도 따라가야 조작이 화면과 맞는다. 이게 없으면 방 회전 후 키를 누른 채면 옛 방향으로 계속 간다.
        bool headingSnapped = Mathf.Abs(Mathf.DeltaAngle(camYaw, _prevCamYaw)) > MoveBasisSnapRelatchDeg;
        _prevCamYaw = camYaw;

        // [기준 고정] 카메라가 연출로 회전하는 동안 기준을 매 프레임 갱신하면, 입력을 누르고 있는 것만으로
        // 이동 방향이 카메라를 따라 휩쓸려 조작이 어긋난다(계단에서 시선이 도는 동안 특히).
        // 그래서 입력이 유지되는 동안에는 '누르기 시작한 시점의 카메라 기준'을 그대로 쓰고,
        // 입력을 놓거나 방향을 바꿀 때 — 또는 위처럼 heading이 불연속으로 스냅될 때 — 현재 카메라 기준으로 다시 잡는다.
        // → 부드러운 회전 중에는 캐릭터가 일관된 월드 방향으로 계속 이동(연출 유지), 방 전환 스냅에서는 즉시 새 방 기준으로 정렬.
        if (headingSnapped || input.sqrMagnitude < 0.0001f || (input - _lastMoveInput).sqrMagnitude > MoveBasisRelatchThresholdSqr)
            _moveBasisYaw = camYaw;
        _lastMoveInput = input;

        _moveDirection = (Quaternion.Euler(0f, _moveBasisYaw, 0f) * new Vector3(input.x, 0f, input.y)).normalized;
    }
}
