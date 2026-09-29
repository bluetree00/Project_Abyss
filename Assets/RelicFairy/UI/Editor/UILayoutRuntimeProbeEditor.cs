using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <b>플레이 중</b> 콘텐츠 팝업을 하나씩 실제로 열어 레이아웃을 재고 JSON으로 떨군다.
///
/// <para>왜 따로 있나: <see cref="UILayoutProbeEditor"/>(에디트 모드)는 프리팹만 잰다.
/// 그런데 19개 UI 스크립트가 런타임에 배치를 바꾼다 — <c>UIWindowFitter</c>가 창을 화면의 94%까지 키우고,
/// <c>ApplySkin</c>이 아트를 갈아끼우고(서약 두루마리 등), 카드·행처럼 데이터로 만들어지는 내용은
/// 프리팹에 없다. 그래서 프리팹 실측이 "맞다"고 해도 게임 화면은 어긋날 수 있다.
/// 이 프로브는 <c>Managers.UI.ShowPopupUIAndGetAsync</c>로 진짜 경로를 타서 Init·스킨·fitter가
/// 다 돈 뒤의 사각형을 <b>루트 캔버스 좌표</b>로 기록한다. JSON 스키마는 에디트 모드 프로브와 같다.</para>
///
/// <para>쓰는 법: 아무 씬에서 플레이 → 메뉴 실행. 앱은 어느 씬에서든 <c>AppBootstrapper</c>가 스스로 부팅한다.
/// 런 컨텍스트가 없어 <c>Setup</c>을 못 받는 팝업은 빈 상태로 열린다(레이아웃 골격은 그대로 잰다).</para>
/// </summary>
public static class UILayoutRuntimeProbeEditor
{
    private const string OutFile = "ui_layout_probe_runtime.json";
    private const int SettleFrames = 3;

    // 열 팝업 — Addressable "UI/Popup/<이름>"에 등록된 12종. 이름은 곧 주소 키다.
    private static readonly (string name, Func<UniTask<UI_Popup>> open)[] Popups =
    {
        ("UI_ShopPanel",           async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>()),
        ("UI_CruciblePanel",       async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>()),
        ("UI_RefineryPanel",       async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>()),
        ("UI_RuneSelectPopup",     async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>()),
        ("UI_RelicInfoPopup",      async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicInfoPopup>()),
        ("UI_RangedForgePopup",    async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>()),
        ("UI_CovenantAssemble",    async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_CovenantAssemble>()),
        ("UI_AwakeningPanel",      async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>()),
        ("UI_DialoguePopup",       async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>()),
        ("UI_RelicPartDraftPopup", async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>()),
    };

    [MenuItem("RelicFairy/UI/레이아웃 실측 덤프 (런타임·플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 모드에서 실행해야 한다 — 실제 Init·스킨·fitter를 타야 게임과 같은 값이 나온다.");
            return;
        }
        if (Managers.UI == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] Managers.UI가 아직 없다 — 부팅이 끝난 뒤 다시 실행.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        // 플레이 직후엔 씬 진입 페이드가 화면을 검게 덮고 있다 — 처음 두 화면(상점·재련소)이 검게 찍힌 원인. 걷힐 때까지 기다린다.
        await UniTask.Delay(16000, ignoreTimeScale: true);
        HideTestHubGui();
        // 팝업을 열기 전 화면을 한 장 남긴다 — 첫 화면이 검게 나오면 팝업 탓인지 씬 덮개(페이드·로딩 커버) 탓인지 가른다.
        await ShotAsync("_baseline");

        var sb = new StringBuilder(1 << 20);
        sb.Append("{\"screens\":[");
        bool firstScreen = true;
        int screens = 0;

        foreach (var (name, open) in Popups)
        {
            UI_Popup popup = null;
            try
            {
                popup = await open();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                Debug.LogWarning($"[UILayoutRuntimeProbe] {name} 열기 실패 — {e.GetType().Name}: {e.Message}");
            }
            if (popup == null) continue;

            try
            {
                // 데이터가 있어야 카드·태그·아이콘이 그려지는 팝업은 실제 데이터로 Setup을 돌린 뒤 잰다 — 빈 상태는 대표성이 없다.
                FeedSampleData(popup);
                await UniTask.DelayFrame(SettleFrames);   // Start·fitter·스킨 스왑이 끝나도록
                await ShotAsync(name);
                if (Dump(popup.transform as RectTransform, name, sb, ref firstScreen)) screens++;

                // 기억의 제단은 기본이 해금 탭이다 — 업적 탭은 SetMode(true)로 켜서 한 번 더 잰다(합성본 업적3과 대조용).
                if (popup is UI_AwakeningPanel)
                {
                    // 해금 연출(기획 §7-1) 미리보기 — 데이터를 건드리지 않고 연출 메서드만 돌려 두 장 찍는다.
                    await PreviewUnlockFxAsync(popup, name);

                    var setMode = typeof(UI_AwakeningPanel).GetMethod("SetMode",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (setMode != null)
                    {
                        setMode.Invoke(popup, new object[] { true });
                        await UniTask.DelayFrame(SettleFrames);
                        await ShotAsync(name + "#achieve");
                        if (Dump(popup.transform as RectTransform, name + "#achieve", sb, ref firstScreen)) screens++;
                    }
                }

                // 재련소는 탭이 둘이다 — 원거리 탭은 클릭으로만 켜지므로 같은 경로(SelectTab)를 눌러 한 번 더 잰다.
                if (popup is UI_CruciblePanel)
                {
                    var selectTab = typeof(UI_CruciblePanel).GetMethod("SelectTab",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (selectTab != null)
                    {
                        selectTab.Invoke(popup, new object[] { 1 });
                        await UniTask.DelayFrame(SettleFrames);
                        await ShotAsync(name + "#ranged");
                        if (Dump(popup.transform as RectTransform, name + "#ranged", sb, ref firstScreen)) screens++;
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                Debug.LogWarning($"[UILayoutRuntimeProbe] {name} 실측 실패 — {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                // ClosePopupUI(popup)는 스택 맨 위가 아니면 "Close Popup Failed!"로 무시된다 — 기억의 제단처럼 여는 즉시
                // 자기 대사 팝업을 위에 얹는 화면이 그렇다. 실측은 화면마다 깨끗한 스택에서 시작해야 하므로 전부 닫는다.
                try { Managers.UI.CloseAllPopupUI(); }
                catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] {name} 닫기 실패 — {e.Message}"); }
            }
            // 닫기가 페이드·지연 파괴면 다음 화면 스크린샷에 겹쳐 찍힌다 — 실제로 사라질 때까지(최대 60프레임) 기다린다.
            for (int i = 0; i < 60 && popup != null && popup.gameObject.activeInHierarchy; i++)
                await UniTask.DelayFrame(1);
            await UniTask.DelayFrame(2);
        }

        // HUD는 씬에 상주하는 캔버스(@UIRoot/Canvas_HUD)다 — 팝업 경로를 안 타므로 직접 찾아 잰다.
        try
        {
            var hud = GameObject.Find("Canvas_HUD");
            await ShotAsync("HUD_Canvas");
            if (hud != null && hud.transform is RectTransform hrt && Dump(hrt, "HUD_Canvas", sb, ref firstScreen)) screens++;
            else Debug.LogWarning("[UILayoutRuntimeProbe] Canvas_HUD를 찾지 못했다(비활성이거나 씬에 없음).");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] HUD 실측 실패 — {e.Message}"); }

        // 베이스캠프의 HUD는 <b>로비 모드</b>(TopBar만)다 — 런에서 보이는 전투 패널·서약·미니맵을 재려면
        // 게임과 같은 경로인 HudPresenter.SetMode(Combat)로 켠다(HudPresenter.ResolveSections가 조합을 정한다).
        // 슬롯 값은 런타임에 덮어써지므로(프리팹 값은 죽은 데이터) 실제 자릿수의 표본을 먹여야 넘침이 드러난다.
        try
        {
            var presenter = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
            var hudView   = UnityEngine.Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
            if (presenter != null && hudView != null)
            {
                presenter.SetMode(HUDIds.Mode.Combat);
                await UniTask.DelayFrame(SettleFrames);
                FeedHudSample(hudView);
                await UniTask.DelayFrame(SettleFrames);

                var hudCombat = GameObject.Find("Canvas_HUD");
                await ShotAsync("HUD_Canvas#combat");
                if (hudCombat != null && hudCombat.transform is RectTransform hcrt
                    && Dump(hcrt, "HUD_Canvas#combat", sb, ref firstScreen)) screens++;

                presenter.SetMode(HUDIds.Mode.Lobby);   // 다음 화면 실측이 전투 HUD를 배경에 깔지 않도록 되돌린다
                await UniTask.DelayFrame(2);
            }
            else Debug.LogWarning("[UILayoutRuntimeProbe] HudPresenter/HudView가 없어 런 HUD 실측을 건너뛴다.");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] 런 HUD 실측 실패 — {e.Message}"); }

        // 룬 그리드.
        // ⚠️ ShowOverlayUI는 gameObject.SetActive(true)만 한다 — <b>판(육각 셀)을 짓는 것은 Open()</b>(OpenPanel)이다.
        //    예전 실측이 중앙을 빈 채로 찍고 "판은 런에서만 생긴다"고 본 것은 이 경로를 안 탔기 때문이다.
        //    순서도 게임과 같아야 한다: 판(Puzzle)을 먼저 세워야 Open()이 BoardManager.Instance를 만나
        //    EnterExternalGrid로 육각 판과 묶는다(런에서는 GameRunBootstrapper가 먼저 세운다).
        try
        {
            Managers.UI.ShowOverlayUI<UI_GridPanel>();          // 인스턴스만 확보한다(Instance 세팅)
            await UniTask.DelayFrame(SettleFrames);
            var grid = UI_GridPanel.Instance;
            if (grid != null && grid.transform is RectTransform grt)
            {
                // ① 베이스캠프엔 런 인벤토리가 없다 — 표본 룬 5개짜리 보관함을 꽂아 카드·아이콘까지 잰다.
                System.Reflection.MethodInfo onStagingChanged = null;
                try
                {
                    var inv = new RunItemInventory();
                    foreach (var (data, _) in SampleRunes(5)) inv.AddToStaging(data);
                    var invField = typeof(UI_GridPanel).GetField("_inventory",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    onStagingChanged = typeof(UI_GridPanel).GetMethod("OnStagingChanged",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (invField != null && inv.StagingCount > 0)
                    {
                        invField.SetValue(grid, inv);
                        onStagingChanged?.Invoke(grid, null);
                        await UniTask.DelayFrame(SettleFrames);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 그리드 표본 보관함 실패: " + e.Message); }

                // ② 판을 세운다 — GameRunBootstrapper가 런 시작에 부르는 그 메서드다.
                try
                {
                    var bridge = MerlinRuneBridge.Instance
                              ?? UnityEngine.Object.FindFirstObjectByType<MerlinRuneBridge>(FindObjectsInactive.Include);
                    if (bridge != null)
                    {
                        bridge.InitializeGridsFromServer();
                        for (int i = 0; i < 180 && BoardManager.Instance == null; i++)
                            await UniTask.DelayFrame(1);
                        if (BoardManager.Instance == null)
                            Debug.LogWarning("[UILayoutRuntimeProbe] 룬판(BoardManager)이 서지 않았다 — 룬 데이터 초기화를 보라.");
                    }
                    else Debug.LogWarning("[UILayoutRuntimeProbe] MerlinRuneBridge를 찾지 못했다(@UIRoot 확인).");
                }
                catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 룬판 스폰 실패: " + e.Message); }

                // ③ 게임과 같은 경로로 연다 — 여기서 육각 셀이 지어지고 판과 묶인다.
                grid.Open();
                await UniTask.DelayFrame(SettleFrames * 3);
                onStagingChanged?.Invoke(grid, null);           // 판을 만난 뒤라야 카드 모양 미리보기가 실물로 그려진다
                await UniTask.DelayFrame(SettleFrames);

                await ShotAsync("UI_GridPanel");
                if (Dump(grt, "UI_GridPanel", sb, ref firstScreen)) screens++;
                grid.Close();
            }
            else Debug.LogWarning("[UILayoutRuntimeProbe] UI_GridPanel.Instance가 없다.");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] 룬 그리드 실측 실패 — {e.Message}"); }
        await UniTask.DelayFrame(2);

        // 설정 화면은 팝업 스택 밖(자체 캔버스)이라 따로 연다.
        try
        {
            UI_Settings.Open();
            await UniTask.DelayFrame(SettleFrames);
            var settings = UnityEngine.Object.FindFirstObjectByType<UI_Settings>();
            // 설정은 자기 캔버스(SettingsRoot)를 따로 세운다 — 그 캔버스 좌표로 재야 팝업 캔버스 배율에 안 섞인다.
            var settingsRoot = settings != null ? settings.transform.Find("SettingsRoot") as RectTransform : null;
            await ShotAsync("UI_Settings");
            if (settingsRoot != null && Dump(settingsRoot, "UI_Settings", sb, ref firstScreen)) screens++;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] UI_Settings 실측 실패 — {e.Message}"); }
        finally { UI_Settings.CloseIfOpen(); }

        // 표본용 임시 오브젝트 정리 — 남기면 다음 화면 실측의 배경에 섞인다.
        for (int i = 0; i < _tempFeedObjects.Count; i++)
            if (_tempFeedObjects[i] != null) UnityEngine.Object.Destroy(_tempFeedObjects[i]);
        _tempFeedObjects.Clear();

        sb.Append("]}");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, OutFile);
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[UILayoutRuntimeProbe] {screens}개 화면 런타임 실측 완료 → {outPath}  (화면 {Screen.width}×{Screen.height})");
        RestoreTestHubGui();
    }

    // 테스트 허브(BaseCamp_Test)의 선택 패널은 IMGUI(OnGUI)라 모든 uGUI 위에 그려진다 —
    // 켜 둔 채 찍으면 화면 한가운데를 통째로 가려(09-19 전 화면) 판정이 불가능했다. 실측 동안만 끈다.
    private static TestHubLauncher s_hiddenHub;

    private static void HideTestHubGui()
    {
        var hub = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (hub == null || !hub.enabled) return;
        hub.enabled = false;
        s_hiddenHub = hub;
    }

    private static void RestoreTestHubGui()
    {
        if (s_hiddenHub != null) s_hiddenHub.enabled = true;
        s_hiddenHub = null;
    }

    // ── 보상 줍기(방 클리어 → 획득 화면) ────────────────────────────────
    /// <summary>
    /// [런 중] 방을 깨면 바닥에 남는 <see cref="ClearRewardTrigger"/>로 플레이어를 옮겨 <b>실제로 줍는다</b>.
    /// 어떤 팝업이 떴는지 이름으로 남기고 화면을 찍는다.
    ///
    /// 자동 실측 런(런 구조)은 몹을 잡고 곧장 출구로 가므로 이 트리거를 지나친다 —
    /// 그래서 이 경로만 검증 공백이었다. 보상이 생길 때까지 기다렸다가 한 번 줍는다.
    /// </summary>
    [MenuItem("RelicFairy/UI/보상 줍기 실측 — 클리어 보상 (런 중)")]
    private static void RunClearReward()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 모드에서 실행해야 한다."); return; }
        RunClearRewardAsync().Forget();
    }

    private static async UniTaskVoid RunClearRewardAsync()
    {
        HideTestHubGui();
        try
        {
            // 1) 보상이 바닥에 생길 때까지 기다린다(방을 깨는 것은 자동 실측이나 사람이 한다).
            ClearRewardTrigger reward = null;
            for (int i = 0; i < 240 && reward == null; i++)
            {
                reward = UnityEngine.Object.FindFirstObjectByType<ClearRewardTrigger>(FindObjectsInactive.Exclude);
                if (reward == null) await UniTask.Delay(1000, ignoreTimeScale: true);
            }
            if (reward == null) { Debug.LogWarning("[보상실측] 4분 동안 보상 오브젝트가 안 생겼다 — 방을 깨야 한다."); return; }

            Debug.Log($"[보상실측] 보상 발견 {reward.name} pos={reward.transform.position}");

            // 2) 플레이어를 그 위로. 트리거는 근접해야 열린다(SphereCollider).
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) { Debug.LogWarning("[보상실측] 런 플레이어가 없다."); return; }

            Vector3 dest = reward.transform.position;
            if (UnityEngine.AI.NavMesh.SamplePosition(dest, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) dest = hit.position;
            player.transform.position = dest;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = dest; rb.linearVelocity = Vector3.zero; }

            // 3) 팝업이 뜨기를 기다린다(연출 + 대기 시간 여유).
            UI_Popup top = null;
            for (int i = 0; i < 40 && top == null; i++)
            {
                await UniTask.Delay(250, ignoreTimeScale: true);
                foreach (var pop in UnityEngine.Object.FindObjectsByType<UI_Popup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (pop == null || !pop.gameObject.activeInHierarchy) continue;
                    top = pop; break;
                }
            }

            if (top == null)
            {
                Debug.LogWarning("[보상실측] 10초 동안 팝업이 안 떴다 — 주워지지 않았거나 경로가 다르다.");
                await ShotAsync("Reward_NoPopup", 0);
                return;
            }

            Debug.Log($"[보상실측] ★ 뜬 팝업 = {top.GetType().Name}");
            await UniTask.DelayFrame(SettleFrames * 3);
            await ShotAsync($"Reward_{top.GetType().Name}", 400);
            Debug.Log("[보상실측] 끝 — 캡처 저장");
        }
        catch (Exception e) { Debug.LogWarning("[보상실측] 실패: " + e); }
        finally { RestoreTestHubGui(); }
    }

    // ── 월드 상호작용 프롬프트([F] 유물 선택 등) ─────────────────────────────
    /// <summary>
    /// 지금 씬에서 프롬프트(<c>_promptGo</c>)를 가진 상호작용물 앞으로 플레이어를 옮겨, 프롬프트가 뜨는지·
    /// 화면 어디에 어떤 크기로 뜨는지·다른 UI와 겹치는지를 캡처와 로그로 남긴다. 트리거 진입으로 켜지는 구조라
    /// 가까이 세워 두기만 하면 게임과 같은 경로로 켜진다. 빌드 BaseCamp에서 돌린다(허브면 BaseCamp로 이동).
    /// </summary>
    [MenuItem("RelicFairy/UI/레이아웃 실측 — 월드 프롬프트 (플레이 중)")]
    private static void RunWorldPrompts()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 모드에서 실행해야 한다."); return; }
        RunWorldPromptsAsync().Forget();
    }

    private static async UniTaskVoid RunWorldPromptsAsync()
    {
        HideTestHubGui();
        // 테스트 허브에도 제단이 있지만 배치가 임시라(벽에 가려진 각성 제단 등) 판정이 안 된다 — 빌드 BaseCamp에서 잰다.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "BaseCamp")
        {
            RestoreTestHubGui();
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.BaseCamp);
            for (int i = 0; i < 300 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "BaseCamp"; i++)
                await UniTask.Delay(100, ignoreTimeScale: true);
            await UniTask.Delay(6000, ignoreTimeScale: true);   // 도착 연출·복귀 대사가 지나가도록
        }
        Managers.UI.CloseAllPopupUI();   // 복귀 대사 등이 떠 있으면 시간이 멈춰 트리거가 안 돈다
        await UniTask.Delay(500, ignoreTimeScale: true);

        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var cam = Camera.main;
        if (player == null || cam == null) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이어·카메라 없음"); return; }

        var targets = new List<MonoBehaviour>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            var f = mb.GetType().GetField("_promptGo", NonPub);
            if (f != null && f.FieldType == typeof(GameObject)) targets.Add(mb);
        }
        targets.Sort((a, b) => string.CompareOrdinal(a.GetType().Name + a.name, b.GetType().Name + b.name));
        Debug.Log($"[UILayoutRuntimeProbe] 월드 프롬프트 대상 {targets.Count}개: " + string.Join(", ", targets.ConvertAll(t => t.GetType().Name + "/" + t.name)));

        var home = player.transform.position;
        int n = 0;
        foreach (var t in targets)
        {
            if (t == null) continue;
            try
            {
                Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                Vector3 dest = t.transform.position - fwd * 1.8f;
                if (UnityEngine.AI.NavMesh.SamplePosition(dest, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) dest = hit.position;
                player.transform.position = dest;
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = dest; rb.linearVelocity = Vector3.zero; }
                await UniTask.Delay(900, ignoreTimeScale: true);

                var go = t.GetType().GetField("_promptGo", NonPub)?.GetValue(t) as GameObject;
                string info = "프롬프트 없음";
                if (go != null)
                {
                    var tmp = go.GetComponentInChildren<TMPro.TMP_Text>(true);
                    string rect = "-";
                    if (tmp != null && go.activeInHierarchy)
                    {
                        var b = tmp.bounds; var c = tmp.transform.TransformPoint(b.center);
                        var p0 = cam.WorldToScreenPoint(tmp.transform.TransformPoint(b.min));
                        var p1 = cam.WorldToScreenPoint(tmp.transform.TransformPoint(b.max));
                        var pc = cam.WorldToScreenPoint(c);
                        rect = $"화면 중심({pc.x:0},{Screen.height - pc.y:0}) 폭 {Mathf.Abs(p1.x - p0.x):0}px 높이 {Mathf.Abs(p1.y - p0.y):0}px 깊이 {pc.z:0.0}";
                    }
                    info = $"켜짐={go.activeInHierarchy} 「{(tmp != null ? tmp.text : "")}」 {rect}";
                }
                string tag = $"World_{t.GetType().Name}_{n}";
                await ShotAsync(tag, 0);
                Debug.Log($"[UILayoutRuntimeProbe] {tag} ({t.name}) — {info}");
                n++;
            }
            catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] {t.name} 실패: {e.Message}"); }
        }
        player.transform.position = home;
        if (player.TryGetComponent<Rigidbody>(out var rbh)) rbh.position = home;
        RestoreTestHubGui();
        Debug.Log($"[UILayoutRuntimeProbe] 월드 프롬프트 {n}개 실측 끝");
    }

