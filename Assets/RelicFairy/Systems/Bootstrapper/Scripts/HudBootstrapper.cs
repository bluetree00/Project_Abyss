using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using RelicFairy.Monster;

public sealed class HudBootstrapper : MonoBehaviour
{
    [SerializeField] private HudPresenter presenter;
    [SerializeField] private Transform hudVisualRoot;

    private UIHudDataProvider _provider;
    private GameRunSession _run;
    private float _panelGuardTimer;
    private bool _startRoomSuppressed;

    public MinimapView MinimapView => presenter != null ? presenter.MinimapView : null;

    // ✅ Construct 중복 방지
    private bool _constructed;

    /// <summary>스타트 방 대화/위스프 구간에서 HUD를 숨길 때 true. false로 복원하면 즉시 표시.</summary>
    public void SetStartRoomSuppressed(bool suppress)
    {
        _startRoomSuppressed = suppress;
        if (suppress)
        {
            var target = hudVisualRoot != null ? hudVisualRoot : presenter?.transform;
            if (target != null) target.gameObject.SetActive(false);
        }
        else
        {
            EnsureHudHierarchyVisible();
        }
    }

    /// <summary>HUD를 페이드로 표시(전투 진입 연출). 억제 해제 후 CanvasGroup 알파 0→1.</summary>
    public async Cysharp.Threading.Tasks.UniTask FadeInStartRoomAsync(float duration)
    {
        _startRoomSuppressed = false;
        EnsureHudHierarchyVisible();

        // 억제 중에는 SetMode를 막아뒀으므로, 해제 시 전투 패널을 여기서 켜준다.
        if (presenter != null) presenter.SetMode(HUDIds.Mode.Combat);

        var target = hudVisualRoot != null ? hudVisualRoot : presenter?.transform;
        if (target == null) return;

        var cg = target.GetComponent<CanvasGroup>();
        if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();

        if (duration <= 0f) { cg.alpha = 1f; return; }

        cg.alpha = 0f;
        float e = 0f;
        while (e < duration)
        {
            e += Time.deltaTime;
            cg.alpha = Mathf.Clamp01(e / duration);
            await Cysharp.Threading.Tasks.UniTask.Yield();
        }
        cg.alpha = 1f;
    }

    private void Awake()
    {
        if (presenter == null)
            presenter = GetComponentInChildren<HudPresenter>(true);

        if (presenter == null)
            Debug.LogError("[HudBootstrapper] presenter is null.");

        _provider ??= new UIHudDataProvider();
        if (hudVisualRoot == null && presenter != null)
            hudVisualRoot = presenter.transform;

        EnsureHudHierarchyVisible();
    }

    private void OnEnable()
    {
        EnsureHudHierarchyVisible();
        if (_run != null)
            BindRun(_run);

        // 타이밍 무관 허브 플레이어 바인딩: 전역 플레이어 채널 구독 + 이미 스폰돼 있으면 즉시.
        // 플레이어/HUD 어느 쪽이 먼저 준비되든 대응한다. 런(전투)은 run 흐름이 바인딩을 소유하므로 _run==null일 때만.
        if (Managers.Player != null)
        {
            Managers.Player.OnPlayerSpawned -= HandleGlobalPlayerSpawned;
            Managers.Player.OnPlayerSpawned += HandleGlobalPlayerSpawned;
        }
        TryBindHubPlayer();
    }

    private void OnDisable()
    {
        if (Managers.Player != null)
            Managers.Player.OnPlayerSpawned -= HandleGlobalPlayerSpawned;
    }

    private void HandleGlobalPlayerSpawned(Transform _) => TryBindHubPlayer();

