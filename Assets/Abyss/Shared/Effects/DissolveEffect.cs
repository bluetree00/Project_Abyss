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

    /// <summary>디졸브로 등장 (소멸 상태 → 완전 등장 후 원본 복원)</summary>
    public static void PlayAppear(GameObject target, float duration = 0.5f)
    {
        if (target == null) return;
        var effect = target.AddComponent<DissolveEffect>();
        effect.StartCoroutine(effect.DissolveInRoutine(target, duration));
    }

    private IEnumerator DissolveInRoutine(GameObject target, float duration)
    {
        var dissolveMat = Resources.Load<Material>("DissolveMaterial");
        if (dissolveMat == null)
        {
            Destroy(this);
            yield break;
        }

        var renderers = target.GetComponentsInChildren<Renderer>();

        // 원본 머티리얼 백업
        var originalMaterials = new List<Material[]>();
        foreach (var r in renderers)
            originalMaterials.Add(r.sharedMaterials);

        // 디졸브 머티리얼로 교체 (_Dissolve=1, 완전 사라진 상태)
        var dissolveInstances = ReplaceMaterials(renderers, dissolveMat, new Color(0f, 0.8f, 1f, 1f));
        SetDissolveValue(dissolveInstances, 1f);

        // _Dissolve 1 → 0 (나타남)
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetDissolveValue(dissolveInstances, 1f - t);
            yield return null;
        }

        // 완료 후 원본 머티리얼 복원
        for (int i = 0; i < renderers.Length && i < originalMaterials.Count; i++)
            renderers[i].sharedMaterials = originalMaterials[i];

        Destroy(this);
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
                }

                inst.SetColor(EdgeColorID, edgeColor);
                inst.SetFloat(EdgeWidthID, 0.04f);
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
