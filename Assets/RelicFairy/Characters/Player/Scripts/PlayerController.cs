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
        return WeaponManager != null && WeaponManager.HasWeapon;
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

    // 스킬 버프: 기본공격 시 추가 발사 횟수 (0이면 비활성)
    public int ExtraShotCount { get; set; }

    // 사망 처리 1회 가드 (씬 전환 시 새 인스턴스라 리셋 불필요)
    private bool _dead;

    public virtual void TakeDamage(int dmg, GameObject attacker = null)
    {
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

        // [받피감소 통합 채널] 아이템/캐릭터/어둠룬 피해감소(%)를 한 곳에서 1회 적용.
        // (DamageReductionEffect.OnPreTakeDamage 제거 → 여기로 통합. DamageReduction은 Recalculate에서 Clamp01.)
        float dr = RuntimeStats.DamageReduction;
        if (dr > 0f)
            finalDmg = Mathf.Max(0, Mathf.RoundToInt(finalDmg * (1f - dr)));

        // 실드 흡수 — HP 차감 전. 실드가 먼저 피해를 받고, ShieldAccumulate면 피격 피해 일부를 실드로 축적.
        if (finalDmg > 0)
        {
            int incoming = finalDmg;
            finalDmg = RuntimeStats.AbsorbWithShield(finalDmg);
            RuntimeStats.AccumulateShieldFromDamage(incoming);
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

    /// <summary>
    /// 플레이어 입력 전체를 활성/비활성화한다.
    /// 보스 등장 연출 등 컷씬 구간에서 false로 호출해 행동을 막는다.
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        if (inputActions == null) return;
        if (enabled) inputActions.Player.Enable();
        else         inputActions.Player.Disable();
    }

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

    private float _freezeTimer;
    public bool IsFrozen => _freezeTimer > 0f;

    [Tooltip("빙결 상태(얼음 쉴드+스크린 이펙트) 동안 반복 재생할 사운드")]
    [SerializeField] private AudioClip _freezeLoopSfx;
    private AudioSource _freezeLoopAudioSource;

    /// <summary>빙결: duration초 동안 이동·행동·입력을 완전히 차단한다. 연속 피격 시 남은 시간을 연장.</summary>
    public void ApplyFreeze(float duration)
    {
        _freezeTimer = Mathf.Max(_freezeTimer, duration);
        if (_freezeLoopAudioSource == null)
            _freezeLoopAudioSource = Managers.Sound?.PlayLoopingEffectAt(_freezeLoopSfx, transform.position);
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
                // [가이드라인 비주얼] 유물/캐릭터 패시브 발동 토스트(통지만)
                GuidelineVisual.Toast(transform.position + Vector3.up * 2.4f, p.PassiveName, GuidelineVisual.ToastKind.Relic);
            }
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

    //============================================================
    // Unity Lifecycle / Initialization
    //============================================================
    protected override async UniTask InitAsync()
    {
        await base.InitAsync();

        Clock = new UnscaledClock();
        InputBuffer = new InputBuffer(Clock, capacity: 16, bufferWindowSec: 0.4f, dedupeSec: 0.04f);
        Combo = new ComboController();

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
        }

        AutoSetIdleIfNoAction();
        InitPassives();
        ApplyRelic();

        // 회피 연출(잔상/틴트/먼지/트레일) 런타임 자동 부착 — 프리팹/씬 수동 배선 불가 환경 대응.
        // 중복 부착 금지. 시각 자원(CharacterData optional 필드) 미할당 시 컴포넌트는 무동작.
        if (!TryGetComponent<DodgePresentation>(out _))
            gameObject.AddComponent<DodgePresentation>();

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
            _slowTimer = Mathf.Max(0f, _slowTimer - Time.deltaTime);
            if (_slowTimer <= 0f)
                SetMoveScale(1f);
        }
        if (_freezeTimer > 0f)
        {
            _freezeTimer = Mathf.Max(0f, _freezeTimer - Time.deltaTime);
            moveDirection = Vector3.zero;
            if (_freezeTimer <= 0f) StopFreezeLoopSfx();
            return;
        }
        _attackPolicy?.Tick(this, Time.unscaledDeltaTime);
        InputBuffer?.TickPrune();
        CheckMovementInput();
        // Combo.Tick을 FSM Update보다 먼저 실행해 actSM이 최신 창 상태를 즉시 반영하도록 한다
        Combo.Tick(Time.unscaledDeltaTime);
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

        // 계단 오르기: FixedUpdate에서 실행해야 물리 충돌 전 위치 보정이 적용됨
        if (IsGrounded() && moveDirection.sqrMagnitude > 0.01f)
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
                locoSM.CurrentId != LocoState.Dodge)
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
        RuntimeStats.InitializeFromServer(entry, passives);

        var preloaded = Managers.CharacterData?.M_CharacterData;

        // SO 의 LayerMask/Sprite/Passive 참조는 유지하되 수치 컬럼은 CSV(서버) 로 덮어쓴다.
        // 원본 .asset 을 변경하지 않도록 Instantiate 로 런타임 클론을 만든 뒤 적용.
        var source = preloaded ?? characterData;
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

        inputActions.Player.Jump.performed += _ => ProcessJump();
        inputActions.Player.ChangeWeapon1.performed += _ => ChangeWeapon(0);
        inputActions.Player.ChangeWeapon2.performed += _ => ChangeWeapon(1);
        inputActions.Player.PuzzleToggle.performed += _ => TogglePuzzleGrid();
    }

    private void TogglePuzzleGrid()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null || !run.IsRunning) return;

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
        locoSM.Register(LocoState.Idle,  new LocoIdleState());
        locoSM.Register(LocoState.Move,  new LocoMoveState());
        locoSM.Register(LocoState.Air,   new LocoAirState());
        locoSM.Register(LocoState.Dodge, new LocoDodgeState());

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

        if (InputBuffer.TryConsume(Game.Inputs.Command.QSkill))
        {
            Debug.Log($"[Input] Q pressed: CanAttack={CanAttack()}, isInSkill={isInSkill}, actState={actSM.CurrentId}");
            if (CanAttack() && !isInSkill) actSM.Change(ActState.QSkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.ESkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.ESkill);
            return;
        }
        if (InputBuffer.TryConsume(Game.Inputs.Command.RSkill))
        {
            if (CanAttack() && !isInSkill) actSM.Change(ActState.RSkill);
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
            if (isInSkill) return; // 스킬 중에는 회피로 캔슬 불가
            if (!isDodging && UnityEngine.Time.time >= DodgeCooldownEnd)
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

        // 고정 탑다운(월드 정렬) 카메라 — 이동 기준은 월드축 고정.
        // 시작 연출(오버헤드/투어)로 카메라가 움직이거나 거의 수직이 돼도 조작이 어긋나지 않도록
        // 라이브 카메라 transform에 의존하지 않는다.
        moveDirection = (Vector3.forward * input.y + Vector3.right * input.x).normalized;
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
            if (clip != null) _animSvc.Override(mapping.baseClipName, clip);
        }

        AssignAttackPolicyForWeapon(newWeapon);
    }

    private void OnWeaponChangedApplyStats(WeaponData newWeapon, GameObject _)
    {
        if (newWeapon == null)
        {
            RuntimeStats.SetWeaponStats(0, 0, 0);
            return;
        }

        var kind = newWeapon.weaponType.GetAttackStatKind();
        int melee  = kind == AttackStatKind.Melee  ? (int)newWeapon.baseAttack : 0;
        int ranged = kind == AttackStatKind.Ranged ? (int)newWeapon.baseAttack : 0;
        RuntimeStats.SetWeaponStats(melee, ranged, (int)newWeapon.baseDefense);
    }

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
        Anim.CrossFade("JumpBlend", 0.05f);
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
        locoSM?.CurrentId == LocoState.Dodge;

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
    {
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
        bool found = false;

        var cols = Physics.OverlapSphere(origin, radius);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            Vector3 toEnemy = ((d as Component).transform.position) - origin;
            toEnemy.y = 0f;
            float sqr = toEnemy.sqrMagnitude;
            if (sqr < 0.01f) continue;

            Vector3 enemyDir = toEnemy / Mathf.Sqrt(sqr);
            float dot = Vector3.Dot(mouseDir, enemyDir);
            if (dot >= bestDot)
            {
                bestDot = dot;
                bestDir = enemyDir;
                found = true;
            }
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
