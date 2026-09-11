using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 머티리얼 <b>알파만</b> 낮춰 서서히 지우는 퇴장 연출.
///
/// <see cref="DissolveEffect"/>는 머티리얼을 통째로 디졸브 머티리얼로 갈아끼우는 방식이라,
/// 반투명·커스텀 셰이더(무형검의 안개 머티리얼 M_NamelessFog 등)에 씌우면 셰이더가 맞지 않아
/// 핑크 잔상이 남는다. 그런 오브젝트는 이쪽을 쓴다.
///
/// 머티리얼은 <c>renderer.materials</c> 접근 시점에 인스턴스로 복제되므로 원본 에셋을 오염시키지 않는다.
/// </summary>
public static class MaterialFade
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    /// <summary>등장(알파 0 → 원본) 준비 상태. <see cref="BeginFadeIn"/>이 만들고 <see cref="FadeInAsync"/>가 소비한다.</summary>
    public sealed class FadeInHandle
    {
        internal readonly List<Material> Mats    = new();
        internal readonly List<Color>    Targets = new();
    }

    /// <summary>
    /// 대상을 <b>즉시 투명</b>하게 만들고 원본 색을 기억한다. 오브젝트가 처음 그려지기 <b>전</b>에 불러야
    /// 첫 프레임에 통째로 보였다가 꺼지는 팝이 없다(무기 장착의 beforeShow 훅에서 쓴다).
    /// 파티클·트레일은 건드리지 않는다(자체 셰이더).
    /// </summary>
    public static FadeInHandle BeginFadeIn(GameObject target)
    {
        var h = new FadeInHandle();
        if (target == null) return h;

        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            foreach (var m in r.materials)   // 접근 시점에 인스턴스 복제 — 원본 에셋 무오염
            {
                if (m == null) continue;
                var c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                      : m.HasProperty(ColorId)     ? m.GetColor(ColorId)
                      : Color.white;
                h.Mats.Add(m);
                h.Targets.Add(c);
                Apply(m, c, 0f);
            }
        }
        return h;
    }

    /// <summary>알파 0 → 원본. 취소되면 원본 알파로 즉시 복원한다(투명한 채 남지 않게).</summary>
    public static async UniTask FadeInAsync(FadeInHandle h, float duration, CancellationToken ct = default)
    {
        if (h == null || h.Mats.Count == 0) return;

        float t = 0f;
        try
        {
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                for (int i = 0; i < h.Mats.Count; i++)
                    if (h.Mats[i] != null) Apply(h.Mats[i], h.Targets[i], h.Targets[i].a * k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            for (int i = 0; i < h.Mats.Count; i++)
                if (h.Mats[i] != null) Apply(h.Mats[i], h.Targets[i], h.Targets[i].a);
        }
    }

    private static void Apply(Material m, Color c, float alpha)
    {
        c.a = alpha;
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
        if (m.HasProperty(ColorId))     m.SetColor(ColorId, c);
    }

    /// <summary>대상(자식 포함)의 모든 렌더러 알파를 0까지 낮춘다. 파티클은 먼저 꺼서 공중에 남지 않게 한다.</summary>
    public static async UniTask FadeOutAsync(GameObject target, float duration, CancellationToken ct = default)
    {
        if (target == null) return;

        // 붙어 있는 파티클을 먼저 끈다 — 본체가 사라져도 이펙트만 공중에 남는 것 방지.
        foreach (var ps in target.GetComponentsInChildren<ParticleSystem>(true))
            ps.gameObject.SetActive(false);

        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        var mats   = new List<Material>(renderers.Length);
        var starts = new List<Color>(renderers.Length);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials)   // 접근 시점에 인스턴스 복제 — 원본 에셋 무오염
            {
                if (m == null) continue;
                mats.Add(m);
                starts.Add(m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                         : m.HasProperty(ColorId)     ? m.GetColor(ColorId)
                         : Color.white);
            }
        }
        if (mats.Count == 0) return;

        float t = 0f;
        try
        {
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                for (int i = 0; i < mats.Count; i++)
                {
                    if (mats[i] == null) continue;
                    var c = starts[i];
                    c.a = Mathf.Lerp(starts[i].a, 0f, k);
                    if (mats[i].HasProperty(BaseColorId)) mats[i].SetColor(BaseColorId, c);
                    if (mats[i].HasProperty(ColorId))     mats[i].SetColor(ColorId, c);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