    private void LateUpdate()
    {
        if (_startRoomSuppressed) return;

        _panelGuardTimer -= Time.unscaledDeltaTime;
        if (_panelGuardTimer > 0f) return;

        _panelGuardTimer = 0.2f;
        // 바운드 보스가 있으면 씬 이름 무관하게 Boss HUD 유지
        if (!IsInGameScene() && (presenter == null || !presenter.HasBoundBoss)) return;

        EnsureHudHierarchyVisible();
        if (presenter != null)
        {
            if (presenter.HasBoundBoss)
            {
                presenter.SetMode(HUDIds.Mode.Boss);
                presenter.RefreshBoundBossPanel();
            }
            else if (TryBindAnyActiveBoss())
            {
                presenter.SetMode(HUDIds.Mode.Boss);
                presenter.RefreshBoundBossPanel();
            }
            else if (_run != null && _run.TryGetHudMode(out var mode))
            {
                var normalized = NormalizeMode(mode);
                if (normalized == HUDIds.Mode.Boss)
                    normalized = HUDIds.Mode.Combat;

                presenter.SetMode(normalized);
            }
            else
            {
                presenter.SetMode(HUDIds.Mode.Combat);
            }
        }

        SyncCombatHpText();
    }

    public void BindRun(GameRunSession run)
    {
        if (presenter == null)
        {
            presenter = GetComponentInChildren<HudPresenter>(true);
        }

        if (presenter == null)
        {
            Debug.LogError("[HudBootstrapper] BindRun failed: presenter is null.");
            return;
        }
        if (hudVisualRoot == null)
            hudVisualRoot = presenter.transform;
        if (run == null)
        {
            Debug.LogError("[HudBootstrapper] BindRun failed: run is null.");
            return;
        }

        EnsureHudHierarchyVisible();
        if (ReferenceEquals(_run, run))
        {
            EnsureHudHierarchyVisible();
            if (_run.TryGetHudMode(out var currentMode))
                presenter.SetMode(ResolveMode(currentMode));
            else if (IsInGameScene())
                presenter.SetMode(HUDIds.Mode.Combat);

            if (_run.TryGetPlayerState(out var currentState) && currentState != null)
                BindStateNow(currentState);

            if (_run.Player != null)
                presenter.BindPlayer(_run.Player);

            return;
        }

        Unbind(); // ✅ 항상 깨끗하게 정리하고 바인딩

        _run = run;

        // 서약 패널 바인딩
        presenter.BindCovenant(_run.CovenantHandler);

        // ✅ 1) HUD 모드 이벤트 구독
        _run.OnHudModeChanged += HandleHudModeChanged;

        // ✅ 2) 현재 모드 즉시 반영(늦게 뜬 HUD도 동기화)
        if (_run.TryGetHudMode(out var mode))
            presenter.SetMode(ResolveMode(mode));
        else if (IsInGameScene())
            presenter.SetMode(HUDIds.Mode.Combat);

        // ✅ 3) PlayerState가 준비되었으면 즉시 Construct
        if (_run.TryGetPlayerState(out var st) && st != null)
        {
            BindStateNow(st);
        }
        else
        {
            // ✅ 4) 아직이면 준비 이벤트 대기
            _run.OnPlayerStateReady += BindStateNow;
        }

        // ✅ 5) 플레이어 스폰 이벤트 구독 (스탯·장비 HUD 연결)
        _run.OnPlayerBound += HandlePlayerBound;

        // 이미 스폰된 경우 즉시 반영
        if (_run.Player != null)
            presenter.BindPlayer(_run.Player);

        EnsureHudHierarchyVisible();
    }

    /// <summary>런이 없을 때(허브) 전역 플레이어를 HUD에 바인딩 — 타이밍 무관(플레이어/HUD 순서 무관, 재스폰 대응).</summary>
    private void TryBindHubPlayer()
    {
        if (_run != null) return;                       // 런은 run 흐름이 바인딩을 소유
        var t = Managers.Player?.PlayerTransform;
        if (t == null) return;
        var pc = t.GetComponent<PlayerController>();
        // 초기화 완료(WeaponManager 준비 = init 완료 신호) 전이면 스킵 — 플레이어 자체 init 중
        // 조기 SetPlayer 대응. 완료 후 재호출되는 SetPlayer에서 정상 바인딩된다(타이밍 무관).
        if (pc == null || pc.WeaponManager == null) return;
        BindPlayerStandalone(pc);
    }

