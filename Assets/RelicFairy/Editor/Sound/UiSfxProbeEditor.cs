#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [플레이 중 · 테스트 허브] 10-12 UI 소리 확인 — 화면 7개를 차례로 열고 닫으며 「어느 이벤트 키가 울렸는지」를 남긴다.
/// 귀 대신 <b>이벤트 · 표가 고른 주소 · 클립 이름 · 재생 여부</b>(소리 풀에서 그 클립이 실제로 돌았는가)로 판정한다.
///
/// <para>화면: ① 상점 ② 무기대 ③ 룬판 ④ 설정(토글 · 슬라이더 미리듣기) ⑤ ESC ⑥ 런 결과 ⑦ 퀘스트 알림.
/// 이어서 화면 밖 「무음 · 결함 칸」(방 클리어 · 놀이 시작/실패 · 석문 · 재화 · 월드 상호작용 · 룬 집기/놓기 …)을 표에서 직접 한 번씩 울리고,
/// 코드가 부르는 이벤트가 모두 표에 있는지 센다. 상태는 바꾸지 않는다(상점 리롤은 값을 높여 거절 길만 탄다).</para>
///
/// <para>반입 도구(FantasyUiSfxImporter)를 편집 모드에서 돌린 뒤에 쓴다. 결과 Temp/ui_sfx_probe.txt · 로그 「[UiSfxProbe] 끝」.</para>
/// </summary>
public static class UiSfxProbeEditor
{
    // ── Constants ────────────────────────────────────────────────
    private const string Tag       = "[UiSfxProbe]";
    private const string OutTxt    = "Temp/ui_sfx_probe.txt";
    private const string TablePath = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const int    SettleMs  = 450;   // 요청 → 클립 로드 → 풀 재생까지 기다리는 몫
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>화면 밖에서 나는 무음 · 결함 칸(순위 1) — 표에서 직접 울려 본다. ui = UI 채널(PlayUiEvent) · 아니면 효과음(PlayEvent).</summary>
    private static readonly (string evt, bool ui)[] SilentCells =
    {
        (SoundEvent.RoomClear, false), (SoundEvent.MgStart, true), (SoundEvent.MgFail, true), (SoundEvent.RoomClear, true),
        (SoundEvent.GateSeal, false), (SoundEvent.FuelMaterial, false), (SoundEvent.FuelOre, false), (SoundEvent.EssencePickup, false),
        (SoundEvent.GambleOpen, false), (SoundEvent.VaultOpen, false), (SoundEvent.BuffboxOpen, false), (SoundEvent.SacrificeOffer, false),
        (SoundEvent.OracleChoose, false), (SoundEvent.SpringDrink, false), (SoundEvent.SwordPickup, false),
        (SoundEvent.RestForge, false), (SoundEvent.RestHeal, false),
        (SoundEvent.RuneLift, true), (SoundEvent.RunePlace, true), (SoundEvent.RuneReturn, true), (SoundEvent.MemoryAbsorb, true),
    };

    /// <summary>ShopUIStyle.PlaySfx에 문자열로 넘기는 뜻 키(상점 · 재련소 · 정제소 · 제단) — 표에 있어야 한다.</summary>
    private static readonly string[] StyleKeys =
    {
        "shop_select", "shop_buy", "shop_deal", "shop_reroll",
        "crucible_hit", "crucible_success", "crucible_jackpot", "crucible_fail", "enhance_success", "crucible_evolve", "crucible_promote",
        "refine_spin", "refine_reveal", "refine_lit", "refine_legend",
        "altar_buy", "altar_engrave", "altar_grow", "altar_keystone", "altar_era",
    };

    /// <summary>SoundEvent에 있지만 부르는 곳이 없는 옛 상수(§2-2) · 플레이어 담당 몫 — 표 검사에서 뺀다.</summary>
    private static readonly HashSet<string> NotCalled = new()
    {
        "boss_appear", "player_die", "player_hit", "monster_die", "monster_hit", "ui_hover",
    };

    // ── Static ───────────────────────────────────────────────────
    private static readonly List<(string screen, string evt, string key)> s_hits = new();
    private static readonly HashSet<string> s_heard = new();             // 소리 풀에서 실제로 돈 클립 이름
    private static readonly Dictionary<string, string> s_clipName = new(); // 주소 → 클립 이름(없으면 null)
    private static string s_screen = "";

