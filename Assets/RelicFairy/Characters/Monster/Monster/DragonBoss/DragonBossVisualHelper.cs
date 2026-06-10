using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DragonBoss 시각 효과 헬퍼.
/// 원소 종류에 따른 색상 반환 및 드래곤 바디 틴트 적용 유틸리티.
/// </summary>
public static class DragonBossVisualHelper
{
    // ── 원소 기준 색상 (DragonSummonPattern SO 기준과 동일) ──────────────
    public static Color GetElementColor(DragonBossBlackboard.DragonElement element)
    {
        return element switch
        {
            DragonBossBlackboard.DragonElement.Ice     => new Color(0.5f,  0.85f, 1.0f),
            DragonBossBlackboard.DragonElement.Thunder => new Color(0.65f, 0.3f,  1.0f),
            DragonBossBlackboard.DragonElement.Fire    => new Color(1.0f,  0.35f, 0.1f),
            _                                          => new Color(1.0f,  0.35f, 0.1f),
        };
    }

    /// <summary>
    /// 드래곤 SkinnedMeshRenderer 전체에 MaterialPropertyBlock으로 색상 틴트를 입힌다.
    /// 원본 material을 수정하지 않으므로 pool 재사용에 안전하다.
    /// </summary>
    public static void ApplyBodyTint(Transform dragonRoot, Color tint)
    {
        if (dragonRoot == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var rend in dragonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_Color",     tint);
            rend.SetPropertyBlock(mpb);
        }
    }

    public static void ApplyEffectTint(GameObject go, Color tint)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))     mat.SetColor("_BaseColor",     tint);
                if (mat.HasProperty("_Color"))         mat.SetColor("_Color",         tint);
                if (mat.HasProperty("_TintColor"))     mat.SetColor("_TintColor",     tint);
                if (mat.HasProperty("_MainColor"))     mat.SetColor("_MainColor",     tint);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", tint * 0.4f);
                    mat.EnableKeyword("_EMISSION");
                }
            }
        }
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint);
        }
    }
}
}
