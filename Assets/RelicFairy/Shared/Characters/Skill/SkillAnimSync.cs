using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스킬 애니메이션의 <b>닿는 순간</b>을 판정 시각에 맞춘다(09-25).
///
/// 스킬 판정은 고정 타이머(○○Delay)로 나가는데, 클립은 그보다 한참 뒤에 휘두른다 — 실측(<c>PlayerSkillClipBeatProbe</c>,
/// 휴머노이드 손 속도 최고점): 대검 R은 1.22초에 닿는데 판정은 0.6초, 대검 E는 1.27초인데 판정은 즉시,
/// 성광베기·무형일섬(발도 클립)은 1.63초인데 판정은 0.15~0.3초. 칼이 닿기도 전에 적이 맞았다.
///
/// 판정을 늦추지 않고(누른 뒤 반응이 늦어지면 조작감이 나빠진다) <b>클립을 중간부터 재생</b>해 닿는 순간을 판정에 맞춘다.
/// 리치의 시전 박자와 같은 원리이되, 플레이어는 「닿기 전에 버티기」를 넣지 않는다 — 앞부분(준비 동작)을 건너뛸 뿐이다.
/// 닿은 뒤의 무게는 막타 히트스톱(<see cref="HitFeelService"/> longStop)이 맡는다.
///
/// 클립은 무기마다 오버라이드로 바뀌므로 <b>지금 실제로 도는 클립 이름</b>으로 찾는다. 표에 없는 클립은 처음부터 재생(기존 동작).
/// </summary>
public static class SkillAnimSync
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>클립 이름 → 닿는 순간(초). PlayerSkillClipBeatProbe 실측(09-25, 손 속도 최고점).</summary>
    private static readonly Dictionary<string, float> ContactSeconds = new()
    {
        { "GreatswordQSkill", 1.22f },   // 최후의 일격 — 내려찍기
        { "GreatswordESkill", 1.27f },   // 주변경계 — 회전 베기(1.05초 예비 동작 뒤 1.27초가 가장 빠르다)
        { "QSkill_Iasen",     1.63f },   // 발도 — 성광베기·무형일섬
    };

    /// <summary>상태 이름과 그 상태의 원본 클립 이름이 다른 곳(오버라이드 키는 원본 클립 이름이다).</summary>
    private static readonly Dictionary<string, string> StateClip = new()
    {
        { "QSkill_02", "QSkill_Iasen" },
    };

    /// <summary>
    /// 상태 재생 속도(<c>PlayerBaseController.controller</c>의 상태 Speed). 스킬 상태는 1배가 아니다 — 클립은 속도 × 경과 시간만큼 흐른다.
    /// 첫 적용(09-25)에서 이걸 빠뜨려 클립이 닿는 순간을 지나쳐 재생됐다(검증 도구가 잡았다). 컨트롤러 속도를 바꾸면 여기도 바꾼다.
    /// </summary>
    private static readonly Dictionary<string, float> StateSpeed = new()
    {
        { "QSkill_01", 2f },
        { "QSkill_02", 1.5f },
        { "ESkill_01", 2f },
    };

    private const float EndMargin = 0.1f;   // 클립 끝에 붙어 시작하지 않게

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// <paramref name="state"/>를 재생하되, 클립의 닿는 순간이 지금부터 <paramref name="contactIn"/>초 뒤에 오도록 중간부터 시작한다.
    /// </summary>
    public static void CrossFadeToContact(Animator animator, string state, float contactIn, float fadeSeconds)
    {
        if (animator == null) return;
        animator.CrossFadeInFixedTime(state, fadeSeconds, 0, StartOffset(animator, state, contactIn));
    }

    /// <summary>블렌드 없이 바로 재생하는 쪽(<c>Animator.Play</c>를 쓰던 곳).</summary>
    public static void PlayToContact(Animator animator, string state, float contactIn)
    {
        if (animator == null) return;
        animator.PlayInFixedTime(state, 0, StartOffset(animator, state, contactIn));
    }

    // ── Private Methods ───────────────────────────────────────────
    private static float StartOffset(Animator animator, string state, float contactIn)
    {
        var clip = ResolveClip(animator, state);
        if (clip == null || !ContactSeconds.TryGetValue(clip.name, out float contact)) return 0f;
        float speed = StateSpeed.TryGetValue(state, out var sp) && sp > 0f ? sp : 1f;
        // 클립 시각 = 속도 × (시작 지점 + 경과). 경과 = contactIn일 때 클립이 contact에 오도록 시작 지점을 잡는다(상태 시간 단위).
        return Mathf.Clamp(contact / speed - Mathf.Max(0f, contactIn), 0f, Mathf.Max(0f, (clip.length - EndMargin) / speed));
    }

    /// <summary>상태에서 지금 실제로 도는 클립 — 오버라이드가 있으면 교체 클립.</summary>
    private static AnimationClip ResolveClip(Animator animator, string state)
    {
        string original = StateClip.TryGetValue(state, out var c) ? c : state;
        if (animator.runtimeAnimatorController is AnimatorOverrideController aoc)
        {
            var clip = aoc[original];
            if (clip != null) return clip;
        }
        var ctrl = animator.runtimeAnimatorController;
        if (ctrl == null) return null;
        foreach (var clip in ctrl.animationClips)
            if (clip != null && clip.name == original) return clip;
        return null;
    }
}
