using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using RelicFairy.Monster;

public sealed class HudBootstrapper : MonoBehaviour
{
    private const float CutsceneReturnFade = 0.35f;   // 컷신(HUD 모두 걷음)에서 돌아올 때 페이드

    [SerializeField] private HudPresenter presenter;
    [SerializeField] private Transform hudVisualRoot;

    private UIHudDataProvider _provider;
    private GameRunSession _run;
    private float _panelGuardTimer;
    private bool _startRoomSuppressed;
    private bool  _popupHidden;        // 게임을 멈추는 팝업이 열려 있다 — HUD를 걷는다(09-28)
    private float _fadeAlpha = 1f;     // 시작방 페이드가 정한 알파(팝업이 닫히면 이 값으로 돌아간다)
    private float _popupDim  = 1f;     // 1 = 보임, 0 = 팝업에 걷힘 — 사이 값은 걷히는/돌아오는 중(0.12/0.16초)
    private CancellationTokenSource _popupDimCts;
    private HUDIds.Mode _lastRunMode;                  // 런이 마지막으로 요청한 모드 — 컷신에서 돌아오는 순간을 안다
    private CancellationTokenSource _cutsceneFadeCts;

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

    /// <summary>
    /// 억제 해제 + 알파 페이드 인. <see cref="SetStartRoomSuppressed"/>(false)와 같은 표시 규칙(모드는 건드리지 않음)에
    /// 알파만 0→1로 올린다 — 허브에서 검을 쥔 뒤 HUD 전체가 한 프레임에 튀어나오던 것을 없앤다.
    /// 전투 진입용 <see cref="FadeInStartRoomAsync"/>와 달리 Combat 모드를 강제하지 않는다.
    /// </summary>
    public async Cysharp.Threading.Tasks.UniTask RevealStartRoomAsync(float duration)
    {
        var target = hudVisualRoot != null ? hudVisualRoot : presenter?.transform;
        CanvasGroup cg = null;
        if (target != null)
        {
            cg = target.GetComponent<CanvasGroup>();
            if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();
            ApplyHudAlpha(cg, 0f);   // 켜기 전에 투명 — 첫 프레임 팝 방지
        }

        SetStartRoomSuppressed(false);
        if (cg == null) return;
        if (duration <= 0f) { ApplyHudAlpha(cg, 1f); return; }

        float e = 0f;
        while (e < duration)
        {
            if (cg == null) return;   // 씬 전환으로 파괴
            e += Time.deltaTime;
            ApplyHudAlpha(cg, Mathf.Clamp01(e / duration));
            await Cysharp.Threading.Tasks.UniTask.Yield();
        }
        if (cg != null) ApplyHudAlpha(cg, 1f);
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

        if (duration <= 0f) { ApplyHudAlpha(cg, 1f); return; }

        ApplyHudAlpha(cg, 0f);
        float e = 0f;
        while (e < duration)
        {
            e += Time.deltaTime;
            ApplyHudAlpha(cg, Mathf.Clamp01(e / duration));
            await Cysharp.Threading.Tasks.UniTask.Yield();
        }
        ApplyHudAlpha(cg, 1f);
    }

    /// <summary>
    /// 게임을 멈추는 팝업(BlocksGameplay)이 열려 있는 동안 HUD를 걷는다 — 막이 없는 팝업 가장자리에 무기·스킬 칸이 걸치고
    /// 반투명 창(기억의 제단) 안으로 비쳤다(09-28 UI 전수). 활성 상태는 시작방 억제가, 알파는 페이드가 쓰므로
    /// 알파만 <see cref="ApplyHudAlpha"/>로 합쳐 정한다(보스 대사 UI_BossBark는 HUD 루트 밖이라 남는다).
    /// </summary>
    public void SetPopupHidden(bool hidden)
    {
        bool same = hidden == _popupHidden;
        _popupHidden = hidden;
        var cg = HudGroup();
        if (cg == null) { _popupDim = hidden ? 0f : 1f; return; }
        cg.blocksRaycasts = !hidden;

        float target = hidden ? 0f : 1f;
        if (same && (_popupDimCts != null || Mathf.Approximately(_popupDim, target))) return;   // 이미 그쪽으로 가는 중 · 도착

        // 순간 소멸은 팝업(0.16초에 걸쳐 나타남)보다 HUD가 먼저 튀어 「원색·순간 등장 금지」에 걸렸다(09-28 UI 톤 진단).
        _popupDimCts?.Cancel();
        _popupDimCts?.Dispose();
        _popupDimCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        FadePopupDimAsync(cg, target, hidden ? UIFader.CloseSec : UIFader.OpenSec, _popupDimCts).Forget();
    }

    /// <summary>페이드 알파를 기록하고, 팝업 걷힘 정도를 곱해 건다.</summary>
    private void ApplyHudAlpha(CanvasGroup cg, float fadeAlpha)
    {
        _fadeAlpha        = fadeAlpha;
        cg.alpha          = fadeAlpha * _popupDim;
        cg.blocksRaycasts = !_popupHidden;
    }

