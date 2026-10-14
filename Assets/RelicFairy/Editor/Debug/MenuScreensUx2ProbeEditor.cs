#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 10-11 상점 · 원거리 장비 선택(모루) · 런 결과 · ESC · 설정 · 로딩 · 세이브 슬롯 위계 · 가독성 개편 실측 — 테스트 허브에서 플레이 중에 돌린다.
/// 화면당 1장, 모두 7장: Temp/ui_shots/UX2_1_Shop · 2_Forge · 3_Result · 4_Esc · 5_Settings · 6_Loading · 7_SaveSlot.png.
/// 결과 Temp/ux2_menus_probe.txt · 로그 「[UX2Menus] 끝」.
///
/// <para>런 결과는 Light(300) 글자가 실제로 그려졌는지 <b>화면 픽셀</b>로 센다(10-08 04:49 캡처에서 제목 · 시간 · 큰 숫자 7곳이 빈칸).
/// Light가 비고 Regular(대조)가 보이면 「재현」 — 이어서 Light 글꼴의 동적 글리프를 비우고 다시 세어,
/// 다시 보이면 원인은 에디터에서 글리프 표와 아틀라스 그림이 어긋난 것(동적 글꼴 데이터)이다. 사진은 남기지 않고 수치만 적는다.</para>
///
/// <para>마지막(세이브 슬롯)은 로비 씬으로 넘어간다 — 끝나면 플레이를 멈춘다. 런이 있으면 상점 골드를 보이려고 임시 골드 120을 더한다.</para>
/// </summary>
public static class MenuScreensUx2ProbeEditor
{
    private const string Tag     = "[UX2Menus]";
    private const string OutTxt  = "Temp/ux2_menus_probe.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const float  LitLum  = 0.35f;   // 어두운 바탕(#0C0C0C) 위 밝은 글자 픽셀 문턱
    private const System.Reflection.BindingFlags Any =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.Public   | System.Reflection.BindingFlags.NonPublic;

    private static TestHubLauncher s_hiddenHub;

