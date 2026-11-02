#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q31 사용자 「화룡 메테오의 판정과 가이드라인이 제대로 안 돼 있다 — 터지는 자리에 같은 리소스 폭발을 범위 크기에 맞게(예고 원 = 판정 = 폭발)」.
/// 화룡방(잠든 채 · 입구 쪽)에서 플레이어 앞 7 m 자리에 게임과 같은 길로 메테오 하나씩(반경 1 · 2 · 4칸): 예고 원(DragonThreat.WarnCircle)을 깔고
/// DragonThreat.SpawnMeteor(화염구 비 SO의 Meteor.prefab · 몸통/땅 배율 · 낙하 0.6초)를 띄워 착탄 순간과 뒤를 찍는다.
/// 판정 원(반경 r + 0.5칸)을 화면 좌표로 적어 두면 사진 위에 겹쳐 그린다. 예고 원은 게임처럼 착탄 때 번쩍인 뒤 걷힌다.
/// 결과: Temp/meteor_fit/r{r}_{시점}.png · circle.txt · 로그 「[MeteorFit] 끝」.
/// </summary>
public static class MeteorFitShotProbeEditor
{
    private static readonly float[] ShotsAfterImpact = { -0.25f, 0f, 0.12f, 0.3f, 0.6f };

    [MenuItem("RelicFairy/Boss/11-02 화룡 메테오 — 예고 원 · 폭발 크기 사진 (화룡방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorFit] 화룡방 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "meteor_fit");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("화룡 메테오 — 예고 원 · 폭발 크기\n");
        var circles = new StringBuilder();
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<DragonFireballRainPatternSO>("Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/SO/DK_FireballRainPattern.asset");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main;
            if (so == null || so.FireballPrefab == null || player == null || cam == null) { sb.AppendLine("SO · 플레이어 · 카메라 없음"); return; }
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 land = player.transform.position + fwd * 7f;
            if (Physics.Raycast(land + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) land = hit.point;
            land.x = Mathf.Round(land.x); land.z = Mathf.Round(land.z);   // 칸 가운데(1 m 격자)
            land += Vector3.up * 0.05f;
            float fall = so.ImpactDelay;
            sb.AppendLine($"착탄 {land} · 낙하 {fall:0.00}초 · 화면 {Screen.width}×{Screen.height}");

            foreach (int r in new[] { 1, 2, 4 })
            {
                // 예고 원 — 게임과 같은 DragonThreat.WarnCircle(낙하 동안은 다 찬 상태)
                var warn = DragonThreat.WarnCircle(land, r, heavy: r >= 3);
                DragonThreat.WarnProgress(warn, 1f);
                float body = r >= 3 ? DragonThreat.BigBodyScale : DragonThreat.BodyScale;
                var go = DragonThreat.SpawnMeteor(so.FireballPrefab, land, so.FireballScale * body, so.FireballScale * DragonThreat.MeteorScale(r), fall);
                float t0 = Time.realtimeSinceStartup;
                float R = r + 0.5f;
                circles.Append($"r{r} R {R:0.0}:");
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    Vector3 sp = cam.WorldToScreenPoint(land + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R);
                    circles.Append($" {sp.x:0},{Screen.height - sp.y:0}");
                }
                circles.AppendLine();
                foreach (float s in ShotsAfterImpact)
                {
                    float at = fall + s;
                    while (Time.realtimeSinceStartup - t0 < at) await UniTask.Yield();
                    if (warn != null && s >= 0f) DragonThreat.ImpactCircle(ref warn);   // 게임처럼 착탄 때 번쩍 → 0.2초 뒤 걷힌다
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"r{r}_{(s < 0 ? "pre" : "t")}{Mathf.Abs(s) * 100:000}.png"));
                    await UniTask.Yield();
                }
                DragonThreat.DropCircle(ref warn);
                await UniTask.Delay(1500, DelayType.Realtime);
                if (go != null) BossEffectPool.Release(go);
                sb.AppendLine($"  r{r}: 판정 반경 {R:0.0} m · 몸통 ×{so.FireballScale * body:0.00} · 땅 ×{so.FireballScale * DragonThreat.MeteorScale(r):0.00}");
                await UniTask.Delay(500, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "fit.txt"), sb.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "circle.txt"), circles.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorFit] 끝 — {dir}");
        }
    }
}
#endif
