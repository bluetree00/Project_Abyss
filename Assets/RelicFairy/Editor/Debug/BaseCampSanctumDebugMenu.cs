#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 베이스캠프 새 지형(<c>BaseCamp_Sanctum</c>, 재설계 「멀린의 공간」)을 <b>플레이 중</b> 걸어 보기 위한 이동 메뉴(09-27).
/// 스폰·기능 연결(3단계) 전이라 게임 흐름으로는 새 지형에 갈 수 없다 — 씬에 놓인 인스턴스로 플레이어를 옮긴다.
/// 메뉴: RelicFairy/Debug/BaseCamp Sanctum/…  · 좌표는 프리팹 로컬(설계 생성기 gen_phase1b 기준).
/// </summary>
public static class BaseCampSanctumDebugMenu
{
    private const string Root = "RelicFairy/Debug/BaseCamp Sanctum/";
    private const string InstanceName = "BaseCamp_Sanctum";

    private static readonly (string name, Vector3 local)[] TourPoints =
    {
        ("1_summon", new Vector3(-3f, 0.3f, 0f)),
        ("2_ascent", new Vector3(15f, 0.3f, 0f)),
        ("3_plaza",  new Vector3(59.4f, 9.3f, 0f)),
        ("4_gate",   new Vector3(85.4f, 9.3f, 0f)),
        ("5_portal", new Vector3(116.6f, 3.3f, 0f)),
    };

    [MenuItem(Root + "1 소환의 방 (Play)")]  public static void ToSummonRoom() => _ = TeleportLocal(TourPoints[0].local);
    [MenuItem(Root + "2 회랑 입구 (Play)")]  public static void ToAscent()     => _ = TeleportLocal(TourPoints[1].local);
    [MenuItem(Root + "3 광장 (Play)")]       public static void ToPlaza()      => _ = TeleportLocal(TourPoints[2].local);
    [MenuItem(Root + "4 성문 앞 (Play)")]    public static void ToGate()       => _ = TeleportLocal(TourPoints[3].local);
    [MenuItem(Root + "5 포탈 발판 (Play)")]  public static void ToPortal()     => _ = TeleportLocal(TourPoints[4].local);

