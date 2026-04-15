using UnityEngine;

namespace Abyss.Monster
{
public class MonsterGroundWarning : MonoBehaviour
{
    private const int SegmentCount = 48;
    private static Material s_material;
    private const float CellSize = 1f;
    private const float CellHalfSize = 0.45f;

    private float _remaining;

    public enum GridShape
    {
        Front1 = 0,
        Front2 = 1,
        FrontWide3 = 2,
        Around8 = 3,
    }

    public static void Spawn(Vector3 worldPos, float radius, float duration, Color color)
    {
        var go = new GameObject("[MonsterGroundWarning]");
        var warning = go.AddComponent<MonsterGroundWarning>();
        warning.Setup(worldPos, radius, duration, color);
    }

    public static void SpawnGrid(Vector3 origin, Vector3 forward, GridShape shape, float duration, Color color)
    {
        var go = new GameObject("[MonsterGroundWarning.Grid]");
        var warning = go.AddComponent<MonsterGroundWarning>();
        warning.SetupGrid(origin, forward, shape, duration, color);
    }

    private void Setup(Vector3 worldPos, float radius, float duration, Color color)
    {
        transform.position = worldPos + Vector3.up * 0.05f;
        _remaining = Mathf.Max(0.05f, duration);

        var line = gameObject.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = false;
        line.positionCount = SegmentCount;
        line.widthMultiplier = 0.08f;
        line.material = GetMaterial();
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        for (int i = 0; i < SegmentCount; i++)
        {
            float t = (float)i / SegmentCount * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius));
        }
    }

    private void SetupGrid(Vector3 origin, Vector3 forward, GridShape shape, float duration, Color color)
    {
        _remaining = Mathf.Max(0.05f, duration);

        Vector3 planarForward = forward;
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude < 0.001f)
            planarForward = Vector3.forward;
        planarForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, planarForward).normalized;
        Vector2Int[] cells = GetCells(shape);

        for (int i = 0; i < cells.Length; i++)
        {
            Vector2Int c = cells[i];
            Vector3 center =
                origin
                + right * (c.x * CellSize)
                + planarForward * (c.y * CellSize)
                + Vector3.up * 0.05f;

            CreateCellLine(center, right, planarForward, color);
        }
    }

    private static Vector2Int[] GetCells(GridShape shape)
    {
        switch (shape)
        {
            case GridShape.Front1:
                return new[] { new Vector2Int(0, 1) };
            case GridShape.Front2:
                return new[] { new Vector2Int(0, 1), new Vector2Int(0, 2) };
            case GridShape.FrontWide3:
                return new[] { new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1) };
            case GridShape.Around8:
                return new[]
                {
                    new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
                    new Vector2Int(-1,  0),                         new Vector2Int(1,  0),
                    new Vector2Int(-1,  1), new Vector2Int(0,  1), new Vector2Int(1,  1),
                };
            default:
                return new[] { new Vector2Int(0, 1) };
        }
    }

    private void CreateCellLine(Vector3 center, Vector3 right, Vector3 forward, Color color)
    {
        var cellGo = new GameObject("Cell");
        cellGo.transform.SetParent(transform, false);

        var line = cellGo.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = 4;
        line.widthMultiplier = 0.06f;
        line.material = GetMaterial();
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        Vector3 rx = right * CellHalfSize;
        Vector3 fz = forward * CellHalfSize;
        line.SetPosition(0, center - rx - fz);
        line.SetPosition(1, center + rx - fz);
        line.SetPosition(2, center + rx + fz);
        line.SetPosition(3, center - rx + fz);
    }

    private void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
            Destroy(gameObject);
    }

    private static Material GetMaterial()
    {
        if (s_material != null)
            return s_material;

        var shader = Shader.Find("Sprites/Default");
        s_material = new Material(shader);
        return s_material;
    }
}
}
