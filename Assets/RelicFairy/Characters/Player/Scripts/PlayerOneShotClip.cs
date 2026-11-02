using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 클립 하나를 플레이어 Animator 위에 잠깐 덮어 재생한다 — 컨트롤러에 상태를 더하지 않는다(11-02 Q3 무기 받는 동작 · Q5 쓰러짐).
/// 출력 가중치로 들어가고(fadeIn) 끝나기 전에 빠진다(fadeOut). hold = 끝 자세로 멈춘 채 둔다(쓰러짐 — 씬이 바뀌어 플레이어가 사라질 때까지).
/// unscaled = 시간 배율 0(주변을 멈춘 사망 순간)에서도 실제 시간으로 돈다 — Animator도 실제 시간으로 돌린다(되돌리지 않음 · 사망 뒤 씬 전환).
/// </summary>
public static class PlayerOneShotClip
{
    public static async UniTask PlayAsync(Animator animator, AnimationClip clip, float fadeIn, float fadeOut, bool hold, bool unscaled, CancellationToken ct)
    {
        if (animator == null || clip == null) return;
        if (unscaled) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        var graph = PlayableGraph.Create("~PlayerOneShot");
        graph.SetTimeUpdateMode(unscaled ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "oneShot", animator);
        var play = AnimationClipPlayable.Create(graph, clip);
        play.SetSpeed(0);   // 시간은 여기서 정한다(끝 자세 유지 · 실제 시간)
        output.SetSourcePlayable(play);
        output.SetWeight(0f);
        graph.Play();
        float t = 0f, len = Mathf.Max(0.01f, clip.length);
        try
        {
            while (true)
            {
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                float w = fadeIn > 0f ? Mathf.Clamp01(t / fadeIn) : 1f;
                if (!hold && fadeOut > 0f && t > len - fadeOut) w = Mathf.Min(w, Mathf.Clamp01((len - t) / fadeOut));
                output.SetWeight(w);
                play.SetTime(Mathf.Min(t, len - 0.001f));
                if (!hold && t >= len) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
