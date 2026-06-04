using UnityEngine;

public class LocoMoveState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    // MoveBlend(1D) 목표값: 정지=0, 걷기=0.5, 달리기=1.0
    private const float WalkTarget = 0.5f;
    private const float RunTarget = 1f;
    // MoveSpeed 보간 시간(클수록 걷기↔달리기 전환이 더 점진적).
    private const float BlendDamp = 0.18f;
    // 유물 보유 시, 걷기를 이 시간 이상 지속하면 달리기로 자동 전환.
    private const float RunHoldTime = 1.0f;

    private float _walkTime;   // 연속 걷기 누적 시간
    private bool _forceRun;    // 대시(우클릭) 직후 — 정지 전까지 달리기 유지
    private bool _prevRunning; // 진단용 — running 상태 변화 로그
    private string _lastClip = ""; // 진단용 — 재생 클립 변화 로그

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    {
        _controller = c;
        _stateChanger = changer;
    }

    public void Enter()
    {
        // 공격/스킬 중이면 CrossFade 생략 (공격 애니메이션 덮어쓰기 방지)
        if (!_controller.Combo.IsAttacking)
            _controller.Anim.CrossFade("MoveBlend", 0.05f);

        _walkTime = 0f;
        // 대시 직후 진입이면 바로 달리기로 시작(장비 보유 시에만).
        _forceRun = _controller.HasWeapon && _controller.ConsumeRunAfterDash();
    }

    public void Update()
    {
        var dir = _controller.MoveDirection * _controller.MoveScale;
        bool moving = dir.sqrMagnitude > 0.0001f;

        if (moving) _walkTime += Time.deltaTime;

        // 무장비=걷기만. 장비(무기) 보유 시: 대시 직후(_forceRun) 또는 걷기 1초 지속 → 달리기.
        bool running = moving && _controller.HasWeapon && (_forceRun || _walkTime >= RunHoldTime);
        _controller.IsRunning = running;

        // [진단] running 상태가 바뀔 때 핵심 값 출력 — 원인 확인 후 제거
        if (running != _prevRunning)
        {
            _prevRunning = running;
            var cd = _controller.CharacterData;
            Debug.Log($"[Loco] running={running} | HasRelic={_controller.HasRelic} forceRun={_forceRun} walkTime={_walkTime:F2} " +
                      $"| moveSpeed={(cd != null ? cd.baseMoveSpeed : -1f)} runSpeed={(cd != null ? cd.baseRunSpeed : -1f)} dataName={(cd != null ? cd.characterName : "null")}");
        }

        // 실제 이동 처리 (Move가 IsRunning으로 속도 결정)
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터 — 정지(0)/걷기(0.5)/달리기(1.0)로 부드럽게 보간
        float target = !moving ? 0f : (running ? RunTarget : WalkTarget);
        SetSpeedParam(_controller.Anim, target, BlendDamp);

        // [진단] 실제 재생 중인 클립(최대 가중치) + 장착 무기 — 변할 때만 출력. 원인 확인 후 제거
        if (moving)
        {
            var infos = _controller.Anim.GetCurrentAnimatorClipInfo(0);
            string top = ""; float w = -1f;
            for (int i = 0; i < infos.Length; i++)
                if (infos[i].clip != null && infos[i].weight > w) { w = infos[i].weight; top = infos[i].clip.name; }
            if (top != _lastClip)
            {
                _lastClip = top;
                var wd = _controller.WeaponManager != null ? _controller.WeaponManager.CurrentWeaponData : null;
                Debug.Log($"[LocoClip] 재생='{top}' | 무기={(wd != null ? wd.weaponType.ToString() : "없음")} HasRelic={_controller.HasRelic} running={running}");
            }
        }

        // Air 전이
        if (!_controller.IsGrounded())
        {
            _stateChanger.Change(LocoState.Air);
            return;
        }

        // Idle 전이 (실제 이동 입력 기준)
        if (!moving)
            _stateChanger.Change(LocoState.Idle);
    }

    public void Exit() => _controller.IsRunning = false;

    static void SetSpeedParam(Animator anim, float target01, float damp)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
