using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// [밸런스·UI 실측 · 플레이 중, 런 안] 룬(아이템) 전수 점검.
///
/// 1단계 — 120종을 하나씩 놓았다 빼며 <b>스탯이 실제로 얼마나 바뀌는지</b>와 <b>효과 인스턴스가 생기는지</b>를 잰다(전투 없음, 약 1분).
///  · 스탯 변화 있음        → 스탯형 룬(값으로 비교 가능)
///  · 스탯 변화 없음 + 효과 인스턴스 있음 → 기술형·조건부 룬 → 2단계(DPS 실측) 대상
///  · 스탯 변화 없음 + 효과가 GenericStatEffect 폴백뿐 → <b>효과 코드가 없는 룬</b>(미등록 effectType)
///  · 둘 다 없음            → <b>죽은 룬</b>
/// 끝에 속성 시너지 사슬(존 점유 → 단계 → 효과 인스턴스 → 해제)을 6속성 모두 한 번씩 확인한다.
/// 결과: Temp/rune_stat_pass.json
///
/// UI 흐름 — 상점에서 룬을 사면 룬판이 어디에 열리는지, 4지선다 카드, 새 전설 룬을 든 룬판을 찍는다.
/// 결과: Temp/ui_shots/audit_*.png + 콘솔 로그
/// </summary>
public static class RuneAuditEditor
{
    private const string Root     = "RelicFairy/Debug/룬 전수 점검/";
    private const string Database = "Assets/RelicFairy/Shared/Item/SOdata/ItemSODatabase.asset";
    private const double StepGap  = 0.35;   // 놓고 스탯·효과가 갱신될 틈
    private const string DeadLegendId = "item_t4_fire_aoe";   // 빈 판에서도 불 존에 안 들어가는 5×4 테두리(정적 점검)
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Row
    {
        public string id, name, rarity, element, effects;
        public int shapeId, effectCount, genericCount;
        public readonly Dictionary<string, double> diff = new();
    }

    private static readonly List<ItemSO> s_items = new();
    private static readonly List<Row> s_rows = new();
    private static Dictionary<string, double> s_base;
    private static RuntimeItemData s_current;
    private static int  s_index;
    private static bool s_placed, s_running;
    private static double s_next;

