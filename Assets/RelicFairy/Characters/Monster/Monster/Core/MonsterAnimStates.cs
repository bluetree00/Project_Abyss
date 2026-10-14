using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 팩 컨트롤러의 보조 동작 상태 — 옆 · 뒤 걷기 · 어지러움 · 도발(10-14 정예 애니 점검).
/// 팩마다 이름이 둘이다: Wave1·2 「WalkBWD · WalkLeft · WalkRight · Dizzy · Taunting」 /
/// Wave3 「접두어_WalkBWD · 접두어_WalkLFT · 접두어_WalkRGT · 접두어_Dizzy · 접두어_Taunting」(접두어 = 추적 상태 이름의 「_」 앞).
/// 컨트롤러마다 한 번 찾아 해시로 둔다(매 프레임 문자열을 만들지 않게). 없는 칸은 0.
/// </summary>
public static class MonsterAnimStates
{
    public readonly struct Set
    {
        public readonly int Back, Left, Right, Dizzy, Taunt;
        public Set(int back, int left, int right, int dizzy, int taunt) { Back = back; Left = left; Right = right; Dizzy = dizzy; Taunt = taunt; }
    }

    private static readonly Dictionary<RuntimeAnimatorController, Set> s_cache = new();

    public static Set Of(MonsterContext ctx) => ctx != null ? Of(ctx.Animator, ctx.Animation) : default;

    public static Set Of(Animator animator, MonsterAnimationData anim)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return default;
        var rac = animator.runtimeAnimatorController;
        if (s_cache.TryGetValue(rac, out var set)) return set;

        string chase = anim != null ? anim.chaseStateName : null;
        int cut = string.IsNullOrEmpty(chase) ? -1 : chase.IndexOf('_');
        string p = cut > 0 ? chase.Substring(0, cut + 1) : null;
        set = new Set(
            Find(animator, p, "WalkBWD"),
            Find(animator, p, "WalkLeft", "WalkLFT"),
            Find(animator, p, "WalkRight", "WalkRGT"),
            Find(animator, p, "Dizzy"),
            Find(animator, p, "Taunting"));
        s_cache[rac] = set;
        return set;
    }

    private static int Find(Animator a, string prefix, params string[] names)
    {
        foreach (var n in names)
        {
            int h = Animator.StringToHash(n);
            if (a.HasState(0, h)) return h;
            if (prefix == null) continue;
            h = Animator.StringToHash(prefix + n);
            if (a.HasState(0, h)) return h;
        }
        return 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_cache.Clear();
}
}
