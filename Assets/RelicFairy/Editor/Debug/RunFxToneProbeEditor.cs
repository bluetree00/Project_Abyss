#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-11 이펙트 톤 점검(b0 몫 — 런 공용 · 서약 · 룬 · 거점) 촬영. 사용자 10-10 「이펙트의 무분별한 사용 — 룬 · 다음 챕터 포탈 아래 · 서약 등」.
/// 테스트 허브에서 런에 들어가 <b>적이 없는 방</b>(대기방 · 클리어한 방)에 서서 메뉴를 누른다. 플레이어 앞 같은 자리 · 같은 게임 카메라로
/// 대상 이펙트를 하나씩 재생해 정점 프레임을 찍는다(8장 — 10-11 2차 촬영 뒤 고친 것 확인 장면 VFX3_* · 08 = f9 기사 고리). 한 번 재생은 먼저 0.1초 간격으로
/// <b>보이는 양</b>(살아 있는 입자의 알파 × 넓이 합 — 켜진 렌더러만)을 재 가장 큰 순간을 찾고, 다시 재생해 그 순간에 찍는다.
/// 2차 도구는 「범위가 가장 큰 순간」을 정점으로 골라, 퍼지며 옅어지는 고리(SSEP31 충격파)가 수명 끝(1.24초)에서 골려 입자 0을 찍었다(VFX2_12).
/// 크기는 「살아 있는 입자」 기준 — 파티클 bounds는 「날 수 있는 최대」라 틀리게 잰다(SealFxCompareEditor 1차).
/// 게임과 같은 길로 띄운다: 런 공용 칸 = <see cref="RunFx"/>(색 곱 · 굴절층 끔), 서약 · 속성 = 프리팹 × 배율 · 입자 크기 모드 Hierarchy(<c>ElementVfxPlayer</c>와 같게).
/// 거점(BaseCamp)에서 누르면 포탈 소용돌이(PortalVortex)의 살아 있는 입자 수 · 원경 에너지체(EnergyOrb_*) 남은 수만 재고 한 장 찍는다 — 로드 뒤 20초 넘게 지나서 누를 것.
/// 서약 · 속성은 2차의 색 곱(<c>LichVfxScaleCache</c>) · 끌 자식 · 굴절층 끔 · 점광원 낮춤까지 재생기와 같게 입힌다.
/// 빛기둥(RunFx Pillar)은 「새 인스턴스」(풀 밖 · 같은 손질)와 「풀 재사용」(한 번 꺼냈다 돌려준 같은 인스턴스)을 따로 찍어 비교한다.
/// 결과: Temp/ui_shots/VFX3_*.png · Temp/vfx_tone_probe.txt · 로그 「[VfxToneProbe] 끝」.
/// </summary>
public static class RunFxToneProbeEditor
{
    // ── Constants ─────────────────────────────────────────────────
    private const string ShotDir      = "Temp/ui_shots";
    private const string ReportPath   = "Temp/vfx_tone_probe.txt";
    private const int    W = 1280, H = 720;
    private const float  Step         = 0.1f;   // 정점 찾기 간격(초)
    private const float  SearchWindow = 1.6f;   // 한 번 재생의 정점을 찾는 구간(초)
    private const float  LoopSettle   = 1.5f;   // 반복 이펙트는 이만큼 돈 뒤 찍는다
    private const float  Ahead        = 2.5f;   // 플레이어 앞(화면 위쪽) 자리
    private const float  Side         = 2.4f;   // 나란히 놓을 때 간격
    private const float  RewardLift   = 0.6f;   // 보상 구체 높이(RoomClearGate.effectHeightOffset)

    private const string CovSetPath   = "Assets/RelicFairy/Systems/Covenant/Assembly/CovenantVfxSet.asset";
    private const string RunSetPath   = "Assets/RelicFairy/Systems/Stage/RunGame/Fx/RunFxSet.asset";
    private const float  KnightRingScale = 1.6f;   // DeathKnightBossMonster.SwordCueRingScale(비공개)와 같은 값
    private const string RegistryPath = "Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset";

