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
    private ItemBuffViewSource _itemBuffSource;
    private bool _hasDynamicBuffSources;          // 룬/유물 등 폴링 기반 소스 등록 여부
    private float _buffPollAccum;
    private const float BuffPollInterval = 0.25f; // 룬 배지 폴링과 동일 cadence
    private readonly List<BuffViewItem> _lastBuffItems = new();  // 더티 체크 캐시(재사용)
    private CovenantHandler _covenantHandler;
    private CovenantBuffViewSource _covenantBuffSource;

    private UserInfo _userInfo;

    private MonsterBase _boss;
    public MonsterBase BoundBoss => _boss;
    public bool HasBoundBoss => _boss != null;
    public CanvasGroup MainCanvasGroup => canvasGroup;
    public MinimapView  MinimapView => view != null ? view.MinimapView : null;

    private bool _bossPanelSuppressed;

    private int _fadeToken = 0;
    private HUDIds.Mode _currentMode = HUDIds.Mode.None;

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
        view?.CombatPanel?.SetRelicResource((player.RelicBehavior as IRelicResourceProvider)?.RelicResource);

        _runtimeStats.OnChanged += RefreshStats;
        _weaponManager.OnWeaponChanged += HandleWeaponChanged;
        _weaponManager.OnEquippedWeaponRefreshed += HandleEquippedWeaponRefreshed;
        _weaponManager.OnSlotsChanged += RefreshWeaponSlots;   // 비활성 슬롯 장착(예비 원거리 지급 등) 반영
        _cooldownTracker.OnCooldownChanged += HandleCooldownChanged;

        // 동적 지속 버프 소스(룬 리소스 + 유물 메커닉) 등록 → 버프창 폴링 갱신
        _playerBuffSource = new PlayerBuffViewSource(player);
        _buffAggregator.AddSource(_playerBuffSource);
        _hasDynamicBuffSources = _buffAggregator.DynamicSourceCount > 0;
        _buffPollAccum = 0f;
        RefreshBuffWindow();   // 즉시 1회 반영
    }

    public void UnbindPlayer()
    {
        _player = null;
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

        view?.BossPanel?.Init(maxHp, boss.BossName);
        view?.BossPanel?.SetHP(currentHp, maxHp);

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

        _bossPanelSuppressed = false;
    }

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
    private void HandleCooldownChanged(SkillType skill, float remaining, float total)
        => view?.CombatPanel?.SetSkillCooldown(skill, remaining, total);

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
            }

            view.CombatPanel.SetWeaponSlot(i, info);
        }

        var current = _weaponManager.CurrentWeaponData;
        view.CombatPanel.SetSkillIcon(SkillType.Q, current?.skillQIcon);
        view.CombatPanel.SetSkillIcon(SkillType.E, current?.skillEIcon);
        view.CombatPanel.SetSkillIcon(SkillType.R, current?.skillRIcon);

        // 스킬 없는 슬롯(무형검 등)은 잠금 표시. 무기 교체·진화 때마다 이 경로가 다시 돌아
        // 스킬이 생기면 자동으로 풀린다(별도 해제 처리 불필요).
        if (_player != null)
        {
            view.CombatPanel.SetSkillLocked(SkillType.Q, !_player.HasSkillInSlot(SkillType.Q));
            view.CombatPanel.SetSkillLocked(SkillType.E, !_player.HasSkillInSlot(SkillType.E));
            view.CombatPanel.SetSkillLocked(SkillType.R, !_player.HasSkillInSlot(SkillType.R));
        }
        view.CombatPanel.SetActiveWeapon(_weaponManager.CurrentSlotIndex);   // 활성 무기 강조
    }

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

        if (mode == HUDIds.Mode.Boss)
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
                       HUDIds.Section.SystemNotices |
                       HUDIds.Section.Minimap;

            case HUDIds.Mode.Boss:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.CombatPanel |
                       HUDIds.Section.BossPanel |
                       HUDIds.Section.CovenantPanel |
                       HUDIds.Section.SystemNotices |
                       HUDIds.Section.Minimap;

            case HUDIds.Mode.Cutscene:
                return HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Spectate:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Puzzle:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.GridPanel |
                       HUDIds.Section.SystemNotices;

            default:
                return HUDIds.Section.None;
        }
    }

    public void SetVisible(bool visible, bool immediate)
    {
        if (canvasGroup == null)
        {
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
