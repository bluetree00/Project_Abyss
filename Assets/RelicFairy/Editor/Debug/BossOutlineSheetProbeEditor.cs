#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-30 사용자 「보스의 테두리 강조가 아직 좀 두껍다」 — 보스방에서 부른다(테스트 허브 · 보스방 입장 뒤).
/// 등장 컷신이 끝나면 플레이어를 보스 정면 7 m에 세우고(무적) 1.5초 뒤 시간을 멈춘 채(같은 자세) 외곽선 굵기만 0 · 1 · 1.2 · 1.5 · 2 px로 바꿔 찍는다
/// (보스 Monster 레이어 렌더러 MPB _OutlineWidth). 보스 화면 영역 · 조각 수 · 무적 · 외곽선 플래시 값을 적는다.
/// 결과 Temp/boss_outline/{보스}_{굵기}.png · {보스}.txt · 로그 「[BossOutline] 끝」.
/// </summary>
public static class BossOutlineSheetProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Dir = "Temp/boss_outline";
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int HitFlashId     = Shader.PropertyToID("_HitFlash");
    private static readonly int GuiZTestId     = Shader.PropertyToID("unity_GUIZTestMode");
    private static readonly float[] Widths = { 0f, 1f, 1.2f, 1.5f, 2f };

    [MenuItem("RelicFairy/Debug/10-30 보스 외곽선 굵기 비교 사진 (보스방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossOutline] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder();
        var hold = new object();
        string name = "boss";
        try
        {
            var boss = await WakeBossAsync(sb);
            if (boss == null) return;
            name = boss.GetType().Name.Replace("Monster", "");
            PlaceFacing(boss, boss is DragonBossMonster ? 16f : 7f);   // 화룡은 7 m면 몸이 화면 밖(첫 판)
            await UniTask.Delay(1500, DelayType.Realtime);

            int monsterLayer = LayerMask.NameToLayer("Monster");
            var rends = new List<Renderer>();
            int skinned = 0, meshes = 0;
            foreach (var r in boss.GetComponentsInChildren<Renderer>(false))
            {
                if (r.gameObject.layer != monsterLayer || !r.enabled) continue;
                if (r is SkinnedMeshRenderer) skinned++; else if (r is MeshRenderer) meshes++; else continue;
                rends.Add(r);
            }

            TimeScaleArbiter.Acquire(hold, 0f, TimeScaleArbiter.Priority.Pause);
            await UniTask.Yield(PlayerLoopTiming.Update);

            var cam = Camera.main;
            var rect = ScreenRect(cam, rends);
            var mpb = new MaterialPropertyBlock();
            float flash = 0f;
            if (rends.Count > 0) { rends[0].GetPropertyBlock(mpb); flash = mpb.GetFloat(HitFlashId); }
            sb.AppendLine($"{name} — Monster 레이어 렌더러 {rends.Count}(스킨 {skinned} · 메시 {meshes}) · 무적 {boss.IsDamageImmuneNow} · 외곽선 플래시 {flash:0.00} · 화면 {Screen.width}×{Screen.height} · 보스 영역 {rect.xMin:0} {rect.yMin:0} {rect.xMax:0} {rect.yMax:0} · 카메라 거리 {(cam != null ? Vector3.Distance(cam.transform.position, boss.transform.position) : -1f):0.0} m");
            foreach (var r in rends) sb.AppendLine($"  조각 {r.name} ({r.GetType().Name} · 재질 {r.sharedMaterials.Length})");

            foreach (float w in Widths)
            {
                for (int f = 0; f < 3; f++)
                {
                    SetWidth(rends, mpb, w);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                SetWidth(rends, mpb, w);
                ScreenCapture.CaptureScreenshot($"{Dir}/{name}_{w:0.0}.png");
                for (int f = 0; f < 3; f++) { SetWidth(rends, mpb, w); await UniTask.Yield(PlayerLoopTiming.Update); }
            }
            SetWidth(rends, mpb, 2f);   // 재질 값과 같게 되돌림
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            TimeScaleArbiter.Release(hold);
            File.WriteAllText($"{Dir}/{name}.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossOutline] 끝\n{sb}");
        }
    }

    /// <summary>보스 깨우기 — Fight Sim과 같은 길(입구 트리거는 Enter 뒤 Exit가 있어야 발동). 등장 컷신이 끝나고 2초 뒤 돌려준다.</summary>
    private static async UniTask<MonsterBase> WakeBossAsync(StringBuilder sb)
    {
        TestHubDebugMenu.StepIntoBossEntrance();
        await UniTask.Delay(600, DelayType.Realtime);
        TestHubDebugMenu.StepPastBossEntrance();
        MonsterBase boss = null;
        for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until && boss == null; )
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead && m.Grade == MonsterGrade.Boss && m.gameObject.activeInHierarchy) { boss = m; break; }
            if (boss == null) await UniTask.Delay(250, DelayType.Realtime);
        }
        if (boss == null) { sb.AppendLine("보스 없음"); return null; }
        if (boss is LichMonster)
        {
            // 리치는 플레이어가 감지 반경 안으로 와야 깬다(Fight Sim 등장 촬영과 같이 4 m 앞)
            var pl = GameRunBootstrapper.Instance?.Run?.Player;
            if (pl != null)
            {
                Vector3 away = pl.transform.position - boss.transform.position; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : -boss.transform.forward;
                Vector3 to = boss.transform.position + away * 4f; to.y = pl.transform.position.y;
                pl.transform.position = to;
                if (pl.Rigid != null) { pl.Rigid.position = to; pl.Rigid.linearVelocity = Vector3.zero; }
            }
            await UniTask.Delay(3000, DelayType.Realtime);
        }
        for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until; )
        {
            var r = GameRunBootstrapper.Instance?.Run;
            bool cut = r != null && r.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene);
            if (!cut) break;
            await UniTask.Delay(250, DelayType.Realtime);
        }
        await UniTask.Delay(2000, DelayType.Realtime);
        return boss;
    }

    /// <summary>플레이어(무적)를 보스 정면 dist m에 보스를 보게 세운다.</summary>
    private static void PlaceFacing(MonsterBase boss, float dist)
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) return;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        Vector3 fw = boss.transform.forward; fw.y = 0f; fw = fw.sqrMagnitude > 0.01f ? fw.normalized : Vector3.forward;
        Vector3 at = boss.transform.position + fw * dist;
        if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
        var rot = Quaternion.LookRotation(-fw);
        p.transform.SetPositionAndRotation(at, rot);
        if (p.Rigid != null) { p.Rigid.position = at; p.Rigid.rotation = rot; p.Rigid.linearVelocity = Vector3.zero; }
    }

    // ── 10-30 데미지 숫자가 큰 보스 몸에 가리는가 ──
    // 보스 숫자는 MonsterBase가 발밑 + 1.2 m에 띄운다 · 팝업은 월드 캔버스 TMP라 깊이 검사(LEqual)를 한다 → 큰 몸 안이면 묻힐 수 있다.
    // 실제 피격과 같은 자리 · 같은 대상 열쇠로 0.14초 간격 네 대(마지막 치명 — 합산 창 0.12초보다 길어 영수증 줄이 된다)를 띄우고 멈춘 채 세 장:
    //   a 지금 그대로 · b 숫자 재질 ZTest Always(맨 위) · c 보스 렌더러 끔(가림 없을 때 정답). a와 c가 다르면 몸이 숫자를 가린 것.
    private const string DmgDir = "Temp/boss_dmg";
    private static readonly float[] DmgValues = { 84f, 126f, 97f, 342f };

    [MenuItem("RelicFairy/Debug/10-30 보스 데미지 숫자 가림 사진 (보스방, Play)")]
    private static void RunDmg()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossDmg] 플레이 중에"); return; }
        DmgAsync().Forget();
    }

    private static async UniTaskVoid DmgAsync()
    {
        Directory.CreateDirectory(DmgDir);
        var sb = new StringBuilder();
        var hold = new object();
        string name = "boss";
        var mats = new List<Material>();
        var origZ = new List<float>();
        var hidden = new List<Renderer>();
        try
        {
            var boss = await WakeBossAsync(sb);
            if (boss == null) return;
            name = boss.GetType().Name.Replace("Monster", "");
            // 컷신 동안은 Spawn이 숫자를 숨긴다 — 첫 판 기사 · 리치는 HUD 모드가 풀린 뒤에도 InCutscene이 남아 숫자가 하나도 안 떴다
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until && GameRunBootstrapper.Instance?.Run?.InCutscene == true; )
                await UniTask.Delay(250, DelayType.Realtime);
            await UniTask.Delay(1000, DelayType.Realtime);
            int bossId = boss.GetInstanceID();
            foreach (var f in new[] { "s_normalMat", "s_critMat" })
                if (typeof(DamagePopup).GetField(f, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is Material m && m != null) { mats.Add(m); origZ.Add(m.GetFloat(GuiZTestId)); }

            float[] dists = boss is DragonBossMonster ? new[] { 6f, 16f } : new[] { 3f, 8f };
            foreach (float d in dists)
            {
                PlaceFacing(boss, d);
                DamagePopupSpawner.Spawn(boss.transform.position + Vector3.up * 1.2f, 1f, false, 0);   // 첫 판 프리팹 로드 · 재질 만들기(1.5초 뒤엔 사라짐)
                await UniTask.Delay(1500, DelayType.Realtime);
                if (mats.Count == 0)
                    foreach (var f in new[] { "s_normalMat", "s_critMat" })
                        if (typeof(DamagePopup).GetField(f, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is Material m && m != null) { mats.Add(m); origZ.Add(m.GetFloat(GuiZTestId)); }

                Vector3 anchor = boss.transform.position + Vector3.up * 1.2f;   // MonsterBase.TakeDamage와 같은 자리
                for (int i = 0; i < DmgValues.Length; i++)
                {
                    anchor = boss.transform.position + Vector3.up * 1.2f;
                    DamagePopupSpawner.Spawn(anchor, DmgValues[i], i == DmgValues.Length - 1, bossId);
                    await UniTask.Delay(140, DelayType.Realtime);
                }
                TimeScaleArbiter.Acquire(hold, 0f, TimeScaleArbiter.Priority.Pause);
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);

                var cam = Camera.main;
                hidden.Clear();
                var body = new List<Renderer>();
                foreach (var r in boss.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r.forceRenderingOff) continue;
                    hidden.Add(r);
                    if (r is SkinnedMeshRenderer || r is MeshRenderer) body.Add(r);
                }
                var rect = ScreenRect(cam, body);
                Bounds b = body.Count > 0 ? body[0].bounds : new Bounds(boss.transform.position, Vector3.zero);
                foreach (var r in body) b.Encapsulate(r.bounds);
                Vector3 sp = cam != null ? cam.WorldToScreenPoint(anchor) : Vector3.zero;
                string tag = $"{name}_{d:0}m";
                sb.AppendLine($"{tag} — 숫자 자리 {sp.x:0} {Screen.height - sp.y:0} · 보스 영역 {rect.xMin:0} {rect.yMin:0} {rect.xMax:0} {rect.yMax:0} · 화면 {Screen.width}×{Screen.height}" +
                              $" · 카메라 거리 {(cam != null ? Vector3.Distance(cam.transform.position, anchor) : -1f):0.0} m · 보스 키 {b.size.y:0.0} m(숫자 1.2 m) · 숫자 자리가 몸 상자 안 {b.Contains(anchor)} · 재질 {mats.Count}(ZTest {(origZ.Count > 0 ? origZ[0] : -1f):0}) · 컷신 {GameRunBootstrapper.Instance?.Run?.InCutscene}");

                ScreenCapture.CaptureScreenshot($"{DmgDir}/{tag}_a.png");
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                foreach (var m in mats) m.SetFloat(GuiZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                ScreenCapture.CaptureScreenshot($"{DmgDir}/{tag}_b.png");
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                for (int i = 0; i < mats.Count; i++) mats[i].SetFloat(GuiZTestId, origZ[i]);
                foreach (var r in hidden) r.forceRenderingOff = true;
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                ScreenCapture.CaptureScreenshot($"{DmgDir}/{tag}_c.png");
                for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
                hidden.Clear();
                TimeScaleArbiter.Release(hold);
                await UniTask.Delay(1500, DelayType.Realtime);   // 남은 숫자 흐려짐
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            for (int i = 0; i < mats.Count; i++) if (mats[i] != null) mats[i].SetFloat(GuiZTestId, origZ[i]);
            TimeScaleArbiter.Release(hold);
            File.WriteAllText($"{DmgDir}/{name}.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossDmg] 끝\n{sb}");
        }
    }

    // ── 10-31 데미지 숫자 · 보스바 연출 — 실제 피해 경로(TakeDamage · TakeSynergyDamage)로 연타를 넣고 시간 순서대로 찍는다 ──
    // 0.18초 간격 10타(4 · 8번째 치명 · 3의 배수마다 불 추가 피해) · 한 타 = 최대 체력 1.5% + 60. 보스 근접 3 m(화룡 6 m).
    // 사진: 3 · 7 · 10타 직후, 끝나고 0.25 · 0.8 · 1.7초 — 영수증 줄 · 누적 세어 올림 · 보스바 미끄러짐 · 깎인 몫 띠.
    private const string Dmg31Dir = "Temp/boss_dmg31";

    [MenuItem("RelicFairy/Debug/10-31 데미지 숫자 · 보스바 연출 사진 (보스방, Play)")]
    private static void RunDmg31()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossDmg31] 플레이 중에"); return; }
        Dmg31Async().Forget();
    }

    private static async UniTaskVoid Dmg31Async()
    {
        Directory.CreateDirectory(Dmg31Dir);
        var sb = new StringBuilder();
        string name = "boss";
        try
        {
            var boss = await WakeBossAsync(sb);
            if (boss == null) return;
            name = boss.GetType().Name.Replace("Monster", "");
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until && GameRunBootstrapper.Instance?.Run?.InCutscene == true; )
                await UniTask.Delay(250, DelayType.Realtime);
            PlaceFacing(boss, boss is DragonBossMonster ? 6f : 3f);
            await UniTask.Delay(1500, DelayType.Realtime);

            var pl = GameRunBootstrapper.Instance?.Run?.Player;
            GameObject src = pl != null ? pl.gameObject : null;
            float hit = boss.EffectiveMaxHp * 0.015f + 60f;
            int hp0 = boss.CurrentHp;
            sb.AppendLine($"{name} — 최대 {boss.EffectiveMaxHp} · 시작 {hp0} · 한 타 {hit:0} · 무적 {boss.IsDamageImmuneNow}");
            for (int i = 1; i <= 10; i++)
            {
                bool crit = i % 4 == 0;
                boss.TakeDamage(hit * (crit ? 1.8f : 1f) * UnityEngine.Random.Range(0.9f, 1.1f), src, 0f, crit);
                if (i % 3 == 0) boss.TakeSynergyDamage(hit * 0.3f, src, 1f, false, DamageKind.Synergy, RuneElement.Fire);
                if (i == 3 || i == 7 || i == 10)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    ScreenCapture.CaptureScreenshot($"{Dmg31Dir}/{name}_hit{i:00}.png");
                }
                await UniTask.Delay(180, DelayType.Realtime);
            }
            foreach (var (ms, tag) in new[] { (70, "after0.25"), (550, "after0.8"), (900, "after1.7") })
            {
                await UniTask.Delay(ms, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot($"{Dmg31Dir}/{name}_{tag}.png");
            }
            sb.AppendLine($"  끝 체력 {boss.CurrentHp}(−{hp0 - boss.CurrentHp}) · 죽음 {boss.IsDead}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"{Dmg31Dir}/{name}.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossDmg31] 끝\n{sb}");
        }
    }

    // ── 지금 화면 한 장(10-30 데미지 숫자 점검 — 보스전 Fight Sim 중 실제 숫자) ──
    [MenuItem("RelicFairy/Debug/10-30 지금 화면 사진 (Play)")]
    private static void ShotNow()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ShotNow] 플레이 중에"); return; }
        Directory.CreateDirectory("Temp/shot_now");
        string path = $"Temp/shot_now/{DateTime.Now:HHmmss}.png";
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[ShotNow] {path}");
    }

    // ── 일반 몬스터(슬라임 · 오크) — 바깥 테두리만 전후 비교용 · 굵기는 그대로(재질 2 px) ──
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-30 일반 몬스터 외곽선 사진 (테스트 허브, Play)")]
    private static void RunNormals()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossOutline] 플레이 중에"); return; }
        s_armed = true;
        Application.logMessageReceived -= OnRunAuto;
        Application.logMessageReceived += OnRunAuto;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnRunAuto(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnRunAuto;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            NormalsAsync().Forget();
        };
    }

    private static async UniTaskVoid NormalsAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder();
        var hold = new object();
        try
        {
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("런 플레이어 없음"); return; }
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, DelayType.Realtime);
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            Vector3 fw = p.transform.forward; fw.y = 0f; fw = fw.sqrMagnitude > 0.01f ? fw.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fw);
            var spawned = new List<MonsterBase>();
            foreach (var (key, side) in new[] { ("Slime/Slime", -1.4f), ("Orc/Orc", 1.4f) })
            {
                Vector3 at = p.transform.position + fw * 4.5f + right * side;
                if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
                var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, at, Quaternion.LookRotation(-fw));
                if (m != null) { spawned.Add(m); m.Status.ApplyCc("outline_probe_hold", 9999f); }
            }
            await UniTask.Delay(4000, DelayType.Realtime);   // 디졸브 + 표기 레이어(MarkupRevealDelay 1.3초)

            int monsterLayer = LayerMask.NameToLayer("Monster");
            var rends = new List<Renderer>();
            foreach (var m in spawned)
                foreach (var r in m.GetComponentsInChildren<Renderer>(false))
                    if (r.gameObject.layer == monsterLayer && r.enabled && (r is SkinnedMeshRenderer || r is MeshRenderer)) rends.Add(r);
            TimeScaleArbiter.Acquire(hold, 0f, TimeScaleArbiter.Priority.Pause);
            await UniTask.Yield(PlayerLoopTiming.Update);
            var rect = ScreenRect(Camera.main, rends);
            sb.AppendLine($"Normals — 몬스터 {spawned.Count} · Monster 레이어 렌더러 {rends.Count} · 화면 {Screen.width}×{Screen.height} · 보스 영역 {rect.xMin:0} {rect.yMin:0} {rect.xMax:0} {rect.yMax:0}");
            for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
            ScreenCapture.CaptureScreenshot($"{Dir}/Normals_2.0.png");
            for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            TimeScaleArbiter.Release(hold);
            File.WriteAllText($"{Dir}/Normals.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BossOutline] 끝\n{sb}");
        }
    }

    private static void SetWidth(List<Renderer> rends, MaterialPropertyBlock mpb, float w)
    {
        foreach (var r in rends)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(OutlineWidthId, w);
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>보스 렌더러 경계 상자를 화면 픽셀 사각형으로(왼쪽 아래 원점 → 사진 좌표는 위에서 아래라 y를 뒤집는다).</summary>
    private static Rect ScreenRect(Camera cam, List<Renderer> rends)
    {
        if (cam == null || rends.Count == 0) return Rect.zero;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            var s = cam.WorldToScreenPoint(c);
            if (s.z <= 0f) continue;
            x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x);
            float y = Screen.height - s.y;
            y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        x0 = Mathf.Clamp(x0, 0, Screen.width); x1 = Mathf.Clamp(x1, 0, Screen.width);
        y0 = Mathf.Clamp(y0, 0, Screen.height); y1 = Mathf.Clamp(y1, 0, Screen.height);
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }
}
#endif
