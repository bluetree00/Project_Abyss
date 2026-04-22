using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 디졸브 셰이더 기반 등장 연출.
/// 무기 장착/교체/전환 시 무기 프리팹에 적용.
/// Resources/DissolveMaterial 사용.
/// </summary>
public class DissolveEffect : MonoBehaviour
{
    private static readonly int DissolveID = Shader.PropertyToID("_Dissolve");
    private static readonly int EdgeColorID = Shader.PropertyToID("_EdgeColor");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");

    // Polyart/다른 서드파티 셰이더가 쓰는 흔한 메인 컬러 프로퍼티 — BaseColor 대체 후보
    private static readonly string[] FallbackColorProps =
        { "_Color01", "_Color", "_MainColor", "_TintColor", "_AlbedoColor" };
    private const float EdgeFadePortion = 0.25f;   // 후반 25%는 edge만 페이드
    private const float MaxEdgeWidth    = 0.12f;   // 초기 edge 두께

    /// <summary>디졸브로 등장 (소멸 상태 → 완전 등장 후 원본 복원).
    /// 완료 시 onComplete 콜백 호출 — 렌더러 머티리얼이 원본으로 복원된 이후 시점임이 보장되므로,
    /// 원소 팔레트 주입 등 머티리얼 색을 건드리는 후속 작업은 이 콜백에서 수행하는 것이 안전.</summary>
    public static void PlayAppear(GameObject target, float duration = 0.5f, System.Action onComplete = null)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        var effect = target.AddComponent<DissolveEffect>();
        effect.StartCoroutine(effect.DissolveInRoutine(target, duration, onComplete));
    }

    /// <summary>디졸브로 퇴장 (완전 등장 상태 → 소멸). 완료 후 onComplete 콜백 호출.
    /// 원본 머티리얼은 복원되지 않으며, 사용처가 gameObject를 숨기거나 파괴하는 책임.</summary>
    public static void PlayDisappear(GameObject target, float duration = 0.8f, System.Action onComplete = null)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        var effect = target.AddComponent<DissolveEffect>();
        effect.StartCoroutine(effect.DissolveOutRoutine(target, duration, onComplete));
    }

    private IEnumerator DissolveOutRoutine(GameObject target, float duration, System.Action onComplete)
    {
        var dissolveMat = Resources.Load<Material>("DissolveMaterial");
        if (dissolveMat == null)
        {
            Debug.LogWarning($"[DissolveEffect] Resources/DissolveMaterial 로드 실패 — '{target.name}' 퇴장 디졸브 스킵");
            onComplete?.Invoke();
            Destroy(this);
            yield break;
        }

        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            onComplete?.Invoke();
            Destroy(this);
            yield break;
        }

        Debug.Log($"[DissolveEffect] '{target.name}' 퇴장 디졸브 시작 — Renderer {renderers.Length}개, duration={duration:F2}s");

        var dissolveInstances = ReplaceMaterials(renderers, dissolveMat, new Color(0f, 2.4f, 3f, 1f));
        SetDissolveValue(dissolveInstances, 0f);
        SetEdgeWidth(dissolveInstances, MaxEdgeWidth);

        // 0 → 1 (점점 사라짐). 에지는 지속 유지하여 사라지는 과정이 잘 보이도록
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetDissolveValue(dissolveInstances, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        SetDissolveValue(dissolveInstances, 1f);

        onComplete?.Invoke();
        Destroy(this);
    }

    private IEnumerator DissolveInRoutine(GameObject target, float duration, System.Action onComplete)
    {
        var dissolveMat = Resources.Load<Material>("DissolveMaterial");
        if (dissolveMat == null)
        {
            Debug.LogWarning($"[DissolveEffect] Resources/DissolveMaterial 로드 실패 — '{target.name}' 디졸브 스킵");
            onComplete?.Invoke();
            Destroy(this);
            yield break;
        }

        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"[DissolveEffect] '{target.name}' 에 Renderer 없음 — 디졸브 스킵");
            onComplete?.Invoke();
            Destroy(this);
            yield break;
        }

        Debug.Log($"[DissolveEffect] '{target.name}' 디졸브 시작 — Renderer {renderers.Length}개, duration={duration:F2}s");

        // 원본 머티리얼 백업
        var originalMaterials = new List<Material[]>();
        foreach (var r in renderers)
            originalMaterials.Add(r.sharedMaterials);

        // 디졸브 머티리얼로 교체 (_Dissolve=1, 완전 사라진 상태)
        // Edge는 네온 청록 + HDR 강도 3.0 (어두운 방에서도 확실히 보이도록)
        var dissolveInstances = ReplaceMaterials(renderers, dissolveMat, new Color(0f, 2.4f, 3f, 1f));
        SetDissolveValue(dissolveInstances, 1f);

        // 2-Phase 애니메이션으로 원본 교체 점프를 최소화
        //   Phase 1 (약 75%): _Dissolve 1 → 0 (몬스터 드러남)
        //   Phase 2 (약 25%): EdgeWidth 0.12 → 0 (네온 경계만 페이드 아웃)
        float mainDur = Mathf.Max(0.01f, duration * (1f - EdgeFadePortion));
        float edgeDur = Mathf.Max(0.01f, duration - mainDur);

        // Phase 1
        float t1 = 0f;
        while (t1 < mainDur)
        {
            t1 += Time.deltaTime;
            SetDissolveValue(dissolveInstances, 1f - Mathf.Clamp01(t1 / mainDur));
            yield return null;
        }
        SetDissolveValue(dissolveInstances, 0f);

        // Phase 2 — edge 두께만 줄임, dissolve는 0 유지
        float t2 = 0f;
        while (t2 < edgeDur)
        {
            t2 += Time.deltaTime;
            SetEdgeWidth(dissolveInstances, MaxEdgeWidth * (1f - Mathf.Clamp01(t2 / edgeDur)));
            yield return null;
        }
        SetEdgeWidth(dissolveInstances, 0f);

        // Edge=0 상태 1프레임 보호 후 원본 복원 — 교체 순간의 시각 점프 완화
        yield return null;

        for (int i = 0; i < renderers.Length && i < originalMaterials.Count; i++)
            renderers[i].sharedMaterials = originalMaterials[i];

        onComplete?.Invoke();
        Destroy(this);
    }

    private void SetEdgeWidth(List<Material> materials, float value)
    {
        foreach (var mat in materials)
            if (mat != null) mat.SetFloat(EdgeWidthID, value);
    }

    private List<Material> ReplaceMaterials(Renderer[] renderers, Material dissolveMat, Color edgeColor)
    {
        var instances = new List<Material>();
        foreach (var r in renderers)
        {
            var newMats = new Material[r.sharedMaterials.Length];
            for (int j = 0; j < newMats.Length; j++)
            {
                var inst = new Material(dissolveMat);
                var orig = r.sharedMaterials[j];

                if (orig != null)
                {
                    if (orig.HasProperty(BaseMapID) && inst.HasProperty(BaseMapID))
                        inst.SetTexture(BaseMapID, orig.GetTexture(BaseMapID));
                    if (orig.HasProperty(BaseColorID) && inst.HasProperty(BaseColorID))
                        inst.SetColor(BaseColorID, orig.GetColor(BaseColorID));

                    // Polyart/Tint 계열은 _BaseColor가 비어있고 _Color01 / _Color 등을 씀 → 그 값으로 덮어씀
                    if (inst.HasProperty(BaseColorID))
                    {
                        foreach (var propName in FallbackColorProps)
                        {
                            if (!orig.HasProperty(propName)) continue;
                            var c = orig.GetColor(propName);
                            if (c.a <= 0.01f) continue; // 알파 0이면 의미 없음
                            inst.SetColor(BaseColorID, c);
                            break;
                        }
                    }
                }

                inst.SetColor(EdgeColorID, edgeColor);
                inst.SetFloat(EdgeWidthID, 0.12f); // 0.04 → 0.12 (에지 3배 두껍게)
                inst.SetFloat(DissolveID, 0f);

                newMats[j] = inst;
                instances.Add(inst);
            }
            r.materials = newMats;
        }
        return instances;
    }

    private void SetDissolveValue(List<Material> materials, float value)
    {
        foreach (var mat in materials)
            if (mat != null) mat.SetFloat(DissolveID, value);
    }
}
