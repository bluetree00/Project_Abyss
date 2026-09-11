using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 프리팹의 <b>실제 레이아웃</b>을 재서 JSON으로 떨군다.
///
/// <para>프리팹 파일에 적힌 rect는 레이아웃 그룹·ContentSizeFitter 아래에서 <b>죽은 값</b>이다 —
/// Unity가 첫 프레임에 다시 쓴다. 그래서 파일만 파싱해서는 "넘쳤다/안 넘쳤다"를 말할 수 없다.
/// 여기서는 <see cref="PrefabUtility.LoadPrefabContents"/>로 <b>격리된 미리보기 씬</b>에 열고
/// (열려 있는 씬은 건드리지 않는다), 루트를 1920×1080으로 세운 뒤
/// <see cref="LayoutRebuilder.ForceRebuildLayoutImmediate"/>로 레이아웃을 <b>실제로 계산시켜</b> 잰다.</para>
///
/// <para>저장하지 않는다 — 잰 뒤 그대로 언로드하므로 프리팹은 바뀌지 않는다.</para>
/// </summary>
public static class UILayoutProbeEditor
{
    private const float ScreenW = 1920f;
    private const float ScreenH = 1080f;

    [MenuItem("RelicFairy/UI/레이아웃 실측 덤프")]
    private static void Probe()
    {
        var sb = new StringBuilder();
        sb.Append("{\"screens\":[");

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RelicFairy/UI" });
        bool firstScreen = true;
        int screens = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("_PrefabBackup")) continue;

            GameObject root = null;
            try { root = PrefabUtility.LoadPrefabContents(path); }
            catch { continue; }
            if (root == null) continue;

            try
            {
                var rootRT = root.transform as RectTransform;
                if (rootRT == null) continue;

                // 위젯 프리팹(대화 알림·체력바 등)은 <b>제 크기</b>가 설계값이다.
                // 그걸 화면 크기로 늘려버리면 안쪽 배치가 통째로 거짓이 된다.
                // 반대로 전면 팝업은 authored 크기가 0이거나 화면만 한 값이므로 화면 크기로 세운다.
                Vector2 authored = rootRT.rect.size;
                bool widget = authored.x > 1f && authored.y > 1f &&
                              authored.x < ScreenW - 20f && authored.y < ScreenH - 20f;

                // 스크린 스페이스 Canvas는 <b>자기 RectTransform을 스스로 구동</b>한다 —
                // 우리가 세운 크기를 캔버스 갱신이 곧바로 덮어써(격리 씬에는 화면이 없어 100×100이 된다)
                // 측정이 통째로 무너진다. 재는 동안만 WorldSpace로 돌려 구동을 끈다(저장하지 않는다).
                var rootCanvas = root.GetComponent<Canvas>();
                if (rootCanvas == null) rootCanvas = root.AddComponent<Canvas>();
                rootCanvas.renderMode = RenderMode.WorldSpace;
                foreach (var cv in root.GetComponentsInChildren<Canvas>(true))
                    cv.renderMode = RenderMode.WorldSpace;
                rootRT.anchorMin = rootRT.anchorMax = rootRT.pivot = new Vector2(0.5f, 0.5f);
                rootRT.anchoredPosition = Vector2.zero;
                rootRT.sizeDelta = widget ? authored : new Vector2(ScreenW, ScreenH);
                rootRT.localScale = Vector3.one;

                // 자식 Canvas는 런타임에 <b>화면 크기로 구동</b>된다(스크린 스페이스).
                // 격리 씬에는 화면이 없어 0×0으로 남으므로, 여기서 직접 세워준다.
                // 이렇게 하지 않으면 HUD처럼 캔버스를 여러 장 쓰는 프리팹이 통째로 0×0으로 측정된다.
                foreach (var cv in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (cv.transform == rootRT) continue;
                    if (cv.transform is not RectTransform crt) continue;
                    crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
                    crt.anchoredPosition = Vector2.zero;
                    crt.sizeDelta = new Vector2(ScreenW, ScreenH);
                    // 스크린 스페이스 캔버스는 위치·크기·<b>배율</b>을 Unity가 런타임에 덮어쓴다.
                    // 프리팹에는 배율 0으로 저장돼 있어(정상), 그대로 두면 그 아래 전부가
                    // 월드 공간에서 0으로 접혀 측정이 불가능해진다.
                    crt.localScale = Vector3.one;
                }

                // @UIRoot의 Canvas_*·@HUD 처럼 <b>런타임에 크기를 받는</b> 빈 컨테이너는
                // 프리팹에서 크기가 0이다. 그대로 두면 그 아래 전부가 0×0으로 측정돼
                // HUD 전체가 검사에서 사라진다. <b>자식을 가진 0크기 칸</b>은 부모를 채우게 한다.
                // 부모를 고치면 자식의 크기가 달라지므로 위에서 아래로 훑는다.
                FillZeroContainers(rootRT);

                // 레이아웃을 실제로 돌린다. 중첩 그룹이 수렴하도록 두 번.
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);

