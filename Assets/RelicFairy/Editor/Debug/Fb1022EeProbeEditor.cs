#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// 10-22 ee 몫 묶음 실측(챕터 시작 대기방) — C1 · C2 서약 제단 「[F] 서약 조립」 화면 크기 · 화살표 · C3 HUD 「재련 n」 ·
/// C4 금화 · 정수 밀어내기 · C5 외곽선 · 일반 등급 기둥 · C9 상점 원석 환전 · C12 서약 지속 피해 출처 · 무기 빌드 줄 ·
/// C15 모르드레드 초상(타락 전 · 뒤) · C13 HUD 데이터를 떼면 재화 칸이 비워지는가(맨 끝 — HUD를 뗀다).
/// 사진: Temp/ui_shots/EE22_*.png · 결과: Temp/fb1022_ee.txt · 로그 「[Fb1022Ee] 끝」.
/// </summary>
public static class Fb1022EeProbeEditor
{
    private const string Tag     = "[Fb1022Ee]";
    private const string OutTxt  = "Temp/fb1022_ee.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static int s_pass, s_fail;

    [MenuItem("RelicFairy/Debug/10-22 ee 묶음 실측 — 서약 안내 · 금화 · 환전 · 초상 · HUD (런 중, 챕터 시작방)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_pass = s_fail = 0;
        var sb = new StringBuilder("10-22 ee 묶음 실측\n");
        Directory.CreateDirectory(ShotDir);
        var run = GameRunBootstrapper.Instance.Run;
        var p   = run.Player;
        var ct  = p.GetCancellationTokenOnDestroy();
        GameObject shopGo = null, rewardGo = null;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            var cam = Camera.main;

            // 챕터 시작 대사(멀린 · ???)가 시간을 멈춘다 — 1차 실측이 timeScale 0에서 돌아 착지 · 물리 · 예고가 멈췄다. 대사를 다 넘기고 시간이 돌 때까지
            for (int i = 0; i < 8 && (Time.timeScale < 0.5f || UI_BossBark.IsShowing); i++)
            {
                EditorApplication.ExecuteMenuItem("RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)");
                await Wait(2.5f, ct);
            }
            sb.AppendLine($"시작 — timeScale {Time.timeScale:0.##} · 자막 {UI_BossBark.IsShowing} · 시간 잡은 쪽 {TimeScaleArbiter.DescribeHolders()}");
            if (Time.timeScale < 0.5f) { sb.AppendLine("시간이 멈춰 있다 — 실측 중단"); s_fail++; return; }

            // ── C3 HUD 무기 칸 「재련 n」 ──
            var combat = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);
            var lv = combat != null ? typeof(CombatPanelView).GetField("_weaponLevel", Inst)?.GetValue(combat) as TMP_Text[] : null;
            bool lvHidden = lv != null && lv.All(t => t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text));
            Check(sb, $"C3 HUD 무기 칸 「재련 n」 안 보임 ({(lv == null ? "칸 없음" : string.Join(" · ", lv.Select(t => t == null ? "-" : $"'{t.text}' {t.gameObject.activeInHierarchy}")))})", lvHidden);
            await Shot("EE22_1_hud");