    [MenuItem("RelicFairy/Debug/10-11 상점·모루·결과·메뉴 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-11 상점 · 모루 · 결과 · 메뉴 실측\n");
        Directory.CreateDirectory(ShotDir);
        HideTestHubGui();
        try
        {
            await ShopAsync(sb);
            await ForgeAsync(sb);
            await ResultAsync(sb);
            await EscAsync(sb);
            await SettingsAsync(sb);
            await LoadingAsync(sb);
            RestoreTestHubGui();
            await SaveSlotAsync(sb);   // 로비로 넘어간다 — 맨 끝
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RestoreTestHubGui();
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── ① 상점 ──────────────────────────────────────────────
    private static async UniTask ShopAsync(StringBuilder sb)
    {
        sb.AppendLine("[1 상점]");
        GameObject ctrlGo = null;
        try
        {
            var run = GameRunBootstrapper.Instance?.Run;
            run?.PlayerState?.AddTempGold(120);
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) { sb.AppendLine("  상점 팝업 로드 실패"); return; }
            ctrlGo = new GameObject("~UX2ProbeShop");
            var sc = ctrlGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Any)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", Any)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(7)));
            typeof(ShopRoomController).GetField("_rerollEnabled", Any)?.SetValue(sc, true);
            typeof(ShopRoomController).GetField("_rerollCost", Any)?.SetValue(sc, 10);
            shop.Bind(sc);
            await UniTask.Delay(900, ignoreTimeScale: true);   // 판 · 카드 진열 연출(약 0.6초)이 끝난 뒤

            // 룬 카드를 고른다 — 고른 테 · 등급 태그 · 상세 열이 한 장에 담긴다(룬이 없으면 0번)
            int pick = 0;
            for (int i = 0; i < sc.Products.Count; i++)
                if (sc.Products[i].Rune != null) { pick = i; break; }
            typeof(UI_ShopPanel).GetMethod("Select", Any)?.Invoke(shop, new object[] { pick });
            await UniTask.Delay(300, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_1_Shop");
            if (tex != null) Object.Destroy(tex);

            var card = shop.transform.Find($"Window/Card{pick}");
            var edge = card != null ? card.Find("SelEdge") : null;
            sb.AppendLine($"  CHECK 고른 카드({pick}) 2px 금 테: {edge != null && edge.gameObject.activeInHierarchy}" +
                          (edge != null && edge.TryGetComponent<CanvasGroup>(out var eg) ? $" (알파 {eg.alpha:0.00})" : ""));
            DescribeText(sb, card, "Name", "카드 이름(목표 Noto 500 · 20)");
            DescribeText(sb, card, "Price", "카드 가격(목표 Noto 500 · 20 · 금)");
            DescribeText(sb, card, "Effect", "카드 효과(목표 Noto 400 · 18 · 한 줄)");
            DescribeText(sb, card?.Find("BadgeBg"), "BadgeText", "범주 태그(목표 15 · 500)");
            DescribeText(sb, card?.Find("Tag0"), "Label", "등급 태그(목표 17 · 영문)");
            var glow = card != null ? card.Find("IconGlow")?.GetComponent<Image>() : null;
            sb.AppendLine($"  CHECK 카드 등급빛 꺼짐: {glow == null || glow.color.a < 0.01f}");
            StyleAudit(sb, shop.transform, allowDnf: "Name");
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (ctrlGo != null) Object.Destroy(ctrlGo);
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    // ── ② 원거리 장비 선택(모루) ─────────────────────────────
    private static async UniTask ForgeAsync(StringBuilder sb)
    {
        sb.AppendLine("[2 원거리 장비 선택]");
        try
        {
            var weapons = RangedWeapons();
            if (weapons.Count == 0) { sb.AppendLine("  원거리 무기 에셋을 못 찾음"); return; }
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>();
            if (popup == null) { sb.AppendLine("  팝업 로드 실패"); return; }
            var list = new List<UI_RangedForgePopup.Entry> { new() { Weapon = weapons[0] } };
            if (weapons.Count > 1)
                list.Add(new UI_RangedForgePopup.Entry { Weapon = weapons[1], Locked = true, LockReason = "기억의 제단 — 1,200 ◆" });
            popup.Setup(list);
            await UniTask.Delay(500, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_2_Forge");
            if (tex != null) Object.Destroy(tex);

            var stats = typeof(UI_RangedForgePopup).GetField("_stats", Any)?.GetValue(popup) as TMP_Text;
            if (stats != null)
                sb.AppendLine($"  CHECK 수치표 라벨 왼쪽 · 값 오른쪽: {stats.text.Contains("<align=right>")} · 표 폭 {stats.rectTransform.rect.width - stats.margin.x - stats.margin.z:0}(목표 360) · 값 {stats.fontSize:0}");
            var count = popup.transform.Find("Panel/Dots/Count")?.GetComponent<TMP_Text>();
            sb.AppendLine($"  CHECK 「1 / N」: {(count != null ? count.text : "없음")}");
            var dot = popup.transform.Find("Panel/Dots/Dot") as RectTransform;
            if (dot != null) sb.AppendLine($"  점 크기 {dot.rect.width:0}×{dot.rect.height:0}(목표 14)");
            int blue = 0;
            foreach (var t in popup.GetComponentsInChildren<TMP_Text>(false))
                if (Bluish(t.color) || t.text.Contains("#8FC7FF")) { blue++; sb.AppendLine($"  파랑 글자 남음: {t.name}"); }
            sb.AppendLine($"  CHECK 파랑 계열 글자 0: {blue == 0}");
            StyleAudit(sb, popup.transform, allowDnf: "Title");
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    // ── ③ 런 결과 + Light 글자 누락 재현 ─────────────────────
    private static async UniTask ResultAsync(StringBuilder sb)
    {
        sb.AppendLine("[3 런 결과]");
        var snap = typeof(RunResultProbeEditor).GetMethod("Full", Any)?.Invoke(null, null) as RunResultSnapshot;
        if (snap == null) { sb.AppendLine("  가짜 사본을 못 만듦(RunResultProbeEditor.Full)"); return; }
        var task = RunResultView.ShowAsync(snap, default);
        Texture2D tex = null;
        try
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 2.0f) await UniTask.Yield();   // 등장 끝(1.7초) 뒤
            tex = await ShotAsync("UX2_3_Result");
            var view = RunResultView.Active;
            if (view == null || tex == null) { sb.AppendLine("  결과창이 안 떴거나 캡처 실패"); return; }

            var light = UITheme.Font(UIWeight.Light);
            sb.AppendLine($"  Light 글꼴: {(light != null ? light.name : "null")} · 아틀라스 {(light != null && light.atlasTexture != null ? $"{light.atlasTexture.width}×{light.atlasTexture.height}" : "-")}" +
                          $" · 글리프 {(light != null ? light.glyphTable.Count : 0)} · 문자 {(light != null ? light.characterTable.Count : 0)}");
            var lightNames   = new[] { "Title", "Time", "Essence", "Big0", "CVal1" };
            var controlNames = new[] { "Subtitle", "EssenceName", "WeaponName" };
            int lightLit = 0, controlLit = 0;
            foreach (var n in lightNames)   lightLit   += LitReport(sb, tex, Find(view.transform, n), "Light");
            foreach (var n in controlNames) controlLit += LitReport(sb, tex, Find(view.transform, n), "대조");
            bool repro = lightLit < 60 && controlLit > 300;
            sb.AppendLine($"  CHECK P3 Light 글자 보임: {!repro} (Light 밝은 픽셀 {lightLit} · 대조 {controlLit})");
            AtlasReport(sb, Find(view.transform, "Title")?.GetComponent<TMP_Text>());

            if (repro && light != null)
            {
                // 원인 가르기 — 동적 글리프를 비우면 TMP가 다시 굽는다. 다시 보이면 글리프 표 ↔ 아틀라스 그림 어긋남(에디터 동적 글꼴 데이터).
                light.ClearFontAssetData(false);
                foreach (var t in view.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
                await UniTask.DelayFrame(3);
                var tex2 = await ShotAsync(null);
                int relit = 0;
                if (tex2 != null)
                {
                    foreach (var n in lightNames) relit += LitPixels(tex2, Find(view.transform, n) as RectTransform);
                    Object.Destroy(tex2);
                }
                sb.AppendLine($"  재현 → Light 동적 글리프 비우고 다시 그림: 밝은 픽셀 {relit} → " +
                              (relit > 300 ? "원인 = 글리프 표와 아틀라스 그림 어긋남(에디터 동적 글꼴 데이터). 빌드는 ClearDynamicDataOnBuild라 해당 없음일 가능성이 크다"
                                           : "다시 그려도 비어 있음 — 머티리얼 · 글꼴 연결 쪽을 볼 것"));
            }

            // 대비(P1) — 잉크 알파 0.5 아래 글자(「아직 안 간 챕터」 0.32는 비활성이라 예외)
            int low = 0;
            foreach (var t in view.GetComponentsInChildren<TMP_Text>(false))
            {
                if (t.color.a >= 0.5f || t.name == "Label" && t.transform.parent != null && t.transform.parent.name.StartsWith("Node")) continue;
                low++;
                sb.AppendLine($"  알파 0.5 미만: {t.name} {t.color.a:0.00}");
            }
            sb.AppendLine($"  CHECK P1 정보 글 알파 ≥ 0.5: {low == 0}");
            DescribeText(sb, view.transform, "Keep", "작은 라벨 「남는 것」(목표 15 · 500 · 자간 15)", deep: true);
            DescribeText(sb, view.transform, "ERow0", "행 글자(목표 20)", deep: true);
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally
        {
            if (tex != null) Object.Destroy(tex);
            RunResultView.Active?.Continue();
            try { await task; } catch (System.Exception) { }
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
    }

    // ── ④ ESC ──────────────────────────────────────────────
    private static async UniTask EscAsync(StringBuilder sb)
    {
        sb.AppendLine("[4 ESC 메뉴]");
        var esc = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        if (esc == null) { sb.AppendLine("  UI_EscMenu 없음"); return; }
        GameObject hovered = null;
        try
        {
            esc.Open();
            await UniTask.Delay(400, ignoreTimeScale: true);
            // 「설정」 줄에 마우스가 올라온 상태 — 위아래 1px 금 띠
            hovered = esc.transform.Find("EscMenuRoot/Panel/Btn_설정")?.gameObject;
            if (hovered != null) ExecuteEvents.Execute(hovered, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            await UniTask.Delay(300, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_4_Esc");
            if (tex != null) Object.Destroy(tex);

            var panel = esc.transform.Find("EscMenuRoot/Panel") as RectTransform;
            var btn   = hovered != null ? hovered.transform as RectTransform : null;
            var band  = btn != null ? btn.Find("BandTop")?.GetComponent<Image>() : null;
            sb.AppendLine($"  판 {(panel != null ? $"{panel.rect.width:0}×{panel.rect.height:0}" : "-")}(목표 420×400) · 버튼 {(btn != null ? $"{btn.rect.width:0}×{btn.rect.height:0}" : "-")}(목표 320×48)");
            sb.AppendLine($"  CHECK 고른 줄 금 띠 켜짐: {band != null && band.color.a > 0.8f} ({(band != null ? band.color.a : 0f):0.00})");
            DescribeText(sb, btn, "Label", "버튼 글자(목표 Noto 500 · 22)");
            DescribeText(sb, panel, "Title", "제목(목표 DNF 32 · 굵게 없음)");
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally
        {
            if (hovered != null) ExecuteEvents.Execute(hovered, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            esc.Close();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    // ── ⑤ 설정 ─────────────────────────────────────────────
    private static async UniTask SettingsAsync(StringBuilder sb)
    {
        sb.AppendLine("[5 설정]");
        try
        {
            UI_Settings.Open();
            await UniTask.Delay(500, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_5_Settings");
            if (tex != null) Object.Destroy(tex);

            var settings = Object.FindFirstObjectByType<UI_Settings>(FindObjectsInactive.Include);
            var panel = settings != null ? settings.transform.Find("SettingsRoot/Panel") as RectTransform : null;
            if (panel != null)
            {
                sb.AppendLine($"  CHECK 판 = 내용 크기: {panel.rect.width:0}×{panel.rect.height:0}(목표 1480×859) · 맞춤 부품 없음 {panel.GetComponent<UIWindowFitter>() == null}");
                var track = panel.Find("Slider_마스터/Background") as RectTransform;
                sb.AppendLine($"  CHECK 슬라이더 트랙 6px: {(track != null ? track.rect.height : -1f):0.0}");
                var l1 = panel.Find("Label_마스터") as RectTransform;
                var l2 = panel.Find("Label_배경음") as RectTransform;
                if (l1 != null && l2 != null) sb.AppendLine($"  CHECK 행 간격 60: {l1.anchoredPosition.y - l2.anchoredPosition.y:0}");
                DescribeText(sb, panel, "Label_마스터", "행 라벨(목표 Noto 400 · 20)");
                DescribeText(sb, panel, "Title", "제목(목표 DNF 32)");
                StyleAudit(sb, panel, allowDnf: "Title");
            }
            else sb.AppendLine("  설정 판을 못 찾음");
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally
        {
            UI_Settings.CloseIfOpen();   // 아무것도 안 바꿨다 — 되돌림 경로도 그대로
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    // ── ⑥ 로딩 ─────────────────────────────────────────────
    private static async UniTask LoadingAsync(StringBuilder sb)
    {
        sb.AppendLine("[6 로딩]");
        var ld = UI_SceneLoading.Instance;
        if (ld == null) { sb.AppendLine("  로딩 오버레이 없음"); return; }
        try
        {
            await ld.ShowAsync();
            ld.SetProgress(0.45f);
            await UniTask.Delay(900, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_6_Loading");
            if (tex != null) Object.Destroy(tex);
            DescribeText(sb, ld.transform.Find("LoadingBar"), "Label", "라벨 「불러오는 중」(목표 Noto 500 · 20 · 막대 760×6 유지)");
            DescribeText(sb, ld.transform.Find("LoadingBar"), "Percent", "퍼센트(목표 Noto 400 · 20)");
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
        finally { await ld.HideAsync(); }
    }

    // ── ⑦ 세이브 슬롯(로비로 넘어간다) ──────────────────────
    private static async UniTask SaveSlotAsync(StringBuilder sb)
    {
        sb.AppendLine("[7 세이브 슬롯 — 로비]");
        try
        {
            if (Object.FindFirstObjectByType<UI_Lobby>() == null)
            {
                AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
                for (int i = 0; i < 300 && Object.FindFirstObjectByType<UI_Lobby>() == null; i++)
                    await UniTask.Delay(100, ignoreTimeScale: true);
                await UniTask.Delay(5000, ignoreTimeScale: true);   // 로딩 · 타이틀 페이드가 걷히도록
            }
            var lobby = Object.FindFirstObjectByType<UI_Lobby>();
            if (lobby == null) { sb.AppendLine("  로비(UI_Lobby)를 못 찾음"); return; }
            typeof(UI_Lobby).GetMethod("OnClickStartRun", Any)?.Invoke(lobby, null);
            await UniTask.Delay(800, ignoreTimeScale: true);
            var tex = await ShotAsync("UX2_7_SaveSlot");
            if (tex != null) Object.Destroy(tex);

            foreach (var card in Object.FindObjectsByType<UI_SaveSlotCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var p = typeof(UI_SaveSlotCard).GetField("progressText", Any)?.GetValue(card) as TMP_Text;
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                sb.AppendLine($"  {card.name} 카드 글: {Font(p)} · {p.fontSize:0} · {p.fontStyle}(목표 Noto 400 · 24 · Normal) 「{p.text}」");
            }
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e.Message); }
    }

    // ── 부품 ────────────────────────────────────────────────

    /// <summary>화면을 읽어(검은 프레임이면 다시) 이름이 있으면 PNG로 남기고 텍스처를 돌려준다. 호출측이 Destroy한다.</summary>
    private static async UniTask<Texture2D> ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await UniTask.WaitForEndOfFrame(runner);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;
            bool black = true;
            for (int y = 0; y < tex.height && black; y += Mathf.Max(1, tex.height / 12))
                for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                    if (tex.GetPixel(x, y).maxColorComponent > 0.02f) { black = false; break; }
            if (black && attempt < 11) { Object.Destroy(tex); continue; }
            if (!string.IsNullOrEmpty(name)) File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            return tex;
        }
        return null;
    }

    /// <summary>글자 칸(화면 사각형) 안에서 밝은 픽셀 수 — 오버레이 캔버스라 월드 모서리가 곧 화면 좌표다.</summary>
    private static int LitPixels(Texture2D tex, RectTransform rt)
    {
        if (tex == null || rt == null) return 0;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(c[0].x), 0, tex.width - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(c[2].x), 0, tex.width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(c[0].y), 0, tex.height - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(c[2].y), 0, tex.height - 1);
        int n = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = tex.GetPixel(x, y);
                if (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b > LitLum) n++;
            }
        return n;
    }

    private static int LitReport(StringBuilder sb, Texture2D tex, Transform tf, string kind)
    {
        var t = tf != null ? tf.GetComponent<TMP_Text>() : null;
        if (t == null) { sb.AppendLine($"  {kind} {(tf != null ? tf.name : "?")}: 없음"); return 0; }
        int lit = LitPixels(tex, t.rectTransform);
        sb.AppendLine($"  {kind} {t.name} 「{t.text}」 {Font(t)} · 밝은 픽셀 {lit} · 글자 {t.textInfo.characterCount}");
        return lit;
    }

    /// <summary>글리프 표가 가리키는 아틀라스 칸에 그림이 있는지(CPU 사본) — 표만 있고 그림이 비면 빈칸으로 그려진다.</summary>
    private static void AtlasReport(StringBuilder sb, TMP_Text t)
    {
        if (t == null || t.font == null) return;
        var info = t.textInfo;
        int checkedN = 0, empty = 0;
        for (int i = 0; i < info.characterCount && checkedN < 6; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible || ch.textElement == null || ch.textElement.glyph == null) continue;
            var g = ch.textElement.glyph;
            var atlases = t.font.atlasTextures;
            if (atlases == null || g.atlasIndex < 0 || g.atlasIndex >= atlases.Length || atlases[g.atlasIndex] == null) { empty++; checkedN++; continue; }
            var atlas = atlases[g.atlasIndex];
            if (!atlas.isReadable) { sb.AppendLine("  아틀라스 읽기 불가 — CPU 사본 검사 생략"); return; }
            var r = g.glyphRect;
            float sum = 0f;
            for (int y = r.y; y < r.y + r.height; y += 2)
                for (int x = r.x; x < r.x + r.width; x += 2)
                    sum += atlas.GetPixel(x, y).a;
            if (sum < 1f) empty++;
            checkedN++;
        }
        sb.AppendLine($"  제목 글리프 {checkedN}개 중 아틀라스 칸이 빈 것 {empty}개(CPU 사본 기준)");
    }

    private static Transform Find(Transform root, string name) => root != null ? ShopUIStyle.FindDeep(root, name) : null;

    private static void DescribeText(StringBuilder sb, Transform parent, string name, string what, bool deep = false)
    {
        var tf = parent == null ? null : deep ? ShopUIStyle.FindDeep(parent, name) : parent.Find(name);
        var t = tf != null ? tf.GetComponent<TMP_Text>() : null;
        sb.AppendLine(t == null ? $"  {what}: 없음"
            : $"  {what}: {Font(t)} · {t.fontSize:0.#} · {t.fontStyle} · 자간 {t.characterSpacing:0} · 알파 {t.color.a:0.00}");
    }

    /// <summary>켜진 글자 중 가짜 굵게 · 기울임 · DNF 본문(허용한 제목 한 줄 제외)을 센다.</summary>
    private static void StyleAudit(StringBuilder sb, Transform root, string allowDnf)
    {
        int fake = 0, dnf = 0;
        var dnfFont = TMP_Settings.defaultFontAsset;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            if ((t.fontStyle & (FontStyles.Bold | FontStyles.Italic)) != 0) { fake++; sb.AppendLine($"  굵게/기울임 남음: {t.name} {t.fontStyle}"); }
            if (t.font == dnfFont && t.name != allowDnf) { dnf++; sb.AppendLine($"  DNF 본문 남음: {t.name}"); }
        }
        sb.AppendLine($"  CHECK 가짜 굵게 · 기울임 0: {fake == 0} · DNF 본문 0: {dnf == 0}");
    }

    private static string Font(TMP_Text t) => t.font != null ? t.font.name.Replace(" SDF", "") : "null";

    private static bool Bluish(Color c) => c.b > 0.6f && c.b > c.r + 0.15f && c.a > 0.3f;

    private static List<WeaponSO> RangedWeapons()
    {
        var res = new List<WeaponSO>();
        var altar = Object.FindFirstObjectByType<WeaponForgeAltar>(FindObjectsInactive.Include);
        if (altar != null)
        {
            foreach (var f in new[] { "bowOption", "crossbowOption" })
                if (typeof(WeaponForgeAltar).GetField(f, Any)?.GetValue(altar) is WeaponSO w && w != null) res.Add(w);
            if (res.Count > 0) return res;
        }
        foreach (var guid in AssetDatabase.FindAssets("t:MainWeaponSO"))
        {
            var w = AssetDatabase.LoadAssetAtPath<MainWeaponSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (w == null) continue;
            string n = w.name.ToLowerInvariant();
            if (n.Contains("bow")) res.Add(w);
            if (res.Count >= 2) break;
        }
        return res;
    }

    private static void HideTestHubGui()
    {
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        if (hub == null || !hub.enabled) return;
        hub.enabled = false;
        s_hiddenHub = hub;
    }

    private static void RestoreTestHubGui()
    {
        if (s_hiddenHub != null) s_hiddenHub.enabled = true;
        s_hiddenHub = null;
    }
}
#endif
