#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 사용자 「화룡 메테오가 다 작고 가까이부터 와서 아쉽다 — 전체적으로 크고 · 폭발도 제대로 · 멀리서 강하게」 — 후보 비교 촬영.
///   · 떨어지는 몸통 후보: 착탄 자리 위 · 카메라 먼 쪽에서 0.6초(화염구 비 ImpactDelay) 동안 가속해 내리꽂는다(스스로 움직이는 스크립트는 끔) — 세 번 찍는다.
///   · 착탄 후보: 배율 1 보이는 반경을 먼저 재고 판정 반폭 2.5 m(흩뿌림 2칸)에 맞춘 배율로 다시 띄워 세 번 찍는다.
///   · 지금 운석(Meteor.prefab ×0.52 — 몸통 · 착탄이 한 덩어리)도 같은 자리에서 찍어 견준다.
/// 촬영 동안 캔버스는 숨긴다(허브 패널이 가렸다 — 10-15). 결과: Logs/boss_fight_sim/meteor_look_*/look.txt · 사진.
/// </summary>
public static class DragonMeteorLookEditor
{
    private const float Fall     = 0.6f;   // DK_FireballRainPattern._impactDelay
    private const float Judge    = 2.5f;   // 흩뿌림 2칸 판정 반폭((2 + 0.5)칸 × 1 m)
    private const float StartUp  = 22f;    // 몸통 출발 — 착탄 자리 위
    private const float StartFar = 14f;    //            카메라 먼 쪽
    private const string Pack = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string Ss2  = Pack + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/";
    private const string Drg  = "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Effects/";

    private static readonly (string tag, string path, float scale)[] Bodies =
    {
        ("body_big10",   Ss2 + "Effect_10_MeteorStrike/Effect_10_Parts/Effect_10_BigMeteor.prefab", 1f),
        ("body_small10", Ss2 + "Effect_10_MeteorStrike/Effect_10_Parts/Effect_10_SmallMeteor.prefab", 1f),
        ("body_m49",     Ss2 + "Effect_49_FuryofNature/Effect_49_Parts/Effect_49_Meteor.prefab", 1f),
        ("body_fireball", Drg + "Fireball.prefab", 2f),
    };

    private static readonly (string tag, string path)[] Hits =
    {
        ("hit_big10",  Ss2 + "Effect_10_MeteorStrike/Effect_10_Parts/Effect_10_BigMeteorHitEffect.prefab"),
        ("hit_hovl2",  Pack + "Hovl Studio/AOE Magic spells Vol.1/Prefabs/Meteor hit 2.prefab"),
        ("hit_hovl1",  Pack + "Hovl Studio/AOE Magic spells Vol.1/Prefabs/Meteor hit.prefab"),
        ("hit_dragon", Drg + "Meteor Hit.prefab"),
        ("hit_m49",    Ss2 + "Effect_49_FuryofNature/Effect_49_Parts/Effect_49_MeteorHit.prefab"),
    };

