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