    // ── 설명 UI 가독성(재화 툴팁 · 룬 카드) ─────────────────────────────────
    /// <summary>
    /// 마우스를 올려야 뜨는 설명 UI를 호버 흉내로 띄워 찍는다 — HUD 재화 툴팁 4종.
    /// 룬 보관함 카드는 룬판을 열어 같이 찍는다(글자가 룬 아트 위에서 읽히는지).
    /// </summary>
    [MenuItem("RelicFairy/UI/설명 UI 실측 — 재화 툴팁·룬 카드 (플레이 중)")]
    private static void RunTooltipReadability()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중에 실행한다."); return; }
        RunTooltipReadabilityAsync().Forget();
    }

    private static async UniTaskVoid RunTooltipReadabilityAsync()
    {
        HideTestHubGui();
        try
        {
            // ① 재화 툴팁 — 칸마다 호버를 넣었다 뺀다.
            var triggers = UnityEngine.Object.FindObjectsByType<UITooltipTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Debug.Log($"[UILayoutRuntimeProbe] 툴팁 대상 {triggers.Length}개");
            var ptr = new PointerEventData(EventSystem.current);
            int n = 0;
            foreach (var t in triggers)
            {
                if (t == null) continue;
                t.OnPointerEnter(ptr);
                await UniTask.Delay(450, ignoreTimeScale: true);
                await ShotAsync($"Tooltip_{n}_{t.name}", 0);
                t.OnPointerExit(ptr);
                await UniTask.Delay(120, ignoreTimeScale: true);
                if (++n >= 4) break;   // 재화 4칸이면 충분
            }

            // ② 룬 보관함 카드 — 룬판을 열어 카드 글자를 본다(표본 3장).
            var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
            var added = new List<RuntimeItemData>();
            if (inv != null && inv.StagingCount == 0)
                foreach (var (data, _) in SampleRunes(3)) if (inv.AddToStaging(data)) added.Add(data);
            if (UI_GridPanel.Instance == null) Managers.UI.ShowOverlayUI<UI_GridPanel>();
            await UniTask.DelayFrame(SettleFrames);
            var grid = UI_GridPanel.Instance;
            if (grid != null)
            {
                grid.Open();
                await UniTask.DelayFrame(SettleFrames * 3);
                await ShotAsync("Tooltip_GridCards", 300);
                grid.Close();
            }
            foreach (var d in added) inv?.DiscardFromStaging(d);
            Debug.Log("[UILayoutRuntimeProbe] 설명 UI 실측 끝");
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 설명 UI 실측 실패: " + e); }
        finally { RestoreTestHubGui(); }
    }

    // ── HUD 무기 칸(근접·원거리) ───────────────────────────────────────────
    /// <summary>
    /// 무기 칸이 <b>지금 든 장비</b>를 말하는지 본다 — 이름·강화 단계·활성 강조. 칸을 찍고, 반대 칸으로 바꿔 다시 찍는다.
    /// 무기 교체는 실제 경로(PlayerWeaponManager.SwitchToSlotAsync)를 탄다.
    /// </summary>
    [MenuItem("RelicFairy/UI/무기 칸 실측 (런 중)")]
    private static void RunWeaponSlots()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunWeaponSlotsAsync().Forget();
    }

    private static async UniTaskVoid RunWeaponSlotsAsync()
    {
        HideTestHubGui();
        try
        {
            var wm = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager;
            if (wm == null) { Debug.LogWarning("[UILayoutRuntimeProbe] WeaponManager 없음"); return; }

            for (int i = 0; i < wm.SlotCount; i++)
            {
                var d = wm.slots != null && i < wm.slots.Length ? wm.slots[i]?.runtimeData : null;
                Debug.Log($"[UILayoutRuntimeProbe] 무기 칸 {i} — {(d != null ? d.displayName : "빈 칸")} · +{(d != null ? d.enhanceLevel : 0)} · {(d != null ? d.weaponType.ToString() : "-")}");
            }

            // 런 시작에는 원거리 칸이 비어 있다 — 실제 경로로 활을 들려 두 칸을 같이 본다.
            if (wm.slots != null && wm.slots.Length > 1 && (wm.slots[1] == null || wm.slots[1].IsEmpty))
            {
                try
                {
                    await wm.AcquireWeaponToSlotAsync(await RuntimeWeaponForProbe("T1_Bow"), 1, setActive: false);
                    await UniTask.Delay(500, ignoreTimeScale: true);
                }
                catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 원거리 지급 실패: " + e.Message); }
            }

            // 강화 단계 표기(+N) 확인 — 근접 무기에 3강을 얹고 HUD를 새로 그린다(표시층만, 세이브 무관).
            var melee = wm.slots != null && wm.slots.Length > 0 ? wm.slots[0]?.runtimeData : null;
            if (melee != null && melee.enhanceLevel == 0)
            {
                melee.enhanceLevel = 3;
                wm.RaiseEquippedWeaponRefreshed();
                await UniTask.Delay(400, ignoreTimeScale: true);
            }

            int start = wm.CurrentSlotIndex;
            await UniTask.Delay(500, ignoreTimeScale: true);
            await ShotAsync($"WeaponSlots_active{start}", 0);

            int other = start == 0 ? 1 : 0;
            await wm.SwitchToSlotAsync(other);
            await UniTask.Delay(120, ignoreTimeScale: true);
            await ShotAsync($"WeaponSlots_switch{other}", 0);       // 교체 직후(칸이 튀는 순간)
            await UniTask.Delay(600, ignoreTimeScale: true);
            await ShotAsync($"WeaponSlots_active{other}", 0);

            await wm.SwitchToSlotAsync(start);
            await UniTask.Delay(700, ignoreTimeScale: true);
            await ShotAsync($"WeaponSlots_back{start}", 0);
            Debug.Log($"[UILayoutRuntimeProbe] 무기 칸 실측 끝 (활성 {start} → {other} → {start})");
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 무기 칸 실측 실패: " + e); }
        finally { RestoreTestHubGui(); }
    }


    /// <summary>실측용 무기 하나 — WeaponSO를 주소로 읽어 런타임 데이터로 만든다(게임과 같은 경로).</summary>
    private static async UniTask<WeaponData> RuntimeWeaponForProbe(string soKey)
    {
        var so = await Managers.AddressableManager.LoadAssetAsync<WeaponSO>(soKey);
        return so != null ? WeaponData.FromSO(so) : null;
    }
    // ── 상점 구매 · 정제소 응축 연출(런 중) ─────────────────────────────────
    /// <summary>
    /// 상점 구매(동전·상인 반응·물건 날아감·특가 품절)와 정제소 응축 3박자를 순간별로 찍는다.
    /// 골드·원석은 넉넉히 채워 넣고, 실제 컨트롤러·서비스로 산다 — 결과는 이 테스트 런에 실제로 반영된다.
    /// </summary>
    [MenuItem("RelicFairy/UI/상점·정제소 연출 실측 (런 중)")]
    private static void RunShopRefineryFx()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunShopRefineryFxAsync().Forget();
    }

    private static readonly int[] ShopFxShotMs    = { 60, 220, 420, 700, 1100 };
    private static readonly int[] RefineFxShotMs  = { 120, 380, 620, 900, 1300, 1800 };

    private static async UniTaskVoid RunShopRefineryFxAsync()
    {
        HideTestHubGui();
        var run = GameRunBootstrapper.Instance.Run;
        GameObject shopGo = null;
        try
        {
            // ── 상점 ──
            run?.PlayerState?.AddTempGold(900);   // 카드·특가를 둘 다 살 수 있게
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shopGo = new GameObject("~ProbeShopFx");
            var sc = shopGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", NonPub)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", NonPub)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(11)));
            shop.Bind(sc);
            await UniTask.Delay(900, ignoreTimeScale: true);
            await ShotAsync("Shop_Idle", 0);

            var st  = typeof(UI_ShopPanel);
            var sel = st.GetMethod("Select", NonPub);
            var buy = st.GetMethod("OnBuyClicked", NonPub);

            // 카드 구매 — 물건이 받는 곳으로 날아가고 동전이 빠진다.
            sel?.Invoke(shop, new object[] { 0 });
            await UniTask.Delay(400, ignoreTimeScale: true);
            await ShotSeriesAsync("Shop_Buy", ShopFxShotMs, () => buy?.Invoke(shop, null));
            await UniTask.Delay(800, ignoreTimeScale: true);
            await ShotAsync("Shop_AfterBuy", 0);

            // 특가 구매 — 「1개 남음」이 품절로 바뀌고 도장이 찍힌다.
            sel?.Invoke(shop, new object[] { -2 });
            await UniTask.Delay(400, ignoreTimeScale: true);
            await ShotSeriesAsync("Shop_Deal", ShopFxShotMs, () => buy?.Invoke(shop, null));
            await UniTask.Delay(900, ignoreTimeScale: true);
            await ShotAsync("Shop_AfterDeal", 0);
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 상점 연출 실측 실패: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (shopGo != null) UnityEngine.Object.Destroy(shopGo);
        }
        await UniTask.Delay(600, ignoreTimeScale: true);

        try
        {
            // ── 정제소 ── 세 번 돌려 등급별 연출을 본다(등급은 서비스가 굴린다).
            if (run?.FuelBank != null) run.FuelBank.Add(FuelKind.RuneOre, 60);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            await UniTask.Delay(900, ignoreTimeScale: true);
            await ShotAsync("Refine_Idle", 0);

            var rt   = typeof(UI_RefineryPanel);
            var spin = rt.GetMethod("OnSpinClicked", NonPub);
            var busyF = rt.GetField("_busy", NonPub);
            var rarF  = rt.GetField("_rarLine", NonPub);

            // 무작위 뽑기라 등급은 운이다(09-28 피버 폐지) — 세 번 돌려 나온 등급의 연출을 본다.
            for (int i = 0; i < 3; i++)
            {
                await UniTask.Delay(350, ignoreTimeScale: true);
                await ShotSeriesAsync($"Refine_Spin{i}", RefineFxShotMs, () => spin?.Invoke(panel, null));

                float t0 = Time.realtimeSinceStartup;
                while (busyF != null && (bool)busyF.GetValue(panel) && Time.realtimeSinceStartup - t0 < 8f)
                    await UniTask.Yield();
                var line = rarF?.GetValue(panel) as TMPro.TMP_Text;
                Debug.Log($"[UILayoutRuntimeProbe] 정제소 {i + 1}회 → {(line != null ? line.text : "")}");

                // 결과는 판으로 넘어간다(그리드가 열린다) — 닫고 정제소를 다시 연다.
                await UniTask.Delay(1800, ignoreTimeScale: true);
                Managers.UI.CloseAllPopupUI();
                await UniTask.Delay(400, ignoreTimeScale: true);
                panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
                await UniTask.Delay(700, ignoreTimeScale: true);
            }
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 정제소 연출 실측 실패: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            RestoreTestHubGui();
        }
        Debug.Log("[UILayoutRuntimeProbe] 상점·정제소 연출 실측 끝");
    }

    /// <summary>연출 하나를 시작하고 정해 둔 순간마다 찍는다(캡처가 프레임을 붙잡으므로 시간은 근사).</summary>
    private static async UniTask ShotSeriesAsync(string tag, int[] shots, Action start)
    {
        float t0 = Time.realtimeSinceStartup;
        start?.Invoke();
        foreach (int ms in shots)
        {
            int wait = ms - Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
            if (wait > 0) await UniTask.Delay(wait, ignoreTimeScale: true);
            await ShotAsync($"{tag}_{ms:0000}", 0);
        }
        Debug.Log($"[UILayoutRuntimeProbe] {tag} 연출 캡처 끝 ({Time.realtimeSinceStartup - t0:0.00}초)");
    }

    // ── 재련소 강화 연출(런 중) ─────────────────────────────────────────────
    /// <summary>
    /// 재련소 강화 연출을 순간별로 찍는다(포커스 · 망치 · 임팩트 · 판정 · 여운). 성공·실패는 실제 강화에서 처음 나온 것을,
    /// 잭팟은 드물어 성공 결과에 잭팟 표시(환불 포함)만 켜서 같은 시퀀스로 재생한다.
    /// 강화 결과는 실제로 반영된다 — 테스트 런에서만 돌린다. 연출마다 전체 길이(시작→다음 입력 가능)를 로그로 남긴다.
    /// </summary>
    [MenuItem("RelicFairy/UI/재련소 연출 실측 (런 중)")]
    private static void RunCrucibleFx()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunCrucibleFxAsync().Forget();
    }

    private static readonly int[] CrucibleFxShotMs  = { 60, 280, 480, 700, 1100, 1700 };
    private static readonly int[] CrucibleJackpotMs = { 60, 700, 1300, 1800, 2300, 2900 };   // 멈칫·맥박 뒤 빛살·배너까지

    private static async UniTaskVoid RunCrucibleFxAsync()
    {
        HideTestHubGui();
        var run = GameRunBootstrapper.Instance.Run;
        GameObject go = null;
        try
        {
            go = new GameObject("~ProbeCrucibleFx");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            run.FuelBank?.Add(FuelKind.EnhanceMaterial, 300);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await ShotAsync("Crucible_Idle", 0);

            var t     = typeof(UI_CruciblePanel);
            var play  = t.GetMethod("PlayEnhanceSequence", NonPub);
            var melee = Enum.ToObject(t.GetNestedType("EnhanceView", NonPub), 0);
            var animF = t.GetField("_animating", NonPub);
            bool gotOk = false, gotFail = false, gotDanger = false;

            for (int i = 0; i < 24 && !(gotOk && gotFail); i++)
            {
                if (!gotDanger && ctrl.DropAt(PlayerWeaponManager.Slot0) > 0)
                {
                    gotDanger = true;
                    await UniTask.Delay(900, ignoreTimeScale: true);   // 열기가 차오르도록
                    await ShotAsync("Crucible_Danger", 0);
                }
                var r = ctrl.TryEnhance(PlayerWeaponManager.Slot0);
                if (r.IsReject) { Debug.Log($"[UILayoutRuntimeProbe] 재련소 강화 거부 {r.outcome} — 중단"); break; }
                string tag = null;
                if (r.IsSuccess && ctrl.LastJackpot)  tag = "Crucible_JackpotNatural";
                else if (r.IsSuccess && !gotOk)       { tag = "Crucible_Success"; gotOk = true; }
                else if (!r.IsSuccess && !gotFail)    { tag = "Crucible_Fail"; gotFail = true; }
                await PlayCrucibleAsync(panel, play, melee, animF, r, tag);
            }

            // 잭팟 — 성공 하나를 받아 잭팟 표시(환불)만 켜서 같은 시퀀스로 재생한다.
            var ct = typeof(CrucibleRoomController);
            for (int i = 0; i < 12; i++)
            {
                var r = ctrl.TryEnhance(PlayerWeaponManager.Slot0);
                if (r.IsReject) { Debug.Log($"[UILayoutRuntimeProbe] 잭팟 실측 전 강화 거부 {r.outcome}"); break; }
                if (!r.IsSuccess) { await PlayCrucibleAsync(panel, play, melee, animF, r, null); continue; }
                if (!ctrl.LastJackpot)
                {
                    ct.GetField("_lastJackpot", NonPub)?.SetValue(ctrl, true);
                    ct.GetField("_lastRefund",  NonPub)?.SetValue(ctrl, r.spent);
                    run.FuelBank?.Add(FuelKind.EnhanceMaterial, r.spent);
                }
                await PlayCrucibleAsync(panel, play, melee, animF, r, "Crucible_Jackpot");
                break;
            }
            await ShotAsync("Crucible_After", 300);

            // 도박 구간 → 최대 강화(진화 열림)까지 올리며 상태 화면을 찍는다.
            bool dangerShot = false;
            for (int i = 0; i < 30; i++)
            {
                if (!dangerShot && ctrl.CanEnhance(PlayerWeaponManager.Slot0) && ctrl.DropAt(PlayerWeaponManager.Slot0) > 0)
                {
                    dangerShot = true;
                    await UniTask.Delay(900, ignoreTimeScale: true);   // 열기가 차오르도록
                    await ShotAsync("Crucible_Danger", 0);
                }
                var r = ctrl.TryEnhance(PlayerWeaponManager.Slot0);
                if (r.IsReject) break;
                await PlayCrucibleAsync(panel, play, melee, animF, r, null);
            }
            await ShotAsync("Crucible_Maxed", 600);
            // 무기에 따라(무형검 +6) 최대 전까지 하락 단계가 없다 — 표시만 켜서 위험 구간 모습을 찍는다.
            if (!dangerShot)
            {
                t.GetMethod("SetDanger", NonPub)?.Invoke(panel, new object[] { true, true });
                await UniTask.Delay(1400, ignoreTimeScale: true);
                await ShotAsync("Crucible_DangerForced", 0);
                t.GetMethod("SetDanger", NonPub)?.Invoke(panel, new object[] { false, false });
            }
            t.GetMethod("SelectTab", NonPub)?.Invoke(panel, new object[] { 1 });
            await ShotAsync("Crucible_Ranged", 600);
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 재련소 연출 실측 실패: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            RestoreTestHubGui();
        }
        Debug.Log("[UILayoutRuntimeProbe] 재련소 연출 실측 끝");
    }

    /// <summary>연출 하나를 재생하고, 태그가 있으면 정해 둔 순간마다 찍는다. 시작→다음 입력 가능까지의 길이를 로그로 남긴다.</summary>
    private static async UniTask PlayCrucibleAsync(UI_CruciblePanel panel, System.Reflection.MethodInfo play, object view,
                                                   System.Reflection.FieldInfo animF, EnhanceResult r, string tag)
    {
        float t0 = Time.realtimeSinceStartup;
        play.Invoke(panel, new object[] { r, view });
        if (tag != null)
        {
            foreach (int ms in tag.Contains("Jackpot") ? CrucibleJackpotMs : CrucibleFxShotMs)
            {
                int wait = ms - Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
                if (wait > 0) await UniTask.Delay(wait, ignoreTimeScale: true);
                await ShotAsync($"{tag}_{ms:0000}", 0);
            }
        }
        while ((bool)animF.GetValue(panel) && Time.realtimeSinceStartup - t0 < 8f) await UniTask.Yield();
        // 강화로 스킬 단계에 오르면 각인 선택 화면이 창을 덮는다 — 이 실측은 강화 연출이 목적이라 첫 각인을 골라 치운다.
        if (EngraveDim(panel) is { activeSelf: true })
        {
            typeof(UI_CruciblePanel).GetMethod("OnEngravePicked", NonPub)?.Invoke(panel, new object[] { 0 });
            Debug.Log("[UILayoutRuntimeProbe] 재련소 — 각인 선택 화면이 떠 첫 각인을 골랐다(실측용)");
        }
        string shot = tag != null ? $" [{tag} · 캡처 포함]" : "";
        Debug.Log($"[UILayoutRuntimeProbe] 재련소 {r.outcome} +{r.beforeLevel}→+{r.afterLevel}{shot} 연출 {Time.realtimeSinceStartup - t0:0.00}초");
        await UniTask.Delay(250, ignoreTimeScale: true);
    }

    // ── 재련소 각인 택1 (09-26) ─────────────────────────────────────────
    private const string EngraveKatanaSO = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string EngraveOut      = "Temp/crucible_engrave_probe.txt";

    /// <summary>
    /// 재련소 스킬 각인 선택 화면 — ① +3으로 열면 바로 묻는다 ② +2에서 강화로 +3에 오르면 연출 뒤 묻는다.
    /// 둘 다 찍고, 고른 각인이 무기에 기록되고 같은 단계를 다시 묻지 않는지 확인한다.
    /// 카타나(환영베기 R)를 근접 칸에 든다 — 테스트 런의 무기가 바뀐다. 결과: Temp/crucible_engrave_probe.txt
    /// </summary>
    [MenuItem("RelicFairy/UI/재련소 각인 선택 실측 (런 중)")]
    private static void RunCrucibleEngrave()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunCrucibleEngraveAsync().Forget();
    }

    private static async UniTaskVoid RunCrucibleEngraveAsync()
    {
        HideTestHubGui();
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(bool ok, string msg) { sb.AppendLine($"{(ok ? "✅" : "❌")} {msg}"); if (ok) pass++; else fail++; }

        var run = GameRunBootstrapper.Instance.Run;
        var t   = typeof(UI_CruciblePanel);
        GameObject go = null;
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(EngraveKatanaSO);
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, run.Player, PlayerWeaponManager.Slot0);
            await UniTask.Delay(800, ignoreTimeScale: true);

            go = new GameObject("~ProbeCrucibleEngrave");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(11), null);
            run.FuelBank?.Add(FuelKind.EnhanceMaterial, 500);
            var wd = ctrl.GetSlot(PlayerWeaponManager.Slot0);
            Check(wd?.skillQ != null && wd.skillQ.Engravings.Count >= 2,
                  $"근접 칸 {wd?.displayName} · R {wd?.skillQ?.skillName} · 각인 {wd?.skillQ?.Engravings.Count ?? 0}종");
            if (wd == null) return;

            // ① +3으로 열면 바로 묻는다
            wd.enhanceLevel = 3;
            wd.engravings   = "";
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            var dim = EngraveDim(panel);
            Check(dim != null && dim.activeSelf, "① +3으로 열자 각인 선택 화면");
            if (dim != null && dim.activeSelf)
            {
                LogEngraveTexts(dim, sb);
                await ShotAsync("Crucible_Engrave_Open", 200);
                t.GetMethod("OnEngravePicked", NonPub)?.Invoke(panel, new object[] { 0 });
                await UniTask.Delay(300, ignoreTimeScale: true);
                Check(!dim.activeSelf, "고르면 화면이 닫힌다");
                Check(wd.HasEngraving(PhantomDanceBehaviorSO.EngravePursuit), $"「잔상 추격」 기록 (engravings=\"{wd.engravings}\")");
                Check(!SkillEngravingService.TryGetPendingOffer(wd, new List<SkillSO.EngravingDef>(), out _, out _),
                      "같은 단계는 다시 묻지 않는다");
                await ShotAsync("Crucible_Engrave_Picked", 300);
            }
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ② +2에서 강화로 +3에 오르면 연출 뒤 묻는다
            wd.enhanceLevel = 2;
            wd.engravings   = "";
            panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            dim = EngraveDim(panel);
            Check(dim == null || !dim.activeSelf, "② +2로 열면 묻지 않는다");

            var play  = t.GetMethod("PlayEnhanceSequence", NonPub);
            var melee = Enum.ToObject(t.GetNestedType("EnhanceView", NonPub), 0);
            var animF = t.GetField("_animating", NonPub);
            for (int i = 0; i < 20 && wd.enhanceLevel < 3; i++)
            {
                var r = ctrl.TryEnhance(PlayerWeaponManager.Slot0);
                if (r.IsReject) { sb.AppendLine($"   강화 거부 {r.outcome}"); break; }
                float t0 = Time.realtimeSinceStartup;
                play.Invoke(panel, new object[] { r, melee });
                while ((bool)animF.GetValue(panel) && Time.realtimeSinceStartup - t0 < 8f) await UniTask.Yield();
                await UniTask.Delay(300, ignoreTimeScale: true);
                sb.AppendLine($"   강화 {r.outcome} +{r.beforeLevel}→+{r.afterLevel}");
            }
            dim = EngraveDim(panel);
            Check(wd.enhanceLevel >= 3 && dim != null && dim.activeSelf, $"강화로 +{wd.enhanceLevel} → 연출 뒤 각인 선택 화면");
            if (dim != null && dim.activeSelf)
            {
                await ShotAsync("Crucible_Engrave_AfterEnhance", 200);
                t.GetMethod("OnEngravePicked", NonPub)?.Invoke(panel, new object[] { 1 });
                await UniTask.Delay(300, ignoreTimeScale: true);
                Check(wd.HasEngraving(PhantomDanceBehaviorSO.EngraveCondense) && !wd.HasEngraving(PhantomDanceBehaviorSO.EngravePursuit),
                      $"「응축」만 기록 (engravings=\"{wd.engravings}\")");
            }
        }
        catch (Exception e) { Check(false, "예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            RestoreTestHubGui();
            sb.Insert(0, $"재련소 각인 선택 실측 — {pass}/{pass + fail}\n");
            File.WriteAllText(EngraveOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 재련소 각인 실측 끝 {pass}/{pass + fail} → {EngraveOut}");
        }
    }

    private static GameObject EngraveDim(UI_CruciblePanel panel)
        => typeof(UI_CruciblePanel).GetField("_engravePanel", NonPub)?.GetValue(panel) as GameObject;

    /// <summary>각인 화면 글자 — 화면 px(자동 크기·창 배율 반영)과 넘침.</summary>
    private static void LogEngraveTexts(GameObject dim, StringBuilder sb)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var tmp in dim.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            tmp.ForceMeshUpdate();
            var root = tmp.canvas != null ? tmp.canvas.rootCanvas : null;
            float px = root != null
                ? tmp.fontSize * tmp.rectTransform.lossyScale.y / root.transform.lossyScale.y * root.scaleFactor
                : tmp.fontSize;
            sb.AppendLine($"   {tmp.name,-12} {px,5:0.0}px 넘침={tmp.isTextOverflowing} 「{tmp.text}」");
        }
    }

    // ── 방 전환 와이프 (09-26) ─────────────────────────────────────────
    /// <summary>
    /// 방 전환 와이프를 게임과 같은 색(RunFlowController.KindColor)으로 느리게 돌려 중간을 찍는다 — 전투·상점·보스.
    /// 실제 길이(0.12초/0.2초)로는 가장자리가 한두 프레임이라 눈으로 못 본다. 결과: Temp/ui_shots/Wipe_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/방 전환 와이프 실측 (플레이 중)")]
    private static void RunScreenWipe()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중에만 동작한다."); return; }
        RunScreenWipeAsync().Forget();
    }

    private static async UniTaskVoid RunScreenWipeAsync()
    {
        HideTestHubGui();
        var kindColor = typeof(RunFlowController).GetMethod("KindColor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Debug.Log($"[UILayoutRuntimeProbe] 와이프 스킨 {(UISkin.ScreenWipe != null ? "있음 — 잉크 와이프" : "없음 — 단색 판")}");
        const float Dur = 1.2f;
        var linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        try
        {
            foreach (var (kind, dir) in new[] { (RoomPlanKind.Normal, Vector2.right), (RoomPlanKind.Shop, Vector2.up), (RoomPlanKind.Boss, Vector2.left) })
            {
                var c = kindColor != null ? (Color)kindColor.Invoke(null, new object[] { kind }) : Color.black;
                var cover = ScreenFade.CoverAsync(dir, c, Dur, linear);
                await UniTask.Delay(TimeSpan.FromSeconds(Dur * 0.30f), ignoreTimeScale: true); await ShotAsync($"Wipe_{kind}_cover30", 0);
                await UniTask.Delay(TimeSpan.FromSeconds(Dur * 0.35f), ignoreTimeScale: true); await ShotAsync($"Wipe_{kind}_cover65", 0);
                await cover;
                await UniTask.Delay(400, ignoreTimeScale: true);                               await ShotAsync($"Wipe_{kind}_hold", 0);
                var reveal = ScreenFade.RevealAsync(dir, Dur, linear);
                await UniTask.Delay(TimeSpan.FromSeconds(Dur * 0.45f), ignoreTimeScale: true); await ShotAsync($"Wipe_{kind}_reveal45", 0);
                await reveal;
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            await ShotAsync("Wipe_After", 0);   // 다 걷힌 뒤 화면이 깨끗한지
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 와이프 실측 실패: " + e); }
        finally { RestoreTestHubGui(); }
        Debug.Log("[UILayoutRuntimeProbe] 와이프 실측 끝 → Temp/ui_shots/Wipe_*.png");
    }

    // ── 시작 파츠 선택창 (09-26 · 베이스캠프 파츠 공방) ─────────────────────────────
    private const string StartPartOut = "Temp/start_part_probe.txt";

    /// <summary>
    /// 시작 파츠 선택창을 스텁 데이터로 연다(분열·거력 열림 / 관통·작렬·추적 잠김 — b6 제안안).
    /// ① 처음 열기 ② 카드 고르기 ③ 확정 → 결과·창이 먼저 닫혔는지 ④ 「장착 중」으로 다시 열기 ⑤ 닫기 → 결과 null.
    /// 글자 크기(화면 px)·넘침도 적는다. 결과: Temp/start_part_probe.txt · Temp/ui_shots/StartPart_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/시작 파츠 선택창 실측 (플레이 중)")]
    private static void RunStartPart()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중에만 동작한다."); return; }
        RunStartPartAsync().Forget();
    }

    private static async UniTaskVoid RunStartPartAsync()
    {
        HideTestHubGui();
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(bool ok, string msg) { sb.AppendLine($"{(ok ? "✅" : "❌")} {msg}"); if (ok) pass++; else fail++; }
        var t = typeof(UI_StartPartPopup);
        try
        {
            var all = Managers.WeaponParts?.All;
            Check(all != null && all.Count >= 5, $"파츠 정의 {all?.Count ?? 0}종");
            if (all == null) return;
            var cards = new List<UI_StartPartPopup.Card>();
            foreach (var p in all)
            {
                bool open = p.part_id == "part_split" || p.part_id == "part_power";
                cards.Add(new UI_StartPartPopup.Card { Part = p, Unlocked = open,
                    UnlockHint = open ? null : $"기억의 제단 「{p.part_name}」에서 해금" });
            }

            // ① 처음 열기
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_StartPartPopup>();
            var wait = popup.WaitForInteractionAsync(default);
            popup.Setup(cards, null);
            await UniTask.Delay(700, ignoreTimeScale: true);
            LogPopupTexts(popup.transform, sb);
            await ShotAsync("StartPart_Open", 0);

            // ② 두 번째 열린 카드(거력) 고르기
            t.GetMethod("SetSelected", NonPub)?.Invoke(popup, new object[] { 1, false });
            await UniTask.Delay(300, ignoreTimeScale: true);
            await ShotAsync("StartPart_Selected", 0);

            // ③ 확정 — 결과가 차고, 대기가 풀릴 때 창은 이미 스택에서 빠져 있어야 한다(시간정지 해제)
            bool closedFirst = false;
            wait.ContinueWith(() => closedFirst = !Managers.UI.IsGameplayBlocked).Forget();
            t.GetMethod("OnConfirmClicked", NonPub)?.Invoke(popup, null);
            await UniTask.Delay(500, ignoreTimeScale: true);
            Check(popup == null || popup.ResultPartId == "part_power", $"확정 → ResultPartId={(popup != null ? popup.ResultPartId : "(파괴)")}");
            Check(closedFirst, "대기가 풀릴 때 시간정지가 이미 풀려 있다(창이 먼저 닫힘)");

            // ④ 「장착 중」으로 다시 열기
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_StartPartPopup>();
            wait = popup.WaitForInteractionAsync(default);
            popup.Setup(cards, "part_split");
            await UniTask.Delay(700, ignoreTimeScale: true);
            await ShotAsync("StartPart_Current", 0);
            Check(ShopUIStyle.FindDeep(popup.transform, "CurrentBadge") != null, "고른 파츠에 「장착 중」 표시");

            // ⑤ 닫기(ESC와 같은 경로) → 결과 null · 대기 끝
            popup.ClosePopupUI();
            await UniTask.Delay(400, ignoreTimeScale: true);
            Check(wait.Status != UniTaskStatus.Pending, "닫으면 대기가 끝난다");
            Check(!Managers.UI.IsGameplayBlocked, "닫은 뒤 시간정지 해제");
        }
        catch (Exception e) { Check(false, "예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            RestoreTestHubGui();
            sb.Insert(0, $"시작 파츠 선택창 실측 — {pass}/{pass + fail}\n");
            File.WriteAllText(StartPartOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 시작 파츠 실측 끝 {pass}/{pass + fail} → {StartPartOut}");
        }
    }

    /// <summary>창 안 글자 — 화면 px(자동 크기·창 배율 반영)과 넘침. 16px 미만은 ⚠.</summary>
    private static void LogPopupTexts(Transform root, StringBuilder sb)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(false))
        {
            if (string.IsNullOrWhiteSpace(tmp.text)) continue;
            tmp.ForceMeshUpdate();
            var rc = tmp.canvas != null ? tmp.canvas.rootCanvas : null;
            float px = rc != null ? tmp.fontSize * tmp.rectTransform.lossyScale.y / rc.transform.lossyScale.y * rc.scaleFactor : tmp.fontSize;
            string flag = px < 15.95f ? " ⚠16px 미만" : "";
            sb.AppendLine($"   {tmp.name,-12} {px,5:0.0}px 넘침={tmp.isTextOverflowing}{flag} 「{tmp.text.Replace('\n', ' ')}」");
        }
    }

    // ── 몬스터 이름·체력바 · 보스 체력바 (09-27) ─────────────────────────────────
    private const string NameplateOut = "Temp/nameplate_probe.txt";
    private static readonly string[] NameplateMonsters = { "Slime/Slime", "Skeleton/Skeleton", "Orc/Orc", "Golem/Golem" };
    private static readonly float[]  NameplateDamage   = { 0.40f, 0.70f, 0.12f, 0f };   // 최대 HP 대비 깎을 비율

    /// <summary>
    /// 플레이어 앞에 일반 몬스터 4종을 부채꼴로 세우고 HP를 서로 다르게 깎아 머리 위 이름·체력바를 찍는다
    /// (맞은 직후 = 잔상 · 1.5초 뒤 = 정착). 이어서 HUD 보스 체력바를 표본 값으로 띄워 찍는다.
    /// 글자 크기(화면 px)·굵기·테두리를 적는다. 결과: Temp/nameplate_probe.txt · Temp/ui_shots/Nameplate_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/몬스터 이름·체력바 실측 (런 중)")]
    private static void RunNameplates()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunNameplatesAsync().Forget();
    }

    private static async UniTaskVoid RunNameplatesAsync()
    {
        HideTestHubGui();
        var sb = new StringBuilder();
        var spawned = new List<RelicFairy.Monster.MonsterBase>();
        var p = GameRunBootstrapper.Instance.Run.Player;
        try
        {
            Vector3 fwd = p.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            for (int i = 0; i < NameplateMonsters.Length; i++)
            {
                Vector3 pos = p.transform.position + fwd * 6.5f + right * ((i - 1.5f) * 2.6f);
                var mb = await Managers.ObjectPooler.SpawnAsync<RelicFairy.Monster.MonsterBase>(
                    NameplateMonsters[i], ObjectPoolerManager.PoolType.Monster, pos, Quaternion.LookRotation(-fwd));
                if (mb == null) { sb.AppendLine($"스폰 실패 {NameplateMonsters[i]}"); continue; }
                mb.HpFloorMin1 = true;
                spawned.Add(mb);
            }
            await UniTask.Delay(1500, ignoreTimeScale: true);   // 등장·바 연결 대기
            for (int i = 0; i < spawned.Count; i++)
                if (NameplateDamage[i] > 0f) spawned[i].TakeDamage(spawned[i].EffectiveMaxHp * NameplateDamage[i], p.gameObject, 0f);
            await UniTask.Delay(250, ignoreTimeScale: true);
            await ShotAsync("Nameplate_Hit", 0);
            await UniTask.Delay(1500, ignoreTimeScale: true);
            await ShotAsync("Nameplate_Settled", 0);

            // 바·이름 글자 정보
            foreach (var bar in UnityEngine.Object.FindObjectsByType<MonsterHPBar>(FindObjectsSortMode.None))
            {
                if (!bar.isActiveAndEnabled) continue;
                foreach (var tmp in bar.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    if (!string.IsNullOrWhiteSpace(tmp.text))
                        sb.AppendLine($"   바 글자 {tmp.name,-10} size {tmp.fontSize:0.#} style {tmp.fontStyle} outline {tmp.outlineWidth:0.##} 「{tmp.text}」");
            }

            // HUD 보스 체력바 — 표본 값으로 띄운다
            var boss = UnityEngine.Object.FindFirstObjectByType<BossPanelView>(FindObjectsInactive.Include);
            if (boss != null)
            {
                bool was = boss.gameObject.activeSelf;
                boss.gameObject.SetActive(true);
                boss.Init(7000, "숲의 수호자");
                boss.SetHP(3458, 7000);
                await UniTask.Delay(600, ignoreTimeScale: true);
                await ShotAsync("Nameplate_BossHud", 0);
                LogPopupTexts(boss.transform, sb);
                boss.gameObject.SetActive(was);
            }
            else sb.AppendLine("보스 패널 없음");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var mb in spawned) if (mb != null) UnityEngine.Object.Destroy(mb.gameObject);
            RestoreTestHubGui();
            File.WriteAllText(NameplateOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 이름·체력바 실측 끝 → {NameplateOut}");
        }
    }

    // ── 로딩바 (09-27) ─────────────────────────────────────────
    /// <summary>씬 로딩 오버레이를 띄워 진행 45%·100%를 찍는다(실제 씬 전환 없이). 결과: Temp/ui_shots/Loading_*.png</summary>
    [MenuItem("RelicFairy/UI/로딩바 실측 (플레이 중)")]
    private static void RunLoadingBar()
    {
        if (!EditorApplication.isPlaying || UI_SceneLoading.Instance == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중(로딩 오버레이 있음)에만 동작한다.");
            return;
        }
        RunLoadingBarAsync().Forget();
    }

    private static async UniTaskVoid RunLoadingBarAsync()
    {
        HideTestHubGui();
        var ld = UI_SceneLoading.Instance;
        try
        {
            await ld.ShowAsync();
            ld.SetProgress(0.45f);
            await UniTask.Delay(900, ignoreTimeScale: true);
            await ShotAsync("Loading_45", 0);
            ld.SetProgress(1f);
            await UniTask.Delay(500, ignoreTimeScale: true);
            await ShotAsync("Loading_100", 0);
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 로딩바 실측 실패: " + e); }
        finally
        {
            await ld.HideAsync();
            RestoreTestHubGui();
            Debug.Log("[UILayoutRuntimeProbe] 로딩바 실측 끝 → Temp/ui_shots/Loading_*.png");
        }
    }

    // ── 전설 승급 화면 · 서약 4칸 (09-27) ─────────────────────────────────────
    /// <summary>
    /// ① 서약 4개(복원 경로 — 절대 상한 4)를 넣어 HUD 격자(2열)·「서약 n/N」을 찍는다.
    /// ② 근접 칸에 T1 카타나(진화 분기 없음)를 최대 강화로 들고 재련소를 열어 「◆ 승급」 버튼과 전설 택1 화면을 찍는다.
    /// 테스트 런의 서약·무기가 바뀐다. 결과: Temp/promote_covenant_probe.txt · Temp/ui_shots/Covenant_Four·Promote_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/승급·서약 칸 실측 (런 중)")]
    private static void RunPromoteCovenant()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunPromoteCovenantAsync().Forget();
    }

    private static async UniTaskVoid RunPromoteCovenantAsync()
    {
        HideTestHubGui();
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        GameObject go = null;
        try
        {
            // ① 서약 4칸
            var h = run.CovenantHandler;
            var entries = new List<CovenantSaveEntry>();
            var usedE = new HashSet<string>();
            foreach (var c in CovenantPalette.CauseIds)
            {
                if (entries.Count + (h.Covenants?.Count ?? 0) >= 4) break;
                foreach (var e in CovenantPalette.EffectIds)
                {
                    if (usedE.Contains(e) || CovenantPalette.IsBannedPair(c, e)) continue;
                    entries.Add(new CovenantSaveEntry { id = AssembledCovenant.MakeId(c, CovenantTier.Silver, e, CovenantTier.Silver), stage = 0 });
                    usedE.Add(e);
                    break;
                }
            }
            h.RestoreSelections(entries);
            await UniTask.Delay(800, ignoreTimeScale: true);
            sb.AppendLine($"서약 {h.Covenants?.Count ?? 0}개 (상한 {CovenantHandler.Capacity})");
            await ShotAsync("Covenant_Four", 0);

            // ② 전설 승급
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(EngraveKatanaSO);
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, run.Player, PlayerWeaponManager.Slot0);
            await UniTask.Delay(800, ignoreTimeScale: true);
            go = new GameObject("~ProbePromote");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(5), null);
            run.FuelBank?.Add(FuelKind.EnhanceMaterial, 100);
            var wd = ctrl.GetSlot(PlayerWeaponManager.Slot0);
            if (wd != null) { wd.enhanceLevel = ctrl.MaxAt(PlayerWeaponManager.Slot0); wd.legendId = ""; wd.RecomputeEnhancedStats(); }
            sb.AppendLine($"무기 {wd?.displayName} +{wd?.enhanceLevel} · 승급 가능 {ctrl.CanPromote(PlayerWeaponManager.Slot0)} · 후보 {ctrl.Legends.Length}");
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            await ShotAsync("Promote_Button", 0);
            typeof(UI_CruciblePanel).GetMethod("ShowEvolvePanel", NonPub)?.Invoke(panel, null);
            await UniTask.Delay(500, ignoreTimeScale: true);
            var legend = typeof(UI_CruciblePanel).GetField("_legendPanel", NonPub)?.GetValue(panel) as GameObject;
            sb.AppendLine($"전설 택1 화면 {(legend != null && legend.activeSelf ? "열림" : "안 열림")}");
            if (legend != null && legend.activeSelf) LogPopupTexts(legend.transform, sb);
            await ShotAsync("Promote_Legend", 0);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            RestoreTestHubGui();
            File.WriteAllText("Temp/promote_covenant_probe.txt", sb.ToString());
            Debug.Log("[UILayoutRuntimeProbe] 승급·서약 실측 끝 → Temp/promote_covenant_probe.txt");
        }
    }

    // ── 로비(메인 메뉴 · 세이브 슬롯) ─────────────────────────────────────────
    // ── 정제소·룬판 개편 (09-27) ─────────────────────────────────────────
    private const string RefineGridOut = "Temp/refine_grid_probe.txt";

    /// <summary>
    /// 정제소 무작위 뽑기(09-28 · 여는 화면 · 젬 링이 도는 중 · 결과 · 원석/보관함 변화)와 룬판 가시성(카드 호버 테두리 ·
    /// 미리보기 겹판 · 존 맥박 · 거절 사유 · 확인창 순서)을 찍고 판정 결과를 적는다. 테스트 런의 원석·보관함·판이 바뀐다.
    /// 결과: Temp/refine_grid_probe.txt · Temp/ui_shots/RG_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/정제소·룬판 개편 실측 (런 중)")]
    private static void RunRefineGrid()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunRefineGridAsync().Forget();
    }

    private static async UniTaskVoid RunRefineGridAsync()
    {
        HideTestHubGui();
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        try
        {
            // ① 정제소 — 여는 화면 · 돌리는 중(젬 링의 불이 돈다) · 결과(나온 룬의 속성에 멎고 좌측 판에 그 룬)
            run.FuelBank?.Add(FuelKind.RuneOre, 60);
            var inv = run.ItemInventory;
            int stagedBefore = inv.StagingCount;
            int oreBefore    = run.FuelBank?.RuneOre ?? 0;
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            await UniTask.Delay(900, ignoreTimeScale: true);
            await ShotAsync("RG_Refine_Idle", 0);
            var pt = typeof(UI_RefineryPanel);
            var odds = (pt.GetField("_svc", NonPub)?.GetValue(panel) as RefineryService)?.CurrentOdds();
            sb.AppendLine($"확률 희귀/영웅/전설 = {odds?.rare:0.00}/{odds?.epic:0.00}/{odds?.legend:0.00}");
            pt.GetMethod("OnSpinClicked", NonPub)?.Invoke(panel, null);
            await UniTask.Delay(350, ignoreTimeScale: true);
            await ShotAsync("RG_Refine_Spin", 0);
            await UniTask.Delay(1100, ignoreTimeScale: true);
            await ShotAsync("RG_Refine_Result", 0);
            sb.AppendLine($"결과 줄 「{(pt.GetField("_rarLine", NonPub)?.GetValue(panel) as TMPro.TMP_Text)?.text}」");
            await UniTask.Delay(2200, ignoreTimeScale: true);   // 판으로 넘어간다

            RuntimeItemData drawn = inv.StagingCount > stagedBefore ? inv.StagingItems[inv.StagingCount - 1] : null;
            sb.AppendLine($"보관함 {stagedBefore} → {inv.StagingCount} · 원석 {oreBefore} → {run.FuelBank?.RuneOre}");
            sb.AppendLine($"뽑힌 룬 = {drawn?.displayName} ({drawn?.rarity} · {drawn?.element}) · 드롭 풀에 있음 {(drawn != null && ItemSORegistry.Find(drawn.itemId) != null)} · 존핵 {RefineryService.IsZoneCore(drawn)}(False가 정상)");
            if (UI_GridPanel.Instance == null || !UI_GridPanel.Instance.gameObject.activeInHierarchy)
            {
                Managers.UI.CloseAllPopupUI();
                if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
                UI_GridPanel.Instance?.ShowWithNewItem(drawn);
                await UniTask.Delay(900, ignoreTimeScale: true);
            }

            // ② 룬판 — 새 룬이 골라진 채(존 맥박 · 카드 얼굴·효과 줄)
            await ShotAsync("RG_Grid_NewRune", 0);
            var hex = UnityEngine.Object.FindFirstObjectByType<MerlinRuneHexGridView>();
            var pulse = typeof(MerlinRuneHexGridView).GetField("_pulseImages", NonPub)?.GetValue(hex) as List<Image>;
            float a1 = pulse != null && pulse.Count > 0 ? pulse[0].color.a : -1f;
            await UniTask.Delay(420, ignoreTimeScale: true);
            float a2 = pulse != null && pulse.Count > 0 ? pulse[0].color.a : -1f;
            sb.AppendLine($"맥박 칸 {pulse?.Count ?? -1}개 · 알파 {a1:0.00} → {a2:0.00}(달라야 정상)");

            // 카드 호버
            var staging = UnityEngine.Object.FindFirstObjectByType<StagingAreaView>();
            var hover = typeof(StagingAreaView).GetMethod("SetSlotHover", NonPub);
            hover?.Invoke(staging, new object[] { 0, true });
            await UniTask.Delay(200, ignoreTimeScale: true);
            await ShotAsync("RG_Grid_Hover", 0);
            hover?.Invoke(staging, new object[] { 0, false });

            // 미리보기 겹판 — 불 존 3칸 초록 · 물 존 3칸 빨강(타일 위에 보여야 한다)
            var squares = hex != null && hex.HexGrid != null ? hex.HexGrid.GetGridSquares() : null;
            var lit = new List<GridSquare>();
            GridSquare fireSq = null, iceSq = null, centerSq = null;
            int nf = 0, ni = 0;
            if (squares != null)
                foreach (var sq in squares)
                {
                    if (sq == null || sq.isOccupied) continue;
                    if (sq.zoneCode == ElementDef.IdToCode("FIRE"))
                    {
                        if (fireSq == null) fireSq = sq;
                        if (nf++ < 3) { sq.SetPreviewHighlight(true, new Color(0.2f, 0.9f, 0.3f, 0.55f)); lit.Add(sq); }
                    }
                    else if (sq.zoneCode == ElementDef.IdToCode("ICE"))
                    {
                        if (iceSq == null) iceSq = sq;
                        if (ni++ < 3) { sq.SetPreviewHighlight(true, new Color(1f, 0.25f, 0.25f, 0.55f)); lit.Add(sq); }
                    }
                    else if (sq.zoneCode == ElementDef.CenterCode && centerSq == null) centerSq = sq;
                }
            await UniTask.Delay(150, ignoreTimeScale: true);
            await ShotAsync("RG_Grid_Preview", 0);
            foreach (var sq in lit) sq.SetPreviewHighlight(false, Color.clear);

            // 거절 사유 — 뽑힌 룬을 물 존 · 중앙 위에 얹어 본다(놓지는 않는다)
            var shape = drawn != null && staging != null ? staging.GetShapeForItem(drawn) : null;
            var host  = BoardManager.Instance?.gridHost;
            if (shape != null && host != null)
            {
                shape.transform.SetParent(host, false);
                shape.transform.localScale = Vector3.one;
                foreach (var target in new[] { iceSq, centerSq })
                {
                    if (target == null || shape.transform.childCount == 0) continue;
                    var first = shape.transform.GetChild(0);
                    shape.transform.position += target.transform.position - first.position;
                    sb.AppendLine($"얹은 칸 {target.zoneCode} → 「{GridManager.Instance.DescribeDropFailure(shape)}」");
                }
                UI_GridPanel.NotifyDropRejected(GridManager.Instance.DescribeDropFailure(shape));
                await UniTask.Delay(250, ignoreTimeScale: true);
                await ShotAsync("RG_Grid_Toast", 0);

                // 불 존에 실제로 놓아 본다(속성이 다르면 거절되는 것도 정상)
                bool placed = fireSq != null && GridManager.Instance.TryPlaceShapeAt(shape, fireSq);
                if (placed) BoardManager.Instance.OnShapePlaced(shape);
                else        BoardManager.Instance.ReSlotAndReturn(shape);
                await UniTask.Delay(500, ignoreTimeScale: true);
                sb.AppendLine($"불 존에 놓음 = {placed}");
                await ShotAsync("RG_Grid_Placed", 0);
            }
            else sb.AppendLine("룬 모양을 못 찾음 — 거절·배치 실측 생략");

            // 폐기 확인창 — 판에 놓인 룬보다 위여야 한다
            if (staging != null && inv.StagingCount > 0)
            {
                typeof(StagingAreaView).GetMethod("ShowDiscardDialog", NonPub)?.Invoke(staging, new object[] { inv.StagingItems[0] });
                await UniTask.Delay(250, ignoreTimeScale: true);
                await ShotAsync("RG_Grid_Discard", 0);
                typeof(StagingAreaView).GetMethod("HideDiscardDialog", NonPub)?.Invoke(staging, null);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            RestoreTestHubGui();
            File.WriteAllText(RefineGridOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 정제소·룬판 실측 끝 → {RefineGridOut}");
        }
    }

    // ── 기타 HUD (09-28, UI 전수 재검증) ───────────────────────────────
    /// <summary>
    /// 팝업·런 화면 실측이 안 잡는 HUD 조각을 게임 API로 띄워 찍는다: 보스 체력바(2페이지 눈금) · 보스 대사(예고·바크·멀린) ·
    /// 퀘스트 완료 알림 · 출구 나침반(배지 2개) · 엔딩 카드(카드·크레딧). 결과: Temp/ui_shots/Extra_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/기타 HUD 실측 (플레이 중)")]
    private static void RunExtraHud()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중에 실행해야 한다."); return; }
        RunExtraHudAsync().Forget();
    }

    private static async UniTaskVoid RunExtraHudAsync()
    {
        HideTestHubGui();
        var temps = new List<GameObject>();
        var presenter = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        var hudView   = UnityEngine.Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        var modeField = typeof(HudPresenter).GetField("_currentMode", NonPub);
        var prevMode  = presenter != null && modeField != null ? (HUDIds.Mode)modeField.GetValue(presenter) : HUDIds.Mode.None;
        try
        {
            // (보스 체력바는 HUD 가드가 표본 모드를 0.2초 만에 되돌려 여기선 못 찍는다 — 이름표 실측의 Nameplate_BossHud가 실제 보스로 찍는다.)

            // ② 보스 대사 — 종류마다 자리·크기가 다르다
            foreach (var (tag, text, type) in new[]
                     {
                         ("Extra_Bark_Pattern", "하늘이 불탄다 — 운석이 떨어진다!", RelicFairy.UI.BossBarkType.PatternAnnounce),
                         ("Extra_Bark_Line",    "네놈의 검이 여기까지 닿을 줄은 몰랐다. 하지만 이 불꽃은 꺼지지 않는다.", RelicFairy.UI.BossBarkType.Bark),
                         ("Extra_Bark_Merlin",  "숨을 고르렴. 저 문 너머가 첫 번째 시련이란다.", RelicFairy.UI.BossBarkType.MerlinNarration),
                     })
            {
                RelicFairy.UI.UI_BossBark.Show(text, type);
                await UniTask.Delay(700, ignoreTimeScale: true);
                await ShotAsync(tag, 0);
                await UniTask.Delay(3200, ignoreTimeScale: true);
            }

            // ③ 퀘스트 완료 알림
            var notifier = UnityEngine.Object.FindFirstObjectByType<QuestNotifierView>(FindObjectsInactive.Include);
            if (notifier != null)
            {
                notifier.gameObject.SetActive(true);
                notifier.PlayAsync(null, "퀘스트 완료", "유물을 손에 넣어라", notifier.GetCancellationTokenOnDestroy()).Forget();
                await UniTask.Delay(600, ignoreTimeScale: true);
                await ShotAsync("Extra_QuestNotice", 0);
                await UniTask.Delay(2400, ignoreTimeScale: true);
            }

            // ④ 출구 나침반 — 플레이어 앞 두 출구(정예 · 상점)
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                var a = new GameObject("~ProbeExitA"); a.transform.position = player.transform.position + new Vector3(12f, 0f, 18f); temps.Add(a);
                var b = new GameObject("~ProbeExitB"); b.transform.position = player.transform.position + new Vector3(-30f, 0f, -6f); temps.Add(b);
                var compass = ExitCompassHud.Create();
                temps.Add(compass.gameObject);
                compass.SetExits(new List<(Transform, string, string, Color)>
                {
                    (a.transform, "◆ 정예", "희귀 이상 · 후보 4", new Color(0.85f, 0.35f, 0.35f)),
                    (b.transform, "◇ 상점", "포션 · 룬", new Color(0.95f, 0.78f, 0.35f)),
                });
                await UniTask.Delay(500, ignoreTimeScale: true);
                await ShotAsync("Extra_ExitCompass", 0);
            }

            // (엔딩 카드는 여기서 돌리지 않는다 — 끝나면 암전·귀환 흐름까지 이어져 뒤 실측을 망쳤다(09-28). 엔딩은 전용 흐름으로 본다.)
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 기타 HUD 실측 예외: " + e); }
        finally
        {
            foreach (var t in temps) if (t != null) UnityEngine.Object.Destroy(t);
            if (presenter != null && prevMode != HUDIds.Mode.None) presenter.SetMode(prevMode);
            RestoreTestHubGui();
            Debug.Log("[UILayoutRuntimeProbe] 기타 HUD 실측 끝 → Temp/ui_shots/Extra_*.png");
        }
    }

    // ── 미니맵 (09-28, ab·ae 실측 「베이스캠프 왼쪽 위 빈 회색 네모」) ─────────
    private const string MinimapOut = "Temp/minimap_probe.txt";

    /// <summary>
    /// 미니맵이 방 범위를 받았는지 · 판이 보이는지 · 플레이어 점이 판 어디에 찍히는지(구석에 붙으면 범위가 다른 방)를 적고
    /// 화면을 찍는다. 베이스캠프(범위 없음 → 숨김)와 런 안(범위 있음 → 점이 판 안) 둘 다에서 돌린다.
    /// 결과: Temp/minimap_probe.txt(덧붙임) · Temp/ui_shots/Minimap_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/미니맵 실측 (플레이 중)")]
    private static void RunMinimap()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중에 실행해야 한다."); return; }
        RunMinimapAsync().Forget();
    }

    private static async UniTaskVoid RunMinimapAsync()
    {
        var sb = new StringBuilder();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        try
        {
            var mm = UIRootBootstrapper.Instance != null ? UIRootBootstrapper.Instance.GetMinimapView() : null;
            if (mm == null) { sb.AppendLine($"[{scene}] 미니맵 없음"); return; }
            var t  = typeof(MinimapView);
            var c  = (Vector3)(t.GetField("_roomCenter", NonPub)?.GetValue(mm) ?? Vector3.zero);
            var hs = (Vector2)(t.GetField("_roomHalfSize", NonPub)?.GetValue(mm) ?? Vector2.zero);
            var pm = t.GetField("_playerMarkerRect", NonPub)?.GetValue(mm) as RectTransform;
            var pl = t.GetField("_playerTransform", NonPub)?.GetValue(mm) as Transform;
            mm.TryGetComponent<CanvasGroup>(out var g);
            sb.AppendLine($"[{scene}] 섹션 켜짐 {mm.gameObject.activeInHierarchy} · 범위 있음 {mm.HasRoom} · 판 알파 {(g != null ? g.alpha : -1f):0.##}");
            sb.AppendLine($"  방 중심 {c} · 반크기 {hs} · 플레이어 {(pl != null ? pl.position.ToString() : "없음")}");
            if (pm != null)
                sb.AppendLine($"  점 {pm.anchoredPosition} (±55 끝이면 구석에 붙음) · 켜짐 {pm.gameObject.activeInHierarchy}");
            HideTestHubGui();
            await UniTask.Delay(200, ignoreTimeScale: true);
            await ShotAsync($"Minimap_{scene}", 0);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RestoreTestHubGui();
            File.AppendAllText(MinimapOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 미니맵 실측 끝 → {MinimapOut}");
        }
    }

    // ── 구역 이름 표시 (09-27, ab 의뢰) ─────────────────────────────────
    /// <summary>화면 위 구역 이름을 띄워 다 나타난 순간 · 사라지는 중 · 교체를 찍는다. 결과: Temp/ui_shots/Zone_*.png</summary>
    [MenuItem("RelicFairy/UI/구역 이름 표시 실측 (플레이 중)")]
    private static void RunZoneTitle()
    {
        var banner = EditorApplication.isPlaying ? UnityEngine.Object.FindFirstObjectByType<ZoneTitleBanner>() : null;
        if (banner == null) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 중(구역 이름 표시 설치됨)에만 동작한다."); return; }
        RunZoneTitleAsync(banner).Forget();
    }

    private static async UniTaskVoid RunZoneTitleAsync(ZoneTitleBanner banner)
    {
        HideTestHubGui();
        try
        {
            // 룬판(정렬 400)이 떠 있으면 구역 이름(150)을 가린다 — 뒤로 가기와 같은 경로로 닫는다.
            var grid = UI_GridPanel.Instance;
            if (grid != null && grid.gameObject.activeInHierarchy)
            {
                typeof(UI_GridPanel).GetMethod("OnBackClicked", NonPub)?.Invoke(grid, null);
                await UniTask.Delay(600, ignoreTimeScale: true);
            }
            banner.Show("장비 공방", "무기를 고르고 벼리는 곳");
            await UniTask.Delay(600, ignoreTimeScale: true);
            await ShotAsync("Zone_Shown", 0);
            await UniTask.Delay(2000, ignoreTimeScale: true);
            await ShotAsync("Zone_FadingOut", 0);
            banner.Show("기억의 제단", "정수로 다음 런의 가능성을 연다");   // 사라지는 중 교체 — 지금 밝기에서 이어 간다
            await UniTask.Delay(500, ignoreTimeScale: true);
            await ShotAsync("Zone_Replaced", 0);
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 구역 이름 실측 실패: " + e); }
        finally { RestoreTestHubGui(); Debug.Log("[UILayoutRuntimeProbe] 구역 이름 실측 끝 → Temp/ui_shots/Zone_*.png"); }
    }

    // ── 룬판 좌측 열 · 시너지 설명 · 상점 액자 (09-28) ─────────────────────
    private const string LeftShopOut = "Temp/left_shop_probe.txt";

    /// <summary>
    /// 룬판을 열어 좌측 열을 찍고(칩 높이 로그), 속성 시너지 행에 손을 얹어 설명(툴팁)이 판 위에 뜨는지 찍는다.
    /// 이어서 상점을 열어 여는 순간(60·160ms)과 다 열린 뒤(900ms)를 찍는다. 결과: Temp/left_shop_probe.txt · Temp/ui_shots/LS_*.png
    /// </summary>
    [MenuItem("RelicFairy/UI/룬판 좌측·시너지 설명·상점 액자 실측 (런 중)")]
    private static void RunLeftShop()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunLeftShopAsync().Forget();
    }

    private static async UniTaskVoid RunLeftShopAsync()
    {
        HideTestHubGui();
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        GameObject shopGo = null;
        try
        {
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var grid = UI_GridPanel.Instance;
            grid?.Open();
            await UniTask.Delay(1000, ignoreTimeScale: true);
            await ShotAsync("LS_Grid", 0);

            var info = UnityEngine.Object.FindFirstObjectByType<CharacterInfoPanelView>();
            if (info != null)
                foreach (var le in info.GetComponentsInChildren<LayoutElement>(false))
                    sb.AppendLine($"칩 {le.name} 높이 {((RectTransform)le.transform).rect.height:0.#} (목표 34)");
            foreach (var n in new[] { "EffectsHdr", "GuideHeader" })
                foreach (var t in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                    if (t.name == n) sb.AppendLine($"머리띠 {n} 높이 {t.rect.height:0.#} (목표 32)");

            // 속성 시너지 행에 손을 얹는다 — 설명이 판(룬 칸) 위에 떠야 한다
            var syn  = UnityEngine.Object.FindFirstObjectByType<MerlinRuneSynergyStatusView>();
            var rows = typeof(MerlinRuneSynergyStatusView).GetField("_rows", NonPub)?.GetValue(syn) as System.Collections.IDictionary;
            GameObject rowGo = null;
            if (rows != null)
                foreach (System.Collections.DictionaryEntry kv in rows)
                {
                    var go = kv.Value?.GetType().GetField("go")?.GetValue(kv.Value) as GameObject;
                    if (go == null) continue;
                    if (rowGo == null || (string)kv.Key == "GRASS") rowGo = go;
                }
            if (rowGo != null)
            {
                var ped = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(rowGo, ped, ExecuteEvents.pointerEnterHandler);
                await UniTask.Delay(300, ignoreTimeScale: true);
                await ShotAsync("LS_SynergyTooltip", 0);
                var tip = grid != null ? grid.transform.Find("SynergyTooltip") as RectTransform : null;
                sb.AppendLine(tip == null ? "시너지 설명: 룬판 루트 아래에 없음"
                    : $"시너지 설명: 부모 {tip.parent.name} · 맨 위 {tip.GetSiblingIndex() == tip.parent.childCount - 1} · 크기 {tip.rect.size}");
                ExecuteEvents.Execute(rowGo, ped, ExecuteEvents.pointerExitHandler);
            }
            else sb.AppendLine("시너지 행을 못 찾음");

            grid?.Close();
            await UniTask.Delay(700, ignoreTimeScale: true);

            // 상점 — 여는 순간 · 다 열린 뒤
            run?.PlayerState?.AddTempGold(300);
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shopGo = new GameObject("~ProbeShopFrame");
            var sc = shopGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", NonPub)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", NonPub)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(11)));
            shop.Bind(sc);
            await ShotSeriesAsync("LS_ShopOpen", new[] { 60, 160, 900 }, null);
            var win = shop.transform.Find("Window");
            sb.AppendLine($"상점 액자: 그림자 {win?.Find("FrameShadow") != null} · 테두리 {win?.Find("FrameLine") != null} · 가장자리 음영 {win?.Find("FrameEdgeShade") != null}");
            if (shop.transform.Find("Veil") is RectTransform vr && vr.TryGetComponent<Image>(out var veil))
                sb.AppendLine($"상점 막 알파 {veil.color.a:0.00} (목표 0.95)");
            foreach (var img in shop.GetComponentsInChildren<Image>(false))
            {
                if (img.name == "Card0")
                    sb.AppendLine($"카드 틀 배율 {img.pixelsPerUnitMultiplier:0.00} · 카드 폭 {img.rectTransform.rect.width:0}");
                else if (img.name == "Tag0" && img.transform.parent.name == "Card0")
                    sb.AppendLine($"등급 태그 폭 {img.rectTransform.rect.width:0}");
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (shopGo != null) UnityEngine.Object.Destroy(shopGo);
            RestoreTestHubGui();
            File.WriteAllText(LeftShopOut, sb.ToString());
            Debug.Log($"[UILayoutRuntimeProbe] 룬판 좌측·상점 액자 실측 끝 → {LeftShopOut}");
        }
    }

    private const string LobbyOutFile = "ui_layout_probe_lobby.json";

    /// <summary>
    /// 로비 씬 화면 — 메인 메뉴와 세이브 슬롯 패널. 플레이 중 게임과 같은 경로(RequestLoad)로 로비를 불러 잰다.
    /// 에디터에 열린 씬은 바뀌지 않는다(플레이 모드 런타임 로드).
    /// </summary>
    [MenuItem("RelicFairy/UI/레이아웃 실측 — 로비 화면 (플레이 중)")]
    private static void RunLobby()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[UILayoutRuntimeProbe] 플레이 모드에서 실행해야 한다."); return; }
        RunLobbyAsync().Forget();
    }

    private static async UniTaskVoid RunLobbyAsync()
    {
        var sb = new StringBuilder(1 << 18);
        sb.Append("{\"screens\":[");
        bool first = true;
        int screens = 0;
        try
        {
            if (UnityEngine.Object.FindFirstObjectByType<UI_Lobby>() == null)
            {
                AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
                for (int i = 0; i < 300 && UnityEngine.Object.FindFirstObjectByType<UI_Lobby>() == null; i++)
                    await UniTask.Delay(100, ignoreTimeScale: true);
                await UniTask.Delay(5000, ignoreTimeScale: true);   // 부트 커버·타이틀 페이드가 걷히도록
            }
            var lobby = UnityEngine.Object.FindFirstObjectByType<UI_Lobby>();
            if (lobby == null) { Debug.LogWarning("[UILayoutRuntimeProbe] 로비(UI_Lobby)를 못 찾았다."); return; }
            var root = (lobby.GetComponentInParent<Canvas>()?.rootCanvas.transform ?? lobby.transform) as RectTransform;

            await ShotAsync("Lobby_Main", 300);
            if (root != null && Dump(root, "Lobby_Main", sb, ref first)) screens++;

            typeof(UI_Lobby).GetMethod("OnClickStartRun", NonPub)?.Invoke(lobby, null);
            await UniTask.Delay(700, ignoreTimeScale: true);
            await ShotAsync("Lobby_SaveSlots", 0);
            if (root != null && Dump(root, "Lobby_SaveSlots", sb, ref first)) screens++;
            (typeof(UI_Lobby).GetField("saveSlotPanel", NonPub)?.GetValue(lobby) as UI_SaveSlotPanel)?.Close();
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 로비 실측 실패: " + e.Message); }
        finally
        {
            sb.Append("]}");
            string outPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", LobbyOutFile);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[UILayoutRuntimeProbe] 로비 화면 {screens}개 실측 완료 → {outPath}");
        }
    }

    // ── 런 화면(런 안에서만 뜨는 코드 생성 화면) ───────────────────────────────
    private const string RunOutFile = "ui_layout_probe_run.json";
    private const System.Reflection.BindingFlags NonPub = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    /// <summary>
    /// 런 안에서만 뜨는 화면 — ESC 메뉴 · 이벤트 챌린지 HUD · 런 종료 메시지 · 심연 루프 선택 · 멀린 부활.
    /// 전부 코드 생성이라 팝업 경로(<see cref="Run"/>)로는 안 잡힌다. 게임이 부르는 그 메서드를 그대로 불러 띄우고,
    /// 떠 있는 동안 재고 찍은 뒤 취소 토큰으로 닫는다(각 메서드의 finally가 오버레이를 치운다).
    /// </summary>
    [MenuItem("RelicFairy/UI/레이아웃 실측 — 런 화면 (런 중·플레이 중)")]
    private static void RunInRun()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance == null)
        {
            Debug.LogWarning("[UILayoutRuntimeProbe] 런에 들어간 뒤 실행해야 한다(GameRunBootstrapper가 있어야 런 화면을 띄운다).");
            return;
        }
        RunInRunAsync().Forget();
    }

    private static async UniTaskVoid RunInRunAsync()
    {
        var sb = new StringBuilder(1 << 18);
        sb.Append("{\"screens\":[");
        bool first = true;
        int screens = 0;
        var boot = GameRunBootstrapper.Instance;

        // ① ESC 메뉴 — 자체 캔버스(SystemModal)
        try
        {
            var esc = UnityEngine.Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (esc != null)
            {
                esc.Open();
                await UniTask.DelayFrame(SettleFrames);
                await ShotAsync("Run_EscMenu", 200);
                if (esc.transform.Find("EscMenuRoot") is RectTransform ert && Dump(ert, "Run_EscMenu", sb, ref first)) screens++;
                esc.Close();
            }
            else Debug.LogWarning("[UILayoutRuntimeProbe] UI_EscMenu 없음");
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] ESC 메뉴 실패: " + e.Message); }
        await UniTask.DelayFrame(2);

        // ①-2 룬판(런 상태) — 허브에선 캐릭터 정보가 비어 「—」로 찍힌다. 런에선 실제 유물 이름·스탯·보관함으로 잰다.
        try
        {
            if (UI_GridPanel.Instance == null) Managers.UI.ShowOverlayUI<UI_GridPanel>();
            await UniTask.DelayFrame(SettleFrames);
            var grid = UI_GridPanel.Instance;
            var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
            var added = new List<RuntimeItemData>();
            if (inv != null && inv.StagingCount == 0)
                foreach (var (data, _) in SampleRunes(3)) if (inv.AddToStaging(data)) added.Add(data);
            if (grid != null && grid.transform is RectTransform grt)
            {
                grid.Open();
                await UniTask.DelayFrame(SettleFrames * 3);
                await ShotAsync("Run_GridPanel", 700);   // 열기 연출(0.45초)이 끝난 뒤
                if (Dump(grt, "Run_GridPanel", sb, ref first)) screens++;
                grid.Close();
            }
            foreach (var d in added) inv.DiscardFromStaging(d);
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 런 룬판 실패: " + e.Message); }
        await UniTask.DelayFrame(3);

        // ①-3 방 콘텐츠(런 데이터) — 허브에선 컨트롤러가 없어 숫자·게이지가 빈 채로 찍힌다. 게임과 같은 결속으로 연다.
        var run = boot.Run;
        // 재련소 — 디버그 패널(DebugStageRunPanel.OpenCrucibleTestAsync)과 같은 방식: 임시 컨트롤러 + Bind
        GameObject crucibleGo = null;
        try
        {
            crucibleGo = new GameObject("~ProbeCrucible");
            var ctrl = crucibleGo.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(3), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.DelayFrame(SettleFrames * 2);
            await ShotAsync("Run_CruciblePanel", 300);
            if (panel.transform is RectTransform crt && Dump(crt, "Run_CruciblePanel", sb, ref first)) screens++;
            typeof(UI_CruciblePanel).GetMethod("SelectTab", NonPub)?.Invoke(panel, new object[] { 1 });
            await UniTask.DelayFrame(SettleFrames * 2);
            await ShotAsync("Run_CruciblePanel#ranged", 300);
            if (panel.transform is RectTransform crt2 && Dump(crt2, "Run_CruciblePanel#ranged", sb, ref first)) screens++;
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 런 재련소 실패: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (crucibleGo != null) UnityEngine.Object.Destroy(crucibleGo);
        }
        await UniTask.DelayFrame(5);

        // 정제소 — 원석이 모자라면 돌리기 버튼이 비활성이라 실제 상태가 안 보인다. 디버그 패널처럼 20 지급.
        try
        {
            if (run?.FuelBank != null && run.FuelBank.RuneOre < 8) run.FuelBank.Add(FuelKind.RuneOre, 20);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            await UniTask.DelayFrame(SettleFrames * 2);
            await ShotAsync("Run_RefineryPanel", 300);
            if (panel != null && panel.transform is RectTransform rrt && Dump(rrt, "Run_RefineryPanel", sb, ref first)) screens++;
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 런 정제소 실패: " + e.Message); }
        finally { Managers.UI.CloseAllPopupUI(); }
        await UniTask.DelayFrame(5);

        // 상점 — 카드 하나를 고른 상태(오른쪽 상세판이 채워진 모습). 런을 컨트롤러에 꽂아 골드·가격 색까지 실제와 같게.
        GameObject shopGo = null;
        try
        {
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shopGo = new GameObject("~ProbeShop");
            var sc = shopGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", NonPub)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", NonPub)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(7)));
            // 실게임처럼 새로고침을 켠다(@GameRun shopRerollEnabled=1·비용 10). 안 켜면 버튼이 빠진 채 찍혀
            // 카드 아래가 실제보다 비어 보였다(09-25 오진 직전).
            typeof(ShopRoomController).GetField("_rerollEnabled", NonPub)?.SetValue(sc, true);
            typeof(ShopRoomController).GetField("_rerollCost", NonPub)?.SetValue(sc, 10);
            shop.Bind(sc);
            await UniTask.Delay(800, ignoreTimeScale: true);   // 열기 연출(카드 진열 약 0.6초)이 끝난 뒤에 잰다
            await UniTask.DelayFrame(SettleFrames);
            foreach (int idx in new[] { 0, 3 })   // 룬 카드 · 두 번째 줄 카드
            {
                typeof(UI_ShopPanel).GetMethod("Select", NonPub)?.Invoke(shop, new object[] { idx });
                await UniTask.DelayFrame(SettleFrames);
                string tag = "Run_ShopSelected#" + idx;
                await ShotAsync(tag, 200);
                if (shop.transform is RectTransform srt && Dump(srt, tag, sb, ref first)) screens++;
            }
        }
        catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 런 상점 실패: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (shopGo != null) UnityEngine.Object.Destroy(shopGo);
        }
        await UniTask.DelayFrame(5);

        // ② 이벤트 챌린지 HUD — 목표 문구가 가장 긴 두 유형(생존·무결)을 게임 문구 그대로
        foreach (var (tag, objective, status) in new[]
                 {
                     ("Run_ChallengeHud", "[생존] 높은 체력으로 클리어 — 남은 HP가 많을수록 높은 등급", "HP 87% · 플래티넘"),
                     ("Run_ChallengeHud#timer", "[속공] 제한시간 90초 — 빠를수록 높은 등급", "경과 12.3초 · 골드"),
                 })
        {
            UI_ChallengeHud hud = null;
            try
            {
                hud = UI_ChallengeHud.Create();
                hud.SetObjective(objective);
                hud.SetStatus(status);
                await UniTask.DelayFrame(SettleFrames);
                await ShotAsync(tag, 200);
                if (hud.transform is RectTransform hrt && Dump(hrt, tag, sb, ref first)) screens++;
            }
            catch (Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 챌린지 HUD 실패: " + e.Message); }
            finally { if (hud != null) hud.Close(); }
            await UniTask.DelayFrame(2);
        }

        // ③ 런 종료 메시지(클리어) · ④ 심연 루프 선택 · ⑤ 멀린 부활 — 게임 메서드를 띄운 채 재고 취소로 닫는다
        foreach (var (method, args, root, tag, waitMs) in new (string, bool, string, string, int)[]
                 {
                     ("ShowRunEndMessageAsync", true,  "@RunEndMessage",   "Run_RunEndMessage", 600),
                     ("ShowAbyssLoopChoiceAsync", false, "@AbyssLoopChoice", "Run_AbyssLoopChoice", 400),
                     ("ShowMerlinRevivalAsync", false, "@MerlinRevival",   "Run_MerlinRevival", 3600),
                 })
        {
            var cts = new System.Threading.CancellationTokenSource();
            try
            {
                var mi = typeof(GameRunBootstrapper).GetMethod(method, NonPub);
                if (mi == null) { Debug.LogWarning($"[UILayoutRuntimeProbe] {method} 없음"); continue; }
                object[] a = method == "ShowRunEndMessageAsync" ? new object[] { args, cts.Token } : new object[] { cts.Token };
                object task = mi.Invoke(boot, a);
                await UniTask.Delay(waitMs, ignoreTimeScale: true);
                await ShotAsync(tag, 0);
                var go = GameObject.Find(root);
                if (go != null && go.transform is RectTransform grt && Dump(grt, tag, sb, ref first)) screens++;
                else Debug.LogWarning($"[UILayoutRuntimeProbe] {root}를 못 찾았다(이미 닫혔나?)");
                cts.Cancel();
                try
                {
                    if (task is UniTask ut) await ut;
                    else if (task is UniTask<bool> ub) await ub;
                }
                catch (OperationCanceledException) { }
            }
            catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] {tag} 실패: " + e.Message); }
            finally { cts.Dispose(); }
            await UniTask.DelayFrame(3);
        }

        sb.Append("]}");
        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", RunOutFile);
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[UILayoutRuntimeProbe] 런 화면 {screens}개 실측 완료 → {outPath}");
    }

    /// <summary>
    /// 데이터형 팝업에 실제 자산으로 만든 표본을 먹인다. 전부 best-effort — 하나가 실패해도 다음 화면 실측은 계속된다.
    /// 유물 SO·무기 SO는 에디터 AssetDatabase로, 파츠 풀은 Managers.RelicParts로, 룬은 정제소의 존핵 생성기로 만든다.
    /// </summary>
    /// <summary>표본을 먹이려고 만든 임시 씬 오브젝트 — 실측이 끝나면 지운다.</summary>
    private static readonly System.Collections.Generic.List<GameObject> _tempFeedObjects = new();

    private static void FeedSampleData(UI_Popup popup)
    {
        try
        {
            switch (popup)
            {
                case UI_CovenantAssemble covenant:
                    // 서약은 Setup이 돌아야 완성본 아트(두루마리)로 바뀌고 책 크기가 잡힌다 — 빈 상태로 재면 프리즘이 남는다.
                    covenant.Setup(false, new System.Random(1));
                    break;
                case UI_RelicInfoPopup relicInfo:
                {
                    var relic = FirstAsset<RelicClassSO>();
                    if (relic != null) relicInfo.Setup(relic, () => { });
                    break;
                }
                case UI_ShopPanel shop:
                {
                    // 상점은 <b>행상 진열(AbyssPeddlerCatalog)</b>을 읽는다. 빈 채로 열면 카드가 어두운 빈 판이라
                    // 합성본과 대조가 불가능했다("배치 결함인지 표본 탓인지 모른다"가 여기서 계속 걸렸다).
                    // 게임이 쓰는 그 빌더로 진열을 만들어 컨트롤러에 꽂는다(방 시드 고정 = 매번 같은 진열).
                    var shopGo   = new GameObject("~ProbeShopController");
                    var shopCtrl = shopGo.AddComponent<ShopRoomController>();
                    var peddlerF = typeof(ShopRoomController).GetField("_peddler",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    peddlerF?.SetValue(shopCtrl, AbyssPeddlerCatalog.Build(new System.Random(7)));
                    shop.Bind(shopCtrl);
                    _tempFeedObjects.Add(shopGo);
                    break;
                }
                case UI_RangedForgePopup ranged:
                {
                    // 빈 채로 열면 카드가 접혀 있어 <b>스탯이 무대를 넘치는 것도, 테마색 판도 안 보인다</b>
                    // (2026-09-10 게임 화면에서 드러난 결함을 프로브가 못 잡던 이유). 실제 무기 2종을 먹인다 —
                    // 하나는 잠금으로 두어 배지 줄이 생겼을 때의 높이까지 함께 잰다.
                    var rw = Assets<WeaponSO>(2);
                    if (rw.Count > 0)
                    {
                        var list = new System.Collections.Generic.List<UI_RangedForgePopup.Entry>
                        {
                            new() { Weapon = rw[0] },
                        };
                        if (rw.Count > 1)
                            list.Add(new UI_RangedForgePopup.Entry { Weapon = rw[1], Locked = true, LockReason = "제단 해금 필요" });
                        ranged.Setup(list);
                    }
                    break;
                }
                case UI_RelicPartDraftPopup partDraft:
                {
                    var pool = Managers.RelicParts?.GetDraftPool("gawain", 1, new List<string>());
                    if (pool != null && pool.Count > 0) partDraft.Setup(pool.GetRange(0, Math.Min(3, pool.Count)));
                    break;
                }
                case UI_RuneSelectPopup runeSelect:
                {
                    // 실제 룬(ITEM_DATA 효과가 있는 ItemSO)으로 채운다 — 존핵만 넣으면 셋 다 등급 각인석 하나로 보여
                    // 아이템별 문양이 제대로 붙는지 확인할 수 없다(2026-09-09 지적).
                    var candidates = SampleRunes(3);
                    // 등급마다 1장 — 뒷면·테두리·전설 광선까지 한 화면에서 잰다(같은 등급 셋이면 등급 표현을 못 본다).
                    var byRarity = new List<(RuntimeItemData, ItemSO)>();
                    foreach (var rarity in new[] { ItemRarity.Common, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary })
                        foreach (var so in Assets<ItemSO>(400))
                        {
                            if (so == null || so.rarity != rarity) continue;
                            var d = RuntimeItemData.FromSO(so);
                            if (d?.effects == null || d.effects.Count == 0) continue;
                            byRarity.Add((d, so));
                            break;
                        }
                    if (byRarity.Count >= 3) candidates = byRarity;
                    if (candidates.Count > 0)
                    {
                        runeSelect.Setup(candidates, new RunItemInventory());
                        // 공개 연출(뒷면→뒤집기)을 건너뛰어 끝 상태를 잰다 — 3프레임 뒤엔 아직 뒷면이다.
                        typeof(UI_RuneSelectPopup).GetMethod("SkipReveal", NonPub)?.Invoke(runeSelect, null);
                    }
                    break;
                }
                case UI_DialoguePopup dialogue:
                {
                    // 이미지형 띠 검증용 표본 — 역할 태그 문구는 프로브 전용 자리표시자(기획 문구 아님).
                    var t = typeof(UI_DialoguePopup);
                    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    if (t.GetField("speakerTitles", F)?.GetValue(dialogue) is string[] titles && titles.Length > (int)DialogueSpeaker.Merlin)
                        titles[(int)DialogueSpeaker.Merlin] = "(역할)";
                    t.GetMethod("SetSpeakerName", F)?.Invoke(dialogue, new object[] { DialogueSpeaker.Merlin });
                    if (t.GetField("bodyText", F)?.GetValue(dialogue) is TMPro.TextMeshProUGUI body)
                    {
                        body.text = "여행자, 나와 시게루, 누구의 생각이 더 맞는 거 같아? 두 줄 상한을 보려고 일부러 길게 적은 표본 문장이다.";
                        // 띠 진단 — 생성 스프라이트·렌더러 상태를 남긴다(띠가 안 보이던 09-09 새벽 문제 추적).
                        if (body.transform.parent is RectTransform box && box.TryGetComponent<Image>(out var band))
                        {
                            var tex = band.sprite != null ? band.sprite.texture : null;
                            string px = tex != null && tex.isReadable ? tex.GetPixel(48, 48).ToString() : "(unreadable)";
                            Debug.Log($"[UILayoutRuntimeProbe] 대화 띠: sprite={(band.sprite ? band.sprite.name : "null")} tex={(tex ? tex.width + "x" + tex.height : "null")} px48={px} " +
                                      $"color={band.color} type={band.type} border={(band.sprite ? band.sprite.border.ToString() : "-")} enabled={band.enabled} " +
                                      $"crAlpha={band.canvasRenderer.GetAlpha()} cull={band.canvasRenderer.cull} mat={(band.materialForRendering ? band.materialForRendering.name : "null")} " +
                                      $"rect={box.rect} canvas={(band.canvas ? band.canvas.renderMode.ToString() : "null")} group={(box.GetComponentInParent<CanvasGroup>() ? box.GetComponentInParent<CanvasGroup>().alpha.ToString() : "-")}");
                        }
                    }
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UILayoutRuntimeProbe] {popup.GetType().Name} 표본 Setup 실패 — {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>효과가 있는 실제 룬 표본 N개(속성·효과가 서로 다르게). ITEM_DATA가 없으면 빈 목록.</summary>
    private static List<(RuntimeItemData data, ItemSO so)> SampleRunes(int count)
    {
        var result = new List<(RuntimeItemData, ItemSO)>();
        var seenEffect = new HashSet<string>();
        foreach (var so in Assets<ItemSO>(200))
        {
            if (so == null || string.IsNullOrEmpty(so.itemId)) continue;
            RuntimeItemData data;
            try { data = RuntimeItemData.FromSO(so); } catch { continue; }
            if (data == null || data.effects == null || data.effects.Count == 0) continue;
            string key = data.effects[0].effectType ?? "";
            if (!seenEffect.Add(key)) continue;
            result.Add((data, so));
            if (result.Count >= count) break;
        }
        return result;
    }

    private static T FirstAsset<T>() where T : UnityEngine.Object
    {
        var list = Assets<T>(1);
        return list.Count > 0 ? list[0] : null;
    }

    private static List<T> Assets<T>(int max) where T : UnityEngine.Object
    {
        var result = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) result.Add(asset);
            if (result.Count >= max) break;
        }
        return result;
    }

    /// <summary>팝업 루트를 <b>루트 캔버스 좌표</b>로 걷어 기록한다. 캔버스 크기도 함께 남겨 화면 배율을 되짚을 수 있게 한다.</summary>
    /// <summary>
    /// 게임 뷰를 PNG로 남긴다 — 가독성·배경처럼 수치로 못 잡는 것은 눈으로 봐야 한다.
    /// 백버퍼는 프레임 끝에서만 읽힌다(그 전엔 검은 그림). 정적 클래스라 씬의 MonoBehaviour(Managers)를 코루틴 러너로 빌린다.
    /// </summary>
    /// <summary>
    /// 해금 순간 연출을 <b>구매 없이</b> 미리 본다: 첫 열에서 차례 행과 그 다음 행을 찾아
    /// 빛 흐름(PlayFlowDownAsync)·승격(PlayBecomeTurnAsync)만 돌리고 중간 프레임을 찍는다. 저장·정수는 건드리지 않는다.
    /// </summary>
    private static async UniTask PreviewUnlockFxAsync(UI_Popup popup, string name)
    {
        try
        {
            var rowsField = typeof(UI_AwakeningPanel).GetField("_rows",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (!(rowsField?.GetValue(popup) is System.Collections.Generic.List<System.Collections.Generic.List<AltarNodeRowView>> rows)) return;
            AltarNodeRowView turn = null, next = null;
            foreach (var col in rows)
            {
                for (int i = 0; i < col.Count - 1; i++)
                {
                    if (col[i] != null && col[i].CurrentHeight >= 70f) { turn = col[i]; next = col[i + 1]; break; }
                }
                if (turn != null) break;
            }
            if (turn == null || next == null) return;

            var ct = popup.GetCancellationTokenOnDestroy();
            float nextTo = next.CurrentHeight;
            // 연출은 0.35초·0.30초짜리다 — 스크린샷의 기본 안정 대기(0.5초)를 쓰면 끝난 뒤 정지 화면만 찍힌다.
            // 연출 시계를 프레임당 1/60초로 고정해 "몇 프레임째"가 곧 "연출 몇 %"가 되게 한다(에디터 fps 무관).
            // (Time.captureFramerate는 unscaledDeltaTime을 고정하지 않아 소용없었다.)
            AltarNodeRowView.FxStepOverride = 1f / 60f;
            try
            {
                next.SetHeightImmediate(34f);
                var flow = turn.PlayFlowDownAsync(turn.CurrentHeight * 0.5f + 22f + 34f * 0.5f, ct);
                await UniTask.DelayFrame(11);                       // ≈0.18s / 0.35s
                await ShotAsync(name + "#fx_flow", 0);
                await flow;
                // 실제 흐름은 Refresh가 새 높이(차례 74)를 먼저 잡고 옛 높이(34)에서 트윈한다 — 같은 순서로 흉내 낸다.
                next.SetHeightImmediate(turn.CurrentHeight);
                var grow = next.PlayBecomeTurnAsync(34f, ct);
                await UniTask.DelayFrame(8);                        // ≈0.13s / 0.30s
                await ShotAsync(name + "#fx_grow", 0);
                await grow;
            }
            finally { AltarNodeRowView.FxStepOverride = 0f; }
            next.SetHeightImmediate(nextTo);
            await UniTask.DelayFrame(SettleFrames);
        }
        catch (System.Exception e) { Debug.LogWarning("[UILayoutRuntimeProbe] 해금 연출 미리보기 실패: " + e.Message); }
    }

    /// <summary>
    /// 런 HUD 표본. 재화·체력·물약·스탯·무기 슬롯에 <b>실제 자릿수</b>를 먹인다 —
    /// 슬롯 값은 런타임에 덮어써지므로 프리팹 값만 보면 넘침·빈 칸을 못 잡는다.
    /// </summary>
    /// <summary>
    /// 프로브 전용 유물 자원 표본. 광기 게이지(검 실루엣 바)는 유물이 바인딩돼야 나타나므로
    /// 이 표본이 없으면 <b>체력바 아래 절반이 빈 채로 측정</b>된다(2026-09-10 게임 화면에서 겹침이 드러난 자리).
    /// </summary>
    private sealed class SampleRelicResource : IRelicResource
    {
        public float           Fill         => 0.62f;
        public RelicGaugeStyle Style        => RelicGaugeStyle.Bar;
        public string          Label        => "광기 62%";
        public int             Phase        => 1;
        public Color           BarColor     => new(0.95f, 0.35f, 0.25f, 1f);
        public bool            IsSkillReady => false;
        public event Action OnChanged { add { } remove { } }
        public void Tick(float deltaTime) { }
        public void OnAttackLanded(GameObject target) { }
        public void OnKill(GameObject target) { }
        public RelicResourceState Capture() => default;
        public void Restore(RelicResourceState state) { }
        public void ApplyConfig(RelicResourceConfig config) { }
    }

    private static void FeedHudSample(HudView view)
    {
        view.SetGold(128450);
        view.SetEnhanceMaterial(3280);
        view.SetRuneOre(742);
        view.SetEssence(12960);

        var combat = view.CombatPanel;
        if (combat == null) return;

        combat.SetHp(834, 1250);
        combat.SetPotion(2, 3);
        combat.SetStats(146, 88);
        combat.SetRevive(true, true);
        combat.SetWeaponSlot(0, new WeaponSlotInfo { HasWeapon = true, Name = "여명의 대검", Attack = 146f, Defense = 12f });
        combat.SetWeaponSlot(1, new WeaponSlotInfo { HasWeapon = true, Name = "심연의 장궁", Attack = 121f, Defense = 8f });
        combat.SetActiveWeapon(0);
        combat.SetSkillCooldown(SkillType.E, 4.2f, 9f);
        combat.SetSkillLocked(SkillType.R, true);
        combat.SetRelicResource(new SampleRelicResource());   // 광기 게이지(검 실루엣) — 체력바와의 간격을 재려면 필요
    }

    private static async UniTask ShotAsync(string name, int settleMs = 500)
    {
        try
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
            if (runner == null) return;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ui_shots");
            Directory.CreateDirectory(dir);
            // 에디터 게임 뷰는 어떤 프레임엔 백버퍼가 비어(검게) 읽힌다 — 검으면 다음 프레임을 다시 읽는다(최대 8회).
            // 열림 연출(암막 페이드)이 있는 창은 첫 프레임들이 검다 — 반 초 기다렸다가 읽고, 검으면 더 기다린다.
            if (settleMs > 0) await UniTask.Delay(settleMs, ignoreTimeScale: true);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await UniTask.WaitForEndOfFrame(runner);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) continue;
                bool black = true;
                for (int y = 0; y < tex.height && black; y += Mathf.Max(1, tex.height / 12))
                    for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                        if (tex.GetPixel(x, y).maxColorComponent > 0.02f) { black = false; break; }
                if (!black || attempt == 19)
                {
                    File.WriteAllBytes(Path.Combine(dir, name.Replace('#', '_') + (black ? "_black" : "") + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
                UnityEngine.Object.Destroy(tex);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Debug.LogWarning($"[UILayoutRuntimeProbe] {name} 스크린샷 실패 — {e.Message}"); }
    }

    private static bool Dump(RectTransform popupRT, string name, StringBuilder sb, ref bool firstScreen)
    {
        if (popupRT == null) return false;
        var canvas = popupRT.GetComponentInParent<Canvas>();
        var rootRT = canvas != null ? canvas.rootCanvas.transform as RectTransform : popupRT;
        if (rootRT == null) rootRT = popupRT;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(popupRT);

        // 화면 하나는 지역 버퍼에 만든다 — 중간에 예외가 나면 반쪽짜리 JSON이 파일 전체를 깨뜨린다.
        var local = new StringBuilder(1 << 16);
        local.Append("{\"prefab\":\"").Append(UILayoutProbeEditor.Esc(name)).Append("\",\"runtime\":true")
             .Append(",\"canvas\":{\"w\":").Append(UILayoutProbeEditor.F(rootRT.rect.width))
             .Append(",\"h\":").Append(UILayoutProbeEditor.F(rootRT.rect.height))
             .Append(",\"rootScale\":").Append(UILayoutProbeEditor.F(popupRT.lossyScale.x / Mathf.Max(rootRT.lossyScale.x, 1e-6f))).Append('}')
             .Append(",\"nodes\":[");
        int order = 0;
        bool first = true;
        // 팝업 루트 자신도 한 줄 남긴다 — fitter가 키운 창 크기를 바로 읽기 위해.
        UILayoutProbeEditor.Emit(popupRT, rootRT, popupRT.name, 0, order, local);
        first = false;
        UILayoutProbeEditor.Walk(popupRT, rootRT, popupRT.name, 0, ref order, local, ref first);
        local.Append("]}");

        if (!firstScreen) sb.Append(',');
        firstScreen = false;
        sb.Append(local);
        return true;
    }
}
