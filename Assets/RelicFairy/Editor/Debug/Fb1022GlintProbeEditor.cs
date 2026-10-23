#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 몬스터 공격 예고 반짝임 리소스 비교(사용자 「몬스터 근처 위에 폭발 같은 이펙트 리소스가 문제」).
/// 지금 반짝임(RunFxSlot.AttackGlint = Hovl Yellow Flash: Glow · Smoke · Smoke2 · Sparks)을 실제 재생 길(RunFx.Play)로 띄우고
/// 층을 꺼 가며(A 지금 · B 연기 끔 · C 빛만 · D 빛만 ×0.6) 0.05 · 0.12 · 0.25초에 찍는다. 자리 = 플레이어 옆 키 1.6 m 몬스터 머리.
/// 사진: Temp/ui_shots/GLINT22_*.png · 결과: Temp/fb1022_glint.txt · 로그 「[Fb1022Glint] 끝」.
/// </summary>
public static class Fb1022GlintProbeEditor
{
    private static readonly Color Tint = new(1f, 0.95f, 0.8f, 1f);   // MonsterAttackGlint와 같은 색

    [MenuItem("RelicFairy/Debug/10-22 공격 예고 반짝임 리소스 비교 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Fb1022Glint] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("공격 예고 반짝임 리소스 비교\n");
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;
        try
        {
            if (!RunFx.IsReady) await RunFx.LoadAsync();
            PlayerController player = null;
            Camera cam = null;
            for (int i = 0; i < 60 && (player == null || cam == null); i++)   // 런이 막 열린 직전이면 기다린다
            {
                player = GameRunBootstrapper.Instance?.Run?.Player;
                if (player == null) player = Object.FindFirstObjectByType<PlayerController>();
                cam = Camera.main;
                if (cam == null)
                    foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                        if (c.isActiveAndEnabled && c.targetTexture == null) { cam = c; break; }
                if (player == null || cam == null) await UniTask.Delay(500, DelayType.Realtime);
            }
            sb.AppendLine($"  플레이어 {(player != null ? player.name : "없음")} · 카메라 {(cam != null ? cam.name : "없음")} · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            if (player == null || cam == null) return;
            Vector3 head = player.transform.position + cam.transform.right * 1.6f + Vector3.up * 1.4f;
            head += (cam.transform.position - head).normalized * 0.5f;   // MonsterAttackGlint — 카메라 쪽으로 0.5

            (string name, string[] off, float size)[] variants =
            {
                ("A_now",       new string[0],                            1f),
                ("B_noSmoke",   new[] { "Smoke", "Smoke2" },              1f),
                ("C_glowOnly",  new[] { "Smoke", "Smoke2", "Sparks" },    1f),
                ("D_glowSmall", new[] { "Smoke", "Smoke2", "Sparks" },    0.6f),
            };
            foreach (var v in variants)
            {
                var go = RunFx.Play(RunFxSlot.AttackGlint, head, 1f * v.size, Tint);
                if (go == null) { sb.AppendLine($"  {v.name}: 재생 안 됨"); continue; }
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (System.Array.IndexOf(v.off, t.name) >= 0) t.gameObject.SetActive(false);
                int systems = go.GetComponentsInChildren<ParticleSystem>(false).Length;
                sb.AppendLine($"  {v.name}: 켜진 입자계 {systems} · 배율 {go.transform.lossyScale.x:0.##} · 끈 층 {string.Join(",", v.off)}");
                foreach (float at in new[] { 0.05f, 0.12f, 0.25f })
                {
                    await UniTask.Delay((int)(at * 1000), DelayType.Realtime);
                    await Shot($"GLINT22_{v.name}_{at:0.00}");
                }
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);   // 풀로 돌아갈 때 원래대로
                await UniTask.Delay(900, DelayType.Realtime);
            }
            Vector2 sp = cam.WorldToScreenPoint(head);
            sb.AppendLine($"  반짝임 화면 자리 x {sp.x:0} · y {sp.y:0}(사진 자르기용)");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            File.WriteAllText("Temp/fb1022_glint.txt", sb.ToString());
            Debug.Log("[Fb1022Glint] 끝\n" + sb);
        }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
