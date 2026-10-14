#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 뽑기 연출 촬영 — v4(10-14 저녁) 전체 화면 제단 + 「아발론의 밤」(설계 정제소_v4_제단화면·연출 · v3 1-1 · 3장). 같은 메뉴를 갈아 끼웠다.
/// 테스트 허브에서 Ch1 런을 띄우고 원석 40을 준 뒤 정제소를 연다(<see cref="RefineryEscProbeEditor"/>와 같은 길). 칸 7개:
///  ① 제단 화면 1장 + 수치(전체 화면 제단 켜짐 · 옛 창 숨김 · 등급 세 줄 = 서비스 확률 · 비용 · 글자 최소 · 넘침 · 겹침)
///  ② 실제 [정제하기] 한 번(리플렉션 OnSpinClicked — 희귀 45 · 영웅 40 · 전설 15) — 끝 상태(버튼 · 입력 열림 시각) · 빈 곳 누르기 → 제단 화면 ·
///     제단 화면에서 빈 곳 누르기 → 닫히지 않음
///  ③ 등급 3종 강제 — 연출 함수(PlayGachaAsync)에 등급을 넣은 outcome을 직접 준다(에디터 전용 · 서비스 · 원석 · 보관함 무관).
///     v5 사진 — 희귀: 충전 1.2 · 방출 2.0 · 낙성 3.0 · 착지 4.46 · 실루엣 5.0 / 영웅: 고비 3.4 / 전설: 고비 3.95 · 모여듦 5.0 · 불티 6.4 · 각 끝(입력 열림 + 0.25초)
///  ④ 「두 장 중 하나」(Epic + Rare) 끝 1장 ⑤ 건너뛰기 1회(전설 0.5초에 입력 → 끝 모습까지 걸린 시간 · 섬광 보장) 1장
/// CHECK(설계 값으로 잰다 — 구현 상수를 읽지 않는다): 글자 최소 16 · 넘침 0 · 겹침 0 · 은백을 떠나는 첫 고비 2.7±0.1초(실측 + 색 함수) ·
/// 고비마다 위로만 · 마지막 단계 = 등급 · 그 고비(2.7 / 3.25 / 3.8)±0.15초에 도달 · 착지 때 속성 보석 맨 위(±6°) ·
/// 입력 열림 Rare 6.1 / Epic 6.4 / Legend 7.4 ±0.2초 · 건너뛰기 0.15초 · 건너뛰어도 섬광 한 번 ·
/// 착지 섬광 0.015~0.04(v4 — 화면을 덮지 않는 「쿵」 · UI는 선형 색공간이라 알파가 낮아도 밝다) · 전설 금빛 불티 12개 이상.
/// 결과: Temp/refinery_gacha_probe.txt · Temp/ui_shots/RefWish_*.png · 로그 「[RefineryGacha] 끝」. CHECK 줄이 전부 True면 합격.
/// </summary>
public static class RefineryGachaProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ShotDir = "Temp/ui_shots/";
    private const float TintAt     = 2.70f;   // v5 설계 — 첫 고비(은백 → Rare)
    private const float TintTol    = 0.10f;
    private const float OpenTol    = 0.20f;
    private const float SkipMax    = 0.15f;
    private const float FrameSlack = 0.02f;   // 한 프레임 여유(건너뛰기)
    private const float ArriveTol  = 0.15f;   // 마지막 단계 도달 = 그 고비 ± (전환 0.06초 + 에디터 한 프레임 ~0.1초)
    private const float LandAt     = 4.40f;   // v5 설계 — 착지(보석 룰렛이 멎음)
    private const float AltarCy    = 150f;    // 제단 중심(화면 가운데 기준 y)
    private const float GemTopTol  = 6f;      // 맨 위 ±도
    private static readonly float[] CheckAt = { 2.70f, 3.25f, 3.80f };   // v5 설계 — 오르는 고비
    private const float SettleTol  = 0.05f;   // 이만큼 가까워야 그 등급 색으로 「자리 잡음」 — 전환 중간색은 단계로 세지 않는다
    private const float MinFont    = 16f;
    private const float FlashMin   = 0.015f;  // v4 착지 섬광(설계 2.5%) — 보이긴 하되
    private const float FlashMax   = 0.04f;   // 화면을 덮지 않는다
    private const int   SparkMin   = 12;
    private static readonly System.Type P = typeof(UI_RefineryPanel);
    private static readonly float[] Shots       = { 3.40f };
    private static readonly float[] RareShots   = { 1.20f, 2.00f, 3.00f, 4.46f, 5.00f };
    private static readonly float[] LegendShots = { 3.95f, 5.00f, 6.40f };
    private static readonly float[] NoShots     = { };

    private static readonly Color Silver = new(0.86f, 0.90f, 1f, 1f);
    private static readonly string[] StageNames = { "은백", "Rare", "Epic", "Legend" };

    [MenuItem("RelicFairy/Debug/10-11 정제소 뽑기 연출 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RefineryGacha] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("정제소 뽑기 연출 촬영 — v4 제단 화면 · 아발론의 밤\n");
        try
        {
            Directory.CreateDirectory("Temp/ui_shots");
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);

            run.FuelBank?.Add(FuelKind.RuneOre, 40);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null) { sb.AppendLine("정제소 패널을 못 열었다"); return; }
            await UniTask.Delay(800, DelayType.Realtime);

            // ① 제단(대기) 화면
            LogIdle(panel, sb);
            await Shot("RefWish_0_idle", sb);

            // ② 실제 정제 — 서비스 경로 한 번. 끝 상태(버튼 · 잠금 풀림) · 빈 곳 누르기 → 제단.
            if (!MemoryAltarService.IsRefinePickUnlocked) await RealSpin(panel, run, sb);
            else sb.AppendLine("실제 정제 생략 — 「정제 두 장」 해금 세이브라 룬 선택 화면으로 넘어가 창이 닫힌다");

            // ③ 등급 3종 강제 · ④ 두 장 · ⑤ 건너뛰기
            await Forced(panel, ItemRarity.Rare,      null,            "R",    RareShots,   sb);
            await Forced(panel, ItemRarity.Epic,      null,            "E",    Shots,       sb);
            await Forced(panel, ItemRarity.Legendary, null,            "L",    LegendShots, sb);
            await Forced(panel, ItemRarity.Epic,      ItemRarity.Rare, "Twin", NoShots,     sb);
            await Skip(panel, sb);
            Call(panel, "ApplyIdleLook");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/refinery_gacha_probe.txt", sb.ToString());
            Debug.Log("[RefineryGacha] 끝\n" + sb);
        }
    }

    // ── 칸 ──

    /// <summary>
    /// 제단 화면 수치(v4) — 전체 화면 제단 묶음 켜짐 · 옛 창 숨김 · [정제하기] · 등급 세 줄(= 서비스 확률) · 비용(= 서비스) ·
    /// 글자 최소 · 넘침 · 겹침. 크기는 캔버스 단위(1920×1080 게임 뷰면 화면 px).
    /// </summary>
    private static void LogIdle(UI_RefineryPanel p, StringBuilder sb)
    {
        var wish  = FindDeep(p.transform, "WishOverlay");
        var idle  = wish != null ? wish.Find("Idle") : null;
        var grp   = idle != null ? idle.GetComponent<CanvasGroup>() : null;
        var spin  = Get(p, "_iSpin") as Image;
        var label = Get(p, "_iSpinLabel") as TMP_Text;
        var cost  = Get(p, "_iSpinCost") as TMP_Text;
        var win   = FindDeep(p.transform, "Window");
        bool windowOff = win == null || !win.gameObject.activeInHierarchy;
        sb.AppendLine($"[정제하기] {Size(spin != null ? spin.rectTransform : null)} · 글 「{label?.text}」 {label?.fontSize:0.0} · 비용 「{cost?.text}」 {cost?.fontSize:0.0} · 옛 창 숨김 {windowOff}");

        var svc = GameRunBootstrapper.Instance?.Run?.Refinery;
        var (r, e, l) = svc != null ? svc.CurrentOdds() : (0f, 0f, 0f);
        float[] want = { r, e, l };
        var odds = new StringBuilder("등급 세 줄:");
        int rows = 0, sum = 0;
        bool same = svc != null;
        for (int tier = 1; tier <= 3; tier++)
        {
            var name = idle != null ? idle.Find($"OddsName{tier}")?.GetComponent<TMP_Text>() : null;
            var pct  = idle != null ? idle.Find($"OddsPct{tier}")?.GetComponent<TMP_Text>() : null;
            var fill = idle != null ? idle.Find($"OddsFill{tier}") as RectTransform : null;
            if (!Visible(name) || !Visible(pct)) continue;
            rows++;
            if (int.TryParse(pct.text.TrimEnd('%'), out int v)) sum += v;
            if (pct.text != Mathf.RoundToInt(want[tier - 1] * 100f) + "%") same = false;
            odds.Append($" {name.text} {pct.text}(막대 {(fill != null ? fill.rect.width : 0f):0})");
        }
        sb.AppendLine(odds.ToString() + $" · 서비스 {r:P0} / {e:P0} / {l:P0}");
        string T(string n) => idle != null ? idle.Find(n)?.GetComponent<TMP_Text>()?.text : null;
        var bigCost = idle != null ? idle.Find("Cost")?.GetComponent<TMP_Text>() : null;
        sb.AppendLine($"오른쪽 열 「{bigCost?.text}」 {bigCost?.fontSize:0.0} · 「{T("NextCost")}」 · 「{T("Mode")}」 · 보유 「{T("Own")}」 · 머리 「{T("Title")}」 「{T("Sub")}」");

        bool idleOn = wish != null && wish.gameObject.activeInHierarchy && grp != null && grp.alpha > 0.99f && grp.blocksRaycasts;
        sb.AppendLine($"CHECK 제단 화면 — 전체 화면 제단 켜짐 · 옛 창 숨김 · [정제하기] 글: {idleOn && windowOff && label != null && label.text == "정제하기"}");
        bool costOk = svc != null && cost != null && bigCost != null
                      && cost.text.EndsWith(svc.CurrentCost.ToString()) && bigCost.text.EndsWith(svc.CurrentCost.ToString());
        sb.AppendLine($"CHECK 제단 화면 — 등급 세 줄 · 합 100±1% · 서비스 확률과 같음 · 비용 = 서비스({svc?.CurrentCost}): {rows == 3 && Mathf.Abs(sum - 100) <= 1 && same && costOk}");
        if (idle == null) return;
        sb.AppendLine(FontCheck("제단 화면", idle));
        sb.AppendLine(OverflowCheck("제단 화면", idle));
        sb.AppendLine(IdleOverlapCheck(idle));
    }

    /// <summary>제단 화면 겹침 — 묶음 바로 아래 글(실제 글자 모양) · 버튼 · 아이콘끼리 4px² 넘게 겹치는 쌍(버튼 안 글은 버튼과 한 몸이라 뺀다).</summary>
    private static string IdleOverlapCheck(Transform idle)
    {
        var items = new List<(string name, Rect r)>();
        for (int i = 0; i < idle.childCount; i++)
        {
            var c = idle.GetChild(i);
            if (!c.gameObject.activeInHierarchy) continue;
            if (c.TryGetComponent<TMP_Text>(out var t)) { AddText(items, c.name, t); continue; }
            if (c.name is "SpinBig" or "Close" or "OwnIcon" or "CostIcon" or "Ore" && c.TryGetComponent<Image>(out var img) && img.enabled)
                AddRect(items, c.name, (RectTransform)c);
        }
        var bad = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        for (int j = i + 1; j < items.Count; j++)
        {
            var a = items[i].r; var b = items[j].r;
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (w > 0f && h > 0f && w * h > 4f) bad.Append($" {items[i].name}×{items[j].name}({w:0}×{h:0})");
        }
        return $"CHECK 제단 화면 겹침 0(요소 {items.Count}){(bad.Length > 0 ? ":" + bad : "")}: {bad.Length == 0}";
    }

    /// <summary>실제 [정제하기] 한 번 — 끝 상태 · 입력 열림 시각 · 빈 곳 누르기(유예 0.4초 뒤) → 제단으로.</summary>
    private static async UniTask RealSpin(UI_RefineryPanel panel, GameRunSession run, StringBuilder sb)
    {
        int ore0 = run.FuelBank?.RuneOre ?? -1;
        Call(panel, "OnSpinClicked");
        float s0 = Time.unscaledTime;
        await UniTask.Delay(100, DelayType.Realtime);
        while (Time.unscaledTime - s0 < 10f && (bool)Get(panel, "_busy")) await UniTask.Yield();
        bool busy    = (bool)Get(panel, "_busy");
        var pending  = Get(panel, "_pendingRune") as RuntimeItemData;
        var place    = FindDeep(panel.transform, "WishOverlay")?.Find("EndButtons/Place");
        bool placeOn = place != null && place.gameObject.activeInHierarchy;
        float open   = (float)Get(panel, "_inputOpenAt") - (float)Get(panel, "_gachaT0");
        float target = OpenTarget(pending != null ? pending.rarity : ItemRarity.Rare);
        sb.AppendLine($"실제 정제: 원석 {ore0} → {run.FuelBank?.RuneOre} · 입력 열림 {open:0.00}초(목표 {target:0.0}) · 놓기 버튼 {placeOn} · " +
                      $"룬 {pending?.displayName} ({pending?.rarity})");
        sb.AppendLine($"CHECK 실제 정제 끝 상태(놓기 버튼 · 잠금 풀림 · 입력 열림 ±{OpenTol}): {!busy && placeOn && pending != null && Mathf.Abs(open - target) <= OpenTol}");

        await UniTask.Delay(500, DelayType.Realtime);   // 유예(0.4초) 뒤
        Call(panel, "OnWishBackdrop");
        await UniTask.Delay(100, DelayType.Realtime);
        var wish = FindDeep(panel.transform, "WishOverlay");
        var idle = wish != null ? wish.Find("Idle")?.GetComponent<CanvasGroup>() : null;
        var card = wish != null ? wish.Find("CardMain") : null;
        bool back = wish != null && wish.gameObject.activeInHierarchy && idle != null && idle.alpha > 0.99f
                    && (card == null || !card.gameObject.activeInHierarchy) && Get(panel, "_pendingRune") == null;
        sb.AppendLine($"CHECK 끝 상태에서 빈 곳 → 제단 화면(공개 걷힘 · 같은 하늘): {back}");

        // 제단 화면에서 빈 곳 — 닫지 않는다(실수 닫힘 방지 · 닫기는 [닫기 · ESC])
        Call(panel, "OnWishBackdrop");
        await UniTask.Delay(300, DelayType.Realtime);
        bool stays = panel != null && panel.gameObject.activeInHierarchy && idle != null && idle.alpha > 0.99f;
        sb.AppendLine($"CHECK 제단 화면에서 빈 곳 → 닫히지 않음: {stays}");
        Call(panel, "ApplyIdleLook");
        await UniTask.Delay(300, DelayType.Realtime);
    }

    /// <summary>등급 하나를 강제로 돌린다 — 연출 함수에 outcome을 직접 준다. 꼬리 색은 매 프레임 실측, 사진은 shots + 끝 1장.</summary>
    private static async UniTask Forced(UI_RefineryPanel panel, ItemRarity rarity, ItemRarity? alt, string tag, float[] shots, StringBuilder sb)
    {
        Call(panel, "ApplyIdleLook");
        await UniTask.Delay(250, DelayType.Realtime);
        var rune    = SampleRune(rarity, null);
        var altRune = alt.HasValue ? SampleRune(alt.Value, rune) : null;
        int top     = Mathf.Max(RarityTier(rarity), alt.HasValue ? RarityTier(alt.Value) : 0);
        sb.AppendLine($"── {tag}: {rarity}{(alt.HasValue ? " + " + alt.Value : "")} · {rune?.displayName} ({rune?.element})" +
                      (altRune != null ? $" / {altRune.displayName} ({altRune.element})" : ""));
        var outcome = new RefineryOutcome { Success = true, Rarity = rarity, Rune = rune, Alt = altRune };
        var task = (UniTask)P.GetMethod("PlayGachaAsync", Inst).Invoke(panel, new object[] { outcome, panel.destroyCancellationToken });
        float t0 = (float)Get(panel, "_gachaT0");
        sb.AppendLine("  " + TailTable(panel, top));

        var tail = Get(panel, "_wTail") as Image;
        var seq = new StringBuilder("은백");
        float depart = -1f, arrive = -1f;
        int prev = 0;
        bool upOnly = true;
        Color final = RarityColorTable.Get(TierRarity(top));
        int si = 0;
        float openAt = -1f, endShotAt = -1f, maxFlash = 0f, gemAng = -999f;
        int maxSparks = 0;
        bool endDone = false;
        var flashImg = Get(panel, "_wFlash") as Image;
        var sparks   = Get(panel, "_wSparks") as Image[];
        while (true)
        {
            float t = Time.unscaledTime - t0;
            // 꼬리 색 실측 — 은백을 떠난 시각 · 등급 색에 닿은 시각 · 단계가 내려가지 않는지.
            // 읽기는 PostLateUpdate(연출 루프가 Update에서 칠한 같은 프레임 값) — 예전엔 한 프레임 앞 색을 읽었다.
            // 단계는 등급 색에 자리 잡은 색만 센다(은백 → 금 전환 중간의 푸른 흰색이 「Epic」으로 잡혀 가짜 단계처럼 보였다).
            if (tail != null && tail.enabled && t < LandAt)
            {
                var c = tail.color;
                if (depart < 0f && Dist(c, Silver) > 0.01f) depart = t;
                if (arrive < 0f && Dist(c, final) < 0.02f) arrive = t;
                int s = SettledStage(c, prev);
                if (s != prev)
                {
                    if (s < prev) upOnly = false;
                    seq.Append($" → {StageNames[s]}@{t:0.00}");
                    prev = s;
                }
            }
            if (flashImg != null && flashImg.enabled) maxFlash = Mathf.Max(maxFlash, flashImg.color.a);
            if (gemAng < -900f && t >= LandAt + 0.05f) gemAng = TopGemAngle(panel, rune);
            if (sparks != null && t > 6.15f && t < 7.2f)
            {
                int n = 0;
                foreach (var s in sparks) if (s != null && s.enabled && s.color.a > 0.05f) n++;
                maxSparks = Mathf.Max(maxSparks, n);
            }
            if (openAt < 0f && task.Status.IsCompleted())
            {
                openAt = t;
                if (!alt.HasValue) Call(panel, "EnterEndState", rune);   // 실제 흐름처럼 입력을 연다(두 장은 룬 선택 화면으로 가므로 없음)
                endShotAt = t + 0.25f;                                    // 버튼이 스미는 0.18초 뒤
            }
            if (si < shots.Length && t >= shots[si])
            {
                string name = $"RefWish_{tag}_{shots[si]:0.00}";
                ScreenCapture.CaptureScreenshot(ShotDir + name + ".png");
                sb.AppendLine($"  사진 {name}.png (실제 {t:0.00}초) · {State(panel)}");
                si++;
            }
            if (!endDone && endShotAt > 0f && t >= endShotAt)
            {
                endDone = true;
                string name = $"RefWish_{tag}_end";
                ScreenCapture.CaptureScreenshot(ShotDir + name + ".png");
                sb.AppendLine($"  사진 {name}.png (실제 {t:0.00}초) · {State(panel)}");
            }
            if (endDone && si >= shots.Length) break;
            if (t > 12f) { sb.AppendLine("  12초 넘김 — 중단"); break; }
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
        }

        float target = OpenTarget(TierRarity(top));
        float endAt  = (float)Get(panel, "_gachaEndAt") - t0;
        sb.AppendLine($"  꼬리 실측: {seq} · 은백을 떠난 시각 {depart:0.000} · 등급 색 도달 {arrive:0.000}");
        float want = CheckAt[Mathf.Clamp(top, 1, 3) - 1];
        sb.AppendLine($"CHECK {tag} 오르는 고비(실측 — 첫 고비 {TintAt}±{TintTol}초 · 끝 단계 {want}±{ArriveTol}초에 도달) · 위로만 · 끝 {StageNames[top]}: " +
                      $"{depart >= 0f && Mathf.Abs(depart - TintAt) <= TintTol && arrive >= 0f && Mathf.Abs(arrive - want) <= ArriveTol && upOnly && prev == top}");
        sb.AppendLine($"CHECK {tag} 착지 때 나온 룬 속성({rune?.element}) 보석이 맨 위(90±{GemTopTol}° — 실측 {gemAng:0.0}°): {Mathf.Abs(Mathf.DeltaAngle(gemAng, 90f)) <= GemTopTol}");
        sb.AppendLine($"  끝 모습 {endAt:0.00}초 · 연출 함수 돌아옴(입력 열림) {openAt:0.00}초 · 목표 {target:0.0}");
        sb.AppendLine($"CHECK {tag} 입력 열림 {target:0.0}±{OpenTol}초: {openAt >= 0f && Mathf.Abs(openAt - target) <= OpenTol}");
        sb.AppendLine($"CHECK {tag} 착지 섬광 {FlashMin}~{FlashMax}(화면을 덮지 않음 — 실측 최대 {maxFlash:0.00}): {maxFlash >= FlashMin && maxFlash <= FlashMax}");
        if (top >= 3) sb.AppendLine($"CHECK {tag} 전설 금빛 불티 {SparkMin}개 이상(실측 최대 {maxSparks}): {maxSparks >= SparkMin}");

        var wish = FindDeep(panel.transform, "WishOverlay");
        LogReveal(panel, wish, tag, top, alt.HasValue, sb);
        await UniTask.Delay(200, DelayType.Realtime);   // 마지막 사진이 저장되게
    }

    /// <summary>끝 모습 — 카드 · ◆ · 버튼 · 전설 빛살, 글자 최소 · 넘침 · 겹침(전체 화면만).</summary>
    private static void LogReveal(UI_RefineryPanel p, Transform wish, string tag, int top, bool twin, StringBuilder sb)
    {
        if (wish == null || !wish.gameObject.activeInHierarchy) { sb.AppendLine($"CHECK {tag} 전체 화면 공개: False (꺼져 있음)"); return; }
        var main = wish.Find("CardMain");
        var alt  = wish.Find("CardAlt");
        bool mainOk = CardShown(main, out string mainS);
        bool altOk  = CardShown(alt, out string altS);
        sb.AppendLine($"  카드 Main {mainS}" + (twin ? $" · Alt {altS}" : ""));
        if (twin)
        {
            float mx = Center(main?.Find("Icon") as RectTransform).x, ax = Center(alt?.Find("Icon") as RectTransform).x;
            sb.AppendLine($"CHECK {tag} 두 장 나란히 공개(높은 쪽 오른쪽): {mainOk && altOk && mx > ax}");
        }
        else
        {
            var place = wish.Find("EndButtons/Place");
            var again = wish.Find("EndButtons/Again");
            string againS = again != null ? again.GetComponentInChildren<TMP_Text>(true)?.text : null;
            bool buttons = place != null && place.gameObject.activeInHierarchy && again != null && again.gameObject.activeInHierarchy;
            sb.AppendLine($"  버튼 [{againS}] [룬판에 놓기] 켜짐 {buttons}");
            sb.AppendLine($"CHECK {tag} 공개 · 끝 버튼: {mainOk && buttons}");
        }
        if (top >= 3)
        {
            var gold = wish.Find("RaysGold")?.GetComponent<Image>();
            var name = main?.Find("Text/Name")?.GetComponent<TMP_Text>();
            bool nameGold = name != null && Dist(name.color, UITheme.Thin.Gold) < 0.05f;
            sb.AppendLine($"CHECK {tag} 전설 — 금빛 빛살 · 이름 금빛: {gold != null && gold.enabled && gold.color.a > 0.3f && nameGold}");
        }
        sb.AppendLine(FontCheck(tag + " 공개", wish));
        sb.AppendLine(OverflowCheck(tag + " 공개", wish));
        sb.AppendLine(OverlapCheck(tag + " 공개", wish, twin));
    }

    /// <summary>건너뛰기 — 전설을 돌리고 0.5초에 「아무 입력」을 넣는다(_skipRequested). 끝 모습까지 걸린 시간 · 섬광 보장을 잰다.</summary>
    private static async UniTask Skip(UI_RefineryPanel panel, StringBuilder sb)
    {
        Call(panel, "ApplyIdleLook");
        await UniTask.Delay(250, DelayType.Realtime);
        var rune    = SampleRune(ItemRarity.Legendary, null);
        var outcome = new RefineryOutcome { Success = true, Rarity = ItemRarity.Legendary, Rune = rune };
        var task = (UniTask)P.GetMethod("PlayGachaAsync", Inst).Invoke(panel, new object[] { outcome, panel.destroyCancellationToken });
        float t0 = (float)Get(panel, "_gachaT0");
        while (Time.unscaledTime - t0 < 0.5f) await UniTask.Yield();

        Set(panel, "_skipRequested", true);
        float ts = Time.unscaledTime;
        float maxFlash = 0f;
        bool shot = false;
        while (!task.Status.IsCompleted() && Time.unscaledTime - ts < 3f)
        {
            if (Get(panel, "_wFlash") is Image flash && flash.enabled) maxFlash = Mathf.Max(maxFlash, flash.color.a);
            if (!shot && Time.unscaledTime - ts >= 0.2f) { ScreenCapture.CaptureScreenshot(ShotDir + "RefWish_skip.png"); shot = true; }
            await UniTask.Yield();
        }
        if (!shot) ScreenCapture.CaptureScreenshot(ShotDir + "RefWish_skip.png");
        float endAt   = (float)Get(panel, "_gachaEndAt");
        float flashAt = (float)Get(panel, "_flashAt");
        float lat     = endAt - ts;
        sb.AppendLine($"── 건너뛰기(전설, 0.5초에 입력): 끝 모습까지 {lat:0.000}초 · 섬광 시작 +{flashAt - ts:0.000}초 · 섬광 최대 알파 {maxFlash:0.00} · " +
                      $"연출 함수 돌아옴 +{Time.unscaledTime - ts:0.000}초 · 사진 RefWish_skip.png(+0.2초)");
        sb.AppendLine($"  {State(panel)}");
        sb.AppendLine($"CHECK 건너뛰기 {SkipMax}초 안(한 프레임 여유 {FrameSlack}): {lat >= 0f && lat <= SkipMax + FrameSlack}");
        sb.AppendLine($"CHECK 건너뛰기에도 등급 색 섬광 한 번: {flashAt >= ts && maxFlash >= FlashMin}");
        await UniTask.Delay(200, DelayType.Realtime);
    }

    // ── 검사 ──

    /// <summary>보이는 글(켜짐 · 글 있음 · 물려받은 알파 포함 0.05 넘음)의 글자 크기 최소 — 16 미만 목록.</summary>
    private static string FontCheck(string what, Transform root)
    {
        float min = float.MaxValue;
        var low = new StringBuilder();
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!Visible(t)) continue;
            n++;
            min = Mathf.Min(min, t.fontSize);
            if (t.fontSize < MinFont - 0.01f) low.Append($" {t.name}「{Short(t.text)}」{t.fontSize:0.0}");
        }
        return $"CHECK {what} 글자 최소 {MinFont}(보이는 글 {n}개 · 최소 {(n > 0 ? min : 0f):0.0}){(low.Length > 0 ? " 미달:" + low : "")}: {low.Length == 0 && n > 0}";
    }

    /// <summary>
    /// 넘침 — 실제 글리프 상자(문자마다 bottomLeft~topRight)가 칸 밖으로 2px 넘게 나간 글.
    /// textBounds는 글리프가 아니라 줄 상자(글꼴 ascender~descender, Noto는 1.45em)라 48px 이름이 64 높이를 다 채우고,
    /// Midline 정렬은 글리프 가운데를 맞추므로 줄 상자가 칸 위아래로 몇 px 밀려 나온다 — 그걸 넘침으로 잘못 셌다(10-14 실측).
    /// </summary>
    private static string OverflowCheck(string what, Transform root)
    {
        var bad = new StringBuilder();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!Visible(t) || !InkBounds(t, out var min, out var max)) continue;
            var r = t.rectTransform.rect;
            if (min.x < r.xMin - 2f || max.x > r.xMax + 2f || min.y < r.yMin - 2f || max.y > r.yMax + 2f)
                bad.Append($" {t.name}「{Short(t.text)}」 글리프 x {min.x:0}~{max.x:0} y {min.y:0}~{max.y:0} > 칸 x {r.xMin:0}~{r.xMax:0} y {r.yMin:0}~{r.yMax:0}");
        }
        return $"CHECK {what} 넘침 0{(bad.Length > 0 ? ":" + bad : "")}: {bad.Length == 0}";
    }

    /// <summary>보이는 문자들의 글리프 상자 합(글 로컬 좌표 = 칸 rect와 같은 피벗 기준).</summary>
    private static bool InkBounds(TMP_Text t, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        var info = t.textInfo;
        if (info == null) return false;
        bool any = false;
        for (int i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
        {
            var ci = info.characterInfo[i];
            if (!ci.isVisible) continue;
            min = Vector2.Min(min, ci.bottomLeft);
            max = Vector2.Max(max, ci.topRight);
            any = true;
        }
        return any;
    }

    /// <summary>겹침 — 공개 카드의 글(실제 글자 모양) · ◆ 줄 · 그림 · 버튼 · 안내끼리 화면 사각형이 4px² 넘게 겹치는 쌍.</summary>
    private static string OverlapCheck(string what, Transform wish, bool twin)
    {
        var items = new List<(string name, Rect r)>();
        foreach (var cardName in twin ? new[] { "CardMain", "CardAlt" } : new[] { "CardMain" })
        {
            var card = wish.Find(cardName);
            if (card == null || !card.gameObject.activeInHierarchy) continue;
            string k = twin ? cardName.Substring(4) + "." : "";
            AddRect(items, k + "Icon", card.Find("Icon") as RectTransform);
            foreach (var n in new[] { "Grade", "Name", "Attr", "Effect" })
                AddText(items, k + n, card.Find("Text/" + n)?.GetComponent<TMP_Text>());
            Rect gems = default;
            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                var g = card.Find($"Text/Gem{i}") as RectTransform;
                if (g == null || !g.gameObject.activeInHierarchy) continue;
                var r = ScreenRect(g);
                gems = any ? Rect.MinMaxRect(Mathf.Min(gems.xMin, r.xMin), Mathf.Min(gems.yMin, r.yMin), Mathf.Max(gems.xMax, r.xMax), Mathf.Max(gems.yMax, r.yMax)) : r;
                any = true;
            }
            if (any) items.Add((k + "◆", gems));
        }
        AddRect(items, "Again", wish.Find("EndButtons/Again") as RectTransform);
        AddRect(items, "Place", wish.Find("EndButtons/Place") as RectTransform);
        AddText(items, "Hint", wish.Find("Hint")?.GetComponent<TMP_Text>());
        AddText(items, "SkipHint", wish.Find("SkipHint")?.GetComponent<TMP_Text>());
        AddText(items, "TwinCaption", wish.Find("TwinCaption")?.GetComponent<TMP_Text>());

        var bad = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        for (int j = i + 1; j < items.Count; j++)
        {
            var a = items[i].r; var b = items[j].r;
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (w > 0f && h > 0f && w * h > 4f) bad.Append($" {items[i].name}×{items[j].name}({w:0}×{h:0})");
        }
        return $"CHECK {what} 겹침 0(요소 {items.Count}){(bad.Length > 0 ? ":" + bad : "")}: {bad.Length == 0}";
    }

    private static void AddRect(List<(string, Rect)> items, string name, RectTransform rt)
    {
        if (rt != null && rt.gameObject.activeInHierarchy) items.Add((name, ScreenRect(rt)));
    }

    private static void AddText(List<(string, Rect)> items, string name, TMP_Text t)
    {
        if (!Visible(t)) return;
        var b = t.textBounds;
        if (b.size.sqrMagnitude < 0.01f) return;
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        items.Add((name, Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y))));
    }

    private static bool Visible(TMP_Text t)
    {
        if (t == null || !t.gameObject.activeInHierarchy || !t.enabled || string.IsNullOrWhiteSpace(t.text)) return false;
        return t.color.a * t.canvasRenderer.GetInheritedAlpha() > 0.05f;
    }

    private static bool CardShown(Transform card, out string s)
    {
        s = "-";
        if (card == null || !card.gameObject.activeInHierarchy) return false;
        var icon = card.Find("Icon")?.GetComponent<Image>();
        var name = card.Find("Text/Name")?.GetComponent<TMP_Text>();
        var grp  = card.Find("Text")?.GetComponent<CanvasGroup>();
        int lit = 0, gems = 0;
        for (int i = 0; i < 3; i++)
        {
            var g = card.Find($"Text/Gem{i}");
            if (g == null || !g.gameObject.activeInHierarchy) continue;
            gems++;
            if (g.TryGetComponent<Image>(out var gi) && gi.color.a > 0.9f) lit++;
        }
        bool iconOk = icon != null && icon.enabled && icon.color.a > 0.99f && icon.color.r > 0.99f;
        s = $"그림 {(icon != null && icon.sprite != null ? icon.sprite.name : "-")} {Size(icon != null ? icon.rectTransform : null)} 밝기 {icon?.color.r:0.00} · " +
            $"이름 「{name?.text}」 {name?.fontSize:0.0} · 글 알파 {grp?.alpha:0.00} · ◆ {lit}/{gems}";
        return iconOk && grp != null && grp.alpha > 0.99f && gems > 0 && lit == gems;
    }

    // ── 기록 ──

    /// <summary>꼬리 색 함수(TailColor)를 0.70~1.80초 0.01초 간격으로 읽어 전환 시각 · 순서를 본다 — 은백 → 등급 색이 0.9초 · 위로만.</summary>
    private static string TailTable(UI_RefineryPanel p, int tier)
    {
        var m = P.GetMethod("TailColor", Inst);
        if (m == null) return "꼬리 색 함수 없음";
        int prev = 0;
        float start = -1f;
        bool upOnly = true;
        var seq = new StringBuilder("은백");
        for (int i = 0; i <= 220; i++)
        {
            float t = 2.20f + i * 0.01f;
            var c = (Color)m.Invoke(p, new object[] { t });
            if (start < 0f && Dist(c, Silver) > 0.01f) start = t;
            int s = StageIndex(c, start >= 0f);   // 은백을 떠난 뒤엔 등급 색끼리만 견준다(전환 중간색이 은백으로 잡히지 않게)
            if (s == prev) continue;
            if (s < prev) upOnly = false;
            seq.Append($" → {StageNames[s]}@{t:0.00}");
            prev = s;
        }
        bool ok = start >= 0f && Mathf.Abs(start - TintAt) <= TintTol && upOnly && prev == tier;
        return $"꼬리 색 함수: {seq} · 은백을 떠난 시각 {start:0.00}초 · 위로만 {upOnly}\n  CHECK 꼬리 색 함수 {TintAt}±{TintTol}초 전환 · 위로만 · 끝 {StageNames[Mathf.Clamp(tier, 0, 3)]}: {ok}";
    }

    private static string State(UI_RefineryPanel p)
    {
        var wish  = FindDeep(p.transform, "WishOverlay");
        if (wish == null || !wish.gameObject.activeInHierarchy) return "전체 화면 꺼짐";
        var night = wish.Find("Night")?.GetComponent<CanvasGroup>();
        var idle  = wish.Find("Idle")?.GetComponent<CanvasGroup>();
        var tail  = Get(p, "_wTail") as Image;
        var flash = Get(p, "_wFlash") as Image;
        var rays  = wish.Find("Rays")?.GetComponent<Image>();
        var icon  = wish.Find("CardMain/Icon")?.GetComponent<Image>();
        string tailS = tail != null && tail.enabled ? $"{StageNames[StageIndex(tail.color)]}#{ColorUtility.ToHtmlStringRGB(tail.color)}" : "-";
        string iconS = icon != null && icon.gameObject.activeInHierarchy ? $"밝기 {icon.color.r:0.00} 알파 {icon.color.a:0.00}" : "-";
        return $"제단 {(idle != null ? idle.alpha : 0f):0.00} · 밤하늘 {(night != null ? night.alpha : 0f):0.00} · 꼬리 {tailS} · 섬광 {(flash != null && flash.enabled ? flash.color.a : 0f):0.00} · " +
               $"빛살 {(rays != null && rays.enabled ? rays.color.a : 0f):0.00} · 룬 {iconS}";
    }

    private static float OpenTarget(ItemRarity r) => r == ItemRarity.Legendary ? 7.4f : r == ItemRarity.Epic ? 6.4f : 6.1f;

    /// <summary>나온 룬 속성 보석의 각(제단 중심 기준 · 도) — 룰렛이 멎으면 90°(맨 위). 속성이 없으면 90 그대로(검사 면제).</summary>
    private static float TopGemAngle(UI_RefineryPanel p, RuntimeItemData rune)
    {
        var gems = Get(p, "_iGems") as RectTransform[];
        int idx = -1;
        var order = ElementDef.Order;
        for (int i = 0; rune != null && i < order.Count; i++) if (order[i] == rune.element) idx = i;
        if (gems == null || idx < 0 || idx >= gems.Length) return 90f;
        var a = gems[idx].anchoredPosition;
        return Mathf.Atan2(a.y - AltarCy, a.x) * Mathf.Rad2Deg;
    }

    private static ItemRarity TierRarity(int tier) => tier >= 3 ? ItemRarity.Legendary : tier == 2 ? ItemRarity.Epic : ItemRarity.Rare;

    private static int RarityTier(ItemRarity r) => r == ItemRarity.Legendary ? 3 : r == ItemRarity.Epic ? 2 : 1;

    private static float Dist(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

    /// <summary>자리 잡은 단계 — 은백이거나 등급 색에 <see cref="SettleTol"/> 안으로 닿았을 때만 바뀐다(전환 중간은 앞 단계 유지).</summary>
    private static int SettledStage(Color c, int prev)
    {
        if (Dist(c, Silver) <= 0.01f) return 0;
        for (int i = 1; i <= 3; i++)
            if (Dist(c, RarityColorTable.Get(TierRarity(i))) < SettleTol) return i;
        return prev;
    }

    private static int StageIndex(Color c, bool gradesOnly = false)
    {
        var refs = new[] { Silver, RarityColorTable.Get(ItemRarity.Rare), RarityColorTable.Get(ItemRarity.Epic), RarityColorTable.Get(ItemRarity.Legendary) };
        int best = 0;
        float bd = float.MaxValue;
        for (int i = gradesOnly ? 1 : 0; i < refs.Length; i++)
        {
            float d = (c.r - refs[i].r) * (c.r - refs[i].r) + (c.g - refs[i].g) * (c.g - refs[i].g) + (c.b - refs[i].b) * (c.b - refs[i].b);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);   // 화면 공간 오버레이 = 화면 px
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static Vector2 Center(RectTransform rt) => rt == null ? Vector2.zero : ScreenRect(rt).center;

    private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length <= 12 ? s.Replace("\n", " ") : s.Substring(0, 12).Replace("\n", " ") + "…";

    private static string Size(RectTransform rt) => rt == null ? "-" : $"{rt.rect.width:0}×{rt.rect.height:0}";

    private static RuntimeItemData SampleRune(ItemRarity r, RuntimeItemData not)
    {
        var list = ItemSORegistry.GetByRarity(r);
        foreach (var so in list)
        {
            if (so == null || (not != null && so.itemId == not.itemId)) continue;
            var d = RuntimeItemData.FromSO(so);
            if (d != null && !string.IsNullOrEmpty(d.element)) return d;
        }
        foreach (var so in list)
        {
            var d = RuntimeItemData.FromSO(so);
            if (d != null) return d;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static async UniTask Shot(string name, StringBuilder sb)
    {
        ScreenCapture.CaptureScreenshot(ShotDir + name + ".png");
        sb.AppendLine($"사진 {name}.png");
        await UniTask.Delay(250, DelayType.Realtime);
    }

    private static object Get(object o, string field) => P.GetField(field, Inst)?.GetValue(o);
    private static void Set(object o, string field, object v) => P.GetField(field, Inst)?.SetValue(o, v);
    private static void Call(object o, string method) => P.GetMethod(method, Inst)?.Invoke(o, null);
    private static void Call(object o, string method, RuntimeItemData arg) => P.GetMethod(method, Inst)?.Invoke(o, new object[] { arg });
}
#endif
