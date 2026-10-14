using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 리치 추방(10-11)의 몸 자세 — 플레이어 Animator에 <see cref="PlayableGraph"/>로 클립 하나를 직접 튼다.
/// 애니메이터 컨트롤러에 상태를 더하지 않는다(컨트롤러 에셋 수정 · 무기 세트 오버라이드가 공용 클립을 덮어쓰는 위험이 없다).
/// 출력 가중치로 컨트롤러 위에 겹치고, 끝나면 그래프를 지워 원래 컨트롤러로 돌아간다. 시간은 실시간(unscaled).
/// </summary>
public sealed class ExilePose
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>추락 자세 기본값 — 플레이어 컨트롤러(JumpBlend)에 이미 물린 점프 루프. @GameRun의 추락 클립 칸이 비었을 때.</summary>
    public const string DefaultFallClip = "GhostSamurai_APose_Jump_Loop_Inplace";
    /// <summary>착지 기본값 — 큰 피격(HitLand 상태 클립). 엎드림 · 일어남 클립 칸이 비었을 때.</summary>
    public const string DefaultLandClip = "GhostSamurai_APose_Large_Hit_Inplace";

    // ── Private ───────────────────────────────────────────────────
    private PlayableGraph           _graph;
    private AnimationPlayableOutput _output;
    private AnimationClipPlayable   _playable;
    private AnimationClip           _clip;
    private float                   _weight;

    // ── Properties ────────────────────────────────────────────────
    public bool          IsValid  => _graph.IsValid();
    public AnimationClip Clip     => _clip;
    public float         Length   => _clip != null ? _clip.length : 0f;
    public float         ClipTime => IsValid ? (float)_playable.GetTime() : 0f;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>클립을 튼다. 애니메이터 · 클립이 없으면 null. <paramref name="speed"/> 0이면 그 프레임에 멈춰 둔다.</summary>
    public static ExilePose Play(Animator animator, AnimationClip clip, float startTime = 0f, float speed = 1f, float weight = 1f)
    {
        if (animator == null || clip == null) return null;

        var pose = new ExilePose();
        pose._graph = PlayableGraph.Create("ExilePose");
        pose._graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
        pose._output = AnimationPlayableOutput.Create(pose._graph, "ExilePose", animator);
        pose.Connect(clip, startTime, speed);
        pose.SetWeight(weight);
        pose._graph.Play();
        return pose;
    }

    /// <summary>같은 그래프에서 클립만 바꾼다(엎드림 → 일어남).</summary>
    public void Swap(AnimationClip clip, float startTime, float speed)
    {
        if (!IsValid || clip == null) return;
        if (_playable.IsValid()) _playable.Destroy();
        Connect(clip, startTime, speed);
    }

    public void SetSpeed(float speed)
    {
        if (IsValid) _playable.SetSpeed(speed);
    }

    public void SetWeight(float weight)
    {
        _weight = Mathf.Clamp01(weight);
        if (IsValid) _output.SetWeight(_weight);
    }

    /// <summary>출력 가중치를 실시간으로 옮긴다 — 컨트롤러 자세와 겹쳐 튀지 않게. 몸이 사라져 끊기면 조용히 멈춘다.</summary>
    public async UniTask FadeAsync(float to, float seconds, CancellationToken ct)
    {
        float from = _weight;
        try
        {
            for (float t = 0f; t < seconds && IsValid; t += Time.unscaledDeltaTime)
            {
                SetWeight(Mathf.Lerp(from, to, t / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetWeight(to);
        }
        catch (OperationCanceledException) { }
    }

    public void Destroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }

    /// <summary>
    /// 플레이어 컨트롤러에서 이름으로 클립을 찾는다 — 무기 세트 오버라이드가 덮어쓴 클립이 아니라 그 아래 원본 컨트롤러의 클립.
    /// 없으면 null. 연출 시작에 한 번만 부른다(배열을 만든다).
    /// </summary>
    public static AnimationClip FindControllerClip(Animator animator, string clipName)
    {
        var rac = animator != null ? animator.runtimeAnimatorController : null;
        while (rac is AnimatorOverrideController aoc && aoc.runtimeAnimatorController != null)
            rac = aoc.runtimeAnimatorController;
        if (rac == null || string.IsNullOrEmpty(clipName)) return null;

        foreach (var clip in rac.animationClips)
            if (clip != null && string.Equals(clip.name, clipName, StringComparison.Ordinal)) return clip;
        return null;
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Connect(AnimationClip clip, float startTime, float speed)
    {
        _clip     = clip;
        _playable = AnimationClipPlayable.Create(_graph, clip);
        _playable.SetApplyFootIK(false);
        _playable.SetTime(Mathf.Clamp(startTime, 0f, clip.length));
        _playable.SetSpeed(speed);
        _output.SetSourcePlayable(_playable);
    }
}
