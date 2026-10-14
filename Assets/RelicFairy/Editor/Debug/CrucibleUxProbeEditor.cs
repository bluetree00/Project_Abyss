#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [촬영 · 플레이 중] 10-11 재련소 위계 · 가독성 개편(UI4종 개편설계 v2 · 비교 리서치 C §1 「고칠 값」) — 화면 6장.
/// 입구 · 무기 강화(+2 · +5) · 원거리 파츠 · 진화(형 고르기) · 화로 칩. 후광 수 · 채움 버튼 수 · 글자 단계 · 대사 띠 · 판 테를 같이 잰다.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면(자동 플레이 「방 진입」) 멈추고 돈다.
/// 결과 Temp/crucible_ux.txt · Temp/ui_shots/CX_*.png · 로그 「[CrucibleUx] 끝」.
/// </summary>
public static class CrucibleUxProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly float[] Steps = { 13f, 15f, 17f, 20f, 24f, 32f, 48f };   // v2 글자 단계(13 = 조작 힌트)
    private static readonly string[] OldHex = { "#FFD24A", "#FF5250", "#8AB0D5", "#FFA35A", "#E8C07A", "#7FE3FF", "#FF9A5A", "#C9A66A", "#FFB45A" };
    private static bool s_armed;
    private static int  s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-11 재련소 화면 촬영 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CrucibleUx] 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[CrucibleUx] 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[CrucibleUx] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("재련소 위계 · 가독성 개편 촬영(10-11)\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var go = new GameObject("~CrucibleUxProbe");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(1.5f, ct);

            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
            if (wm.Weapon1Data == null)
            {
                var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
                if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
            }
            if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            SetLevel(wm.Weapon0Data, 2);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 60);
            HubGui("HideTestHubGui");

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            typeof(CrucibleRoomController).GetField("_event", Inst)?.SetValue(ctrl, CrucibleEvent.None);   // 화로 칩은 6번 장에서 켠다
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            var root = panel.transform;
            var win  = F<RectTransform>(panel, "_window");

            // ── 1 입구 — 두 카드 같은 형식 4줄 · 후광 0 · 할 수 있는 쪽 테 = 금(준비됨) · 큰 수 48 · 안내는 호버 때만
            var chooser = F<GameObject>(panel, "_chooser");
            var cardA = Find(root, "ChooseMelee");
            var cardB = Find(root, "ChooseRanged");
            int la = Lines(cardA), lb = Lines(cardB);
            Check(sb, "입구 — 두 카드 같은 형식(보이는 줄 3~4 · 같은 수)", chooser != null && chooser.activeSelf && la == lb && la >= 3 && la <= 4, $"근거리 {la}줄 · 원거리 {lb}줄");
            Check(sb, "입구 — 후광 0", Halos(root, out string hl) == 0, hl);
            var frames = F<UIThinFrame[]>(panel, "_chooseFrame");
            Check(sb, "입구 — 할 수 있는 카드 테 = 준비됨(금 1px)", frames != null && frames[0] != null && frames[0].State == ThinFrameState.Ready,
                  $"근거리 {frames?[0]?.State} · 원거리 {frames?[1]?.State}");
            var big = Find(cardA, "Big")?.GetComponent<TMP_Text>();
            Check(sb, "입구 — 큰 수 48 · 가짜 굵게 없음", big != null && Mathf.Abs(big.fontSize - 48f) < 0.6f && (big.fontStyle & FontStyles.Bold) == 0,
                  $"{big?.fontSize:0.#} · {big?.fontStyle} · 「{Parsed(big)}」");
            var hints = F<CanvasGroup[]>(panel, "_chooseHint");
            Check(sb, "입구 — 「눌러서 들어간다」 평소 숨김", hints != null && hints[0] != null && hints[0].alpha < 0.05f, $"{hints?[0]?.alpha:0.00}");
            Sizes(sb, "입구 글자 단계", chooser != null ? chooser.transform : null, win);
            NoOldHex(sb, "입구", chooser != null ? chooser.transform : null);
            await Shot("CX_1_Entrance", 300);
            Call(panel, "ShowChooseHint", 0, true);
            await Wait(0.35f, ct);
            Check(sb, "입구 — 호버하면 안내가 보인다", hints != null && hints[0] != null && hints[0].alpha > 0.95f, $"{hints?[0]?.alpha:0.00}");

            // ── 2 무기 강화 +2 — 후광 1곳(강화하기 · 8px) · 정보판 1px 테 · 대사 띠 64 · 글자 20 · 잉크 0.7 · 큰 숫자 48
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.6f, ct);
            var enh = F<Button>(panel, "_enhanceBtn");
            Check(sb, "무기 +2 — 후광 1곳 = [강화하기]", Halos(root, out hl) == 1 && hl.Contains("Enhance"), hl);
            var glow = enh != null ? enh.transform.Find("AffordGlow") as RectTransform : null;
            Check(sb, "후광 번짐 8px", glow != null && Mathf.Abs(glow.offsetMax.x - 8f) < 0.5f, $"{glow?.offsetMax.x:0.#}");
            var info = Find(root, "InfoPanel");
            var infoImg = info != null ? info.GetComponent<Image>() : null;
            Check(sb, "정보판 — 주황 아트 걷고 1px 테", infoImg != null && infoImg.sprite == null && info.Find("ThinEdge") != null, infoImg?.sprite?.name ?? "아트 없음");
            var band = Find(root, "DialogueBand") as RectTransform;
            float k = win != null ? win.rect.height / 1000f : 1f;
            var dlg = F<TMP_Text>(panel, "_dialogText");
            Check(sb, "대사 띠 — 높이 64 · 글자 20 · 잉크 0.7", band != null && Mathf.Abs(band.rect.height / k - 64f) < 1.5f && dlg != null
                  && Mathf.Abs(dlg.fontSize - 20f) < 0.6f && Mathf.Abs(dlg.color.a - 0.7f) < 0.02f,
                  $"띠 {(band != null ? band.rect.height / k : 0f):0.#} · 글자 {dlg?.fontSize:0.#} · 알파 {dlg?.color.a:0.00}");
            var rate = F<TMP_Text>(panel, "_successText");
            Check(sb, "성공률 48 · Noto 300(72 숫자 0)", rate != null && Mathf.Abs(rate.fontSize - 48f) < 0.6f, $"{rate?.fontSize:0.#} · 「{Parsed(rate)}」");
            Check(sb, "판 겹침 없음(무대 · 정보창 ↔ 대사 띠)", NoOverlap(root, out string ov), ov);
            Sizes(sb, "무기 +2 글자 단계", win, win);
            NoOldHex(sb, "무기 +2", win);
            await Shot("CX_2_Weapon", 300);

            // ── 3 무기 +5 — [강화하기] 채움 + [진화] 채움 0 · 1px 테 · 후광은 여전히 1곳
            SetLevel(wm.Weapon0Data, 5);
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            var evo = F<Button>(panel, "_evolveBtn");
            var evoImg = evo != null ? evo.GetComponent<Image>() : null;
            Check(sb, "무기 +5 — 채움 주 버튼 1개([진화] 채움 0 · 1px 테)", evo != null && evo.gameObject.activeInHierarchy && evoImg != null && evoImg.color.a < 0.01f
                  && evo.transform.Find("ThinEdge") != null, $"진화 채움 알파 {evoImg?.color.a:0.00}");
            Check(sb, "무기 +5 — 후광 1곳", Halos(root, out hl) == 1, hl);
            int fuel = run.FuelBank.EnhanceMaterial;
            run.FuelBank.TrySpend(FuelKind.EnhanceMaterial, fuel);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            Check(sb, "재료 0 — 후광이 [진화]로 옮겨 감(여전히 1곳)", Halos(root, out hl) == 1 && hl.Contains("Evolve"), hl);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, fuel);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            Sizes(sb, "무기 +5 글자 단계", win, win);
            await Shot("CX_3_Weapon5", 300);

            // ── 4 원거리 파츠 — 행 버튼 채움 0 · 후광은 고른 행 하나 · 상세판 1px 테
            Call(panel, "SelectTab", 1);
            await Wait(0.5f, ct);
            Check(sb, "원거리 — 후광 1곳 이하(고른 행)", Halos(root, out hl) <= 1, hl);
            var rowBtn = Find(Find(root, "PartRow0"), "RowEnhance")?.GetComponent<Image>();
            Check(sb, "원거리 — 행 버튼 채움 0 · 1px 테", rowBtn != null && rowBtn.color.a < 0.01f && rowBtn.transform.Find("ThinEdge") != null, $"{rowBtn?.color.a:0.00}");
            var pd = Find(root, "PartDetail")?.GetComponent<Image>();
            Check(sb, "원거리 상세판 — 1px 테", pd != null && pd.sprite == null && pd.transform.Find("ThinEdge") != null, pd?.sprite?.name ?? "아트 없음");
            Sizes(sb, "원거리 글자 단계", win, win);
            NoOldHex(sb, "원거리", win);
            await Shot("CX_4_Ranged", 300);

            // ── 5 진화(형 고르기) — 글자 15 이상 · 다섯 단계 · 성격 칩 무채 · 수치 머리 · 바닥 글 숨김
            Call(panel, "SelectTab", 0);
            Call(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            Call(panel, "PickEvolveCard", ki);
            await Wait(0.5f, ct);
            var A = Find(root, ki == 0 ? "EvolveA" : "EvolveB");
            var persona = Find(Find(A, "Persona"), "Text")?.GetComponent<TMP_Text>();
            var pc = persona != null ? persona.color : Color.clear;
            Check(sb, "성격 칩 — 무채 글자 + 모양(◇ 빠른 검)", persona != null && Parsed(persona).Contains("◇") && Mathf.Abs(pc.r - pc.b) < 0.12f,
                  $"「{Parsed(persona)}」 {pc}");
            var sh = Find(A, "StatsHead");
            var foot = Find(A, "Foot");
            Check(sb, "보조 문장 — 수치 머리 · 바닥 글 숨김(열린 갈래)", (sh == null || !sh.gameObject.activeInHierarchy) && (foot == null || !foot.gameObject.activeInHierarchy),
                  $"수치 머리 {sh?.gameObject.activeInHierarchy} · 바닥 {foot?.gameObject.activeInHierarchy}");
            Check(sb, "카드 글 묶음 12 이하(작동 3컷 = 3)", Groups(A, out string gd) <= 12, gd);
            var evoContent = Find(root, "EvolveContent");
            Sizes(sb, "진화 글자 단계(그림 속 글자 제외)", evoContent, win, minOk: 15f, skipUnder: "Draw");
            NoOldHex(sb, "진화", evoContent);
            await Shot("CX_5_Evolve", 300);
            Call(panel, "HideEvolvePanel");

            // ── 6 화로 칩 — 붉은 상자 → 얇은 선 칩 「▲ 화로 · …」(금) · 저주는 빨강 「▼」
            typeof(CrucibleRoomController).GetField("_event", Inst)?.SetValue(ctrl, CrucibleEvent.Discount);
            Call(panel, "RefreshAll");
            await Wait(0.4f, ct);
            var chipText = F<TMP_Text>(panel, "_eventBannerText");
            Check(sb, "화로 칩 — 「▲ 화로 · 재료 비용 50%」 · 금", chipText != null && chipText.gameObject.activeInHierarchy && Parsed(chipText).StartsWith("▲ 화로")
                  && Mathf.Abs(chipText.color.g - UITheme.Thin.Gold.g) < 0.05f, $"「{Parsed(chipText)}」 {chipText?.color}");
            var chipRt = F<GameObject>(panel, "_eventBanner")?.transform as RectTransform;
            Check(sb, "화로 칩 높이 36 · 띠 안", chipRt != null && Mathf.Abs(chipRt.rect.height / k - 36f) < 1.5f, $"{(chipRt != null ? chipRt.rect.height / k : 0f):0.#}");
            await Shot("CX_6_ForgeChip", 300);
            typeof(CrucibleRoomController).GetField("_event", Inst)?.SetValue(ctrl, CrucibleEvent.Curse);
            Call(panel, "RefreshAll");
            await Wait(0.3f, ct);
            Check(sb, "저주 — 「▼」 · 빨강", chipText != null && Parsed(chipText).StartsWith("▼") && chipText.color.r > 0.9f && chipText.color.g < 0.6f, $"「{Parsed(chipText)}」");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/crucible_ux.txt", sb.ToString());
            Debug.Log("[CrucibleUx] 끝\n" + sb);
        }
    }

    // ── 재는 도구 ──

    /// <summary>켜진 후광 수 — UIAffordGlow가 켜짐(_on)이고 화면에 살아 있는 것. 이름 목록을 함께.</summary>
    private static int Halos(Transform root, out string names)
    {
        int n = 0;
        var sb = new StringBuilder();
        var on = typeof(UIAffordGlow).GetField("_on", Inst);
        foreach (var g in root.GetComponentsInChildren<UIAffordGlow>(false))
        {
            if (!g.isActiveAndEnabled || on == null || !(bool)on.GetValue(g)) continue;
            n++;
            sb.Append(g.name).Append(' ');
        }
        names = n == 0 ? "없음" : sb.ToString();
        return n;
    }

    /// <summary>카드 안 보이는 글 줄 수(빈 글 · 호버 안내 제외).</summary>
    private static int Lines(Transform card)
    {
        if (card == null) return -1;
        int n = 0;
        foreach (var t in card.GetComponentsInChildren<TMP_Text>(false))
            if (t.name != "Hint" && t.gameObject.activeInHierarchy && Parsed(t).Trim().Length > 0) n++;
        return n;
    }

    /// <summary>진화 카드의 글 묶음 — 작동 그림 한 컷 · 수치 칸 한 줄 · 칩 한 줄은 한 묶음으로 센다.</summary>
    private static int Groups(Transform card, out string detail)
    {
        detail = "";
        if (card == null) return 99;
        var seen = new HashSet<string>();
        foreach (var t in card.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || Parsed(t).Trim().Length == 0) continue;
            string key = t.name;
            for (var p = t.transform.parent; p != null && p != card; p = p.parent)
            {
                if (p.name.StartsWith("Cut")) { key = p.name; break; }
                if (p.name.StartsWith("Stat")) { key = "Stats"; break; }
                if (p.name is "Persona" or "Family") { key = "Chips"; break; }
                if (p.name == "LostChip") { key = "Lost"; break; }
            }
            if (t.name == "Lost") key = "Lost";
            if (t.name == "Arrow") continue;
            seen.Add(key);
        }
        detail = $"{seen.Count}묶음: " + string.Join(" · ", seen);
        return seen.Count;
    }

    /// <summary>
    /// 보이는 글자 크기 단계 — 창 맞춤 배율(ScaleFonts)을 나눠 1080 기준으로 본다. 단계 밖 · 최소 미만을 이름과 함께 적는다.
    /// 판정 도장(StageResult) · 잭팟 배너는 연출 순간 글자라 뺀다.
    /// </summary>
    private static void Sizes(StringBuilder sb, string what, Transform scope, RectTransform win, float minOk = 13f, string skipUnder = null)
    {
        if (scope == null) { Check(sb, what, false, "범위 없음"); return; }
        float k = win != null ? win.rect.height / 1000f : 1f;
        var used = new SortedSet<int>();
        var bad = new StringBuilder();
        foreach (var t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || Parsed(t).Trim().Length == 0) continue;
            if (t.name is "StageResult" or "JackpotBanner") continue;
            if (skipUnder != null && Under(t.transform, skipUnder, scope)) continue;
            float s = NearStep(t.fontSize) ? t.fontSize : t.fontSize / k;
            used.Add(Mathf.RoundToInt(s));
            if (!NearStep(s) || s < minOk - 0.6f) bad.Append($"{t.name}={s:0.#} ");
        }
        Check(sb, what + $" — 단계 {used.Count}개", bad.Length == 0 && used.Count <= 6, $"[{string.Join(",", used)}] " + (bad.Length == 0 ? "단계 밖 없음" : "단계 밖: " + bad));
    }

    private static bool NearStep(float s)
    {
        foreach (var x in Steps) if (Mathf.Abs(s - x) < 0.6f) return true;
        return false;
    }

    private static bool Under(Transform t, string name, Transform stop)
    {
        for (var p = t.parent; p != null && p != stop; p = p.parent) if (p.name == name) return true;
        return false;
    }

    /// <summary>예전 강조 색(노랑 숫자 · 주황 · 하늘 · 진한 빨강)이 보이는 글에 남았나.</summary>
    private static void NoOldHex(StringBuilder sb, string what, Transform scope)
    {
        if (scope == null) return;
        var hit = new StringBuilder();
        foreach (var t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
            if (t.name is "StageResult" or "JackpotBanner") continue;
            foreach (var h in OldHex) if (t.text.Contains(h)) hit.Append($"{t.name}:{h} ");
        }
        Check(sb, what + " — 예전 강조 색 0", hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());
    }

    /// <summary>무대 · 정보창 바닥이 대사 띠 위에 있고, 막대가 무대 안에 있다.</summary>
    private static bool NoOverlap(Transform root, out string why)
    {
        var stage = Find(root, "StageArea") as RectTransform;
        var info  = Find(root, "InfoPanel") as RectTransform;
        var band  = Find(root, "DialogueBand") as RectTransform;
        var gauge = Find(root, "Gauge") as RectTransform;
        var evo   = Find(info, "Evolve") as RectTransform;
        if (stage == null || info == null || band == null) { why = "판 없음"; return false; }
        var sb = new StringBuilder();
        if (Bottom(stage) < Top(band) - 0.5f) sb.Append($"무대↔띠 {Top(band) - Bottom(stage):0} ");
        if (Bottom(info) < Top(band) - 0.5f)  sb.Append($"정보↔띠 {Top(band) - Bottom(info):0} ");
        if (gauge != null && gauge.gameObject.activeInHierarchy && Bottom(gauge) < Bottom(stage) - 0.5f) sb.Append("막대가 무대 밖 ");
        if (evo != null && evo.gameObject.activeInHierarchy && Bottom(evo) < Bottom(info) - 0.5f) sb.Append("진화 버튼이 판 밖 ");
        why = sb.Length == 0 ? "없음" : sb.ToString();
        return sb.Length == 0;
    }

    private static float Top(RectTransform rt)    { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[1].y; }
    private static float Bottom(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[0].y; }

    private static void Check(StringBuilder sb, string what, bool ok, string detail)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine($"  CHECK {what}: {ok} ({detail})");
    }

    private static void SetLevel(WeaponData wd, int lv)
    {
        if (wd == null) return;
        wd.enhanceLevel = lv;
        wd.RecomputeEnhancedStats();
    }

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        var preload = typeof(PlayerWeaponManager).GetMethod("PreloadWeaponClipsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (preload != null) await (UniTask)preload.Invoke(null, new object[] { runtime, ct });
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        await Wait(0.5f, ct);
        return wm.Weapon0Data != null && wm.Weapon0Data.evolution != null;
    }

    private static string Parsed(TMP_Text t) => t != null && t.gameObject.activeInHierarchy ? t.GetParsedText() : "";

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, PlayerLoopTiming.Update, ct);
}
#endif