    private static readonly Color Gold      = new(1.00f, 0.82f, 0.30f, 1f);   // 탐욕 상자 · 전설 금빛 계열
    private static readonly Color RestInk   = new(1.00f, 0.72f, 0.38f, 1f);   // RestRoomController
    private static readonly Color SpringInk = new(0.55f, 0.88f, 1.00f, 1f);   // ChapterSpring
    private static readonly Color Radiant   = new(1.00f, 0.86f, 0.40f, 1f);   // RelicMemoryRecall 찬란

    // ── Static ────────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;

    /// <summary>찍을 장면 하나 — spawn이 띄운 것을 pooled(RunFx 풀) · owned(직접 만든 것)에 담는다.</summary>
    private sealed class Shot
    {
        public string name;
        public string note;
        public bool   loop;
        public Action<List<GameObject>, List<GameObject>> spawn;
        public Func<string> after;   // 찍은 뒤 txt에 덧붙일 한 줄(없으면 null)
    }

    // ── Public Methods ────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/10-11 런 이펙트 톤 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[VfxToneProbe] 플레이 모드에서(테스트 허브 → 런 · 적 없는 방)"); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token).Forget();
    }

    // ── Private Methods ───────────────────────────────────────────
    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(ShotDir);
        var sb = new StringBuilder($"런 이펙트 톤 촬영 {DateTime.Now:MM-dd HH:mm:ss}\n");
        var cam = Camera.main;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (cam == null) { Debug.LogWarning("[VfxToneProbe] 카메라 없음"); return; }
        if (player == null)
        {
            await BaseCampPortalAsync(cam, sb, ct);
            Finish(sb);
            return;
        }

        try
        {
            await RunFx.LoadAsync();
            if (!RunFx.IsReady) sb.AppendLine("⚠ 런 공용 이펙트 목록이 없다 — 런 칸은 비어 찍힌다");

            player.SetInvincible(120f);
            player.SetInputEnabled(false);
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, cancellationToken: ct);   // 멈춘 뒤 카메라가 자리 잡게

            Vector3 feet  = player.transform.position;
            Vector3 ahead = cam.transform.forward; ahead.y = 0f;
            ahead = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
            Vector3 side  = Vector3.Cross(Vector3.up, ahead);
            Vector3 spot  = feet + ahead * Ahead;
            sb.AppendLine($"플레이어 {feet} · 자리 = 플레이어 앞 {Ahead} m · 나란히 ±{Side} m · 카메라 {cam.transform.position}");
            sb.AppendLine("크기 = 살아 있는 입자(위치 ± 크기)와 입자 아닌 렌더러의 범위 · 정점 = 그 범위가 가장 큰 순간");
            sb.AppendLine();

            foreach (var shot in BuildShots(feet, spot, side, ahead, cam, sb))
            {
                ct.ThrowIfCancellationRequested();
                await ShootAsync(shot, cam, sb, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (player != null) player.SetInputEnabled(true);
        }
        Finish(sb);
    }

    private static void Finish(StringBuilder sb)
    {
        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log("[VfxToneProbe] 끝\n" + sb);
    }

    /// <summary>찍을 장면 목록(7장) — 10-11 2차 촬영(VFX2_*)에서 못 미친 3 · 약한 3을 고친 뒤 모습.</summary>
    private static List<Shot> BuildShots(Vector3 feet, Vector3 spot, Vector3 side, Vector3 ahead, Camera cam, StringBuilder sb)
    {
        var cov      = AssetDatabase.LoadAssetAtPath<CovenantVfxSet>(CovSetPath);
        var registry = AssetDatabase.LoadAssetAtPath<ElementVfxRegistry>(RegistryPath);
        var runSet   = AssetDatabase.LoadAssetAtPath<RunFxSetSO>(RunSetPath);
        if (cov == null || registry == null || runSet == null)
            sb.AppendLine($"⚠ 목록 누락 — 서약 {cov != null} · 속성 {registry != null} · 런 공용 {runSet != null}");

        Vector3 orb = spot + Vector3.up * RewardLift;
        var seenPillars = new HashSet<int>();   // 풀 재사용 확인 — 이 도구가 꺼냈다 돌려준 빛기둥 인스턴스
        GameObject knightWhite = null, knightDark = null;   // 기사 고리 — 찍은 뒤 입자 시작 색을 읽는다
        bool reused = false;

        return new List<Shot>
        {
            new Shot { name = "VFX3_01_pillar_fresh", note = "1 되찾은 기억 빛기둥(SSEP31 × 0.6 · 찬란 금) — 새 인스턴스(풀 밖 · RunFx와 같은 손질) · 플레이어 발밑",
                spawn = (pooled, owned) => owned.Add(FreshRunFx(runSet, RunFxSlot.Pillar, feet, 0.6f, Radiant)) },
            new Shot { name = "VFX3_02_pillar_reused", note = "1 같은 빛기둥 — RunFx 풀 재사용(한 번 꺼냈다 돌려준 인스턴스를 다시 꺼냄 · 게임 RelicMemoryRecall.cs:177 길)",
                spawn = (pooled, owned) =>
                {
                    var seed = RunFx.Play(RunFxSlot.Pillar, feet, 0.6f, Radiant);   // 시드 — 꺼냈다 바로 돌려준다
                    if (seed != null) seenPillars.Add(seed.GetInstanceID());
                    RunFx.Stop(ref seed);
                    var go = RunFx.Play(RunFxSlot.Pillar, feet, 0.6f, Radiant);
                    reused = go != null && seenPillars.Contains(go.GetInstanceID());
                    if (go != null) seenPillars.Add(go.GetInstanceID());
                    pooled.Add(go);
                },
                after = () => $"풀 재사용 확인 — 찍은 인스턴스가 앞서 꺼냈다 돌려준 것인가: {(reused ? "예" : "아니오")}" },
            new Shot { name = "VFX3_03_orb_dark_emblem", note = "2 · 6 기억의 빛(금덩이 · 금 고리 끔 + 진한 재채색) 왼쪽 금 · 가운데 하늘 / 오른쪽 심연 각성 문장(어둠 짙은 색 곱)", loop = true,
                spawn = (pooled, owned) =>
                {
                    pooled.Add(RunFx.PlayLoop(RunFxSlot.Orb, orb - side, 0.55f, Radiant));
                    pooled.Add(RunFx.PlayLoop(RunFxSlot.Orb, orb, 0.55f, SpringInk));
                    var emblem = registry != null ? registry.GetBurst(RuneElement.Dark) : null;
                    if (emblem != null) owned.Add(Prefab(emblem, spot + side, 1f, emblem.transform.rotation, element: true, tint: ElementPalette.Deep(RuneElement.Dark)));
                } },
            new Shot { name = "VFX3_04_ring_tints_curse", note = "4 · 5 박자 고리(진한 빛깔) 쉼터 주황 · 금 · 샘 하늘 / 맨 오른쪽 저주(Buff 6 진한 보라 × 0.7)",
                spawn = (pooled, owned) =>
                {
                    pooled.Add(RunFx.Play(RunFxSlot.Ring, spot - side * 1.5f, 1f, RestInk));
                    pooled.Add(RunFx.Play(RunFxSlot.Ring, spot - side * 0.5f, 1f, Gold));
                    pooled.Add(RunFx.Play(RunFxSlot.Ring, spot + side * 0.5f, 1f, SpringInk));
                    owned.Add(Cov(cov, "curse", spot + side * 1.5f));
                } },
            new Shot { name = "VFX3_05_cov_lastbreath", note = "3 마지막 숨결 모음(Buff 6 진한 금 × 0.4 · 플레이어 발밑) — 혼자 · 제 정점",
                spawn = (pooled, owned) => owned.Add(Cov(cov, "lastbreath", feet)) },
            new Shot { name = "VFX3_06_cov_lastbreath_save", note = "3 마지막 숨결 막음(성역 방패 × 0.6 · 플레이어 발밑) — 혼자 · 제 정점",
                spawn = (pooled, owned) => owned.Add(Cov(cov, "lastbreath_save", feet)) },
            new Shot { name = "VFX3_07_cov_stasis", note = "3 정지(Chain explosion · 9 m 섬광 끔 · 반경 3 m) — 혼자 · 제 정점",
                spawn = (pooled, owned) => owned.Add(Cov(cov, "stasis", spot, 3f)) },
            new Shot { name = "VFX3_08_knight_ring", note = "f9 기사 검 색 고리(CueSwordFloor와 같은 호출 · vivid 끔 · × 1.6 · 바닥 +0.1 m) — 왼쪽 흰 검 = 흰 고리 · 오른쪽 검은 검 = DarkReadableTint",
                spawn = (pooled, owned) =>
                {
                    knightWhite = RunFx.Play(RunFxSlot.Ring, spot - side + Vector3.up * 0.1f, KnightRingScale, Color.white, vivid: false);
                    knightDark  = RunFx.Play(RunFxSlot.Ring, spot + side + Vector3.up * 0.1f, KnightRingScale,
                                             RelicFairy.Monster.DKGridPatternHelper.DarkReadableTint, vivid: false);
                    pooled.Add(knightWhite);
                    pooled.Add(knightDark);
                },
                after = () => $"바닥 = 대기방(기사 아레나 아님 · 아레나 바닥은 더 어둡다) · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n" +
                              $"    입자 시작 색(첫 입자계) — 흰 고리 {FirstStartColor(knightWhite)} · 보라 고리 {FirstStartColor(knightDark)}\n" +
                              $"    판정 기준: 받은 색 × 원본 시작 색(흰 1, 1, 1)이면 vivid 꺼짐. vivid였다면 보라 = {Vividized(RelicFairy.Monster.DKGridPatternHelper.DarkReadableTint)} · 흰 = 그대로(채도 0)" },
        };
    }

    /// <summary>첫 입자계의 시작 색(숫자) — 색 곱이 어떻게 들어갔는지 판정용.</summary>
    private static string FirstStartColor(GameObject go)
    {
        if (go == null) return "없음";
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0) return "입자계 없음";
        var sc = systems[0].main.startColor;
        Color c = sc.mode == ParticleSystemGradientMode.TwoColors ? sc.colorMax
                : sc.mode == ParticleSystemGradientMode.Gradient && sc.gradient != null ? sc.gradient.Evaluate(0f)
                : sc.color;
        return $"{systems[0].name} ({c.r:0.00}, {c.g:0.00}, {c.b:0.00}, {c.a:0.00})";
    }

    /// <summary>RunFx.Vivid와 같은 식(비교용) — 채도 0.15 미만은 그대로 · 채도를 1까지 남은 거리의 절반 올리고 밝기 × 0.45.</summary>
    private static string Vividized(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.15f) return $"({c.r:0.00}, {c.g:0.00}, {c.b:0.00}) 그대로";
        var o = Color.HSVToRGB(h, Mathf.Lerp(s, 1f, 0.5f), v * 0.45f);
        return $"({o.r:0.00}, {o.g:0.00}, {o.b:0.00})";
    }

    /// <summary>
    /// 런 공용 칸을 풀 밖에서 새로 만든다 — RunFx.Spawn과 같은 손질(배율 = 칸 × 배율 · 배율 ≠ 1이면 입자 크기 Hierarchy · 색 곱 · 굴절층 끔 · 끌 자식).
    /// 풀 재사용과 나란히 찍어 「재사용이 원인인가」를 가린다.
    /// </summary>
    private static GameObject FreshRunFx(RunFxSetSO set, RunFxSlot slot, Vector3 pos, float scale, Color tint)
    {
        if (set == null || !set.TryGet(slot, out var e)) return null;
        var go = Object.Instantiate(e.prefab, pos + e.offset, Quaternion.identity);
        go.name = "~VfxToneProbe_Fresh_" + e.prefab.name;
        float s = (e.scale > 0f ? e.scale : 1f) * scale;
        go.transform.localScale = e.prefab.transform.localScale * s;
        var cache = go.AddComponent<RelicFairy.Monster.LichVfxScaleCache>();
        cache.Prepare(false);
        cache.ApplyTint(tint);
        if (!Mathf.Approximately(s, 1f)) cache.EnsureHierarchyScaling();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
            if (sh != null && (sh.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0
                            || sh.name.IndexOf("ShockWave", StringComparison.OrdinalIgnoreCase) >= 0)) r.enabled = false;
        }
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
        return go;
    }

    /// <summary>한 장면 — 한 번 재생이면 정점을 찾고 다시 재생해 찍는다. 반복이면 자리 잡은 뒤 찍는다.</summary>
    private static async UniTask ShootAsync(Shot shot, Camera cam, StringBuilder sb, CancellationToken ct)
    {
        var pooled = new List<GameObject>();
        var owned  = new List<GameObject>();
        float peakAt = LoopSettle;
        try
        {
            if (!shot.loop)
            {
                // 1회차 — 살아 있는 입자 범위가 가장 큰 순간을 찾는다
                shot.spawn(pooled, owned);
                float best = -1f, t0 = Time.realtimeSinceStartup;
                for (float t = 0f; t < SearchWindow; t = Time.realtimeSinceStartup - t0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    var m = Measure(pooled, owned);
                    float score = m.coverage;   // 보이는 양 — 퍼지며 옅어지는 고리가 수명 끝에서 골리지 않게
                    if (score > best) { best = score; peakAt = Time.realtimeSinceStartup - t0; }
                    await UniTask.Delay(TimeSpan.FromSeconds(Step), DelayType.Realtime, cancellationToken: ct);
                }
                Clear(pooled, owned);
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, cancellationToken: ct);
            }

            // 2회차(반복은 처음이자 마지막) — 정점에 찍는다
            shot.spawn(pooled, owned);
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.05f, peakAt)), DelayType.Realtime, cancellationToken: ct);
            var r = Measure(pooled, owned);
            Render(cam, $"{ShotDir}/{shot.name}.png");
            sb.AppendLine($"{shot.name} — {shot.note}");
            sb.AppendLine($"    찍은 때 {peakAt:0.00}초{(shot.loop ? "(반복 · 자리 잡은 뒤)" : "(정점)")} · 크기 {r.size.x:0.0}×{r.size.y:0.0}×{r.size.z:0.0} m · 살아 있는 입자 {r.particles}(보이는 것 {r.visible}) · 보이는 양 {r.coverage:0.00} · 입자계 {r.systems} · 켜진 광원 {r.lights}(최대 세기 {r.maxLight:0.#}) · 굴절층 켜짐 {r.refraction}");
            if (r.visible == 0) sb.AppendLine("    ⚠ 찍을 때 보이는 입자 0 — 정점이 수명 끝이었거나 렌더러가 꺼졌다");
            if (shot.after != null) sb.AppendLine("    " + shot.after());
        }
        finally
        {
            Clear(pooled, owned);
        }
        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, cancellationToken: ct);
    }

    /// <summary>거점 — 포탈 소용돌이(SSEP16 · 반복 꺼진 원본)가 로드 뒤 사라지는지 잰다.</summary>
    private static async UniTask BaseCampPortalAsync(Camera cam, StringBuilder sb, CancellationToken ct)
    {
        var vortex = GameObject.Find("PortalVortex");
        if (vortex == null) { sb.AppendLine("런 플레이어도 거점 포탈(PortalVortex)도 없다 — 테스트 허브 → 런(적 없는 방) 또는 거점에서 누를 것"); return; }
        int alive = 0, playing = 0, looping = 0, systems = 0;
        foreach (var ps in vortex.GetComponentsInChildren<ParticleSystem>(true))
        {
            systems++;
            alive += ps.particleCount;
            if (ps.isPlaying) playing++;
            if (ps.main.loop) looping++;
        }
        sb.AppendLine($"거점 포탈 소용돌이 — 씬 로드 뒤 {Time.timeSinceLevelLoad:0.0}초 · 입자계 {systems} · 재생 중 {playing} · 반복 {looping} · 살아 있는 입자 {alive} · 배율 {vortex.transform.lossyScale.x:0.###}");
        sb.AppendLine(alive == 0 && Time.timeSinceLevelLoad > 15f ? "    → 결함 확인: 반복이 꺼져 한 번(약 9초) 돌고 사라졌다" : "    → 아직 보인다(15초 전이면 다시 잴 것)");
        var cpos = cam.transform.position;
        sb.AppendLine($"    카메라 {cpos} → 포탈 {vortex.transform.position} 거리 {Vector3.Distance(cpos, vortex.transform.position):0.0} m");

        // 원경 에너지체(SSEP PulseCore ×12) — 원본의 ObjectMoveDestroy(isDestroy · 1초)가 켜진 채라 스스로 터져 사라지는지
        int orbs = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("EnergyOrb", StringComparison.Ordinal)) orbs++;
        sb.AppendLine($"    원경 에너지체 EnergyOrb_* 남은 수 {orbs}(배치 12) — 0이면 자폭 결함 확인");
        Render(cam, $"{ShotDir}/VFX_BC_portal.png");
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
    }

    /// <summary>
    /// 프리팹을 게임과 같은 방식으로 띄운다 — 배율 · 입자 크기 모드 Hierarchy · 판정 부품 끔 · 처음부터 재생.
    /// <paramref name="element"/>면 속성 · 서약 재생기(ElementVfxPlayer)의 2차 손질도 같게 — 굴절층 끔 · 점광원 × 0.4 · 색 곱 · 끌 자식.
    /// </summary>
    private static GameObject Prefab(GameObject prefab, Vector3 pos, float scale, Quaternion rot, bool keepScale = false,
                                     bool element = false, Color tint = default, string mute = null)
    {
        var go = Object.Instantiate(prefab, pos, rot);
        go.name = "~VfxToneProbe_" + prefab.name;
        go.transform.localScale = prefab.transform.localScale * scale;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in go.GetComponentsInChildren<EffectBehaviour>(true)) b.enabled = false;
        foreach (var c in go.GetComponentsInChildren<ColliderInstance>(true)) c.enabled = false;
        if (element)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (sh != null && (sh.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0
                                || sh.name.IndexOf("ShockWave", StringComparison.OrdinalIgnoreCase) >= 0)) r.enabled = false;
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.intensity *= 0.4f;
            if (!string.IsNullOrEmpty(mute))
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t != go.transform && Array.IndexOf(mute.Split(','), t.name) >= 0) t.gameObject.SetActive(false);
            if (tint.a > 0f)
            {
                var cache = go.AddComponent<RelicFairy.Monster.LichVfxScaleCache>();
                cache.Prepare(false);
                cache.ApplyTint(tint);
            }
        }
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!keepScale) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            ps.Clear(true);
            ps.Play(true);
        }
        return go;
    }

    /// <summary>서약 세트 칸 하나 — CovenantFxService.Burst/Attach와 같은 프리팹 · 배율 · 색 · 끌 자식(반경을 주면 반경 맞춤).</summary>
    private static GameObject Cov(CovenantVfxSet set, string key, Vector3 pos, float radius = 0f)
    {
        if (set == null || !set.TryGet(key, radius, out var prefab, out float scale, out _, out var tint, out var mute)) return null;
        return Prefab(prefab, pos, scale, prefab.transform.rotation, element: true, tint: tint, mute: mute);
    }

    /// <summary>적 몸 상태 이펙트 — 속성 목록의 몸 칸 · 배율 · 색(ElementVfxPlayer.AttachStatus와 같게, 몸 가운데 높이 0.9 m).</summary>
    private static GameObject Body(ElementVfxRegistry registry, RuneElement element, Vector3 pos)
    {
        if (registry == null) return null;
        var body = registry.GetStatusBody(element, out float scale, out Color tint);
        if (body == null) return null;
        return Prefab(body, pos + Vector3.up * 0.9f, scale, body.transform.rotation, element: true, tint: tint);
    }

    private static void Clear(List<GameObject> pooled, List<GameObject> owned)
    {
        for (int i = 0; i < pooled.Count; i++)
        {
            var go = pooled[i];
            if (go != null) RunFx.Stop(ref go);
        }
        for (int i = 0; i < owned.Count; i++)
            if (owned[i] != null) Object.Destroy(owned[i]);
        pooled.Clear();
        owned.Clear();
    }

    private struct Measured
    {
        public Vector3 size;
        public int     particles, visible, systems, lights, refraction;
        public float   maxLight, coverage;   // coverage = 켜진 렌더러의 살아 있는 입자마다 알파 × 크기² 합
    }

    /// <summary>
    /// 살아 있는 입자(위치 ± 크기)와 입자 아닌 렌더러의 범위 · 입자 수 · 켜진 광원 · 굴절층(Distortion · ShockWave 셰이더 렌더러) 수.
    /// </summary>
    private static Measured Measure(List<GameObject> pooled, List<GameObject> owned)
    {
        var m = new Measured();
        bool any = false;
        var b = new Bounds();
        void Add(Bounds x) { if (!any) { b = x; any = true; } else b.Encapsulate(x); }
        void Scan(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                m.systems++;
                int n = ps.particleCount;
                if (n == 0) continue;
                var arr = new ParticleSystem.Particle[n];
                n = ps.GetParticles(arr);
                m.particles += n;
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                float s = ps.transform.lossyScale.x;
                bool shown = ps.TryGetComponent<ParticleSystemRenderer>(out var pr) && pr.enabled;
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = local ? ps.transform.TransformPoint(arr[k].position) : arr[k].position;
                    float size = arr[k].GetCurrentSize(ps) * s;
                    Add(new Bounds(p, Vector3.one * size));
                    if (!shown) continue;
                    float a = arr[k].GetCurrentColor(ps).a / 255f;
                    if (a <= 0.01f) continue;
                    m.visible++;
                    m.coverage += a * size * size;
                }
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (sh != null && (sh.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0
                                || sh.name.IndexOf("ShockWave", StringComparison.OrdinalIgnoreCase) >= 0)) m.refraction++;
                if (r is ParticleSystemRenderer) continue;
                Add(r.bounds);
            }
            foreach (var l in go.GetComponentsInChildren<Light>())
            {
                if (!l.enabled) continue;
                m.lights++;
                m.maxLight = Mathf.Max(m.maxLight, l.intensity);
            }
        }
        foreach (var go in pooled) Scan(go);
        foreach (var go in owned) Scan(go);
        m.size = any ? b.size : Vector3.zero;
        return m;
    }

    /// <summary>게임 카메라를 렌더 텍스처에 한 번 그려 저장한다(HUD 없이 — MonsterAttackTimingProbeEditor와 같은 방식).</summary>
    private static void Render(Camera cam, string path)
    {
        var rt = new RenderTexture(W, H, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(rt);
        Object.Destroy(tex);
    }
}
#endif