    private CanvasGroup HudGroup()
    {
        var target = hudVisualRoot != null ? hudVisualRoot : presenter != null ? presenter.transform : null;
        if (target == null) return null;
        var cg = target.GetComponent<CanvasGroup>();
        return cg != null ? cg : target.gameObject.AddComponent<CanvasGroup>();
    }

    private async UniTaskVoid FadePopupDimAsync(CanvasGroup cg, float target, float dur, CancellationTokenSource cts)
    {
        try
        {
            while (!Mathf.Approximately(_popupDim, target))
            {
                _popupDim = Mathf.MoveTowards(_popupDim, target, Time.unscaledDeltaTime / dur);
                if (cg == null) return;
                cg.alpha = _fadeAlpha * _popupDim;
                await UniTask.Yield(PlayerLoopTiming.Update, cts.Token);
            }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (_popupDimCts == cts) { _popupDimCts.Dispose(); _popupDimCts = null; }
        }
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

        // 컷신 모드(봉인 의식 · 보스 페이즈 전환)는 요청한 쪽이 끝낼 때까지 유지한다 — 되돌리면 전투 HUD가 다시 뜨고 페이즈 바 차오름이 끊긴다.
        // EnsureHudHierarchyVisible보다 먼저 본다(그 안의 하드 가드가 전투 패널을 강제로 켠다).
        if (_run != null && _run.TryGetHudMode(out var held) &&
            (held == HUDIds.Mode.Cutscene || held == HUDIds.Mode.BossCutscene))
            return;

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
        // 보스 HP바만 남기는 페이즈 전환(BossCutscene)은 바가 떠 있으므로 페이드하지 않는다 — 전부 걷었던 컷신만.
        bool fromCutscene = _lastRunMode == HUDIds.Mode.Cutscene && mode != HUDIds.Mode.Cutscene;
        _lastRunMode = mode;
        if (_startRoomSuppressed) return;   // 억제 중엔 모드 전환으로도 HUD를 켜지 않는다
        EnsureHudHierarchyVisible();
        presenter.SetMode(ResolveMode(mode));
        if (fromCutscene) FadeBackFromCutsceneAsync().Forget();
    }

    /// <summary>
    /// 컷신에서 돌아올 때 알파 0→1 — 무기 · 스킬 칸이 한 프레임에 튀어나오지 않게(「원색 · 순간 등장 금지」, 10-01 f5 전주기 시뮬).
    /// 알파는 <see cref="ApplyHudAlpha"/>로 건다 — 0.2초마다 도는 하드 가드가 <c>_fadeAlpha</c>를 보고 다시 건다.
    /// </summary>
    private async UniTaskVoid FadeBackFromCutsceneAsync()
    {
        var cg = HudGroup();
        if (cg == null) return;

        _cutsceneFadeCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        _cutsceneFadeCts = cts;
        try
        {
            for (float e = 0f; e < CutsceneReturnFade; e += Time.unscaledDeltaTime)
            {
                if (cg == null) return;
                ApplyHudAlpha(cg, e / CutsceneReturnFade);
                await UniTask.Yield(PlayerLoopTiming.Update, cts.Token);
            }
            if (cg != null) ApplyHudAlpha(cg, 1f);
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (_cutsceneFadeCts == cts) _cutsceneFadeCts = null;
            cts.Dispose();
        }
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

        // 위 하드 가드는 전투 패널 부모의 CanvasGroup 알파를 전부 1로 되돌린다 — 차단 팝업이 떠 있으면 숨김을 다시 건다
        // (런 안에서만 0.2초마다 돌아, 팝업 동안 걷은 HUD가 되살아났다 — 09-28 UI 전수). 페이드 중(컷신 복귀 · 시작방)도 같다.
        if (_popupHidden || _popupDim < 1f || _fadeAlpha < 1f)
        {
            var cg = HudGroup();
            if (cg != null) { cg.alpha = _fadeAlpha * _popupDim; cg.blocksRaycasts = !_popupHidden; }
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

    /// <summary>
    /// 수명을 스스로 관리하는 <b>일시 알림</b> 텍스트인지. 이런 건 하드가드가 건드리면 안 된다.
    ///
    /// ForceTextsVisible은 "HUD 텍스트가 꺼져 있으면 무조건 켠다"는 무딘 보정이라,
    /// 만료돼 꺼둔 안내 문구까지 되살려 화면에 영구히 남겼다(방 전환마다 재현).
    /// 페이드 중인 알림의 알파를 1로 되돌려 연출을 끊는 문제도 같이 있었다.
    /// 이 오브젝트들은 CombatPanelView가 코드로 만들므로 이름이 곧 계약이다.
    /// </summary>
    private static bool IsSelfManagedNotice(string goName)
        => goName.StartsWith("BuffNoticeText", System.StringComparison.Ordinal)
        || goName.StartsWith("ItemNotice", System.StringComparison.Ordinal);

    private static void ForceTextsVisible(Transform from)
    {
        if (from == null) return;

        var texts = from.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            if (t == null) continue;
            if (IsSelfManagedNotice(t.gameObject.name)) continue;

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
