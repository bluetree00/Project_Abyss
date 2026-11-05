#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 11-05 #20 룬 성소 · #14 문 뒤 스폰 실측(테스트 허브 · 런 구조 자동 진행).
///   · #20: 다음 문을 ch{N}_event_runeshrine_01로 고정(로컬 방 풀)해 실제 방 흐름으로 들어간다 — 제단 셋(속성 · 자리 · 길) · 고르기 → 받은 룬(영웅 · 한 칸 · 효과 2 · 효과 생성) ·
///     판에 놓으면 효과가 켜지나 · 상점 진열 · 드랍 풀에 안 섞이나 · 문 배지. 차트(CDN)에 성소 룬이 아직 없으면 폴백 JSON 행을 차트에 넣고 잰다(「업로드 전」이라 적는다).
///   · #14: 해방기 Ch3를 피해 모사로 진행(몬스터가 살아 움직인다) — 일반 · 정예방마다 몬스터가 문 칸 너머(복도 쪽)에 선 적이 있나 · 처음 선 자리.
///   · #20 겹침(사용자 11-05 「중복 작동 · 같은 룬 여러 개」): 성소 방에서 슬라임에 — 성소 룬 + 같은 속성 4칸 영웅 룬 · 성소 룬 여섯 · 멀린 보관(<see cref="OverlapAsync"/>).
/// 결과 Temp/req1105_level/shrine_ch{n}.txt · doors_ch3.txt · overlap_ch1.txt · 사진 Temp/ui_shots/R1105S_* · 로그 「[Req1105S] 끝 …」.
/// </summary>
public static class Req1105ShrineProbeEditor
{
    private const string Tag = "[Req1105S]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "req1105_level");

    private static int s_ch;
    private static bool s_doors, s_observing;
    private static string s_key;
    private static double s_deadline;
    private static int s_launchTries;
    private static int s_injected;
    private static bool s_acted;
    private static bool s_overlap;
    private static bool s_timer;
    private static bool s_cdn;
    private static StringBuilder s_sb;
    private static readonly HashSet<string> s_seenRooms = new();
    private static int s_prevStory = -99;
    private static string s_prevSimDps;

