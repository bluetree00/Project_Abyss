#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중 · 허브 가능] 10-13 「이펙트 = 판정」 — 정예 접두어(박격 · 가시) · 황소(내려찍기 · 도약 · 벽) 판정 순간 이펙트(RunFx 폭발 · 고리)를
/// 배율별로 띄워 <see cref="EffectReachProbe"/>로 보이는 반경(보이는 양 90% 도달)을 잰다 → 배율 1당 m를 구해 판정 반경에 맞출 배율을 정한다.
/// RunFx는 풀에서 같은 인스턴스를 다시 쓴다 — 재생 전 스냅샷에서 돌려받은 인스턴스의 입자계를 빼야 잡힌다(4b 측정에서 두 번째가 0 m였다).
/// 결과: Logs/boss_fight_sim/fx_reach_*.txt · 로그 「[BossFxReach] 끝」.
/// </summary>
internal static class BossFxReachEditor
{
    private static readonly float[] Scales = { 0.5f, 1f, 2f, 3f };
    private static readonly RunFxSlot[] Slots = { RunFxSlot.Burst, RunFxSlot.Ring };
    private static readonly Color Dust = new(0.62f, 0.5f, 0.38f, 1f);

    [MenuItem("RelicFairy/Boss/FX Reach - RunFx Burst · Ring (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossFxReach] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    // 화룡 원소 합주 — 시뮬 안 측정은 원 여러 개(불 8 · 얼음 6)의 입자가 섞여 ×3.4로 부풀었다 → 이펙트 하나씩 따로 띄워 판정(원 반경 · 줄 반폭)과 견준다(10-13)
    private const string ConcertAsset = "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/SO/DragonElementConcertPattern.asset";

    [MenuItem("RelicFairy/Boss/FX Reach - Dragon Concert (Play)")]
    private static void RunConcert()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossFxReach] 플레이 중에만"); return; }
        RunConcertAsync().Forget();
    }

