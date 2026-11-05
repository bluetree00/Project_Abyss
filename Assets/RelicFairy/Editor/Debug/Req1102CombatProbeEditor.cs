#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 11-02 전투 몫 실측(38 · 정본 구현설계/세션별_남은작업_20261102/00_모음.md).
/// Q6 「R 스킬로 베이스캠프 계단을 뚫고 갈 수 있다」 — Build/BaseCamp 플레이 중. 거점 계단(BaseCamp_Sanctum/Ascent · Descent 아래 Ground 콜라이더)
/// 둘레 8방향 바닥에서 계단 가운데로 무형검 R(무형일섬 · 8 m 돌진)을 실제로 쓴다. 돌진 전 앞길 거리(ClearDistanceAhead) · 끝난 자리 ·
/// 계단 안에 박혔나(캡슐 겹침) · 끝난 자리 위 계단 면보다 낮은가(밑으로 뚫음)를 적는다. 결과 Temp/req1102_q6.txt · 로그 「[R1102] 끝」.
/// </summary>
public static class Req1102CombatProbeEditor
{
    private const string Tag = "[R1102]";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly FieldInfo s_invincible = typeof(PlayerController).GetField("debugInvincible", Inst);
    private const float DashReach = 8f;   // 무형일섬 돌진 거리(NamelessIasen.asset dashDistance)

