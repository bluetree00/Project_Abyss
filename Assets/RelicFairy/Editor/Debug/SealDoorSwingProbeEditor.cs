#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-06 봉인 문 젖힘 실측(사용자 「문짝 연출이 잘 보여 주는 구도가 된다면 좋긴 해」).
/// 전투방(나무문 팔레트)에서 출구 문을 닫힌 상태로 맞춘 뒤 젖혀 열고(클리어 연출과 같은 함수), 게임 카메라 화면과 방 안 근접 화면을
/// 0 · 0.3 · 0.6 · 1.0초에 찍는다. 이어 다시 쾅 닫히는 장면도 찍는다. 실행 동안 플레이어 무적.
/// 결과: Temp/ui_shots/Swing_{game|close}_{open|close}_{t}.png · Temp/seal_door_swing.txt · 로그 「[DoorSwing] 끝」.
/// </summary>
public static class SealDoorSwingProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int W = 960, H = 540;

    [MenuItem("RelicFairy/Debug/10-06 봉인 문 젖힘 실측 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("봉인 문 젖힘 실측\n");
        try
        {
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(30f);
            var flow = Object.FindFirstObjectByType<RunFlowController>();
            if (flow == null) { sb.AppendLine("RunFlowController 없음"); return; }
            var gates = typeof(RunFlowController).GetField("_gates", Inst)?.GetValue(flow) as IList;
            if (gates == null || gates.Count == 0) { sb.AppendLine("출구 문 없음"); return; }

            object view = null;
            foreach (var g in gates)
            {
                if (g == null) continue;
                var hinges = g.GetType().GetField("hinges").GetValue(g) as Transform[];
                if (hinges != null) { view = g; break; }
            }
            if (view == null) { sb.AppendLine("젖힘 문 없음(이 방 팔레트가 Swing이 아니다)"); return; }

            var vt     = view.GetType();
            var door   = (Transform)vt.GetField("door").GetValue(view);
            float outw = (float)vt.GetField("outward").GetValue(view);
            var hs     = (Transform[])vt.GetField("hinges").GetValue(view);
            sb.AppendLine($"문 {door.parent?.name}/{door.name} · 경첩 {hs.Length} · 바깥 {outw}");

            // 방 안 근접 카메라 자리 — 문 앞 방 안쪽 9 m · 위 4.5 m
            Vector3 outWorld = door.parent != null ? door.parent.forward * outw : door.forward;
            Vector3 eye      = door.position - outWorld * 9f + Vector3.up * 4.5f;

            var raise = typeof(RunFlowController).GetMethod("SealDoorRaiseAsync", Inst);
            var drop  = typeof(RunFlowController).GetMethod("SealDoorDropAsync", Inst);

            // 닫힌 상태로 맞추고 연다
            vt.GetField("swingK").SetValue(view, 0f);
            vt.GetField("opening").SetValue(view, false);
            SealDoorFit.SetSwing(hs, outw, 0f);
            await UniTask.Delay(300, ignoreTimeScale: true);
            vt.GetField("opening").SetValue(view, true);
            raise.Invoke(flow, new[] { view });
            await ShootSeries("open", door, eye, sb);

            // 다시 쾅 닫기
            vt.GetField("opening").SetValue(view, false);
            drop.Invoke(flow, new[] { view, (object)true });
            await ShootSeries("close", door, eye, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/seal_door_swing.txt", sb.ToString());
            Debug.Log("[DoorSwing] 끝\n" + sb);
        }
    }

    private static async UniTask ShootSeries(string tag, Transform door, Vector3 eye, StringBuilder sb)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        float start = Time.unscaledTime;
        foreach (float t in new[] { 0f, 0.3f, 0.6f, 1.0f })
        {
            float wait = start + t - Time.unscaledTime;
            if (wait > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/Swing_game_{tag}_{t:0.0}.png");
            Close(door, eye, $"Temp/ui_shots/Swing_close_{tag}_{t:0.0}.png");
            sb.AppendLine($"{tag} {t:0.0}초: 경첩 각 {LeafAngles(door)}");
        }
        await UniTask.Delay(400, ignoreTimeScale: true);
    }

    private static string LeafAngles(Transform door)
    {
        var s = new StringBuilder();
        for (int i = 0; i < door.childCount; i++)
        {
            var c = door.GetChild(i);
            if (c.name.StartsWith("Hinge")) s.Append($"{c.name} {Mathf.DeltaAngle(0f, c.localEulerAngles.y):0} ");
        }
        return s.ToString();
    }

    private static void Close(Transform door, Vector3 eye, string path)
    {
        var go  = new GameObject("~SwingProbeCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt  = new RenderTexture(W, H, 24);
        try
        {
            go.transform.position = eye;
            go.transform.LookAt(door.position);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask = ~(1 << ui);
            cam.fieldOfView   = 55f;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
#endif
