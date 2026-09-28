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
