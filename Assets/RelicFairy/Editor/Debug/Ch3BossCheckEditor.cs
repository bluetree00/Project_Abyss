#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 10-22 A0 · A1 · A1b — 챕터3 보스(데스나이트) 전투가 열린 뒤(「Fight Sim/Entrance Shots」 끝) 이어서 돌린다.
/// · A0 「챕터3 보스로 이동했을 때 플레이어가 이동할 수 없다」 — 성흔석과 플레이어 거리 · W · S를 1초씩 눌러 실제 이동 거리.
/// · A1 「챕터3에서는 보스가 여전히 떠 있다」 — 품질 2(에디터 · 최고 LOD 1)와 1(빌드 기본 · LOD0 씀)에서 기사 옆 발 높이로 찍는다 · 발밑 바닥 · 몸 아래.
/// · A1b 「근접 타격 피해가 제대로 안 들어간다」 — 가운데 성흔석 앞 1.4 m에서 평타 6초 · 기사 체력 줄어든 양 · 성흔석이 식은 시간.
/// 결과: Logs/boss_fight_sim/ch3_check_*/check.txt · 사진 · 로그 「[Ch3Check] 끝」.
/// </summary>
public static class Ch3BossCheckEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Boss/10-22 Ch3 Boss Check (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Ch3Check] 플레이 중에(챕터3 보스 전투가 열린 뒤)"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"ch3_check_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("챕터3 보스 점검(A0 이동 · A1 뜸 · A1b 근접 피해)\n");
        int q0 = QualitySettings.GetQualityLevel();
        try
        {
            var boss = UnityEngine.Object.FindFirstObjectByType<DeathKnightBossMonster>();
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (boss == null || p == null) { sb.AppendLine($"기사 {boss != null} · 플레이어 {p != null}"); return; }
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            float t0 = Time.realtimeSinceStartup;
            DKResonanceStone[] stones = Array.Empty<DKResonanceStone>();
            while (Time.realtimeSinceStartup - t0 < 20f)
            {
                stones = UnityEngine.Object.FindObjectsByType<DKResonanceStone>(FindObjectsSortMode.None);
                if (stones.Length > 0) break;
                await UniTask.Delay(250, DelayType.Realtime);
            }
            sb.AppendLine($"성흔석 {stones.Length}개 · 품질 {q0}({QualitySettings.names[q0]})");

            // ① A0 — 겹침 · 실제 이동
            foreach (var s in stones)
                sb.AppendLine($"  성흔석 {s.transform.position} · 플레이어까지 {Flat(s.transform.position - p.transform.position).magnitude:0.00} m");
            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector3 a = p.transform.position;
                await HoldKey(kb, Key.W, 1f);
                Vector3 b = p.transform.position;
                await HoldKey(kb, Key.S, 1f);
                Vector3 c = p.transform.position;
                sb.AppendLine($"A0 이동 — W 1초 {Flat(b - a).magnitude:0.00} m · S 1초 {Flat(c - b).magnitude:0.00} m(0이면 갇힘)");
            }

            // ② A1 — 품질 단계별 기사 발밑(옆 6 m · 발 높이)
            foreach (int q in new[] { 2, 1 })
            {
                QualitySettings.SetQualityLevel(q, true);
                await UniTask.Delay(600, DelayType.Realtime);
                Vector3 bp = boss.transform.position;
                float ground = Physics.Raycast(bp + Vector3.up * 3f, Vector3.down, out var gh, 10f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore) ? gh.point.y : float.NaN;
                float body = float.NaN;
                var smr = boss.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
                if (smr.Length > 0) { var bb = smr[0].bounds; foreach (var r in smr) bb.Encapsulate(r.bounds); body = bb.min.y; }
                sb.AppendLine($"A1 품질 {q}({QualitySettings.names[q]} · lodBias {QualitySettings.lodBias} · 최고 LOD {QualitySettings.maximumLODLevel}) — 기사 {bp.y:0.00} · 발밑 바닥 {ground:0.00}{(gh.collider != null ? $"({gh.collider.name})" : "")} · 몸 아래 {body:0.00}");
                Vector3 side = boss.transform.right; side.y = 0f; side.Normalize();
                Shoot(Path.Combine(dir, $"A1_q{q}.png"), bp + side * 6f + Vector3.up * 0.8f, bp + Vector3.up * 0.6f);
                Shoot(Path.Combine(dir, $"A1_q{q}_far.png"), bp + side * 14f + Vector3.up * 3f, bp + Vector3.up * 0.8f);
            }
            QualitySettings.SetQualityLevel(q0, true);

            // ③ A1b — 가운데 성흔석 앞 평타 6초
            if (stones.Length > 0)
            {
                Vector3 bpos = boss.transform.position;
                var center = stones.OrderBy(s => Vector3.Angle(Flat(s.transform.position - bpos), Flat(p.transform.position - bpos))).First();
                Vector3 outward = Flat(center.transform.position - bpos).normalized;
                Vector3 at = center.transform.position + outward * 1.4f;
                at.y = p.transform.position.y;
                p.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-outward));
                if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
                await UniTask.Delay(500, DelayType.Realtime);
                int hp0 = boss.CurrentHp;
                float cool = 0f, tA = Time.realtimeSinceStartup;
                var mouse = Mouse.current;
                int swings = 0;
                while (Time.realtimeSinceStartup - tA < 6f && mouse != null)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                    await UniTask.Delay(90, DelayType.Realtime);
                    InputSystem.QueueStateEvent(mouse, new MouseState());
                    await UniTask.Delay(110, DelayType.Realtime);
                    swings++;
                    if (center != null && center.IsCooling) cool += 0.2f;
                }
                int hp1 = boss.CurrentHp;
                sb.AppendLine($"A1b 근접 — 가운데 성흔석 앞 1.4 m · 평타 누름 {swings}번 · 6초 · 기사 체력 {hp0} → {hp1}(−{hp0 - hp1}) · 성흔석 식음 약 {cool:0.0}초 · 무기 {p.GetType().GetProperty("CurrentWeaponName", Inst)?.GetValue(p) ?? "-"}");
                Shoot(Path.Combine(dir, "A1b_stone.png"), at - outward * 2.5f + Vector3.up * 3.5f, center.transform.position + Vector3.up * 1f);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            QualitySettings.SetQualityLevel(q0, true);
            File.WriteAllText(Path.Combine(dir, "check.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Ch3Check] 끝 — {dir}\n{sb}");
        }
    }

    private static async UniTask HoldKey(Keyboard kb, Key key, float seconds)
    {
        float t = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t < seconds)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        InputSystem.QueueStateEvent(kb, new KeyboardState());
        await UniTask.Delay(200, DelayType.Realtime);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static void Shoot(string path, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("~Ch3CheckCam");
        var cam = go.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.1f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        int w = 1280, h = 720;
        var rtex = new RenderTexture(w, h, 24);
        cam.targetTexture = rtex;
        cam.Render();
        cam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rtex;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rtex);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
    }
}
#endif