                if (!firstScreen) sb.Append(',');
                firstScreen = false;
                sb.Append("{\"prefab\":\"").Append(Esc(Path.GetFileNameWithoutExtension(path)))
                  .Append("\",\"path\":\"").Append(Esc(path)).Append("\",\"nodes\":[");

                int order = 0;
                bool firstNode = true;
                Walk(rootRT, rootRT, "", 0, ref order, sb, ref firstNode);

                sb.Append("]}");
                screens++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        sb.Append("]}");

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, "ui_layout_probe.json");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[UILayoutProbe] {screens}개 화면 실측 완료 → {outPath}");
    }

    /// <summary>자식이 있는데 크기가 0인 칸을 부모 채움으로 바꾼다(위에서 아래로).</summary>
    private static void FillZeroContainers(RectTransform rt)
    {
        for (int i = 0; i < rt.childCount; i++)
        {
            if (rt.GetChild(i) is not RectTransform c) continue;

            if (c.childCount > 0 && (c.rect.width <= 1f || c.rect.height <= 1f))
            {
                c.anchorMin = Vector2.zero;
                c.anchorMax = Vector2.one;
                c.pivot = new Vector2(0.5f, 0.5f);
                c.anchoredPosition = Vector2.zero;
                c.sizeDelta = Vector2.zero;
                if (c.localScale == Vector3.zero) c.localScale = Vector3.one;
            }
            FillZeroContainers(c);
        }
    }

    internal static void Walk(RectTransform rt, RectTransform root, string parentPath, int depth,
                             ref int order, StringBuilder sb, ref bool first)
    {
        for (int i = 0; i < rt.childCount; i++)
        {
            var c = rt.GetChild(i) as RectTransform;
            if (c == null) continue;

            string p = parentPath.Length == 0 ? c.name : parentPath + "/" + c.name;
            order++;

            if (!first) sb.Append(',');
            first = false;
            Emit(c, root, p, depth + 1, order, sb);

            Walk(c, root, p, depth + 1, ref order, sb, ref first);
        }
    }

    internal static void Emit(RectTransform rt, RectTransform root, string path, int depth, int order, StringBuilder sb)
    {
        // 루트 로컬 좌표로 환산한다 — 부모 배율·회전이 모두 반영된 <b>실제로 보이는</b> 사각형이다.
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 bl = root.InverseTransformPoint(corners[0]);
        Vector3 tr = root.InverseTransformPoint(corners[2]);

        sb.Append("{\"path\":\"").Append(Esc(path)).Append('"')
          .Append(",\"name\":\"").Append(Esc(rt.name)).Append('"')
          .Append(",\"depth\":").Append(depth)
          .Append(",\"order\":").Append(order)
          .Append(",\"active\":").Append(rt.gameObject.activeInHierarchy ? "true" : "false")
          .Append(",\"x\":").Append(F(bl.x)).Append(",\"y\":").Append(F(bl.y))
          .Append(",\"w\":").Append(F(tr.x - bl.x)).Append(",\"h\":").Append(F(tr.y - bl.y));

        if (rt.TryGetComponent<Button>(out var btn))
            sb.Append(",\"btn\":true,\"btnOn\":").Append(btn.interactable ? "true" : "false");

        // 꺼진 Image(enabled=false)는 안 그려진다 — 스킨이 끈 구판 조각을 왜곡으로 오판하지 않게 건너뛴다.
        if (rt.TryGetComponent<Image>(out var img) && img.enabled)
        {
            sb.Append(",\"img\":{\"a\":").Append(F(img.color.a))
              .Append(",\"ray\":").Append(img.raycastTarget ? "true" : "false")
              .Append(",\"type\":\"").Append(img.type).Append('"')
              .Append(",\"keepAspect\":").Append(img.preserveAspect ? "true" : "false");
            if (img.sprite != null)
            {
                var b = img.sprite.border;
                sb.Append(",\"sprite\":\"").Append(Esc(img.sprite.name)).Append('"')
                  .Append(",\"nw\":").Append(F(img.sprite.rect.width))
                  .Append(",\"nh\":").Append(F(img.sprite.rect.height))
                  .Append(",\"border\":").Append(b.sqrMagnitude > 0.01f ? "true" : "false");
            }
            sb.Append('}');
        }

        if (rt.TryGetComponent<TMP_Text>(out var t))
        {
            // 비활성·미초기화 TMP는 preferredWidth 계산에서 예외를 던지고 text가 null일 수 있다 — 한 노드 때문에 덤프 전체가 깨지면 안 된다.
            float prefW = 0f, prefH = 0f;
            string txt = t.text ?? string.Empty;
            try { if (rt.gameObject.activeInHierarchy) { prefW = t.preferredWidth; prefH = t.preferredHeight; } }
            catch (System.Exception) { }
            sb.Append(",\"tmp\":{\"size\":").Append(F(t.fontSize))
              .Append(",\"auto\":").Append(t.enableAutoSizing ? "true" : "false")
              .Append(",\"min\":").Append(F(t.fontSizeMin)).Append(",\"max\":").Append(F(t.fontSizeMax))
              .Append(",\"prefW\":").Append(F(prefW)).Append(",\"prefH\":").Append(F(prefH))
              .Append(",\"wrap\":").Append(t.textWrappingMode != TextWrappingModes.NoWrap ? "true" : "false")
              .Append(",\"ha\":").Append((int)t.horizontalAlignment)
              .Append(",\"va\":").Append((int)t.verticalAlignment)
              .Append(",\"col\":\"").Append(ColorUtility.ToHtmlStringRGBA(t.color)).Append('"')
              .Append(",\"font\":\"").Append(Esc(t.font != null ? t.font.name : "")).Append('"')
              .Append(",\"text\":\"").Append(Esc(txt.Length > 40 ? txt.Substring(0, 40) : txt)).Append("\"}");
        }

        if (rt.GetComponent<LayoutGroup>() != null)        sb.Append(",\"group\":true");
        if (rt.GetComponent<ContentSizeFitter>() != null)  sb.Append(",\"fitter\":true");
        if (rt.GetComponent<ScrollRect>() != null)         sb.Append(",\"scroll\":true");
        if (rt.GetComponent<RectMask2D>() != null || rt.GetComponent<Mask>() != null) sb.Append(",\"mask\":true");

        sb.Append('}');
    }

    internal static string F(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            const char q = '"', bslash = (char)92;
            if (c == q || c == bslash) sb.Append(bslash).Append(c);
            else if (c == (char)10 || c == (char)13 || c == (char)9) sb.Append(' ');
            else if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