    /// <summary>다섯 지점을 돌며 게임 화면(실제 게임 카메라)을 찍고 접지 여부를 적는다. 결과: Temp/sanctum_view/</summary>
    [MenuItem(Root + "6 다섯 지점 캡처 (Play)")]
    public static async void CaptureTour()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        System.IO.Directory.CreateDirectory("Temp/sanctum_view");
        foreach (var (name, local) in TourPoints)
        {
            var player = TeleportLocal(local);
            if (player == null) return;
            await System.Threading.Tasks.Task.Delay(1500);               // 착지 · 카메라 따라오기
            if (!Application.isPlaying || player == null) return;
            ScreenCapture.CaptureScreenshot($"Temp/sanctum_view/{name}.png");
            Debug.Log($"[SanctumDebug] {name}: 위치 {player.transform.position} · 접지 {player.IsGrounded()}");
            await System.Threading.Tasks.Task.Delay(400);
        }
        Debug.Log("[SanctumDebug] 캡처 완료 → Temp/sanctum_view/");
    }

    /// <summary>「처음 한 번」 연출 기록(멀린의 공간 생성 · 얻기 · 구역 이름)을 모든 슬롯에서 지운다 — 다시 처음처럼 보려면.</summary>
    [MenuItem(Root + "7 처음 연출 기록 지우기")]
    public static void ResetFirstTimeRecords()
    {
        for (int slot = 0; slot < RunProgressManager.SlotCount; slot++)
        {
            BaseCampFxDirector.ClearFirstTimeForSlot(slot);
            ZoneSign.ClearAllForSlot(slot);
        }
        PlayerPrefs.Save();
        Debug.Log("[SanctumDebug] 처음 연출 기록(공간 생성 · 얻기 · 구역 이름) 지움 — 슬롯 0~3");
    }

    private const string FreshFromSlotPref = "SanctumDebug.FreshFromSlot";

    /// <summary>
    /// 빈 세이브 슬롯으로 「새 게임 → 첫 진입」을 재현한다 — 사용자 슬롯은 건드리지 않는다.
    /// 첫 소환 · 무형검 받침 · 공간 생성 · 얻기 처음 연출은 새 슬롯에서만 나와 그동안 실측할 수 없었다(09-28).
    /// 프롤로그는 건너뛰고 베이스캠프로 곧장 간다(프롤로그발 표시만 세운다). 끝나면 11번으로 원래 슬롯에 돌아가며 시험 슬롯을 비운다.
    /// </summary>
    [MenuItem(Root + "10 첫 진입 재현 — 빈 슬롯 (Play)")]
    public static void FreshSlotFirstEntry()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var rpm = RunProgressManager.Instance;
        var app = AppBootstrapper.Instance;
        if (rpm == null || app == null) { Debug.LogWarning("[SanctumDebug] RunProgressManager/AppBootstrapper가 없다 — 부트부터 플레이."); return; }

        int slot = -1;
        for (int s = RunProgressManager.SlotCount - 1; s >= 0; s--)
            if (s != rpm.ActiveSlotIndex && IsSlotEmpty(rpm, s)) { slot = s; break; }
        if (slot < 0) { Debug.LogWarning("[SanctumDebug] 빈 슬롯이 없다 — 사용자 슬롯은 지우지 않는다."); return; }

        EditorPrefs.SetInt(FreshFromSlotPref, rpm.ActiveSlotIndex);
        rpm.ResetSlot(slot);            // 빈 슬롯이라 지울 세이브는 없다 — 처음 연출 기록(PlayerPrefs)만 확실히 비운다
        rpm.ActiveSlotIndex = slot;
        BackendGameData.Instance?.ApplyActiveSlot();
        Managers.Quest?.ReloadForActiveSlot();
        app.Loadout?.Clear();           // 직전 허브에서 고른 무기·파츠·유물이 따라오지 않게
        app.MarkFromIntro();
        app.RequestLoad(Define.Scene.BaseCamp);
        Debug.Log($"[SanctumDebug] 첫 진입 재현 — 빈 슬롯 {slot} (원래 슬롯 {EditorPrefs.GetInt(FreshFromSlotPref)})");
    }

    /// <summary>10번의 시험 슬롯에서 원래 슬롯으로 돌아가고, 시험하며 생긴 시험 슬롯 세이브를 비운다.</summary>
    [MenuItem(Root + "11 원래 슬롯으로 · 시험 슬롯 비우기 (Play)")]
    public static void BackToOriginalSlot()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var rpm = RunProgressManager.Instance;
        if (rpm == null || !EditorPrefs.HasKey(FreshFromSlotPref)) { Debug.LogWarning("[SanctumDebug] 10번으로 들어온 시험 슬롯이 아니다."); return; }

        int test = rpm.ActiveSlotIndex;
        int orig = EditorPrefs.GetInt(FreshFromSlotPref, 0);
        if (test == orig) { Debug.LogWarning("[SanctumDebug] 이미 원래 슬롯이다."); return; }
        rpm.ActiveSlotIndex = orig;
        BackendGameData.Instance?.ApplyActiveSlot();
        Managers.Quest?.ReloadForActiveSlot();
        rpm.ResetSlot(test);            // 10번이 고른 빈 슬롯 — 시험 중 생긴 세이브만 지운다
        EditorPrefs.DeleteKey(FreshFromSlotPref);
        Debug.Log($"[SanctumDebug] 원래 슬롯 {orig}로 복귀 · 시험 슬롯 {test} 비움 — 베이스캠프를 다시 들어가면 원래 슬롯 상태");
    }

    private static bool IsSlotEmpty(RunProgressManager rpm, int slot)
        => !rpm.HasLocalRun(slot)
        && !SaveFileIO.Exists(SaveFileIO.PathFor($"meta_save_{slot}.json"))
        && RunProgressManager.GetRetryCount(slot) == 0;

    /// <summary>소환 연출만 다시 본다 — 처음(소환의 방) 경로는 프롤로그를 거쳐야만 나오므로 실측용으로 제자리 재생.</summary>
    [MenuItem(Root + "9a 소환 — 처음 (Play)")]      public static void SummonFirst() => PlaySummon(BaseCampFxDirector.SummonKind.First);
    [MenuItem(Root + "9b 소환 — 사망 복귀 (Play)")] public static void SummonDeath() => PlaySummon(BaseCampFxDirector.SummonKind.Death);
    [MenuItem(Root + "9c 소환 — 클리어 복귀 (Play)")] public static void SummonClear() => PlaySummon(BaseCampFxDirector.SummonKind.Clear);

    // ── 2차 개편(09-29) — 봉인 전망대 · 기억 성소 실측 ──
    // 좌표 = 생성기 gen_wing.py meta(전망대 가운데에서 섬 쪽 3 m · 성소 책 받침대 앞)
    [MenuItem(Root + "12 봉인 전망대 (Play)")] public static void ToSealTerrace()   => _ = TeleportLocal(new Vector3(96.4f, 9.3f, -21.3f));
    [MenuItem(Root + "13 기억 성소 앞 (Play)")] public static void ToMemorySanctum() => _ = TeleportLocal(new Vector3(76.5f, 9.3f, -24.1f));

    /// <summary>봉인 섬 넷의 겉모습만 바꾼다(저장 없음) — 상태 4종 캡처용.</summary>
    [MenuItem(Root + "14a 봉인 섬 — 풀림 (Play)")]   public static void SealUnsealed() => PreviewSeal(0);
    [MenuItem(Root + "14b 봉인 섬 — 봉인 (Play)")]   public static void SealSealed()   => PreviewSeal(1);
    [MenuItem(Root + "14c 봉인 섬 — 깨짐 (Play)")]   public static void SealBroken()   => PreviewSeal(2);
    [MenuItem(Root + "14d 봉인 섬 — 처치 (Play)")]   public static void SealSlain()    => PreviewSeal(3);
    [MenuItem(Root + "15 봉인 섬 점등 — Ch1 (Play)")] public static void SealIgnite()  => Shrine()?.DebugIgnite(0);
    [MenuItem(Root + "16a 기억 수정 — 열 수 있음 켜기 (Play)")] public static void CrystalReadyOn()  => Crystal()?.DebugForceReady(true);
    [MenuItem(Root + "16b 기억 수정 — 열 수 있음 끄기 (Play)")] public static void CrystalReadyOff() => Crystal()?.DebugForceReady(false);

    [System.Serializable] private sealed class FxCandidate { public string name; public string path; public float scale = 1f; public float y = 0.1f; }
    [System.Serializable] private sealed class FxCandidateList { public FxCandidate[] items; }

    /// <summary>
    /// 파츠 공방 자리에 이펙트 후보를 차례로 세워 <b>게임 화면</b>으로 찍는다(09-30) — 검은 배경 미리보기로 고른 마법진이
    /// 실제 화면에선 불꽃처럼 솟아 대장간 불과 겹쳤다. 목록: Temp/parts_fx_candidates.json · 결과: Temp/parts_fx/&lt;이름&gt;.png
    /// </summary>
    [MenuItem(Root + "17 파츠 공방 이펙트 후보 캡처 (Play)")]
    public static async void CapturePartsFxCandidates()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        const string jobPath = "Temp/parts_fx_candidates.json";
        if (!System.IO.File.Exists(jobPath)) { Debug.LogWarning("[SanctumDebug] " + jobPath + " 없음"); return; }
        var list = JsonUtility.FromJson<FxCandidateList>(System.IO.File.ReadAllText(jobPath));
        var root = GameObject.Find(InstanceName);
        var spot = root != null ? root.transform.Find("Markers/PartsSpot") : null;
        if (spot == null || list?.items == null) { Debug.LogWarning("[SanctumDebug] PartsSpot 또는 후보 목록이 없다."); return; }

        System.IO.Directory.CreateDirectory("Temp/parts_fx");
        if (TeleportLocal(new Vector3(70.5f, 9.3f, 10.2f)) == null) return;   // 파츠 받침에서 회랑 쪽 7 m — 카메라(성문 방향)가 받침을 정면에 둔다
        await System.Threading.Tasks.Task.Delay(1800);
        int shot = 0;
        foreach (var c in list.items)
        {
            if (!Application.isPlaying) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (prefab == null) { Debug.LogWarning("[SanctumDebug] 후보 없음: " + c.path); continue; }
            var go = Object.Instantiate(prefab, spot.position + Vector3.up * c.y, Quaternion.identity);
            go.transform.localScale = Vector3.one * (c.scale <= 0f ? 1f : c.scale);
            await System.Threading.Tasks.Task.Delay(3200);
            if (!Application.isPlaying) return;
            ScreenCapture.CaptureScreenshot($"Temp/parts_fx/{c.name}.png");
            await System.Threading.Tasks.Task.Delay(500);
            if (go != null) Object.Destroy(go);
            await System.Threading.Tasks.Task.Delay(300);
            shot++;
        }
        Debug.Log($"[SanctumDebug] 파츠 이펙트 후보 캡처 완료 — {shot}장 → Temp/parts_fx/");
    }

    /// <summary>
    /// 악몽 모드 갈림길 실측(10-01) — 테스트 오버라이드 「Ended (Nightmare Off)」로 플레이할 것(갈림길은 엔딩 뒤에만 선다).
    /// 갈림길 앞 한 장 → 창 한 장 → 「악몽으로」 고르고 성문 앞 한 장 → 「해방」으로 되돌리고 성문 앞 한 장 + 시기 로그.
    /// 창은 [F] 대신 리플렉션으로 연다(키 입력을 흉내 낼 수 없다). 결과: Temp/nightmare_toggle/
    /// </summary>
    [MenuItem(Root + "18 악몽 갈림길 실측 (Play)")]
    public static async void CaptureNightmareToggle()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(BaseCampNightmareToggle);
        var toggle = Object.FindFirstObjectByType<BaseCampNightmareToggle>(FindObjectsInactive.Include);
        if (toggle == null) { Debug.LogWarning("[SanctumDebug] 악몽 갈림길이 없다 — 오버라이드 「Ended (Nightmare Off)」인지 확인"); return; }
        var readyField = type.GetField("_ready", Inst);
        for (int i = 0; i < 80 && !(bool)readyField.GetValue(toggle); i++)
        {
            await System.Threading.Tasks.Task.Delay(250);
            if (!Application.isPlaying || toggle == null) return;
        }
        Debug.Log($"[SanctumDebug] 악몽 갈림길 — 드러남 {readyField.GetValue(toggle)} · 위치 {toggle.transform.position} · 시기 {StoryProgress.Era}");

        System.IO.Directory.CreateDirectory("Temp/nightmare_toggle");
        var player = BaseCampBootstrapper.Instance != null ? BaseCampBootstrapper.Instance.Player : Object.FindFirstObjectByType<PlayerController>();
        if (player == null) return;
        var open = type.GetMethod("OpenAsync", Inst);
        var picked = type.GetField("_picked", Inst);

        for (int round = 0; round < 2; round++)
        {
            if (toggle == null || player == null) { Debug.LogWarning("[SanctumDebug] 갈림길·플레이어가 사라졌다(씬이 바뀜?) — 중단"); return; }
            int pick = round == 0 ? 1 : 0;   // 1 = 악몽으로 · 0 = 해방된 세계로
            Place(player, toggle.transform.position + toggle.transform.forward * 3.4f);   // 받침 밑단 밖(안에 서면 가림 페이더가 받침을 투명하게 만든다)
            await System.Threading.Tasks.Task.Delay(1600);
            if (!Application.isPlaying || toggle == null) return;
            if (round == 0)
            {
                ScreenCapture.CaptureScreenshot("Temp/nightmare_toggle/1_station.png");
                await System.Threading.Tasks.Task.Delay(300);
                // 멀리서(광장 쪽 9 m) — 처음 드러날 때 무엇인지 읽히는가 · 다른 이름표와 겹치지 않는가
                Place(player, toggle.transform.position + toggle.transform.forward * 9f);
                await System.Threading.Tasks.Task.Delay(1600);
                ScreenCapture.CaptureScreenshot("Temp/nightmare_toggle/1b_station_far.png");
                Place(player, toggle.transform.position + toggle.transform.forward * 3.4f);
                await System.Threading.Tasks.Task.Delay(1400);
            }
            await System.Threading.Tasks.Task.Delay(300);

            open.Invoke(toggle, new object[] { toggle.GetCancellationTokenOnDestroy() });
            await System.Threading.Tasks.Task.Delay(900);
            ScreenCapture.CaptureScreenshot($"Temp/nightmare_toggle/{(round == 0 ? "2_choice" : "4_choice_again")}.png");
            await System.Threading.Tasks.Task.Delay(300);
            picked.SetValue(toggle, pick);
            await System.Threading.Tasks.Task.Delay(2600);   // 창 닫힘 + 게이트 물듦(1.2초)
            if (!Application.isPlaying) return;

            TeleportLocal(TourPoints[3].local);   // 성문 앞
            await System.Threading.Tasks.Task.Delay(1800);
            ScreenCapture.CaptureScreenshot($"Temp/nightmare_toggle/{(round == 0 ? "3_gate_nightmare" : "5_gate_liberated")}.png");
            Debug.Log($"[SanctumDebug] 악몽 갈림길 — 「{(pick == 1 ? "악몽으로" : "해방")}」 고른 뒤 시기 {StoryProgress.Era} · 악몽 모드 {StoryProgress.IsNightmareMode}");
            await System.Threading.Tasks.Task.Delay(400);
            if (round == 0)
            {
                // 하강 층계참 — 「심연의 문」 이름판 아래 「악몽」 · 포탈 결계 띠(포탈 발판까지 가면 심연으로 내려가 버린다)
                var landing = GameObject.Find(InstanceName)?.transform.Find("Descent/DownLanding");
                if (landing != null) Place(player, landing.position);
                await System.Threading.Tasks.Task.Delay(1800);
                ScreenCapture.CaptureScreenshot("Temp/nightmare_toggle/3b_portal_nightmare.png");
                await System.Threading.Tasks.Task.Delay(400);
            }
        }
        Debug.Log("[SanctumDebug] 악몽 갈림길 실측 끝 → Temp/nightmare_toggle/");
    }

    /// <summary>
    /// 서비스 방 NPC(상점 · 재련소 · 정제소) 재질 확인(10-01 핑크 감시 「재질 없음」 — 오탐인지) — 플레이어 앞 4 m에 나란히 세워 게임 화면으로 한 장,
    /// 렌더러마다 빈 재질 칸 · 셰이더를 로그로. 결과: Temp/npc_material/npcs.png · 끝나면 지운다.
    /// </summary>
    [MenuItem(Root + "19 서비스 NPC 재질 확인 (Play)")]
    public static async void CheckServiceNpcMaterials()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var player = BaseCampBootstrapper.Instance != null ? BaseCampBootstrapper.Instance.Player : Object.FindFirstObjectByType<PlayerController>();
        if (player == null) return;
        string[] paths =
        {
            "Assets/RelicFairy/Systems/Stage/Shop/Prefabs/ShopNpc.prefab",
            "Assets/RelicFairy/Systems/Stage/Crucible/Prefabs/CrucibleNpc.prefab",
            "Assets/RelicFairy/Systems/Stage/Refinery/Prefabs/RefineryNpc.prefab",
        };
        var cam = Camera.main;
        Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : player.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var spawned = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < paths.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (prefab == null) { Debug.LogWarning("[SanctumDebug] NPC 프리팹 없음: " + paths[i]); continue; }
            Vector3 at = player.transform.position + fwd * 4f + right * ((i - 1) * 1.6f);
            var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(-fwd, Vector3.up));
            spawned.Add(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < mats.Length; k++)
                    sb.Append(k > 0 ? ", " : "").Append(mats[k] == null ? "NULL" : mats[k].name + "(" + (mats[k].shader != null ? mats[k].shader.name : "셰이더 없음") + ")");
                int sub = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.subMeshCount
                        : r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : -1;
                Debug.Log($"[SanctumDebug] NPC 재질 — {prefab.name}/{r.name} · 켜짐 {r.enabled && r.gameObject.activeInHierarchy} · 서브메시 {sub} · [{sb}]");
            }
        }
        await System.Threading.Tasks.Task.Delay(1500);
        if (!Application.isPlaying) return;
        System.IO.Directory.CreateDirectory("Temp/npc_material");
        ScreenCapture.CaptureScreenshot("Temp/npc_material/npcs.png");
        await System.Threading.Tasks.Task.Delay(800);
        foreach (var go in spawned) if (go != null) Object.Destroy(go);
        Debug.Log("[SanctumDebug] 서비스 NPC 재질 확인 끝 → Temp/npc_material/npcs.png");
    }

    // 갈림길 자리 후보 — 성문 앞 마커(GateFront) 로컬 값(씬 nightmareStationLocal과 같은 단위 · 마커 배율 1.25가 곱해진다)
    private static readonly (string name, Vector3 local)[] CrossroadCandidates =
    {
        ("A_now",        new Vector3(-4.8f, 0f, -1.0f)),   // 지금 — 기사상 +17 바로 뒤
        ("B_outerL",     new Vector3(-7.6f, 0f, -1.3f)),   // 왼쪽 기사상 둘(+17 · +36) 사이 너머
        ("C_innerL",     new Vector3(-2.8f, 0f, -0.9f)),   // 아치 쪽으로 — 기사상 +17 안쪽
        ("D_outerR",     new Vector3( 7.6f, 0f, -1.3f)),   // B의 오른쪽 거울
        ("E_passageL",   new Vector3(-3.4f, 0f,  2.4f)),   // 아치 안 통로 왼쪽
    };

    /// <summary>
    /// 악몽 갈림길 자리 후보 실측(10-01 f5 검토 — 「유물 성소」 도착 배너와 같이 뜸 · 기사상에 가림) — 오버라이드 「Ended (Nightmare Off)」로 플레이할 것.
    /// 후보마다 갈림길을 옮겨 위에서 본 바닥(바닥 +3.2 m 위를 잘라 지붕 · 석상 상반신 제외) + 귀환 자리 · 중간 · 3.4 m 앞 게임 화면.
    /// 로그: 박힘(받침 자리와 겹친 콜라이더) · 성문 축에서 거리 · 받침/3.4 m 앞이 든 구역(그 구역 도착 배너가 뜬다) · 이름표가 이 갈림길인가.
    /// 끝나면 원래 자리로 되돌린다. 결과: Temp/crossroad_cand/
    /// </summary>
    [MenuItem(Root + "20 갈림길 자리 후보 (Play)")]
    public static async void CaptureCrossroadCandidates()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var toggle = Object.FindFirstObjectByType<BaseCampNightmareToggle>(FindObjectsInactive.Include);
        var inst = GameObject.Find(InstanceName);
        var gate = inst != null ? inst.transform.Find("Markers/GateFront") : null;
        var spawn = inst != null ? inst.transform.Find("Markers/ReturnSpawn") : null;
        var player = BaseCampBootstrapper.Instance != null ? BaseCampBootstrapper.Instance.Player : Object.FindFirstObjectByType<PlayerController>();
        if (toggle == null || gate == null || spawn == null || player == null)
        {
            Debug.LogWarning($"[SanctumDebug] 갈림길 후보 — 준비 안 됨(갈림길 {toggle != null} · 성문 마커 {gate != null} · 귀환 마커 {spawn != null} · 플레이어 {player != null})");
            return;
        }
        for (int i = 0; i < 80 && !toggle.gameObject.activeInHierarchy; i++)
        {
            await System.Threading.Tasks.Task.Delay(250);
            if (!Application.isPlaying || toggle == null) return;
        }
        System.IO.Directory.CreateDirectory("Temp/crossroad_cand");
        var root = toggle.transform;
        Vector3 home = root.position;
        var own = root.GetComponentsInChildren<Collider>(true);
        var zones = Object.FindObjectsByType<ZoneSign>(FindObjectsSortMode.None);
        int groundMask = LayerMask.GetMask("Ground");

        foreach (var (name, local) in CrossroadCandidates)
        {
            if (!Application.isPlaying || toggle == null || player == null) return;
            Vector3 pos = gate.TransformPoint(local);
            bool grounded = Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out var gh, 10f, groundMask, QueryTriggerInteraction.Ignore);
            if (grounded) pos.y = gh.point.y;
            root.position = pos;

            // 박힘 — 받침 자리(반지름 0.7 m · 바닥 위 0.05~2.5 m)와 겹친 콜라이더(갈림길 자신 · 트리거 제외).
            // 캡슐 아래 구가 바닥에 닿으면 바닥(PlazaDisc)이 늘 잡혀 바닥 위에서 시작한다
            var sb = new System.Text.StringBuilder();
            foreach (var h in Physics.OverlapCapsule(pos + Vector3.up * 0.75f, pos + Vector3.up * 1.8f, 0.7f, ~0, QueryTriggerInteraction.Ignore))
                if (System.Array.IndexOf(own, h) < 0) sb.Append(sb.Length > 0 ? ", " : "").Append(h.name);

            Vector3 toSpawn = Vector3.ProjectOnPlane(spawn.position - pos, Vector3.up).normalized;
            Vector3 near = pos + toSpawn * 3.4f;
            float axis = Mathf.Abs(Vector3.Dot(Vector3.ProjectOnPlane(pos - gate.position, Vector3.up), gate.right));
            Debug.Log($"[SanctumDebug] 갈림길 후보 {name} — 로컬 {local} · 월드 {pos} · 바닥 {grounded} · 성문 축에서 {axis:0.0} m"
                      + $" · 박힘 [{sb}] · 받침이 든 구역 [{ZonesAt(zones, pos)}] · 3.4 m 앞이 든 구역 [{ZonesAt(zones, near)}]");

            RenderCrossroadTop($"Temp/crossroad_cand/{name}_0top.png", gate, pos.y);

            Place(player, spawn.position);
            await System.Threading.Tasks.Task.Delay(1800);
            if (!Application.isPlaying || toggle == null) return;
            ScreenCapture.CaptureScreenshot($"Temp/crossroad_cand/{name}_1spawn.png");
            await System.Threading.Tasks.Task.Delay(300);

            Place(player, Vector3.Lerp(spawn.position, pos, 0.6f));
            await System.Threading.Tasks.Task.Delay(1600);
            if (!Application.isPlaying || toggle == null) return;
            ScreenCapture.CaptureScreenshot($"Temp/crossroad_cand/{name}_2mid.png");
            await System.Threading.Tasks.Task.Delay(300);

            Place(player, near);
            await System.Threading.Tasks.Task.Delay(1600);
            if (!Application.isPlaying || toggle == null) return;
            ScreenCapture.CaptureScreenshot($"Temp/crossroad_cand/{name}_3near.png");
            Debug.Log($"[SanctumDebug] 갈림길 후보 {name} — 3.4 m 앞 이름표가 갈림길인가 {BaseCampLabelRule.IsShown(root)}");
            await System.Threading.Tasks.Task.Delay(300);
        }
        if (toggle != null) toggle.transform.position = home;
        if (player != null) Place(player, spawn.position);
        Debug.Log("[SanctumDebug] 갈림길 자리 후보 실측 끝 → Temp/crossroad_cand/");
    }

    private static string ZonesAt(ZoneSign[] zones, Vector3 p)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var z in zones)
        {
            if (z == null || !z.TryGetComponent<SphereCollider>(out var s)) continue;
            Vector3 sc = z.transform.lossyScale;
            float r = s.radius * Mathf.Max(sc.x, Mathf.Max(sc.y, sc.z));
            if (Vector3.Distance(z.transform.TransformPoint(s.center), p) <= r) sb.Append(sb.Length > 0 ? ", " : "").Append(z.name);
        }
        return sb.ToString();
    }

    /// <summary>성문 앞을 위에서 — 화면 위 = 성문 쪽(게임 화면과 같은 왼쪽/오른쪽), 바닥 +3.2 m 위는 잘라 지붕 · 석상 상반신을 뺀다. 가로 32 m.</summary>
    private static void RenderCrossroadTop(string path, Transform gate, float floorY)
    {
        const int W = 1280, H = 960;
        const float Lift = 40f;
        var go = new GameObject("~CrossroadTopCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(W, H, 24);
        bool fog = RenderSettings.fog;
        try
        {
            Vector3 fwd = Vector3.ProjectOnPlane(gate.forward, Vector3.up).normalized;
            Vector3 c = gate.position - fwd * 4f;
            go.transform.SetPositionAndRotation(new Vector3(c.x, floorY + Lift, c.z), Quaternion.LookRotation(Vector3.down, fwd));
            cam.orthographic = true;
            cam.orthographicSize = 12f;
            cam.aspect = (float)W / H;
            cam.nearClipPlane = Lift - 3.2f;
            cam.farClipPlane = Lift + 8f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.targetTexture = rt;
            RenderSettings.fog = false;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    private static void Place(PlayerController player, Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + 0.1f;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static void PreviewSeal(int state) => Shrine()?.DebugPreview(-1, state);

    private static BaseCampSealShrine Shrine()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return null; }
        var s = Object.FindFirstObjectByType<BaseCampSealShrine>();
        if (s == null) Debug.LogWarning("[SanctumDebug] 봉인 섬(BaseCampSealShrine)이 없다.");
        return s;
    }

    private static MemoryAltarCrystal Crystal()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return null; }
        var c = Object.FindFirstObjectByType<MemoryAltarCrystal>();
        if (c == null) Debug.LogWarning("[SanctumDebug] 기억 수정(MemoryAltarCrystal)이 없다.");
        return c;
    }

    private static void PlaySummon(BaseCampFxDirector.SummonKind kind)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var fx = BaseCampFxDirector.Instance;
        var player = BaseCampBootstrapper.Instance != null ? BaseCampBootstrapper.Instance.Player : null;
        if (fx == null || player == null) { Debug.LogWarning("[SanctumDebug] 연출 담당 또는 플레이어가 없다."); return; }
        fx.PlaySummonArrivalAsync(player, kind, fx.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>열린 대사창을 끝까지 넘긴다 — 대사창이 떠 있으면 시간이 멈춰 트리거·연출·입자가 안 돈다(실측 전용).</summary>
    [MenuItem(Root + "8 대사 넘기기 (Play)")]
    public static async void SkipDialogue()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return; }
        var advance = typeof(UI_DialoguePopup).GetMethod("OnAdvanceClicked",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        for (int i = 0; i < 40; i++)
        {
            var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
            if (popup == null) break;
            advance?.Invoke(popup, null);
            await System.Threading.Tasks.Task.Delay(350);
            if (!Application.isPlaying) return;
        }
        Debug.Log("[SanctumDebug] 대사 넘기기 끝");
    }

    private static PlayerController TeleportLocal(Vector3 local)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SanctumDebug] 플레이 모드에서만 동작한다."); return null; }
        var root = GameObject.Find(InstanceName);
        if (root == null) { Debug.LogWarning($"[SanctumDebug] 씬에 {InstanceName} 인스턴스가 없다."); return null; }

        var player = BaseCampBootstrapper.Instance != null ? BaseCampBootstrapper.Instance.Player : null;
        if (player == null) player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
        if (player == null) { Debug.LogWarning("[SanctumDebug] 플레이어를 찾지 못했다."); return null; }

        Vector3 pos = root.transform.TransformPoint(local);
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = pos;
            rb.linearVelocity = Vector3.zero;
        }
        Debug.Log($"[SanctumDebug] 플레이어 이동 → {pos} (로컬 {local})");
        return player;
    }
}
#endif
