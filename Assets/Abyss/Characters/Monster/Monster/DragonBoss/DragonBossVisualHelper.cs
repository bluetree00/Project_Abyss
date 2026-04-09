using UnityEngine;

namespace Abyss.Monster
{
public static class DragonBossVisualHelper
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public static Color GetElementColor(DragonBossBlackboard.DragonElement element)
    {
        switch (element)
        {
            case DragonBossBlackboard.DragonElement.Ice:
                return new Color(0.40f, 0.78f, 1f, 1f);
            case DragonBossBlackboard.DragonElement.Thunder:
                return new Color(1f, 0.86f, 0.25f, 1f);
            default:
                return new Color(1f, 0.38f, 0.18f, 1f);
        }
    }

    public static void ApplyRendererTint(Renderer[] renderers, Color color)
    {
        if (renderers == null) return;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            var materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                if (material.HasProperty(BaseColorId))
                    material.SetColor(BaseColorId, color);
                if (material.HasProperty(ColorId))
                    material.SetColor(ColorId, color);
                if (material.HasProperty(EmissionColorId))
                    material.SetColor(EmissionColorId, color * 0.35f);
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            block.SetColor(EmissionColorId, color * 0.35f);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// 지정 위치에서 위로 10m 올려 아래 방향으로 레이캐스트해 실제 지면 y를 반환.
    /// Trigger 콜라이더는 무시. 감지 실패 시 pos.y 반환.
    /// </summary>
    public static float GetGroundY(Vector3 pos)
    {
        Vector3 origin = pos + Vector3.up * 10f;
        if (Physics.Raycast(origin, Vector3.down, out var hit, 30f, -1, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return pos.y;
    }

    public static void ApplyEffectTint(GameObject root, Color color)
    {
        if (root == null) return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;

            var materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                if (material.HasProperty(BaseColorId))
                    material.SetColor(BaseColorId, color);
                if (material.HasProperty(ColorId))
                    material.SetColor(ColorId, color);
                if (material.HasProperty(EmissionColorId))
                    material.SetColor(EmissionColorId, color * 0.5f);
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            block.SetColor(EmissionColorId, color * 0.5f);
            renderer.SetPropertyBlock(block);
        }

        foreach (var trail in root.GetComponentsInChildren<TrailRenderer>(true))
        {
            if (trail == null) continue;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particle.main;
            main.startColor = color;
        }

        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (light != null)
                light.color = color;
        }
    }
}
}
