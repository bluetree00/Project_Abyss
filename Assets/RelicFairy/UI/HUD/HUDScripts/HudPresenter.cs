//============================================================
// HudPresenter.cs
// - Mode/Section 제어 + CanvasGroup Fade
// - PlayerRunState 이벤트 구독 (HP, Gold)
// - PlayerRuntimeStats 이벤트 구독 (AttackPower)
// - PlayerWeaponManager 이벤트 구독 (장비 변화)
//============================================================
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
    private UIHudDataProvider _provider;

    private PlayerRuntimeStats _runtimeStats;
    private PlayerWeaponManager _weaponManager;
    private SkillCooldownTracker _cooldownTracker;
    private RoomBuffHandler _buffHandler;
    private CovenantHandler _covenantHandler;

    private UserInfo _userInfo;

    private MonsterBase _boss;
    public MonsterBase BoundBoss => _boss;
    public bool HasBoundBoss => _boss != null;
    public CanvasGroup MainCanvasGroup => canvasGroup;
    public MinimapView  MinimapView => view != null ? view.MinimapView : null;

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

    public void Construct(GameRunSession run, UIHudDataProvider provider)
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state = null;
        }

        _provider = provider;
        _state = run?.PlayerState;

        // 버프 핸들러 구독
        UnbindBuffHandler();
        if (run?.BuffHandler != null)
        {
            _buffHandler = run.BuffHandler;
            _buffHandler.OnBuffsChanged += HandleBuffsChanged;
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

        _runtimeStats = player.RuntimeStats;
        _weaponManager = player.WeaponManager;
        _cooldownTracker = player.CooldownTracker;

        RefreshStats();
        RefreshWeaponSlots();

        _runtimeStats.OnChanged += RefreshStats;
        _weaponManager.OnWeaponChanged += HandleWeaponChanged;
        _cooldownTracker.OnCooldownChanged += HandleCooldownChanged;
    }

    public void UnbindPlayer()
    {
        if (_runtimeStats != null)
        {
            _runtimeStats.OnChanged -= RefreshStats;
            _runtimeStats = null;
        }

        if (_weaponManager != null)
        {
            _weaponManager.OnWeaponChanged -= HandleWeaponChanged;
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
            SetMode(HUDIds.Mode.Boss);
            return;
        }

        UnbindBoss();
        if (boss == null) return;

        _boss = boss;
        _boss.OnHPChanged += HandleBossHPChanged;

        view?.BossPanel?.Init(maxHp, boss.BossName);
        view?.BossPanel?.SetHP(currentHp, maxHp);

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
        if (_boss != null)
        {
            _boss.OnHPChanged -= HandleBossHPChanged;
            _boss = null;
        }

        if (_currentMode == HUDIds.Mode.Boss)
            SetMode(HUDIds.Mode.Combat);
    }

    private void HandleHpChanged(int hp, int maxHp) => view?.CombatPanel?.SetHp(hp, maxHp);
    private void HandleGoldChanged(int gold) => view?.SetGold(gold);
    private void HandleWeaponChanged(WeaponData _, GameObject __) => RefreshWeaponSlots();
    private void HandleBuffsChanged() => view?.CombatPanel?.RefreshBuffList(_buffHandler?.ActiveBuffs);

    /// <summary>버프 획득 알림 텍스트 표시.</summary>
    public void ShowBuffNotice(string message) => view?.CombatPanel?.ShowBuffNotice(message);

    /// <summary>아이템 효과 발동 알림 (왼쪽 스택형).</summary>
    public void ShowItemEffectNotice(string message) => view?.CombatPanel?.ShowItemEffectNotice(message);
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
    }

    public void Dispose()
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state = null;
        }

        _provider = null;
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
    }

    public void BindCovenant(CovenantHandler handler)
    {
        UnbindCovenant();
        if (handler == null) return;

        _covenantHandler = handler;
        _covenantHandler.OnCovenantListChanged += HandleCovenantChanged;
        HandleCovenantChanged(); // 현재 목록 즉시 반영
    }

    public void UnbindCovenant()
    {
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
        // SetActive 를 호출해 Unity 예외를 유발하므로 이벤트 해제만 수행한다.
        if (_boss != null)
        {
            _boss.OnHPChanged -= HandleBossHPChanged;
            _boss = null;
        }
        Dispose();
    }

    public void SetMode(HUDIds.Mode mode)
    {
        if (view == null) return;

        bool shouldBeVisible = mode != HUDIds.Mode.None;
        SetVisible(shouldBeVisible, true);

        if (mode == HUDIds.Mode.Combat || mode == HUDIds.Mode.Boss)
            view.EnsureCombatPanelVisible();

        if (mode == HUDIds.Mode.Boss)
            RefreshBoundBossPanel();

        if (_currentMode == mode) return;

        _currentMode = mode;
        view.SetSections(ResolveSections(mode));
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
