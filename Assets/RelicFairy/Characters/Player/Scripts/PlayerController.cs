//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Cinemachine;
using Cysharp.Threading.Tasks;

// 입력 버퍼
using Game.Inputs;

public class PlayerController : CharacterBase
{
    //============================================================
    // Public API (Combat / HUD)
    //============================================================

    // 무기 장착 여부 검사 유틸
    public bool CanAttack()
    {
        // 날아가는 중(피격 넉백)엔 어떤 공격도 불가.
        return WeaponManager != null && WeaponManager.HasWeapon && !IsLaunched;
    }

    // PendingAttack 설정 (무기가 없으면 무시)
    public void SetPendingAttack(Command cmd)
    {
        if (!CanAttack())
        {
            // (선택) 디버그 메시지 또는 UI 안내 호출
            Debug.Log("[PlayerController] 공격 시도했지만 무기가 없습니다.");
            return;
        }

        PendingAttackCommand = cmd;
    }

    public Command PendingAttackCommand { get; private set; } = Command.None;
    public bool HasPendingAttack => PendingAttackCommand != Command.None;

    public WeaponActionType CurrentAttackTypeForEffect { get; set; }

    /// <summary>강공 차지 완료도(0~1). ActAttackChargeState가 발동 시점에 기록, WeaponEffectHandler가 원형 AoE 반경 스케일에 사용.</summary>
    public float HeavyChargeLevel01 { get; set; }

    // PendingAttack 초기화
    public void ClearPendingAttack()
    {
        PendingAttackCommand = Command.None;
    }

    //============================================================
    // Character / Runtime Stats (HUD)
    //============================================================
    [Header("Hit VFX")]
    [Tooltip("피격 시 스폰할 VolumetricBlood VFX 프리팹.")]
    [SerializeField] private GameObject _hitBloodVfxPrefab;
    [Tooltip("Blood VFX 스케일 배율.")]
    [SerializeField] private float _hitBloodVfxScale = 1f;
    [Tooltip("플레이어 발 기준 Blood VFX 높이 오프셋.")]
    [SerializeField] private float _hitBloodVfxHeightOffset = 1f;

    [Header("Combat Tuning")]
    [Tooltip("모든 공격 애니메이션 속도에 곱해지는 전역 배율. 레벨 디자인용 (기본값 1.0).")]
    [Range(0.1f, 3f)]
    [SerializeField] private float _globalAttackAnimSpeedScale = 1.25f;
    public float GlobalAttackAnimSpeedScale => _globalAttackAnimSpeedScale;

    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    [Tooltip("선택된 유물 클래스 (패시브+고유스킬+외형). 비우면 유물 없음.")]
    [SerializeField] protected RelicClassSO relicClass;
    [SerializeField] private bool debugInvincible = false;
    public CharacterData CharacterData => characterData;
    public RelicClassSO RelicClass => relicClass;

    /// <summary>현재 적용된 유물 행동 객체 (없으면 null). HolyShield 등 스킬/외부가 참조.</summary>
    public IRelicBehavior RelicBehavior { get; private set; }

    // 아이템 효과: 시간 제한 무적 (DeathNegate 등)
    private float _invincibleEnd;

    // 런타임 실시간 스탯 (HUD는 이걸 구독)
    public PlayerRuntimeStats RuntimeStats { get; private set; } = new PlayerRuntimeStats();

    // 멀린 룬 속성 단계 효과 디스패처 (단계 도달 시 MerlinRuneBridge가 Activate)
    private RuneEffectDispatcher _runeEffects;
    public RuneEffectDispatcher RuneEffects => _runeEffects ??= new RuneEffectDispatcher(this);
    /// <summary>이미 생성된 룬 디스패처(없으면 null). 지연 생성을 강제하지 않는 읽기 전용 조회 — 버프창 수집용.</summary>
    public RuneEffectDispatcher RuneEffectsOrNull => _runeEffects;

    // 스킬 버프: 기본공격 시 추가 발사 횟수 (0이면 비활성)
    public int ExtraShotCount { get; set; }

    // 사망 처리 1회 가드 (씬 전환 시 새 인스턴스라 리셋 불필요)
    private bool _dead;

    /// <param name="ignorePoise">포이즈를 무시하고 확정으로 날아감을 발동한다(보스 대기술 등). 넉백 면역은 그대로 적용.</param>
    public virtual void TakeDamage(int dmg, GameObject attacker = null, bool ignorePoise = false)
    {
        // 저스트 회피 — 회피 초반(퍼펙트 창)에 스친 공격이면 슬로모+이동보너스로 보상하고 피해는 무효.
        if (TryPerfectDodge()) return;

        if (debugInvincible || Time.time < _invincibleEnd)
            return;

        // 유물 피해 보정 (갈라하드 방패 전방 감소 등)
        if (RelicBehavior != null)
            dmg = RelicBehavior.ModifyIncomingDamage(this, dmg, attacker);

        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;

        // 피격 전 — 무효화/감소 처리
        var pkt = new DamagePacket(dmg, attacker: attacker, target: gameObject);
        mgr?.OnPreTakeDamage(ref pkt);

        if (pkt.Negated)
        {
            FirePassive(PassiveTrigger.OnTakeDamage, new PassiveContext { damage = 0, attacker = attacker });
            return;
        }

        int finalDmg = Mathf.Max(0, (int)pkt.FinalDamage);

        // [서약] 들어오는 피해 변조 — 사망 체크 전(Galahad 무효 등이 치명타를 취소할 수 있도록)
        var covHandler = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
        if (covHandler != null)
        {
            float fd = covHandler.ModifyIncoming(finalDmg, new CombatContext { Target = gameObject, Damage = finalDmg });
            finalDmg = Mathf.Max(0, (int)fd);
        }

        // [방어력] 체감 감소(diminishing returns) — 피해배율 = K / (K + 방어력).
        // 가산 스택이 100%로 수렴하지 않아 상한 캡이 불필요하고, 방어력 K당 유효체력이 원 체력만큼 선형 증가한다.
        // K는 밸런스 튜닝 값(기본 방어 32 기준 감소율 약 24%).
        const float DefenseK = 100f;
        int defense = RuntimeStats.Defense;
        if (defense > 0)
            finalDmg = Mathf.Max(0, Mathf.RoundToInt(finalDmg * (DefenseK / (DefenseK + defense))));

        // [받피감소 통합 채널] 아이템/캐릭터/어둠룬 피해감소(%)를 한 곳에서 1회 적용.
        // (DamageReductionEffect.OnPreTakeDamage 제거 → 여기로 통합. DamageReduction은 Recalculate에서 Clamp01.)
        float dr = RuntimeStats.DamageReduction;
        if (dr > 0f)
            finalDmg = Mathf.Max(0, Mathf.RoundToInt(finalDmg * (1f - dr)));

        // 실드 흡수 — HP 차감 전. 실드가 먼저 피해를 받는다.
        if (finalDmg > 0)
        {
            finalDmg = RuntimeStats.AbsorbWithShield(finalDmg);
        }

        // 사망 직전 체크
        if (RuntimeStats.Hp - finalDmg <= 0 && mgr != null)
        {
            if (mgr.OnNearDeath(out float healPct, out float invDur))
            {
                int healHp = Mathf.Max(1, (int)(RuntimeStats.MaxHp * healPct));
                RuntimeStats.SetHp(healHp);
                if (invDur > 0f)
                {
                    _invincibleEnd = Time.time + invDur;
                    ItemEffectVfxHelper.AttachLoopVfx("VFX_DeathNegateAura", transform, invDur).Forget();
                    ItemEffectVfxHelper.ShowNotice($"<color=#FF4444>사망 무효!</color> {invDur:F0}초 무적");
                }
                return;
            }
        }

        RuntimeStats.Damage(finalDmg);

        if (finalDmg > 0)
        {
            SpawnHitBloodVfx();
            OnDamageTaken?.Invoke();

            // 피격 반작용 — 카메라 셰이크(피해 비례). 위험 전달/타격감.
            // 글로벌 히트스톱은 의도적으로 생략(들어오는 피해에 프리즈=렉 체감, 보스별 설계 히트스톱은 별도 유지).
            float maxHp = RuntimeStats != null ? Mathf.Max(1f, RuntimeStats.MaxHp) : 100f;
            float sev = Mathf.Clamp01(finalDmg / (maxHp * 0.2f)); // 최대HP 20% 피해 = 최대 강도
            HitFeelService.CameraShake(Mathf.Lerp(0.05f, 0.16f, sev), 0.18f);

            // 룬 속성 OnDamaged 통지(어둠 게이지 등). 실제 피해가 들어갈 때만 — i-frame/회피/무효/사망무효는 위에서 이미 return.
            _runeEffects?.NotifyDamaged(finalDmg, attacker);
        }

        // [서약] 피격 통보 (실제 적용 피해량)
        covHandler?.OnTakeDamage(finalDmg);

        // 사망 판정 — 아이템(OnNearDeath) 부활 실패 후 HP 0이면 서약 사망방지 체크, 그래도 0이면 사망 처리.
        TryHandleDeath();

        // 포이즈(아머치) — 누적 임팩트가 최대치를 넘으면 날아감(LocoState.Launched) 발동.
        // 회복이 붙어 있어 연타로 맞을 때만 브레이크된다(띄엄띄엄 맞으면 회복돼 안 날아감).
        // 넉백 면역 중이면 PoiseController가 알아서 무시 → 무한 저글링 방지. 사망했으면 생략.
        //
        // 잡힌 상태(IsGrabbed)에서도 생략한다. 보스 손에 붙들린 채 내리찍히는 동안 포이즈가 터지면
        // 몸은 손바닥에 고정돼 있는데 자세만 '날아감(누움)'으로 바뀌어, 잡혀 있는데 누워 있는 그림이 된다.
        // 붙들린 동안의 연출 권한은 잡기 패턴이 가진다.
        if (!_dead && !IsGrabbed && Poise != null && CharacterData != null && RuntimeStats != null)
        {
            float maxPoise = RuntimeStats.MaxPoise;
            float immunity = CharacterData.knockbackImmunity;

            bool broke = ignorePoise
                ? Poise.ForceBreak(maxPoise, immunity)
                : Poise.TakeImpact(finalDmg * CharacterData.poiseImpactPerDamage,
                                   maxPoise, CharacterData.poiseRegenDelay, immunity);

            if (broke) LaunchFrom(attacker);
        }

        // 피격 후 — 반사/방버프 등. Attacker를 채워야 DamageReflect/FireReflect가 반사 대상을 안다.
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker = attacker,
            Target = gameObject,
            HitPosition = attacker != null ? attacker.transform.position : transform.position,
        };
        mgr?.OnPostTakeDamage(report);

