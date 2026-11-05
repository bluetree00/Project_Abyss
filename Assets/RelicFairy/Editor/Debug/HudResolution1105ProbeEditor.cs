#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-05 사용자 「HUD UI가 해상도에 짤리는 문제」 — 게임 화면을 여러 비율로 바꿔 가며(16:9 · 16:10 · 21:9 · 32:9 · 4:3 · 5:4)
/// 화면 공간 캔버스의 켜진 그림 · 글자가 화면 밖으로 나간 만큼(px)을 적고 사진을 찍는다. 끝나면 원래 게임 화면 크기로 되돌린다.
/// 캔버스 = 1920×1080 · Scale With Screen Size · Match 0.5 — 비율이 다르면 논리 화면이 1920×1080이 아니다(21:9 = 약 2217×935).
/// 결과 Temp/hudres1105.txt · Temp/ui_shots/HR1105_*.png · 로그 「[HR1105] 끝」. 플레이 중(런 HUD가 떠 있을 때).
/// </summary>
public static class HudResolution1105ProbeEditor
{
    private const string Tag = "[HR1105]";
    private const string ShotDir = "Temp/ui_shots";
    private const float  Slack = 2f;   // 이만큼은 넘쳐도 무시(테 · 그림자)

    private static readonly (int w, int h, string label)[] Sizes =
    {
        (1920, 1080, "16x9"), (1920, 1200, "16x10"), (2560, 1080, "21x9"), (3840, 1080, "32x9"), (1440, 1080, "4x3"), (1280, 1024, "5x4"),
    };

    [MenuItem("RelicFairy/Debug/11-05 HUD 해상도 점검 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 HUD 해상도 점검 — 화면 밖으로 나간 켜진 그림 · 글자(px)\n");
        Directory.CreateDirectory(ShotDir);
        var gv = GameViewAccess.Open();
        int prevIndex = gv?.SelectedIndex ?? -1;
        var added = new List<int>();
        try
        {
            if (gv == null) { sb.AppendLine("게임 화면 창을 못 찾았다"); return; }
            foreach (var (w, h, label) in Sizes)
            {
                int idx = gv.AddFixed(w, h, $"HR1105 {label}");
                added.Add(idx);
                gv.SelectedIndex = idx;
                for (int i = 0; i < 6; i++) await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                await UniTask.Delay(400, DelayType.Realtime);
                sb.AppendLine($"\n## {label} — 화면 {Screen.width}×{Screen.height}");
                Audit(sb);
                ScreenCapture.CaptureScreenshot($"{ShotDir}/HR1105_{label}.png");
                await UniTask.Delay(300, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (gv != null)
            {
                if (prevIndex >= 0) gv.SelectedIndex = prevIndex;
                added.Sort();
                for (int i = added.Count - 1; i >= 0; i--) gv.RemoveCustom(added[i]);
            }
            File.WriteAllText("Temp/hudres1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>화면 공간 캔버스(오버레이 · 카메라)의 켜진 Graphic마다 화면 밖으로 나간 px — 많이 나간 순 30개.</summary>
    private static void Audit(StringBuilder sb)
    {
        var rows = new List<(float over, string line)>();
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.WorldSpace) continue;
            var cam = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
            float scale = canvas.scaleFactor;
            foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.isActiveAndEnabled || g.color.a < 0.05f || g.canvasRenderer.GetInheritedAlpha() < 0.05f) continue;
                var rt = g.rectTransform;
                if (rt.rect.width < 2f || rt.rect.height < 2f) continue;
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, c[i]);
                    minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                    minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
                }
                float left = -minX, right = maxX - Screen.width, bottom = -minY, top = maxY - Screen.height;
                float over = Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));
                if (over <= Slack) continue;
                string side = over == left ? "왼쪽" : over == right ? "오른쪽" : over == bottom ? "아래" : "위";
                string text = g is TMPro.TMP_Text t ? $" 「{Short(t.text)}」" : "";
                rows.Add((over, $"  {over,6:0}px {side} · {Path(rt, canvas.transform)}{text} · 캔버스 {canvas.name} ×{scale:0.###}"));
            }
        }
        rows.Sort((a, b) => b.over.CompareTo(a.over));
        if (rows.Count == 0) sb.AppendLine("  화면 밖 없음");
        for (int i = 0; i < rows.Count && i < 30; i++) sb.AppendLine(rows[i].line);
        if (rows.Count > 30) sb.AppendLine($"  … 그 밖 {rows.Count - 30}개");
    }

    private static string Short(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").Replace("\n", " / ");
        return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
    }

    private static string Path(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>게임 화면 창 크기 고르기(에디터 내부 형식 — 리플렉션). 고정 해상도를 더하고 · 고르고 · 지운다.</summary>
    private sealed class GameViewAccess
    {
        private readonly EditorWindow _view;
        private readonly PropertyInfo _selected;
        private readonly object _group;
        private readonly Type _sizeType, _sizeKind;

        private GameViewAccess(EditorWindow view, PropertyInfo selected, object group, Type sizeType, Type sizeKind)
        { _view = view; _selected = selected; _group = group; _sizeType = sizeType; _sizeKind = sizeKind; }

        public static GameViewAccess Open()
        {
            var asm = typeof(Editor).Assembly;
            var gvType = asm.GetType("UnityEditor.GameView");
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var sizeType = asm.GetType("UnityEditor.GameViewSize");
            var sizeKind = asm.GetType("UnityEditor.GameViewSizeType");
            if (gvType == null || sizesType == null || sizeType == null || sizeKind == null) return null;
            var view = EditorWindow.GetWindow(gvType, false, null, false);
            var selected = gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = single.GetProperty("instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var cgt = sizesType.GetProperty("currentGroupType", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object current = cgt == null ? GameViewSizeGroupType.Standalone : cgt.GetGetMethod(true).IsStatic ? cgt.GetValue(null) : cgt.GetValue(sizes);
            var group = sizesType.GetMethod("GetGroup")?.Invoke(sizes, new[] { current });
            if (view == null || selected == null || group == null) return null;
            return new GameViewAccess(view, selected, group, sizeType, sizeKind);
        }

        public int SelectedIndex
        {
            get => (int)_selected.GetValue(_view);
            set { _selected.SetValue(_view, value); _view.Repaint(); }
        }

        public int AddFixed(int w, int h, string name)
        {
            var ctor = _sizeType.GetConstructor(new[] { _sizeKind, typeof(int), typeof(int), typeof(string) });
            var size = ctor.Invoke(new object[] { Enum.ToObject(_sizeKind, 1), w, h, name });   // 1 = FixedResolution
            _group.GetType().GetMethod("AddCustomSize").Invoke(_group, new[] { size });
            return (int)_group.GetType().GetMethod("GetTotalCount").Invoke(_group, null) - 1;
        }

        public void RemoveCustom(int index)
        {
            int builtin = (int)_group.GetType().GetMethod("GetBuiltinCount").Invoke(_group, null);
            _group.GetType().GetMethod("RemoveCustomSize").Invoke(_group, new object[] { index - builtin });
        }
    }
}
#endif