    private static async UniTaskVoid RunConcertAsync()
    {
        var sb = new StringBuilder("화룡 원소 합주 — 이펙트 하나의 보이는 반경(보이는 양 90% 도달 · 정점 / 구간 최대) ÷ 판정 · 0.8~1.25 기준(10-13 84)\n");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"fx_reach_concert_{DateTime.Now:MMdd_HHmmss}.txt");
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<DragonElementConcertPatternSO>(ConcertAsset);
            if (so == null) { sb.AppendLine("자산 없음: " + ConcertAsset); return; }
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Vector3 at = player != null ? player.transform.position + player.transform.forward * 4f : Vector3.zero;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            var items = new (string name, GameObject prefab, float scale, float judge)[]
            {
                ("얼음(원)", so.IceVfxPrefab,     so.IceVfxScale,     so.IceDiscRadius),
                ("번개(줄 반폭)", so.ThunderVfxPrefab, so.ThunderVfxScale, so.ThunderWidth * 0.5f),
                ("불(원)",   so.FireVfxPrefab,    so.FireVfxScale,    so.FireDiscRadius),
            };
            foreach (var (name, prefab, scale, judge) in items)
            {
                if (prefab == null) { sb.AppendLine($"  {name}: 프리팹 없음"); continue; }
                foreach (float mul in new[] { 1f, 2f })
                foreach (bool noSnow in new[] { false, true })
                {
                    float s = scale * mul;
                    var before = EffectReachProbe.Snapshot();
                    var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
                    go.transform.localScale = Vector3.one * s;
                    // 얼음의 넓은 반경이 흩날리는 눈(Snow) 입자 몫인지 — 몸통만 따로 잰다
                    var snow = noSnow ? go.transform.Find("Snow") : null;
                    if (noSnow && snow == null) { UnityEngine.Object.Destroy(go); continue; }
                    if (snow != null) snow.gameObject.SetActive(false);
                    var r = await EffectReachProbe.ObserveAsync(before, at, Vector3.forward, 1.5f, ct);
                    UnityEngine.Object.Destroy(go);
                    sb.AppendLine($"  {name,-8} 배율 {s:0.00}{(noSnow ? " · 눈 끔" : "")}: 반경 {r.Radius:0.00} m(×{r.Radius / judge:0.00}) · 최대 {r.RadiusMax:0.00} m(×{r.RadiusMax / judge:0.00}) · 판정 {judge:0.00} m · 정점 {r.PeakAt:0.00}초 · 입자계 {r.Systems}");
                    await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
                }
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossFxReach] 합주 끝 — {path}");
        }
    }

    // 리치 봉인 완성(SealComplete · Effect_38 GloryBoundary) — 흰 빛줄기 · 파편 구름이 화면을 덮었다(10-14 전주기 5회차 재확인) →
    // 원본 · 연기만 끔 · 다듬은 판(LichPatternUtil.PlaySealComplete) · 고리만 을 플레이어 발밑에 띄워 0.4 · 1.2 · 2.2초에 찍는다.
    [MenuItem("RelicFairy/Boss/FX Shot - Lich Seal Complete (Play)")]
    private static void RunSealShot()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossFxReach] 플레이 중에만"); return; }
        RunSealShotAsync().Forget();
    }

    private static async UniTaskVoid RunSealShotAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"seal_fx_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        try
        {
            await LichVfx.LoadAsync();
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Vector3 at = player != null ? player.transform.position + Vector3.up * 0.05f : Vector3.zero;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            var cases = new (string tag, string[] off)[]
            {
                ("a_raw",     Array.Empty<string>()),
                ("b_nosmoke", new[] { "Effect_38_SmokeField" }),
                ("c_trim",    null),   // 실제 재생기
                ("d_rings",   new[] { "Effect_38_SmokeField", "Effect_38_ParticlesField_3", "Effect_38_ParticlesField", "Effect_38_ParticlesField_2" }),
            };
            foreach (var (tag, off) in cases)
            {
                GameObject go;
                if (off == null) go = LichPatternUtil.PlaySealComplete(at);
                else
                {
                    go = LichVfx.Play(LichVfxSlot.SealComplete, at, Quaternion.identity);
                    // 풀 재사용 인스턴스는 앞 판에서 꺼진 자식이 남는다 — 판마다 켜고 끈다
                    if (go != null)
                        foreach (var t in go.GetComponentsInChildren<Transform>(true))
                            if (t != go.transform) t.gameObject.SetActive(Array.IndexOf(off, t.name) < 0);
                }
                if (go == null) { Debug.LogWarning($"[BossFxReach] {tag} 재생 실패(칸 비었음)"); continue; }
                float waited = 0f;
                foreach (float s in new[] { 0.4f, 1.2f, 2.2f })
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(s - waited), ignoreTimeScale: true, cancellationToken: ct);
                    waited = s;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_{s * 10:00}.png"));
                }
                await UniTask.Delay(TimeSpan.FromSeconds(4.5f - waited), ignoreTimeScale: true, cancellationToken: ct);   // 수명 3초 뒤 풀로
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning("[BossFxReach] 봉인 완성 촬영 예외: " + e); }
        finally { Debug.Log($"[BossFxReach] 봉인 완성 촬영 끝 — {dir}"); }
    }

    // 10-14 남은 정예 이펙트 — 후보를 배율 1로 띄워 보이는 반경(90%) · 앞길이를 재고, 맞출 판정 크기 배율로 다시 띄워 사진
    private const string FxRoot = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string BhFx = "Assets/RelicFairy/Characters/Monster/Monster/Beholder/Effect/";
    private const string BkFx = "Assets/RelicFairy/Characters/Monster/Monster/BlackKnight/Effect/";
    private static readonly (string tag, string path, float judge)[] FitBeholder =
    {
        // 10-15 — Spell_Storm_4 · 8은 셰이더가 깨져 마젠타 · 청록 다각형(그래서 안 쓰였다) → 리치가 쓰는 이펙트(전기 마법진 · 번개 장판 · 어둠 오라)
        ("electro", FxRoot + "Hovl Studio/Magic circles/Prefabs/Magic circle electro.prefab", 5f),
        ("lfield",  FxRoot + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_52_LightningField/Effect_52_LightningField.prefab", 5f),
        ("dark",    FxRoot + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Auras/Aura_Dark_LWRP.prefab", 5f),
    };
    private static readonly (string tag, string path, float judge)[] FitBlackKnight =
    {
        ("prick2",  BkFx + "Prick 2.prefab", 2.5f),          // 찌르기 — 앞길이 2.5 m
        ("slash7",  BkFx + "Sword Slash 7.prefab", 2.2f),    // 내려찍기 — 반경 2.2 m
        ("slash14", BkFx + "Sword Slash 14.prefab", 2.2f),
        ("slash15", BkFx + "Sword Slash 15.prefab", 2.2f),
        ("combo6",  BkFx + "Sword Slash Combo 6.prefab", 2.2f),
    };

    [MenuItem("RelicFairy/Boss/FX Fit - Beholder Field (Play)")]
    private static void FitBh() { if (EditorApplication.isPlaying) FitAsync("beholder", FitBeholder).Forget(); }

    // 화룡 메테오(10-14 개편) — 크기마다 배율을 맞출 기준: 배율 1 보이는 반경 ÷ 반경 1칸 원 반폭 1.5 m
    private static readonly (string tag, string path, float judge)[] FitDragon =
    {
        ("meteor", "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Effects/Meteor.prefab", 1.5f),
        ("bite",   "Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Art/Effects/Meteor.prefab", 2.35f),   // 물기 화염구 — 판정 반경 2.35 m(10-15)
    };

    [MenuItem("RelicFairy/Boss/FX Fit - Dragon Meteor (Play)")]
    private static void FitDr() { if (EditorApplication.isPlaying) FitAsync("dragon_meteor", FitDragon).Forget(); }

    [MenuItem("RelicFairy/Boss/FX Fit - BlackKnight (Play)")]
    private static void FitBk() { if (EditorApplication.isPlaying) FitAsync("blackknight", FitBlackKnight).Forget(); }

    private static async UniTaskVoid FitAsync(string name, (string tag, string path, float judge)[] items)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"fx_fit_{name}_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder($"{name} 이펙트 맞춤 — 배율 1 보이는 반경(90% · 정점/구간 최대) · 앞길이 → 판정 크기 배율\n");
        try
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 at = (player != null ? player.transform.position : Vector3.zero) + fwd * 6f;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            foreach (var (tag, path, judge) in items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {tag}: 없음 {path}"); continue; }
                float perUnit = 0f;
                foreach (bool fitted in new[] { false, true })
                {
                    float scale = !fitted ? 1f : (perUnit > 0.01f ? judge / perUnit : 1f);
                    var before = EffectReachProbe.Snapshot();
                    var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(fwd));
                    go.transform.localScale = Vector3.one * scale;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                    if (fitted) ShotLater(Path.Combine(dir, $"{tag}_x{scale:0.00}.png"), 0.6f, ct);
                    var r = await EffectReachProbe.ObserveAsync(before, at, fwd, 1.5f, ct);
                    UnityEngine.Object.Destroy(go);
                    float reach = Mathf.Max(r.Radius, r.RadiusMax);
                    if (!fitted) perUnit = reach;
                    sb.AppendLine($"  {tag,-8} ×{scale:0.00}: 반경 {r.Radius:0.00} · 최대 {r.RadiusMax:0.00} · 앞 {r.Forward:0.00} · 옆 {r.Lateral:0.00} · 판정 {judge:0.0} m(×{reach / judge:0.00}) · 정점 {r.PeakAt:0.00}초 · {r.Names}");
                    await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
                }
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "fit.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossFxReach] 맞춤 끝 — {dir}");
        }
    }

    private static void ShotLater(string path, float at, System.Threading.CancellationToken ct)
    {
        UniTask.Void(async () =>
        {
            try { await UniTask.Delay(TimeSpan.FromSeconds(at), ignoreTimeScale: true, cancellationToken: ct); }
            catch (OperationCanceledException) { return; }
            ScreenCapture.CaptureScreenshot(path);
        });
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("RunFx 이펙트 보이는 반경 — 배율별(보이는 양 90% 도달 · 정점 / 구간 최대 · 10-13 84)\n");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"fx_reach_{DateTime.Now:MMdd_HHmmss}.txt");
        try
        {
            await RunFx.LoadAsync();
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Vector3 at = player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero;
            var ct = player != null ? player.GetCancellationTokenOnDestroy() : default;
            foreach (var slot in Slots)
            {
                float perUnit = 0f; int n = 0;
                foreach (float scale in Scales)
                {
                    var before = EffectReachProbe.Snapshot();
                    var go = RunFx.Play(slot, at, scale, Dust);
                    if (go == null) { sb.AppendLine($"  {slot} ×{scale}: 재생 실패(칸 비었음)"); continue; }
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) before.Remove(ps.GetInstanceID());   // 풀 재사용 인스턴스도 잰다
                    var r = await EffectReachProbe.ObserveAsync(before, at, Vector3.forward, 1.5f, ct);
                    sb.AppendLine($"  {slot,-6} ×{scale,-4}: 반경 {r.Radius:0.00} m · 최대 {r.RadiusMax:0.00} m · 정점 {r.PeakAt:0.00}초 · 입자계 {r.Systems} · {r.Names}");
                    if (r.RadiusMax > 0f) { perUnit += r.RadiusMax / scale; n++; }
                    await UniTask.Delay(1800, ignoreTimeScale: true, cancellationToken: ct);   // 끝나 풀로 돌아가게
                }
                if (n > 0) sb.AppendLine($"  → {slot} 배율 1당 약 {perUnit / n:0.00} m(구간 최대 기준) — 판정 반경 R이면 배율 ≈ R ÷ {perUnit / n:0.00}");
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossFxReach] 끝 — {path}");
        }
    }
}
#endif
