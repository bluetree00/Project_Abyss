#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 챕터 방 문 실측(사용자 「문도 제대로 된 오브젝트로 — 지금은 얇은 판이 움직이는 느낌」).
/// 전투방(문이 닫힌 뒤)에서 봉인 문(SealDoor)마다 방 안쪽에서 정면 · 비스듬히 두 장을 찍고 개구부 · 문 배율 · 두께를 적는다.
/// 결과: Temp/ui_shots/Door_*.png · Temp/room_door_probe.txt · 로그 「[DoorProbe] 끝」. ⚠️ 플레이 중 · 전투방.
/// </summary>
public static class RoomDoorProbeEditor
{
    private const int W = 960, H = 540;

    [MenuItem("RelicFairy/Debug/10-02 방 문 실측 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("방 문 실측\n");
        await UniTask.Delay(2500, ignoreTimeScale: true);   // 낙하 연출이 끝나게
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name != "SealDoor" || !t.gameObject.activeInHierarchy) continue;
            var gate = t.parent;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var mf = t.GetComponentInChildren<MeshFilter>();
            string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
            sb.AppendLine($"{gate?.name}/{t.name}: 배율 {t.localScale} · 월드 크기 {b.size} · 메시 {mesh} · 재질 {(rs[0].sharedMaterial != null ? rs[0].sharedMaterial.name : "-")}");

            // 방 안쪽 = 문 앞에서 방 중심 쪽(게이트 +Z 또는 −Z 중 방 쪽)
            Vector3 fwd = gate != null ? gate.forward : t.forward;
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player != null && Vector3.Dot(player.transform.position - b.center, fwd) < 0f) fwd = -fwd;
            Vector3 eye1 = b.center + fwd * Mathf.Max(6f, b.size.y * 1.1f) + Vector3.up * 1.0f;
            Vector3 eye2 = b.center + (fwd + (gate != null ? gate.right : Vector3.right) * 0.8f).normalized * Mathf.Max(6f, b.size.y * 1.1f) + Vector3.up * 2.5f;
            Render($"Temp/ui_shots/Door_{n}_front.png", eye1, b.center);
            Render($"Temp/ui_shots/Door_{n}_side.png", eye2, b.center);
            n++;
            if (n >= 3) break;
        }
        sb.AppendLine($"문 {n}");
        File.WriteAllText("Temp/room_door_probe.txt", sb.ToString());
        Debug.Log("[DoorProbe] 끝\n" + sb);
    }

    private static void Render(string path, Vector3 pos, Vector3 target)
    {
        var go = new GameObject("~DoorProbeCam");
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        try
        {
            go.transform.position = pos;
            go.transform.LookAt(target);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
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