    [MenuItem(Root + "1단계 — 120종 스탯 변화·효과 등록 + 속성 시너지 사슬 (플레이 중, 약 1분)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneAudit] 플레이 모드에서만"); return; }
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player == null) { Debug.LogWarning("[RuneAudit] 런에 들어간 뒤 실행"); return; }
        var db = AssetDatabase.LoadAssetAtPath<ItemSODatabase>(Database);
        if (db == null) { Debug.LogError("[RuneAudit] 아이템 DB 없음: " + Database); return; }

        // 기존 배치를 비워 한 장씩 단독으로 잰다(보관함은 건드리지 않는다)
        foreach (var it in run.ItemInventory.PlacedItems.ToList()) run.ItemInventory.RemovePlaced(it);

        s_items.Clear(); s_items.AddRange(db.Items.Where(x => x != null));
        s_rows.Clear();
        s_base    = Snapshot(run.Player);
        s_index   = 0;
        s_placed  = false;
        s_running = true;
        s_next    = EditorApplication.timeSinceStartup + StepGap;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log($"[RuneAudit] 시작 — {s_items.Count}종");
    }

    private static void Tick()
    {
        if (!s_running) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) { Finish("play-stopped"); return; }
        double now = EditorApplication.timeSinceStartup;
        if (now < s_next) return;
        s_next = now + StepGap;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player == null) return;
        var inv = run.ItemInventory;
        // 가웨인 정오 게이지는 시간으로 돈다 — 안 묶으면 정오 버프(공속·치명·모든 피해)가 룬 스탯 변화로 잡힌다(09-19 1차 오분류).
        if (run.Player.TryGetComponent<ZenithGauge>(out var zg) &&
            (zg.CurrentPhase != ZenithGauge.ZPhase.Charging || zg.ChargeFill > 0.05f))
            zg.Restore(new RelicResourceState { fill = 0f, phase = (int)ZenithGauge.ZPhase.Charging, aux = 0f });

        if (!s_placed)
        {
            if (s_index >= s_items.Count) { Finish("done"); return; }
            var so = s_items[s_index];
            s_current = RuntimeItemData.FromSO(so);
            if (!inv.AddToStaging(s_current)) { Debug.LogWarning("[RuneAudit] 보관함 가득 — 비운 뒤 다시"); Finish("staging-full"); return; }
            inv.PlaceItem(s_current);
            s_placed = true;
            return;   // 다음 틱에 스탯·효과를 읽는다
        }

        var so2  = s_items[s_index];
        var snap = Snapshot(run.Player);
        var row  = new Row
        {
            id = so2.itemId, name = so2.displayName, rarity = so2.rarity.ToString(),
            shapeId = so2.shapeId, element = s_current.element ?? "",
            effects = string.Join("+", (s_current.effects ?? new List<ItemEffectSlot>()).Select(e => e.effectType)),
        };
        foreach (var kv in snap)
            if (s_base.TryGetValue(kv.Key, out double b) && System.Math.Abs(kv.Value - b) > 1e-4)
                row.diff[kv.Key] = kv.Value - b;
        foreach (var eff in run.EffectManager.ActiveEffects)
        {
            row.effectCount++;
            if (eff.GetType().Name == "GenericStatEffect") row.genericCount++;
        }
        s_rows.Add(row);

        inv.RemovePlaced(s_current);
        s_placed = false;
        s_index++;
    }

    /// <summary>플레이어 스탯 스냅숏 — 공개 프로퍼티 + 아이템 기여 비공개 필드(_item*, 치명타 등).</summary>
    private static Dictionary<string, double> Snapshot(PlayerController player)
    {
        var d = new Dictionary<string, double>();
        var st = player.RuntimeStats;
        if (st == null) return d;
        foreach (var p in st.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.PropertyType == typeof(int))   d[p.Name] = (int)p.GetValue(st);
            if (p.PropertyType == typeof(float)) d[p.Name] = (float)p.GetValue(st);
        }
        foreach (var f in st.GetType().GetFields(All))
        {
            if (!f.Name.StartsWith("_item") && !f.Name.Contains("Crit")) continue;
            if (f.FieldType == typeof(int))   d[f.Name] = (int)f.GetValue(st);
            if (f.FieldType == typeof(float)) d[f.Name] = (float)f.GetValue(st);
        }
        return d;
    }

    /// <summary>
    /// 속성 시너지 사슬 — 존 점유를 4단계 임계로 올렸다가 0으로 내린다. 게임이 부르는 그 메서드(OnZoneCellsUpdated)다.
    /// 확인: 차트 행 수 · 효과 인스턴스 클래스(기본 RuneEffect = 본문 없음) · 해제 후 잔존 0.
    /// </summary>
    private static string SynergyChainJson()
    {
        var bridge = MerlinRuneBridge.Instance;
        var disp   = GameRunBootstrapper.Instance?.Run?.Player?.RuneEffects;
        var data   = Managers.RuneData;
        if (bridge == null || disp == null || data == null) return "\"skipped(bridge/dispatcher/data 없음)\"";

        var sb = new StringBuilder("[");
        int k = 0;
        foreach (var zone in ElementDef.Order)
        {
            var rows = data.GetZoneSynergies(zone);
            int max = 0;
            var types = new List<string>();
            if (rows != null)
                foreach (var e in rows)
                {
                    if (e == null) continue;
                    max = Mathf.Max(max, e.threshold);
                    types.Add(e.effect_type);
                }

            bridge.OnZoneCellsUpdated(new Dictionary<string, int> { { zone, max } });
            var active = disp.Active.Select(a => a.GetType().Name).ToList();
            bridge.OnZoneCellsUpdated(new Dictionary<string, int>());
            int left = disp.Active.Count;

            Debug.Log($"[RuneAudit] 시너지 사슬 {zone}: 차트 {types.Count}행({string.Join(",", types)}) · 점유 {max} → 효과 {active.Count}개({string.Join(",", active)}) · 해제 후 {left}");
            if (k++ > 0) sb.Append(',');
            sb.Append("{\"zone\":\"").Append(zone).Append("\",\"rows\":\"").Append(string.Join(",", types))
              .Append("\",\"occupied\":").Append(max).Append(",\"active\":\"").Append(string.Join(",", active))
              .Append("\",\"left\":").Append(left).Append('}');
        }
        return sb.Append(']').ToString();
    }

    private static void Finish(string reason)
    {
        if (!s_running) return;
        s_running = false;
        EditorApplication.update -= Tick;
        var run = GameRunBootstrapper.Instance?.Run;
        if (s_placed && run != null) run.ItemInventory.RemovePlaced(s_current);
        s_placed = false;

        var sb = new StringBuilder();
        sb.Append("{\"reason\":\"").Append(reason).Append("\",\"items\":[");
        int statOnly = 0, mechanic = 0, generic = 0, dead = 0;
        for (int i = 0; i < s_rows.Count; i++)
        {
            var r = s_rows[i];
            string kind = r.diff.Count > 0 ? "stat"
                        : r.effectCount > r.genericCount ? "mechanic"
                        : r.genericCount > 0 ? "generic-fallback" : "dead";
            if (kind == "stat") statOnly++; else if (kind == "mechanic") mechanic++; else if (kind == "dead") dead++; else generic++;
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":\"").Append(r.id).Append("\",\"name\":\"").Append((r.name ?? "").Replace("\"", "'"))
              .Append("\",\"rarity\":\"").Append(r.rarity).Append("\",\"shape\":").Append(r.shapeId)
              .Append(",\"element\":\"").Append(r.element).Append("\",\"effects\":\"").Append(r.effects)
              .Append("\",\"kind\":\"").Append(kind).Append("\",\"effectCount\":").Append(r.effectCount)
              .Append(",\"genericCount\":").Append(r.genericCount).Append(",\"diff\":{");
            int k = 0;
            foreach (var kv in r.diff) { if (k++ > 0) sb.Append(','); sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value.ToString("0.####")); }
            sb.Append("}}");
        }
        sb.Append("],\"synergyChain\":");
        sb.Append(reason == "done" ? SynergyChainJson() : "\"skipped\"");
        sb.Append('}');
        File.WriteAllText(Path.Combine("Temp", "rune_stat_pass.json"), sb.ToString());
        Debug.Log($"[RuneAudit] 종료({reason}) — {s_rows.Count}종 · 스탯형 {statOnly} · 기술형 {mechanic} · 코드 없음(폴백) {generic} · 죽은 룬 {dead}");
    }

    // ── UI 흐름 ──────────────────────────────────────────────

    [MenuItem(Root + "UI 흐름 — 상점 룬 구매·4지선다·새 전설 룬 룬판 (플레이 중, 약 15초)")]
    private static void RunUiFlow()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneAudit] 플레이 모드에서만"); return; }
        if (GameRunBootstrapper.Instance?.Run?.ItemInventory == null) { Debug.LogWarning("[RuneAudit] 런에 들어간 뒤 실행"); return; }
        UiFlowAsync().Forget();
    }

    private static async UniTaskVoid UiFlowAsync()
    {
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        var db  = AssetDatabase.LoadAssetAtPath<ItemSODatabase>(Database);
        if (db == null) { Debug.LogError("[RuneAudit] 아이템 DB 없음"); return; }

        // ① 상점에서 룬을 산 순간 — 게임과 같은 경로(AbyssPeddlerCatalog.OpenGridForRune)로 룬판을 연다.
        GameObject ctrlGo = null;
        var rare = db.Items.FirstOrDefault(x => x != null && x.rarity == ItemRarity.Rare);
        var bought = rare != null ? RuntimeItemData.FromSO(rare) : null;
        try
        {
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            ctrlGo = new GameObject("~AuditShopController");
            var ctrl = ctrlGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_peddler", All)?.SetValue(ctrl, AbyssPeddlerCatalog.Build(new System.Random(7)));
            shop.Bind(ctrl);
            await UniTask.DelayFrame(5);

            bool added = bought != null && inv.AddToStaging(bought);
            typeof(AbyssPeddlerCatalog).GetMethod("OpenGridForRune", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, new object[] { bought, added });
            await UniTask.DelayFrame(20);

            var grid = UI_GridPanel.Instance;
            var shopCanvas = shop.GetComponent<Canvas>();
            var gridCanvas = grid != null ? grid.GetComponentInParent<Canvas>() : null;
            string top = TopRaycast(out var topGo);
            string owner = topGo == null ? "없음"
                         : grid != null && topGo.transform.IsChildOf(grid.transform) ? "룬판"
                         : topGo.transform.IsChildOf(shop.transform) ? "상점" : "기타";
            Debug.Log($"[RuneAudit] 상점 룬 구매 → 룬판: 상점 캔버스 order={(shopCanvas ? shopCanvas.sortingOrder : -1)} · " +
                      $"룬판 캔버스 order={(gridCanvas ? gridCanvas.sortingOrder : -1)}(override={(gridCanvas && gridCanvas.overrideSorting)}) · " +
                      $"룬판 열림={(grid != null && grid.IsOpen)} · 화면 중앙 최상단 클릭 대상={top} → {owner}");
            await Shot("audit_shop_buy_rune");

            grid?.Close();
            if (added) inv.DiscardFromStaging(bought);
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 상점 흐름 실패: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (ctrlGo != null) Object.Destroy(ctrlGo);
        }
        await UniTask.DelayFrame(10);

        // ② 정예방 4지선다 — Common · 효과 최다 · Epic · 빈 판에서도 못 놓는 전설.
        var picks = new List<ItemSO>();
        void Pick(ItemSO so) { if (so != null && !picks.Contains(so)) picks.Add(so); }
        Pick(db.Items.FirstOrDefault(x => x != null && x.rarity == ItemRarity.Common));
        Pick(db.Items.Where(x => x != null).OrderByDescending(x => RuntimeItemData.FromSO(x)?.effects?.Count ?? 0).FirstOrDefault());
        Pick(db.Items.FirstOrDefault(x => x != null && x.rarity == ItemRarity.Epic));
        var legend = db.Items.FirstOrDefault(x => x != null && x.itemId == DeadLegendId);
        Pick(legend);
        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(picks.Select(so => (RuntimeItemData.FromSO(so), so)).ToList(), inv);
            await UniTask.Delay(2500, ignoreTimeScale: true);
            foreach (var t in popup.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(t => t.name == "PlaceBadge" || t.name == "Name"))
                Debug.Log($"[RuneAudit] 4지선다 {t.name}: {t.text}");
            await Shot("audit_rune_select_4");
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 4지선다 실패: " + e.Message); }
        finally { Managers.UI.CloseAllPopupUI(); }
        await UniTask.DelayFrame(10);

        // ③ 못 놓는 전설 룬을 새로 받은 룬판 — 배치 안내가 무엇을 말하는가.
        if (legend != null)
        {
            var data = RuntimeItemData.FromSO(legend);
            bool added = inv.AddToStaging(data);
            try
            {
                if (UI_GridPanel.Instance == null) Managers.UI.ShowOverlayUI<UI_GridPanel>();
                UI_GridPanel.Instance?.ShowWithNewItem(data);
                await UniTask.DelayFrame(20);
                await Shot("audit_grid_new_legend");
            }
            catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 전설 룬판 실패: " + e.Message); }
            finally
            {
                UI_GridPanel.Instance?.Close();
                if (added) inv.DiscardFromStaging(data);
            }
        }
        Debug.Log("[RuneAudit] UI 흐름 끝 — Temp/ui_shots/audit_*.png");
    }

    /// <summary>
    /// 상점 새로고침 — 방문당 1회 확인. 실제 런을 컨트롤러에 꽂고 골드를 준 뒤 두 번 누른다(두 번째는 막혀야 한다).
    /// 설정은 @GameRun 값(켜짐·비용)을 그대로 쓴다.
    /// </summary>
    [MenuItem(Root + "UI 흐름 — 상점 새로고침 1회 확인 (플레이 중, 약 5초)")]
    private static void RunRerollCheck()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneAudit] 플레이 모드에서만"); return; }
        if (GameRunBootstrapper.Instance?.Run?.PlayerState == null) { Debug.LogWarning("[RuneAudit] 런에 들어간 뒤 실행"); return; }
        RerollCheckAsync().Forget();
    }

    private static async UniTaskVoid RerollCheckAsync()
    {
        var run = GameRunBootstrapper.Instance.Run;
        var boot = GameRunBootstrapper.Instance;
        bool enabled = (bool)(typeof(GameRunBootstrapper).GetField("shopRerollEnabled", All)?.GetValue(boot) ?? false);
        int  cost    = (int)(typeof(GameRunBootstrapper).GetField("shopRerollCost", All)?.GetValue(boot) ?? -1);
        GameObject ctrlGo = null;
        try
        {
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            ctrlGo = new GameObject("~AuditShopController");
            var ctrl = ctrlGo.AddComponent<ShopRoomController>();
            var t = typeof(ShopRoomController);
            t.GetField("_run", All)?.SetValue(ctrl, run);
            t.GetField("_rerollEnabled", All)?.SetValue(ctrl, enabled);
            t.GetField("_rerollCost", All)?.SetValue(ctrl, cost);
            t.GetField("_peddler", All)?.SetValue(ctrl, AbyssPeddlerCatalog.Build(new System.Random(7)));
            run.PlayerState.AddTempGold(100);
            shop.Bind(ctrl);
            await UniTask.DelayFrame(5);
            await Shot("audit_reroll_0_before");

            int g0 = run.PlayerState.TempGold;
            bool first  = ctrl.TryReroll();
            int g1 = run.PlayerState.TempGold;
            bool second = ctrl.TryReroll();
            typeof(UI_ShopPanel).GetMethod("RefreshAll", All)?.Invoke(shop, null);
            await UniTask.DelayFrame(5);
            string label = ShopUIStyle.FindDeep(shop.transform, "Reroll")?.GetComponentInChildren<TMPro.TMP_Text>(true)?.text;
            Debug.Log($"[RuneAudit] 새로고침: @GameRun 켜짐={enabled} 비용={cost} · 1회차={first}(골드 {g0}→{g1}) · 2회차={second} · 버튼 「{label}」");
            await Shot("audit_reroll_1_after");
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 새로고침 확인 실패: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (ctrlGo != null) Object.Destroy(ctrlGo);
        }
    }

    /// <summary>
    /// 새 룬 픽업 연출 확인 — 전설 포함 4장(내려옴·뒤집기·전설 멈춤·개봉·끝)과 선택 → 보관함으로 날아감, 일반 3장.
    /// 문양 배정표(등급·컨셉별 스프라이트 이름)를 Temp/rune_icon_map.txt로, 보관함·판 조각의 전설 광택 유무를 로그로 남긴다.
    /// </summary>
    [MenuItem(Root + "UI 흐름 — 룬 픽업 연출·문양 배정 (플레이 중, 약 15초)")]
    private static void RunPickupFlow()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneAudit] 플레이 모드에서만"); return; }
        if (GameRunBootstrapper.Instance?.Run?.ItemInventory == null) { Debug.LogWarning("[RuneAudit] 런에 들어간 뒤 실행"); return; }
        PickupFlowAsync().Forget();
    }

    private static async UniTaskVoid PickupFlowAsync()
    {
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var db  = AssetDatabase.LoadAssetAtPath<ItemSODatabase>(Database);
        if (db == null) { Debug.LogError("[RuneAudit] 아이템 DB 없음"); return; }
        ItemSO Find(string id) => db.Items.FirstOrDefault(x => x != null && x.itemId == id);

        // ① 문양 배정표
        var map = new StringBuilder();
        var byIcon = new Dictionary<string, List<string>>();
        foreach (var so in db.Items.Where(x => x != null).OrderBy(x => x.rarity).ThenBy(x => x.itemId))
        {
            var d = RuntimeItemData.FromSO(so);
            string icon = RuneArt.ResolveRuneIcon(d)?.name ?? "(없음)";
            map.AppendLine($"{so.rarity}	{so.itemId}	{d.displayName}	{RuneArt.EffectTypeOf(d)}	{icon}");
            if (!byIcon.TryGetValue(icon, out var l)) byIcon[icon] = l = new List<string>();
            l.Add(so.itemId);
        }
        File.WriteAllText(Path.Combine("Temp", "rune_icon_map.txt"), map.ToString());
        Debug.Log($"[RuneAudit] 문양 배정: 룬 {db.Items.Count(x => x != null)} · 쓰인 문양 {byIcon.Count}장 · " +
                  string.Join(" / ", byIcon.OrderByDescending(kv => kv.Value.Count).Select(kv => $"{kv.Key}×{kv.Value.Count}")));

        // ② 전설 포함 4장 — 시점별 캡처
        var four = new[] { "item_t1_weight", "item_t2_crisis_sword", "item_t3_fire_ember", "item_t4_fire_aoe" }
                   .Select(Find).Where(x => x != null).ToList();
        var three = new[] { "item_t1_dull_shield", "item_t2_iron_armor", "item_t3_ice_frost" }.Select(Find).Where(x => x != null).ToList();

        // 캡처는 프레임을 멈춰 연출 시간을 늘린다 — 길이는 캡처 없이 따로 잰다(끝 = _revealing이 꺼진 시점).
        var revealingField = typeof(UI_RuneSelectPopup).GetField("_revealing", All);
        foreach (var (set, label) in new[] { (four, "전설 포함 4장"), (three, "일반 3장") })
        {
            try
            {
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
                float t0 = Time.unscaledTime;
                popup.Setup(set.Select(so => (RuntimeItemData.FromSO(so), so)).ToList(), inv);
                while ((bool)revealingField.GetValue(popup) && Time.unscaledTime - t0 < 6f) await UniTask.Yield();
                Debug.Log($"[RuneAudit] 연출 길이({label}): {Time.unscaledTime - t0:0.00}초 · 모드 {RewardPresentation.Mode}");
            }
            catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 연출 길이 측정 실패: " + e.Message); }
            finally { Managers.UI.CloseAllPopupUI(); }
            await UniTask.DelayFrame(5);
        }
        var legendData = (RuntimeItemData)null;
        int stagedBefore = inv.StagingCount;
        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            var cands = four.Select(so => (RuntimeItemData.FromSO(so), so)).ToList();
            legendData = cands.Last().Item1;
            float t0 = Time.unscaledTime;
            popup.Setup(cands, inv);
            foreach (var (at, name) in new[] { (0.30f, "pickup_1_deal"), (0.80f, "pickup_2_flip"), (1.15f, "pickup_3_legend_hold"),
                                               (1.55f, "pickup_4_legend_open"), (2.60f, "pickup_5_final") })
            {
                while (Time.unscaledTime - t0 < at) await UniTask.Yield();
                await ShotNow(name);
                Debug.Log($"[RuneAudit] 픽업 캡처 {name} @ {Time.unscaledTime - t0:0.00}s");
            }

            typeof(UI_RuneSelectPopup).GetMethod("SetSelected", All)?.Invoke(popup, new object[] { 3 });
            await UniTask.Delay(300, ignoreTimeScale: true);
            await ShotNow("pickup_6_selected");
            typeof(UI_RuneSelectPopup).GetMethod("OnConfirmClicked", All)?.Invoke(popup, null);
            await UniTask.Delay(140, ignoreTimeScale: true);
            await ShotNow("pickup_7_fly");
            await UniTask.Delay(900, ignoreTimeScale: true);
            bool closed = popup == null || !popup.gameObject.activeInHierarchy;
            Debug.Log($"[RuneAudit] 확정: 팝업 닫힘={closed} · 보관함 {stagedBefore}→{inv.StagingCount} · 룬판 열림={UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen}");

            // ③ 보관함·판 조각의 전설 광택
            await UniTask.Delay(600, ignoreTimeScale: true);
            var stagingView = Object.FindFirstObjectByType<StagingAreaView>();
            var shape = stagingView != null ? stagingView.GetShapeForItem(legendData) : null;
            int sheenBlocks = shape != null ? shape.GetComponentsInChildren<StagingSlotShimmer>(true).Length : -1;
            int blocks = shape != null ? shape.transform.childCount : -1;
            int slotSheen = stagingView != null ? stagingView.GetComponentsInChildren<StagingSlotShimmer>(true).Length : -1;
            Debug.Log($"[RuneAudit] 전설 광택: 판 조각 {sheenBlocks}/{blocks}칸 · 보관함 광택 컴포넌트 {slotSheen}개");
            await ShotNow("pickup_8_grid_legend");
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 픽업 연출 실패: " + e); }
        finally
        {
            UI_GridPanel.Instance?.Close();
            Managers.UI.CloseAllPopupUI();
            if (legendData != null && inv.StagingCount > stagedBefore) inv.DiscardFromStaging(legendData);
        }
        await UniTask.DelayFrame(10);

        // ④ 일반 방 3장(전설 없음)
        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            float t0 = Time.unscaledTime;
            popup.Setup(three.Select(so => (RuntimeItemData.FromSO(so), so)).ToList(), inv);
            while (Time.unscaledTime - t0 < 0.55f) await UniTask.Yield();
            await ShotNow("pickup_9_normal_mid");
            while (Time.unscaledTime - t0 < 1.6f) await UniTask.Yield();
            await ShotNow("pickup_10_normal_final");
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] 일반 3장 실패: " + e); }
        finally { Managers.UI.CloseAllPopupUI(); }
        Debug.Log("[RuneAudit] 룬 픽업 연출 끝 — Temp/ui_shots/pickup_*.png · Temp/rune_icon_map.txt");
    }

    /// <summary>
    /// 전설 광역 이펙트 3종(심연 잠식·신성 폭발·마그마 분출)의 GrabPass 왜곡 조각 확인 — 원본 프리팹을 그대로 띄운 것과
    /// 게임 경로(LegendaryRuntime.SpawnVfx, 왜곡 조각 끔)를 번갈아 띄워 켜진 왜곡 렌더러 수·_GrabTexture 오류 수·화면을 남긴다.
    /// </summary>
    [MenuItem(Root + "전설 이펙트 — GrabPass 왜곡 조각 확인 (플레이 중, 약 10초)")]
    private static void RunGrabPassCheck()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneAudit] 플레이 모드에서만"); return; }
        if (GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[RuneAudit] 런에 들어간 뒤 실행"); return; }
        GrabPassCheckAsync().Forget();
    }

    private static async UniTaskVoid GrabPassCheckAsync()
    {
        LegendaryRuntime.EnsureExists();
        var cat = LegendaryRuntime.Catalog;
        var pos = GameRunBootstrapper.Instance.Run.Player.transform.position;
        var sets = new (string name, GameObject prefab)[] { ("심연 잠식", cat?.darkAoeVfx), ("신성 폭발", cat?.lightAoeVfx), ("마그마 분출", cat?.fireAoeVfx) };
        int grabErrors = 0;
        void OnLog(string msg, string st, LogType t) { if (msg != null && msg.Contains("_GrabTexture")) grabErrors++; }
        Application.logMessageReceived += OnLog;
        try
        {
            await ShotNow("grabpass_0_none");
            foreach (var (name, prefab) in sets)
            {
                if (prefab == null) { Debug.LogWarning($"[RuneAudit] {name} 프리팹 없음"); continue; }
                foreach (bool viaGame in new[] { false, true })
                {
                    var before = new HashSet<GameObject>(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Select(r => r.transform.root.gameObject));
                    GameObject raw = null;
                    grabErrors = 0;
                    if (viaGame) LegendaryRuntime.SpawnVfx(prefab, pos, Quaternion.identity, 1f, 1f);   // 1초 뒤 스스로 사라진다
                    else raw = Object.Instantiate(prefab, pos, Quaternion.identity);
                    await UniTask.Delay(500, ignoreTimeScale: true);
                    int live = 0;
                    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        var root = r.transform.root.gameObject;
                        if (before.Contains(root)) continue;
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                        if (sh != null && sh.name.Contains("Distort")) live++;
                    }
                    string tag = viaGame ? "game" : "raw";
                    await ShotNow($"grabpass_{name}_{tag}");
                    Debug.Log($"[RuneAudit] GrabPass {name} · {(viaGame ? "게임 경로(SpawnVfx)" : "원본 프리팹")}: 켜진 왜곡 렌더러 {live} · _GrabTexture 오류 {grabErrors}건");
                    if (raw != null) Object.Destroy(raw);
                    await UniTask.Delay(700, ignoreTimeScale: true);
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[RuneAudit] GrabPass 확인 실패: " + e); }
        finally { Application.logMessageReceived -= OnLog; }
        Debug.Log("[RuneAudit] GrabPass 확인 끝 — Temp/ui_shots/grabpass_*.png");
    }

    private static async UniTask ShotNow(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) { Debug.LogWarning("[RuneAudit] ShotAsync 없음 — 스크린샷 생략"); return; }
        await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static string TopRaycast(out GameObject top)
    {
        top = null;
        var es = EventSystem.current;
        if (es == null) return "EventSystem 없음";
        var results = new List<RaycastResult>();
        es.RaycastAll(new PointerEventData(es) { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) }, results);
        if (results.Count == 0) return "없음";
        top = results[0].gameObject;
        var t = top.transform;
        string path = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    /// <summary>레이아웃 프로브의 스크린샷(프레임 끝 백버퍼)을 그대로 쓴다 — Temp/ui_shots/&lt;name&gt;.png.</summary>
    private static async UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) { Debug.LogWarning("[RuneAudit] ShotAsync 없음 — 스크린샷 생략"); return; }
        await (UniTask)m.Invoke(null, new object[] { name, 500 });
    }
}
