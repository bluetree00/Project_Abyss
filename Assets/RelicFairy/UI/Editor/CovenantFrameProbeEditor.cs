using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 서약 조립 카드의 <b>등급 테두리</b> 실측 — 플레이 중 메뉴.
///
/// <para>일반 런타임 프로브(<see cref="UILayoutRuntimeProbeEditor"/>)는 드래프트를 한 번 굴린 화면만 찍어서
/// 어떤 등급이 나올지 모른다. 테두리는 등급마다 조각 아트의 크기·비율이 달라 <b>등급별로 따로</b> 봐야 한다.
/// 여기서는 카드 6장을 실버 → 골드 → 루비로 강제로 묶고, 열마다 한 장을 선택 상태로 둔 채 찍는다.</para>
///
/// <para>산출물: <c>Temp/ui_shots/covenant_frame_&lt;등급&gt;.png</c> +
/// <c>Temp/covenant_frame_probe.json</c>(카드·조각의 화면 좌표와 스프라이트 크기).</para>
/// </summary>
public static class CovenantFrameProbeEditor
{
    private const string OutFile = "covenant_frame_probe.json";

    [MenuItem("RelicFairy/UI/서약 등급 테두리 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || Managers.UI == null)
        {
            Debug.LogWarning("[CovenantFrameProbe] 플레이 모드에서 부팅이 끝난 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        UI_CovenantAssemble popup = null;
        try
        {
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_CovenantAssemble>();
            if (popup == null) { Debug.LogWarning("[CovenantFrameProbe] 팝업을 열지 못했다."); return; }
            popup.Setup(false, new System.Random(1));
            await UniTask.DelayFrame(3);

            var cards = popup.GetComponentsInChildren<UI_AssembleCard>(true);
            var sb = new StringBuilder(1 << 16);
            sb.Append("{\"screen\":{\"w\":").Append(Screen.width).Append(",\"h\":").Append(Screen.height).Append("},\"tiers\":[");

            // 등급색은 팝업이 쓰는 값 그대로(비공개 정적 메서드) — 흰색으로 대신하면 글로우·배지 색이 실제와 달라진다.
            var tierColor = typeof(UI_CovenantAssemble).GetMethod("TierColor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // 0번 카드 = 최악 조합(가장 긴 이름·설명·배지를 한 장에). 1·2번 = 긴 설명들 — 낱말 중간 줄바꿈 검증용.
            CovenantPalette.TryGetCause("hunt", out var longCauseName);
            CovenantPalette.TryGetCause("swap", out var longCauseDesc);
            CovenantPalette.TryGetEffect("bloodmark", out var longEffectName);
            CovenantPalette.TryGetEffect("ward", out var longEffectDesc);
            string badge = EffectTaxonomy.Badge(longEffectName.axis, longEffectName.status);
            var causeIds  = new[] { "besiege", "streak" };
            var effectIds = new[] { "arcflash", "stasis" };

            var tiers = new[] { CovenantTier.Silver, CovenantTier.Gold, CovenantTier.Ruby };
            for (int t = 0; t < tiers.Length; t++)
            {
                var tier  = tiers[t];
                var color = tierColor != null ? (Color)tierColor.Invoke(null, new object[] { tier }) : Color.white;
                for (int i = 0; i < cards.Length; i++)
                {
                    int slot = i % 3;
                    if (cards[i].name.StartsWith("Cause"))
                    {
                        if (slot == 0) cards[i].Bind(longCauseName.name, longCauseDesc.desc, tier, color);
                        else if (CovenantPalette.TryGetCause(causeIds[slot - 1], out var cd))
                            cards[i].Bind(cd.name, cd.desc, tier, color);
                    }
                    else
                    {
                        if (slot == 0) cards[i].Bind(longEffectName.name, longEffectDesc.desc, tier, color, badge);
                        else if (CovenantPalette.TryGetEffect(effectIds[slot - 1], out var ed))
                            cards[i].Bind(ed.name, ed.desc, tier, color, EffectTaxonomy.Badge(ed.axis, ed.status));
                    }
                    // 열마다 가운데 카드(1번)만 선택 — 한 화면에 선택/비선택 바탕을 같이 담는다.
                    cards[i].SetSelected(i % 3 == 1);
                }
                await UniTask.DelayFrame(3);
                Canvas.ForceUpdateCanvases();

                string label = tier.ToString();
                await ShotAsync("covenant_frame_" + label);

                if (t > 0) sb.Append(',');
                sb.Append("{\"tier\":\"").Append(label).Append("\",\"cards\":[");
                for (int i = 0; i < cards.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendCard(sb, cards[i]);
                }
                sb.Append("]}");
            }
            sb.Append("]}");

            string path = Path.Combine(Directory.GetCurrentDirectory(), "Temp", OutFile);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[CovenantFrameProbe] 완료 — {path}");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[CovenantFrameProbe] 실패 — {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            if (popup != null) Managers.UI.CloseAllPopupUI();
        }
    }

    // ── HUD 보유 서약 패널 ─────────────────────────────────
    // 런 HUD의 서약 칸은 레이아웃 그룹이 크기를 정해 프리팹 값이 죽은 데이터다 — 실제로 켜고 표본을 먹여 잰다.
    // 가장 긴 이름·설명 조합 4개를 넣는다(칸은 최대 4개).
    [MenuItem("RelicFairy/UI/서약 HUD 칸 실측 (플레이 중)")]
    private static void RunHud()
    {
        if (!EditorApplication.isPlaying || Managers.UI == null)
        {
            Debug.LogWarning("[CovenantFrameProbe] 플레이 모드에서 부팅이 끝난 뒤 실행해야 한다.");
            return;
        }
        RunHudAsync().Forget();
    }

    private static async UniTaskVoid RunHudAsync()
    {
        var presenter = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        var panel     = UnityEngine.Object.FindFirstObjectByType<CovenantPanelView>(FindObjectsInactive.Include);
        if (presenter == null || panel == null)
        {
            Debug.LogWarning("[CovenantFrameProbe] HudPresenter/CovenantPanelView가 없다.");
            return;
        }
        try
        {
            presenter.SetMode(HUDIds.Mode.Combat);
            await UniTask.DelayFrame(3);

            var ids = new[]
            {
                AssembledCovenant.MakeId("heartbeat", CovenantTier.Ruby,   "ward",       CovenantTier.Ruby),
                AssembledCovenant.MakeId("hunt",      CovenantTier.Gold,   "lastbreath", CovenantTier.Gold),
                AssembledCovenant.MakeId("swap",      CovenantTier.Silver, "arcflash",   CovenantTier.Silver),
                AssembledCovenant.MakeId("besiege",   CovenantTier.Ruby,   "stasis",     CovenantTier.Gold),
            };
            var list = new System.Collections.Generic.List<CovenantBase>();
            foreach (var id in ids)
            {
                var c = CovenantFactory.Create(id);
                if (c != null) list.Add(c);
            }
            panel.Refresh(list);
            await UniTask.DelayFrame(3);
            Canvas.ForceUpdateCanvases();
            await ShotAsync("covenant_hud");

            var sb = new StringBuilder(1 << 14);
            sb.Append("{\"screen\":{\"w\":").Append(Screen.width).Append(",\"h\":").Append(Screen.height).Append("},\"slots\":[");
            bool first = true;
            foreach (var slot in panel.GetComponentsInChildren<UI_CovenantSlot>(false))
            {
                if (!first) sb.Append(',');
                first = false;
                var srt = (RectTransform)slot.transform;
                sb.Append("{\"name\":\"").Append(slot.name).Append("\",\"rect\":");
                AppendScreenRect(sb, srt);
                if (slot.TryGetComponent<Image>(out var frame)) AppendSprite(sb, "frame", frame);
                sb.Append(",\"texts\":[");
                AppendTexts(sb, slot.transform, null);
                sb.Append("]}");
            }
            sb.Append("]}");
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "covenant_hud_probe.json");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[CovenantFrameProbe] HUD 완료 — {path}");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[CovenantFrameProbe] HUD 실패 — {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            panel.Clear();
            presenter.SetMode(HUDIds.Mode.Lobby);
        }
    }

    private static void AppendCard(StringBuilder sb, UI_AssembleCard card)
    {
        var rt = (RectTransform)card.transform;
        sb.Append("{\"name\":\"").Append(card.name).Append("\",\"rect\":");
        AppendScreenRect(sb, rt);
        if (card.TryGetComponent<Image>(out var bg)) AppendSprite(sb, "bg", bg);
        sb.Append(",\"pieces\":[");
        bool first = true;
        // 조각은 몸통 틀(GradeFrame) 아래에 붙는다 — 틀이 없으면(구 스킨) 카드 직속을 본다.
        Transform pieceRoot = rt.Find("GradeFrame");
        if (pieceRoot == null) pieceRoot = rt;
        foreach (Transform child in pieceRoot)
        {
            if (!child.name.StartsWith("Grade")) continue;
            if (!child.TryGetComponent<Image>(out var img)) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":\"").Append(child.name).Append("\",\"enabled\":").Append(img.enabled ? "true" : "false")
              .Append(",\"scale\":[").Append(F(child.localScale.x)).Append(',').Append(F(child.localScale.y)).Append("],\"rect\":");
            AppendScreenRect(sb, (RectTransform)child);
            AppendSprite(sb, "sprite", img);
            sb.Append('}');
        }
        sb.Append(']');

        // 조각 틀(몸통)·리롤 버튼·글자 — 테두리가 몸통에 붙었는지, 글자가 몸통 안에 있는지 대조한다.
        var frame = rt.Find("GradeFrame") as RectTransform;
        if (frame != null) { sb.Append(",\"body\":"); AppendScreenRect(sb, frame); }
        if (card.RerollButton != null) { sb.Append(",\"reroll\":"); AppendScreenRect(sb, (RectTransform)card.RerollButton.transform); }

        sb.Append(",\"texts\":[");
        AppendTexts(sb, card.transform, card.RerollButton != null ? card.RerollButton.transform : null);
        sb.Append("]}");
    }

    /// <summary>root 아래 켜진 글자 전부 — 상자·실제 글꼴 크기·줄 수·넘침·잉크(실제 글자 영역).</summary>
    private static void AppendTexts(StringBuilder sb, Transform root, Transform exclude)
    {
        bool firstText = true;
        foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (!tmp.gameObject.activeInHierarchy) continue;
            if (exclude != null && tmp.transform.IsChildOf(exclude)) continue;
            if (!firstText) sb.Append(',');
            firstText = false;
            tmp.ForceMeshUpdate();
            var b = tmp.textBounds;   // 로컬 — 실제 글자가 차지하는 영역
            var trt = tmp.rectTransform;
            var w0 = trt.TransformPoint(b.min);
            var w1 = trt.TransformPoint(b.max);
            var canvas = trt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, w0);
            Vector2 s1 = RectTransformUtility.WorldToScreenPoint(cam, w1);
            sb.Append("{\"name\":\"").Append(tmp.name).Append("\",\"size\":").Append(F(tmp.fontSize))
              .Append(",\"lines\":").Append(tmp.textInfo.lineCount)
              .Append(",\"overflow\":").Append(tmp.isTextOverflowing ? "true" : "false")
              .Append(",\"empty\":").Append(string.IsNullOrEmpty(tmp.text) ? "true" : "false")
              .Append(",\"lineText\":[");
            var info = tmp.textInfo;
            for (int li = 0; li < info.lineCount; li++)
            {
                var line = info.lineInfo[li];
                var lsb = new StringBuilder();
                for (int ci = line.firstCharacterIndex; ci <= line.lastCharacterIndex && ci < info.characterCount; ci++)
                    lsb.Append(info.characterInfo[ci].character);
                if (li > 0) sb.Append(',');
                sb.Append('"').Append(lsb.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "")).Append('"');
            }
            sb.Append("],\"rect\":");
            AppendScreenRect(sb, trt);
            sb.Append(",\"ink\":[").Append(F(Mathf.Min(s0.x, s1.x))).Append(',').Append(F(Mathf.Min(s0.y, s1.y)))
              .Append(',').Append(F(Mathf.Max(s0.x, s1.x))).Append(',').Append(F(Mathf.Max(s0.y, s1.y))).Append("]}");
        }
    }

    private static void AppendSprite(StringBuilder sb, string key, Image img)
    {
        var s = img.sprite;
        sb.Append(",\"").Append(key).Append("\":{\"name\":\"").Append(s != null ? s.name : "")
          .Append("\",\"w\":").Append(s != null ? F(s.rect.width) : "0")
          .Append(",\"h\":").Append(s != null ? F(s.rect.height) : "0")
          .Append(",\"type\":\"").Append(img.type).Append("\",\"ppum\":").Append(F(img.pixelsPerUnitMultiplier))
          .Append(",\"preserve\":").Append(img.preserveAspect ? "true" : "false").Append('}');
    }

    // 화면 픽셀 좌표(좌하단 원점). 반전(scale -1)된 조각도 네 귀퉁이의 최소·최대로 잡는다.
    private static void AppendScreenRect(StringBuilder sb, RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var c in corners)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, c);
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y);
            x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        sb.Append("[").Append(F(x0)).Append(',').Append(F(y0)).Append(',').Append(F(x1)).Append(',').Append(F(y1)).Append(']');
    }

    private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static async UniTask ShotAsync(string name)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ui_shots");
        Directory.CreateDirectory(dir);
        await UniTask.Delay(600, ignoreTimeScale: true);   // 선택 팝·등장 연출이 끝나도록
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await UniTask.WaitForEndOfFrame(runner);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;
            bool black = tex.GetPixel(tex.width / 2, tex.height / 2).maxColorComponent < 0.02f;
            if (!black || attempt == 19)
            {
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                return;
            }
            UnityEngine.Object.Destroy(tex);
        }
    }
}
