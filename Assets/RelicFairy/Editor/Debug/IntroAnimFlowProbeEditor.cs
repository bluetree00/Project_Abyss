#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-13 인트로 인물 애니 실측 — 사용자 「인트로 씬 연출에서 인물들의 애니메이션 연출이나 액션이 스킵되듯이 진행돼 어색하다」.
/// 테스트 허브에서 Game_Intro를 열어(오프닝 넘김 · 검 집기는 10-10 인트로 실측과 같은 길) 서임 → 강림 → 타락 → 배신의 일격 동안
/// 모르드레드 · 리치 · 플레이어 애니메이터를 <b>매 프레임</b> 읽어 상태가 바뀔 때마다 적는다:
/// 앞 상태가 얼마나 재생됐나(정규화 시간 · 초) · 반복 클립인가 · 섞여 넘어갔나(전환 중이었나) · 같은 순간 컷이 바뀌었나.
/// 「잘림」 = 반복이 아닌 클립이 90% 전에 다른 상태로 바뀜 · 「뚝」 = 섞임 없이 바로 바뀜.
/// 결과: Temp/intro_anim_probe.txt · Temp/ui_shots/IntroAnim_*.png(바뀌는 순간 · 0.25초 뒤) · 로그 「[IntroAnim] 끝」.
/// 실측 뒤 플레이를 멈출 것(전투에서 죽으면 인트로 완료가 기록된다).
/// </summary>
public static class IntroAnimFlowProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Track
    {
        public string   Who;
        public Animator Anim;
        public Dictionary<int, (string name, float len, bool loop, float speed)> States = new();
        public int   Hash;
        public float Norm;
        public bool  InTransition;
        public bool  Seen;
    }

    [MenuItem("RelicFairy/Debug/10-13 인트로 인물 애니 실측 — 잘림 · 뚝 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[IntroAnim] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("인트로 인물 애니 실측\n");
        int cut = 0, snap = 0, changes = 0;
        try
        {
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
            if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "Game_Intro", 60f)) { sb.AppendLine("Game_Intro 진입 실패"); return; }

            // ① 오프닝 — 대사를 넘겨 조작이 돌아올 때까지
            float t0 = Time.realtimeSinceStartup, lastSkip = 0f;
            PlayerController player = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                float t = Time.realtimeSinceStartup - t0;
                player = IntroBootstrapper.Instance?.Player;
                if (player != null && !InputLocked(player) && t > 8f) break;
                if (t - lastSkip > 1.6f) { lastSkip = t; TestHubDebugMenu.AdvanceDialogue(); }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            await UniTask.Delay(800, DelayType.Realtime);

            // ② 검 집기 — 10-10 실측과 같은 사건
            var pickup = Object.FindFirstObjectByType<IntroSwordPickup>();
            if (pickup == null) { sb.AppendLine("검 없음"); return; }
            var to = pickup.transform.position + Vector3.back * 1.2f;
            to.y = player.transform.position.y;
            player.transform.position = to;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
            await UniTask.Delay(400, DelayType.Realtime);
            typeof(IntroSwordPickup).GetField("_consumed", Any)?.SetValue(pickup, true);
            (typeof(IntroSwordPickup).GetField("OnPickup", Any)?.GetValue(pickup) as System.Delegate)?.DynamicInvoke(player);
            float p0 = Time.realtimeSinceStartup;

            // ③ 인물 애니메이터 — 상태 이름 · 클립 길이 · 반복 · 속도 표
            var director = Object.FindFirstObjectByType<IntroMordredDirector>();
            var mordred  = typeof(IntroMordredDirector).GetField("mordredActor", Any)?.GetValue(director) as GameObject;
            var lich     = typeof(IntroMordredDirector).GetField("lichActor", Any)?.GetValue(director) as GameObject;
            var tracks = new List<Track>
            {
                MakeTrack("모르드레드", mordred != null ? mordred.GetComponentInChildren<Animator>(true) : null),
                MakeTrack("리치",       null),   // 컨트롤러는 강림 때 주입된다 — 아래에서 늦게 잡는다
                MakeTrack("플레이어",   player.GetComponentInChildren<Animator>(true)),
            };
            foreach (var tr in tracks)
                if (tr.Anim != null) sb.AppendLine($"  {tr.Who} 상태 표 {tr.States.Count}개");
            foreach (var nm in new[] { "Idle2", "AttackReady", "GetHit1", "GetHit2", "GetHit3", "Attack1" })
            {
                var v = Info(tracks[0], Animator.StringToHash(nm));
                sb.AppendLine($"    모르드레드 {nm} — 클립 {v.len:0.00}초{(v.loop ? "(반복)" : "")} · 속도 {v.speed:0.##}");
            }

            // ④ 매 프레임 — 상태가 바뀌면 적는다. 조작이 돌아오면 끝.
            string lastShot = null;
            float  lastShotAt = -9f;
            float  lastEntrance = -9f;
            int    entrance = 0;
            int    pics = 0;
            sb.AppendLine("시각  | 누구 | 앞 상태 → 새 상태 | 앞 상태 재생(정규화 · 초 / 클립) | 섞임 | 같은 순간 컷 | 판정");
            while (Time.realtimeSinceStartup - p0 < 120f)
            {
                float t = Time.realtimeSinceStartup - p0;
                string shot = IntroMordredDirector.CurrentShot;
                if (shot != lastShot) { lastShot = shot; lastShotAt = t; if (shot != null) sb.AppendLine($"{t,6:0.00} ── 컷 {shot}"); }

                if (tracks[1].Anim == null && lich != null)
                {
                    var la = lich.GetComponentInChildren<Animator>(true);
                    if (la != null && la.runtimeAnimatorController != null) tracks[1] = MakeTrack("리치", la);
                }

                foreach (var tr in tracks)
                {
                    var a = tr.Anim;
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                    var cur = a.GetCurrentAnimatorStateInfo(0);
                    if (!tr.Seen) { tr.Seen = true; tr.Hash = cur.shortNameHash; tr.Norm = cur.normalizedTime; tr.InTransition = a.IsInTransition(0); continue; }
                    // 같은 상태를 처음부터 다시 틀어도(정규화 시간이 되감김) 바뀜으로 센다 — 반복 클립도 뚝 끊겨 보인다
                    if (cur.shortNameHash != tr.Hash || cur.normalizedTime + 0.05f < tr.Norm)
                    {
                        changes++;
                        var from = Info(tr, tr.Hash);
                        var toI  = Info(tr, cur.shortNameHash);
                        float played = tr.Norm * (from.len / Mathf.Max(0.01f, from.speed));
                        bool  isCut  = !from.loop && tr.Norm < 0.9f;
                        bool  isSnap = !tr.InTransition && !a.IsInTransition(0);
                        bool  onCut  = Mathf.Abs(t - lastShotAt) < 0.2f;
                        if (isCut) cut++;
                        if (isSnap) snap++;
                        string verdict = (isCut ? "잘림 " : "") + (isSnap ? "뚝" : "");
                        sb.AppendLine($"{t,6:0.00} | {tr.Who} | {from.name} → {toI.name} | {tr.Norm:0.00} · {played:0.00}초 / {from.len / Mathf.Max(0.01f, from.speed):0.00}초{(from.loop ? "(반복)" : "")} | {(isSnap ? "없음" : "있음")} | {(onCut ? "예" : "")} | {verdict}");
                        if (pics < 40 && tr.Who != "플레이어")   // 사진은 따로 돈다 — 기다리면 그동안 프레임을 놓친다
                            ShotPairAsync($"IntroAnim_{pics++:00}_{Safe(tr.Who)}_{Safe(toI.name)}").Forget();
                    }
                    tr.Hash = cur.shortNameHash;
                    tr.Norm = cur.normalizedTime;
                    tr.InTransition = a.IsInTransition(0);
                }

                // 보스 등장 연출(컷신이 끝나고 조작이 돌아오기 전) — 0.8초마다 카메라 높이 · 보스까지 거리 · 사진(84 부탁: 전경이 낮게 나오나)
                if (shot == null && lastShotAt > 0f && InputLocked(player) && t - lastEntrance > 0.8f && entrance < 8)
                {
                    lastEntrance = t;
                    var cam  = Camera.main;
                    var boss = Object.FindFirstObjectByType<RelicFairy.Monster.DeathKnightBossMonster>();
                    if (cam != null)
                    {
                        float h    = cam.transform.position.y - player.transform.position.y;
                        float dist = boss != null ? Vector3.Distance(cam.transform.position, boss.transform.position) : -1f;
                        float down = Vector3.Angle(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up), cam.transform.forward) * (cam.transform.forward.y < 0f ? 1f : -1f);
                        sb.AppendLine($"{t,6:0.00} ── 보스 등장 {entrance} · 카메라 높이(플레이어 기준) {h:0.0} m · 보스까지 {dist:0.0} m · 내려다봄 {down:0}°");
                        ShotPairAsync($"IntroAnim_E{entrance:00}_Entrance").Forget();
                    }
                    entrance++;
                }
                if (shot == null && lastShotAt > 0f && !InputLocked(player)) { sb.AppendLine($"{t,6:0.00} 조작 복귀 — 끝"); break; }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            sb.AppendLine($"상태 바뀜 {changes}번 · 잘림 {cut} · 뚝(섞임 없음) {snap}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/intro_anim_probe.txt", sb.ToString());
            Debug.Log("[IntroAnim] 끝\n" + sb);
        }
    }

    private static Track MakeTrack(string who, Animator anim)
    {
        var tr = new Track { Who = who, Anim = anim };
        if (anim == null) return tr;
        var rac = anim.runtimeAnimatorController;
        var ac = rac as UnityEditor.Animations.AnimatorController;   // 프로젝트에 같은 이름 클래스가 있다 — 전체 이름으로
        if (ac == null && rac is AnimatorOverrideController oc) ac = oc.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (ac == null) return tr;
        foreach (var cs in ac.layers[0].stateMachine.states)
        {
            var st   = cs.state;
            var clip = st.motion as AnimationClip;
            if (rac is AnimatorOverrideController ovr && clip != null && ovr[clip] != null) clip = ovr[clip];
            tr.States[Animator.StringToHash(st.name)] = (st.name, clip != null ? clip.length : 0f, clip != null && clip.isLooping, st.speed);
        }
        return tr;
    }

    private static async UniTaskVoid ShotPairAsync(string nm)
    {
        await Shot(nm + "_a");
        await UniTask.Delay(250, DelayType.Realtime);
        await Shot(nm + "_b");
        await UniTask.Delay(350, DelayType.Realtime);
        await Shot(nm + "_c");   // 0.6초 뒤 — 동작이 어디까지 갔나(일격이 내려왔나)
    }

    private static (string name, float len, bool loop, float speed) Info(Track tr, int hash)
        => tr.States.TryGetValue(hash, out var v) ? v : ($"#{hash}", 0f, true, 1f);

    private static bool InputLocked(PlayerController p)
        => p != null && (bool)(typeof(PlayerController).GetField("_inputDisabledExternally", Any)?.GetValue(p) ?? false);

    private static string Safe(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s) b.Append(char.IsLetterOrDigit(c) ? c : '_');
        return b.ToString();
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(250, DelayType.Realtime);
        }
        return false;
    }
}
#endif