    // ── #20 ─────────────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/Ch1 (테스트 허브, 플레이 중)")] private static void S1() => BeginShrine(1);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/Ch2 (테스트 허브, 플레이 중)")] private static void S2() => BeginShrine(2);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/Ch3 (테스트 허브, 플레이 중)")] private static void S3() => BeginShrine(3);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/Ch4 (테스트 허브, 플레이 중)")] private static void S4() => BeginShrine(4);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/겹침 · 멀린 보관 — Ch1 (테스트 허브, 플레이 중)")] private static void SOverlap() => BeginShrine(1, overlap: true);
    // CDN 업로드 뒤 — 폴백을 넣지 않고 방 풀도 CDN에서 읽는다(다음 문 고정이 켜는 「로컬 방 풀 우선」을 끈다)
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/CDN 뒤 — Ch1 (테스트 허브, 플레이 중)")] private static void C1() => BeginShrine(1, cdn: true);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/CDN 뒤 — Ch2 (테스트 허브, 플레이 중)")] private static void C2() => BeginShrine(2, cdn: true);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/CDN 뒤 — Ch3 (테스트 허브, 플레이 중)")] private static void C3() => BeginShrine(3, cdn: true);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/CDN 뒤 — Ch4 (테스트 허브, 플레이 중)")] private static void C4() => BeginShrine(4, cdn: true);
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/남은 시간 글 화면 안 — Ch3 룬 성소 (테스트 허브, 플레이 중)")] private static void TShrine() => BeginTimer(3, "ch3_event_runeshrine_01");
    [MenuItem("RelicFairy/Debug/11-05 #20 룬 성소 실측/남은 시간 글 화면 안 — Ch3 제물 제단 (테스트 허브, 플레이 중)")] private static void TSacrifice() => BeginTimer(3, "ch3_event_sacrifice_01");

    // ── #14 ─────────────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/11-05 #14 문 뒤 스폰 실측 — 해방기 Ch3 피해 모사 (테스트 허브, 플레이 중)")]
    private static void Doors()
    {
        if (!Ready()) return;
        s_doors = true; s_ch = 3; s_key = null;
        s_sb = new StringBuilder("11-05 #14 문 뒤 스폰 — 해방기 Ch3 · 피해 모사(몬스터가 살아 움직인다)" + Environment.NewLine);
        s_prevStory = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, 1);   // 해방기
        StoryProgress.RefreshDebugOverride();
        s_prevSimDps = EditorPrefs.GetString("RunAuto.SimDpsByChapter", null);
        EditorPrefs.SetString("RunAuto.SimDpsByChapter", "120,250,600,900");
        Start(sim: true, seconds: 720);
    }

    private static bool Ready()
    {
        if (EditorApplication.isPlaying && UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>() != null) return true;
        Debug.LogWarning($"{Tag} 테스트 허브 플레이 중에");
        return false;
    }

    /// <summary>이벤트방 공용 「남은 시간」 글이 플레이어가 무대 어디에 서도 화면 안(위 재화 띠 · 아래 기술 칸 · 왼쪽 서약 판 밖)에 있나.</summary>
    private static void BeginTimer(int ch, string key)
    {
        if (!Ready()) return;
        s_doors = false; s_overlap = false; s_timer = true; s_ch = ch; s_key = key;
        s_sb = new StringBuilder($"11-05 이벤트방 남은 시간 글 — {key}" + Environment.NewLine);
        s_injected = InjectShrineRunesIfMissing();
        s_acted = false;
        EventMinigameDebugMenu.ForceDoor(key);
        Start(sim: false, seconds: 420);
    }

    private static void BeginShrine(int ch, bool overlap = false, bool cdn = false)
    {
        if (!Ready()) return;
        s_doors = false; s_timer = false; s_ch = ch; s_key = $"ch{ch}_event_runeshrine_01"; s_overlap = overlap; s_cdn = cdn;
        s_sb = new StringBuilder($"11-05 #20 룬 성소 — Ch{ch}{(overlap ? " · 겹침 · 멀린 보관" : "")}{(cdn ? " · CDN 업로드 뒤(폴백 없음 · 방 풀도 CDN)" : "")}" + Environment.NewLine);
        // 방은 첫 프레임에 제단을 세운다 — 차트에 넣는 일은 런을 걸기 전에(11-05 첫 실측: 들어선 뒤 넣어 빈 제단이 섰다)
        s_injected = cdn ? 0 : InjectShrineRunesIfMissing();
        s_acted = false;
        EventMinigameDebugMenu.ForceDoor(s_key);   // 로컬 방 풀 우선도 같이 켠다
        if (cdn) EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);   // CDN 뒤 — 방 풀은 서버 차트에서
        Start(sim: false, seconds: 420);
    }

    private static void Start(bool sim, double seconds)
    {
        s_observing = false;
        s_seenRooms.Clear();
        s_deadline = EditorApplication.timeSinceStartup + seconds;
        s_launchTries = 0;
        TryLaunchAuto(sim);
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
        Debug.Log($"{Tag} 시작 ch{s_ch} {(s_doors ? "#14" : "#20 " + s_key)}");
    }

    private static void TryLaunchAuto(bool sim)
    {
        var auto = typeof(RunStructureAutoPlayEditor).GetMethod("BeginWith", Any);
        auto.Invoke(null, new object[] { false, s_ch, false, false, sim });
        bool running = typeof(RunStructureAutoPlayEditor).GetField("s_running", Any)?.GetValue(null) is bool r && r;
        if (running || ++s_launchTries > 20) { if (!running) Debug.LogWarning($"{Tag} 자동 진행을 못 걸었다"); return; }
        double at = EditorApplication.timeSinceStartup + 2.0;
        void Later() { if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= Later; if (EditorApplication.isPlaying) TryLaunchAuto(sim); }
        EditorApplication.update += Later;
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_deadline)
        {
            Finish(EditorApplication.isPlaying ? "시간 끝" : "플레이 끝");
            return;
        }
        if (!s_doors && !s_acted) HoldAuto();   // 고르기 전까지 자동 진행이 이벤트를 대신 끝내지 않게(2초 규칙) — 매 프레임
        if (s_observing) return;
        var run = GameRunBootstrapper.Instance?.Run;
        var rfc = RunFlowController.Active;
        if (run == null || rfc == null) return;
        if (!(bool)(typeof(RunFlowController).GetField("_hasLastPlan", Any)?.GetValue(rfc) ?? false)) return;
        var plan = (DoorPlan)typeof(RunFlowController).GetField("_lastPlan", Any).GetValue(rfc);
        string pool = plan.entry?.pool_key ?? "";
        var cur = typeof(RunFlowController).GetField("_current", Any)?.GetValue(rfc) as ProcRoomResult;
        if (cur?.roomGO == null || string.IsNullOrEmpty(pool) || s_seenRooms.Contains(pool + cur.roomGO.GetInstanceID())) return;

        if (!s_doors && plan.kind == RoomPlanKind.Event && pool == s_key)
        {
            s_seenRooms.Add(pool + cur.roomGO.GetInstanceID());
            s_observing = true;
            EventMinigameDebugMenu.ForceDoor("");
            if (s_timer) TimerAsync(run, cur).Forget();
            else if (s_overlap) OverlapAsync(run, cur).Forget();
            else ShrineAsync(run, cur).Forget();
        }
        else if (s_doors && (plan.kind == RoomPlanKind.Normal || plan.kind == RoomPlanKind.Elite) && (int)run.CurrentChapter == 3)
        {
            s_seenRooms.Add(pool + cur.roomGO.GetInstanceID());
            s_observing = true;
            DoorRoomAsync(run, cur, pool, plan.kind).Forget();
        }
        else if (s_doors && plan.kind == RoomPlanKind.Boss)
            Finish("보스방 도착");
    }

    // ── #20 룬 성소 ───────────────────────────────────────────────
    private static async UniTaskVoid ShrineAsync(GameRunSession run, ProcRoomResult room)
    {
        var sb = s_sb;
        int pass = 0, fail = 0;
        void Check(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {what}"); }
        try
        {
            sb.AppendLine($"  방 {room.roomGO.name} · 문 배지 「{EventRewards.Title(s_key)}」 · {StripTags(EventRewards.Badge(s_key))}");
            Check(EventRewards.Title(s_key) == "룬 성소", "문 배지 제목 = 룬 성소");

            int injected = s_injected;
            sb.AppendLine(injected > 0
                ? $"  ※ 업로드 전 — 차트(CDN)에 성소 룬이 없어 폴백 JSON {injected}행을 차트에 넣고 잰다(런 시작 전)"
                : "  차트(CDN)에 성소 룬 있음 — 넣지 않고 잰다");
            if (s_cdn)
            {
                int shrineRows = Managers.ItemData.GetAllItems().Where(kv => RuneShrineChallenge.IsShrineRune(kv.Key)).Sum(kv => kv.Value?.Count ?? 0);
                Check(shrineRows == 12, $"차트(CDN ITEM_DATA) 성소 룬 {shrineRows}행(= 6종 × 2) — 폴백을 넣지 않았다");
                int poolRows = 0;
                int total = ChartLoader.Load($"CHAPTER_{s_ch}_ROOM_POOL", row => { if (row != null && row.ToJson().Contains(s_key)) poolRows++; });
                Check(poolRows == 1, $"CDN 방 풀 CHAPTER_{s_ch}_ROOM_POOL({total}행)에 {s_key} {poolRows}행");
            }

            RuneShrineChallenge sh = null;
            for (float t = 0f; t < 8f && sh == null; t += 0.2f)
            {
                sh = room.roomGO.GetComponentInChildren<RuneShrineChallenge>(true);
                if (sh == null) await Wait(0.2f);
            }
            Check(sh != null, $"룬 성소 붙음({(sh != null ? sh.GetType().Name : "없음")})");
            if (sh == null) return;

            for (float t = 0f; t < 20f && typeof(WorldInteractionChallenge).GetField("OnResolved", Any)?.GetValue(sh) == null; t += 0.25f) { HoldAuto(); await Wait(0.25f); }
            await Wait(0.6f); HoldAuto();

            var offered = (List<string>)typeof(RuneShrineChallenge).GetField("_offered", Any).GetValue(sh);
            var markers = ((IList)typeof(WorldInteractionChallenge).GetField("_markers", Any).GetValue(sh)).Cast<object>().ToList();
            var chart = Managers.ItemData;
            var elems = offered.Select(id => chart.GetItemMeta(id)?.element).ToList();
            sb.AppendLine($"  제단 룬: {string.Join(" / ", offered.Select(id => $"{chart.GetItemMeta(id)?.item_name}({chart.GetItemMeta(id)?.element})"))}");
            Check(offered.Count == 3, $"제단 3개(지금 {offered.Count})");
            Check(elems.Distinct().Count() == offered.Count, "제단 속성이 모두 다르다");
            Check(offered.All(RuneShrineChallenge.IsShrineRune), "모두 성소 룬(item_t3_shrine_*)");

            Vector3 pp = run.Player.transform.position;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                Vector3 pos = (Vector3)m.GetType().GetField("Pos").GetValue(m);
                string label = (m.GetType().GetField("Label").GetValue(m) as TMPro.TMP_Text)?.text;
                string prompt = (m.GetType().GetField("Prompt").GetValue(m) as TMPro.TMP_Text)?.text;
                // 게임 배치 · 10-30 전수 실측과 같은 판(사람 크기 agent 0) — 아무 판에나 붙이면 큰 몬스터 판에 붙어 길이 끊겨 보였다(11-05 Ch2)
                bool onNav = NavMesh.SamplePosition(pos, out var hit, 0.8f, MinigameArena.Filter);
                bool reach = onNav && MinigameArena.IsLinked(pp, hit.position);
                float nearest = markers.Where((o, j) => j != i).Select(o => Flat((Vector3)o.GetType().GetField("Pos").GetValue(o), pos)).DefaultIfEmpty(99f).Min();
                sb.AppendLine($"  제단 {i + 1} 「{label}」 {Fmt(pos)} · 걸을 면 {onNav} · 걸어서 닿음 {reach} · 이웃 제단 {nearest:0.0} m");
                sb.AppendLine($"     [F] 글 「{StripTags(prompt).Replace("\n", " / ")}」");
                Check(onNav && reach, $"제단 {i + 1} 걸어서 닿는다");
                Check(nearest > 2.8f, $"제단 {i + 1} 이웃과 누르는 거리(2.8 m) 밖");
            }

            if (offered.Count == 0) { sb.AppendLine("  제단 룬 없음 — 빈 제단만 섰다(고르기 단계 건너뜀)"); return; }

            // 사람처럼 — 첫 제단 앞에 서서(글이 보이게) 사진 · 고르기
            Vector3 p0 = (Vector3)markers[0].GetType().GetField("Pos").GetValue(markers[0]);
            Vector3 stage = (Vector3)typeof(WorldInteractionChallenge).GetProperty("Stage", Any).GetValue(sh);
            Vector3 stand = p0 + (stage - p0).normalized * 1.6f; stand.y = p0.y;
            TeleportPlayer(run, stand);
            for (int i = 0; i < 6; i++) { HoldAuto(); await Wait(0.25f); }
            await ShotAsync($"R1105S_ch{s_ch}_1_altar");

            var inv = run.ItemInventory;
            int staged0 = inv.StagingItems.Count;
            string pick = offered[0];
            bool resolvedBefore = (bool)typeof(WorldInteractionChallenge).GetProperty("IsResolved", Any).GetValue(sh);
            Check(!resolvedBefore, "고르기 전엔 아직 안 끝났다(자동 진행이 대신 끝내지 않음)");
            typeof(RuneShrineChallenge).GetMethod("OnActivate", Any).Invoke(sh, new object[] { 0 });
            s_acted = true;
            RuntimeItemData got = null;
            for (float t = 0f; t < 4f && got == null; t += 0.2f)
            {
                got = inv.StagingItems.FirstOrDefault(x => x.itemId == pick);
                if (got == null) await Wait(0.2f);
            }
            Check(got != null, $"고른 룬이 보관함에 들어왔다({pick} · 보관함 {staged0} → {inv.StagingItems.Count})");
            var labels = markers.Select(m => (m.GetType().GetField("Label").GetValue(m) as TMPro.TMP_Text)?.text).ToList();
            Check(labels.Skip(1).All(l => l == Loc.T("닫힘")), $"나머지 제단 닫힘({string.Join(" / ", labels)})");
            if (got != null)
            {
                var shape = Managers.RuneData?.GetShape(got.shapeId);
                int cells = shape == null ? -1 : new[] { shape.r1, shape.r2, shape.r3, shape.r4 }.Where(r => !string.IsNullOrEmpty(r)).Sum(r => r.Count(c => c == '1'));
                sb.AppendLine($"  받은 룬 {got.displayName} · {got.rarity} · 속성 {got.element} · 모양 {got.shapeId}({cells}칸) · 효과 {string.Join(" + ", got.effects.Select(e => $"{e.effectType}/{e.trigger} {e.value}"))}");
                Check(got.rarity == ItemRarity.Epic, "영웅 등급");
                Check(cells == 1, "한 칸");
                Check(got.element == chart.GetItemMeta(pick)?.element, "속성 = 차트");
                Check(got.effects.Count == 2, "효과 2줄");
                Check(got.effects.All(e => ItemEffectRegistry.Create(e) != null), "효과 2줄 모두 효과 객체로 만들어진다(미등록 0)");
            }

            await Wait(0.8f);
            var grid = UI_GridPanel.Instance;
            Check(grid != null && grid.IsOpen, "룬판이 열렸다(새 룬 강조)");
            await ShotAsync($"R1105S_ch{s_ch}_2_grid");
            if (grid != null && grid.IsOpen) typeof(UI_GridPanel).GetMethod("ClosePanel", Any)?.Invoke(grid, null);
            await Wait(0.5f);

            // 판에 놓으면(배치 판정은 건너뛰고 놓인 목록에 넣는다) 효과가 켜지나
            if (got != null)
            {
                var em = run.EffectManager;
                int before = em.ActiveEffects.Count;
                inv.PlaceItem(got);
                await UniTask.DelayFrame(2);
                var types = got.effects.Select(e => e.effectType).ToList();
                int after = em.ActiveEffects.Count;
                sb.AppendLine($"  놓기 전 효과 {before} → 뒤 {after}");
                Check(after - before >= 2, "놓으면 효과 2개가 켜진다");
                inv.UnplaceItem(got);
                await UniTask.DelayFrame(2);
                Check(em.ActiveEffects.Count == before, $"빼면 원래대로({em.ActiveEffects.Count})");
            }

            // 상점 · 드랍에 안 섞이나
            var build = typeof(AbyssPeddlerCatalog).GetMethod("BuildRune", Any);
            var rng = new System.Random(1105);
            int shopShrine = 0, shopN = 0, shopEpic = 0;
            for (int i = 0; i < 400; i++)
            {
                var p = build.Invoke(null, new object[] { rng, null }) as ShopProduct;
                if (p == null) continue;
                shopN++;
                if (p.Rune != null && RuneShrineChallenge.IsShrineRune(p.Rune.itemId)) shopShrine++;
            }
            for (int i = 0; i < 200; i++)
            {
                var p = build.Invoke(null, new object[] { rng, (ItemRarity?)ItemRarity.Epic }) as ShopProduct;
                if (p?.Rune != null && RuneShrineChallenge.IsShrineRune(p.Rune.itemId)) shopEpic++;
            }
            Check(shopN > 0 && shopShrine == 0, $"상점 룬 칸 400번 — 성소 룬 {shopShrine}번(진열 {shopN})");
            Check(shopEpic == 0, $"상점 귀한 물건(영웅) 200번 — 성소 룬 {shopEpic}번");
            int drop = ItemSORegistry.GetByRarity(ItemRarity.Epic).Count(so => RuneShrineChallenge.IsShrineRune(so.itemId));
            Check(drop == 0, $"방 보상 · 시작 선물 · 정제소 영웅 풀(ItemSO) — 성소 룬 {drop}종");

            // 다시 들어와도(같은 방 시드) 이미 가진 룬은 안 올린다
            var pickMethod = typeof(RuneShrineChallenge).GetMethod("PickOffers", Any);
            pickMethod.Invoke(sh, null);
            Check(!offered.Contains(pick), $"가진 룬은 제단에서 빠진다({string.Join(", ", offered)})");
        }
        catch (Exception e) { fail++; sb.AppendLine($"  ‼ 예외 {e}"); }
        finally
        {
            sb.AppendLine($"  합계 PASS {pass} · FAIL {fail}");
            Write(s_cdn ? $"shrine_cdn_ch{s_ch}.txt" : $"shrine_ch{s_ch}.txt", sb);
            Finish($"#20 PASS {pass} FAIL {fail}");
        }
    }

    // ── #20 겹침 · 멀린 보관 ───────────────────────────────────────
    private const string Grass4 = "item_t3_grass_venom", Ice4 = "item_t3_ice_frost", Dark4 = "item_t3_dark_erode",
                         Light4 = "item_t3_light_radiance", Elec4 = "item_t3_elec_static", Fire4 = "item_t3_fire_spark";
    private static readonly string[] ShrineIds =
        { "item_t3_shrine_fire", "item_t3_shrine_ice", "item_t3_shrine_elec", "item_t3_shrine_grass", "item_t3_shrine_light", "item_t3_shrine_dark" };

    /// <summary>
    /// 사용자 11-05 「중복 작동 · 같은 룬을 여러 개 쓸 때 문제」 — 성소 방(몬스터 없음)에 슬라임을 불러 잰다. 판 배치 판정 · 도감 · 소리는 건너뛰고 놓인 목록에 직접 넣는다.
    ///   · 성소 룬 + 같은 속성 4칸 영웅 룬(같은 효과 종류): 둘 다 따로 걸리나 · 서로 덮지 않나(독 중첩당) · 같은 적 간격(서리)은 하나로 도나 · 한 번 쓰는 것(구르기 첫 타)은 한 번씩만.
    ///   · 성소 룬 여섯: 상시 효과가 한 번씩만 더해지나 · 효과를 다시 만들어도 그대로 · 최대 체력을 넣다 빼도 체력이 차지 않나.
    ///   · 멀린 보관: 맡긴 성소 룬이 다음 런 시작에 돌아오나 · 돌아온 룬은 성소 제단에서 빠지나(같은 룬 둘 금지).
    /// </summary>
    private static async UniTaskVoid OverlapAsync(GameRunSession run, ProcRoomResult room)
    {
        var sb = s_sb;
        int pass = 0, fail = 0;
        void Check(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {what}"); }
        var p = run.Player; var inv = run.ItemInventory; var em = run.EffectManager; var stats = p.RuntimeStats;
        var ctx = (ItemEffectContext)typeof(ItemEffectManager).GetField("_ctx", Any).GetValue(em);
        var placed  = (List<RuntimeItemData>)typeof(RunItemInventory).GetField("_placedItems", Any).GetValue(inv);
        var staging = (List<RuntimeItemData>)typeof(RunItemInventory).GetField("_stagingItems", Any).GetValue(inv);
        var mine = new List<RuntimeItemData>();
        var invField = typeof(PlayerController).GetField("debugInvincible", Any);
        bool invWas = invField?.GetValue(p) is bool iw && iw;
        var meleeF = typeof(PlayerRuntimeStats).GetField("_baseMelee", Any);
        var rangedF = typeof(PlayerRuntimeStats).GetField("_baseRanged", Any);
        int baseMelee = (int)meleeF.GetValue(stats), baseRanged = (int)rangedF.GetValue(stats);
        MonsterBase slime = null;

        void Rebuild() { em.RefreshContext(p, run); em.Rebuild(); typeof(GameRunSession).GetMethod("RefreshPlayerItemStats", Any)?.Invoke(run, null); }
        List<RuntimeItemData> Put(params string[] ids)
        {
            var list = new List<RuntimeItemData>();
            foreach (var id in ids)
            {
                var d = RuntimeItemData.FromServer(Managers.ItemData.GetItem(id));
                if (d == null) { sb.AppendLine($"  ‼ 차트에 없음 {id}"); continue; }
                placed.Add(d); mine.Add(d); list.Add(d);
            }
            Rebuild();
            return list;
        }
        void Take(List<RuntimeItemData> list) { foreach (var d in list) { placed.Remove(d); mine.Remove(d); } Rebuild(); }
        DamageReport Rep(float dmg, float coef = 1f, bool heavy = false) => new DamageReport
        {
            DamageDealt = dmg, Attacker = p.gameObject, Target = slime.gameObject, HitPosition = slime.transform.position,
            ActionType = WeaponActionType.GroundLight, ProcCoef = coef, IsHeavy = heavy,
        };
        DamagePacket Pkt() => new DamagePacket(100f, p.gameObject, slime.gameObject);
        List<T> Of<T>(string trigger = null) where T : ItemEffectBase => em.ActiveEffects.OfType<T>().Where(e => trigger == null || e.Trigger == trigger).ToList();

        try
        {
            // 자동 진행은 매 프레임 방 안 일반 몬스터를 처치한다(소환한 슬라임이 1.2초 안에 체력 0) — 여기서 멈추고 잰다(이벤트는 60초 시간 끝까지 그대로)
            s_acted = true;
            if (typeof(RunStructureAutoPlayEditor).GetField("s_running", Any)?.GetValue(null) is bool ar && ar)
                typeof(RunStructureAutoPlayEditor).GetMethod("Finish", Any, null, new[] { typeof(string) }, null)?.Invoke(null, new object[] { "req1105-overlap" });
            invField?.SetValue(p, true);
            sb.AppendLine(s_injected > 0 ? $"  ※ 업로드 전 — 폴백 JSON {s_injected}행을 차트에 넣고 잰다" : "  차트(CDN)에 성소 룬 있음 — 넣지 않고 잰다");
            sb.AppendLine($"  시작 판 룬 {inv.PlacedCount}개 · 보관함 {inv.StagingCount}개 · 근접 공격력 {stats.MeleeAttack} · 시간 배율 {Time.timeScale}");
            Check(inv.PlacedCount == 0, "시작 판이 비었다(다른 룬이 재는 값을 흐리지 않는다)");

            // ① 독 칸 단독 — 같은 독에 약한 중첩당 값을 덧걸 때
            {
                var st = new MonsterStatusReceiver();
                st.ApplyStackingDot("item_poison", 40f, 1f, 4, 8, null);
                st.ApplyStackingDot("item_poison", 20f, 1f, 4, 8, null);
                int n = st.GetDotStacks("item_poison");
                float per = st.GetDotDamagePerTick("item_poison") / Mathf.Max(1, n);
                sb.AppendLine($"  [독 칸] 중첩당 40을 건 뒤 20을 덧걸면 — 중첩 {n} · 중첩당 {per:0.#}");
                Check(n == 2 && Mathf.Abs(per - 40f) < 0.01f, "약한 독을 덧걸어도 중첩당 피해는 센 쪽(40)");
            }

            Vector3 stage = (Vector3)typeof(WorldInteractionChallenge).GetProperty("Stage", Any).GetValue(room.roomGO.GetComponentInChildren<RuneShrineChallenge>(true));
            slime = await SpawnSlimeAsync(p, sb, stage);
            Check(slime != null, "슬라임 소환");
            if (slime == null) return;

            // ② 숲 — 스며드는 숲(중첩당 4%) + 응축된 숲(2%) · 공격력을 잠시 크게(중첩당 값이 1로 깎여 같아 보이지 않게)
            meleeF.SetValue(stats, baseMelee + 1000); rangedF.SetValue(stats, baseRanged + 1000);
            Rebuild();
            int atk = stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind());
            (int n, float per) Poison(params string[] ids)
            {
                var l = Put(ids);
                ClearStatus(slime);
                for (int i = 0; i < 3; i++) em.OnPostDealDamage(Rep(1f, coef: 2f));   // 확률 50% × 계수 2 = 늘 걸린다
                int n = slime.Status.GetDotStacks("item_poison");
                float per = n > 0 ? slime.Status.GetDotDamagePerTick("item_poison") / n : 0f;
                ClearStatus(slime);
                Take(l);
                return (n, per);
            }
            var g4 = Poison(Grass4); var gs = Poison(ShrineIds[3]); var gb = Poison(Grass4, ShrineIds[3]); var gr = Poison(ShrineIds[3], Grass4);
            sb.AppendLine($"  [숲] 공격력 {atk} · 3타 — 스며드는 숲만 {g4.n}중첩 × {g4.per:0.#} · 응축된 숲만 {gs.n} × {gs.per:0.#} · 둘(4칸 먼저) {gb.n} × {gb.per:0.#} · 둘(성소 먼저) {gr.n} × {gr.per:0.#}");
            Check(g4.per > gs.per * 1.5f, "스며드는 숲 중첩당(4%) > 응축된 숲(2%) — 재는 값이 구분된다");
            Check(gb.n == Mathf.Min(8, g4.n + gs.n), $"둘이면 중첩이 둘 다 쌓인다({gb.n} = {g4.n} + {gs.n})");
            Check(gb.per >= g4.per - 0.01f && gr.per >= g4.per - 0.01f,
                  $"둘이어도 중첩당은 센 쪽 — 4칸 먼저 {gb.per:0.#} · 성소 먼저 {gr.per:0.#} ≥ 스며드는 숲만 {g4.per:0.#}(약한 룬을 더 껴서 약해지지 않는다)");
            meleeF.SetValue(stats, baseMelee); rangedF.SetValue(stats, baseRanged);
            Rebuild();

            // ③ 얼음 — 굳히는 얼음(+15%) + 응축된 얼음(+8%)
            {
                var l = Put(Ice4, ShrineIds[1]);
                var fr = Of<FrostOnHitEffect>();
                ClearStatus(slime);
                em.OnPostDealDamage(Rep(1f));
                int s1 = slime.Status.GetSlowStacks("frost");
                await Wait(0.6f);
                em.OnPostDealDamage(Rep(1f));
                int s2 = slime.Status.GetSlowStacks("frost");
                var pk = Pkt(); em.OnPreDealDamage(ref pk, false);
                float expect = fr.Aggregate(1f, (m, e) => m * (1f + V(e))) - 1f;
                sb.AppendLine($"  [얼음] 효과 {fr.Count}개({string.Join(" · ", fr.Select(e => $"{V(e):P0}"))}) · 첫 타 서리 {s1} · 0.6초 뒤 {s2} · 2중첩 적 보너스 {pk.BonusRune:P1}");
                Check(fr.Count == 2, "서리 효과 2개가 따로 있다");
                Check(s1 == 1 && s2 == 2, "룬이 둘이어도 한 타에 서리 1중첩(같은 적 0.5초 간격을 함께 쓴다) — 두 배로 쌓이지 않는다");
                Check(Mathf.Abs(pk.BonusRune - expect) < 0.002f, $"2중첩 적 피해 보너스 = 둘이 곱해진다 {pk.BonusRune:P1}(기대 {expect:P1})");
                ClearStatus(slime);
                Take(l);
            }

            // ④ 그림자 — 덮치는 그림자(+80%) + 응축된 그림자(+40%) · 구르기 직후 첫 공격(한 번 쓴다)
            {
                var l = Put(Dark4, ShrineIds[5]);
                var fa = Of<FirstAttackAfterRollEffect>();
                ProcCoefficient.ConsumeHeavyNext();
                em.OnRollEnd();
                var a = Pkt(); em.OnPreDealDamage(ref a, false); bool h1 = ProcCoefficient.ConsumeHeavyNext();
                var b = Pkt(); em.OnPreDealDamage(ref b, false); bool h2 = ProcCoefficient.ConsumeHeavyNext();
                float expect = fa.Aggregate(1f, (m, e) => m * (1f + V(e))) - 1f;
                sb.AppendLine($"  [그림자] 효과 {fa.Count}개({string.Join(" · ", fa.Select(e => $"+{V(e):P0}"))}) · 구르기 첫 타 보너스 {a.BonusRune:P0} 강타 {h1} · 둘째 타 {b.BonusRune:P0} 강타 {h2}");
                Check(fa.Count == 2, "구르기 첫 공격 효과 2개가 따로 있다");
                Check(Mathf.Abs(a.BonusRune - expect) < 0.002f && h1, $"첫 타에 둘 다 — 곱해서 {a.BonusRune:P0}(기대 {expect:P0}) · 그 타는 강타");
                Check(b.BonusRune < 0.001f && !h2, "둘째 타엔 남는 것 없음(둘 다 한 번씩만 쓴다 · 강타도 한 번)");
                Take(l);
            }

            // ⑤ 성광 — 결투하는 성광(+30%) + 응축된 성광(+15%) · 강타 뒤 3초 치명 피해
            {
                var l = Put(Light4, ShrineIds[4]);
                var cb = Of<ConditionalStatBuffEffect>("AfterHeavyHit");
                em.OnPostDealDamage(Rep(1f, heavy: true));
                var dyn = new ItemDynamicStats();
                foreach (var c in cb) c.ContributeDynamicStats(ctx, ref dyn);
                float expect = cb.Sum(c => V(c));
                sb.AppendLine($"  [성광] 효과 {cb.Count}개 · 강타 뒤 치명 피해 {dyn.critDamage:P0}(기대 {expect:P0})");
                Check(cb.Count == 2 && Mathf.Abs(dyn.critDamage - expect) < 0.002f, "강타 뒤 치명 피해 — 둘 다 더해진다");
                Take(l);
            }

            // ⑥ 화염 — 터지는 화염(12%) + 응축된 화염(6%) · 추가타는 룬마다 따로 굴린다(계수 100 = 늘)
            {
                int Drop(params string[] ids)
                {
                    var l = Put(ids);
                    int hp0 = slime.CurrentHp;
                    em.OnPostDealDamage(Rep(3f, coef: 100f));
                    int d = hp0 - slime.CurrentHp;
                    Take(l);
                    return d;
                }
                int one = Drop(Fire4), two = Drop(Fire4, ShrineIds[0]);
                sb.AppendLine($"  [화염] 한 타의 추가타 피해 — 터지는 화염만 {one} · 둘 {two}");
                Check(one > 0 && two == one * 2, "추가타가 룬마다 한 번씩(둘이면 두 번) — 서로 막거나 되풀이하지 않는다");
            }

            // ⑦ 번개 — 휘감는 번개 + 응축된 번개 · 같은 적 N번째 셈은 룬마다 따로
            if (slime == null || slime.IsDead) slime = await SpawnSlimeAsync(p, sb, stage);
            if (slime != null)
            {
                var l = Put(Elec4, ShrineIds[2]);
                var tb = Of<TriggerBurstEffect>("SameTarget");
                var req = tb.Select(e => (int)typeof(TriggerBurstEffect).GetMethod("Required", Any).Invoke(e, null)).ToList();
                var firstFire = tb.Select(_ => 0).ToList();
                for (int hit = 1; hit <= 10 && !slime.IsDead; hit++)
                {
                    em.OnPostDealDamage(Rep(1f));
                    for (int i = 0; i < tb.Count; i++)
                        if (firstFire[i] == 0 && StreakCount(tb[i]) == 0) firstFire[i] = hit;
                }
                sb.AppendLine($"  [번개] 효과 {tb.Count}개 · 필요 타수 {string.Join("/", req)} · 처음 터진 타 {string.Join("/", firstFire)}");
                Check(tb.Count == 2 && firstFire.SequenceEqual(req), "같은 적 N번째에 룬마다 따로 터진다(서로 셈을 지우지 않는다)");
                Take(l);
            }

            // ⑧ 성소 룬 여섯 — 상시 효과가 한 번씩만 · 다시 만들어도 그대로 · 최대 체력 넣다 빼기
            {
                var acc0 = em.GetAccumulatedStats();
                int hp0 = stats.Hp, max0 = stats.MaxHp;
                var l = Put(ShrineIds);
                var descs = new HashSet<string>(l.SelectMany(d => d.effects).Select(s => s.description));
                List<ItemEffectBase> MineFx() => em.ActiveEffects.OfType<ItemEffectBase>().Where(e => descs.Contains(SlotOf(e)?.description)).ToList();
                var m1 = MineFx();
                var acc1 = em.GetAccumulatedStats();
                float SumOf(string type) => m1.Where(e => e.EffectType == type).Sum(e => V(e));
                sb.AppendLine($"  [여섯] 효과 {m1.Count}개 · 치명 피해 +{acc1.CritDamagePercent - acc0.CritDamagePercent:P0} · 방어 +{acc1.Defense - acc0.Defense} · 공속 +{acc1.AttackSpeed - acc0.AttackSpeed:P0} · " +
                              $"최대 체력 +{acc1.MaxHP - acc0.MaxHP} · 치명 확률 +{acc1.CritChancePercent - acc0.CritChancePercent:P0} · 공격력 +{acc1.AllDamagePercent - acc0.AllDamagePercent:P0}");
                Check(m1.Count == 12, $"효과 12개(룬 6 × 2) — 슬롯마다 하나씩({m1.Count})");
                Check(Mathf.Abs(acc1.CritDamagePercent - acc0.CritDamagePercent - SumOf("CritDamage")) < 0.001f
                      && acc1.Defense - acc0.Defense == (int)SumOf("Defense")
                      && Mathf.Abs(acc1.AttackSpeed - acc0.AttackSpeed - SumOf("AttackSpeed")) < 0.001f
                      && acc1.MaxHP - acc0.MaxHP == (int)SumOf("MaxHP")
                      && Mathf.Abs(acc1.CritChancePercent - acc0.CritChancePercent - SumOf("CritChance")) < 0.001f
                      && Mathf.Abs(acc1.AllDamagePercent - acc0.AllDamagePercent - SumOf("AllDamage")) < 0.001f,
                      "상시 효과 여섯이 한 번씩만 더해진다(효과 값 합 = 늘어난 스탯)");
                Rebuild();
                var m2 = MineFx(); var acc2 = em.GetAccumulatedStats();
                Check(m2.Count == 12 && Mathf.Abs(acc2.CritDamagePercent - acc1.CritDamagePercent) < 0.0001f && acc2.MaxHP == acc1.MaxHP && acc2.Defense == acc1.Defense,
                      "효과를 다시 만들어도(Rebuild) 그대로 — 겹쳐 쌓이지 않는다");
                Check(stats.MaxHp == max0 + (acc1.MaxHP - acc0.MaxHP) && stats.Hp == hp0, $"최대 체력 {max0} → {stats.MaxHp} · 지금 체력 {hp0} → {stats.Hp}(놓는다고 차지 않는다)");
                int hpMax = stats.Hp;
                for (int k = 0; k < 3; k++) { Take(l); hpMax = Mathf.Max(hpMax, stats.Hp); l = Put(ShrineIds); hpMax = Mathf.Max(hpMax, stats.Hp); }
                Take(l);
                Check(hpMax <= hp0 && stats.MaxHp == max0, $"넣다 빼기 3번 — 체력이 한 번도 안 늘었다(가장 높을 때 {hpMax} ≤ {hp0}) · 빼면 최대 체력 {stats.MaxHp} = {max0}");
            }

            // ⑨ 멀린 보관 — 맡긴 성소 룬이 다음 런 시작에 돌아오나 · 돌아온 룬은 성소 제단에 다시 안 오른다
            {
                string id = ShrineIds[0];
                bool oldNull = RuntimeItemData.FromSO(ItemSORegistry.Find(id)) == null;
                sb.AppendLine($"  [멀린 보관] 예전 길(ItemSO로만 찾기) = {(oldNull ? "없음 → 「데이터에서 사라진 룬」으로 맡긴 칸을 비웠다" : "있음")}");
                var gd = BackendGameData.Instance;
                if (gd?.Data == null) sb.AppendLine("  세이브 데이터 없음 — 건너뜀");
                else
                {
                    string prev = gd.Data.keptRune; bool prevPending = run.KeptRunePending;
                    try
                    {
                        gd.Data.keptRune = id + "|FIRE"; run.KeptRunePending = false;
                        var got = MerlinRuneKeep.GrantPending(run);
                        Check(got != null && got.itemId == id && got.element == "FIRE" && got.fromMerlinKeep && got.effects.Count == 2 && staging.Contains(got),
                              $"맡긴 성소 룬이 보관함으로 돌아온다({got?.displayName} · {got?.element} · 효과 {got?.effects.Count} · 멀린 표시 {got?.fromMerlinKeep})");
                        Check(gd.Data.keptRune == id + "|FIRE" && run.KeptRunePending, "맡긴 칸은 첫 런 저장 때까지 남는다(바로 지우지 않는다)");
                        var sh = room.roomGO.GetComponentInChildren<RuneShrineChallenge>(true);
                        if (sh != null && got != null)
                        {
                            var offered = (List<string>)typeof(RuneShrineChallenge).GetField("_offered", Any).GetValue(sh);
                            var keep = new List<string>(offered);
                            typeof(RuneShrineChallenge).GetMethod("PickOffers", Any).Invoke(sh, null);
                            Check(!offered.Contains(id), $"돌아온 룬은 성소 제단에서 빠진다 — 같은 룬이 둘 안 된다({string.Join(", ", offered)})");
                            offered.Clear(); offered.AddRange(keep);
                        }
                        if (got != null) staging.Remove(got);
                    }
                    finally { gd.Data.keptRune = prev; run.KeptRunePending = prevPending; }
                }
            }
        }
        catch (Exception e) { fail++; sb.AppendLine($"  ‼ 예외 {e}"); }
        finally
        {
            foreach (var d in mine) placed.Remove(d);
            meleeF.SetValue(stats, baseMelee); rangedF.SetValue(stats, baseRanged);
            try { Rebuild(); } catch (Exception e) { sb.AppendLine($"  ‼ 되돌리기 예외 {e.Message}"); }
            invField?.SetValue(p, invWas);
            if (slime != null && !slime.IsDead) slime.TakeSynergyDamage(999999f, null);
            sb.AppendLine($"  합계 PASS {pass} · FAIL {fail}");
            Write($"overlap_ch{s_ch}.txt", sb);
            Finish($"#20 겹침 PASS {pass} FAIL {fail}");
        }
    }

    // ── 이벤트방 남은 시간 글(11-05 개선) ─────────────────────────
    // HUD 띠(1920×1080 실측): 위 재화 띠 아래끝 y 0.884 · 아래 기술 칸 위끝 y 0.13 · 왼쪽 서약 판 x < 0.16 · y > 0.72
    private const float HudTop = 0.885f, HudBottom = 0.12f, CovenantRight = 0.16f, CovenantBottom = 0.72f;

    private static async UniTaskVoid TimerAsync(GameRunSession run, ProcRoomResult room)
    {
        var sb = s_sb;
        int pass = 0, fail = 0;
        void Check(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {what}"); }
        try
        {
            WorldInteractionChallenge wc = null;
            for (float t = 0f; t < 8f && wc == null; t += 0.2f)
            {
                wc = room.roomGO.GetComponentInChildren<WorldInteractionChallenge>(true);
                if (wc == null) await Wait(0.2f);
            }
            Check(wc != null, $"이벤트 붙음({(wc != null ? wc.GetType().Name : "없음")})");
            if (wc == null) return;
            var fTimer = typeof(WorldInteractionChallenge).GetField("_autoTimer", Any);
            TMPro.TextMeshPro tm = null;
            for (float t = 0f; t < 20f && tm == null; t += 0.25f)
            {
                HoldAuto();
                tm = fTimer.GetValue(wc) as TMPro.TextMeshPro;
                if (tm == null) await Wait(0.25f);
            }
            Check(tm != null, "남은 시간 글이 섰다");
            if (tm == null) return;

            var cam = Camera.main;
            Vector3 stage = (Vector3)typeof(WorldInteractionChallenge).GetField("_stage", Any).GetValue(wc);
            float radius = (float)typeof(WorldInteractionChallenge).GetField("_stageRadius", Any).GetValue(wc);
            sb.AppendLine($"  {wc.GetType().Name} · 무대 {Fmt(stage)} 반경 {radius:0.0} m · 글꼴 {tm.font?.name} · 굵기 {tm.fontStyle} · 크기 {tm.fontSize}");
            Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            var spots = new (string name, Vector3 at, string shot)[]
            {
                ("가운데", stage, null), ("오른쪽 끝", stage + right * radius, "R"), ("왼쪽 끝", stage - right * radius, "L"),
                ("앞(화면 아래쪽)", stage - fwd * radius, null), ("뒤(화면 위쪽)", stage + fwd * radius, "B"),
            };
            foreach (var (name, at0, shot) in spots)
            {
                Vector3 at = at0;
                if (NavMesh.SamplePosition(at, out var hit, 2.5f, MinigameArena.Filter)) at = hit.position;
                TeleportPlayer(run, at);
                for (int i = 0; i < 8; i++) { HoldAuto(); await Wait(0.2f); }   // 카메라가 따라오게
                tm.ForceMeshUpdate();
                var (mn, mx) = ViewRect(cam, tm);
                bool inX = mn.x >= 0f && mx.x <= 1f;
                bool belowTop = mx.y <= HudTop, aboveBottom = mn.y >= HudBottom;
                bool clearCov = !(mn.x < CovenantRight && mx.y > CovenantBottom);
                bool front = cam.WorldToViewportPoint(tm.transform.position).z > 0.5f;   // 카메라 뒤면 안 보인다(화면 좌표는 뒤집혀 나온다)
                inX &= front;
                var wcCam = typeof(WorldInteractionChallenge).GetField("_cam", Any)?.GetValue(wc) as Camera;
                Vector3 anchor = stage + Vector3.up * 4.2f;
                Vector3 avp = cam.WorldToViewportPoint(anchor);
                sb.AppendLine($"  {name} {Fmt(at)} — 화면 x {mn.x:0.00}~{mx.x:0.00} · y {mn.y:0.00}~{mx.y:0.00} · 원래 자리 화면 ({avp.x:0.00}, {avp.y:0.00}, 깊이 {avp.z:0.0}) · 옮긴 거리 {Vector3.Distance(tm.transform.position, anchor):0.0} m · 글 깊이 {cam.WorldToViewportPoint(tm.transform.position).z:0.0} · 카메라 {cam.name}{(wcCam == cam ? "" : $" ≠ 이벤트 카메라 {(wcCam != null ? wcCam.name : "없음")}")} · 카메라 자리 {Fmt(cam.transform.position)}");
                Check(inX && belowTop && aboveBottom && clearCov,
                      $"{name}: 화면 안{(front ? "" : " · 카메라 뒤(안 보임)")}{(inX || !front ? "" : " · 좌우로 잘림")}{(belowTop ? "" : " · 위 재화 띠와 겹침")}{(aboveBottom ? "" : " · 아래 기술 칸과 겹침")}{(clearCov ? "" : " · 왼쪽 서약 판과 겹침")}");
                if (shot != null) await ShotAsync($"R1105T_{s_key}_{shot}");
            }
        }
        catch (Exception e) { fail++; sb.AppendLine($"  ‼ 예외 {e}"); }
        finally
        {
            s_acted = true;
            sb.AppendLine($"  합계 PASS {pass} · FAIL {fail}");
            Write($"timer_{s_key}.txt", sb);
            Finish($"남은 시간 글 PASS {pass} FAIL {fail}");
        }
    }

    /// <summary>글 사각형(textBounds 네 모서리)의 화면 좌표(0 ~ 1).</summary>
    private static (Vector2 min, Vector2 max) ViewRect(Camera cam, TMPro.TMP_Text t)
    {
        var b = t.textBounds;
        Vector2 mn = new(float.MaxValue, float.MaxValue), mx = new(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = b.center + new Vector3((i & 1) == 0 ? -b.extents.x : b.extents.x, (i & 2) == 0 ? -b.extents.y : b.extents.y, 0f);
            Vector2 v = cam.WorldToViewportPoint(t.transform.TransformPoint(local));
            mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v);
        }
        return (mn, mx);
    }

    private static async UniTask<MonsterBase> SpawnSlimeAsync(PlayerController p, StringBuilder sb, Vector3? near = null)
    {
        for (int tryN = 0; tryN < 3; tryN++)
        {
            Vector3 at = (near ?? p.transform.position + p.transform.forward * 3f) + new Vector3(tryN * 1.5f, 0f, 0f);
            bool onNav = NavMesh.SamplePosition(at, out var hit, 4f, MinigameArena.Filter);
            if (onNav) at = hit.position;
            GameObject go = null;
            try { go = await Managers.ObjectPooler.SpawnAsync("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity); }
            catch (Exception e) { sb.AppendLine($"  ‼ 슬라임 소환 예외 {e.Message}"); }
            var m = go != null ? go.GetComponentInChildren<MonsterBase>(true) : null;
            string now = go == null ? "오브젝트 없음" : m == null ? $"MonsterBase 없음({go.name})" : $"켜짐 {m.isActiveAndEnabled} · 쓰러짐 {m.IsDead} · 체력 {m.CurrentHp}";
            await Wait(1.2f);
            string later = m == null ? "-" : $"켜짐 {m.isActiveAndEnabled} · 쓰러짐 {m.IsDead} · 체력 {m.CurrentHp} · 자리 {Fmt(m.transform.position)}";
            sb.AppendLine($"  슬라임 소환 {tryN + 1} — {Fmt(at)} 길 위 {onNav} · 바로 {now} · 1.2초 뒤 {later}");
            if (m != null && m.isActiveAndEnabled && !m.IsDead) return m;
        }
        return null;
    }

    /// <summary>재는 사이 상태(독 · 둔화 · 적별 간격)를 비운다.</summary>
    private static void ClearStatus(MonsterBase m)
    {
        foreach (var f in new[] { "_dot", "_slow", "_cooldowns" })
            (typeof(MonsterStatusReceiver).GetField(f, Any)?.GetValue(m.Status) as IDictionary)?.Clear();
    }

    private static float V(object e, string f = "_value") => (float)typeof(ItemEffectBase).GetField(f, Any).GetValue(e);
    private static ItemEffectSlot SlotOf(object e) => typeof(ItemEffectBase).GetField("_slot", Any).GetValue(e) as ItemEffectSlot;
    private static int StreakCount(TriggerBurstEffect e)
    {
        var s = typeof(TriggerBurstEffect).GetField("_streak", Any).GetValue(e);
        return (int)s.GetType().GetProperty("Count").GetValue(s);
    }

    /// <summary>CDN에 성소 룬이 아직 없으면 폴백 JSON의 성소 행을 차트에 넣는다(업로드 전 실측 전용). 넣은 행 수.</summary>
    private static int InjectShrineRunesIfMissing()
    {
        var chart = Managers.ItemData;
        if (chart.GetAllItems().Keys.Any(RuneShrineChallenge.IsShrineRune)) return 0;
        var col = JsonUtility.FromJson<ItemEntryCollection>(File.ReadAllText("Assets/RelicFairy/Resources/ITEM_DATA.json"));
        var map = (Dictionary<string, List<ItemEntry>>)typeof(ItemDataManager).GetField("_itemById", Any).GetValue(chart);
        int n = 0;
        foreach (var e in col.items.Where(e => RuneShrineChallenge.IsShrineRune(e.item_id)))
        {
            if (!map.TryGetValue(e.item_id, out var list)) map[e.item_id] = list = new List<ItemEntry>();
            list.Add(e); n++;
        }
        return n;
    }

    // ── #14 문 뒤 ───────────────────────────────────────────────
    private static async UniTaskVoid DoorRoomAsync(GameRunSession run, ProcRoomResult room, string pool, RoomPlanKind kind)
    {
        var sb = s_sb;
        try
        {
            var slots = new List<ProcExitSlot>();
            if (room.hasEntrance) slots.Add(room.entrance);
            if (room.exits != null) slots.AddRange(room.exits);
            Vector3 center = RoomCenter(room.roomGO);
            var first = new Dictionary<int, (Vector3 pos, bool behind)>();
            var behindEver = new HashSet<int>();
            var behindAt = new List<string>();
            double t0 = EditorApplication.timeSinceStartup;
            bool sawMonster = false;
            while (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - t0 < 150)
            {
                var cur = typeof(RunFlowController).GetField("_current", Any)?.GetValue(RunFlowController.Active) as ProcRoomResult;
                if (cur != room) break;
                var mons = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).Where(m => m.isActiveAndEnabled && !m.IsDead).ToList();
                if (mons.Count > 0) sawMonster = true;
                foreach (var m in mons)
                {
                    int id = m.GetInstanceID();
                    Vector3 p = m.transform.position;
                    bool behind = BehindDoor(p, center, slots, out string which);
                    if (!first.ContainsKey(id)) first[id] = (p, behind);
                    if (behind && behindEver.Add(id)) behindAt.Add($"{m.name} {which} {Fmt(p)} (+{EditorApplication.timeSinceStartup - t0:0.0}초)");
                }
                if (sawMonster && mons.Count == 0 && EditorApplication.timeSinceStartup - t0 > 8) break;
                await Wait(0.2f);
            }
            int spawnBehind = first.Values.Count(v => v.behind);
            sb.AppendLine($"  {pool}({kind}) — 몬스터 {first.Count} · 처음 선 자리가 문 너머 {spawnBehind} · 한 번이라도 문 너머 {behindEver.Count} · 문 {slots.Count}");
            foreach (var s in behindAt.Take(6)) sb.AppendLine($"     ‼ {s}");
            s_doorRooms++; s_doorMons += first.Count; s_doorBehind += behindEver.Count; s_doorSpawnBehind += spawnBehind;
        }
        catch (Exception e) { sb.AppendLine($"  ‼ 예외 {e.Message}"); }
        finally { s_observing = false; }
    }

    private static int s_doorRooms, s_doorMons, s_doorBehind, s_doorSpawnBehind;

    /// <summary>문 칸 너머(복도 쪽 · 문 폭 + 1.5 m 안)에 섰나.</summary>
    private static bool BehindDoor(Vector3 p, Vector3 center, List<ProcExitSlot> slots, out string which)
    {
        which = null;
        foreach (var s in slots)
        {
            Vector3 o = s.worldPos - center; o.y = 0f;
            Vector3 axis = Mathf.Abs(o.x) > Mathf.Abs(o.z) ? new Vector3(Mathf.Sign(o.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(o.z));
            Vector3 d = p - s.worldPos; d.y = 0f;
            float along = Vector3.Dot(d, axis);
            float side = (d - axis * along).magnitude;
            if (along > 0.5f && side < s.openingWidth * 0.5f + 1.5f) { which = $"{s.edge} 문 너머 {along:0.0} m"; return true; }
        }
        return false;
    }

    private static Vector3 RoomCenter(GameObject roomGO)
    {
        var rs = roomGO.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return roomGO.transform.position;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }

    // ── 공용 ─────────────────────────────────────────────────────
    private static void Finish(string why)
    {
        EditorApplication.update -= Watch;
        s_acted = true;
        if (s_doors && s_sb != null)
        {
            s_sb.AppendLine($"  합계 — 방 {s_doorRooms} · 몬스터 {s_doorMons} · 처음부터 문 너머 {s_doorSpawnBehind} · 한 번이라도 문 너머 {s_doorBehind}");
            Write("doors_ch3.txt", s_sb);
            if (s_prevStory != -99) { EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, s_prevStory); StoryProgress.RefreshDebugOverride(); }
            if (s_prevSimDps == null) EditorPrefs.DeleteKey("RunAuto.SimDpsByChapter"); else EditorPrefs.SetString("RunAuto.SimDpsByChapter", s_prevSimDps);
            s_prevStory = -99;
            s_doorRooms = s_doorMons = s_doorBehind = s_doorSpawnBehind = 0;
        }
        EventMinigameDebugMenu.ForceDoor("");
        if (typeof(RunStructureAutoPlayEditor).GetField("s_running", Any)?.GetValue(null) is bool r && r)
            typeof(RunStructureAutoPlayEditor).GetMethod("Finish", Any, null, new[] { typeof(string) }, null)?.Invoke(null, new object[] { "req1105" });
        Debug.Log($"{Tag} 끝 {why}");
        s_doors = false;
        s_overlap = false;
        s_timer = false;
        s_cdn = false;
    }

    private static void HoldAuto()
    {
        var cur = typeof(RunStructureAutoPlayEditor).GetField("s_cur", Any)?.GetValue(null);
        cur?.GetType().GetField("enterT")?.SetValue(cur, EditorApplication.timeSinceStartup);
    }

    private static void TeleportPlayer(GameRunSession run, Vector3 at)
    {
        var p = run.Player;
        if (p == null) return;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
        p.transform.position = at;
    }

    private static void Write(string file, StringBuilder sb)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, file), sb.ToString(), new UTF8Encoding(true));
        Debug.Log($"{Tag} 기록 {file}");
    }

    private static async UniTask ShotAsync(string name)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        string path = $"Temp/ui_shots/{name}.png";
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 40 && !File.Exists(path); i++) await UniTask.Delay(50, ignoreTimeScale: true);
    }

    private static string StripTags(string s) => string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
    private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    private static UniTask Wait(float s) => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime);
}
#endif