    [MenuItem("RelicFairy/Boss/10-18 Dragon Meteor Look (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorLook] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"meteor_look_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder($"화룡 메테오 후보 — 몸통 {StartUp} m 위 · {StartFar} m 먼 쪽 → {Fall}초 가속 낙하 · 착탄 판정 반폭 {Judge} m에 맞춤\n");
        var hidden = new List<Canvas>();
        try
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 land = (player != null ? player.transform.position : Vector3.zero) + fwd * 7f;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.enabled && c.isRootCanvas) { c.enabled = false; hidden.Add(c); }

            foreach (var (tag, path, scale) in Bodies)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {tag}: 없음 {path}"); continue; }
                Vector3 from = land + fwd * StartFar + Vector3.up * StartUp;
                var go = UnityEngine.Object.Instantiate(prefab, from, Quaternion.LookRotation(land - from));
                go.transform.localScale = Vector3.one * scale;
                StopSelfMove(go);
                float t0 = Time.realtimeSinceStartup;
                int shot = 0;
                float[] at = { 0.3f, 0.45f, 0.57f };
                while (true)
                {
                    float k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / Fall);
                    go.transform.position = Vector3.LerpUnclamped(from, land, k * k);   // 가속 — 내리꽂는다
                    if (shot < at.Length && k * Fall >= at[shot])
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_{at[shot++] * 100:00}.png"));
                    if (k >= 1f) break;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"  {tag} ×{scale:0.0} — 찍음({string.Join(" · ", at)}초)");
                UnityEngine.Object.Destroy(go);
                await UniTask.Delay(700, DelayType.Realtime, cancellationToken: ct);
            }

            foreach (var (tag, path) in Hits)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {tag}: 없음 {path}"); continue; }
                var before = EffectReachProbe.Snapshot();
                var probe = Spawn(prefab, land, fwd, 1f);
                var r = await EffectReachProbe.ObserveAsync(before, land, fwd, 1.5f, ct);
                UnityEngine.Object.Destroy(probe);
                float reach = Mathf.Max(r.Radius, r.RadiusMax);
                float scale = reach > 0.01f ? Judge / reach : 1f;
                await UniTask.Delay(500, DelayType.Realtime, cancellationToken: ct);
                var go = Spawn(prefab, land, fwd, scale);
                await ShotsAsync(dir, tag, new[] { 0.15f, 0.5f, 1.0f }, ct);
                UnityEngine.Object.Destroy(go);
                sb.AppendLine($"  {tag}: 배율 1 반경 {r.Radius:0.00} · 최대 {r.RadiusMax:0.00} · 정점 {r.PeakAt:0.00}초 → ×{scale:0.00} — 찍음");
                await UniTask.Delay(600, DelayType.Realtime, cancellationToken: ct);
            }

            // 지금 운석 — 몸통 · 착탄이 한 덩어리(2칸 배율 0.52)
            var now = AssetDatabase.LoadAssetAtPath<GameObject>(Drg + "Meteor.prefab");
            if (now != null)
            {
                var go = Spawn(now, land, fwd, 0.52f);
                await ShotsAsync(dir, "now_meteor", new[] { 0.2f, 0.45f, 0.75f, 1.2f }, ct);
                UnityEngine.Object.Destroy(go);
                sb.AppendLine("  now_meteor ×0.52 — 찍음");
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            File.WriteAllText(Path.Combine(dir, "look.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorLook] 끝 — {dir}");
        }
    }

    // 10-19 ⑬ 서약 초신성 후보 — 판정 3.5 m에 맞춘 배율로 0.1 · 0.25 · 0.5 · 0.9초(정점이 빠른 것도 잡게)
    private const string SpellsFx = Pack + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/";
    private static readonly (string tag, string path)[] Novas =
    {
        // 1차(10-19 01:15) — 지금 것 · Hit 18 · Spell_Light_3/4 · Flash 18 · Explosion_Light: 구름이거나 작다
        ("nova_sunburst",    Ss2 + "Effect_47_ChildrenOfUniverse/Effect_47_Parts/Effect_47_SunBurstImpact.prefab"),
        ("nova_holyhit05",   Ss2 + "Effect_05_HolyEmission/Effect_05_Parts/Effect_05_Hit.prefab"),
        ("nova_inab_holy",   Pack + "INab Studio/Vfx Assets/Character Effects/Effect Prefabs/Holy.prefab"),
        ("nova_exp_light",   SpellsFx + "Projectiles/Explosion/Explosion_Light_LWRP.prefab"),
    };

    [MenuItem("RelicFairy/Boss/10-19 Covenant Nova Look (Play)")]
    private static void NovaLook()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorLook] 플레이 모드(테스트 허브)에서"); return; }
        NovaLookAsync().Forget();
    }

    private static async UniTaskVoid NovaLookAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"nova_look_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("서약 초신성 후보 — 판정 3.5 m에 맞춤 · 0.1 · 0.25 · 0.5 · 0.9초\n");
        try
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 at = (player != null ? player.transform.position : Vector3.zero) + fwd * 7f;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            foreach (var (tag, path) in Novas)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {tag}: 없음"); continue; }
                var before = EffectReachProbe.Snapshot();
                var probe = Spawn(prefab, at, fwd, 1f);
                var r = await EffectReachProbe.ObserveAsync(before, at, fwd, 1.5f, ct);
                UnityEngine.Object.Destroy(probe);
                float reach = Mathf.Max(r.Radius, r.RadiusMax);
                float scale = reach > 0.01f ? 3.5f / reach : 1f;
                await UniTask.Delay(500, DelayType.Realtime, cancellationToken: ct);
                var go = Spawn(prefab, at, fwd, scale);
                await ShotsAsync(dir, tag, new[] { 0.1f, 0.25f, 0.5f, 0.9f }, ct);
                UnityEngine.Object.Destroy(go);
                sb.AppendLine($"  {tag}: 배율 1 반경 {r.Radius:0.00} · 최대 {r.RadiusMax:0.00} · 정점 {r.PeakAt:0.00}초 → ×{scale:0.00}");
                await UniTask.Delay(600, DelayType.Realtime, cancellationToken: ct);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "look.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorLook] 끝 — {dir}");
        }
    }

    // 실전 촬영 — 화룡 전투 중(전투 시뮬과 같이) 새 몸통(Effect_10_BigMeteor)이 보이면 낙하 ~ 착탄을 네 장씩, 다섯 번(10-18 반입 뒤 확인)
    [MenuItem("RelicFairy/Boss/10-18 Dragon Meteor Live Shots (Play)")]
    private static void Live()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorLook] 플레이 모드(화룡 전투)에서"); return; }
        LiveAsync().Forget();
    }

    private static async UniTaskVoid LiveAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"meteor_live_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("화룡 메테오 실전 촬영 — 몸통이 보이면 0 · 0.2 · 0.45 · 0.75초\n");
        try
        {
            float until = Time.realtimeSinceStartup + 240f;
            int burst = 0;
            while (burst < 5 && Time.realtimeSinceStartup < until)
            {
                GameObject body = null;
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.gameObject.activeInHierarchy && (t.name == "Meteor" || t.name == "Meteor(Clone)") && t.position.sqrMagnitude > 1f) { body = t.gameObject; break; }   // 풀 = 이름 그대로 · 원점에 선 풀 견본은 뺀다(10-19 Meteor.prefab으로 되돌림)
                if (body == null) { await UniTask.Delay(100, DelayType.Realtime); continue; }
                sb.AppendLine($"  {burst}: 몸통 {body.transform.position} ×{body.transform.localScale.x:0.00}");
                await ShotsAsync(dir, $"live{burst}", new[] { 0.01f, 0.2f, 0.45f, 0.75f }, default);
                burst++;
                await UniTask.Delay(2500, DelayType.Realtime);
            }
            if (burst == 0) sb.AppendLine("240초 안에 몸통이 안 보였다");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "live.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorLook] 실전 끝 — {dir}");
        }
    }

    private static GameObject Spawn(GameObject prefab, Vector3 at, Vector3 fwd, float scale)
    {
        var go = UnityEngine.Object.Instantiate(prefab, at + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
        go.transform.localScale = Vector3.one * scale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        StopSelfMove(go);
        return go;
    }

    /// <summary>이펙트가 저 혼자 움직이는 스크립트(ObjectMoveDestroy · VariousTranslateMove 등)를 끈다 — 자리는 코드가 정한다.</summary>
    private static void StopSelfMove(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
    }

    private static async UniTask ShotsAsync(string dir, string tag, float[] at, System.Threading.CancellationToken ct)
    {
        float t = 0f;
        foreach (float s in at)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(s - t), DelayType.Realtime, cancellationToken: ct);
            t = s;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_{s * 100:000}.png"));
        }
        await UniTask.Delay(300, DelayType.Realtime, cancellationToken: ct);
    }
}
#endif