    [MenuItem("RelicFairy/Debug/11-02 전투/Q6 돌진 R 계단 뚫기 (Build/BaseCamp, 플레이 중)")]
    private static void RunQ6()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunQ6Async().Forget();
    }

    private static async UniTaskVoid RunQ6Async()
    {
        var sb = new StringBuilder("11-02 Q6 돌진 R 계단 뚫기\n");
        var ct = CancellationToken.None;
        PlayerController p = null;
        bool wasInvincible = false;
        try
        {
            for (float t = 0f; t < 30f && (p = UnityEngine.Object.FindFirstObjectByType<PlayerController>()) == null; t += 0.5f)
                await UniTask.Delay(500, ignoreTimeScale: true);
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            await UniTask.Delay(4000, ignoreTimeScale: true);   // 거점 생성 연출 · 첫 대사가 지나가게
            Managers.UI?.CloseAllPopupUI();
            wasInvincible = s_invincible != null && (bool)s_invincible.GetValue(p);
            s_invincible?.SetValue(p, true);
            p.SetInputEnabled(true);

            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, Nameless, ct, PlayerWeaponManager.Slot0 }))
            { sb.AppendLine("무형검 장착 실패"); return; }

            int ground = LayerMask.GetMask("Ground");
            var root = GameObject.Find("BaseCamp_Sanctum");
            if (root == null) { sb.AppendLine("BaseCamp_Sanctum 없음 — Build/BaseCamp에서 돌려야 한다"); return; }
            var stairs = new List<Collider>();
            foreach (var groupName in new[] { "Ascent", "Descent" })
            {
                var g = root.transform.Find(groupName);
                if (g == null) { sb.AppendLine($"  {groupName} 없음"); continue; }
                foreach (var c in g.GetComponentsInChildren<Collider>())
                    if (c.enabled && !c.isTrigger && ((1 << c.gameObject.layer) & ground) != 0 && c.bounds.size.y > 0.6f) stairs.Add(c);
            }
            sb.AppendLine($"계단 콜라이더 {stairs.Count}개(높이 0.6 m 넘는 Ground · Ascent/Descent 아래)");
            stairs.Sort((a, b) => (b.bounds.size.y * b.bounds.size.x * b.bounds.size.z).CompareTo(a.bounds.size.y * a.bounds.size.x * a.bounds.size.z));
            if (stairs.Count > 6) stairs.RemoveRange(6, stairs.Count - 6);

            var body = p.GetComponent<CapsuleCollider>();
            float bodyR = body != null ? body.radius : 0.27f, bodyH = body != null ? body.height : 1.8f;
            int cases = 0, through = 0, stuck = 0, shots = 0;
            // 처음 광장 연출 트리거(문턱 @BaseCampFx)가 입력을 막아 셋째 돌진부터 안 나갔다 — 실측 동안만 끈다(플레이 중 · 저장 안 됨)
            var fx = UnityEngine.Object.FindFirstObjectByType<BaseCampFxDirector>();
            if (fx != null && fx.TryGetComponent<BoxCollider>(out var fxBox)) fxBox.enabled = false;
            foreach (var stair in stairs)
            {
                if (!(stair is BoxCollider) || stair.name.IndexOf("Ramp", StringComparison.Ordinal) < 0) continue;
                if (File.Exists("Temp/req1102_q6_only.txt") && stair.name.IndexOf(File.ReadAllText("Temp/req1102_q6_only.txt").Trim(), StringComparison.Ordinal) < 0) continue;
                var b = stair.bounds;
                sb.AppendLine($"── {Path(stair.transform, root.transform)} · 크기 {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0} · 바닥 {b.min.y:0.00}~꼭대기 {b.max.y:0.00}");
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
                {
                    Vector3 over = new Vector3(Mathf.Lerp(b.min.x, b.max.x, f), b.max.y + 5f, b.center.z);
                    if (!Physics.Raycast(over, Vector3.down, out var surf, 20f, ground, QueryTriggerInteraction.Ignore) || surf.collider != stair) continue;
                    Vector3 downhill = new Vector3(surf.normal.x, 0f, surf.normal.z);
                    if (downhill.sqrMagnitude < 0.0001f) continue;
                    downhill.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, downhill);
                    foreach (var (label, dir) in new[] { ("오르막", -downhill), ("내리막", downhill), ("옆+", side), ("옆-", -side) })
                    {
                        Vector3 start = surf.point;
                        float clear = p.ClearDistanceAhead(start, dir, DashReach);
                        p.SetInputEnabled(true);
                        for (float w = 0f; w < 2f && p.IsInSkillAction; w += Time.unscaledDeltaTime) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        await PlaceAsync(p, start, dir, ct);
                        p.CooldownTracker.ResetCooldown(SkillType.R);
                        p.InputBuffer.Clear();
                        p.InputBuffer.Push(Command.RSkill);
                        for (float w = 0f; w < 0.5f && !p.IsInSkillAction; w += Time.unscaledDeltaTime)
                        {
                            AimClick(p, start + dir * 6f);
                            await UniTask.Yield(PlayerLoopTiming.EarlyUpdate, ct);
                        }
                        Vector3 aimed = p.transform.forward;
                        string gate = p.IsInSkillAction ? "" : " · " + SkillGateNote(p, SkillType.R);
                        Vector3 hi = start;
                        float t0 = Time.realtimeSinceStartup, sunk = 0f;
                        int sunkFrames = 0;
                        bool skill = false;
                        while (Time.realtimeSinceStartup - t0 < 1.6f)
                        {
                            var at = p.transform.position;
                            if (at.y > hi.y) hi = at;
                            skill |= p.IsInSkillAction;
                            // 몸(발)이 그 자리 계단 면보다 얼마나 아래인가 — 돌진이 높이를 그대로 두고 경사로 안을 지나는지
                            // 발 바로 위 1.5 m 안의 면만 본다 — 6 m에서 내리면 위층 바닥 · 다른 면을 「계단 면」으로 잡았다(11-02 2회차)
                            if (Physics.Raycast(new Vector3(at.x, at.y + 1.5f, at.z), Vector3.down, out var face, 3f, ground, QueryTriggerInteraction.Ignore))
                            {
                                float d = face.point.y - at.y;
                                if (d > sunk) sunk = d;
                                if (d > 0.15f) sunkFrames++;
                            }
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        }
                        Vector3 end = p.transform.position;
                        float moved = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z));
                        bool overlap = Physics.CheckCapsule(end + Vector3.up * (bodyR + 0.05f), end + Vector3.up * (bodyH - bodyR), bodyR * 0.9f, ground, QueryTriggerInteraction.Ignore);
                        float topY = Physics.Raycast(new Vector3(end.x, end.y + 6f, end.z), Vector3.down, out var top, 40f, ground, QueryTriggerInteraction.Ignore) ? top.point.y : float.NaN;
                        bool under = !float.IsNaN(topY) && end.y < topY - 0.4f;
                        bool pass = overlap || under || sunkFrames > 0;
                        cases++;
                        if (overlap) stuck++;
                        if (pass) through++;
                        float aimErr = Vector3.Angle(new Vector3(end.x - start.x, 0f, end.z - start.z).sqrMagnitude > 0.04f ? new Vector3(end.x - start.x, 0f, end.z - start.z) : aimed, dir);
                        sb.AppendLine($"  {f:0.00} {label} 방향 어긋남 {aimErr:0}° · 앞길 {clear:0.00} m · 움직임 {moved:0.00} m · 높이 {start.y:0.00}→{end.y:0.00}(최고 {hi.y:0.00}) · 끝 자리 위 면 {topY:0.00} · " +
                                      $"돌진 중 면 아래로 최대 {sunk:0.00} m · {sunkFrames}프레임 · 스킬 {(skill ? "씀" : "안 나감" + gate)} · " +
                                      $"{(pass ? (overlap ? "✗ 계단 안에 박힘" : under ? "✗ 계단 밑으로 뚫음" : "✗ 돌진 중 계단 면 아래를 지남") : "○ 막힘 없음")}");
                        if (pass && shots < 3)
                        {
                            CaptureFollow(p, $"Temp/ui_shots/R1102_Q6_{shots++}.png");
                            sb.AppendLine($"    사진 R1102_Q6_{shots - 1}.png");
                        }
                    }
                }
            }
            if (fx != null && fx.TryGetComponent<BoxCollider>(out var fxBox2)) fxBox2.enabled = true;
            sb.AppendLine($"합계 — 돌진 {cases}번 · 뚫음 {through}번(박힘 {stuck})");
        }
        catch (OperationCanceledException) { sb.AppendLine("취소"); }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (p != null) s_invincible?.SetValue(p, wasInvincible);
            File.WriteAllText("Temp/req1102_q6.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q6.txt");
        }
    }

    /// <summary>Q6 구조 — 거점 계단(Ascent · Descent) 둘레 3 m 안 충돌체 전부(이름 · 층 · 크기)와 위에서 본 사진(편집 중에도 됨).</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q6b 계단 둘레 충돌체 · 위에서 본 사진 (Build/BaseCamp)")]
    private static void DumpQ6()
    {
        var sb = new StringBuilder("11-02 Q6b 계단 둘레 충돌체\n");
        var root = GameObject.Find("BaseCamp_Sanctum");
        if (root == null) { Debug.LogWarning($"{Tag} BaseCamp_Sanctum 없음"); return; }
        Bounds area = default;
        bool any = false;
        foreach (var groupName in new[] { "Ascent", "Descent" })
        {
            var g = root.transform.Find(groupName);
            if (g == null) continue;
            foreach (var r in g.GetComponentsInChildren<Collider>())
            {
                if (!any) { area = r.bounds; any = true; } else area.Encapsulate(r.bounds);
            }
        }
        if (!any) { Debug.LogWarning($"{Tag} 계단 없음"); return; }
        area.Expand(new Vector3(6f, 2f, 6f));
        sb.AppendLine($"영역 {area.min} ~ {area.max}");
        var list = new List<Collider>(UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None));
        list.Sort((a, b) => a.bounds.center.y.CompareTo(b.bounds.center.y));
        foreach (var c in list)
        {
            if (!c.enabled || c.isTrigger || !area.Intersects(c.bounds)) continue;
            var b = c.bounds;
            sb.AppendLine($"  [{LayerMask.LayerToName(c.gameObject.layer)}] {Path(c.transform, root.transform)} ({c.GetType().Name}) 가운데 {b.center.x:0.0},{b.center.y:0.0},{b.center.z:0.0} · 크기 {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0}");
        }
        var go = new GameObject("~R1102Top");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(area.extents.x, area.extents.z) * 0.6f;
        cam.transform.position = new Vector3(area.center.x, area.max.y + 30f, area.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.farClipPlane = 200f;
        CaptureMain(cam, "Temp/ui_shots/R1102_Q6b_top.png");
        UnityEngine.Object.DestroyImmediate(go);
        File.WriteAllText("Temp/req1102_q6b.txt", sb.ToString());
        Debug.Log($"{Tag} 끝 → Temp/req1102_q6b.txt");
    }

    /// <summary>Q6 — 이름에 Stair가 든 렌더러 전부(어디 있나 · 충돌체가 있나 · 층).</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q6c 계단 그림 · 충돌체 목록 (Build/BaseCamp)")]
    private static void DumpStairs()
    {
        var sb = new StringBuilder("11-02 Q6c 계단 그림 · 충돌체\n");
        var groups = new Dictionary<string, List<string>>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.name.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var cols = r.GetComponentsInChildren<Collider>();
            string colInfo = cols.Length == 0 ? "충돌체 없음" : string.Join(",", Array.ConvertAll(cols, c => $"{c.GetType().Name}[{LayerMask.LayerToName(c.gameObject.layer)}]{(c.isTrigger ? "트리거" : "")}{(c.enabled ? "" : "꺼짐")}"));
            string parent = r.transform.parent != null ? Path(r.transform.parent, null) : "(루트)";
            var b = r.bounds;
            if (!groups.TryGetValue(parent, out var list)) groups[parent] = list = new List<string>();
            list.Add($"    {r.name} 가운데 {b.center.x:0.0},{b.center.y:0.0},{b.center.z:0.0} · 크기 {b.size.x:0.0}×{b.size.y:0.0}×{b.size.z:0.0} · {colInfo}");
        }
        foreach (var kv in groups) { sb.AppendLine($"── {kv.Key} ({kv.Value.Count})"); foreach (var l in kv.Value) sb.AppendLine(l); }
        File.WriteAllText("Temp/req1102_q6c.txt", sb.ToString());
        Debug.Log($"{Tag} 끝 → Temp/req1102_q6c.txt");
    }

    private const string Bow   = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string Great = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset";

    /// <summary>Q15 「플레이어가 활을 잡은 위치가 이상하다」(사진 7) — 활 들고 대기 · 달리기를 게임 카메라 · 앞 · 옆 · 위에서 찍고
    /// 활의 긴 축이 세로에서 몇 도 누웠는지 · 손에서 활 가운데까지 거리를 적는다. 결과 Temp/req1102_q15.txt · 사진 R1102_Q15_*.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q15 활 잡은 자세 (Build/BaseCamp, 플레이 중)")]
    private static void RunQ15() { if (EditorApplication.isPlaying) RunQ15Async().Forget(); }

    private static async UniTaskVoid RunQ15Async()
    {
        var sb = new StringBuilder("11-02 Q15 활 잡은 자세\n");
        var ct = CancellationToken.None;
        GameObject camGo = null;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, Bow, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine("활 장착 실패"); return; }
            await UniTask.Delay(1200, ignoreTimeScale: true);
            camGo = new GameObject("~R1102Cam");
            var cam = camGo.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            cam.fieldOfView = 35f;
            var mount = p.HandTransformLeft;
            sb.AppendLine($"왼손 소켓 {(mount != null ? mount.name : "없음")} · 부모 {(mount != null && mount.parent != null ? mount.parent.name : "-")} · 로컬 위치 {(mount != null ? mount.localPosition.ToString("F3") : "-")} · 로컬 회전 {(mount != null ? mount.localEulerAngles.ToString("F1") : "-")}");
            foreach (var (label, run) in new[] { ("대기", false), ("달리기", true), ("쏘기", false) })
            {
                bool shoot = label == "쏘기";
                if (shoot) { p.InputBuffer.Clear(); p.InputBuffer.Push(Command.Light); await UniTask.Delay(250, ignoreTimeScale: true); }   // 평타(활 쏘기) 당기는 자세
                var kb = Keyboard.current;
                if (run && kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
                await UniTask.Delay(run ? 700 : 300, ignoreTimeScale: true);
                sb.AppendLine($"── {label}");
                sb.AppendLine("  " + BowPose(p));
                Vector3 c = p.transform.position + Vector3.up * 1.1f, fw = p.transform.forward, rt = p.transform.right;
                string tagName = shoot ? "shoot" : run ? "run" : "idle";
                CaptureFollow(p, $"Temp/ui_shots/R1102_Q15_{tagName}_game.png");
                foreach (var (name, at) in new[] { ("front", c + fw * 3.2f + Vector3.up * 0.3f), ("left", c - rt * 3.2f + Vector3.up * 0.3f), ("right", c + rt * 3.2f + Vector3.up * 0.3f), ("top", c + Vector3.up * 3.5f - fw * 0.01f) })
                {
                    cam.transform.position = at;
                    cam.transform.LookAt(c);
                    CaptureMain(cam, $"Temp/ui_shots/R1102_Q15_{tagName}_{name}.png");
                }
                if (run && kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState());
                await UniTask.Delay(600, ignoreTimeScale: true);
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            File.WriteAllText("Temp/req1102_q15.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q15.txt");
        }
    }

    /// <summary>지금 든 활 — 긴 축(로컬 경계 상자 가장 긴 축)의 세로 기울기 · 그 축이 몸 앞/옆 어디를 향하나 · 손(소켓)에서 활 가운데까지.</summary>
    private static string BowPose(PlayerController p)
    {
        var inst = p.WeaponManager.CurrentWeaponInstance;
        if (inst == null) return "활 없음";
        var mf = inst.GetComponentInChildren<MeshFilter>();
        var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        Bounds lb; Transform t;
        if (mf != null && mf.sharedMesh != null) { lb = mf.sharedMesh.bounds; t = mf.transform; }
        else if (smr != null) { lb = smr.localBounds; t = smr.transform; }
        else return "활 메시 없음";
        Vector3 e = Vector3.Scale(lb.size, t.lossyScale);
        Vector3 axis = e.x >= e.y && e.x >= e.z ? t.right : e.y >= e.z ? t.up : t.forward;
        float tilt = Vector3.Angle(axis, Vector3.up); if (tilt > 90f) tilt = 180f - tilt;
        Vector3 flat = Vector3.ProjectOnPlane(axis, Vector3.up);
        float yaw = flat.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(p.transform.forward, flat, Vector3.up) : 0f;
        var mount = p.HandTransformLeft;
        Vector3 center = t.TransformPoint(lb.center);
        Vector3 local = p.transform.InverseTransformPoint(center);
        return $"활 {inst.name} · 긴 축 세로에서 {tilt:0}° 누움(0 = 곧게 섬) · 누운 쪽 몸 앞 기준 {yaw:0}° · 길이 {Mathf.Max(e.x, Mathf.Max(e.y, e.z)):0.00} m · " +
               $"활 가운데 몸 기준(오른쪽 {local.x:0.00} · 위 {local.y:0.00} · 앞 {local.z:0.00}) · 손에서 {(mount != null ? Vector3.Distance(mount.position, center) : -1f):0.00} m";
    }

    /// <summary>Q23 「랜슬롯 Q가 무적이라는 걸 느낄 수 있게」 — 유물 Q 쓰는 동안 몸 금빛(머티리얼 블록 발광) · 막은 타격 반짝임 · 무적 결과를 잰다.
    /// 랜슬롯(광란 채워 Q) · 가웨인(정오에 Q) 차례로. 결과 Temp/req1102_q23.txt · 사진 R1102_Q23_*.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q23 유물 Q 무적 신호 (테스트 허브, 플레이 중)")]
    private static void RunQ23() { if (EditorApplication.isPlaying) RunQ23Async().Forget(); }

    private static async UniTaskVoid RunQ23Async()
    {
        var sb = new StringBuilder("11-02 Q23 유물 Q 무적 신호\n");
        var ct = CancellationToken.None;
        PlayerController p = null;
        bool wasInv = false;
        try
        {
            p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            wasInv = s_invincible != null && (bool)s_invincible.GetValue(p);
            s_invincible?.SetValue(p, false);
            int guardOn = 0, guardOff = 0, blocked = 0, applied = 0;
            void OnGuard(bool on) { if (on) guardOn++; else guardOff++; }
            void OnResolved(GameObject a, int raw, int fin, PlayerController.DamageOutcome o) { if (o == PlayerController.DamageOutcome.Invincible) blocked++; else if (o == PlayerController.DamageOutcome.Applied) applied++; }
            var evGuard = typeof(PlayerController).GetEvent("OnRelicGuard");
            if (evGuard == null) { sb.AppendLine("OnRelicGuard 없음(고치기 전)"); }
            else evGuard.AddEventHandler(p, (Action<bool>)OnGuard);
            p.OnDamageResolved += OnResolved;
            var smr = p.GetComponentsInChildren<SkinnedMeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            float Emission() { float m = 0f; foreach (var r in smr) { if (r == null || !r.HasPropertyBlock()) continue; r.GetPropertyBlock(mpb); var c = mpb.GetColor("_EmissionColor"); m = Mathf.Max(m, c.maxColorComponent); } return m; }
            sb.AppendLine($"Q 전 몸 발광 {Emission():0.00}");
            // 유물 Q 무적을 직접 켠다 — Q 상태(ActSkillState)가 켜고 끄는 것은 10-30 실측 Q(가웨인 8/8 · 랜슬롯 18/18)로 확인했다. 여기선 신호만 잰다.
            p.RelicSkillInvulnerable = true;
            await UniTask.Delay(200, ignoreTimeScale: true);
            float during = Emission(), peak = 0f;
            for (int i = 0; i < 3; i++)
            {
                p.TakeDamage(10, null);   // 막혀야 한다 — 막은 타격 반짝임 · 「틱」
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                peak = Mathf.Max(peak, Emission());
                if (i == 0 && Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/R1102_Q23_block.png");
                await UniTask.Delay(250, ignoreTimeScale: true);
            }
            float settle = Emission();
            if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/R1102_Q23_guard.png");
            p.RelicSkillInvulnerable = false;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            sb.AppendLine($"무적 중 몸 발광 {during:0.00} · 막은 순간 최고 {peak:0.00} · 반짝임 뒤 {settle:0.00}");
            sb.AppendLine($"무적 켜짐 알림 {guardOn}번 · 꺼짐 {guardOff}번 · 막음 {blocked} · 들어감 {applied}(기대 막음 3 · 들어감 0)");
            sb.AppendLine($"Q 뒤 몸 발광 {Emission():0.00}(0 = 원래대로)");
            if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/R1102_Q23_after.png");
            if (evGuard != null) evGuard.RemoveEventHandler(p, (Action<bool>)OnGuard);
            p.OnDamageResolved -= OnResolved;
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (p != null) s_invincible?.SetValue(p, wasInv);
            File.WriteAllText("Temp/req1102_q23.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q23.txt");
        }
    }

    /// <summary>Q21 속성 검(대검 R) — 뽑기 · 속성 문장 · 휘두름 판정 이펙트를 게임 카메라로 0.1 · 0.35 · 0.5 · 0.65 · 0.8초에.
    /// 결과 Temp/req1102_q21.txt · 사진 R1102_Q21_*.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q21 속성 검 뽑기 · 판정 이펙트 (테스트 허브, 플레이 중)")]
    private static void RunQ21() { if (EditorApplication.isPlaying) RunQ21Async().Forget(); }

    private static async UniTaskVoid RunQ21Async()
    {
        var sb = new StringBuilder("11-02 Q21 속성 검\n");
        var ct = CancellationToken.None;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, Great, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine("대검 장착 실패"); return; }
            foreach (int el in new[] { 0, 3 })
            {
                GiantSwordBuff.ProbeElement = el;
                await UniTask.Delay(800, ignoreTimeScale: true);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.RSkill);
                float t0 = Time.realtimeSinceStartup;
                float[] at = { 0.1f, 0.35f, 0.5f, 0.65f, 0.8f };
                int k = 0;
                while (Time.realtimeSinceStartup - t0 < 1.4f)
                {
                    if (k < at.Length && Time.realtimeSinceStartup - t0 >= at[k]) CaptureFollow(p, $"Temp/ui_shots/R1102_Q21_e{el}_{k++}.png");
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"속성 {el} · 속성 검 {(GiantSwordBuff.IsActive(p) ? "켜짐" : "꺼짐")} · 속성 {GiantSwordBuff.ElementOf(p)} · 사진 R1102_Q21_e{el}_0~{at.Length - 1}");
                for (float w = 0f; w < 20f && GiantSwordBuff.IsActive(p); w += 0.25f) await UniTask.Delay(250, ignoreTimeScale: true);   // 버프가 끝나야 다음 속성이 붙는다
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            GiantSwordBuff.ProbeElement = -1;
            File.WriteAllText("Temp/req1102_q21.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q21.txt");
        }
    }

    private const string GsDie   = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Die/Inplace/GhostSamurai_APose_Die{0:00}_Inplace.FBX";
    private static readonly string[] EquipCands =
    {
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Greatsword/GhostSamurai_APose_Equip01_Inplace.FBX",
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/Common/Unarm&Equip/Inplace/GhostSamurai_APose_Equip02_Inplace.FBX",
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/Bow/Common/Inplace/GhostSamurai_Bow_Common_Equip_Inplace.FBX",
    };

    /// <summary>Q5 「죽음 연출이 끊기듯 넘어간다 — 플레이어가 그 자리에 쓰러지는 애니」 후보 11개 · Q3 「무기를 얻을 때 애니메이션」 후보 3개.
    /// 클립을 플레이어 Animator에 덮어(PlayableGraph) 25 · 50 · 75 · 100% 자세를 옆(앞 45°) · 뒤 높은 구도(59 인트로 S4)에서 찍는다.
    /// 결과 Temp/req1102_anim.txt(클립 길이 · 끝 자세 높이) · 사진 R1102_Anim_{Q5|Q3}_{n}_{구간}_{side|back}.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q5 · Q3 쓰러짐 · 받는 동작 후보 사진 (테스트 허브, 플레이 중)")]
    private static void RunAnimCands() { if (EditorApplication.isPlaying) RunAnimCandsAsync().Forget(); }

    private static async UniTaskVoid RunAnimCandsAsync()
    {
        var sb = new StringBuilder("11-02 Q5 · Q3 동작 후보\n");
        var ct = CancellationToken.None;
        GameObject camGo = null;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip != null) await (UniTask<bool>)equip.Invoke(null, new object[] { p, Nameless, ct, PlayerWeaponManager.Slot0 });
            await UniTask.Delay(1000, ignoreTimeScale: true);
            var anim = p.GetComponentInChildren<Animator>();
            if (anim == null) { sb.AppendLine("Animator 없음"); return; }
            camGo = new GameObject("~R1102AnimCam");
            var cam = camGo.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            cam.fieldOfView = 40f;
            var hips = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            var list = new List<(string tag, string path)>();
            for (int i = 1; i <= 11; i++) list.Add(("Q5_" + i.ToString("00"), string.Format(GsDie, i)));
            for (int i = 0; i < EquipCands.Length; i++) list.Add(("Q3_" + (i + 1), EquipCands[i]));
            foreach (var (tag, path) in list)
            {
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
                if (clip == null) { sb.AppendLine($"{tag} 없음 {path}"); continue; }
                var graph = PlayableGraph.Create("~R1102Anim");
                var output = AnimationPlayableOutput.Create(graph, "anim", anim);
                var play = AnimationClipPlayable.Create(graph, clip);
                output.SetSourcePlayable(play);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                graph.Play();
                Vector3 c0 = p.transform.position + Vector3.up * 0.9f, fw = p.transform.forward, rt = p.transform.right;
                float minHip = float.MaxValue;
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f, 1f })
                {
                    play.SetTime(clip.length * f - (f >= 1f ? 0.01f : 0f));
                    graph.Evaluate(0f);
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    play.SetTime(clip.length * f - (f >= 1f ? 0.01f : 0f));
                    graph.Evaluate(0f);
                    if (hips != null) minHip = Mathf.Min(minHip, hips.position.y - p.transform.position.y);
                    cam.transform.position = c0 + (fw + rt).normalized * 3.6f + Vector3.up * 0.6f;
                    cam.transform.LookAt(c0 - Vector3.up * 0.3f);
                    CaptureMain(cam, $"Temp/ui_shots/R1102_Anim_{tag}_{(int)(f * 100)}_side.png");
                    cam.transform.position = c0 - fw * 3.4f + Vector3.up * 2.6f;
                    cam.transform.LookAt(c0 + fw * 0.6f);
                    CaptureMain(cam, $"Temp/ui_shots/R1102_Anim_{tag}_{(int)(f * 100)}_back.png");
                }
                sb.AppendLine($"{tag} {clip.name} · {clip.length:0.00}초 · 루프 {clip.isLooping} · 엉덩이 최저 높이 {minHip:0.00} m · {path}");
                graph.Destroy();
                await UniTask.Delay(150, ignoreTimeScale: true);
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            File.WriteAllText("Temp/req1102_anim.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_anim.txt");
        }
    }

    private const string PickFile  = "Temp/req1102_anim_pick.txt";
    private const string ClipGroup = "WeaponAnimation";
    private const string ClipDir   = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/OneShot";

    /// <summary>Q3 · Q5 고른 클립을 주소에 올린다 — Temp/req1102_anim_pick.txt 「receive=경로」 · 「death=경로」.
    /// _ThirdParty(깃 제외)에 있으면 _Imported/…/katana/OneShot으로 복사(AssetDatabase.CopyAsset — 임포트 설정 그대로 · .meta는 Unity가 만든다)한 뒤
    /// WeaponAnimation 그룹에 Player_WeaponReceive · Player_DeathFall 주소로 등록. 몇 번 돌려도 같은 결과.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q3 · Q5 동작 클립 등록 (편집 모드)")]
    private static void RegisterOneShotClips()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 편집 모드에서"); return; }
        var sb = new StringBuilder("11-02 Q3 · Q5 동작 클립 등록\n");
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(ClipGroup) : null;
        if (group == null || !File.Exists(PickFile)) { Debug.LogWarning($"{Tag} 그룹 {ClipGroup} 또는 {PickFile} 없음"); return; }
        foreach (var line in File.ReadAllLines(PickFile))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string which = line.Substring(0, eq).Trim(), path = line.Substring(eq + 1).Trim();
            // addr:주소=경로 — 주소를 그대로 지정(잘못 바꾼 주소 되돌리기)
            string address = which.StartsWith("addr:") ? which.Substring(5) : which == "receive" ? "Player_WeaponReceive" : which == "death" ? "Player_DeathFall" : null;
            // 이미 다른 주소로 쓰이는 에셋은 주소를 바꾸지 않는다(11-02 — Equip01 = 대검 R 뽑기 GreatswordDraw를 덮어썼다) → 그 주소를 그대로 쓴다
            var had = address != null && !which.StartsWith("addr:") ? settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)) : null;
            if (had != null && !string.IsNullOrEmpty(had.address) && had.address != address)
            {
                sb.AppendLine($"  기존 주소 유지 {had.address} ← {path} (코드 쪽 키를 이 주소로)");
                continue;
            }
            if (address == null || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null) { sb.AppendLine($"  건너뜀 {line}"); continue; }
            if (path.StartsWith("Assets/_ThirdParty/"))
            {
                if (!AssetDatabase.IsValidFolder(ClipDir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(ClipDir).Replace('\\', '/'), System.IO.Path.GetFileName(ClipDir));
                string to = ClipDir + "/" + System.IO.Path.GetFileName(path);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(to) == null && !AssetDatabase.CopyAsset(path, to)) { sb.AppendLine($"  복사 실패 {path}"); continue; }
                sb.AppendLine($"  복사 {path} → {to}");
                path = to;
            }
            // 같은 주소를 쓰던 다른 클립이 있으면 주소를 뗀다(고른 것을 바꿀 때)
            foreach (var g in settings.groups)
            {
                if (g == null) continue;
                foreach (var e in new List<UnityEditor.AddressableAssets.Settings.AddressableAssetEntry>(g.entries))
                    if (e.address == address && e.AssetPath != path) { settings.RemoveAssetEntry(e.guid); sb.AppendLine($"  옛 주소 뗌 {e.AssetPath}"); }
            }
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
            entry.address = address;
            sb.AppendLine($"  {address} ← {path}");
        }
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/req1102_clipreg.txt", sb.ToString());
        Debug.Log($"{Tag} 끝 → Temp/req1102_clipreg.txt");
    }

    /// <summary>Q5 확인 — 시간을 멈추고(배율 0) PlayDeathFallAsync가 실제 시간으로 쓰러지는지 · 끝 자세를 지키는지. 0.3 · 0.8 · 1.4 · 2.2초 사진 · 엉덩이 높이.
    /// Q3 확인 — PlayWeaponReceiveAsync 길이 · 끝난 뒤 평소 자세(엉덩이 높이)로 돌아왔나. 결과 Temp/req1102_q5q3.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q5 · Q3 확인 — 쓰러짐(시간 멈춤) · 받는 동작 (테스트 허브, 플레이 중)")]
    private static void RunQ5Q3() { if (EditorApplication.isPlaying) RunQ5Q3Async().Forget(); }

    private static async UniTaskVoid RunQ5Q3Async()
    {
        var sb = new StringBuilder("11-02 Q5 · Q3 확인\n");
        var ct = CancellationToken.None;
        var owner = new object();
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip != null) await (UniTask<bool>)equip.Invoke(null, new object[] { p, Nameless, ct, PlayerWeaponManager.Slot0 });
            await UniTask.Delay(800, ignoreTimeScale: true);
            var hips = p.Anim != null && p.Anim.isHuman ? p.Anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            float Hip() => hips != null ? hips.position.y - p.transform.position.y : -1f;
            sb.AppendLine($"평소 엉덩이 높이 {Hip():0.00} m");
            // Q3
            var mRecv = typeof(PlayerController).GetMethod("PlayWeaponReceiveAsync");
            if (mRecv == null) sb.AppendLine("PlayWeaponReceiveAsync 없음(고치기 전)");
            else
            {
                float len = await (UniTask<float>)mRecv.Invoke(p, null);
                float t0 = Time.realtimeSinceStartup, lo = 9f, hi = -9f;
                int k = 0;
                while (Time.realtimeSinceStartup - t0 < len + 0.5f)
                {
                    float h = Hip(); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    if (k < 4 && Time.realtimeSinceStartup - t0 >= len * (k + 1) / 4f && Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/R1102_Q3_{k++}.png");
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"Q3 받는 동작 {len:0.00}초 · 엉덩이 {lo:0.00}~{hi:0.00} · 끝난 뒤 {Hip():0.00}(평소로 돌아왔나)");
            }
            // Q5
            var mFall = typeof(PlayerController).GetMethod("PlayDeathFallAsync");
            if (mFall == null) sb.AppendLine("PlayDeathFallAsync 없음(고치기 전)");
            else
            {
                TimeScaleArbiter.Acquire(owner, 0f, TimeScaleArbiter.Priority.SlowMotion);
                float len = await (UniTask<float>)mFall.Invoke(p, null);
                float t0 = Time.realtimeSinceStartup;
                var at = new[] { 0.3f, 0.8f, 1.4f, 2.2f };
                int k = 0;
                var hs = new StringBuilder();
                while (k < at.Length)
                {
                    if (Time.realtimeSinceStartup - t0 >= at[k])
                    {
                        hs.Append($" {at[k]:0.0}초 {Hip():0.00}");
                        if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/R1102_Q5_{k}.png");
                        k++;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"Q5 쓰러짐 {len:0.00}초 · 배율 {Time.timeScale:0.0} 동안 엉덩이 높이{hs}(내려가 머물면 정상)");
                TimeScaleArbiter.Release(owner);
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            TimeScaleArbiter.Release(owner);
            File.WriteAllText("Temp/req1102_q5q3.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q5q3.txt");
        }
    }

    /// <summary>R(등)이 안 나간 까닭 — 무기 · 날아감 · 슬롯 · 봉인 · 유물 게이트 · 쿨 · 행동/이동 상태.</summary>
    private static string SkillGateNote(PlayerController p, SkillType slot)
    {
        bool B(string name, params object[] a)
        {
            var m = typeof(PlayerController).GetMethod(name, Inst);
            try { return m != null && (bool)m.Invoke(p, a); } catch { return false; }
        }
        object Get(string field) => typeof(PlayerController).GetField(field, Inst)?.GetValue(p);
        var act = Get("_actSM"); var loco = Get("_locoSM");
        string Cur(object sm) => sm?.GetType().GetProperty("CurrentId")?.GetValue(sm)?.ToString() ?? "?";
        return $"공격 가능 {B("CanAttack")} · 슬롯 {p.HasSkillInSlot(slot)} · 봉인 {B("IsSkillSealed", slot)} · 게이트 {B("IsSkillGateOpen", slot)} · 쿨 {p.CooldownTracker.IsReady(slot)} · 행동 {Cur(act)} · 이동 {Cur(loco)}";
    }

    /// <summary>플레이어 뒤 위에서 따라 보는 사진 — 거점 Camera.main은 플레이어를 안 비췄다(11-02 실측).</summary>
    private static void CaptureFollow(PlayerController p, string path)
    {
        var go = new GameObject("~R1102Follow");
        try
        {
            var cam = go.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            cam.fieldOfView = 45f;
            Vector3 c = p.transform.position + Vector3.up * 1f, fw = p.transform.forward;
            cam.transform.position = c - fw * 6f + Vector3.up * 5f + p.transform.right * 2f;
            cam.transform.LookAt(c);
            CaptureMain(cam, path);
        }
        finally { UnityEngine.Object.Destroy(go); }
    }

    private const string GripFile = "Temp/req1102_bowgrip.json";
    private static readonly string[] BowPrefabs =
    {
        "Assets/RelicFairy/Weapon/Bow/Prefabs/T1_Bow_Weapon.prefab",
        "Assets/RelicFairy/Weapon/Bow/Prefabs/T2_Bow_Weapon.prefab",
        "Assets/RelicFairy/Weapon/Bow/Prefabs/T3_Bow_Weapon.prefab",
    };

    /// <summary>Q15b 활 손잡이 재기 — 활을 들고 대기 1.5초 뒤 BakeMesh 꼭짓점의 주축(긴 축) · 가운데를 구하고,
    /// 왼손 주먹 구멍(검지 · 가운데 · 약지 세 마디 원의 중심)과의 차이를 왼손 소켓(WeaponMountLeft) 기준으로 적는다(→ 맞추기 메뉴가 읽는다).
    /// 활마다(T1~T3). 결과 Temp/req1102_q15b.txt · Temp/req1102_bowgrip.json · 사진 R1102_Q15b_{n}_{front|left}.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q15b 활 손잡이 재기 (Build/BaseCamp, 플레이 중)")]
    private static void RunQ15b() { if (EditorApplication.isPlaying) RunQ15bAsync().Forget(); }

    private static async UniTaskVoid RunQ15bAsync()
    {
        var sb = new StringBuilder("11-02 Q15b 활 손잡이\n");
        var json = new List<string>();
        var ct = CancellationToken.None;
        GameObject camGo = null;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            camGo = new GameObject("~R1102Cam");
            var cam = camGo.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            cam.fieldOfView = 30f;
            for (int i = 0; i < BowPrefabs.Length; i++)
            {
                string so = $"Assets/RelicFairy/Weapon/Bow/Data/T{i + 1}_Bow.asset";
                if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, so, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine($"T{i + 1} 장착 실패"); continue; }
                await UniTask.Delay(1500, ignoreTimeScale: true);
                var inst = p.WeaponManager.CurrentWeaponInstance;
                var mount = p.HandTransformLeft;
                var smr = inst != null ? inst.GetComponentInChildren<SkinnedMeshRenderer>() : null;
                if (smr == null || mount == null) { sb.AppendLine($"T{i + 1} 메시 · 소켓 없음"); continue; }
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var v = mesh.vertices;
                Vector3 mean = Vector3.zero;
                for (int k = 0; k < v.Length; k++) { v[k] = smr.transform.TransformPoint(v[k]); mean += v[k]; }
                mean /= Mathf.Max(1, v.Length);
                // 주축 — 가운데에서 가장 먼 점 방향(활은 길쭉해 이것으로 충분)
                Vector3 far = mean; float fd = 0f;
                foreach (var x in v) { float d = (x - mean).sqrMagnitude; if (d > fd) { fd = d; far = x; } }
                Vector3 axis = (far - mean).normalized;
                float lo = 0f, hi = 0f;
                foreach (var x in v) { float t = Vector3.Dot(x - mean, axis); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); }
                Vector3 grip = mean + axis * ((lo + hi) * 0.5f);   // 긴 축 위 두 끝의 가운데
                UnityEngine.Object.DestroyImmediate(mesh);
                Vector3 fist = LeftFist(p.Anim, out string note, out Vector3 hole);
                Vector3 gripM = mount.InverseTransformPoint(grip), fistM = mount.InverseTransformPoint(fist);
                Vector3 axisM = mount.InverseTransformDirection(axis), holeM = mount.InverseTransformDirection(hole);
                if (Vector3.Dot(axisM, holeM) < 0f) holeM = -holeM;
                Quaternion fix = hole != Vector3.zero ? Quaternion.FromToRotation(axisM, holeM) : Quaternion.identity;
                var rt0 = inst.transform;   // 소켓 아래 루트(프리팹 루트 값 그대로)
                Vector3 newPos = fistM - fix * (gripM - rt0.localPosition);
                Quaternion newRot = fix * rt0.localRotation;
                float tilt = Vector3.Angle(axis, Vector3.up); if (tilt > 90f) tilt = 180f - tilt;
                sb.AppendLine($"T{i + 1} 긴 축 ↔ 주먹 구멍 축 {Vector3.Angle(axisM, holeM):0}° · 새 루트 위치 {newPos.ToString("F3")} 회전 {newRot.eulerAngles.ToString("F1")} (지금 {rt0.localPosition.ToString("F3")} · {rt0.localEulerAngles.ToString("F1")})");
                sb.AppendLine($"T{i + 1} 활 길이 {(hi - lo):0.00} m · 긴 축 세로에서 {tilt:0}° · 손잡이(가운데) ↔ 주먹 구멍 {(grip - fist).magnitude * 100f:0.0} cm · " +
                              $"소켓 기준 손잡이 {gripM.ToString("F3")} 주먹 {fistM.ToString("F3")} · {note}");
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                json.Add($"\"T{i + 1}\":[{newPos.x.ToString(ic)},{newPos.y.ToString(ic)},{newPos.z.ToString(ic)},{newRot.x.ToString(ic)},{newRot.y.ToString(ic)},{newRot.z.ToString(ic)},{newRot.w.ToString(ic)}]");
                Vector3 c = mount.position, fw = p.transform.forward, rt = p.transform.right;
                foreach (var (name, at) in new[] { ("front", c + fw * 1.6f + Vector3.up * 0.2f), ("left", c - rt * 1.6f + Vector3.up * 0.2f) })
                {
                    cam.transform.position = at;
                    cam.transform.LookAt(c);
                    CaptureMain(cam, $"Temp/ui_shots/R1102_Q15b_{i + 1}_{name}.png");
                }
            }
            File.WriteAllText(GripFile, "{" + string.Join(",", json) + "}");
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            File.WriteAllText("Temp/req1102_q15b.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q15b.txt");
        }
    }

    /// <summary>왼손 주먹 구멍 — 검지 · 가운데 · 약지마다 세 마디를 지나는 원의 중심 평균(WeaponGripFit.FistCenter의 왼손판).</summary>
    private static Vector3 LeftFist(Animator anim, out string note) => LeftFist(anim, out note, out _);

    /// <summary>왼손 주먹 구멍 + 구멍 축(세 마디 원 평면의 법선 평균 — 손잡이가 지나가는 방향).</summary>
    private static Vector3 LeftFist(Animator anim, out string note, out Vector3 holeAxis)
    {
        holeAxis = Vector3.zero;
        var fingers = new[]
        {
            (HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal),
            (HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal),
            (HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal),
        };
        Vector3 sum = Vector3.zero, all = Vector3.zero;
        int used = 0, pts = 0;
        var radii = new List<string>();
        foreach (var (pa, pb, pc) in fingers)
        {
            var A = anim.GetBoneTransform(pa); var B = anim.GetBoneTransform(pb); var C = anim.GetBoneTransform(pc);
            if (A == null || B == null || C == null) { radii.Add("뼈 없음"); continue; }
            all += A.position + B.position + C.position; pts += 3;
            Vector3 a = A.position - C.position, b = B.position - C.position, axb = Vector3.Cross(a, b);
            if (axb.sqrMagnitude < 1e-10f) { radii.Add("곧음"); continue; }
            Vector3 o = C.position + Vector3.Cross(a.sqrMagnitude * b - b.sqrMagnitude * a, axb) / (2f * axb.sqrMagnitude);
            float r = (o - A.position).magnitude;
            radii.Add($"{r * 100f:0.0}");
            if (r < 0.01f || r > 0.07f) continue;
            sum += o; used++;
            Vector3 nrm = axb.normalized;
            if (holeAxis != Vector3.zero && Vector3.Dot(nrm, holeAxis) < 0f) nrm = -nrm;
            holeAxis += nrm;
        }
        holeAxis = holeAxis.sqrMagnitude > 1e-6f ? holeAxis.normalized : Vector3.zero;
        note = $"주먹 원 반지름 cm [{string.Join(", ", radii)}] · 쓴 손가락 {used}";
        if (used > 0) return sum / used;
        return pts > 0 ? all / pts : anim.GetBoneTransform(HumanBodyBones.LeftHand).position;
    }

    /// <summary>Q15 활 손잡이 맞추기 — Q15b가 남긴 차이(소켓 기준 주먹 − 손잡이)만큼 활 프리팹(T1~T3) 루트를 옮긴다(회전 · 배율 그대로).
    /// 소켓(WeaponMountLeft)이 프리팹 루트의 부모라 소켓 기준 차이를 루트 위치에 그대로 더한다. 몇 번 돌려도 Q15b를 다시 재고 돌리면 수렴.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q15 활 손잡이 맞추기 (편집 모드 · Q15b 결과로)")]
    private static void FitBowGrip()
    {
        if (EditorApplication.isPlaying || !File.Exists(GripFile)) { Debug.LogWarning($"{Tag} 편집 모드에서 · {GripFile} 필요"); return; }
        var sb = new StringBuilder("11-02 Q15 활 손잡이 맞추기\n");
        string txt = File.ReadAllText(GripFile);
        for (int i = 0; i < BowPrefabs.Length; i++)
        {
            string key = $"\"T{i + 1}\":[";
            int a = txt.IndexOf(key, StringComparison.Ordinal);
            if (a < 0) { sb.AppendLine($"  T{i + 1} 값 없음"); continue; }
            int b = txt.IndexOf(']', a);
            var n = txt.Substring(a + key.Length, b - a - key.Length).Split(',');
            if (n.Length < 7) { sb.AppendLine($"  T{i + 1} 값 모자람"); continue; }
            float F(int k) => float.Parse(n[k], System.Globalization.CultureInfo.InvariantCulture);
            var pos = new Vector3(F(0), F(1), F(2));
            var rot = new Quaternion(F(3), F(4), F(5), F(6));
            var root = PrefabUtility.LoadPrefabContents(BowPrefabs[i]);
            try
            {
                var oldP = root.transform.localPosition; var oldR = root.transform.localEulerAngles;
                root.transform.SetLocalPositionAndRotation(pos, rot);
                PrefabUtility.SaveAsPrefabAsset(root, BowPrefabs[i]);
                sb.AppendLine($"  T{i + 1} 루트 위치 {oldP.ToString("F3")} → {pos.ToString("F3")} · 회전 {oldR.ToString("F1")} → {rot.eulerAngles.ToString("F1")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        File.WriteAllText("Temp/req1102_bowfit.txt", sb.ToString());
        Debug.Log($"{Tag} 끝 → Temp/req1102_bowfit.txt");
    }

    /// <summary>Q32 ① 스킬 중 다음 스킬을 미리 누른 뒤 회피 — 회피가 몇 초 만에 나가나(고치기 전: 버퍼 맨 앞 스킬 칸에 묻혀 최대 0.4초 안 나감).
    /// 대검 E(주변경계 · 긴 스킬)를 쓰고 0.25초 뒤 E · 0.05초 뒤 회피를 넣는다. 5번. 결과 Temp/req1102_q32.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/Q32 스킬 중 미리 누른 스킬 뒤 회피 (Build/BaseCamp, 플레이 중)")]
    private static void RunQ32() { if (EditorApplication.isPlaying) RunQ32Async().Forget(); }

    private static async UniTaskVoid RunQ32Async()
    {
        var sb = new StringBuilder("11-02 Q32 ① 미리 누른 스킬 뒤 회피\n");
        var ct = CancellationToken.None;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, Great, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine("대검 장착 실패"); return; }
            object Get(string field) => typeof(PlayerController).GetField(field, Inst)?.GetValue(p);
            string Loco() { var sm = Get("_locoSM"); return sm?.GetType().GetProperty("CurrentId")?.GetValue(sm)?.ToString() ?? "?"; }
            for (int i = 0; i < 5; i++)
            {
                await UniTask.Delay(1500, ignoreTimeScale: true);
                Managers.UI?.CloseAllPopupUI();   // 거점 대사 · 팝업이 시간을 멈추고 입력을 막았다(실측 2회차부터)
                p.SetInputEnabled(true);
                await UniTask.Delay(200, ignoreTimeScale: true);
                string env = $"배율 {Time.timeScale:0.0} · 팝업 정지 {TimeScaleArbiter.DescribeHolders()}";
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.ESkill);
                await UniTask.Delay(250, ignoreTimeScale: true);
                bool inSkill = p.IsInSkillAction;
                p.InputBuffer.Push(Command.ESkill);   // 스킬 중 미리 누른 다음 스킬
                await UniTask.Delay(50, ignoreTimeScale: true);
                var st = typeof(PlayerController).GetProperty("Stamina", Inst)?.GetValue(p);
                string stam = st != null ? (st.GetType().GetProperty("Current", Inst)?.GetValue(st) ?? st.GetType().GetField("_current", Inst)?.GetValue(st))?.ToString() : "?";
                object cdEnd = typeof(PlayerController).GetProperty("DodgeCooldownEnd", Inst)?.GetValue(p);
                string why = $"스태미너 {stam} · 회피 쿨 끝 {cdEnd} (지금 {Time.time:0.00}) · 캔슬 잠금 {p.SkillCancelLocked} · 이동 {Loco()}";
                p.InputBuffer.Push(Command.Dodge);
                float t0 = Time.realtimeSinceStartup, at = -1f;
                while (Time.realtimeSinceStartup - t0 < 1f)
                {
                    if (Loco() == "Dodge") { at = Time.realtimeSinceStartup - t0; break; }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"  {i + 1} 스킬 중 {inSkill} · 회피 {(at < 0f ? "1초 안에 안 나감" : $"{at:0.000}초 만에 나감")} · {why} · {env}");
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            File.WriteAllText("Temp/req1102_q32.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_q32.txt");
        }
    }

    // ── 11-02 26 점검 「안 된 것」 전투 몫 ─────────────────────────────
    private const string FireAuraNow  = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Auras/Aura_Fire_LWRP.prefab";
    private const string FireAuraCand = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Spells/Spell_Fire_11_LWRP.prefab";
    private static readonly float[] FireTimes = { 0.15f, 0.4f, 0.8f, 1.2f, 2f, 3f, 4f, 5f, 5.8f, 6.5f };
    private static readonly float[] FireShots = { 0.15f, 0.8f, 2f, 4f, 5.8f };
    private static readonly ParticleSystem.Particle[] s_fireParts = new ParticleSystem.Particle[4096];

    /// <summary>불 오라(불붙은 바닥) 교체 — ① 프리팹 그대로(배율 1 · 자체 반복) 시간순 가로 폭 · 입자 · 사진(5.6초 주기에 다시 터지나)
    /// ② 실제 길(ElementVfxPlayer.AttachAura 불 · 반경 3 m · 4초 = 전설 불바다와 같은 수명) 시간순 가로 폭 — 판정 지름 6 m와 맞나.
    /// 결과 Temp/req1102_fireaura.txt · 사진 R1102_Fire_{raw_now|raw_cand|real}_{n}.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/불 오라 후보 시간순 · 실제 길 (Build/BaseCamp, 플레이 중)")]
    private static void RunFireAura() { if (EditorApplication.isPlaying) RunFireAuraAsync().Forget(); }

    private static async UniTaskVoid RunFireAuraAsync()
    {
        var sb = new StringBuilder("11-02 불 오라 후보\n");
        var ct = CancellationToken.None;
        GameObject anchor = null;
        int h = 0;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            Vector3 fw = p.transform.forward;
            Vector3 at = p.transform.position + fw * 4f;
            if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, LayerMask.GetMask("Ground"))) at = hit.point;
            sb.AppendLine($"자리 {at} · 배율 {Time.timeScale}");
            // 후보 목록 — Temp/fire_aura_cand2.txt(줄마다 경로)가 있으면 그것(11-02 밤 「계속 타는 바닥 불」 찾기)
            var paths = File.Exists("Temp/fire_aura_cand2.txt") ? File.ReadAllLines("Temp/fire_aura_cand2.txt") : new[] { FireAuraNow, FireAuraCand };
            for (int pi = 0; pi < paths.Length; pi++)
            {
                string path = paths[pi].Trim();
                if (path.Length == 0) continue;
                if (Time.timeScale == 0f) Managers.UI?.CloseAllPopupUI();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine("없음 " + path); continue; }
                string key = path == FireAuraNow ? "now" : path == FireAuraCand ? "cand" : "c" + pi;
                var go = UnityEngine.Object.Instantiate(prefab, at + Vector3.up * 0.02f, prefab.transform.rotation);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var pss = go.GetComponentsInChildren<ParticleSystem>(true);
                // 실제 길(ElementVfxPlayer.RestartParticles)처럼 다시 튼다. 거점 Camera.main은 이 자리를 안 비춰 「화면 밖 멈춤」 계통이 멈췄다 — 실측만 늘 돌린다
                foreach (var ps in pss) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; ps.Clear(true); ps.Play(true); }
                sb.AppendLine($"① {prefab.name} 배율 1 · 계통 {pss.Length} · 반복 {(pss.Length > 0 && pss[0].main.loop)} · 길이 {(pss.Length > 0 ? pss[0].main.duration : 0f):0.0}초");
                await SampleFireAsync(go.transform, at, fw, "R1102_Fire_raw_" + key, sb, ct);
                UnityEngine.Object.Destroy(go);
                await UniTask.Delay(600, ignoreTimeScale: true);
            }

            // ② 실제 길 — 속성 이펙트 목록(ElementVfxRegistry)에 지금 든 불 오라
            anchor = new GameObject("~R1102FireAnchor");
            anchor.transform.position = at;
            ElementVfxPlayer.Warmup();   // 목록은 처음 부를 때 비동기로 불러온다 — 될 때까지 다시 건다
            for (float w = 0f; w < 8f && (h = ElementVfxPlayer.AttachAura(RuneElement.Fire, anchor.transform, 4f, 3f)) == 0; w += 0.25f)
                await UniTask.Delay(250, ignoreTimeScale: true);
            Transform real = null;
            var player = UnityEngine.Object.FindFirstObjectByType<ElementVfxPlayer>();
            if (player != null)
                foreach (Transform c in player.transform)
                    if (c.gameObject.activeSelf && (c.position - at).sqrMagnitude < 0.25f) { real = c; break; }
            sb.AppendLine($"② 실제 길 AttachAura(불 · 반경 3 m = 지름 6 m · 4초) 핸들 {h} · {(real != null ? real.name + " 배율 " + real.localScale.x.ToString("0.00") : "인스턴스 못 찾음")}");
            if (real != null)
            {
                foreach (var ps in real.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; ps.Clear(true); ps.Play(true); }   // 위 ①과 같은 이유
                await SampleFireAsync(real, at, fw, "R1102_Fire_real", sb, ct);
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            if (h != 0) ElementVfxPlayer.ReleaseAura(h);
            if (anchor != null) UnityEngine.Object.Destroy(anchor);
            File.WriteAllText("Temp/req1102_fireaura.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_fireaura.txt");
        }
    }

    /// <summary>시간순 가로 폭(입자 있는 렌더러 묶음 상자의 x · z 큰 쪽 · 12 m 넘는 흩어진 계통 뺌) · 입자 수 · 사진(앞 위에서 자리를 내려다봄).</summary>
    private static async UniTask SampleFireAsync(Transform root, Vector3 at, Vector3 fw, string shot, StringBuilder sb, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        int ti = 0, si = 0;
        while (ti < FireTimes.Length)
        {
            float e = Time.realtimeSinceStartup - t0;
            if (Time.timeScale == 0f) Managers.UI?.CloseAllPopupUI();   // 거점 도착 대사가 늦게 떠 시간을 멈췄다(배율 0 → 입자 멈춤)
            if (si < FireShots.Length && e >= FireShots[si]) CaptureAt(at, fw, $"Temp/ui_shots/{shot}_{si++}.png");
            if (e >= FireTimes[ti])
            {
                var rs = new List<float>();
                int parts = 0;
                if (root != null)
                    foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
                    {
                        if (!ps.TryGetComponent<ParticleSystemRenderer>(out var pr) || !pr.enabled) continue;
                        int n = ps.GetParticles(s_fireParts);
                        parts += n;
                        bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                        for (int k = 0; k < n; k++)
                        {
                            Vector3 wp = world ? s_fireParts[k].position : ps.transform.TransformPoint(s_fireParts[k].position);
                            rs.Add(new Vector2(wp.x - at.x, wp.z - at.z).magnitude);
                        }
                    }
                rs.Sort();
                float r90 = rs.Count > 0 ? rs[Mathf.Min(rs.Count - 1, (int)(rs.Count * 0.9f))] : 0f;
                float rMax = rs.Count > 0 ? rs[rs.Count - 1] : 0f;
                sb.AppendLine($"   {FireTimes[ti]:0.00}초 반경 90% {r90:0.00} · 최대 {rMax:0.00} m · 입자 {parts} · 배율 {Time.timeScale:0.##}");
                ti++;
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private static void CaptureAt(Vector3 at, Vector3 fw, string path)
    {
        var go = new GameObject("~R1102At");
        try
        {
            var cam = go.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            cam.fieldOfView = 45f;
            cam.transform.position = at - fw * 7f + Vector3.up * 6f;
            cam.transform.LookAt(at);
            CaptureMain(cam, path);
        }
        finally { UnityEngine.Object.Destroy(go); }
    }

    /// <summary>풀링 점검 #7(구현설계 RelicFairy_풀링·대량스폰·이펙트_점검_20261029.md) — 전설 룬 이펙트(LegendaryRuntime.SpawnVfx = 매번 Instantiate · 나중에 Destroy)의
    /// 한 번 비용을 풀 재사용(BossEffectPool.Spawn → Release)과 견준다. 카탈로그(LegendaryVfxCatalog)의 프리팹마다 데운 뒤 20번씩 · 편집기 플레이(빌드보다 느리다).
    /// 지우기 = 같은 수를 Instantiate한 뒤 DestroyImmediate(나중 Destroy 비용의 상한). 결과 Temp/req1102_legfx.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/풀링 #7 전설 룬 이펙트 한 번 비용 (Build/BaseCamp, 플레이 중)")]
    private static void RunLegFx() { if (EditorApplication.isPlaying) RunLegFxAsync().Forget(); }

    private static async UniTaskVoid RunLegFxAsync()
    {
        const int N = 20;
        var sb = new StringBuilder("11-02 풀링 #7 전설 룬 이펙트 한 번 비용(ms · 편집기 플레이)\n");
        try
        {
            var p = await FindPlayerAsync();
            var guids = AssetDatabase.FindAssets("t:LegendaryVfxCatalog");
            var cat = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<LegendaryVfxCatalog>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
            if (cat == null) { sb.AppendLine("카탈로그 없음"); return; }
            Vector3 at = (p != null ? p.transform.position : Vector3.zero) + Vector3.down * 50f;   // 화면 밖 — 그리기 비용은 빼고 만들기만 잰다
            var sw = new System.Diagnostics.Stopwatch();
            var seen = new HashSet<GameObject>();
            var made = new GameObject[N];
            double worst = 0, worstNow = 0;
            string worstName = "", worstNowName = "";
            foreach (var fi in typeof(LegendaryVfxCatalog).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (fi.FieldType != typeof(GameObject) || !(fi.GetValue(cat) is GameObject prefab) || prefab == null || !seen.Add(prefab)) continue;   // 빈 칸(Unity null)도 건너뜀
                // 거점 도착 대사가 늦게 떠 시간을 멈췄다(배율 0 → 수명이 안 흘러 풀에 안 돌아옴 · 입자 0) — 닫고 다시 흐를 때까지
                for (float w = 0f; w < 3f && Time.timeScale == 0f; w += 0.1f) { Managers.UI?.CloseAllPopupUI(); await UniTask.Delay(100, ignoreTimeScale: true); }
                int psN = prefab.GetComponentsInChildren<ParticleSystem>(true).Length;
                LegendaryRuntime.SpawnVfx(prefab, at, Quaternion.identity, 0.05f, 1f, 0.5f);   // 데우기(첫 로드 · 셰이더)
                await UniTask.Yield();

                long a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                for (int i = 0; i < N; i++) LegendaryRuntime.SpawnVfx(prefab, at, Quaternion.identity, 0.05f, 1f, 0.5f);   // 알파 칠 길까지(무거운 쪽)
                sw.Stop();
                double make = sw.Elapsed.TotalMilliseconds / N;
                double kb = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024.0 / N;

                for (int i = 0; i < N; i++) made[i] = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
                sw.Restart();
                for (int i = 0; i < N; i++) UnityEngine.Object.DestroyImmediate(made[i]);
                sw.Stop();
                double kill = sw.Elapsed.TotalMilliseconds / N;

                RelicFairy.Monster.BossEffectPool.Release(RelicFairy.Monster.BossEffectPool.Spawn(prefab, at, Quaternion.identity));   // 풀 데우기
                sw.Restart();
                for (int i = 0; i < N; i++) RelicFairy.Monster.BossEffectPool.Release(RelicFairy.Monster.BossEffectPool.Spawn(prefab, at, Quaternion.identity));
                sw.Stop();
                double pool = sw.Elapsed.TotalMilliseconds / N;

                // 새 길(11-02 풀링 #7) — 알파 1 · follow 없음이면 스크립트 없는 프리팹은 전용 풀. 데우기(N개) → 돌아올 때까지 → N번
                for (int i = 0; i < N; i++) LegendaryRuntime.SpawnVfx(prefab, at, Quaternion.identity, 0.05f);
                await UniTask.Delay(400, ignoreTimeScale: true);
                sw.Restart();
                for (int i = 0; i < N; i++) LegendaryRuntime.SpawnVfx(prefab, at, Quaternion.identity, 0.05f);
                sw.Stop();
                double now = sw.Elapsed.TotalMilliseconds / N;
                await UniTask.Delay(400, ignoreTimeScale: true);
                LegendaryRuntime.SpawnVfx(prefab, at, Quaternion.identity, 1f);   // 다시 쓴 사본 — 처음부터 다시 도나
                await UniTask.Delay(150, ignoreTimeScale: true);
                int live = 0;
                var roots = new HashSet<Transform>();
                foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.transform.root.name == prefab.name + "(Clone)" && (ps.transform.position - at).sqrMagnitude < 25f) { live += ps.particleCount; roots.Add(ps.transform.root); }
                int actives = roots.Count;
                sb.AppendLine($"  {fi.Name} · {prefab.name} · 계통 {psN} · 만들기 {make:0.000} + 지우기 {kill:0.000} · 보스 풀 {pool:0.000} · 새 길 {now:0.000} · 다시 쓴 사본 {actives}개 입자 {live} · 할당 {kb:0.0} KB · 배율 {Time.timeScale:0.##}");
                if (make + kill > worst) { worst = make + kill; worstName = fi.Name; }
                if (now > worstNow) { worstNow = now; worstNowName = fi.Name; }
                await UniTask.Delay(150, ignoreTimeScale: true);
            }
            sb.AppendLine($"옛 길 가장 무거운 것 {worstName} {worst:0.000} ms/번 · 한 프레임 8번(광역 맞음 · 몬스터 8) {worst * 8:0.00} ms");
            sb.AppendLine($"새 길 가장 무거운 것 {worstNowName} {worstNow:0.000} ms/번 · 한 프레임 8번 {worstNow * 8:0.00} ms");
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            File.WriteAllText("Temp/req1102_legfx.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_legfx.txt");
        }
    }

    private const string KatanaT1 = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string PhantomDanceAsset = "Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/PhantomDance.asset";
    private const string GolemPrefab = "Assets/RelicFairy/Characters/Monster/Monster/Golem/Prefab/Golem.prefab";

    /// <summary>11-05 #16 「카타나 R 조작감 — 딜레이 · 범위 · 속도」 — 발도 0.35 → 0.25 · X자 대기 0.53 → 0.40 · 후딜 0.20 → 0.12 · 이어 갈라짐 간격 0.10 → 0.08 ·
    /// 사거리 4.5 → 5 · 각도 120 → 90 · 발도 그림 1 → 1.25(그림 = 판정). 결과 Temp/req1105_katana_asset.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/11-05 #16 환영베기 값 반입 (편집 모드)")]
    private static void ApplyKatanaR()
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PhantomDanceAsset);
        if (so == null) { Debug.LogWarning($"{Tag} 환영베기 에셋 없음"); return; }
        var s = new SerializedObject(so);
        var dh = s.FindProperty("drawHitTimes");
        string Read() => $"발도 {(dh.arraySize > 0 ? dh.GetArrayElementAtIndex(0).floatValue : -1f)} · X자 대기 {s.FindProperty("cutDelay").floatValue} · 후딜 {s.FindProperty("endDelay").floatValue}"
                       + $" · 이어 간격 {s.FindProperty("slashProjectileDelay").floatValue} · 사거리 {s.FindProperty("drawRange").floatValue} · 각도 {s.FindProperty("drawAngle").floatValue}"
                       + $" · 발도 그림 {s.FindProperty("drawEffectScale").floatValue}";
        string before = Read();
        dh.arraySize = 1;
        dh.GetArrayElementAtIndex(0).floatValue = 0.25f;
        s.FindProperty("cutDelay").floatValue = 0.40f;
        s.FindProperty("endDelay").floatValue = 0.12f;
        s.FindProperty("slashProjectileDelay").floatValue = 0.08f;
        s.FindProperty("drawRange").floatValue = 5f;
        s.FindProperty("drawAngle").floatValue = 90f;
        s.FindProperty("drawEffectScale").floatValue = 1.25f;
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();
        string msg = $"전: {before}\n뒤: {Read()}";
        File.WriteAllText("Temp/req1105_katana_asset.txt", msg);
        Debug.Log($"{Tag} 끝 → Temp/req1105_katana_asset.txt\n{msg}");
    }

    /// <summary>11-05 #19 「골렘이 잘 맞지 않는다」 — 루트 캡슐(반경 0.47 · 높이 2.59 · 중심 0.89 → 바닥이 발밑 0.4 m 아래 · 윗몸이 빠졌다)을
    /// 그림(렌더러 묶음 상자)에 맞춘다: 그림 바닥부터 그림 높이 97% · 반경 = 좁은 쪽 폭의 28%(0.5 ~ 0.65). 그림 크기가 이상하면(높이 2 ~ 4 밖) 정한 값(0.62 · 2.95 · 1.47).
    /// 결과 Temp/req1105_golem.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/11-05 #19 골렘 캡슐 맞추기 (편집 모드)")]
    private static void FitGolemCapsule()
    {
        var root = PrefabUtility.LoadPrefabContents(GolemPrefab);
        var sb = new StringBuilder("11-05 #19 골렘 캡슐\n");
        try
        {
            var cap = root.GetComponent<CapsuleCollider>();
            if (cap == null) { sb.AppendLine("루트 캡슐 없음"); return; }
            var b = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Vector3 rp = root.transform.position, rs = root.transform.lossyScale;
            sb.AppendLine($"전: 캡슐 반경 {cap.radius} · 높이 {cap.height} · 중심 {cap.center} · 루트 {rp} 배율 {rs}");
            sb.AppendLine($"그림: 가운데 {b.center} · 크기 {b.size} · 바닥 {b.min.y:0.000}");
            float sy = Mathf.Max(0.0001f, Mathf.Abs(rs.y)), sxz = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(rs.x), Mathf.Abs(rs.z)));
            float h = b.size.y / sy, w = Mathf.Min(b.size.x, b.size.z) / sxz;
            float radius, height, cy, cz;
            if (any && h >= 2f && h <= 4f)
            {
                height = h * 0.97f;
                radius = Mathf.Clamp(w * 0.28f, 0.5f, 0.65f);   // 내비 반경(0.5)보다 너무 크면 몸으로 막는 거리가 는다
                cy = (b.min.y - rp.y) / sy + height * 0.5f;
                cz = Mathf.Clamp((b.center.z - rp.z) / sxz, -0.3f, 0.3f);
                sb.AppendLine("그림 크기로 맞춤");
            }
            else
            {
                radius = 0.62f; height = 2.95f; cy = 1.47f; cz = 0.1f;
                sb.AppendLine("그림 크기가 이상해 정한 값으로");
            }
            cap.radius = radius;
            cap.height = Mathf.Max(height, radius * 2f);
            cap.center = new Vector3(0f, Mathf.Max(cy, radius), cz);
            PrefabUtility.SaveAsPrefabAsset(root, GolemPrefab);
            sb.AppendLine($"뒤: 캡슐 반경 {cap.radius:0.000} · 높이 {cap.height:0.000} · 중심 {cap.center}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            File.WriteAllText("Temp/req1105_golem.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1105_golem.txt\n{sb}");
        }
    }

    /// <summary>11-05 #16 카타나 R(환영베기) — 플레이어 안내(판정 부채꼴)를 켜고 R을 써 발도 그림과 판정을 같은 사진에(0.18 · 0.25 · 0.3 · 0.4초) ·
    /// 스킬 끝 시각을 적는다. 결과 Temp/req1102_katanar.txt · 사진 R1102_KatanaR_{n}.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/카타나 R 그림 · 판정 · 박자 (Build/BaseCamp, 플레이 중)")]
    private static void RunKatanaR() { if (EditorApplication.isPlaying) RunKatanaRAsync().Forget(); }

    private static async UniTaskVoid RunKatanaRAsync()
    {
        var sb = new StringBuilder("11-05 #16 카타나 R\n");
        var ct = CancellationToken.None;
        bool guides = SkillRangeRing.PlayerGuides;
        try
        {
            var p = await FindPlayerAsync();
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            var equip = typeof(WeaponFormProbeEditor).GetMethod("EquipAsync", SAny);
            if (equip == null || !await (UniTask<bool>)equip.Invoke(null, new object[] { p, KatanaT1, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine("카타나 장착 실패"); return; }
            SkillRangeRing.PlayerGuides = true;
            for (int run = 0; run < 2; run++)
            {
                if (Time.timeScale == 0f) Managers.UI?.CloseAllPopupUI();
                await UniTask.Delay(900, ignoreTimeScale: true);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.RSkill);
                float t0 = Time.realtimeSinceStartup, started = -1f, ended = -1f;
                float[] at = { 0.18f, 0.25f, 0.3f, 0.4f };
                int k = 0;
                while (Time.realtimeSinceStartup - t0 < 2.5f)
                {
                    float e = Time.realtimeSinceStartup - t0;
                    bool inSkill = p.IsInSkillAction;
                    if (inSkill && started < 0f) started = e;
                    if (!inSkill && started >= 0f && ended < 0f) ended = e;
                    if (run == 0 && started >= 0f && k < at.Length && e - started >= at[k]) CaptureFollow(p, $"Temp/ui_shots/R1102_KatanaR_{k++}.png");
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"R {run + 1} — 시작 {started:0.00}초 · 끝 {(ended >= 0f ? (ended - started).ToString("0.00") + "초" : "안 끝남")} · 배율 {Time.timeScale:0.##}");
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            SkillRangeRing.PlayerGuides = guides;
            File.WriteAllText("Temp/req1102_katanar.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_katanar.txt");
        }
    }

    /// <summary>11-05 #1 「보스 시작 때 공격하면 화면이 고정된다」 · #12 「화룡 등장 때 평타를 치며 들어가면 연출이 끝없이」 — 테스트 허브 보스방에서
    /// 평타를 치며 입구로 들어가(테스트 허브 메뉴 1 · 2) 등장 중에도 0.12초마다 평타를 6초 민다. 보스 HP · 무적 · 등장 대기 상태 · 입력 잠금 · 카메라 수동 제어를 적는다.
    /// 결과 Temp/req1105_bossentrance.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/11-05 #1 · #12 보스 등장 중 평타 (테스트 허브 보스방, 플레이 중)")]
    private static void RunBossEntrance() { if (EditorApplication.isPlaying) RunBossEntranceAsync().Forget(); }

    private static async UniTaskVoid RunBossEntranceAsync()
    {
        var sb = new StringBuilder("11-05 #1 · #12 보스 등장 중 평타\n");
        try
        {
            PlayerController p = null;
            BossRoomController room = null;
            for (float t = 0f; t < 90f && (p == null || room == null); t += 0.5f)
            {
                p = GameRunBootstrapper.Instance?.Run?.Player;
                room = UnityEngine.Object.FindFirstObjectByType<BossRoomController>();
                if (p == null || room == null) await UniTask.Delay(500, ignoreTimeScale: true);
            }
            if (p == null || room == null) { sb.AppendLine("보스방 아님(플레이어 · BossRoomController)"); return; }
            await UniTask.Delay(1500, ignoreTimeScale: true);
            var cam = GameCameraController.Instance;
            var panning = typeof(GameCameraController).GetField("_isPanning", Inst);
            var inputOff = typeof(PlayerController).GetField("_inputDisabledExternally", Inst);
            RelicFairy.Monster.MonsterBase boss = null;   // 보스는 입구를 밟아야 나온다(BossSpawner waitForExternalTrigger)
            var fsmF = typeof(RelicFairy.Monster.MonsterBase).GetField("_fsm", Inst);
            bool Dormant()   // 등장 대기 인스턴스가 지금 상태인가(등록 키가 없어 IsInState로는 못 묻는다)
            {
                if (boss == null) return false;
                var ds = boss.GetType().GetField("_dormantState", Inst)?.GetValue(boss) as RelicFairy.Monster.IMonsterState;
                return fsmF?.GetValue(boss) is RelicFairy.Monster.MonsterFSM fsm && fsm.IsCurrent(ds);
            }
            int probeHits = 0, probeDealt = 0, probeHp0 = -1, probeHp1 = -1;
            float nextProbe = 0f;

            float t0 = Time.realtimeSinceStartup, lastPush = -1f, doneAt = -1f, immuneSeen = -1f, bossAt = -1f;
            int pushes = 0, hp0 = -1, blockedHp = -1;
            bool stepped = false, past = false;
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                float e = Time.realtimeSinceStartup - t0;
                if (e < 9f && e - lastPush >= 0.12f) { p.InputBuffer.Push(Command.Light); lastPush = e; pushes++; }   // 평타를 치면서 들어간다(사용자 재현)
                if (!stepped && e >= 0.6f) { EditorApplication.ExecuteMenuItem("RelicFairy/Test Run/Boss Room/1 Step Into Entrance (Play)"); stepped = true; }
                else if (stepped && !past && e >= 0.9f) { EditorApplication.ExecuteMenuItem("RelicFairy/Test Run/Boss Room/2 Step Past Entrance (Play)"); past = true; }
                if (boss == null)
                    foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                        if (m is RelicFairy.Monster.DragonBossMonster || m is RelicFairy.Monster.DeathKnightBossMonster || m is RelicFairy.Monster.ForestGuardianMonster)
                        { boss = m; bossAt = e; hp0 = blockedHp = m.CurrentHp; sb.AppendLine($"{e:0.0}초 보스 {m.name} · HP {hp0} · 등장 대기 {Dormant()} · 무적 {m.IsDamageImmuneNow}"); break; }
                bool dormant = Dormant();
                // 등장 중 주 피해 길로 직접 넣어 본다(0.5초마다) — 막히면 피해 0 · HP 그대로(평타는 거리상 안 닿을 수 있다)
                if (dormant && boss.CurrentHp > 0 && e >= nextProbe)
                {
                    nextProbe = e + 0.5f;
                    if (probeHp0 < 0) probeHp0 = boss.CurrentHp;
                    float dealt = CombatDamage.Deal(new CombatDamage.Request
                    {
                        Target = boss.gameObject, BaseDamage = 100f, Owner = p.gameObject, ActionType = WeaponActionType.GroundLight,
                        KnockbackMultiplier = 1f, HitPoint = boss.transform.position + Vector3.up, SourcePosition = p.transform.position,
                    });
                    probeHits++;
                    if (dealt > 0f) probeDealt++;
                    probeHp1 = boss.CurrentHp;
                }
                if (boss != null && boss.IsDamageImmuneNow && immuneSeen < 0f) immuneSeen = e;
                if (dormant) blockedHp = boss.CurrentHp;
                bool camManual = cam != null && panning != null && (bool)panning.GetValue(cam);
                bool locked = inputOff != null && (bool)inputOff.GetValue(p);
                if (boss != null && past && e - bossAt > 1f && !dormant && !camManual && !locked) { doneAt = e; break; }
                if (Time.timeScale == 0f) Managers.UI?.CloseAllPopupUI();
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (boss == null) { sb.AppendLine("보스가 안 나왔다(90초)"); return; }
            if (doneAt >= 0f)   // 등장이 끝나면 무적이 풀리나 — 한 번 넣어 HP가 주나
            {
                await UniTask.Delay(500, ignoreTimeScale: true);
                int before = boss.CurrentHp;
                float dealt = CombatDamage.Deal(new CombatDamage.Request
                {
                    Target = boss.gameObject, BaseDamage = 100f, Owner = p.gameObject, ActionType = WeaponActionType.GroundLight,
                    KnockbackMultiplier = 1f, HitPoint = boss.transform.position + Vector3.up, SourcePosition = p.transform.position,
                });
                sb.AppendLine($"등장 뒤 직접 피해 {dealt:0.0} · HP {before} → {boss.CurrentHp} · 무적 {boss.IsDamageImmuneNow}(들어가야 정상)");
            }
            bool camM = cam != null && panning != null && (bool)panning.GetValue(cam);
            bool lockedEnd = inputOff != null && (bool)inputOff.GetValue(p);
            sb.AppendLine($"평타 민 수 {pushes} · 처음 무적 {(immuneSeen >= 0f ? immuneSeen.ToString("0.0") + "초" : "없음")} · 등장 중 HP {hp0} → {blockedHp}(깎이면 등장 중 맞음)");
            sb.AppendLine($"등장 중 직접 피해 {probeHits}번 · 들어감 {probeDealt}번 · HP {probeHp0} → {probeHp1}(0번 · 그대로면 막힘)");
            sb.AppendLine($"등장 끝 {(doneAt >= 0f ? doneAt.ToString("0.0") + "초" : "90초 안에 안 끝남")} · 등장 대기 남음 {Dormant()} · 카메라 수동 {camM} · 입력 잠금 {lockedEnd} · 지금 HP {boss.CurrentHp}");
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            File.WriteAllText("Temp/req1105_bossentrance.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1105_bossentrance.txt");
        }
    }

    /// <summary>11-05 #5 「탄막 이벤트를 회피로 피할 수 있어야 한다」(사용자 확인 = 탄막의 회랑) — 몬스터 없는 방(테스트 허브 보스 대기방)에서
    /// 탄막을 「가만히」 1판 · 「탄 · 구체가 닿기 직전 회피(8방향 중 대시 끝자리가 탄에서 가장 먼 쪽)」 2판. 맞음(OnDamageResolved 가해자 없음)마다
    /// 회피 중 · 회피 시작 뒤 초 · 스태미나 · 회피 가능 · 가까운 위험을 적는다. 결과 Temp/req1105_barrage.txt.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/11-05 #5 탄막의 회랑 회피 (런 안 · 몬스터 없는 방, 플레이 중)")]
    private static void RunBarrageDodge() { if (EditorApplication.isPlaying) RunBarrageDodgeAsync().Forget(); }

    private static async UniTaskVoid RunBarrageDodgeAsync()
    {
        var sb = new StringBuilder("11-05 #5 탄막의 회랑 회피\n");
        try
        {
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("런 · 플레이어 없음"); return; }
            sb.AppendLine($"대시 {p.CharacterData.dashSpeed} m/s × {p.CharacterData.dashDuration}초 · 무적 {p.CharacterData.dodgeIFrameDuration}초 · 스태미나 {p.RuntimeStats.MaxStamina} · 회피 {p.CharacterData.dodgeStaminaCost}");
            string[] modes = { "가만히", "직전 회피", "직전 회피" };
            for (int r = 0; r < modes.Length; r++)
            {
                await BarrageRoundAsync(p, r, modes[r] != "가만히", modes[r], sb);
                await UniTask.Delay(2500, ignoreTimeScale: true);
            }
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            s_invincible?.SetValue(GameRunBootstrapper.Instance?.Run?.Player, false);
            File.WriteAllText("Temp/req1105_barrage.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1105_barrage.txt");
        }
    }

    private static async UniTask BarrageRoundAsync(PlayerController p, int round, bool dodge, string label, StringBuilder sb)
    {
        EventMinigame.DebugDemo = MinigameDemo.None;
        s_invincible?.SetValue(p, false);
        p.RuntimeStats.SetHp(p.RuntimeStats.MaxHp);   // 실측 — 체력 1이면 맞음이 0이 되어 세지 않는다
        p.Stamina?.Refund(p.RuntimeStats.MaxStamina, p.RuntimeStats.MaxStamina);
        EditorApplication.ExecuteMenuItem("RelicFairy/Minigame/Play Here/Barrage (Play)");
        BarrageCorridorGame game = null;
        for (int i = 0; i < 40 && game == null; i++) { await UniTask.Delay(100, ignoreTimeScale: true); game = EventMinigame.Active as BarrageCorridorGame; }
        if (game == null) { sb.AppendLine($"{label}: 놀이가 안 섰다"); return; }

        var gt = typeof(BarrageCorridorGame);
        var hazardsF = gt.GetField("_hazards", Inst);
        var hitsF    = gt.GetField("_hits", Inst);
        var radiusF  = typeof(EventMinigame).GetField("Radius", Inst);
        var centerF  = typeof(EventMinigame).GetField("Center", Inst);
        var bulletT  = gt.GetNestedType("BulletHazard", BindingFlags.NonPublic);
        var orbT     = gt.GetNestedType("OrbHazard", BindingFlags.NonPublic);
        var hazT     = gt.GetNestedType("Hazard", BindingFlags.NonPublic);
        var anchorF  = hazT.GetField("Anchor", Inst);
        var bDirF    = bulletT.GetField("_dir", Inst);
        var bSpdF    = bulletT.GetField("_speed", Inst);
        var bTF      = bulletT.GetField("_t", Inst);
        float cost   = p.CharacterData.dodgeStaminaCost;

        int applied = 0, blocked = 0, perfect = 0, pdTriggers = 0, dodgeAsks = 0, dodgeStarts = 0;
        float lastDodgeAt = -99f;
        bool wasDodging = false;
        var hitLines = new List<string>();
        string Nearest()
        {
            var list = hazardsF.GetValue(game) as System.Collections.IList;
            string best = "장판(낙뢰 · 빛 · 고리)"; float bd = float.MaxValue;
            if (list == null) return best;
            Vector3 pp = p.transform.position; pp.y = 0f;
            foreach (var h in list)
            {
                var a = (Vector3)anchorF.GetValue(h); a.y = 0f;
                float d = Vector3.Distance(a, pp);
                if (h.GetType() == bulletT && d <= 0.7f && d < bd) { bd = d; best = "탄"; }
                else if (h.GetType() == orbT && d <= 1f && d < bd) { bd = d; best = "쫓는 구체"; }
            }
            return best;
        }
        void OnResolved(GameObject attacker, int raw, int fin, PlayerController.DamageOutcome o)
        {
            if (attacker != null) return;
            bool dg = p.LocoSM != null && p.LocoSM.CurrentId == LocoState.Dodge;
            if (o == PlayerController.DamageOutcome.Applied)
            {
                applied++;
                bool can = !dg && Time.time >= p.DodgeCooldownEnd && (p.Stamina == null || p.Stamina.Current >= cost);
                if (hitLines.Count < 14)
                    hitLines.Add($"  맞음 {game.Remaining:0.0}초 남음 · {Nearest()} · 회피 중 {dg} · 회피 시작 뒤 {(lastDodgeAt > 0f ? (Time.time - lastDodgeAt).ToString("0.00") + "초" : "-")} · 스태미나 {(p.Stamina != null ? p.Stamina.Current : -1f):0} · 회피 가능 {can}");
            }
            else if (o == PlayerController.DamageOutcome.PerfectDodge) perfect++;
            else blocked++;
        }
        void OnPd(float _) => pdTriggers++;
        p.OnDamageResolved += OnResolved;
        p.OnPerfectDodge   += OnPd;
        bool placed = false;
        float radius = 0f;
        Vector3 center = Vector3.zero;
        try
        {
            while (EventMinigame.Active == game && game != null)
            {
                if (Time.timeScale == 0f) Managers.UI?.CloseAllPopupUI();
                radius = (float)radiusF.GetValue(game);
                if (!placed && radius > 0f)
                {
                    placed = true;
                    center = (Vector3)centerF.GetValue(game);
                    var pos = new Vector3(center.x, p.transform.position.y, center.z);
                    p.transform.position = pos;
                    if (p.Rigid != null) { p.Rigid.position = pos; p.Rigid.linearVelocity = Vector3.zero; }
                }
                bool dodging = p.LocoSM != null && p.LocoSM.CurrentId == LocoState.Dodge;
                if (dodging && !wasDodging) { dodgeStarts++; lastDodgeAt = Time.time; }
                wasDodging = dodging;

                if (dodge && game.IsPlaying && !dodging && Time.time >= p.DodgeCooldownEnd && (p.Stamina == null || p.Stamina.Current >= cost))
                {
                    // 위협 — 탄이 0.06초 안에 몸(0.55 m)에 닿는가 · 구체가 0.85 m 안
                    var list = hazardsF.GetValue(game) as System.Collections.IList;
                    Vector3 pp = p.transform.position; pp.y = 0f;
                    bool threat = false;
                    if (list != null)
                        foreach (var h in list)
                        {
                            var a = (Vector3)anchorF.GetValue(h); a.y = 0f;
                            if (h.GetType() == bulletT)
                            {
                                var dir = (Vector3)bDirF.GetValue(h); float spd = (float)bSpdF.GetValue(h);
                                if ((float)bTF.GetValue(h) < 0.3f) continue;
                                Vector3 rel = a - pp;
                                float tau = Mathf.Clamp(-Vector3.Dot(rel, dir) / Mathf.Max(0.01f, spd), 0f, 0.06f);
                                if ((rel + dir * spd * tau).magnitude <= 0.6f) threat = true;
                            }
                            else if (h.GetType() == orbT && Vector3.Distance(a, pp) <= 0.85f) threat = true;
                        }
                    if (threat)
                    {
                        // 8방향 중 대시 끝자리(4 m)가 판 안이고, 0.25초 뒤 탄 자리에서 가장 먼 쪽
                        float reach = p.CharacterData.dashSpeed * p.CharacterData.dashDuration;
                        Vector3 bestDir = p.transform.forward; float bestScore = float.MinValue;
                        for (int k = 0; k < 8; k++)
                        {
                            Vector3 d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                            Vector3 e = pp + d * reach;
                            Vector3 ec = e - new Vector3(center.x, 0f, center.z);
                            if (ec.magnitude > radius - 0.8f) continue;
                            float score = float.MaxValue;
                            foreach (var h in list)
                            {
                                if (h.GetType() != bulletT && h.GetType() != orbT) continue;
                                var a = (Vector3)anchorF.GetValue(h); a.y = 0f;
                                if (h.GetType() == bulletT) a += (Vector3)bDirF.GetValue(h) * ((float)bSpdF.GetValue(h) * 0.25f);
                                score = Mathf.Min(score, Vector3.Distance(a, e));
                            }
                            if (score > bestScore) { bestScore = score; bestDir = d; }
                        }
                        if (p.Rigid != null) p.Rigid.linearVelocity = new Vector3(bestDir.x * 3f, p.Rigid.linearVelocity.y, bestDir.z * 3f);
                        p.InputBuffer.Push(Command.Dodge);
                        dodgeAsks++;
                    }
                }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }
        finally
        {
            p.OnDamageResolved -= OnResolved;
            p.OnPerfectDodge   -= OnPd;
        }
        int hits = game != null ? (int)hitsF.GetValue(game) : -1;
        sb.AppendLine($"{round + 1}판 {label}: 판 반경 {radius:0.0} m · 놀이가 센 맞음 {hits} · 실제 피해 {applied} · 회피 무적으로 막힘 {blocked} · 저스트 회피로 막힘 {perfect} · 저스트 회피 발동 {pdTriggers} · 회피 누름 {dodgeAsks} · 회피 시작 {dodgeStarts}");
        foreach (var l in hitLines) sb.AppendLine(l);
    }

    private const string FreshFromSlotPref = "SanctumDebug.FreshFromSlot";   // BaseCampSanctumDebugMenu 11번(되돌리기)과 같은 열쇠

    /// <summary>10-21 ④ 「첫 베이스캠프 진입시 다시 무형검을 얻는 연출」 — 빈 슬롯으로 새 게임 첫 진입을 재현해
    /// 대사 뒤 WorldSwordAwakening.GrantOnArrivalAsync가 [F] 없이 검을 쥐여 주나 본다(대사는 넘김 단추를 눌러 넘김).
    /// 시험 슬롯은 0 · 1 중 빈 곳만(2는 사용자 슬롯 — 쓰지 않는다) · 끝나면 원래 슬롯으로 돌아가고 시험 슬롯을 비운다.
    /// 결과 Temp/req1102_sword.txt · 사진 R1102_Sword_{n}.png.</summary>
    [MenuItem("RelicFairy/Debug/11-02 전투/④ 첫 진입 무형검 자동 받기 (Build/BaseCamp, 플레이 중)")]
    private static void RunFirstSword() { if (EditorApplication.isPlaying) RunFirstSwordAsync().Forget(); }

    private static async UniTaskVoid RunFirstSwordAsync()
    {
        var sb = new StringBuilder("11-02 ④ 첫 진입 무형검 자동 받기\n");
        var logs = new List<string>();
        float t0 = Time.realtimeSinceStartup;
        void OnLog(string msg, string st, LogType type)
        {
            if (msg.StartsWith("[WorldSwordAwakening]") || msg.StartsWith("[BaseCampBootstrapper]") || msg.StartsWith("[BaseCampFx]") || type == LogType.Exception)
                logs.Add($"  {Time.realtimeSinceStartup - t0:0.00}초 {msg.Split('\n')[0]}");
        }
        RunProgressManager rpm = null;
        int orig = -1, test = -1;
        Application.logMessageReceived += OnLog;
        try
        {
            PlayerController old = null;
            for (float t = 0f; t < 30f && (old = UnityEngine.Object.FindFirstObjectByType<PlayerController>()) == null; t += 0.5f)
                await UniTask.Delay(500, ignoreTimeScale: true);
            await UniTask.Delay(3000, ignoreTimeScale: true);
            rpm = RunProgressManager.Instance;
            var app = AppBootstrapper.Instance;
            if (rpm == null || app == null) { sb.AppendLine("RunProgressManager/AppBootstrapper 없음"); return; }
            orig = rpm.ActiveSlotIndex;
            for (int s = 0; s <= 1; s++)   // 2는 사용자 슬롯 — 쓰지 않는다
                if (s != orig && !rpm.HasLocalRun(s) && !SaveFileIO.Exists(SaveFileIO.PathFor($"meta_save_{s}.json")) && RunProgressManager.GetRetryCount(s) == 0)
                { test = s; break; }
            if (test < 0) { sb.AppendLine($"빈 시험 슬롯(0 · 1) 없음 · 원래 {orig} — 중단"); return; }
            sb.AppendLine($"원래 슬롯 {orig} → 시험 슬롯 {test}");
            EditorPrefs.SetInt(FreshFromSlotPref, orig);
            rpm.ResetSlot(test);
            rpm.ActiveSlotIndex = test;
            BackendGameData.Instance?.ApplyActiveSlot();
            Managers.Quest?.ReloadForActiveSlot();
            app.Loadout?.Clear();
            app.MarkFromIntro();
            t0 = Time.realtimeSinceStartup;
            app.RequestLoad(Define.Scene.BaseCamp);

            PlayerController p = null;
            for (float t = 0f; t < 40f && p == null; t += 0.25f)
            {
                var q = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                if (q != null && q != old) p = q;
                else await UniTask.Delay(250, ignoreTimeScale: true);
            }
            if (p == null) { sb.AppendLine("새 플레이어 없음"); return; }
            sb.AppendLine($"{Time.realtimeSinceStartup - t0:0.0}초 새 플레이어 · 로드아웃 준비 {app.Loadout?.IsReady} · 제단 {UnityEngine.Object.FindFirstObjectByType<WorldSwordAwakening>() != null}");

            var adv = typeof(UI_DialoguePopup).GetMethod("OnAdvanceClicked", Inst);
            var locks = new List<FieldInfo>();
            foreach (var n in new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" })
            { var fi = typeof(PlayerController).GetField(n, Inst); if (fi != null) locks.Add(fi); }
            int clicks = 0, shots = 0;
            float lastPopup = -1f, awakeAt = -1f, lockStart = -1f, lockMax = 0f, nextClick = 0f, nextShot = 0f;
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                float now = Time.realtimeSinceStartup;
                var popup = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>();
                if (popup != null && popup.isActiveAndEnabled)
                {
                    lastPopup = now;
                    if (now >= nextClick && adv != null) { adv.Invoke(popup, null); clicks++; nextClick = now + 0.35f; }
                }
                bool locked = false;
                foreach (var fi in locks) if (fi.GetValue(p) is bool b && b) locked = true;
                bool afterTalk = clicks > 0 && popup == null && now - lastPopup > 0.3f;
                if (afterTalk && locked) { if (lockStart < 0f) lockStart = now; lockMax = Mathf.Max(lockMax, now - lockStart); }
                else lockStart = -1f;
                if (afterTalk && shots < 10 && now >= nextShot) { CaptureFollow(p, $"Temp/ui_shots/R1102_Sword_{shots++}.png"); nextShot = now + 0.5f; }
                if (awakeAt < 0f) foreach (var l in logs) if (l.Contains("무형검 각성")) { awakeAt = now; break; }
                if (awakeAt > 0f && now - awakeAt > 1.5f) break;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            var lo = app.Loadout;
            sb.AppendLine($"대사 넘김 {clicks}번 · 마지막 대사 {(lastPopup > 0f ? (lastPopup - t0).ToString("0.0") + "초" : "없음")} · 각성 {(awakeAt > 0f ? (awakeAt - t0).ToString("0.0") + "초" : "없음")}");
            sb.AppendLine($"대사 뒤 입력 막힘 최장 {lockMax:0.00}초(기준 ≤ 1.5) · 슬롯0 {(lo?.WeaponSlot0 != null ? lo.WeaponSlot0.name : "없음")} · 받은 기록 {BaseCampBootstrapper.HasAwakenedSword()} · 제단 남음 {UnityEngine.Object.FindFirstObjectByType<WorldSwordAwakening>() != null} · 사진 {shots}장");
        }
        catch (Exception e) { sb.AppendLine("예외 " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            sb.AppendLine("로그:");
            foreach (var l in logs) sb.AppendLine(l);
            if (rpm != null && test >= 0 && rpm.ActiveSlotIndex == test)
            {
                rpm.ActiveSlotIndex = orig;
                BackendGameData.Instance?.ApplyActiveSlot();
                Managers.Quest?.ReloadForActiveSlot();
                rpm.ResetSlot(test);
                EditorPrefs.DeleteKey(FreshFromSlotPref);
                sb.AppendLine($"원래 슬롯 {orig}로 복귀 · 시험 슬롯 {test} 비움");
            }
            File.WriteAllText("Temp/req1102_sword.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/req1102_sword.txt");
        }
    }

    private static async UniTask<PlayerController> FindPlayerAsync()
    {
        PlayerController p = null;
        for (float t = 0f; t < 30f && (p = UnityEngine.Object.FindFirstObjectByType<PlayerController>()) == null; t += 0.5f)
            await UniTask.Delay(500, ignoreTimeScale: true);
        if (p == null) return null;
        await UniTask.Delay(4000, ignoreTimeScale: true);   // 거점 생성 연출 · 첫 대사가 지나가게
        // 처음 광장 연출 트리거(문턱 @BaseCampFx)가 입력을 막는다 — 실측 동안만 끈다(플레이 중 · 저장 안 됨)
        var fx = UnityEngine.Object.FindFirstObjectByType<BaseCampFxDirector>();
        if (fx != null && fx.TryGetComponent<BoxCollider>(out var fxBox)) fxBox.enabled = false;
        Managers.UI?.CloseAllPopupUI();
        p.SetInputEnabled(true);
        return p;
    }

    private static async UniTask PlaceAsync(PlayerController p, Vector3 at, Vector3 dir, CancellationToken ct)
    {
        p.CancelActions();
        var rb = p.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = at + Vector3.up * 0.05f; }
        p.transform.position = at + Vector3.up * 0.05f;
        p.RequestFacing(Quaternion.LookRotation(dir, Vector3.up));
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 착지 · 카메라 따라옴
    }

    private static readonly FieldInfo s_aimField = typeof(PlayerController).GetField("_aim", Inst);
    private static readonly FieldInfo s_clickField = typeof(PlayerAim).GetField("_lastClickedPosition", Inst);

    /// <summary>조준 객체에 「캐시된 클릭 위치」를 넣는다 — 스킬 조준(TryComputeLookDir)이 마우스보다 먼저 한 번 쓴다.</summary>
    private static void AimClick(PlayerController p, Vector3 world)
    {
        var aim = s_aimField?.GetValue(p);
        if (aim != null) s_clickField?.SetValue(aim, (Vector3?)world);
    }

    private static void AimAt(Vector3 world)
    {
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse == null || cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * 0.8f);
        if (screen.z <= 0f) return;
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    private static string Path(Transform t, Transform root)
    {
        string s = t.name;
        for (var x = t.parent; x != null && x != root; x = x.parent) s = x.name + "/" + s;
        return s;
    }

    private static void CaptureMain(Camera cam, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var rt = new RenderTexture(1280, 720, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(rt);
    }
}
#endif