    /// <summary>런 없이(허브) 플레이어만 HUD에 바인딩 — 스탯/무기/버프뷰(유물·룬 패시브) 표시.
    /// 아이템/방버프/서약 소스는 런(Construct)이 있어야 채워지므로 허브에선 플레이어 소스만 활성.</summary>
    private void BindPlayerStandalone(PlayerController player)
    {
        if (presenter == null)
            presenter = GetComponentInChildren<HudPresenter>(true);
        if (presenter == null || player == null) return;

        // 바인딩(데이터 연결)은 항상 해둔다 — 나중에 표시될 때 값이 비어 있으면 안 되므로.
        presenter.BindPlayer(player);

        // 표시는 억제 중이면 하지 않는다.
        // 이 경로는 플레이어 스폰(OnPlayerSpawned)에서 불려, 인트로 컷신 중에도 HUD를 켜버렸다.
        if (_startRoomSuppressed) return;

        EnsureHudHierarchyVisible();
        presenter.SetMode(HUDIds.Mode.Combat);
    }

    private void BindStateNow(PlayerRunState st)
    {
        if (_constructed) return;
        if (_run == null || st == null) return;

        _constructed = true;

        // ✅ 한번만
        _run.OnPlayerStateReady -= BindStateNow;

        _provider.Bind(st);
        presenter.Construct(_run, _provider);
        TryBindAnyActiveBoss();
    }

    private void HandleHudModeChanged(HUDIds.Mode mode)
    {
        if (_startRoomSuppressed) return;   // 억제 중엔 모드 전환으로도 HUD를 켜지 않는다
        EnsureHudHierarchyVisible();
        presenter.SetMode(ResolveMode(mode));
    }

    private void HandlePlayerBound(PlayerController player)
    {
        presenter.BindPlayer(player);
        TryBindAnyActiveBoss();
        presenter?.MinimapView?.SetPlayerTransform(player != null ? player.transform : null);
    }

    private bool TryBindAnyActiveBoss()
    {
        if (presenter == null || presenter.HasBoundBoss) return false;

        var monsters = FindObjectsByType<MonsterBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (monsters == null) return false;

        for (int i = 0; i < monsters.Length; i++)
        {
            var monster = monsters[i];
            if (monster == null || !monster.isActiveAndEnabled) continue;
            if (monster is not IBoss) continue;

            presenter.BindBoss(monster);
            return true;
        }

        return false;
    }

    public void Unbind()
    {
        if (_run != null)
        {
            _run.OnPlayerStateReady -= BindStateNow;
            _run.OnHudModeChanged   -= HandleHudModeChanged;
            _run.OnPlayerBound      -= HandlePlayerBound;
        }

        _constructed = false;

        presenter?.Dispose();
        _provider?.Unbind();
        _run = null;
        _panelGuardTimer = 0f;

        // 데이터를 뗐으면 화면도 내린다. @UIRoot는 DDOL이라 이걸 안 하면 이전 런의 마지막 프레임
        // (보스 패널·서약 목록·골드)이 허브까지 그대로 따라온다.
        // BindRun 선행 호출 경로에서는 곧바로 알맞은 모드로 다시 켜지므로 부작용이 없다.
        presenter?.SetMode(HUDIds.Mode.None);
    }

    private void OnDestroy() => Unbind();

    private void EnsureHudHierarchyVisible()
    {
        if (_startRoomSuppressed) return;

        Transform target = null;
        if (presenter != null)
            target = hudVisualRoot != null ? hudVisualRoot : presenter.transform;
        if (target == null)
            target = transform;

        Transform current = target;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
                current.gameObject.SetActive(true);

            if (current is RectTransform rect)
            {
                var s = rect.localScale;
                if (Mathf.Abs(s.x) < 0.0001f || Mathf.Abs(s.y) < 0.0001f || Mathf.Abs(s.z) < 0.0001f)
                    rect.localScale = Vector3.one;
            }

            current = current.parent;
        }

        if (presenter != null && !presenter.gameObject.activeSelf)
            presenter.gameObject.SetActive(true);

