using UnityEngine;
using UnityEngine.Rendering;

namespace RelicFairy.Monster
{
public sealed class DragonBossWarningZone : MonoBehaviour
{
    private const float PlaneSize = 10f;
    private const float CylinderRadius = 0.5f;

    private Transform _fillTransform;
    private Material _fillMaterial;
    private Material _outlineMaterial;
    private LineRenderer _outlineRenderer;
    private float _lifeTimer;
    private float _circleRadius;
    private float _fillHeight;
    private float _fillAnimDuration;
    private float _fillAnimElapsed;
    private bool _animateCircleFill;

    public static DragonBossWarningZone CreateCircle(
        string name,
        Vector3 center,
        float radius,
        Color color,
        float lifetime,
        float heightOffset = 0.05f,
        float outlineWidth = 0.12f,
        bool startEmpty = false)
    {
        var root = new GameObject(name);
        var zone = root.AddComponent<DragonBossWarningZone>();
        zone._lifeTimer = lifetime;
        zone.BuildCircle(center, radius, color, heightOffset, outlineWidth, startEmpty);
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
        if (_animateCircleFill)
        {
            _fillAnimElapsed += Time.deltaTime;
            float progress = _fillAnimDuration <= 0f
                ? 1f
                : Mathf.Clamp01(_fillAnimElapsed / _fillAnimDuration);
            SetCircleFill(progress);
            if (progress >= 1f)
                _animateCircleFill = false;
        }

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

    public void TransitionToHitPhase(float hitPhaseDuration)
    {
        if (_fillMaterial != null)
        {
            Color c = _fillMaterial.color;
            _fillMaterial.color = new Color(c.r, c.g, c.b, 0.75f);
        }
        if (_outlineMaterial != null)
        {
            Color c = _outlineMaterial.color;
            _outlineMaterial.color = new Color(c.r, c.g, c.b, 1f);
        }
        if (_outlineRenderer != null)
        {
            Color sc = _outlineRenderer.startColor;
            Color ec = _outlineRenderer.endColor;
            _outlineRenderer.startColor = new Color(sc.r, sc.g, sc.b, 1f);
            _outlineRenderer.endColor   = new Color(ec.r, ec.g, ec.b, 1f);
        }
        _animateCircleFill = false;
        _lifeTimer = Mathf.Max(0.01f, hitPhaseDuration);
    }

    public void BeginCircleFill(float duration)
    {
        _fillAnimDuration = Mathf.Max(0f, duration);
        _fillAnimElapsed = 0f;
        _animateCircleFill = true;
        SetCircleFill(0f);
    }

    public void SetCircleFill(float normalized)
    {
        if (_fillTransform == null)
            return;

        float clamped = Mathf.Clamp01(normalized);
        float scaledRadius = Mathf.Max(0.01f, _circleRadius * clamped);
        _fillTransform.localScale = new Vector3(
            Mathf.Max(0.01f, scaledRadius / CylinderRadius),
            _fillHeight,
            Mathf.Max(0.01f, scaledRadius / CylinderRadius));
    }

    private void BuildCircle(
        Vector3 center,
        float radius,
        Color color,
        float heightOffset,
        float outlineWidth,
        bool startEmpty)
    {
        var fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fill.name = "Fill";
        fill.transform.SetParent(transform, false);
        fill.transform.position = center + Vector3.up * heightOffset;
        _fillTransform = fill.transform;
        _circleRadius = Mathf.Max(0.01f, radius);
        _fillHeight = 0.01f;
        if (fill.TryGetComponent<Collider>(out var fillCollider))
            Destroy(fillCollider);

        var fillRenderer = fill.GetComponent<MeshRenderer>();
        fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        _fillMaterial = CreateMaterial(new Color(color.r, color.g, color.b, color.a * 0.20f));
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
        Color outlineColor = new Color(color.r, color.g, color.b, color.a * 0.65f);
        _outlineMaterial = CreateMaterial(outlineColor);
        _outlineRenderer = outline;
        outline.material = _outlineMaterial;
        outline.startColor = outlineColor;
        outline.endColor = outlineColor;

        float y = center.y + heightOffset + 0.02f;
        for (int i = 0; i < outline.positionCount; i++)
        {
            float t = i / (float)outline.positionCount * Mathf.PI * 2f;
            var point = center + new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * radius;
            point.y = y;
            outline.SetPosition(i, point);
        }

        SetCircleFill(startEmpty ? 0f : 1f);
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
        _fillMaterial = CreateMaterial(new Color(color.r, color.g, color.b, color.a * 0.20f));
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
        Color outlineColor = new Color(color.r, color.g, color.b, color.a * 0.65f);
        _outlineMaterial = CreateMaterial(outlineColor);
        _outlineRenderer = outline;
        outline.material = _outlineMaterial;
        outline.startColor = outlineColor;
        outline.endColor = outlineColor;

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
