//============================================================
// HudPresenter.cs
// - Mode/Section 제어 + CanvasGroup Fade
// - PlayerRunState 이벤트 구독 (HP, Gold)
// - PlayerRuntimeStats 이벤트 구독 (AttackPower)
// - PlayerWeaponManager 이벤트 구독 (장비 변화)
//============================================================
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;

    [Header("Visibility (CanvasGroup)")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("HUD Mode")]
    [SerializeField] private HUDIds.Mode startMode = HUDIds.Mode.None;

    private PlayerRunState _state;
    private RunFuelBank _fuelBank;
    private UIHudDataProvider _provider;

    private PlayerRuntimeStats _runtimeStats;
    private PlayerWeaponManager _weaponManager;
    private SkillCooldownTracker _cooldownTracker;
    private RoomBuffHandler _buffHandler;
    private readonly BuffViewAggregator _buffAggregator = new();
    private PlayerBuffViewSource _playerBuffSource;
    // 스킬 슬롯 잠금 판정(HasSkillInSlot)용 — 무기 교체/진화 시 재평가한다.
    private PlayerController     _player;
    private WeaponFormRuntime    _formRuntime;   // 진화 무기 「형」 — HUD 형 게이지(10-08)
    private WeaponFormBase       _form;
    private bool                 _formReadyShown;   // 형 게이지 「다음 타에 발동」 테가 켜져 있나(10-12 A7)
    private ItemBuffViewSource _itemBuffSource;
    private NightmareRuleBuffViewSource _nightmareRuleSource;
    private bool _hasDynamicBuffSources;          // 룬/유물 등 폴링 기반 소스 등록 여부
    private float _buffPollAccum;
    private const float BuffPollInterval = 0.25f; // 룬 배지 폴링과 동일 cadence
    private readonly List<BuffViewItem> _lastBuffItems = new();  // 더티 체크 캐시(재사용)
    private CovenantHandler _covenantHandler;
    private CovenantBuffViewSource _covenantBuffSource;

    private UserInfo _userInfo;

    private MonsterBase _boss;
    private IBossHudSource _bossHud;   // 보스 HP바 부가 정보(페이지 눈금 · 무적 · 무방비) — 구현한 보스만
    public MonsterBase BoundBoss => _boss;
    public bool HasBoundBoss => _boss != null;
    public CanvasGroup MainCanvasGroup => canvasGroup;
    /// <summary>HUD 숨김(시작방 · 프롤로그 컷신) 중 — 켜기 요청이 루트를 다시 켜지 못하게 한다(HudBootstrapper가 건다).
    /// canvasGroup이 비면 SetVisible이 SetActive로 켜는데, 보스가 나타나며 부른 SetMode(Boss)가 숨김을 뚫고 HUD를 띄웠다(10-09 전주기 시뮬 · 프롤로그 보스 등장).</summary>
    public bool ActivationHeld { get; set; }
    public MinimapView  MinimapView => view != null ? view.MinimapView : null;

    private bool _bossPanelSuppressed;

    private int _fadeToken = 0;
    private HUDIds.Mode _currentMode = HUDIds.Mode.None;

    // ── 빌드 완성 신호 · 유물 진화(10-14 빌드 시너지 · 83 UI 요청 2 · 3) ──
    private const int MaxPendingBuild = 4;
    private readonly List<BuildNotice> _pendingBuild = new();   // 다음 프레임(또는 HUD가 다시 보일 때) 보인다 — 같은 프레임 신호는 진화 → 각인 → 존 순
    private PlayerLoadout _loadout;
    private string _evolutionShown;      // Q 칸에 그려 둔 진화
    private string _pendingMomentId;     // 보여 줄 진화 순간(차단 팝업 · 룬판이 걷히면)
    /// <summary>이번 런에 알린 진화 — 챕터 · 이어하기마다 새 플레이어가 진화 효과를 다시 묶어도(RelicEvolutionNotice) 한 줄만.</summary>
    private static string s_evolutionAnnounced;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_evolutionAnnounced = null;

    private void Awake()
    {
        if (view == null)
            view = GetComponentInChildren<HudView>(true);

        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);
    }

    private void Start()
    {
        if (_currentMode == HUDIds.Mode.None)
            SetMode(startMode);
    }

    private void Update()
    {
        TickFormReadyNext();
        TickBuildNotices();

        // 동적 지속 버프(룬/유물)는 이벤트가 아니라 상태가 매 프레임 변하므로 저빈도 폴링으로 반영.
        // 방버프는 OnBuffsChanged 이벤트로 즉시 갱신되므로 동적 소스가 없으면 폴링 불필요.
        if (!_hasDynamicBuffSources) return;
        _buffPollAccum += Time.unscaledDeltaTime;
        if (_buffPollAccum < BuffPollInterval) return;
        _buffPollAccum = 0f;
        RefreshBuffWindow();
    }

    public void Construct(GameRunSession run, UIHudDataProvider provider)
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state.OnPotionChanged -= HandlePotionChanged;
            _state = null;
        }
        if (_fuelBank != null)
        {
            _fuelBank.OnFuelChanged -= HandleFuelChanged;
            _fuelBank = null;
        }
        if (_run != null)
        {
            _run.OnEssenceChanged -= HandleEssenceChanged;
            _run.OnReviveChanged  -= HandleReviveChanged;
            _run = null;
        }

        _provider = provider;
        _state = run?.PlayerState;
        _fuelBank = run?.FuelBank;
        _run = run;

        // 버프 핸들러 구독
        UnbindBuffHandler();
        if (run?.BuffHandler != null)
        {
            _buffHandler = run.BuffHandler;
            _buffAggregator.SetRoomBuffSource(_buffHandler);
            _buffHandler.OnBuffsChanged += HandleBuffsChanged;
        }

        // 아이템 동적 지속 버프(조건부 Cond*) 소스 등록 — 상태가 매 프레임 변하므로 폴링 갱신.
        if (run?.EffectManager != null)
        {
            _itemBuffSource = new ItemBuffViewSource(run.EffectManager);
            _buffAggregator.AddSource(_itemBuffSource);
            _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
        }

        // 악몽 규칙 — 켜진 규칙만 기여하므로 봉인기엔 아무것도 안 띄운다.
        if (run != null)
        {
            _nightmareRuleSource = new NightmareRuleBuffViewSource();
            _buffAggregator.AddSource(_nightmareRuleSource);
            _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
        }

        if (view == null)
        {
            Debug.LogError("[HudPresenter] view is null.");
            return;
        }

        if (_state == null)
        {
            Debug.LogWarning("[HudPresenter] Construct ignored: PlayerState is null.");
            return;
        }

        // 이 런의 실제 보유량으로만 재화 칸을 켠다 — 직전 런의 잔상이 남지 않게 먼저 초기화.
        view.ResetCurrencyVisibility();

        if (_provider != null && _provider.TryGet(out var data))
        {
            view.CombatPanel?.SetHp(data.Hp, data.MaxHp);
            view.SetGold(data.TempGold);
        }
        else
        {
            view.CombatPanel?.SetHp(_state.Hp, _state.MaxHp);
            view.SetGold(_state.TempGold);
        }

        _state.OnHpChanged += HandleHpChanged;
        _state.OnGoldChanged += HandleGoldChanged;

        // 포션 슬롯(Q/E 위 첫 칸) — 개수 즉시 반영 후 변화 구독
        HandlePotionChanged(_state.PotionCount, _state.PotionCapacity);
        _state.OnPotionChanged += HandlePotionChanged;

        // 런 재화(강화재료·원석) 표시 — 골드와 동일 패턴
        if (_fuelBank != null)
        {
            HandleFuelChanged();
            _fuelBank.OnFuelChanged += HandleFuelChanged;
        }

        // 심연의 정수 — 런을 넘어 남는 유일한 재화. 줍는 곳과 보이는 곳을 맞춘다.
        if (_run != null)
        {
            HandleEssenceChanged(_run.RunDelta?.GainedEssence ?? 0);
            _run.OnEssenceChanged += HandleEssenceChanged;

            HandleReviveChanged(_run.HasReviveCharge);
            _run.OnReviveChanged += HandleReviveChanged;
        }

        // 현재 버프 즉시 반영
        HandleBuffsChanged();

        // 플레이어 스탯이 이미 로드된 경우 즉시 반영
        if (_runtimeStats != null)
            RefreshStats();
    }

    public void BindPlayer(PlayerController player)
    {
        UnbindPlayer();
        if (player == null) return;

        _player = player;
        _runtimeStats = player.RuntimeStats;
        _weaponManager = player.WeaponManager;
        _cooldownTracker = player.CooldownTracker;

        RefreshStats();
        RefreshWeaponSlots();
        // 유물 전용 아이덴티티 바(체력바 아래) 연결 — 활성 유물이 IRelicResourceProvider면
        view?.CombatPanel?.SetRelicResource((player.RelicBehavior as IRelicResourceProvider)?.RelicResource, player);   // player = Q 문 · 남은 횟수(10-12 A4)
        BindLoadout(AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout : null);   // Q 칸 진화 표시(10-14 C)

        _runtimeStats.OnChanged += RefreshStats;
        _weaponManager.OnWeaponChanged += HandleWeaponChanged;
        _weaponManager.OnEquippedWeaponRefreshed += HandleEquippedWeaponRefreshed;
        _weaponManager.OnSlotsChanged += RefreshWeaponSlots;   // 비활성 슬롯 장착(예비 원거리 지급 등) 반영
        _cooldownTracker.OnCooldownChanged += HandleCooldownChanged;
        player.SkillSealed   += HandleSkillSealed;     // 보스 기믹 스킬 봉인 → 잠금 표시
        player.SkillUnsealed += HandleSkillUnsealed;
        player.SkillRejected    += HandleSkillRejected;      // 재사용 대기 중 누름 → 칸 흔들림(A9)
        player.SkillBuffStarted += HandleSkillBuffStarted;   // 석궁 강화 → 남은 시간 막대(A9)
        _formRuntime = WeaponFormRuntime.Of(player);          // 형이 새로 만들어지면(진화 · 무기 교체) 게이지를 다시 붙인다
        if (_formRuntime != null)
        {
            _formRuntime.Changed   += HandleFormChanged;
            _formRuntime.ReadyNext += HandleFormReadyNext;   // 「다음 타에 발동」 — 형 게이지 테가 한 번 밝아진다(10-12 A7)
        }
        HandleFormChanged();

        // 동적 지속 버프 소스(룬 리소스 + 유물 메커닉) 등록 → 버프창 폴링 갱신
        _playerBuffSource = new PlayerBuffViewSource(player);
        _buffAggregator.AddSource(_playerBuffSource);
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
        _buffPollAccum = 0f;
        RefreshBuffWindow();   // 즉시 1회 반영

        // 새 플레이어(챕터 전환 · 부활)의 현재 상태를 바로 그린다 — 쿨다운은 도는 동안에만 사건이 와서
        // 앞 플레이어의 어두운 칸 · 멈춘 숫자가 스킬을 쓸 때까지 남았다(10-09 출시 점검).
        PushCooldown(SkillType.Q);
        PushCooldown(SkillType.E);
        PushCooldown(SkillType.R);
        if (_state != null) HandlePotionChanged(_state.PotionCount, _state.PotionCapacity);
        if (_run != null) HandleReviveChanged(_run.HasReviveCharge);
    }

    public void UnbindPlayer()
    {
        if (_player != null)
        {
            _player.SkillSealed   -= HandleSkillSealed;
            _player.SkillUnsealed -= HandleSkillUnsealed;
            _player.SkillRejected    -= HandleSkillRejected;
            _player.SkillBuffStarted -= HandleSkillBuffStarted;
        }
        if (_formRuntime != null)
        {
            _formRuntime.Changed   -= HandleFormChanged;
            _formRuntime.ReadyNext -= HandleFormReadyNext;
        }
        _formRuntime = null;
        BindForm(null);
        _formReadyShown = false;
        view?.CombatPanel?.SetFormReadyNext(false, flash: false);
        _player = null;
        UnbindLoadout();
        view?.CombatPanel?.SetRelicResource(null);   // 유물 아이덴티티 바 해제
        if (_playerBuffSource != null)
        {
            _buffAggregator.RemoveSource(_playerBuffSource);
            _playerBuffSource = null;
        }
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
        RefreshBuffWindow();   // 동적 버프 제거 반영(방버프만 남김)

        if (_runtimeStats != null)
        {
            _runtimeStats.OnChanged -= RefreshStats;
            _runtimeStats = null;
        }

        if (_weaponManager != null)
        {
            _weaponManager.OnWeaponChanged -= HandleWeaponChanged;
            _weaponManager.OnEquippedWeaponRefreshed -= HandleEquippedWeaponRefreshed;
            _weaponManager.OnSlotsChanged -= RefreshWeaponSlots;
            _weaponManager = null;
        }

        if (_cooldownTracker != null)
        {
            _cooldownTracker.OnCooldownChanged -= HandleCooldownChanged;
            _cooldownTracker = null;
        }
    }

    public void BindLobby(UserInfo userInfo)
    {
        UnbindLobby();
        if (userInfo == null) return;

        _userInfo = userInfo;
        _userInfo.onUserInfoEvent.AddListener(HandleNicknameChanged);

        // 로비엔 진행 중인 런이 없다 — 지난 런의 골드가 남아 보이지 않게 재화 표시를 초기화한다.
        // (재화 칸은 "실제로 얻었을 때"만 켜진다.)
        view?.ResetCurrencyVisibility();

        SetMode(HUDIds.Mode.Lobby);
    }

    public void UnbindLobby()
    {
        if (_userInfo != null)
        {
            _userInfo.onUserInfoEvent.RemoveListener(HandleNicknameChanged);
            _userInfo = null;
        }
    }

    private void HandleNicknameChanged()
    {
        var nickname = UserInfo.Data.nickname ?? UserInfo.Data.gamerId;
        view?.SetNickname(nickname);
    }

    public void BindBoss(MonsterBase boss)
    {
        int currentHp = GetBossCurrentHp(boss);
        int maxHp = GetBossMaxHp(boss);

        if (ReferenceEquals(_boss, boss))
        {
            if (boss == null) return;

            view?.BossPanel?.Init(maxHp, boss.BossName);
            view?.BossPanel?.SetHP(currentHp, maxHp);
            if (!_bossPanelSuppressed)
                SetMode(HUDIds.Mode.Boss);
            return;
        }

        UnbindBoss();
        if (boss == null) return;

        _boss = boss;
        _boss.OnHPChanged += HandleBossHPChanged;

        view?.BossPanel?.ResetPageFx();   // 새 보스 — 지난 전투의 체력바 깨짐 연출을 걷는다
        view?.BossPanel?.Init(maxHp, boss.BossName);
        view?.BossPanel?.SetHP(currentHp, maxHp);
        AttachBossHudSource(boss as IBossHudSource);

        // 등장 연출이 있는 보스면 연출 완료(OnBossCombatReady) 후 패널 표시
        if (boss.HasEntranceAnimation)
        {
            _bossPanelSuppressed = true;
            boss.OnBossCombatReady += HandleBossCombatReady;
            SetMode(HUDIds.Mode.Combat); // 연출 중에는 전투 HUD 유지
        }
        else
        {
            SetMode(HUDIds.Mode.Boss);
        }
    }

    private void HandleBossCombatReady()
    {
        _bossPanelSuppressed = false;
        if (_boss != null)
            _boss.OnBossCombatReady -= HandleBossCombatReady;
        SetMode(HUDIds.Mode.Boss);
    }

    public void RefreshBoundBossPanel()
    {
        if (_boss == null) return;

        int currentHp = GetBossCurrentHp(_boss);
        int maxHp = GetBossMaxHp(_boss);
        view?.BossPanel?.Init(maxHp, _boss.BossName);
        view?.BossPanel?.SetPages(_bossHud?.HudPageMarkers, _bossHud?.HudPage ?? 1);
        view?.BossPanel?.SetHP(currentHp, maxHp);
    }

    public void UnbindBoss()
    {
        DetachBoss();

        if (_currentMode == HUDIds.Mode.Boss)
            SetMode(HUDIds.Mode.Combat);
    }

    /// <summary>보스 구독 해제만 — 모드 전환 cascade 없음.
    /// 파괴 중(OnDestroy)이나 런 종료 teardown처럼 SetMode를 태우면 안 되는 경로가 쓴다.</summary>
    private void DetachBoss()
    {
        if (_boss != null)
        {
            _boss.OnHPChanged -= HandleBossHPChanged;
            _boss.OnBossCombatReady -= HandleBossCombatReady;
            _boss = null;
        }
        AttachBossHudSource(null);

        _bossPanelSuppressed = false;
    }

    // ── 보스 HP바 부가 정보(페이지 눈금 · 무적 · 무방비) — 구현한 보스만 ──
    private void AttachBossHudSource(IBossHudSource src)
    {
        if (_bossHud != null)
        {
            _bossHud.HudInvulnerableChanged -= HandleBossInvulnerableChanged;
            _bossHud.HudVulnerableWindow    -= HandleBossVulnerableWindow;
            _bossHud.HudPageMarkersChanged  -= HandleBossPageMarkersChanged;
            _bossHud.HudPageRefill          -= HandleBossPageRefill;
        }
        _bossHud = src;

        var panel = view?.BossPanel;
        panel?.SetPages(src?.HudPageMarkers, src?.HudPage ?? 1);
        panel?.SetInvulnerable(src != null && src.HudInvulnerable);
        if (src == null) return;

        src.HudInvulnerableChanged += HandleBossInvulnerableChanged;
        src.HudVulnerableWindow    += HandleBossVulnerableWindow;
        src.HudPageMarkersChanged  += HandleBossPageMarkersChanged;
        src.HudPageRefill          += HandleBossPageRefill;
    }

    private void HandleBossInvulnerableChanged(bool on) => view?.BossPanel?.SetInvulnerable(on);
    private void HandleBossVulnerableWindow(float seconds) => view?.BossPanel?.FlashVulnerable(seconds);
    private void HandleBossPageMarkersChanged() => view?.BossPanel?.SetPages(_bossHud?.HudPageMarkers, _bossHud?.HudPage ?? 1);
    private void HandleBossPageRefill(int page, float seconds) => view?.BossPanel?.PlayPageRefill(page, seconds);

    private void HandleHpChanged(int hp, int maxHp) => view?.CombatPanel?.SetHp(hp, maxHp);
    private void HandleGoldChanged(int gold) => view?.SetGold(gold);

    /// <summary>포션 개수 변화 → HUD 슬롯(Q/E 위 첫 칸) 갱신.</summary>
    private void HandlePotionChanged(int count, int capacity)
        => view?.CombatPanel?.SetPotion(count, capacity);
    private void HandleFuelChanged()
    {
        if (_fuelBank == null || view == null) return;
        view.SetEnhanceMaterial(_fuelBank.EnhanceMaterial);
        view.SetRuneOre(_fuelBank.RuneOre);
    }

    private void HandleEssenceChanged(int total) => view?.SetEssence(total);

    /// <summary>
    /// 부활 잔여 표기. <b>해금 여부</b>와 <b>이 런에서 남았는지</b>는 다른 질문이라 둘 다 넘긴다 —
    /// 해금 안 했으면 표식을 감추고, 해금했는데 썼으면 꺼진 채로 남긴다.
    /// </summary>
    private void HandleReviveChanged(bool available)
        => view?.CombatPanel?.SetRevive(MemoryAltarService.HasRevive, available);

    private GameRunSession _run;
    private void HandleWeaponChanged(WeaponData _, GameObject __) => RefreshWeaponSlots();
    private void HandleEquippedWeaponRefreshed(WeaponData _) => RefreshWeaponSlots();
    private void HandleBuffsChanged() => RefreshBuffWindow();

    private enum BuffDiff { None, Values, Structure }

    /// <summary>
    /// 버프창 수집→구조/값 차이 판정→변경 종류에 맞게 갱신. 방버프 이벤트와 동적 폴링의 단일 경로.
    /// 구조 변경=전량 재생성, 값(게이지/스택)만 변경=in-place 갱신, 무변경=스킵(폴링 GC 억제).
    /// </summary>
    private void RefreshBuffWindow()
    {
        if (view?.CombatPanel == null) return;

        var items = _buffAggregator.Collect();
        var diff = DiffBuffs(items);
        if (diff == BuffDiff.None) return;

        CacheBuffItems(items);
        if (diff == BuffDiff.Structure) view.CombatPanel.RefreshBuffView(items);
        else                            view.CombatPanel.UpdateBuffValues(items);
    }

    private BuffDiff DiffBuffs(IReadOnlyList<BuffViewItem> items)
    {
        if (items.Count != _lastBuffItems.Count) return BuffDiff.Structure;

        bool valuesChanged = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i].SameStructure(_lastBuffItems[i])) return BuffDiff.Structure;
            if (!items[i].SameValues(_lastBuffItems[i])) valuesChanged = true;
        }
        return valuesChanged ? BuffDiff.Values : BuffDiff.None;
    }

    private void CacheBuffItems(IReadOnlyList<BuffViewItem> items)
    {
        _lastBuffItems.Clear();
        for (int i = 0; i < items.Count; i++)
            _lastBuffItems.Add(items[i]);
    }

    /// <summary>버프 획득 알림 텍스트 표시. 컷씬 중이면 끝난 뒤로 미룬다.</summary>
    public void ShowBuffNotice(string message)
    {
        if (DeferDuringCutscene(isItem: false, message)) return;
        view?.CombatPanel?.ShowBuffNotice(message);
    }

    /// <summary>아이템 효과 발동 알림 (왼쪽 스택형). 컷씬 중이면 끝난 뒤로 미룬다.</summary>
    public void ShowItemEffectNotice(string message)
    {
        if (DeferDuringCutscene(isItem: true, message)) return;
        view?.CombatPanel?.ShowItemEffectNotice(message);
    }

    // ── 컷씬 중 알림 보류 ────────────────────────────────────
    // 알림 텍스트는 CombatPanel 안에 있는데 컷씬 모드에선 그 패널이 꺼진다.
    // 그대로 두면 <b>화면에 뜨지도 않고 만료 타이머만 돌아 알림이 조용히 유실</b>된다.
    // 그래서 컷씬 동안에는 쌓아두고, 컷씬이 끝나면 순서대로 보여준다.
    private readonly Queue<(bool isItem, string msg)> _deferredNotices = new();
    private bool _flushingNotices;

    /// <summary>보류 상한 — 컷씬이 길어도 알림이 무한정 쌓여 끝난 뒤 도배되지 않게 한다.</summary>
    private const int   MaxDeferredNotices = 3;
    /// <summary>보류분을 하나씩 보여주는 간격(초). 알림 슬롯이 1개라 겹치면 덮어써진다.</summary>
    private const float DeferredNoticeGap  = 1.1f;

    private bool DeferDuringCutscene(bool isItem, string message)
    {
        if (_currentMode != HUDIds.Mode.Cutscene || string.IsNullOrEmpty(message)) return false;

        // 넘치면 가장 오래된 것부터 버린다 — 최근 획득이 더 중요하다.
        while (_deferredNotices.Count >= MaxDeferredNotices) _deferredNotices.Dequeue();
        _deferredNotices.Enqueue((isItem, message));
        return true;
    }

    /// <summary>컷씬이 끝나면 보류분을 간격을 두고 차례로 표시한다.</summary>
    private async UniTaskVoid FlushDeferredNoticesAsync()
    {
        if (_flushingNotices) return;
        _flushingNotices = true;

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            while (_deferredNotices.Count > 0)
            {
                var (isItem, msg) = _deferredNotices.Dequeue();
                if (isItem) view?.CombatPanel?.ShowItemEffectNotice(msg);
                else        view?.CombatPanel?.ShowBuffNotice(msg);

                if (_deferredNotices.Count > 0)
                    await UniTask.Delay(System.TimeSpan.FromSeconds(DeferredNoticeGap),
                                        ignoreTimeScale: true, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
        finally { _flushingNotices = false; }
    }

    // ── 빌드 완성 신호 · 유물 진화(10-14 빌드 시너지 · 83 UI 요청 2 · 3) ──

    /// <summary>
    /// 각인 단계 · 유물 진화 · 속성 존 단계 알림 한 줄(<see cref="ItemEffectVfxHelper.ShowBuildNotice"/>). 바로 그리지 않고 모아 두었다가
    /// HUD가 보일 때(컷신 · 차단 팝업 · 룬판이 걷힌 다음 프레임) 진화 → 각인 → 존 순으로 보인다 — 룬판에서 단계가 오르면 판이 HUD를 덮어
    /// 알림이 안 보인 채 만료됐다. 보류 중 같은 열쇠는 높은 단계 하나로 합치고, 보일 때 아직 참인 것만 낸다(놓았다 뺀 단계는 버린다).
    /// </summary>
    public void ShowBuildNotice(in BuildNotice notice)
    {
        if (notice.What == BuildNotice.Kind.Evolution)
        {
            // 진화 알림은 이 HUD가 진화 순간(HandleRelicPartsChanged)에 직접 낸다 — 효과가 새 플레이어에 다시 묶일 때 오는 것은 거른다.
            if (notice.EvolutionId == s_evolutionAnnounced || !IsLiveRunPlayer) return;
            s_evolutionAnnounced = notice.EvolutionId;
        }
        QueueBuildNotice(notice);
    }

    private void QueueBuildNotice(in BuildNotice notice)
    {
        if (string.IsNullOrEmpty(notice.Key)) return;
        for (int i = 0; i < _pendingBuild.Count; i++)
        {
            if (_pendingBuild[i].Key != notice.Key) continue;
            if (notice.Tier >= _pendingBuild[i].Tier) _pendingBuild[i] = notice;
            return;
        }
        if (_pendingBuild.Count >= MaxPendingBuild) _pendingBuild.RemoveAt(0);
        _pendingBuild.Add(notice);
    }

    /// <summary>지금 런 플레이어에 묶여 있나 — 이어하기 복원(허브 · 타이틀에서 로드아웃이 채워짐)은 진화 「순간」이 아니다.</summary>
    private bool IsLiveRunPlayer
        => _player != null && GameRunBootstrapper.Instance != null
        && ReferenceEquals(GameRunBootstrapper.Instance.Run?.Player, _player);

    /// <summary>HUD가 가려져 알림 · 순간 연출이 보이지 않는 동안 — 전투 패널이 없는 모드(컷신 등) · 차단 팝업(HUD를 걷는다) · 룬판(화면을 덮는다).</summary>
    private bool BuildNoticeHeld
        => (_currentMode != HUDIds.Mode.Combat && _currentMode != HUDIds.Mode.Boss)
        || (UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen)
        || Managers.UI.IsGameplayBlocked;

    private void TickBuildNotices()
    {
        if (_pendingBuild.Count == 0 && _pendingMomentId == null) return;
        var panel = view != null ? view.CombatPanel : null;
        if (panel == null || BuildNoticeHeld) return;

        if (_pendingMomentId != null)
        {
            string id = _pendingMomentId;
            _pendingMomentId = null;
            if (_loadout != null && _loadout.RelicEvolutionId == id)
                panel.SetRelicEvolution(RelicEvolution.ById(id), CrestOf(_loadout), playMoment: true);
        }
        for (int kind = (int)BuildNotice.Kind.Evolution; kind <= (int)BuildNotice.Kind.Zone; kind++)
            for (int i = 0; i < _pendingBuild.Count; i++)
                if ((int)_pendingBuild[i].What == kind && _pendingBuild[i].StillHolds())
                    panel.ShowBuildNotice(_pendingBuild[i]);
        _pendingBuild.Clear();
    }

    /// <summary>로드아웃을 묶고 Q 칸 진화 표시를 지금 상태로 그린다(연출 없음) — 챕터 전환 · 이어하기에 이미 얻은 진화는 다시 알리지 않는다.</summary>
    private void BindLoadout(PlayerLoadout loadout)
    {
        UnbindLoadout();
        _loadout = loadout;
        if (_loadout == null) return;
        string id = _loadout.RelicEvolutionId;
        s_evolutionAnnounced = string.IsNullOrEmpty(id) ? null : id;   // 비었으면 새 런 — 알림 기억도 비운다
        _evolutionShown = id;
        view?.CombatPanel?.SetRelicEvolution(RelicEvolution.ById(id), CrestOf(_loadout), playMoment: false);
        _loadout.RelicPartsChanged += HandleRelicPartsChanged;
    }

    private void UnbindLoadout()
    {
        if (_loadout != null) _loadout.RelicPartsChanged -= HandleRelicPartsChanged;
        _loadout = null;
        _evolutionShown = null;
        _pendingMomentId = null;
        view?.CombatPanel?.SetRelicEvolution(null, null, playMoment: false);
    }

    /// <summary>
    /// 유물 기억이 바뀌었다 — 진화가 <b>런 중 이 플레이어에게 새로</b> 생겼으면(기억 카드에서 「진화」를 고름) 순간 연출 + 알림,
    /// 그 밖(새 런으로 비움 · 복원)은 표시만 맞춘다.
    /// </summary>
    private void HandleRelicPartsChanged()
    {
        string id = _loadout != null ? _loadout.RelicEvolutionId : null;
        if (id == _evolutionShown) return;
        bool moment = !string.IsNullOrEmpty(id) && string.IsNullOrEmpty(_evolutionShown) && IsLiveRunPlayer;
        _evolutionShown = id;
        if (!moment)
        {
            if (string.IsNullOrEmpty(id)) s_evolutionAnnounced = null;   // 기억을 비웠다(유물 교체 · 새 런) — 알림 기억도
            view?.CombatPanel?.SetRelicEvolution(RelicEvolution.ById(id), CrestOf(_loadout), playMoment: false);
            return;
        }
        _pendingMomentId = id;
        s_evolutionAnnounced = id;
        QueueBuildNotice(BuildNotice.Evolution(RelicEvolution.ById(id)));
    }

    private static Sprite CrestOf(PlayerLoadout loadout) => loadout != null && loadout.Relic != null ? loadout.Relic.Crest : null;

    private void HandleCooldownChanged(SkillType skill, float remaining, float total)
        => view?.CombatPanel?.SetSkillCooldown(skill, remaining, total);

    /// <summary>추적기의 지금 쿨다운을 사건 경로로 한 번 흘린다. 전체 시간은 남은 시간 ÷ 진행률로 되돌린다(추적기엔 전체 시간 조회가 없다).</summary>
    private void PushCooldown(SkillType skill)
    {
        if (_cooldownTracker == null) return;
        float remaining = _cooldownTracker.GetRemaining(skill);
        float progress  = _cooldownTracker.GetProgress(skill);
        HandleCooldownChanged(skill, remaining, progress > 0f ? remaining / progress : 0f);
    }

    private void HandleBossHPChanged(int hp, int maxHp)
    {
        view?.BossPanel?.SetHP(hp, maxHp);
    }

    private static int GetBossMaxHp(MonsterBase boss)
        => boss != null ? Mathf.Max(1, boss.BossMaxHp) : 1;

    private static int GetBossCurrentHp(MonsterBase boss)
    {
        if (boss == null) return 1;

        int hp = Mathf.Max(0, boss.CurrentHp);
        return Mathf.Clamp(hp, 0, GetBossMaxHp(boss));
    }

    private void RefreshStats()
    {
        if (_runtimeStats == null || view?.CombatPanel == null) return;
        view.CombatPanel.SetHp(_runtimeStats.Hp, _runtimeStats.MaxHp);
        view.CombatPanel.SetStats(_runtimeStats.AttackPower, _runtimeStats.Defense);
    }

    private void RefreshWeaponSlots()
    {
        if (_weaponManager == null || view?.CombatPanel == null) return;

        for (int i = 0; i < _weaponManager.SlotCount; i++)
        {
            var slot = _weaponManager.slots[i];
            var info = new WeaponSlotInfo();

            if (slot != null && !slot.IsEmpty && slot.runtimeData != null)
            {
                info.HasWeapon = true;
                info.Icon = slot.runtimeData.icon;
                info.Name = slot.runtimeData.displayName;
                info.Attack = slot.runtimeData.baseAttack;
                info.Defense = slot.runtimeData.baseDefense;
                info.Type = slot.runtimeData.weaponType;
                info.EnhanceLevel = slot.runtimeData.enhanceLevel;   // 칸에 +N으로 찍는다
            }

            view.CombatPanel.SetWeaponSlot(i, info);
        }

        // 슬롯 모델: Q = 유물 고유(아이콘은 유물 SO — 비면 뷰 폴백), E = 무기 skillE,
        // R = 무기 skillQ(레거시 필드명 — ActSkillState.GetSkillSO와 동일 매핑).
        // 예전엔 Q에 무기 skillQ 아이콘을 띄워 실제 발동(유물)과 어긋났고 R은 늘 비어 있었다.
        var current = _weaponManager.CurrentWeaponData;
        view.CombatPanel.SetSkillIcon(SkillType.Q, _player != null && _player.RelicClass != null ? _player.RelicClass.QSkillIcon : null);
        view.CombatPanel.SetSkillIcon(SkillType.E, current?.skillEIcon);
        view.CombatPanel.SetSkillIcon(SkillType.R, current?.skillQIcon);

        // 스킬 없는 슬롯(무형검 등)은 잠금 표시. 무기 교체·진화 때마다 이 경로가 다시 돌아
        // 스킬이 생기면 자동으로 풀린다(별도 해제 처리 불필요).
        if (_player != null)
        {
            RefreshSkillLock(SkillType.Q);
            RefreshSkillLock(SkillType.E);
            RefreshSkillLock(SkillType.R);
        }
        view.CombatPanel.SetActiveWeapon(_weaponManager.CurrentSlotIndex);   // 활성 무기 강조
    }

    /// <summary>스킬 칸 잠금 = 스킬 없음(무형검 등) 또는 보스 기믹 봉인 중.</summary>
    private void RefreshSkillLock(SkillType slot)
    {
        if (_player == null || view?.CombatPanel == null) return;
        view.CombatPanel.SetSkillLocked(slot, !_player.HasSkillInSlot(slot) || _player.IsSkillSealed(slot));
    }

    private void HandleSkillSealed(SkillType slot, float remaining) => RefreshSkillLock(slot);
    private void HandleSkillUnsealed(SkillType slot) => RefreshSkillLock(slot);
    private void HandleSkillRejected(SkillType slot) => view?.CombatPanel?.PlaySkillRejected(slot);
    private void HandleSkillBuffStarted(SkillType slot, float duration) => view?.CombatPanel?.ShowSkillBuff(slot, duration);

    // ── 형 게이지(10-08) — 근접 칸 무기의 형(쌓인 수 · 발동)을 HUD 무기 원 위 알약으로 ──
    private void HandleFormChanged() => BindForm(_formRuntime != null ? _formRuntime.Current : null);

    private void BindForm(WeaponFormBase form)
    {
        if (_form != null)
        {
            _form.StacksChanged -= HandleFormStacks;
            _form.Triggered     -= HandleFormTriggered;
        }
        _form = form;
        if (_form != null)
        {
            _form.StacksChanged += HandleFormStacks;
            _form.Triggered     += HandleFormTriggered;
        }
        var panel = view?.CombatPanel;
        if (panel == null) return;
        panel.SetFormGauge(_form?.Form);
        panel.SetFormStacks(_form?.Stacks ?? 0);
    }

    /// <summary>쌓인 수 + 줄어든 까닭(맞음 · 시간 · 발동 · 돌려받음) — 사건 안에서 읽어 넘긴다(10-12 A7 · S9).</summary>
    private void HandleFormStacks(int stacks, int max)
    {
        if (_form == null) { view?.CombatPanel?.SetFormStacks(stacks); return; }
        view?.CombatPanel?.SetFormStacks(stacks, _form.LastReason, _form.LastRefund);
    }

    private void HandleFormReadyNext()
    {
        _formReadyShown = true;
        view?.CombatPanel?.SetFormReadyNext(true, flash: true);
    }

    /// <summary>「다음 타에 발동」이 풀렸나 — 오르는 순간은 ReadyNext 사건이 알리고, 풀림(마지막 타 휘두름 · 쌓임 변화 · 무기 교체)은 사건이 없어 매 프레임 본다(bool 하나).</summary>
    private void TickFormReadyNext()
    {
        bool on = _formRuntime != null && _formRuntime.IsReadyNext;
        if (on == _formReadyShown) return;
        _formReadyShown = on;
        view?.CombatPanel?.SetFormReadyNext(on, flash: false);
    }

    private void HandleFormTriggered(string _) => view?.CombatPanel?.PlayFormTriggered();

    public void Dispose()
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state.OnPotionChanged -= HandlePotionChanged;
            _state = null;
        }
        if (_fuelBank != null)
        {
            _fuelBank.OnFuelChanged -= HandleFuelChanged;
            _fuelBank = null;
        }
        // 정수 · 부활도 뗀다 — 끝난 런이 다음 HUD에 값을 밀어 넣지 않게(10-09 출시 점검)
        if (_run != null)
        {
            _run.OnEssenceChanged -= HandleEssenceChanged;
            _run.OnReviveChanged  -= HandleReviveChanged;
            _run = null;
        }
        _deferredNotices.Clear();   // 컷신 중 보류한 알림이 다음 런 첫 컷신 뒤에 뜨지 않게
        _pendingBuild.Clear();
        _pendingMomentId = null;

        _provider = null;
        // @UIRoot는 DDOL이라 씬 전환으로 파괴되지 않는다 — 여기서 보스를 떼지 않으면
        // 사망 복귀 후에도 이전 런의 보스 체력바가 그대로 남는다.
        DetachBoss();
        UnbindPlayer();
        UnbindLobby();
        UnbindBuffHandler();
        UnbindCovenant();
    }

    private void UnbindBuffHandler()
    {
        if (_buffHandler != null)
        {
            _buffHandler.OnBuffsChanged -= HandleBuffsChanged;
            _buffHandler = null;
        }
        _buffAggregator.SetRoomBuffSource(null);

        if (_itemBuffSource != null)
        {
            _buffAggregator.RemoveSource(_itemBuffSource);
            _itemBuffSource = null;
        }
        if (_nightmareRuleSource != null)
        {
            _buffAggregator.RemoveSource(_nightmareRuleSource);
            _nightmareRuleSource = null;
        }
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
    }

    public void BindCovenant(CovenantHandler handler)
    {
        UnbindCovenant();
        if (handler == null) return;

        _covenantHandler = handler;
        _covenantHandler.OnCovenantListChanged += HandleCovenantChanged;
        HandleCovenantChanged(); // 현재 목록 즉시 반영

        // 서약 발동/지속 상태(옵트인) 버프창 소스 등록 — 보유 목록은 CovenantPanel이 담당.
        _covenantBuffSource = new CovenantBuffViewSource(handler);
        _buffAggregator.AddSource(_covenantBuffSource);
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
    }

    public void UnbindCovenant()
    {
        if (_covenantBuffSource != null)
        {
            _buffAggregator.RemoveSource(_covenantBuffSource);
            _covenantBuffSource = null;
        }
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;

        if (_covenantHandler == null) return;
        _covenantHandler.OnCovenantListChanged -= HandleCovenantChanged;
        _covenantHandler = null;
        view?.CovenantPanel?.Clear();
    }

    private void HandleCovenantChanged()
        => view?.CovenantPanel?.Refresh(_covenantHandler?.Covenants);

    private void OnDestroy()
    {
        // UnbindBoss 의 SetMode(Combat) cascade 는 파괴 중인 GameObject 에
        // SetActive 를 호출해 Unity 예외를 유발하므로 이벤트 해제만 수행한다(DetachBoss).
        Dispose();
    }

    public void SetMode(HUDIds.Mode mode)
    {
        if (view == null) return;

        // 보스 패널 억제 중이면 Boss 모드 요청을 Combat으로 강등
        if (_bossPanelSuppressed && mode == HUDIds.Mode.Boss)
            mode = HUDIds.Mode.Combat;

        bool shouldBeVisible = mode != HUDIds.Mode.None;
        SetVisible(shouldBeVisible, true);

        if (mode == HUDIds.Mode.Combat || mode == HUDIds.Mode.Boss)
            view.EnsureCombatPanelVisible();

        if (mode == HUDIds.Mode.Boss || mode == HUDIds.Mode.BossCutscene)
            RefreshBoundBossPanel();

        if (_currentMode == mode) return;

        var prev = _currentMode;
        _currentMode = mode;
        view.SetSections(ResolveSections(mode));

        // 컷씬을 벗어나면 그동안 미뤄둔 알림을 차례로 보여준다.
        if (prev == HUDIds.Mode.Cutscene && mode != HUDIds.Mode.Cutscene && _deferredNotices.Count > 0)
            FlushDeferredNoticesAsync().Forget();
    }

    private static HUDIds.Section ResolveSections(HUDIds.Mode mode)
    {
        switch (mode)
        {
            case HUDIds.Mode.Lobby:
                return HUDIds.Section.TopBar;

            case HUDIds.Mode.Combat:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.CombatPanel |
                       HUDIds.Section.CovenantPanel |
                       HUDIds.Section.Minimap;

            case HUDIds.Mode.Boss:
                // 재화 줄(TopBar)은 걷는다 — 오른쪽 위 재화 줄이 보스바 오른쪽 끝 · 보스전 목표 띠와 같은 높이라 겹쳤다(10-01 f5 전주기 시뮬).
                return HUDIds.Section.CombatPanel |
                       HUDIds.Section.BossPanel |
                       HUDIds.Section.CovenantPanel |
                       HUDIds.Section.Minimap;

            case HUDIds.Mode.Cutscene:
                return HUDIds.Section.None;

            case HUDIds.Mode.BossCutscene:
                return HUDIds.Section.BossPanel;

            default:
                return HUDIds.Section.None;
        }
    }

    public void SetVisible(bool visible, bool immediate)
    {
        if (canvasGroup == null)
        {
            if (visible && ActivationHeld) return;
            gameObject.SetActive(visible);
            return;
        }

        if (immediate)
        {
            _fadeToken++;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            return;
        }

        FadeTo(visible ? 1f : 0f).Forget();
    }

    private async UniTaskVoid FadeTo(float targetAlpha)
    {
        if (canvasGroup == null) return;

        int token = ++_fadeToken;
        float startAlpha = canvasGroup.alpha;
        float t = 0f;

        bool willBeVisible = targetAlpha > 0.5f;
        canvasGroup.interactable = willBeVisible;
        canvasGroup.blocksRaycasts = willBeVisible;

        float dur = Mathf.Max(0.0001f, fadeDuration);

        while (t < 1f)
        {
            if (token != _fadeToken) return;
            t += Time.unscaledDeltaTime / dur;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            await UniTask.Yield();
        }

        canvasGroup.alpha = targetAlpha;
    }
}