        // Hard guard: in GameScene we always need combat panel to be visible.
        var panelCombat = FindPanelCombatInUIRoot();
        if (panelCombat != null && !panelCombat.gameObject.activeSelf)
            panelCombat.gameObject.SetActive(true);

        if (panelCombat != null)
        {
            ForceCanvasGroupVisible(panelCombat);
            ForceTextsVisible(panelCombat);
        }
    }

    private static HUDIds.Mode NormalizeMode(HUDIds.Mode requestedMode)
    {
        if (IsInGameScene() && requestedMode == HUDIds.Mode.None)
            return HUDIds.Mode.Combat;

        return requestedMode;
    }

    private HUDIds.Mode ResolveMode(HUDIds.Mode requestedMode)
    {
        var normalized = NormalizeMode(requestedMode);

        // 보스가 바인딩된 상태에서 Combat 요청이 오면 Boss 모드 유지
        if (normalized == HUDIds.Mode.Combat && presenter != null && presenter.HasBoundBoss)
            return HUDIds.Mode.Boss;

        if (normalized == HUDIds.Mode.Boss && (presenter == null || !presenter.HasBoundBoss))
            return HUDIds.Mode.Combat;

        return normalized;
    }

    private static bool IsInGameScene()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(sceneName) &&
               sceneName.IndexOf("GameScene", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Transform FindPanelCombatInUIRoot()
    {
        var uiRoot = UIRootBootstrapper.Instance;
        if (uiRoot != null)
        {
            var foundInUiRoot = FindChildRecursive(uiRoot.transform, "Panel_Combat");
            if (foundInUiRoot != null)
                return foundInUiRoot;
        }

        return FindChildRecursive(null, "Panel_Combat");
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name != childName) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                return t;
            }
            return null;
        }

        if (root == null) return null;
        if (root.name == childName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }

    private static void ForceCanvasGroupVisible(Transform from)
    {
        if (from == null) return;

        var groups = from.GetComponentsInParent<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            var g = groups[i];
            if (g == null) continue;
            g.alpha = 1f;
            g.interactable = true;
            g.blocksRaycasts = true;
        }
    }

    private static void ForceTextsVisible(Transform from)
    {
        if (from == null) return;

        var texts = from.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            if (t == null) continue;

            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);

            var c = t.color;
            if (c.a < 0.95f)
            {
                c.a = 1f;
                t.color = c;
            }
        }
    }

    private void SyncCombatHpText()
    {
        var panelCombat = FindPanelCombatInUIRoot();
        if (panelCombat == null) return;

        var hpTextTransform = FindChildRecursive(panelCombat, "HUD_HpText");
        if (hpTextTransform == null)
            hpTextTransform = FindChildRecursive(panelCombat, "Txt_HP");
        if (hpTextTransform == null)
            return;

        var hpText = hpTextTransform.GetComponent<TextMeshProUGUI>();
        if (hpText == null)
            return;

        if (!hpText.gameObject.activeSelf)
            hpText.gameObject.SetActive(true);

        var c = hpText.color;
        if (c.a < 0.95f)
        {
            c.a = 1f;
            hpText.color = c;
        }

        int hp = 0;
        int maxHp = 0;
        bool hasValue = false;

        if (_run?.Player != null && _run.Player.RuntimeStats != null)
        {
            hp = _run.Player.RuntimeStats.Hp;
            maxHp = _run.Player.RuntimeStats.MaxHp;
            hasValue = true;
        }
        else if (_run != null && _run.TryGetPlayerState(out var state) && state != null)
        {
            hp = state.Hp;
            maxHp = state.MaxHp;
            hasValue = true;
        }
        else
        {
            var playerTransform = Managers.Player?.PlayerTransform;
            var player = playerTransform != null ? playerTransform.GetComponent<PlayerController>() : null;
            if (player != null && player.RuntimeStats != null)
            {
                hp = player.RuntimeStats.Hp;
                maxHp = player.RuntimeStats.MaxHp;
                hasValue = true;
            }
        }

        if (!hasValue) return;

        hpText.text = $"{Mathf.Max(0, hp)} / {Mathf.Max(1, maxHp)}";
    }
}