        FirePassive(PassiveTrigger.OnTakeDamage, new PassiveContext { damage = finalDmg, attacker = attacker });
    }

    public void Heal(int amount)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.ModifyHeal(ref amount);
        RuntimeStats.Heal(amount);
    }

    /// <summary>
    /// HP가 0 이하면 서약 사망방지 체크 후 사망 처리. TakeDamage 외 경로(아이템 자해 등)로
    /// HP가 소진됐을 때 호출해 "다음 피격까지 생존" 버그를 막는다.
    /// </summary>
    public void NotifyHpDepleted() => TryHandleDeath();

    private void TryHandleDeath()
    {
        if (RuntimeStats.Hp > 0 || _dead) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.CovenantHandler != null && run.CovenantHandler.TryPreventDeath())
        {
            RuntimeStats.SetHp(Mathf.Max(1, RuntimeStats.Hp)); // 서약 사망방지 → 사망 취소
            _invincibleEnd = Time.time + 1f;
        }
        else
        {
            _dead = true;
            SetInputEnabled(false);
            ClearPerfectDodge();   // 사망했는데 슬로모가 남아 시간이 느린 채로 진행되는 것 방지
            if (IntroBootstrapper.Instance != null)
                IntroBootstrapper.Instance.HandleIntroDeath();
            else
                GameRunBootstrapper.Instance?.HandlePlayerDeath();
        }
    }

    /// <summary>외부에서 일시 무적 상태로 설정. 기존 무적이 남아있으면 더 긴 쪽을 유지.
    /// 사용처: 낙사 리스폰(FallRecoveryController), 부활 아이템 등.</summary>
    public void SetInvincible(float duration)
    {
        if (duration <= 0f) return;
        _invincibleEnd = Mathf.Max(_invincibleEnd, Time.time + duration);
    }

    /// <summary>무적 중 여부 (debugInvincible 포함).</summary>
    public bool IsInvincible => debugInvincible || Time.time < _invincibleEnd;

    /// <summary>보스 잡기 패턴에 붙들려 있는 중인가. 이 동안은 포이즈 브레이크(날아감)를 발동하지 않는다.</summary>
    public bool IsGrabbed { get; private set; }

    /// <summary>잡기 패턴이 붙들기 시작/해제 시 호출. 이동 잠금(SetMoveScale)과 같은 수명으로 다뤄야 한다.</summary>
    public void SetGrabbed(bool grabbed) => IsGrabbed = grabbed;

    /// <summary>
    /// 플레이어 입력 전체를 활성/비활성화한다(컷신·연출 채널).
    /// 보스 등장 연출 등 컷씬 구간에서 false로 호출해 행동을 막는다.
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        // 컷신이 inputActions 생성(비동기 초기화) 전에 차단을 걸 수 있다.
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

    /// <summary>두 차단 사유(컷신/UI)를 합쳐 실제 InputAction 활성 상태에 반영한다.</summary>
    private void ApplyInputState()
    {
        bool enabled = !_inputDisabledExternally && !_inputBlockedByUI;

        // 입력을 끊으면 moveDirection 갱신도 멈춘다 → 마지막 입력값이 그대로 남아
        // 컷신 내내 달리는 자세로 이동한다. 차단 시 즉시 0으로 비운다.
        if (!enabled) moveDirection = Vector3.zero;

        if (inputActions == null) return;
        if (enabled) inputActions.Player.Enable();
        else         inputActions.Player.Disable();
    }

    // 외부(컷신 등)가 요청한 입력 차단이 유효한지. InitInputActions()가 이 의도를 존중한다.
    private bool _inputDisabledExternally;
    // 차단형 UI 팝업이 걸어둔 입력 차단. 컷신 차단과 독립.
    private bool _inputBlockedByUI;

    //============================================================
    // Thunder Groggy (번개 그로기 — 비네트로 시야 축소)
    //============================================================

    /// <summary>번개 그로기: duration초 동안 비네트로 시야를 좁힌다.</summary>
    public void ApplyThunderGroggy(float duration)
        => RelicFairy.Monster.ThunderGroggyVignetteView.Trigger(duration);

    public event Action OnDamageTaken;

    /// <summary>회피 무적 창 시작(true)/종료(false) 신호. 시각 "안전" 피드백(머티리얼 블링크/잔상 등) 구독용.
    /// 현재 렌더러 직접 조작 없이 훅만 노출 — 후속에서 안전한 시각 효과를 여기에 연결한다.</summary>
    public event Action<bool> OnDodgeIFrame;
    public void RaiseDodgeIFrame(bool active) => OnDodgeIFrame?.Invoke(active);

    /// <summary>회피 시작(Enter)/종료(Exit) 신호. 대시 먼지·트레일 등 i-frame 창과 무관하게
    /// 회피 동작 전체에 걸리는 시각 연출 구독용(DodgePresentation).</summary>
    public event Action OnDodgeStart;
    public event Action OnDodgeEnd;
    public void RaiseDodgeStart() => OnDodgeStart?.Invoke();
    public void RaiseDodgeEnd()   => OnDodgeEnd?.Invoke();

    public event Action OnHudStatChanged
    {
        add => RuntimeStats.OnChanged += value;
        remove => RuntimeStats.OnChanged -= value;
    }

    //============================================================
    // References (Weapon / Input / Camera / Animation)
    //============================================================

    // 플레이어가 가지는 무기 매니저 (인스펙터에서 붙이거나 런타임에 AddComponent)
    public PlayerWeaponManager WeaponManager { get; private set; }

    protected PlayerInputActions inputActions;
    [System.NonSerialized] public bool inputReady = false;

    [Header("Camera")]
    [SerializeField] protected CinemachineFreeLook cinemachineCamera;

    /// <summary>추적 중인 FreeLook 카메라. 전투 동적 프레이밍 등이 읽는다.</summary>
    public CinemachineFreeLook CinemachineCamera => cinemachineCamera;

    // 애니메이터 오버라이드 서비스
    private AnimatorOverrideService _animSvc;

    // 애니메이션 이벤트 리시버
    public PlayerAnimationEventReceiver EventReceiver;

    // 장비 이펙트 생성을 관리하는 핸들러
    public WeaponEffectHandler EffectHandler;

    // 현재 활성화된 실행 컨텍스트 (공격/스킬 상태가 Enter 시 할당, Exit 시 해제)
    public AbilityExecution ActiveExecution { get; set; }

    // 회피 쿨다운 종료 시각 (Time.time 기준). 0이면 즉시 사용 가능
    public float DodgeCooldownEnd { get; set; } = 0f;

    private bool _aeSubscribed = false;


    //============================================================
    // Input / Movement State
    //============================================================

    // 이동 기준으로 삼는 카메라 수평각. 입력을 누르고 있는 동안 고정된다(아래 CheckMovementInput 참조).
    private float _moveBasisYaw;
    private Vector2 _lastMoveInput;

    // 직전 프레임 카메라 heading. 한 프레임에 크게 튀는 '불연속 스냅'(방 전환의 SetHeadingImmediate 등) 감지용.
    private float _prevCamYaw;

    // 입력이 "바뀌었다"고 볼 최소 변화량(제곱). 아날로그 스틱 미세 흔들림으로 기준이 재설정되지 않게 한다.
    private const float MoveBasisRelatchThresholdSqr = 0.04f;   // 약 0.2 변화

    // 이 각도(도)를 초과하는 한 프레임 heading 변화는 불연속 스냅으로 보고 이동 기준을 즉시 재정렬한다.
    // 방 회전(90° 단위 스냅)은 이 문턱을 넘고, 연출용 부드러운 회전(RotateHeadingTo)은 프레임당 변화가 훨씬 작아 걸리지 않는다.
    private const float MoveBasisSnapRelatchDeg = 60f;

    // PlayerController.cs (입력 시 클릭 위치 저장)
    private Vector3? _lastClickedPosition;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    private bool isRunChecked = false;
    public bool IsRunChecked => isRunChecked;

    //============================================================
    // 로코모션 모드 — 유물 보유 시 자동 걷기→달리기, 우클릭 대시 후 달리기 유지
    //============================================================
    /// <summary>유물 보유 여부.</summary>
    public bool HasRelic => RelicBehavior != null;
    /// <summary>장비(무기) 보유 여부. 달리기 기능은 장비 획득 시 활성화된다.</summary>
    public bool HasWeapon => WeaponManager != null && WeaponManager.HasWeapon;
    /// <summary>현재 달리기 중인지(LocoMoveState가 결정·설정, DefaultMoveAbility가 속도에 사용).</summary>
    public bool IsRunning { get; set; }
    /// <summary>걷기→달리기 속도 램프 진행도(0=걷기, 1=달리기). LocoMoveState가 설정, DefaultMoveAbility가 속도 보간에 사용.</summary>
    public float RunBlend01 { get; set; }
    /// <summary>현재 수평 실속도를 runMax 기준 0~1로 정규화. 애니 MoveSpeed 구동용(실속도라 가속·감속 반영 + 발미끄러짐 방지).</summary>
    public float HorizontalSpeed01
    {
        get
        {
            var cd = CharacterData;
            if (cd == null) return 0f;
            float runMax = cd.baseRunSpeed > 0.01f ? cd.baseRunSpeed : cd.baseMoveSpeed;
            if (runMax < 0.01f) return 0f;
            Vector3 v = Rigid.linearVelocity;
            float mag = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            return Mathf.Clamp01(mag / runMax);
        }
    }
    private bool _runAfterDash;
    /// <summary>대시(우클릭) 종료 시 다음 이동을 달리기로 시작하도록 요청.</summary>
    public void RequestRunAfterDash() => _runAfterDash = true;
    /// <summary>대시 후 달리기 요청을 소비(읽고 클리어).</summary>
    public bool ConsumeRunAfterDash() { bool v = _runAfterDash; _runAfterDash = false; return v; }
    /// <summary>대시 후 달리기 요청 클리어(정지/Idle 시).</summary>
    public void ClearRunAfterDash() => _runAfterDash = false;

    // 입력 정책(무기 타입별: 소드/활 등)
    private IAttackInputPolicy _attackPolicy;

    //============================================================
    // Layer FSM (Locomotion / Action)
    //============================================================
    protected LayerStateMachine<LocoState> locoSM;
    protected LayerStateMachine<ActState> actSM;
    public LayerStateMachine<LocoState> LocoSM => locoSM;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private LocoState locoStateDebug;
    [SerializeField] private ActState actStateDebug;

    //============================================================
    // Move Scale (MoveLock 제거)
    //============================================================
    public float MoveScale { get; private set; } = 1f;
    public void SetMoveScale(float s) => MoveScale = Mathf.Clamp01(s);

    private float _slowTimer;

    /// <summary>이동 속도를 scale 배율로 duration초 동안 감소시킨다. 종료 시 자동으로 1f로 복구.</summary>
    public void ApplySlow(float scale, float duration)
    {
        SetMoveScale(scale);
        _slowTimer = Mathf.Max(_slowTimer, duration);
    }

    /// <summary>슬로우 상태를 즉시 해제하고 부착된 VFX를 제거한다.</summary>
    public void ClearSlow()
    {
        _slowTimer = 0f;
        SetMoveScale(1f);
        var fx = transform.Find("StatusEffect_Slow");
        if (fx != null) Destroy(fx.gameObject);
        var screenFx = transform.Find("StatusEffectScreen_Slow");
        if (screenFx != null) Destroy(screenFx.gameObject);
    }

    private float _freezeTimer;
    public bool IsFrozen => _freezeTimer > 0f;

    [Tooltip("빙결 상태(얼음 쉴드+스크린 이펙트) 동안 반복 재생할 사운드")]
    [SerializeField] private AudioClip _freezeLoopSfx;
    private AudioSource _freezeLoopAudioSource;

    private int   _iceStage;       // 0=기본, 1=경고(화면이펙트 활성)
    private float _iceStageTimer;
    public const float IceStageWindowDuration = 3f;
    private const float IceStage1SoundVolume  = 0.3f;

    /// <summary>빙결: duration초 동안 이동·행동·입력을 완전히 차단한다. 연속 피격 시 남은 시간을 연장.</summary>
    public void ApplyFreeze(float duration)
    {
        _freezeTimer = Mathf.Max(_freezeTimer, duration);
        if (_freezeLoopAudioSource == null || !_freezeLoopAudioSource.isPlaying)
        {
            StopFreezeLoopSfx();
            _freezeLoopAudioSource = Managers.Sound?.PlayLoopingEffectAt(_freezeLoopSfx, transform.position);
        }
    }

    /// <summary>
    /// 얼음 공격 1회 처리. 반환값: 1=1단계(화면이펙트), 2=빙결 발동, 0=이미 빙결 중(연장만).
    /// </summary>
    public int AddIceStack(float fullDuration)
    {
        if (IsFrozen)
        {
            ApplyFreeze(fullDuration * 0.5f);
            return 2;
        }

        if (_iceStage == 1 && _iceStageTimer > 0f)
        {
            _iceStage      = 0;
            _iceStageTimer = 0f;
            ApplyFreeze(fullDuration * 0.5f);
            return 2;
        }

        _iceStage      = 1;
        _iceStageTimer = IceStageWindowDuration;
        Managers.Sound?.PlayEffect(_freezeLoopSfx, IceStage1SoundVolume);
        return 1;
    }

    private void StopFreezeLoopSfx()
    {
        if (_freezeLoopAudioSource == null) return;
        Managers.Sound?.StopLoopingEffect(_freezeLoopAudioSource);
        _freezeLoopAudioSource = null;
    }

    //============================================================
    // Knockback
    //============================================================
    private float _knockbackTimer;
    public bool IsKnockback => _knockbackTimer > 0f;

    /// <summary>외부 힘(넉백)을 가하고 일정 시간 동안 수평 이동 잠금을 스킵한다.</summary>
    public void ApplyKnockback(Vector3 force, float duration = 0.3f)
    {
        Rigid?.AddForce(force, ForceMode.Impulse);
        _knockbackTimer = duration;
    }

    //============================================================
    // 저스트 회피 (퍼펙트 닷지) — 회피 초반에 공격이 스치면 슬로모 + 플레이어 이동 보너스
    //============================================================
    // 발동 순간 프레임 스톱에 쓰는 시간 배율(완전 0은 물리/애니가 죽어 복귀가 튈 수 있어 아주 작은 값).
    private const float PerfectDodgeFreezeScale = 0.02f;
    private const int   PerfectDodgeSenseMax    = 8;   // windup 감지 시 훑을 적 수 상한

    // windup 감지 질의 버퍼(재사용 — 회피마다 alloc 방지)
    private readonly List<RelicFairy.Monster.MonsterBase> _perfectDodgeSenseBuf = new(PerfectDodgeSenseMax);

    private bool  _perfectDodgeArmed;          // 이번 회피에서 아직 발동하지 않았는지
    private float _perfectDodgeEnd;            // 슬로모/보너스 종료 시각(unscaled)
    private float _perfectDodgeFreezeEnd;      // 프레임 스톱 종료 시각(unscaled)
    private bool  _perfectDodgeFrozen;         // 지금 프레임 스톱 구간인지
    private float _perfectDodgeScale = 0.35f;  // 프리즈 후 적용할 슬로모 배율
    private float _bonusMoveMultiplier = 1f;

    /// <summary>저스트 회피 슬로모 동안의 이동 배율(평소 1). DefaultMoveAbility가 최고속에 곱한다.</summary>
    public float BonusMoveSpeedMultiplier => _bonusMoveMultiplier;

    /// <summary>저스트 회피 발동 — 연출(회색 필터/틴트/잔상)이 구독한다. 인자는 총 지속시간(초, 실제시간).</summary>
    public event Action<float> OnPerfectDodge;

    /// <summary>
    /// 회피 진입 시 호출(LocoDodgeState.Enter). 퍼펙트 판정을 장전하고, <b>이미 날아오는 공격이 있으면 즉시 발동</b>한다.
    ///
    /// '맞으면 발동'만으로는 거의 안 터진다 — 몹은 피해를 적용하는 순간에 거리를 <b>다시</b> 재는데
    /// (MonsterBase.DealDamageToPlayer), 대시로 4m를 빠져나가면 그 검사에서 걸러져 TakeDamage 자체가
    /// 호출되지 않는다. 즉 "회피에 성공하면 판정이 안 온다"는 모순이 생긴다.
    /// 그래서 <b>적의 공격 windup(예고) 중에 회피를 시작했는가</b>로 잡는다. 이게 긴급회피의 실제 정의다.
    /// </summary>
    public void ArmPerfectDodge()
    {
        _perfectDodgeArmed = true;
        if (SensesIncomingAttack()) TriggerPerfectDodge("공격 예고 회피");
    }

    /// <summary>회피 반경 안에 공격 windup 중인 적이 있는가(= 지금 회피하면 아슬하게 피하는 것).</summary>
    private bool SensesIncomingAttack()
    {
        float r = characterData != null ? characterData.perfectDodgeSenseRadius : 4f;
        if (r <= 0f) return false;

        int n = CombatQuery.GetNearbyEnemies(transform.position, r, gameObject, PerfectDodgeSenseMax, _perfectDodgeSenseBuf);
        for (int i = 0; i < n; i++)
        {
            var mb = _perfectDodgeSenseBuf[i];
            if (mb != null && mb.IsTelegraphingAttack) return true;
        }
        return false;
    }

    /// <summary>
    /// <b>대시(회피) 중에 공격을 맞았는가</b>를 판정. TakeDamage 맨 앞에서 호출.
    /// 대시 전 구간이 무적이라 피해는 어차피 0이지만, 회피로 파고들어 실제로 접촉한 경우를 여기서 잡는다.
    /// (대부분의 저스트 회피는 위 windup 감지로 먼저 발동한다.)
    /// </summary>
    private bool TryPerfectDodge()
    {
        if (!_perfectDodgeArmed) return false;
        if (locoSM == null || locoSM.CurrentId != LocoState.Dodge) return false;

        TriggerPerfectDodge("대시 중 피격");
        return true;
    }

    /// <summary>저스트 회피 발동 본체 — 슬로모 + 이동 보너스 + 연출 신호. 회피 1회당 1발.</summary>
    private void TriggerPerfectDodge(string trigger)
    {
        if (!_perfectDodgeArmed) return;
        _perfectDodgeArmed = false;

        float scale    = characterData != null ? Mathf.Clamp(characterData.perfectDodgeTimeScale, 0.05f, 1f) : 0.35f;
        float duration = characterData != null ? Mathf.Max(0f, characterData.perfectDodgeDuration)   : 1.2f;
        float boost    = characterData != null ? Mathf.Max(1f, characterData.perfectDodgeSpeedBoost) : 1.3f;
        float freeze   = characterData != null ? Mathf.Max(0f, characterData.perfectDodgeFreeze)     : 0.07f;
        if (duration <= 0f) return;

        _perfectDodgeScale = scale;

        // 시간은 TimeScaleArbiter가 단일 소유 — 직접 Time.timeScale을 만지지 않는다.
        // 프레임 스톱과 슬로모는 우선순위가 달라(HitStop 10 < SlowMotion 100) 겹쳐 걸 수 없으므로,
        // 같은 owner로 '프리즈 → 슬로모' 순차 덮어쓰기를 한다.
        _perfectDodgeFrozen = freeze > 0f;
        TimeScaleArbiter.Acquire(this,
            _perfectDodgeFrozen ? PerfectDodgeFreezeScale : scale,
            TimeScaleArbiter.Priority.SlowMotion);

        _perfectDodgeFreezeEnd = Time.unscaledTime + freeze;
        _perfectDodgeEnd       = Time.unscaledTime + freeze + duration;

        // 세계는 느려지는데 플레이어는 빨라야 한다.
        // 물리는 스케일된 시간으로 적분되므로, 시간배율의 역수(1/scale)만큼 되돌리고 그 위에 부스트를 얹는다.
        _bonusMoveMultiplier = (1f / scale) * boost;

        // 세계만 느려지고 플레이어는 정상 속도로 움직여야 한다(Witch Time의 핵심).
        // Time.timeScale은 전역이라 Animator까지 같이 느려진다 → 플레이어 Animator만 실제시간으로 돌린다.
        // 이게 없으면 "슬로모 애니로 빠르게 미끄러지고 공격도 느리게 나가는" 반쪽짜리가 된다.
        if (Anim != null) Anim.updateMode = AnimatorUpdateMode.UnscaledTime;

        // 발동 임팩트 — 짧은 카메라 펀치
        HitFeelService.CameraShake(0.1f, 0.12f);

        OnPerfectDodge?.Invoke(freeze + duration);
        Debug.Log($"[저스트회피] 발동 ({trigger}) | 슬로모 {scale:F2}배 {duration:F1}초 · 이동 {_bonusMoveMultiplier:F1}배");
    }

    /// <summary>저스트 회피 프리즈→슬로모 전환 및 만료 처리. Update에서 unscaled 시간으로 구동.</summary>
    private void TickPerfectDodge()
    {
        if (!TimeScaleArbiter.IsHeldBy(this)) return;

        // 프레임 스톱 종료 → 같은 owner로 슬로모 배율로 덮어쓴다.
        if (_perfectDodgeFrozen && Time.unscaledTime >= _perfectDodgeFreezeEnd)
        {
            _perfectDodgeFrozen = false;
            TimeScaleArbiter.Acquire(this, _perfectDodgeScale, TimeScaleArbiter.Priority.SlowMotion);
        }

        if (Time.unscaledTime < _perfectDodgeEnd) return;

        TimeScaleArbiter.Release(this);
        _bonusMoveMultiplier = 1f;
        _perfectDodgeFrozen  = false;
        if (Anim != null) Anim.updateMode = AnimatorUpdateMode.Normal;
    }

    /// <summary>슬로모를 강제 종료한다(사망/씬 전환 시 시간이 느린 채로 남지 않도록).</summary>
    private void ClearPerfectDodge()
    {
        TimeScaleArbiter.Release(this);
        _bonusMoveMultiplier = 1f;
        _perfectDodgeArmed   = false;
        _perfectDodgeFrozen  = false;
        if (Anim != null) Anim.updateMode = AnimatorUpdateMode.Normal;
    }

    // 다음 로코모션 진입(MoveBlend) 크로스페이드 길이 1회 오버라이드. -1이면 각 상태의 기본값 사용.
    // 회피 종료처럼 '자세 차이가 큰 상태에서 복귀'할 때만 길게 잡아 툭 튀는 스냅을 없앤다.
    private float _pendingLocoBlend = -1f;

    /// <summary>회피 종료 등에서 다음 로코모션 크로스페이드를 길게 잡도록 예약한다.</summary>
    public void RequestLocoBlend(float duration) => _pendingLocoBlend = duration;

    /// <summary>예약된 크로스페이드 길이를 소비한다(1회). 없으면 기본값 반환.</summary>
    public float ConsumeLocoBlend(float defaultDuration)
    {
        if (_pendingLocoBlend < 0f) return defaultDuration;
        float d = _pendingLocoBlend;
        _pendingLocoBlend = -1f;
        return d;
    }

    // 날아감 진입 방향 — LaunchFrom이 세팅하고 LocoLaunchedState.Enter가 1회 소비한다.
    private Vector3 _pendingLaunchDir;

    /// <summary>날아감 방향(수평 정규화)을 소비한다. 미설정이면 등 뒤(-forward).</summary>
    public Vector3 ConsumeLaunchDirection()
    {
        Vector3 d = _pendingLaunchDir;
        _pendingLaunchDir = Vector3.zero;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : -transform.forward;
    }

    /// <summary>
    /// 대시 스태미너를 소모 시도한다. 부족하면 false → 대시 불발(쿨타임 대신 자원이 게이트).
    /// </summary>
    private bool TryConsumeDodgeStamina()
    {
        if (Stamina == null || characterData == null || RuntimeStats == null) return true;

        return Stamina.TryConsume(characterData.dodgeStaminaCost, RuntimeStats.MaxStamina,
                                  characterData.staminaRegenDelay);
    }

    /// <summary>진행 중인 공격/스킬을 즉시 취소한다(날아감 진입 등).</summary>
    public void CancelActions()
    {
        Combo?.Reset();
        if (actSM != null && actSM.CurrentId != ActState.None)
            actSM.Change(ActState.None);
    }

    /// <summary>
    /// 피격 넉백 — 날아감(LocoState.Launched) 진입. attacker 반대방향으로 띄운다.
    /// 사망 중이거나 이미 날아가는 중이면 무시한다.
    /// </summary>
    public void LaunchFrom(GameObject attacker)
    {
        if (_dead || locoSM == null) return;
        if (locoSM.CurrentId == LocoState.Launched) return;

        Vector3 dir = attacker != null
            ? (transform.position - attacker.transform.position)
            : -transform.forward;
        dir.y = 0f;
        _pendingLaunchDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;

        locoSM.Change(LocoState.Launched);
    }

    //============================================================
    // Input Buffer & Time
    //============================================================
    protected IClock Clock { get; private set; }
    public InputBuffer InputBuffer { get; private set; }
    protected void EnqueueCommand(Command cmd) => InputBuffer?.Push(cmd);

    //============================================================
    // Ability Modules
    //============================================================
    public IMoveAbility<PlayerController> MoveAbility { get; protected set; }
    public IDodgeAbility<PlayerController> DodgeAbility { get; protected set; }
    public IJumpAbility JumpAbility { get; protected set; }

    public Transform handTransform;
    public Transform handTransformLeft;

    //============================================================
    // Combo State (콤보 관련 상태는 ComboController에 위임)
    //============================================================
    public ComboController Combo { get; private set; }

    /// <summary>포이즈(아머치) 게이지 — 누적 임팩트가 최대치를 넘으면 날아감(LocoState.Launched) 발동.</summary>
    public PoiseController Poise { get; private set; }

    /// <summary>스태미너 게이지 — 대시(회피)의 자원 게이트. 쿨타임을 대체한다.</summary>
    public StaminaController Stamina { get; private set; }

    //============================================================
    // Skill Cooldown
    //============================================================
    public SkillCooldownTracker CooldownTracker { get; private set; } = new SkillCooldownTracker();

    //============================================================
    // Passive System
    //============================================================
    private readonly List<ICharacterPassive> _passives = new();

    protected void RegisterPassive(ICharacterPassive passive) => _passives.Add(passive);

    /// <summary>유물 행동 객체가 패시브를 등록할 때 쓰는 public 래퍼.</summary>
    public void RegisterRelicPassive(ICharacterPassive passive) => RegisterPassive(passive);

    /// <summary>
    /// 트리거 조건이 맞는 패시브를 모두 실행한다.
    /// 상태 클래스 및 외부에서 호출 가능.
    /// </summary>
    public void FirePassive(PassiveTrigger trigger, in PassiveContext ctx)
    {
        foreach (var p in _passives)
            if (p.Trigger == trigger && p.CanApply(this, ctx))
            {
                p.Apply(this, ctx);
                // [가이드라인 비주얼] 유물/캐릭터 패시브 발동 토스트(통지만).
                // OnAttackHit 패시브가 4종이라 매 타 4줄이 쏟아져 화면을 덮었다 → 이름별 스로틀.
                // (CovenantHandler.ProcToast와 같은 패턴)
                if (p is CharacterPassiveBase cb && cb.SuppressAutoToast) continue;
                if (!PassiveToastReady(p.PassiveName)) continue;
                GuidelineVisual.Toast(transform.position + Vector3.up * 2.4f, p.PassiveName, GuidelineVisual.ToastKind.Relic);
            }
    }

    // 패시브명별 토스트 스로틀 — 같은 패시브는 이 간격 안에 1회만 표시.
    private const float PassiveToastInterval = 2f;
    private readonly Dictionary<string, float> _passiveToastAt = new();

    private bool PassiveToastReady(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        float now = UnityEngine.Time.unscaledTime;   // 토스트는 unscaled 수명이라 동일 기준
        if (_passiveToastAt.TryGetValue(name, out float last) && now - last < PassiveToastInterval) return false;
        _passiveToastAt[name] = now;
        return true;
    }

    /// <summary>캐릭터별 패시브 등록 — 파생 클래스에서 override.</summary>
    protected virtual void InitPassives() { }

    private bool _relicApplied;
    private GameObject _relicAuraInstance;

    /// <summary>
    /// 런타임에 선택된 유물을 주입·적용한다. 스폰(InitAsync) 이후 호출.
    /// relicClass가 SerializeField라 Instantiate 후엔 Awake가 이미 지나므로, 이 주입점으로 적용한다.
    /// null이면 무동작(CombatGirl 기본 몸 유지).
    /// </summary>
    public void SetRelicAndApply(RelicClassSO relic)
    {
        if (relic == null) return;
        relicClass = relic;
        ApplyRelic();
    }

    /// <summary>
    /// 선택된 유물(relicClass)을 적용 — 행동 OnAttach(코드 패시브/메커닉)
    /// + 유물 스탯(유물 스탯 필드 + 데이터 패시브 PassiveSO의 스탯 보정을 공통 베이스 위 가산)
    /// + 고유스킬 클립 오버라이드 + 외형(오라). relicClass 없으면 무동작.
    /// _relicApplied 가드로 중복 적용을 방지한다(패시브/스탯 이중 적용 차단).
    /// </summary>
    private void ApplyRelic()
    {
        if (relicClass == null || _relicApplied) return;
        _relicApplied = true;

        RelicBehavior = RelicRegistry.Create(relicClass.Id);
        RelicBehavior?.OnAttach(this);

        // 유물 스탯 — 유물 char_id 행(서버)으로 전체 교체(유물이 곧 캐릭터).
        // 서버 데이터/행 없으면 유물 StatModifier 가산으로 폴백.
        string relicCharId = relicClass.Id.ToString().ToLower(); // Gawain → "gawain"
        if (!TryApplyServerStats(relicCharId))
        {
            var relicMods = new System.Collections.Generic.List<StatModifier>();
            if (relicClass.Stats != null) relicMods.AddRange(relicClass.Stats);
            if (relicClass.Passives != null)
                foreach (var p in relicClass.Passives)
                    if (p != null && p.baseModifiers != null) relicMods.AddRange(p.baseModifiers);
            RuntimeStats?.ApplyRelicStats(relicMods);
        }

        if (!string.IsNullOrEmpty(relicClass.QSkillClipKey))
            TryOverrideClip("QSkill_01", relicClass.QSkillClipKey);

        // 외형 — 현재는 오라 VFX만(키 있을 때). 추후 모델/애니메이터 변형은 이 지점에서 확장.
        if (!string.IsNullOrEmpty(relicClass.AuraVfxKey))
            SpawnRelicAuraAsync(relicClass.AuraVfxKey, relicClass.AuraSocket).Forget();

        Debug.Log($"[PlayerController] 유물 적용: {relicClass.Id} (char_id={relicCharId}, passives={relicClass.Passives?.Length ?? 0})");
    }

    /// <summary>유물 오라 VFX를 소켓(없으면 루트)에 부착. 재적용 시 기존 인스턴스를 먼저 정리(멱등).</summary>
    private async UniTaskVoid SpawnRelicAuraAsync(string key, string socket)
    {
        ReleaseRelicAura();

        Transform parent = string.IsNullOrEmpty(socket)
            ? transform
            : (Util.FindDeepChild(transform, socket) ?? transform);

        try
        {
            _relicAuraInstance = await Managers.AddressableManager.InstantiateAsync(key, parent);
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerController] 유물 오라 VFX 로드 실패: {key}\n{e.Message}");
        }
    }

    private void ReleaseRelicAura()
    {
        if (_relicAuraInstance != null)
        {
            Managers.AddressableManager?.ReleaseInstance(_relicAuraInstance);
            _relicAuraInstance = null;
        }
    }

    // ── 캐릭터 고유 스킬 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 캐릭터 고유 스킬 런타임을 반환한다.
    /// 무기 스킬보다 우선 적용. null이면 무기 스킬 또는 레거시 폴백 사용.
    /// </summary>
    public virtual ISkillRuntime CreateCharacterSkillRuntime(SkillType slot) => RelicBehavior?.CreateSkillRuntime(this, slot);

    /// <summary>캐릭터 고유 스킬의 쿨다운(초). 0이면 무기 쿨다운 사용.</summary>
    public virtual float GetCharacterSkillCooldown(SkillType slot) => RelicBehavior?.GetSkillCooldown(slot) ?? 0f;

    /// <summary>
    /// 해당 슬롯에 실제 발동 가능한 스킬이 있는가. <see cref="ActSkillState"/>의 해석 순서와 동일하게
    /// 유물(캐릭터) 런타임 → 무기 슬롯 순으로 확인한다.
    /// 빈 슬롯 입력이 스킬 상태로 진입해 진행 중인 모션만 끊는 것을 막고, HUD 잠금 표시의 근거로도 쓴다.
    /// </summary>
    public bool HasSkillInSlot(SkillType slot)
    {
        // 유물 제공 스킬(주로 Q) — 런타임이 만들어지면 보유로 본다.
        if (CreateCharacterSkillRuntime(slot) != null) return true;

        // 장비(무기) 제공 스킬 — ActSkillState.GetSkillSO와 동일 매핑(R은 skillQ를 쓴다).
        var wd = WeaponManager?.CurrentWeaponData;
        return slot switch
        {
            SkillType.E => wd?.skillE != null,
            SkillType.R => wd?.skillQ != null,
            _           => false,
        };
    }

    /// <summary>
    /// 유물 게이트가 열려 있는가(가웨인 정오 구간 등). <see cref="ActSkillStateBase"/>의 게이팅과 동일 조건 —
    /// <b>유물이 소유한 슬롯</b>에만 적용하고, 무기 스킬 슬롯은 항상 열린 것으로 본다.
    /// 쿨다운은 포함하지 않는다(쿨다운은 HUD에 별도 연출이 있다).
    /// </summary>
    public bool IsSkillGateOpen(SkillType slot)
    {
        if (RelicBehavior == null) return true;
        if (CreateCharacterSkillRuntime(slot) == null) return true;   // 유물 미소유 슬롯 → 게이팅 대상 아님
        return RelicBehavior.CanUseSkill(slot);
    }

    /// <summary>
    /// 지금 당장 발동 가능한가 = 보유 + 유물 게이트 + 쿨다운.
    /// 입력 단계에서 이걸로 막지 않으면 스킬 상태에 <b>진입했다가 되돌아 나오면서</b>
    /// 진행 중이던 공격 모션만 끊긴다(ActSkillStateBase가 OnEnter에서 되돌리는 구조).
    /// </summary>
    public bool CanUseSkillNow(SkillType slot)
    {
        if (!HasSkillInSlot(slot)) return false;
        if (!IsSkillGateOpen(slot)) return false;
        return CooldownTracker == null || CooldownTracker.IsReady(slot);
    }

    //============================================================
    // Runtime Flags (점프 모듈에서 관리하는 상태를 위임)
    //============================================================
    public bool IsGrounded() => JumpAbility?.IsGrounded ?? true;
    public bool IsJumping => JumpAbility?.IsJumping ?? false;

    /// <summary>
    /// 공격/스킬 등 Act 상태가 캐릭터 facing(회전)을 소유 중인지 여부.
    /// true면 이동 회전(DefaultMoveAbility)이 회전을 양보해 facing 경합을 막는다.
    /// (None/Pickup 외의 모든 Act 상태 = 조준/공격이 회전을 주도)
    /// </summary>
    public bool IsActionControllingFacing =>
        actSM != null && actSM.CurrentId != ActState.None && actSM.CurrentId != ActState.Pickup;

    /// <summary>착지 애니메이션 재생 중 여부. LocoAirState가 관리.</summary>
    public bool IsLanding { get; set; }

    // 회전(facing) 목표 — 각 회전 writer가 저장하고, FixedUpdate(ApplyFacing)에서 Rigidbody에 적용한다.
    // Update에서 회전을 직접 대입하면 Rigidbody Interpolate 보간과 타이밍이 어긋나 회전 각도에서 진동이 생긴다.
    private Quaternion _targetFacing;
    private bool _facingDirty;

    // 이동 회전(슬루) — 목표 Yaw를 향해 일정 각속도로 FixedUpdate에서 적분한다.
    // (Update에서 step을 계산하면 Rigidbody.rotation이 물리 스텝에서만 갱신돼 고FPS에서 회전이 느려지는 프레임률 의존 발생 → FixedUpdate 적분으로 해소)
    private bool _facingSlewActive;
    private float _slewTargetYaw;
    private float _slewDegPerSec;

    // 슬루 ease-out — 목표까지 남은 각이 이 값(도) 이하면 각속도를 부드럽게 줄여 짧은 회전·마무리를 매끄럽게 한다.
    // ease-in은 두지 않는다(시작은 전속력) → 방향전환 반응성/선회감 제거 유지, 끝만 부드럽게 안착.
    private const float FacingSlewEaseOutAngle = 40f;
    private const float FacingSlewEaseFloor = 0.18f; // 목표 직전 정체 방지용 최저 속도비

    /// <summary>즉시(1회) 회전 지정. 스킬/회피/조준 등 한 프레임 스냅 또는 자체 보간 writer용. 진행 중인 이동 회전 슬루를 취소한다.
    /// 실제 적용은 FixedUpdate(ApplyFacing)에서 Rigidbody.MoveRotation으로 수행.</summary>
    public void RequestFacing(Quaternion rot) { _targetFacing = rot; _facingDirty = true; }

    /// <summary>이동 회전 목표를 지정한다. 목표 Yaw로 degPerSec 각속도로 FixedUpdate(ApplyFacing)에서 적분 → 프레임률 독립.</summary>
    public void RequestFacingSlew(float targetYaw, float degPerSec)
    {
        _slewTargetYaw = targetYaw;
        _slewDegPerSec = degPerSec;
        _facingSlewActive = true;
    }

    /// <summary>이동 회전 슬루를 정지한다(정지/행동 양보 시). 현재 facing을 그대로 유지.</summary>
    public void StopFacingSlew() => _facingSlewActive = false;

    // 스텝 오르기 — 상승 중에는 잠깐 공중 판정이 떠도(groundCheckDistance < 스텝높이) 낙하/공중 상태로
    // 전이하지 않도록 억제한다. StepClimb가 상승하는 프레임마다 MarkStepClimbing() 갱신.
    private float _stepClimbUntil;
    /// <summary>스텝 오르는 중인지(공중/낙하 상태 억제용).</summary>
    public bool IsStepClimbing => Time.time < _stepClimbUntil;
    /// <summary>StepClimb 상승 프레임에서 호출 — 짧은 유효시간 동안 IsStepClimbing 유지.</summary>
    public void MarkStepClimbing() => _stepClimbUntil = Time.time + 0.08f;

    //============================================================
    // Unity Lifecycle / Initialization
    //============================================================
    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        Clock = new UnscaledClock();
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.4f, dedupeSec: 0.04f);
        Combo   = new ComboController();
        Poise   = new PoiseController();
        Stamina = new StaminaController();

        InitCoreComponents();
        await InitCharacterDataAsync();

        // 비동기 로드 중 오브젝트가 파괴된 경우 중단한다.
        // 여기서 멈추지 않으면 InitInputActions()가 파괴된 객체 위에 PlayerInputActions를 생성·Enable하지만
        // OnDestroy는 이미 inputActions==null 상태로 지나가, 해당 인스턴스가 Dispose되지 못해 누수 경고가 발생한다.
        if (destroyCancellationToken.IsCancellationRequested) return;

        InitInputActions();
        InitAbilities();
        InitWeaponManager();
        SetupCamera();

        locoSM = new LayerStateMachine<LocoState>(this);
        actSM = new LayerStateMachine<ActState>(this);
        InitLayerFSMs();
        locoSM.Change(LocoState.Idle);
        actSM.Change(ActState.None);

        // 애니메이터 오버라이드 서비스 초기화
        _animSvc = new AnimatorOverrideService(anim);

        // 캐릭터 데이터에 지정된 애니메이션 클립으로 오버라이드 (Q스킬 등)
        ApplyCharacterAnimationOverrides();

        // 이펙트 핸들러 초기화
        EffectHandler = new WeaponEffectHandler(this);

        EventReceiver =
            GetComponent<PlayerAnimationEventReceiver>() ??
            GetComponentInChildren<PlayerAnimationEventReceiver>() ??
            gameObject.AddComponent<PlayerAnimationEventReceiver>();

        EventReceiver.SetTarget(this);
        SubscribeToAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged += OnWeaponChangedApplyAnimation;
            WeaponManager.OnWeaponChanged += OnWeaponChangedApplyStats;
            WeaponManager.OnEquippedWeaponRefreshed += OnEquippedWeaponRefreshedApplyStats;
        }

        AutoSetIdleIfNoAction();
        InitPassives();
        ApplyRelic();

        // 회피 연출(잔상/틴트/먼지/트레일) 런타임 자동 부착 — 프리팹/씬 수동 배선 불가 환경 대응.
        // 중복 부착 금지. 시각 자원(CharacterData optional 필드) 미할당 시 컴포넌트는 무동작.
        if (!TryGetComponent<DodgePresentation>(out _))
            gameObject.AddComponent<DodgePresentation>();

        // 스태미너 바(원신·명조식) — 동일한 런타임 자동 부착. HUD 프리팹을 건드리지 않는다.
        if (!TryGetComponent<StaminaBarView>(out _))
            gameObject.AddComponent<StaminaBarView>();

        // 전투 중 카메라 자동 줌아웃 — 낮은 몰입 구도와 다수 적 가독성을 둘 다 가져간다.
        if (!TryGetComponent<CombatCameraFraming>(out _))
            gameObject.AddComponent<CombatCameraFraming>();

        // 카메라 리그 기준점을 플레이어보다 앞에 — 구도는 그대로, 위치만 앞으로.
        if (!TryGetComponent<CameraRigAnchor>(out _))
            gameObject.AddComponent<CameraRigAnchor>();

        // 검 공격/대시 칼날 트레일(INab Weapon Trail) 구동기 — 동일한 런타임 자동 부착 패턴.
        // 트레일 프리팹 미할당(무기 SO / CharacterData) 시 무동작.
        if (!TryGetComponent<PlayerWeaponTrailVfx>(out _))
            gameObject.AddComponent<PlayerWeaponTrailVfx>();

        // 자동추적 대상 화살표(현재 에임어시스트 타겟을 머리 위 화살표로 실시간 표시) — 동일 자동 부착 패턴.
        if (!TryGetComponent<AimTargetIndicator>(out _))
            gameObject.AddComponent<AimTargetIndicator>();

        if (inputReady) BindInputActions();
    }

    protected override void Update()
    {
        if (!inputReady || characterData == null || cinemachineCamera == null) return;


        _runeEffects?.Tick(Time.deltaTime);
        GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Tick(Time.deltaTime);
        GameRunBootstrapper.Instance?.Run?.EffectManager?.OnTick(Time.deltaTime);   // 아이템 타임드/동적 효과 구동

        _knockbackTimer = Mathf.Max(0f, _knockbackTimer - Time.deltaTime);
        if (_slowTimer > 0f)
        {
            _slowTimer = Mathf.Max(0f, _slowTimer - Time.unscaledDeltaTime);
            if (_slowTimer <= 0f)
                SetMoveScale(1f);
        }
        if (_iceStage == 1)
        {
            _iceStageTimer -= Time.deltaTime;
            if (_iceStageTimer <= 0f)
                _iceStage = 0;
        }
        if (_freezeTimer > 0f)
        {
            _freezeTimer = Mathf.Max(0f, _freezeTimer - Time.unscaledDeltaTime);
            moveDirection = Vector3.zero;
            if (_freezeTimer <= 0f) StopFreezeLoopSfx();
            return;
        }
        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);
        InputBuffer?.TickPrune();
        CheckMovementInput();
        // Combo.Tick을 FSM Update보다 먼저 실행해 actSM이 최신 창 상태를 즉시 반영하도록 한다
        Combo.Tick(Time.unscaledDeltaTime);

        // 포이즈 회복/넉백 면역 타이머
        if (CharacterData != null && RuntimeStats != null)
        {
            Poise.Tick(Time.deltaTime, RuntimeStats.MaxPoise,
                       CharacterData.poiseRegenDelay, CharacterData.poiseRegenPerSec);

            // 스태미너 회복 — 지연 경과 후 초당 회복(원신·명조 방식)
            Stamina.Tick(Time.deltaTime, RuntimeStats.MaxStamina, CharacterData.staminaRegenPerSec);
        }

        // 저스트 회피 슬로모 만료 (unscaled — 느려진 시간에 영향받지 않아야 한다)
        TickPerfectDodge();
        RouteInputsToLayers();

        locoSM?.Update();
        actSM?.Update();

        if (locoSM != null) locoStateDebug = locoSM.CurrentId;
        if (actSM != null) actStateDebug = actSM.CurrentId;

        CooldownTracker.Tick(Time.unscaledDeltaTime);

        // ITickablePassive 틱 (시간 기반 스택 만료 등)
        foreach (var p in _passives)
            if (p is ITickablePassive tickable)
                tickable.Tick(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (characterData == null) return;

        JumpAbility?.UpdateGroundCheck(this);
        JumpAbility?.ApplyGravity(this);
        FreezeRotation();
        ApplyFacing();

        // 계단 오르기: FixedUpdate에서 실행. 상승 중엔 잠깐 공중 판정이 떠도(groundCheckDistance < 스텝높이)
        // 계속 호출해야 상승이 끊겨 떨어지는 진동을 막는다 → IsGrounded 또는 IsStepClimbing이면 호출.
        if ((IsGrounded() || IsStepClimbing) && moveDirection.sqrMagnitude > 0.01f)
            MoveAbility?.StepClimb(this, moveDirection.normalized);
    }

    /// <summary>
    /// 회전 목표(_targetFacing)를 Rigidbody에 적용. FixedUpdate에서만 호출한다.
    /// Rigidbody.rotation(텔레포트)으로 설정 — 회전축 freeze 제약을 우회하면서
    /// 물리 스텝에 동기화돼 Interpolate 보간과 충돌(진동)하지 않는다.
    /// </summary>
    private void ApplyFacing()
    {
        if (Rigid == null) return;

        // 즉시 회전 요청이 우선 — 적용 후 이동 슬루를 취소(직접 지정이 이동 회전을 덮어쓴다).
        if (_facingDirty)
        {
            // MoveRotation: Interpolate 보간과 정합되는 회전 적용(텔레포트 대입은 보간과 어긋나 진동).
            // Y축 회전 freeze가 풀려 있어야 적용된다(X/Z는 freeze 유지로 넘어짐 방지).
            Rigid.MoveRotation(_targetFacing);
            _facingDirty = false;
            _facingSlewActive = false;
            return;
        }

        // 이동 회전 슬루 — 물리 스텝마다 fixedDeltaTime으로 적분(프레임률 독립, Rigidbody.rotation stale-read 없음).
        if (_facingSlewActive)
        {
            float cur = Rigid.rotation.eulerAngles.y;
            // 목표 근처에서만 각속도를 ease-out(시작은 전속력 유지). 짧은 회전은 통째로 부드럽고, 큰 회전은 마지막만 매끄럽게 안착.
            float remaining = Mathf.Abs(Mathf.DeltaAngle(cur, _slewTargetYaw));
            float ease = Mathf.Max(FacingSlewEaseFloor, Mathf.SmoothStep(0f, 1f, remaining / FacingSlewEaseOutAngle));
            float next = Mathf.MoveTowardsAngle(cur, _slewTargetYaw, _slewDegPerSec * ease * Time.fixedDeltaTime);
            Rigid.MoveRotation(Quaternion.Euler(0f, next, 0f));
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromAnimationReceiver(EventReceiver);
        StopFreezeLoopSfx();

        // 저스트 회피 슬로모가 걸린 채 비활성화되면 시간이 느린 상태로 남는다 → 반드시 해제.
        ClearPerfectDodge();
    }

    protected virtual void OnDestroy()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            inputActions.Disable();
            inputActions.Dispose();
        }

        UnsubscribeFromAnimationReceiver(EventReceiver);

        if (WeaponManager != null)
        {
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyAnimation;
            WeaponManager.OnWeaponChanged -= OnWeaponChangedApplyStats;
            WeaponManager.OnEquippedWeaponRefreshed -= OnEquippedWeaponRefreshedApplyStats;
        }

        RelicBehavior?.OnDetach(this);
        ReleaseRelicAura();

        _runeEffects?.Detach();
    }

    //============================================================
    // Init Helpers
    //============================================================
    private void AutoSetIdleIfNoAction()
    {
        if (actSM != null && actSM.CurrentId == ActState.None && !Combo.IsAttacking)
        {
            if (locoSM.CurrentId != LocoState.Move &&
                locoSM.CurrentId != LocoState.Air &&
                locoSM.CurrentId != LocoState.Dodge &&
                locoSM.CurrentId != LocoState.Launched)   // 날아감은 자체적으로 착지까지 유지
            {
                locoSM.Change(LocoState.Idle);
            }
        }
    }

    private void InitCoreComponents()
    {
        Managers.Player.SetPlayer(transform);
        handTransform = Util.FindDeepChild(transform, "WeaponMount")
                     ?? Util.FindDeepChild(transform, "WeaponSocket");
        handTransformLeft = Util.FindDeepChild(transform, "WeaponMountLeft")
                         ?? Util.FindDeepChild(transform, "Cup_L")
                         ?? Util.FindDeepChild(transform, "Weapon_l")
                         ?? Util.FindDeepChild(transform, "hand_l");
        if (handTransform == null) Debug.LogWarning("WeaponMount/WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    private void InitWeaponManager()
    {
        WeaponManager = GetComponent<PlayerWeaponManager>() ?? gameObject.AddComponent<PlayerWeaponManager>();
        WeaponManager.Initialize(this);
    }

    /// <summary>
    /// CharacterData 에 지정된 키들로 AnimatorOverrideController 클립을 교체한다.
    /// 키가 비어 있으면 기본 클립(컨트롤러에 바인딩된 원본) 그대로 사용.
    /// 캐릭터별 Q스킬 등 고유 모션을 적용하는 통로.
    /// </summary>
    private void ApplyCharacterAnimationOverrides()
    {
        if (_animSvc == null || characterData == null) return;

        TryOverrideClip("QSkill_01", characterData.QSkillClipKey);
    }

    private void TryOverrideClip(string stateName, string addressableKey)
    {
        if (string.IsNullOrEmpty(addressableKey)) return;

        var clip = Managers.AnimationResources?.GetClip(addressableKey);
        if (clip == null)
        {
            Debug.LogWarning($"[PlayerController] AnimationClip '{addressableKey}' 로드 실패 — '{stateName}' 기본 클립 유지");
            return;
        }

        if (!_animSvc.Override(stateName, clip))
            Debug.LogWarning($"[PlayerController] '{stateName}' state 가 컨트롤러에 없어 override 스킵");
    }

    private async UniTask InitCharacterDataAsync()
    {
        // CharacterData SO 확보 (서버 데이터 사용 여부와 무관하게 필요)
        var preloaded = Managers.CharacterData?.M_CharacterData;
        if (preloaded != null)
        {
            characterData = preloaded;
            characterData.Initialize();
        }
        else if (characterData != null)
        {
            // 프리팹에 베이스 CharacterData가 직접 지정된 경우(범용 바디 등):
            // 이름 기반 Addressables 로드 대신 지정된 SO를 그대로 사용한다.
            // (GameObject 이름/스폰 키가 바뀌어도 안전 — 이름 의존성 제거)
            characterData.Initialize();
        }
        else
        {
            // SO가 없으면 Addressables에서 로드 (키 형식: "MageData", "KnightData" 등)
            string characterName = gameObject.name.Replace("(Clone)", "");
            await LoadCharacterDataAsync(characterName + "Data");
        }

        // 무유물 기본 스탯은 SO(범용 바디)에서 초기화 — 유물 착용 시 ApplyRelic이 서버 char_id 행으로 교체.
        if (characterData != null)
        {
            RuntimeStats.InitializeFrom(characterData);
            Debug.Log($"[PlayerController] 무유물 기본 스탯(SO): {characterData.characterName}");
        }

        if (Rigid != null)
        {
            Rigid.useGravity = false;
            // 공격 lunge 등 MovePosition 호출 시 시각적 보간을 위해 Interpolate 강제
            if (Rigid.interpolation == RigidbodyInterpolation.None)
                Rigid.interpolation = RigidbodyInterpolation.Interpolate;
            if (characterData != null)
                Rigid.linearDamping = characterData.groundDrag;
        }
    }

    /// <summary>
    /// 지정 char_id의 서버 PlayerStatEntry로 RuntimeStats + CharacterData(수치)를 적용한다.
    /// 무유물 기본은 "knight", 유물 획득 시 유물 char_id("gawain"/"galahad")로 호출 → 행 전체 교체.
    /// 서버 데이터/매칭 행이 없으면 false(호출자가 폴백 처리).
    /// </summary>
    private bool TryApplyServerStats(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return false;

        var mgr = Managers.PlayerData;
        if (mgr == null || !mgr.IsInitialized)
            return false;

        var entry = mgr.GetPlayer(charId);
        if (entry == null)
            return false;

        var passives = mgr.GetPassives(entry.passive_id);

        var preloaded = Managers.CharacterData?.M_CharacterData;

        // SO 의 LayerMask/Sprite/Passive 참조는 유지하되 수치 컬럼은 CSV(서버) 로 덮어쓴다.
        // 원본 .asset 을 변경하지 않도록 Instantiate 로 런타임 클론을 만든 뒤 적용.
        var source = preloaded ?? characterData;

        // 포이즈/스태미너는 CSV에 컬럼이 없다 — SO 경로와 같은 값이 나오도록 같은 SO를 넘긴다.
        RuntimeStats.InitializeFromServer(entry, passives, source);

        if (source != null)
        {
            var clone = ScriptableObject.Instantiate(source);
            clone.name = source.name + " (Runtime)";
            ApplyServerOverridesTo(clone, entry);
            characterData = clone;
            characterData.Initialize();
        }

        Debug.Log($"[PlayerController] 서버 데이터 사용: {entry.char_id} (HP:{entry.max_health}, Melee:{entry.base_melee_attack}, MoveSpd:{entry.base_move_speed})");
        return true;
    }

    /// <summary>CSV(PlayerStatEntry) 값을 CharacterData 클론에 덮어씀. LayerMask/Sprite/SO 참조는 건드리지 않는다.</summary>
    private static void ApplyServerOverridesTo(CharacterData data, PlayerStatEntry e)
    {
        data.maxHealth                  = e.max_health;
        data.baseMeleeAttack            = e.base_melee_attack;
        data.baseRangedAttack           = e.base_ranged_attack;
        data.baseDefense                = e.base_defense;
        data.baseLuck                   = e.base_luck;
        data.baseMoveSpeed              = e.base_move_speed;
        data.baseRunSpeed               = e.base_run_speed;
        if (e.base_run_ramp > 0.01f) data.runRampDuration = e.base_run_ramp; // CSV 컬럼 없으면 에셋값 유지
        if (e.move_accel > 0.01f)          data.moveAccel               = e.move_accel;
        if (e.move_decel > 0.01f)          data.moveDecel               = e.move_decel;
        if (e.reverse_accel_mult > 0.01f)  data.reverseAccelMultiplier  = e.reverse_accel_mult;
        if (e.initial_boost > 0.0001f)     data.initialBoost            = e.initial_boost;

        data.comboDuration              = e.combo_duration;
        data.heavyAttackChargeThreshold = e.heavy_charge_threshold;
        data.heavyAttackReleaseTime     = e.heavy_release_time;
        data.dashSpeed                  = e.dash_speed;
        data.dashDuration               = e.dash_duration;
        data.dodgeCooldown              = e.dodge_cooldown;
        data.jumpForce                  = e.jump_force;
        data.gravity                    = e.gravity;
        data.fallMultiplier             = e.fall_multiplier;
        data.groundCheckDistance        = e.ground_check_distance;
        data.airControlMultiplier       = e.air_control_multiplier;
        data.groundDrag                 = e.ground_drag;
        data.airDrag                    = e.air_drag;
    }

    private async UniTask LoadCharacterDataAsync(string characterName)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            Debug.LogError("캐릭터 이름이 비어있습니다.");
            return;
        }

        try
        {
            // UniTask 기반 Addressables 로드
            CharacterData data = await Managers.AddressableManager.LoadAssetAsync<CharacterData>(characterName);

            if (data == null)
            {
                Debug.LogError($"캐릭터 데이터 '{characterName}' 로드 실패");
                return;
            }

            characterData = data;
            Managers.CharacterData.SetCharacterData(data);

            // HUD/전투용 실시간 스탯 초기화 (SO는 템플릿)
            characterData.Initialize();
            RuntimeStats.InitializeFrom(characterData);

            Debug.Log($"캐릭터 데이터 '{characterName}' 로드 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"캐릭터 데이터 로드 중 예외 발생: {e.Message}");
        }
    }

    private void InitInputActions()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            inputActions.Disable();
            inputActions.Dispose();
        }
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        // 초기화 이전에 컷신/UI가 걸어둔 차단을 존중한다(이게 없으면 컷신 중 조작이 되살아난다).
        if (_inputDisabledExternally || _inputBlockedByUI) inputActions.Player.Disable();
        inputReady = true;
    }

    protected virtual void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility = new DefaultJumpAbility(characterData);
    }

    //============================================================
    // Input Binding
    //============================================================
    protected virtual void BindInputActions()
    {
        if (!inputReady) return;

        inputActions.Player.Attack.started += ctx =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // 스킬 중에는 공격 입력 무시
            bool inSkill = actSM.CurrentId == ActState.QSkill
                        || actSM.CurrentId == ActState.ESkill
                        || actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;

            if (CanAttack())
                _attackPolicy?.OnStarted(this);
            else
                Debug.Log("[Input] Attack started ignored - no weapon");

            // 탑뷰 포함 모든 카메라 상태에서 마우스 월드 위치 계산
            Ray atkRay = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(atkRay, out var hit, 200f, LayerMask.GetMask("Ground")))
                _lastClickedPosition = hit.point;
            else
            {
                // 지면 레이어 미스 시 Y=player 높이 평면으로 폴백 (탑뷰 대응)
                var groundPlane = new Plane(Vector3.up, transform.position);
                if (groundPlane.Raycast(atkRay, out float atkDist))
                    _lastClickedPosition = atkRay.GetPoint(atkDist);
            }
        };

        inputActions.Player.Attack.canceled += _ =>
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            bool inSkill = actSM.CurrentId == ActState.QSkill
                        || actSM.CurrentId == ActState.ESkill
                        || actSM.CurrentId == ActState.RSkill;
            if (inSkill) return;
            _attackPolicy?.OnCanceled(this);
        };

        inputActions.Player.Run.started += _ => isRunChecked = true;
        inputActions.Player.Run.canceled += _ => isRunChecked = false;

        inputActions.Player.Dodge.performed += _ => InputBuffer.Push(Command.Dodge);
        inputActions.Player.QSkill.performed += _ => InputBuffer.Push(Command.QSkill);
        inputActions.Player.ESkill.performed += _ => InputBuffer.Push(Command.ESkill);
        inputActions.Player.RSkill.performed += _ => InputBuffer.Push(Command.RSkill);

        // [점프 폐기] 자유 점프 제거 — 스페이스 입력을 점프에 연결하지 않는다. 공중 상태(낙하·넉백)는 유지.
        // ProcessJump/JumpAbility.Jump는 이 구독이 유일한 진입점이라 도달 불가(사장) 상태가 된다.
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
        inputActions.Player.PuzzleToggle.performed += _ => TogglePuzzleGrid();

        // 포션(C) — New Input System 액션. 레거시 Input.GetKeyDown은 이 프로젝트(Both 모드에서
        // New Input System 활성)에서 안 잡혀 포션이 아예 눌리지 않았다. 시간정지 중엔 무시.
        inputActions.Player.Potion.performed += _ =>
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

    //============================================================
    // Hooks (Derived)
    //============================================================
    /// <summary>FSM 상태 등록. 기본은 공통 세트(RegisterDefaultFSMs). 파생에서 override 가능.</summary>
    protected virtual void InitLayerFSMs() => RegisterDefaultFSMs();
    /// <summary>입력 라우팅. 기본은 공통 라우팅(DefaultRouteInputsToLayers). 파생에서 override 가능.</summary>
    protected virtual void RouteInputsToLayers() => DefaultRouteInputsToLayers();

    /// <summary>
    /// 기본 FSM 등록 — 모든 캐릭터가 공유하는 Loco/Act 상태 세트.
    /// 캐릭터별 InitLayerFSMs()에서 호출.
    /// </summary>
    protected void RegisterDefaultFSMs()
    {
        locoSM.Register(LocoState.Idle,     new LocoIdleState());
        locoSM.Register(LocoState.Move,     new LocoMoveState());
        locoSM.Register(LocoState.Air,      new LocoAirState());
        locoSM.Register(LocoState.Dodge,    new LocoDodgeState());
        locoSM.Register(LocoState.Launched, new LocoLaunchedState());

        actSM.Register(ActState.None,        new ActNoneState());
        actSM.Register(ActState.AttackReady, new ActAttackReadyState());
        actSM.Register(ActState.Attack,      new ActAttackState());
        actSM.Register(ActState.Charge,      new ActAttackChargeState());
        actSM.Register(ActState.HeavyAttack, new ActHeavyAttackState());
        actSM.Register(ActState.QSkill,      new ActSkillState(SkillType.Q, WeaponActionType.QSkill));
        actSM.Register(ActState.ESkill,      new ActSkillState(SkillType.E, WeaponActionType.ESkill));
        actSM.Register(ActState.RSkill,      new ActSkillState(SkillType.R, WeaponActionType.RSkill));
        actSM.Register(ActState.Plunge,      new ActPlungeState());
        actSM.Register(ActState.Pickup,      new ActPickupState());

        locoSM.Change(IsGrounded() ? LocoState.Idle : LocoState.Air);
        actSM.Change(ActState.None);
    }

    /// <summary>
    /// 기본 입력 라우팅 — 무기 기반 스킬 전환 (캐릭터 공통).
    /// 캐릭터별 RouteInputsToLayers()에서 호출.
    /// </summary>
    protected void DefaultRouteInputsToLayers()
    {
        if (actSM.CurrentId == ActState.Pickup) return;

        bool isInSkill = actSM.CurrentId == ActState.QSkill ||
                         actSM.CurrentId == ActState.ESkill ||
                         actSM.CurrentId == ActState.RSkill;
        bool isDodging = locoSM.CurrentId == LocoState.Dodge;
        bool isInAct   = actSM.CurrentId != ActState.None || isDodging;

        // 빈 슬롯(예: 무형검은 skillE/skillQ 모두 없음)으로 전환하면 스킬 없는 상태에 들어가
        // 진행 중이던 공격 모션만 끊기고 아무것도 안 나간다 → HasSkillInSlot으로 입력 자체를 막는다.
        if (InputBuffer.TryConsume(Game.Inputs.Command.QSkill))
        {
            Debug.Log($"[Input] Q pressed: CanAttack={CanAttack()}, isInSkill={isInSkill}, actState={actSM.CurrentId}");
            if (CanAttack() && !isInSkill && CanUseSkillNow(SkillType.Q)) actSM.Change(ActState.QSkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.ESkill))
        {
            if (CanAttack() && !isInSkill && CanUseSkillNow(SkillType.E)) actSM.Change(ActState.ESkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.RSkill))
        {
            if (CanAttack() && !isInSkill && CanUseSkillNow(SkillType.R)) actSM.Change(ActState.RSkill);
            return;
        }

        // 스킬 중에는 공격 관련 입력 소비하고 무시
        if (isInSkill)
        {
            InputBuffer.TryConsume(Game.Inputs.Command.Light);
            InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
            InputBuffer.TryConsume(Game.Inputs.Command.Charge);
        }

        if (InputBuffer.TryConsume(Game.Inputs.Command.Dodge))
        {
            if (isInSkill) return;  // 스킬 중에는 회피로 캔슬 불가
            if (IsLaunched) return; // 날아가는 중엔 회피로 탈출 불가

            // 대시 게이트 3중:
            //  ① !isDodging      — 대시 도중엔 재대시 불가
            //  ② DodgeCooldownEnd — 대시가 끝난 뒤 짧은 텀(dodgeCooldown) 동안 불가 (즉시 연타 방지)
            //  ③ 스태미너         — 자원이 있어야 발동 (소모는 여기서 확정)
            if (!isDodging
                && UnityEngine.Time.time >= DodgeCooldownEnd
                && TryConsumeDodgeStamina())
            {
                if (isInAct) actSM.Change(ActState.None);
                locoSM.Change(LocoState.Dodge);
            }
            return;
        }

        if (isInAct) return;

        if (InputBuffer.TryConsume(Game.Inputs.Command.Charge))
        {
            if (CanAttack()) actSM.Change(ActState.Charge);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.Heavy))
        {
            if (CanAttack()) { SetPendingAttack(Game.Inputs.Command.Heavy); actSM.Change(ActState.AttackReady); }
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.Light))
        {
            if (CanAttack()) { SetPendingAttack(Game.Inputs.Command.Light); actSM.Change(ActState.AttackReady); }
            return;
        }
    }

    /// <summary>
    /// 카메라 기준 이동 방향 계산 → moveDirection 갱신.
    /// 공중 상태에서는 지상 방향 갱신을 생략한다.
    /// 다른 이동 방식이 필요한 캐릭터는 override.
    /// </summary>
    protected virtual void CheckMovementInput()
    {
        if (locoSM?.CurrentId == LocoState.Air) return;
        if (inputActions == null) return;

        var input = inputActions.Player.Move.ReadValue<Vector2>();

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

        moveDirection = (Quaternion.Euler(0f, _moveBasisYaw, 0f) * new Vector3(input.x, 0f, input.y)).normalized;
    }

    /// <summary>
    /// 공격·스킬 상태 여부 판별 (Safe_OnAttackAnimationEnd 내부 사용).
    /// Knight처럼 추가 상태가 있는 캐릭터는 override해서 포함시킨다.
    /// </summary>
    protected virtual bool IsInAttackOrSkillState() =>
        actSM.CurrentId == ActState.Attack      ||
        actSM.CurrentId == ActState.AttackReady ||
        actSM.CurrentId == ActState.Charge      ||
        actSM.CurrentId == ActState.HeavyAttack ||
        actSM.CurrentId == ActState.QSkill      ||
        actSM.CurrentId == ActState.ESkill      ||
        actSM.CurrentId == ActState.RSkill;

    //============================================================
    // Weapon Changed → Animation / Policy
    //============================================================
    private void OnWeaponChangedApplyAnimation(WeaponData newWeapon, GameObject weaponInstance)
    {
        // 이전 무기 오버라이드 원복 (로코모션 포함) — 무기 교체/해제 시 이전 클립이 남지 않도록.
        _animSvc?.ResetOverrides();

        // null 무기면 정책 제거
        if (newWeapon == null)
        {
            _attackPolicy = null;
            Debug.Log("[PlayerController] 무기 해제 - 공격 불가 상태로 전환");
            return;
        }

        if (_animSvc == null || newWeapon.animationSet == null || !Managers.AnimationResources.IsInitialized)
        {
            AssignAttackPolicyForWeapon(newWeapon);
            return;
        }

        var animSet = newWeapon.animationSet;
        foreach (var mapping in animSet.GetAllMappings())
        {
            var clip = Managers.AnimationResources.GetClip(mapping.addressableKey);
            if (clip == null) continue; // 어드레서블 미등록 — AnimationResourceManager 가 이미 경고했다.

            // Override 키는 컨트롤러 상태가 물고 있는 '원본 클립 이름'이다. baseClipName(=상태 이름)과
            // 다르거나 그런 상태가 없으면 조용히 실패해 무기 클립이 영영 적용되지 않는다.
            // 지금까지 이 실패가 묻혀 있었으므로 반드시 드러낸다.
            if (!_animSvc.Override(mapping.baseClipName, clip))
                Debug.LogWarning($"[PlayerController] 애니 오버라이드 실패 — 무기 '{newWeapon.weaponSOKey}' " +
                                 $"키 '{mapping.baseClipName}' (addressable '{mapping.addressableKey}'). " +
                                 $"컨트롤러에 그 이름의 원본 클립이 없다.");
        }

        AssignAttackPolicyForWeapon(newWeapon);
    }

    private void OnWeaponChangedApplyStats(WeaponData newWeapon, GameObject _)
    {
        if (newWeapon == null)
        {
            RuntimeStats.SetWeaponStats(0, 0, 0);
            RuntimeStats.SetWeaponMastery(0f, 0f);
            return;
        }

        var kind = newWeapon.weaponType.GetAttackStatKind();
        // 절삭하지 않고 소수 그대로 넘긴다 — 강화 배율이 여기서 잘리면 강화가 공격력에 안 닿는다.
        float melee  = kind == AttackStatKind.Melee  ? newWeapon.baseAttack : 0f;
        float ranged = kind == AttackStatKind.Ranged ? newWeapon.baseAttack : 0f;
        RuntimeStats.SetWeaponStats(melee, ranged, newWeapon.baseDefense);

        // 진화 후 추가 강화 구간(마스터리) → 스킬 확장. 테이블 미로드면 0(무보정).
        var table   = WeaponEnhanceService.Table;
        int mastery = WeaponEnhanceService.MasteryLevel(newWeapon, table);
        RuntimeStats.SetWeaponMastery(
            table != null ? table.MasterySkillDamage(mastery) : 0f,
            table != null ? table.MasterySkillCdr(mastery)    : 0f);
    }

    /// <summary>강화/승급으로 장착 무기 스탯만 갱신됐을 때 — 교체 없이 데미지 스탯만 재적용.</summary>
    private void OnEquippedWeaponRefreshedApplyStats(WeaponData weapon)
        => OnWeaponChangedApplyStats(weapon, null);

    private void AssignAttackPolicyForWeapon(WeaponData wd)
    {
        if (wd == null)
        {
            _attackPolicy = null;
            return;
        }

        switch (wd.weaponType)
        {
            case WeaponType.Katana:
                _attackPolicy = new SwordAttackPolicy(
                    enterThreshold: 0.4f,
                    fullThreshold: wd.holdThreshold,
                    maxChargeStage: wd.chargeStages
                );
                break;

            case WeaponType.Greatsword:
                _attackPolicy = new SwordAttackPolicy(
                    enterThreshold: 0.5f,
                    fullThreshold: wd.holdThreshold,
                    maxChargeStage: wd.chargeStages
                );
                break;

            case WeaponType.Bow:
            case WeaponType.Crossbow:
                _attackPolicy = new BowAttackPolicy();
                break;

            default:
                _attackPolicy = new SwordAttackPolicy();
                break;
        }
    }

    public void OnAttackHitStep(int stepIndex)
    {
        FirePassive(PassiveTrigger.OnAttackHit,
            new PassiveContext { comboStep = stepIndex });
    }
    public void OnAnimationEventTag(string tag) { /* 구현 */ }

    //============================================================
    // Jump (모듈에 위임)
    //============================================================

    /// <summary>공중 공격 1사이클 사용 여부. 착지 시 리셋.</summary>
    public bool AirAttackUsed { get; set; } = false;

    public void ProcessJump()
    {
        if (IsFrozen) return;
        if (!IsGrounded()) return;

        JumpAbility?.Jump(this);

        // Jump가 쿨다운에 의해 무시됐으면 애니메이션도 스킵
        if (!IsJumping) return;

        // 즉시 점프 애니메이션 시작 (AirState 전이를 기다리지 않음)
        Anim.SetFloat("JumpValue", 0f);
        Anim.CrossFadeInFixedTime("JumpBlend", 0.08f);
    }

    /// <summary>공중 공격 진입 시 호출 — 낙하 속도를 즉시 멈추고 체공 시작</summary>
    public void StartAirHover()
    {
        if (Rigid != null && !IsGrounded())
        {
            Rigid.linearVelocity = new Vector3(Rigid.linearVelocity.x, 0f, Rigid.linearVelocity.z);
        }
    }

    //============================================================
    // Inventory / Weapon / Camera
    //============================================================
    protected virtual void ChangeWeapon(int index)
    {
        if (WeaponManager != null)
            _ = WeaponManager.SwitchToSlotAsync(index);
    }

    protected virtual void SetupCamera()
    {
        if (cinemachineCamera == null)
            cinemachineCamera = FindFirstObjectByType<CinemachineFreeLook>();

        if (cinemachineCamera != null)
        {
            cinemachineCamera.Follow = transform;
            cinemachineCamera.LookAt = transform;
            ConfigureCameraView();
        }
    }

    protected virtual void ConfigureCameraView() { }

    public void StopHorizontalMovement()
    {
        Rigid.linearVelocity = new Vector3(0f, Rigid.linearVelocity.y, 0f);
    }

    /// <summary>낙하 공격 상태인지 여부 (LocoAirState 착지 처리 분기용)</summary>
    public bool IsPlunging => actSM?.CurrentId == ActState.Plunge;

    /// <summary>강공격 실행 중 여부 — SwordPolicy.OnCanceled에서 릴리즈 중복 처리 억제에 사용</summary>
    public bool IsInHeavyAttackState => actSM?.CurrentId == ActState.HeavyAttack;

    /// <summary>차지 불가 상태: 공격 중·공중·회피 중. SwordAttackPolicy 타이머 리셋 조건에 사용</summary>
    public bool IsChargeBlocked =>
        Combo.IsAttacking ||
        !IsGrounded() ||
        locoSM?.CurrentId == LocoState.Dodge ||
        IsLaunched;

    /// <summary>피격 넉백으로 날아가는 중(착지 회복 포함) — 이 동안 모든 조작 불가.</summary>
    public bool IsLaunched => locoSM?.CurrentId == LocoState.Launched;

    /// <summary>픽업 대기 중인 무기 데이터 (WorldWeaponDisplay → ActPickupState 전달용)</summary>
    public WeaponData PendingPickupWeapon { get; set; }

    /// <summary>픽업 소스 오브젝트 (팝업 결과 후 확정/취소 처리용)</summary>
    public WorldWeaponDisplay PendingPickupSource { get; set; }

    /// <summary>무기 픽업 요청 — ActPickupState로 전환</summary>
    public void RequestPickup(WeaponData data, WorldWeaponDisplay source = null)
    {
        if (data == null) return;
        if (actSM == null) return;

        PendingPickupWeapon = data;
        PendingPickupSource = source;
        actSM.Change(ActState.Pickup);
    }

    /// <summary>낙하 공격 진입 시 전달할 데이터 (공격 상태 → ActPlungeState)</summary>
    public struct PlungeInfo
    {
        public string fallClipName;
        public float  fallSpeed;
        public float  descendAt;   // 하강 시작 normalizedTime (0 = 즉시)
    }
    public PlungeInfo PendingPlunge { get; set; }

    /// <summary>
    /// actSM이 None이 아니면 강제로 None으로 전환 (착지·회피 캔슬 시 사용)
    /// </summary>
    public void CancelActState()
    {
        if (actSM != null && actSM.CurrentId != ActState.None)
            actSM.Change(ActState.None);
        Combo?.ResetStep();
    }

    /// <summary>
    /// 현재 이동 입력 방향으로 즉시 회전. 입력이 없으면 유지.
    /// </summary>
    public void RotateTowardsInput()
    {
        if (moveDirection.sqrMagnitude < 0.0001f) return;
        RequestFacing(Quaternion.LookRotation(moveDirection));
    }

    public void RotateTowardsMousePosition()
    {
        if (TryComputeMouseLookDir(out var lookDir))
            RequestFacing(Quaternion.LookRotation(lookDir));
    }

    /// <summary>
    /// 마우스 방향을 기준으로 회전하되, 지정 콘 안에 IDamageable 적이 있으면 그 쪽으로 살짝 보정한다.
    /// 공격 시작/콤보 단계 시작 시 1회만 호출 (매 프레임 호출 시 aimbot 느낌).
    /// </summary>
    /// <param name="radius">적 탐색 거리(m)</param>
    /// <param name="coneHalfAngleDeg">마우스 방향 콘 반각(도)</param>
    /// <param name="strength">마우스 → 적 방향 블렌드 비율 (0=마우스, 1=적)</param>
    public void RotateTowardsMouseWithAimAssist(float radius, float coneHalfAngleDeg, float strength)
    {
        Quaternion target = ComputeMouseAimAssistRotation(radius, coneHalfAngleDeg, strength);
        RequestFacing(target);
    }

    /// <summary>
    /// 마우스 + 에임 어시스트 적용 후의 최종 목표 회전을 "계산만" 해서 반환한다.
    /// 호출자에서 즉시 적용하거나 lerp 시작점으로 사용. 적용은 하지 않음.
    /// </summary>
    public Quaternion ComputeMouseAimAssistRotation(float radius, float coneHalfAngleDeg, float strength)
        => ComputeMouseAimAssistRotation(radius, coneHalfAngleDeg, strength, out _, out _);

    /// <summary>
    /// 위와 동일하되, 콘 안에서 선택된 적(IDamageable)과 그 수평 거리를 함께 반환한다.
    /// 런지(전진)가 좁은 SphereCast 대신 이 OverlapSphere 기반 타겟을 재사용해 인식 안정성을 높이기 위함.
    /// </summary>
    public Quaternion ComputeMouseAimAssistRotation(float radius, float coneHalfAngleDeg, float strength,
                                                    out Transform enemy, out float enemyPlanarDist)
    {
        enemy = null;
        enemyPlanarDist = 0f;

        if (!TryComputeMouseLookDir(out var mouseDir))
            return transform.rotation;

        // 보정 비활성 케이스: 마우스 방향 그대로
        if (radius <= 0f || coneHalfAngleDeg <= 0f || strength <= 0f)
            return Quaternion.LookRotation(mouseDir);

        // 콘 안 가장 작은 각도의 적 탐색
        Vector3 origin = transform.position;
        float cosThreshold = Mathf.Cos(coneHalfAngleDeg * Mathf.Deg2Rad);
        float bestDot = cosThreshold;
        Vector3 bestDir = mouseDir;
        Transform bestEnemy = null;
        float bestDist = 0f;
        bool found = false;

        var cols = Physics.OverlapSphere(origin, radius);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            var dt = (d as Component) != null ? (d as Component).transform : null;
            if (dt == null) continue;

            Vector3 toEnemy = dt.position - origin;
            toEnemy.y = 0f;
            float sqr = toEnemy.sqrMagnitude;
            if (sqr < 0.01f) continue;

            float dist = Mathf.Sqrt(sqr);
            Vector3 enemyDir = toEnemy / dist;
            float dot = Vector3.Dot(mouseDir, enemyDir);
            if (dot >= bestDot)
            {
                bestDot = dot;
                bestDir = enemyDir;
                bestEnemy = dt;
                bestDist = dist;
                found = true;
            }
        }

        if (found)
        {
            enemy = bestEnemy;
            enemyPlanarDist = bestDist;
        }

        Vector3 finalDir = found
            ? Vector3.Slerp(mouseDir, bestDir, Mathf.Clamp01(strength))
            : mouseDir;

        return finalDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(finalDir)
            : transform.rotation;
    }

    /// <summary>
    /// 마우스 / 마지막 클릭 위치에서 수평 방향 벡터를 계산. 실패 시 false.
    /// 1) _lastClickedPosition (입력 시 캐시된 클릭 월드 좌표)
    /// 2) Ground 레이어 콜라이더 레이캐스트
    /// 3) 플레이어 Y 높이의 수학적 수평 평면에 레이 투영 (콜라이더 미스 시 폴백)
    /// </summary>
    private bool TryComputeMouseLookDir(out Vector3 lookDir)
    {
        // 1) 입력으로 저장된 클릭 위치 우선 사용
        if (_lastClickedPosition.HasValue)
        {
            Vector3 target = _lastClickedPosition.Value;
            lookDir = target - transform.position;
            lookDir.y = 0f;
            _lastClickedPosition = null;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                lookDir.Normalize();
                return true;
            }
        }

        // 2/3) 마우스 → 카메라 레이 → Ground 콜라이더 우선, 미스 시 수평 평면 폴백
        if (Camera.main != null && Mouse.current != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

            // 2) Ground 콜라이더 히트
            if (Physics.Raycast(ray, out var hit, 100f, LayerMask.GetMask("Ground")))
            {
                lookDir = hit.point - transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    lookDir.Normalize();
                    return true;
                }
            }

            // 3) 플레이어 Y 높이의 수평 평면에 레이 투영 — Ground 콜라이더 누락/미스 대비
            var plane = new Plane(Vector3.up, transform.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 point = ray.GetPoint(enter);
                lookDir = point - transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    lookDir.Normalize();
                    return true;
                }
            }
        }

        lookDir = Vector3.zero;
        return false;
    }

    //============================================================
    // Animation Receiver Subscribe / Unsubscribe
    //============================================================
    private void SubscribeToAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || _aeSubscribed) return;

        // OnAttackEnd: ActAttackState는 normalizedTime 폴링으로 자체 처리 → 구독 안 함
        // OnOpenCombo / OnCloseCombo: ActAttackState가 폴링으로 처리 → 구독 안 함
        // 아래는 PlayerController가 직접 처리해야 하는 시각적/전역 이벤트만 유지
        receiver.OnAttackEnd  += Safe_OnAttackAnimationEnd; // 스킬 상태 종료용
        receiver.OnHitStep    += Safe_OnHitStep;
        receiver.OnGenericTag += Safe_GenericTag;
        receiver.OnEffectStep += safe_EffectStep;

        _aeSubscribed = true;
    }

    private void UnsubscribeFromAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || !_aeSubscribed) return;

        receiver.OnAttackEnd  -= Safe_OnAttackAnimationEnd;
        receiver.OnHitStep    -= Safe_OnHitStep;
        receiver.OnGenericTag -= Safe_GenericTag;
        receiver.OnEffectStep -= safe_EffectStep;

        _aeSubscribed = false;
    }

    //============================================================
    // Safe Handlers
    //============================================================

    /// <summary>
    /// AE_AttackEnd 애니메이션 이벤트 수신 — 스킬 상태 종료 전용.
    /// ActState.Attack 및 ActState.HeavyAttack은 각 상태가 자체 처리하므로 스킵한다.
    /// </summary>
    private void Safe_OnAttackAnimationEnd()
    {
        // Attack / HeavyAttack은 각 State가 자체적으로 종료를 처리한다
        if (actSM.CurrentId == ActState.Attack ||
            actSM.CurrentId == ActState.HeavyAttack)
            return;

        Combo.SetAttacking(false);

        if (IsInAttackOrSkillState())
            actSM.Change(ActState.None);
    }

    /// <summary>
    /// 콤보가 최대 스텝까지 완료됐을 때 ActAttackState에서 호출된다.
    /// 파생 캐릭터는 override해 캐릭터 전용 로직을 추가할 수 있다.
    /// </summary>
    
    public void NotifyComboFinished(int finalStep)
    {
        OnComboFinished(finalStep);
    }

    protected virtual void OnComboFinished(int finalStep)
    {
        FirePassive(PassiveTrigger.OnComboFinish,
            new PassiveContext { comboStep = finalStep });
    }

    private void Safe_OnHitStep(int stepIndex)
    {
        if (stepIndex < 0 || !Combo.IsAttacking) return;
        OnAttackHitStep(stepIndex);
    }

    private void Safe_GenericTag(string tag) => OnAnimationEventTag(tag);

    private void safe_EffectStep(int step)
    {
        if (EffectHandler != null && WeaponManager.HasWeapon)
            _ = EffectHandler.PlayEffect(CurrentAttackTypeForEffect, Combo.CurrentComboStep, step, ActiveExecution);
    }

    private void SpawnHitBloodVfx()
    {
        if (_hitBloodVfxPrefab == null) return;
        Vector3 pos = transform.position + Vector3.up * _hitBloodVfxHeightOffset;
        var go = Instantiate(_hitBloodVfxPrefab, pos, _hitBloodVfxPrefab.transform.rotation, transform);
        go.transform.localScale = Vector3.one * _hitBloodVfxScale;

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 3f;
        Destroy(go, lifetime);
    }

}
