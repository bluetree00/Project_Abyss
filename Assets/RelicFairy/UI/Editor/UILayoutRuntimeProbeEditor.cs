using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
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
        ("UI_ItemAcquisitionPopup",async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_ItemAcquisitionPopup>()),
        ("UI_RelicPartDraftPopup", async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>()),
        ("UI_WeaponReplacePopup",  async () => await Managers.UI.ShowPopupUIAndGetAsync<UI_WeaponReplacePopup>()),
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
                case UI_WeaponReplacePopup weaponReplace:
                {
                    var weapons = Assets<WeaponSO>(3);
                    if (weapons.Count >= 1)
                    {
                        var w0 = new WeaponData(weapons[0]);
                        var w1 = new WeaponData(weapons[Math.Min(1, weapons.Count - 1)]);
                        var w2 = new WeaponData(weapons[Math.Min(2, weapons.Count - 1)]);
                        weaponReplace.Setup(w0, w1, w2);
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
                    if (candidates.Count == 0)
                    {
                        var order = ElementDef.Order;
                        for (int i = 0; i < order.Count && candidates.Count < 3; i++)
                        {
                            var data = RefineryService.CreateZoneCore(order[i], ItemRarity.Rare);
                            if (data != null) candidates.Add((data, ItemSORegistry.Find(data.itemId)));
                        }
                    }
                    if (candidates.Count > 0) runeSelect.Setup(candidates, new RunItemInventory());
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
                case UI_ItemAcquisitionPopup acquisition:
                {
                    var data = RefineryService.CreateZoneCore(ElementDef.Order[0], ItemRarity.Rare);
                    if (data != null) acquisition.Setup(data, new RunItemInventory());
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
