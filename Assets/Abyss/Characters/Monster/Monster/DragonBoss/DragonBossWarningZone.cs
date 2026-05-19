using UnityEngine;
using UnityEngine.Rendering;

namespace Abyss.Monster
{
public sealed class DragonBossWarningZone : MonoBehaviour
{
    private const float PlaneSize = 10f;
    private const float CylinderRadius = 0.5f;

    private Material _fillMaterial;
    private Material _outlineMaterial;
    private float _lifeTimer;

    public static DragonBossWarningZone CreateCircle(
        string name,
        Vector3 center,
        float radius,
        Color color,
        float lifetime,
        float heightOffset = 0.05f,
        float outlineWidth = 0.12f)
    {
        var root = new GameObject(name);
        var zone = root.AddComponent<DragonBossWarningZone>();
        zone._lifeTimer = lifetime;
        zone.BuildCircle(center, radius, color, heightOffset, outlineWidth);
        return zone;
    }

    public static DragonBossWarningZone CreateRectangle(
        string name,
        Vector3 center,
        Quaternion rotation,
        float width,
        float length,
        Color color,
        float lifetime,
        float heightOffset = 0.05f,
        float outlineWidth = 0.12f)
    {
        var root = new GameObject(name);
        var zone = root.AddComponent<DragonBossWarningZone>();
        zone._lifeTimer = lifetime;
        zone.BuildRectangle(center, rotation, width, length, color, heightOffset, outlineWidth);
        return zone;
    }

    private void Update()
    {
        if (_lifeTimer <= 0f)
            return;

        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_fillMaterial != null)
            Destroy(_fillMaterial);
        if (_outlineMaterial != null)
            Destroy(_outlineMaterial);
    }

    private void BuildCircle(Vector3 center, float radius, Color color, float heightOffset, float outlineWidth)
    {
        var fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fill.name = "Fill";
        fill.transform.SetParent(transform, false);
        fill.transform.position = center + Vector3.up * heightOffset;
        fill.transform.localScale = new Vector3(
            Mathf.Max(0.01f, radius / CylinderRadius),
            0.01f,
            Mathf.Max(0.01f, radius / CylinderRadius));
        if (fill.TryGetComponent<Collider>(out var fillCollider))
            Destroy(fillCollider);

        var fillRenderer = fill.GetComponent<MeshRenderer>();
        fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        _fillMaterial = CreateMaterial(new Color(color.r, color.g, color.b, color.a * 0.45f));
        fillRenderer.material = _fillMaterial;

        var outlineRoot = new GameObject("Outline");
        outlineRoot.transform.SetParent(transform, false);
        var outline = outlineRoot.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.positionCount = 32;
        outline.startWidth = outlineWidth;
        outline.endWidth = outlineWidth;
        outline.numCapVertices = 6;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;
        _outlineMaterial = CreateMaterial(color);
        outline.material = _outlineMaterial;
        outline.startColor = color;
        outline.endColor = color;

        float y = center.y + heightOffset + 0.02f;
        for (int i = 0; i < outline.positionCount; i++)
        {
            float t = i / (float)outline.positionCount * Mathf.PI * 2f;
            var point = center + new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * radius;
            point.y = y;
            outline.SetPosition(i, point);
        }
    }

    private void BuildRectangle(
        Vector3 center,
        Quaternion rotation,
        float width,
        float length,
        Color color,
        float heightOffset,
        float outlineWidth)
    {
        var fill = GameObject.CreatePrimitive(PrimitiveType.Plane);
        fill.name = "Fill";
        fill.transform.SetParent(transform, false);
        fill.transform.position = center + Vector3.up * heightOffset;
        fill.transform.rotation = rotation;
        fill.transform.localScale = new Vector3(
            Mathf.Max(0.01f, width / PlaneSize),
            1f,
            Mathf.Max(0.01f, length / PlaneSize));
        if (fill.TryGetComponent<Collider>(out var fillCollider))
            Destroy(fillCollider);

        var fillRenderer = fill.GetComponent<MeshRenderer>();
        fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        _fillMaterial = CreateMaterial(new Color(color.r, color.g, color.b, color.a * 0.45f));
        fillRenderer.material = _fillMaterial;

        var outlineRoot = new GameObject("Outline");
        outlineRoot.transform.SetParent(transform, false);
        var outline = outlineRoot.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.positionCount = 4;
        outline.startWidth = outlineWidth;
        outline.endWidth = outlineWidth;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;
        _outlineMaterial = CreateMaterial(color);
        outline.material = _outlineMaterial;
        outline.startColor = color;
        outline.endColor = color;

        Vector3 forward = rotation * Vector3.forward * (length * 0.5f);
        Vector3 right = rotation * Vector3.right * (width * 0.5f);
        float y = center.y + heightOffset + 0.02f;
        outline.SetPosition(0, new Vector3(center.x - right.x - forward.x, y, center.z - right.z - forward.z));
        outline.SetPosition(1, new Vector3(center.x + right.x - forward.x, y, center.z + right.z - forward.z));
        outline.SetPosition(2, new Vector3(center.x + right.x + forward.x, y, center.z + right.z + forward.z));
        outline.SetPosition(3, new Vector3(center.x - right.x + forward.x, y, center.z - right.z + forward.z));
    }

    private static Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        return new Material(shader) { color = color };
    }
}
}