    // ── Public Methods ───────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/10-12 UI 소리 확인 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다."); return; }
        RunAsync().Forget();
    }

    // ── Private Methods ──────────────────────────────────────────
    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-12 UI 소리 확인 — 이벤트 · 표 주소 · 클립 · 재생 여부\n");
        var sound = Managers.Sound;
        var table = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (sound == null || table == null) { Debug.LogWarning($"{Tag} SoundManager 또는 표가 없다 — 중단"); return; }

        s_hits.Clear(); s_heard.Clear(); s_clipName.Clear();
        sb.AppendLine($"믹서: {(sound.HasMixer ? "연결됨(Resources/GameAudioMixer)" : "없음 — 폴백 볼륨")}");
        using var sampler = new CancellationTokenSource();
        SoundManager.EditorEventRequested += OnEventRequested;
        SampleAsync(sampler.Token).Forget();
        int fails = 0;
        try
        {
            fails += await ScreenAsync(sb, "① 상점", new[] { SoundEvent.UiOpen, "shop_select", SoundEvent.UiReject, SoundEvent.UiClose }, ShopAsync);
            fails += await ScreenAsync(sb, "② 무기대", new[] { SoundEvent.UiOpen, SoundEvent.UiSelect, SoundEvent.UiClose }, ForgeAsync);
            fails += await ScreenAsync(sb, "③ 룬판", new[] { SoundEvent.UiOpen, SoundEvent.UiClose }, GridAsync);
            fails += await ScreenAsync(sb, "④ 설정", new[] { SoundEvent.UiOpen, SoundEvent.UiClick, SoundEvent.UiClose }, SettingsAsync);
            fails += await ScreenAsync(sb, "⑤ ESC", new[] { SoundEvent.UiOpen, SoundEvent.UiClick, SoundEvent.UiClose }, EscAsync);
            fails += await ScreenAsync(sb, "⑥ 런 결과", new[] { SoundEvent.ResultReveal, SoundEvent.ResultCount, SoundEvent.UiConfirm }, ResultAsync);
            fails += await ScreenAsync(sb, "⑦ 퀘스트 알림", new[] { SoundEvent.QuestDone }, QuestAsync);
            fails += await SilentCellsAsync(sb, sound);
            fails += await DirectKeyAsync(sb, SoundKey.Sfx.PlayerHitMedium, "인트로 모르드레드 피격 · 탄막 경고(직접 키)");
            fails += TableCoverage(sb, table);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); fails++; }
        finally
        {
            SoundManager.EditorEventRequested -= OnEventRequested;
            sampler.Cancel();
            sb.AppendLine($"판정: 어긋남 {fails}건 (0이면 무음 · 결함 칸이 모두 채워졌다 — 소리의 결은 귀 확인 몫)");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>한 화면 — 행동을 돌리고, 그 사이 울린 이벤트를 적고, 기대한 이벤트가 표 · 클립 · 재생까지 이어졌는지 센다. 어긋난 수를 돌려준다.</summary>
    private static async UniTask<int> ScreenAsync(StringBuilder sb, string name, string[] expect, System.Func<UniTask> body)
    {
        s_screen = name;
        int from = s_hits.Count;
        sb.AppendLine($"[{name}]");
        try { await body(); }
        catch (System.Exception e) { sb.AppendLine($"  예외: {e.GetType().Name} {e.Message}"); }
        await Wait(SettleMs);

        for (int i = from; i < s_hits.Count; i++)
            sb.AppendLine("  " + await DescribeAsync(s_hits[i].evt, s_hits[i].key));

        int bad = 0;
        foreach (var evt in expect)
        {
            bool ok = false;
            for (int i = from; i < s_hits.Count && !ok; i++)
                if (s_hits[i].evt == evt && await PlayedAsync(evt, s_hits[i].key)) ok = true;
            if (!ok) bad++;
            sb.AppendLine($"  CHECK {evt}: {ok}");
        }
        return bad;
    }

    private static async UniTask ShopAsync()
    {
        GameObject ctrlGo = null;
        try
        {
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) throw new System.Exception("상점 팝업 로드 실패");
            ctrlGo = new GameObject("~UiSfxProbeShop");
            var sc = ctrlGo.AddComponent<ShopRoomController>();
            SetField(sc, "_run", GameRunBootstrapper.Instance?.Run);
            SetField(sc, "_peddler", AbyssPeddlerCatalog.Build(new System.Random(7)));
            SetField(sc, "_rerollEnabled", true);
            SetField(sc, "_rerollCost", 999999);   // 리롤은 거절 길만 — 골드를 쓰지 않는다
            shop.Bind(sc);
            await Wait(900);
            Call(shop, "Select", sc.Products.Count > 1 ? 1 : 0);
            await Wait(300);
            Call(shop, "OnRerollClicked");
            await Wait(300);
            shop.ClosePopupUI();
        }
        finally
        {
            await Wait(300);
            Managers.UI.CloseAllPopupUI();
            if (ctrlGo != null) Object.Destroy(ctrlGo);
        }
    }

    private static async UniTask ForgeAsync()
    {
        var weapons = RangedWeapons();
        if (weapons.Count == 0) throw new System.Exception("원거리 무기 에셋을 못 찾음");
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>();
        if (popup == null) throw new System.Exception("무기대 팝업 로드 실패");
        var list = new List<UI_RangedForgePopup.Entry>();
        foreach (var w in weapons) list.Add(new UI_RangedForgePopup.Entry { Weapon = w });
        popup.Setup(list);
        await Wait(500);
        Call(popup, "Step", 1);
        await Wait(300);
        Call(popup, "Cancel");
        await Wait(300);
        Managers.UI.CloseAllPopupUI();
    }

    private static async UniTask GridAsync()
    {
        if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        var grid = UI_GridPanel.Instance;
        if (grid == null) throw new System.Exception("룬판을 못 띄움");
        bool wasOpen = grid.IsOpen;
        if (wasOpen) Call(grid, "ClosePanel");   // 열려 있었으면 닫고 다시 연다 — 열기 소리를 듣기 위해
        await Wait(300);
        grid.ShowWithNewItem(null);
        await Wait(600);
        Call(grid, "OnBackClicked");
        await Wait(300);
        if (grid.IsOpen) Call(grid, "ClosePanel");   // 보관함이 넘쳐 정리 창이 떴으면 닫는다
    }

    private static async UniTask SettingsAsync()
    {
        var sound = Managers.Sound;
        try
        {
            UI_Settings.Open();
            await Wait(500);
            var st = Object.FindFirstObjectByType<UI_Settings>(FindObjectsInactive.Include);
            if (st == null) throw new System.Exception("설정 화면을 못 찾음");
            Call(st, "OnToggleWindowMode");                 // 창 모드 글자만 바뀐다 — 「완료」 전엔 적용되지 않는다
            await Wait(300);
            Call(st, "OnSfxChanged", sound.EffectVolume);   // 같은 값 — 손을 뗀 다음 프레임에 효과음 채널 미리듣기
            await Wait(400);
            Call(st, "OnUiChanged", sound.UiVolume);        // UI 채널 미리듣기
            await Wait(400);
        }
        finally { UI_Settings.CloseIfOpen(); }   // 취소 길 — 열 때 값으로 되돌린다
    }

    private static async UniTask EscAsync()
    {
        var esc = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        if (esc == null) esc = new GameObject("~UiSfxProbeEsc").AddComponent<UI_EscMenu>();
        try
        {
            esc.Open();
            await Wait(400);
            Call(esc, "OnMemory");   // 「되찾은 기억」 쪽 — 일반 버튼 클릭
            await Wait(400);
        }
        finally { esc.Close(); }
    }

    private static async UniTask ResultAsync()
    {
        var snap = typeof(RunResultProbeEditor).GetMethod("Full", Any)?.Invoke(null, null) as RunResultSnapshot;
        if (snap == null) throw new System.Exception("가짜 결과 사본을 못 만듦(RunResultProbeEditor.Full)");
        var task = RunResultView.ShowAsync(snap, default);
        await Wait(1300);                    // 등장 · 카운트업(0.5초) 뒤 · [계속]은 1초 뒤부터 받는다
        RunResultView.Active?.Continue();
        await task;
    }

    private static async UniTask QuestAsync()
    {
        var view = Object.FindFirstObjectByType<QuestNotifierView>(FindObjectsInactive.Include);
        if (view == null) throw new System.Exception("퀘스트 알림 View 없음(@UIRoot HUD)");
        using var cts = new CancellationTokenSource();
        var task = view.PlayAsync(null, "UI 소리 확인", "퀘스트 완료 알림", cts.Token);
        await Wait(700);
        cts.Cancel();
        try { await task; }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>화면 밖 무음 · 결함 칸 — 표에서 직접 한 번씩 울린다(0.3초 간격).</summary>
    private static async UniTask<int> SilentCellsAsync(StringBuilder sb, SoundManager sound)
    {
        s_screen = "무음 · 결함 칸";
        sb.AppendLine("[무음 · 결함 칸 — 표에서 직접]");
        int bad = 0;
        foreach (var (evt, ui) in SilentCells)
        {
            int from = s_hits.Count;
            if (ui) sound.PlayUiEvent(evt);
            else    sound.PlayEvent(evt);
            await Wait(300);
            string key = s_hits.Count > from ? s_hits[from].key : null;
            bool ok = await PlayedAsync(evt, key);
            if (!ok) bad++;
            sb.AppendLine($"  {(ok ? "OK " : "!! ")}{(ui ? "UI " : "SFX")} {await DescribeAsync(evt, key)}");
        }
        return bad;
    }

    /// <summary>표를 거치지 않는 직접 키 — 주소 · 재생만 본다.</summary>
    private static async UniTask<int> DirectKeyAsync(StringBuilder sb, string key, string where)
    {
        sb.AppendLine($"[직접 키 — {where}]");
        await Managers.Sound.PlayUiAsync(key, 0.5f, 1f);
        await Wait(300);
        string clip = await ClipNameAsync(key);
        bool ok = clip != null && s_heard.Contains(clip);
        sb.AppendLine($"  {(ok ? "OK " : "!! ")}{key} · 클립 {clip ?? "없음"} · 재생 {(clip != null && s_heard.Contains(clip))}");
        return ok ? 0 : 1;
    }

    /// <summary>코드가 부르는 이벤트(SoundEvent 상수 · ShopUIStyle 뜻 키)가 모두 표에 있는가.</summary>
    private static int TableCoverage(StringBuilder sb, SoundEventTableSO table)
    {
        var missing = new List<string>();
        int total = 0;
        foreach (var f in typeof(SoundEvent).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!f.IsLiteral || !(f.GetRawConstantValue() is string evt) || NotCalled.Contains(evt)) continue;
            total++;
            if (!table.TryGet(evt, out _, out _, out _)) missing.Add(evt);
        }
        foreach (var evt in StyleKeys)
        {
            total++;
            if (!table.TryGet(evt, out _, out _, out _)) missing.Add(evt);
        }
        sb.AppendLine($"[표 덮음] 코드가 부르는 이벤트 {total}개 중 표에 없음 {missing.Count}: {string.Join(", ", missing)}");
        return missing.Count;
    }

    private static async UniTask<string> DescribeAsync(string evt, string key)
    {
        string clip = key != null ? await ClipNameAsync(key) : null;
        string want = ExpectedClip(evt);
        string match = want == null ? "" : clip == want ? " (목록과 같음)" : $" (목록 {want})";
        return $"{s_screen} · {evt} → {key ?? "표 없음"} · 클립 {clip ?? "없음"}{match} · 재생 {(clip != null && s_heard.Contains(clip))}";
    }

    /// <summary>표 → 주소 → 클립이 이어졌고, 클립이 반입 목록과 같고, 소리 풀에서 실제로 돌았는가.</summary>
    private static async UniTask<bool> PlayedAsync(string evt, string key)
    {
        if (key == null) return false;
        string clip = await ClipNameAsync(key);
        if (clip == null || !s_heard.Contains(clip)) return false;
        string want = ExpectedClip(evt);
        return want == null || want == clip;
    }

    private static string ExpectedClip(string evt)
    {
        foreach (var r in FantasyUiSfxImporter.Rows)
            if (r.evt == evt) return r.file ?? r.address;   // 기존 소리 줄(mg_fail)은 주소 = 파일 이름
        return null;
    }

    private static async UniTask<string> ClipNameAsync(string key)
    {
        if (s_clipName.TryGetValue(key, out var cached)) return cached;
        var clip = await Managers.AddressableManager.TryLoadAssetAsync<AudioClip>(key);
        string name = clip != null ? clip.name : null;
        s_clipName[key] = name;
        return name;
    }

    /// <summary>매 프레임 소리 풀(@Sound/EffectPool)에서 돌고 있는 클립 이름을 모은다 — 「재생 여부」의 근거.</summary>
    private static async UniTaskVoid SampleAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var pool = GameObject.Find("@Sound")?.transform.Find("EffectPool");
                if (pool != null)
                    foreach (var src in pool.GetComponentsInChildren<AudioSource>(false))
                        if (src.isPlaying && src.clip != null) s_heard.Add(src.clip.name);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private static List<WeaponSO> RangedWeapons()
    {
        var res = new List<WeaponSO>();
        var altar = Object.FindFirstObjectByType<WeaponForgeAltar>(FindObjectsInactive.Include);
        if (altar != null)
            foreach (var f in new[] { "bowOption", "crossbowOption" })
                if (typeof(WeaponForgeAltar).GetField(f, Any)?.GetValue(altar) is WeaponSO w && w != null) res.Add(w);
        if (res.Count > 0) return res;
        foreach (var guid in AssetDatabase.FindAssets("t:MainWeaponSO"))
        {
            var w = AssetDatabase.LoadAssetAtPath<MainWeaponSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (w != null && w.name.ToLowerInvariant().Contains("bow")) res.Add(w);
            if (res.Count >= 2) break;
        }
        return res;
    }

    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Any)?.Invoke(target, args);

    private static void SetField(object target, string field, object value)
        => target.GetType().GetField(field, Any)?.SetValue(target, value);

    private static UniTask Wait(int ms) => UniTask.Delay(ms, ignoreTimeScale: true);

    // ── Event Handlers ───────────────────────────────────────────
    private static void OnEventRequested(string evt, string key) => s_hits.Add((s_screen, evt, key));
}
#endif