            // ── C1 · C2 서약 제단 — 다가가 「[F] 서약 조립」 화면 크기 · 화살표 ──
            var altar = Object.FindFirstObjectByType<WorldCovenantAltar>(FindObjectsInactive.Include);
            if (altar == null) sb.AppendLine("C1 · C2 서약 제단 — 이 방에 없음(건너뜀)");
            else
            {
                Vector3 front = altar.transform.position + (p.transform.position - altar.transform.position).WithY(0f).normalized * 1.3f;
                Move(p, front);
                await Wait(1.6f, ct);
                var prompt = typeof(WorldCovenantAltar).GetField("_promptText", Inst)?.GetValue(altar) as TextMeshPro;
                var arrow  = typeof(WorldCovenantAltar).GetField("_guideArrow", Inst)?.GetValue(altar) as Component;
                bool shown = prompt != null && prompt.gameObject.activeInHierarchy;
                float px = shown ? LinePx1080(prompt, cam) : -1f;
                Check(sb, $"C2 「[F] 서약 조립」 떠 있음 · 1행 {px:0.0} px(1080 기준) ≤ 36 · 글꼴 {prompt?.font?.name}", shown && px > 0f && px <= 36f);
                Check(sb, $"C2 얇은 글꼴(DNF 아님) — {prompt?.font?.name}", prompt != null && prompt.font != null && !prompt.font.name.Contains("DNF"));
                Check(sb, $"C1 안내가 뜬 동안 머리 위 화살표 숨김 — {(arrow == null ? "화살표 없음(이미 서약)" : arrow.gameObject.activeInHierarchy ? "보임" : "숨김")}", arrow == null || !arrow.gameObject.activeInHierarchy);
                Check(sb, $"C11 상호작용 안내 수 {WorldTextScreenCap.ActivePrompts} ≥ 1(출구 배지 옅게)", WorldTextScreenCap.ActivePrompts >= 1);
                var compass = Object.FindFirstObjectByType<ExitCompassHud>(FindObjectsInactive.Include);
                var cg = compass != null ? typeof(ExitCompassHud).GetField("_group", Inst)?.GetValue(compass) as CanvasGroup : null;
                if (cg == null) sb.AppendLine("C11 출구 배지 — 이 방에 없음(건너뜀)");
                else Check(sb, $"C11 안내가 뜬 동안 출구 배지 알파 {cg.alpha:0.00} ≤ 0.3", cg.alpha <= 0.3f);
                await Shot("EE22_2_altar_near");
                Move(p, altar.transform.position + (front - altar.transform.position).normalized * 4.5f);
                await Wait(1.2f, ct);
                Check(sb, $"C1 · C11 멀어지면 안내 닫힘 · 안내 수 {WorldTextScreenCap.ActivePrompts}", prompt == null || !prompt.gameObject.activeInHierarchy);
                var compass2 = Object.FindFirstObjectByType<ExitCompassHud>(FindObjectsInactive.Include);
                var cg2 = compass2 != null ? typeof(ExitCompassHud).GetField("_group", Inst)?.GetValue(compass2) as CanvasGroup : null;
                if (cg2 != null) Check(sb, $"C11 안내가 닫히면 출구 배지 알파 {cg2.alpha:0.00} ≥ 0.9", cg2.alpha >= 0.9f);
                await Shot("EE22_3_altar_far");
            }

            // ── C4 · C5 금화 · 정수 — 외곽선 · 보상 자리 밀어내기 ──
            Vector3 drop = FindFloor(p.transform.position, 6.5f);
            GoldCoinPickup.SpawnDrops(drop, 8, 1);
            EssenceShardPickup.SpawnDrops(drop, 45);
            await Wait(0.9f, ct);
            var coins  = Object.FindObjectsByType<GoldCoinPickup>(FindObjectsSortMode.None);
            var shards = Object.FindObjectsByType<EssenceShardPickup>(FindObjectsSortMode.None);
            int outlined = coins.Count(c => HasOutline(c.transform)) + shards.Count(s => HasOutline(s.transform));
            Check(sb, $"C5 내려앉은 금화 {coins.Length} · 정수 {shards.Length} 중 외곽선 켜짐 {outlined}", coins.Length > 0 && outlined == coins.Length + shards.Length);
            float before = MinFlat(drop, coins.Select(c => c.transform.position).Concat(shards.Select(s => s.transform.position)));
            await Shot("EE22_4_coins_before");
            GoldCoinPickup.PushAwayFrom(drop, 1.6f);
            EssenceShardPickup.PushAwayFrom(drop, 1.6f);
            await Wait(0.9f, ct);
            float after = MinFlat(drop, coins.Where(c => c != null).Select(c => c.transform.position).Concat(shards.Where(s => s != null).Select(s => s.transform.position)));
            Check(sb, $"C4 보상 자리 둘레 비우기 — 가장 가까운 재화 {before:0.00} m → {after:0.00} m(≥ 1.5)", after >= 1.5f);

            // ── C5 일반 등급 보상 — 작은 기둥이 남는가 ──
            float floorY = drop.y;
            Vector3 spot = drop + Vector3.up * 0.6f;
            var aura = await RewardObjectPresenter.ForetellAsync(spot, floorY, ItemRarity.Common, ct);
            rewardGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rewardGo.name = "~EE22_RewardStandIn";
            rewardGo.transform.position = spot;
            rewardGo.transform.localScale = Vector3.one * 0.5f;
            Object.Destroy(rewardGo.GetComponent<Collider>());
            RewardObjectPresenter.Arrive(rewardGo, aura, floorY, ItemRarity.Common);
            await Wait(1.6f, ct);
            var loops = aura != null ? typeof(RewardAura).GetField("_loops", Inst)?.GetValue(aura) as System.Collections.IList : null;
            Check(sb, $"C5 일반 보상 — 놓인 뒤 남은 빛 {loops?.Count ?? -1}개(등급 기둥 1)", loops != null && loops.Count == 1);
            await Shot("EE22_5_coins_after_reward_common");
            Object.Destroy(rewardGo); rewardGo = null;

