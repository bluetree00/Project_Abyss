using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cinemachine;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구 · Build/BaseCamp 플레이 중 · 읽기 전용] 베이스캠프 구조를 실제 런타임 상태로 잰다(09-26).
/// 플레이어를 옮기거나 상호작용·트리거를 건드리지 않는다(온보딩·세이브 상태를 바꾸지 않게).
///  · 스폰: 플레이어 위치 · 쓰인 스폰 지점 · 발밑 바닥까지 거리
///  · 카메라: 브레인·활성 vcam·FreeLook 추적 대상·흔들림 확장이 붙은 곳·가림 페이더 마스크
///  · 후처리 볼륨 전체(우선순위·가중치) · 이벤트 시스템/오디오 리스너 개수 · 라이트 수
///  · 상호작용 지점: 월드 좌표 · 활성 · 발밑 바닥 · 복귀 스폰에서의 거리
///  · 주 동선(z≈17, x −5~122) 바닥 연속성 — 1.5m 간격으로 위에서 아래로 쏜다
/// 결과: Temp/basecamp_structure_probe.txt
/// </summary>
public static class BaseCampStructureProbeEditor
{
    private const string OutPath = "Temp/basecamp_structure_probe.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/베이스캠프 구조 실측 (Build/BaseCamp 플레이 중, 읽기 전용)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[베이스캠프실측] 플레이 모드에서만 동작한다."); return; }
        RunAsync(CancellationToken.None).Forget();
    }

    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin(OutPath, "베이스캠프실측");
        try
        {
            // 플레이어 스폰 대기(최대 20초)
            float t0 = Time.realtimeSinceStartup;
            while (BaseCampBootstrapper.Instance?.Player == null && Time.realtimeSinceStartup - t0 < 20f)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(1.5), DelayType.Realtime, cancellationToken: ct);   // 카메라 인계·블렌드

            var boot   = BaseCampBootstrapper.Instance;
            var player = boot != null ? boot.Player : null;
            Transform Spawn(string f) => boot != null ? typeof(BaseCampBootstrapper).GetField(f, Inst)?.GetValue(boot) as Transform : null;
            var introSpawn  = Spawn("playerSpawnPoint");
            var returnSpawn = Spawn("returnSpawnPoint");

            sb.AppendLine("■ 스폰");
            if (player == null) sb.AppendLine("  ❌ 플레이어 없음(20초 대기)");
            else
            {
                var p = player.transform.position;
                sb.AppendLine($"  플레이어 {V(p)} · 입구 스폰 {V(introSpawn)} · 복귀 스폰 {V(returnSpawn)}");
                sb.AppendLine($"  쓰인 스폰: {(returnSpawn != null && (p - returnSpawn.position).sqrMagnitude < 4f ? "복귀" : introSpawn != null && (p - introSpawn.position).sqrMagnitude < 4f ? "입구" : "기타")} · 발밑 {Floor(p)}");
            }
            sb.AppendLine($"  입구 스폰 발밑 {Floor(introSpawn != null ? introSpawn.position : (Vector3?)null)} · 복귀 스폰 발밑 {Floor(returnSpawn != null ? returnSpawn.position : (Vector3?)null)}");

            sb.AppendLine("\n■ 카메라");
            var cam = Camera.main;
            sb.AppendLine($"  Camera.main = {(cam != null ? cam.name : "없음")} · 카메라 수 {Camera.allCamerasCount}");
            if (cam != null && cam.TryGetComponent<CinemachineBrain>(out var brain))
            {
                var vcam = brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
                sb.AppendLine($"  브레인 켜짐 {brain.enabled} · 활성 vcam {(vcam != null ? vcam.name : "없음")} · 갱신 {brain.m_UpdateMethod}/{brain.m_BlendUpdateMethod}");
            }
            foreach (var fl in UnityEngine.Object.FindObjectsByType<CinemachineFreeLook>(FindObjectsSortMode.None))
                sb.AppendLine($"  FreeLook {fl.name} 켜짐 {fl.enabled} · Follow {(fl.Follow != null ? fl.Follow.name : "없음")} · LookAt {(fl.LookAt != null ? fl.LookAt.name : "없음")} · 오빗(높이/반경) {string.Join(" ", fl.m_Orbits.Select(o => $"{o.m_Height:F1}/{o.m_Radius:F1}"))}");
            var shakes = UnityEngine.Object.FindObjectsByType<CameraShakeExtension>(FindObjectsSortMode.None);
            sb.AppendLine($"  흔들림 확장 {shakes.Length}개 {string.Join(", ", shakes.Select(s => s.name))}(첫 흔들림 때 붙는다 — 0이면 아직 흔들림이 없었던 것)");
            foreach (var f in UnityEngine.Object.FindObjectsByType<CameraOcclusionFader>(FindObjectsSortMode.None))
            {
                var mask = (LayerMask)(typeof(CameraOcclusionFader).GetField("occlusionMask", Inst)?.GetValue(f) ?? (LayerMask)0);
                sb.AppendLine($"  가림 페이더 {f.name} 켜짐 {f.enabled} · 마스크 {mask.value} ({LayerNames(mask)})");
            }

            sb.AppendLine("\n■ 후처리 볼륨");
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).OrderByDescending(v => v.priority))
                sb.AppendLine($"  {v.name} 전역 {v.isGlobal} · 우선 {v.priority} · 가중 {v.weight:F2} · 프로파일 {(v.sharedProfile != null ? v.sharedProfile.name : "없음")} · 켜짐 {v.isActiveAndEnabled}");

            sb.AppendLine("\n■ 공용");
            var es = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            sb.AppendLine($"  이벤트 시스템 {es.Length}개 ({string.Join(", ", es.Select(e => e.name + (e.isActiveAndEnabled ? "" : "(꺼짐)")))})");
            sb.AppendLine($"  오디오 리스너 {UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}개");
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            sb.AppendLine($"  라이트 {lights.Length}개 · 켜짐 {lights.Count(l => l.isActiveAndEnabled)} · 실시간 그림자 {lights.Count(l => l.isActiveAndEnabled && l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked)}");

            sb.AppendLine("\n■ 상호작용 · 구역 지점 (월드 좌표 · 발밑 바닥 · 복귀 스폰에서 수평 거리)");
            Vector3 home = returnSpawn != null ? returnSpawn.position : Vector3.zero;
            void List<T>(string label) where T : Component
            {
                foreach (var c in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(c => c.transform.position.x))
                {
                    var pos = c.transform.position;
                    float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(home.x, home.z));
                    string box = c.TryGetComponent<BoxCollider>(out var bc) ? $" · 상자 {V(bc.bounds.min)}~{V(bc.bounds.max)}" : "";
                    sb.AppendLine($"  {label,-10} {c.name,-28} {(c.gameObject.activeInHierarchy ? "" : "(비활성) ")}{V(pos)} 발밑 {Floor(pos)} · {d:F0}m{box}");
                }
            }
            List<WorldSwordAwakening>("무형검");
            List<WeaponForgeAltar>("모루");
            List<RelicAltar>("유물제단");
            List<WorldAwakeningAltar>("각성제단");
            List<BaseCampDungeonGate>("게이트");
            List<TrainingDummyMonster>("허수아비");
            List<TrainingDummy>("허수아비(옛)");
            List<RangedPartsTestAltar>("테스트제단");
            List<BaseCampSealShrine>("봉인석");
            List<OnboardingEntranceTrigger>("온보딩입구");
            List<CameraZone>("카메라구역");
            List<CameraBlendZone>("카메라블렌드");
            List<CameraHeadingZone>("시선구역");
            List<NaveProcessionalTrigger>("나브구역");

            sb.AppendLine("\n■ 주 동선 바닥 연속성 (z=17, 위에서 쏜 첫 비트리거 충돌)");
            float? prevY = null; int holes = 0; var steps = new List<string>();
            for (float x = -5f; x <= 122f; x += 1.5f)
            {
                var o = new Vector3(x, 30f, 17f);
                if (Physics.Raycast(o, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (prevY.HasValue && Mathf.Abs(hit.point.y - prevY.Value) > 0.6f) steps.Add($"x{x:F0}: {prevY:F1}→{hit.point.y:F1} ({hit.collider.name})");
                    prevY = hit.point.y;
                }
                else { holes++; steps.Add($"x{x:F0}: 바닥 없음"); prevY = null; }
            }
            sb.AppendLine($"  구멍 {holes}곳 · 높이 변화 {steps.Count}곳");
            foreach (var s in steps.Take(40)) sb.AppendLine("   " + s);
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        ProbeOutput.Write(OutPath, "베이스캠프실측", sb.ToString());
        Debug.Log("[베이스캠프실측] 결과\n" + sb);
    }

    private static string V(Vector3 v) => $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
    private static string V(Transform t) => t != null ? V(t.position) : "(없음)";

    /// <summary>지점 1m 위에서 아래로 쏜 첫 비트리거 충돌까지(= 발밑 바닥). 없으면 「바닥 없음」.</summary>
    private static string Floor(Vector3? p)
    {
        if (!p.HasValue) return "-";
        var o = p.Value + Vector3.up * 1f;
        return Physics.Raycast(o, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
            ? $"{hit.distance - 1f:F2}m 아래 {hit.collider.name}"
            : "바닥 없음(30m)";
    }

    private static string LayerNames(LayerMask m)
    {
        if (m.value == ~0) return "Everything";
        var names = new List<string>();
        for (int i = 0; i < 32; i++) if ((m.value & (1 << i)) != 0) names.Add(LayerMask.LayerToName(i));
        return names.Count == 0 ? "Nothing" : string.Join("+", names);
    }
}
