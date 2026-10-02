#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 평타 「추적 전진」 실측 — 베이스캠프 허수아비(TrainingDummyMonster)에서 거리별로 서서 평타 1회를 넣고,
/// 베기 전까지 얼마나·얼마나 빨리 끌려가는지 잰다(09-30 사용자: 「멀리서도 몬스터한테 순간이동하는 것처럼 느껴진다」).
/// 조준은 클릭 위치 캐시(PlayerAim._lastClickedPosition)를 허수아비로 넣어 마우스 위치와 무관하게 맞춘다.
/// 결과: Temp/lunge_probe.txt + 콘솔 「[LungeProbe]」. 플레이어는 허수아비 앞으로 옮겨진다(세이브·씬 변경 없음).
/// </summary>
public static class PlayerLungeProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly float[] Distances = { 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 8.0f };

    [MenuItem("RelicFairy/Debug/공격 전진 추적 실측 (BaseCamp · Play)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[LungeProbe] 플레이 모드에서만."); return; }
        var p = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var dummy = UnityEngine.Object.FindFirstObjectByType<TrainingDummyMonster>();
        if (p == null || dummy == null) { Debug.LogWarning($"[LungeProbe] 준비 안 됨 — player={p != null} dummy={dummy != null}"); return; }
        RunAsync(p, dummy.transform).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p, Transform dummy)
    {
        var sb = new StringBuilder();
        try
        {
            var act = GetField<LayerStateMachine<ActState>>(p, "_actSM");
            var aim = GetField<object>(p, "_aim");
            var clickField = aim.GetType().GetField("_lastClickedPosition", Inst);
            // 허수아비에서 광장 가운데 쪽(열린 바닥)으로 물러선 자리들
            Vector3 away = p.transform.position - dummy.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            float y = p.Rigid.position.y;

            sb.AppendLine($"무기 {p.WeaponManager?.CurrentWeaponData?.weaponType.ToString() ?? "없음"} · 허수아비 {dummy.position}");
            sb.AppendLine("거리(m)  계획전진  실제전진  걸린시간(s)  평균속도  최고속도(m/s)  프레임  남은거리  슬래시시각  클립길이  애니속도");
            foreach (float d in Distances)
            {
                // 이전 공격이 끝나길 기다린다
                float tw = Time.unscaledTime;
                while (act.CurrentId != ActState.None && Time.unscaledTime - tw < 3f) await UniTask.Yield(PlayerLoopTiming.Update);
                await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.UnscaledDeltaTime);
                if (!Application.isPlaying || p == null) return;

                Vector3 pos = dummy.position + away * d;
                pos.y = y;
                var rot = Quaternion.LookRotation(-away);
                p.transform.SetPositionAndRotation(pos, rot);
                p.Rigid.position = pos;
                p.Rigid.rotation = rot;
                p.Rigid.linearVelocity = Vector3.zero;
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), DelayType.UnscaledDeltaTime);

                bool slashed = false;
                void OnSlash(int _) => slashed = true;
                p.EventReceiver.OnEffectStep += OnSlash;
                try
                {
                    Vector3 start = p.Rigid.position;
                    clickField.SetValue(aim, (Vector3?)dummy.position);
                    p.InputBuffer.Clear();
                    p.InputBuffer.Push(Command.Light);
                    float t0 = Time.unscaledTime;
                    while (act.CurrentId != ActState.Attack && Time.unscaledTime - t0 < 1.5f) await UniTask.Yield(PlayerLoopTiming.Update);
                    if (act.CurrentId != ActState.Attack) { sb.AppendLine($"{d,5:F1}  공격이 시작되지 않음"); continue; }
                    p.InputBuffer.Clear();

                    float planned = 0f, peak = 0f, moveStart = -1f, moveEnd = -1f;
                    int frames = 0;
                    Vector3 prev = p.Rigid.position;
                    float t1 = Time.unscaledTime;
                    float slashNorm = GetStateField<float>(act, "_slashNorm");
                    var st = p.Anim.GetCurrentAnimatorStateInfo(0);
                    while (Time.unscaledTime - t1 < 1.2f && !slashed)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update);
                        planned = Mathf.Max(planned, GetStateField<float>(act, "_effectiveStepDistance"));
                        Vector3 cur = p.Rigid.position;
                        float step = FlatDist(cur, prev);
                        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                        if (step > 0.005f)
                        {
                            if (moveStart < 0f) moveStart = Time.unscaledTime - dt;
                            moveEnd = Time.unscaledTime;
                            frames++;
                            peak = Mathf.Max(peak, step / dt);
                        }
                        prev = cur;
                        st = p.Anim.GetCurrentAnimatorStateInfo(0);
                    }
                    float moved = FlatDist(p.Rigid.position, start);
                    float dur = moveStart >= 0f ? moveEnd - moveStart : 0f;
                    float left = FlatDist(p.Rigid.position, dummy.position);
                    sb.AppendLine($"{d,5:F1}    {planned,5:F2}     {moved,5:F2}      {dur,5:F3}       {(dur > 0f ? moved / dur : 0f),5:F1}      {peak,5:F1}        {frames,2}     {left,5:F2}     {slashNorm,5:F2}      {st.length,5:F2}     {p.Anim.speed,4:F2}{(slashed ? "" : "  (슬래시 이벤트 없음)")}");
                }
                finally { p.EventReceiver.OnEffectStep -= OnSlash; }
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/lunge_probe.txt", sb.ToString());
            Debug.Log("[LungeProbe] 완료 → Temp/lunge_probe.txt\n" + sb);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LungeProbe] 예외: " + e.Message + "\n" + sb);
        }
    }

    private static T GetField<T>(object target, string name)
    {
        var f = target.GetType().GetField(name, Inst);
        if (f == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)f.GetValue(target);
    }

    /// <summary>행동 상태머신의 현재 상태 객체에서 비공개 필드를 읽는다 — 그 상태에 없는 필드면 기본값(공격이 먼저 끝난 경우).</summary>
    private static T GetStateField<T>(LayerStateMachine<ActState> sm, string name)
    {
        var state = GetField<object>(sm, "_current");
        var f = state?.GetType().GetField(name, Inst);
        return f != null ? (T)f.GetValue(state) : default;
    }

    private static float FlatDist(Vector3 a, Vector3 b) { Vector3 d = a - b; d.y = 0f; return d.magnitude; }
}
#endif