            // ── C12 서약 출처 · 무기 빌드 줄 ──
            DamageSource rs;
            using (DamageSourceScope.Enter(DamageSource.Covenant)) rs = DamageSourceScope.Resolve(DamageKind.Dot);
            Check(sb, $"C12 서약 범위의 지속 피해 출처 = {rs}", rs == DamageSource.Covenant);
            var w   = new RunResultSnapshot.WeaponLine { Name = "무형검", Enhance = 0, Build = "튕기는 칼끝 II · 검기 I" };
            var sub = new RunResultSnapshot.WeaponLine { Name = "석궁", Ranged = true };
            string wl = RunResultPresenter.WeaponSub(w, sub), sl = RunResultPresenter.SubWeaponLine(sub);
            Check(sb, $"C12 무기 줄 「{wl}」 · 보조 줄 「{sl}」", wl.Contains("튕기는 칼끝") && sl.StartsWith("보조 석궁") && sl.Contains("파츠 없음"));

            // ── C9 상점 원석 환전 ──
            shopGo = new GameObject("~EE22_Shop");
            var sc = shopGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Inst)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", Inst)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(7)));
            run.FuelBank.Add(FuelKind.RuneOre, 25 - run.FuelBank.RuneOre);
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) sb.AppendLine("C9 상점 — 로드 실패");
            else
            {
                shop.Bind(sc);
                await Wait(1.2f, ct);
                var oreText  = typeof(UI_ShopPanel).GetField("_oreText", Inst)?.GetValue(shop) as TMP_Text;
                var stepBtn  = typeof(UI_ShopPanel).GetField("_exStepBtn", Inst)?.GetValue(shop) as Button;
                var allBtn   = typeof(UI_ShopPanel).GetField("_exAllBtn", Inst)?.GetValue(shop) as Button;
                var allLabel = typeof(UI_ShopPanel).GetField("_exAllLabel", Inst)?.GetValue(shop) as TMP_Text;
                Check(sb, $"C9 환전 줄 — 원석 「{oreText?.text}」 · 전부 「{allLabel?.text}」 · 10개 버튼 {stepBtn?.interactable}", oreText != null && oreText.text.Contains("25") && stepBtn != null && stepBtn.interactable);
                await Shot("EE22_6_shop_exchange");
                int g0 = run.PlayerState.TempGold;
                bool ok = sc.TryExchangeOre(ShopRoomController.ExchangeStep);
                Check(sb, $"C9 10개 환전 → 골드 +{run.PlayerState.TempGold - g0}(30) · 원석 {run.FuelBank.RuneOre}(15)", ok && run.PlayerState.TempGold - g0 == 30 && run.FuelBank.RuneOre == 15);
                ok = sc.TryExchangeOre(run.FuelBank.RuneOre);
                await Wait(0.5f, ct);
                Check(sb, $"C9 전부 환전 → 원석 {run.FuelBank.RuneOre} · 버튼 꺼짐 {!(stepBtn?.interactable ?? true)} · {!(allBtn?.interactable ?? true)}", ok && run.FuelBank.RuneOre == 0 && stepBtn != null && !stepBtn.interactable && allBtn != null && !allBtn.interactable);
                Check(sb, "C9 원석이 없으면 환전 거절", !sc.TryExchangeOre(1));
                await Shot("EE22_7_shop_after");
                Managers.UI.CloseAllPopupUI();
                await Wait(0.4f, ct);
            }

            // ── C15 모르드레드 초상 — 타락 전 · 뒤 모두 보스 초상(사용자 10-22) ──
            bool cine = UI_BossBark.CinematicLayout;
            foreach (bool corrupted in new[] { false, true })
            {
                UI_BossBark.Show(corrupted ? "검을 건네던 그 손으로, 이제 너를 베게 될 것이다." : "아서의 이름으로, 그대를 원탁의 기사로 서임한다.", BossBarkType.Bark, DialogueSpeaker.Mordred);
                await Wait(1.6f, ct);
                var bark = UI_BossBark.Instance;
                var img  = bark != null ? typeof(UI_BossBark).GetField("_portraitImage", Inst)?.GetValue(bark) as Image : null;
                const string want = "Portrait_Boss_Mordred";
                bool vis = img != null && img.isActiveAndEnabled && img.sprite != null;
                Check(sb, $"C15 모르드레드({(corrupted ? "타락 뒤" : "타락 전")}) 초상 「{(vis ? img.sprite.name : "없음")}」 = {want}", vis && img.sprite.name == want);
                await Shot(corrupted ? "EE22_9_mordred_corrupted" : "EE22_8_mordred_knight");
                UI_BossBark.Dismiss(0f);
                await Wait(0.8f, ct);
            }
            UI_BossBark.CinematicLayout = cine;

            // ── C13 HUD를 떼면 재화 칸이 비워지는가(맨 끝) ──
            run.PlayerState.AddTempGold(5);
            await Wait(0.4f, ct);
            var hud  = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
            var view = Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
            var gold = view != null ? typeof(HudView).GetField("goldText", Inst)?.GetValue(view) as TMP_Text : null;
            string g1 = gold?.text;
            hud?.Dispose();
            await Wait(0.3f, ct);
            Check(sb, $"C13 런 데이터를 떼면 골드 칸 「{g1}」 → 「{gold?.text}」 · 보임 {gold != null && gold.gameObject.activeInHierarchy}", gold != null && gold.text == "0");
            await Shot("EE22_10_hud_after_dispose");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); s_fail++; }
        finally
        {
            if (rewardGo != null) Object.Destroy(rewardGo);
            if (shopGo != null) Object.Destroy(shopGo);
            sb.Insert(0, $"통과 {s_pass} · 실패 {s_fail}\n");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝 — 통과 {s_pass} · 실패 {s_fail}\n{sb}");
        }
    }

    private static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }

    private static void Move(PlayerController p, Vector3 to)
    {
        if (NavMesh.SamplePosition(to, out var hit, 2f, NavMesh.AllAreas)) to = hit.position;
        p.transform.position = to;   // 플레이어는 Rigidbody — 몸 위치도 같이(1차 실측에서 transform만 옮겨 제자리였다)
        if (p.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = to;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    /// <summary>플레이어에서 dist 떨어진 걸을 수 있는 바닥 — 여덟 방향 중 처음 닿는 곳(자석 범위 3 m 밖).</summary>
    private static Vector3 FindFloor(Vector3 from, float dist)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (NavMesh.SamplePosition(from + dir * dist, out var hit, 1f, NavMesh.AllAreas)
                && (hit.position - from).WithY(0f).magnitude > dist - 1f)
                return hit.position;
        }
        return from + Vector3.forward * dist;
    }

    private static bool HasOutline(Transform t)
    {
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            if (c.name == "~Outline") return c.gameObject.activeInHierarchy;
        return false;
    }

    private static float MinFlat(Vector3 center, System.Collections.Generic.IEnumerable<Vector3> pts)
    {
        float m = float.MaxValue;
        foreach (var q in pts) m = Mathf.Min(m, (q - center).WithY(0f).magnitude);
        return m == float.MaxValue ? -1f : m;
    }

    /// <summary>글 한 줄의 화면 높이(1080 기준 px) — 메시 경계 위아래를 화면에 비춘다.</summary>
    private static float LinePx1080(TMP_Text t, Camera cam)
    {
        if (t == null || cam == null) return -1f;
        t.ForceMeshUpdate();
        var b = t.textBounds;
        int lines = Mathf.Max(1, t.textInfo.lineCount);
        Vector3 a = cam.WorldToScreenPoint(t.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f)));
        Vector3 c = cam.WorldToScreenPoint(t.transform.TransformPoint(new Vector3(b.center.x, b.max.y, 0f)));
        return Mathf.Abs(c.y - a.y) / lines * 1080f / Screen.height;
    }

    private static void Check(StringBuilder sb, string what, bool ok)
    {
        if (ok) s_pass++; else s_fail++;
        sb.AppendLine($"[{(ok ? "O" : "X")}] {what}");
    }

    private static UniTask Wait(float s, System.Threading.CancellationToken ct) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);

    private static async UniTask Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
