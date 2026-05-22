using UnityEngine;

namespace RelicFairy.Monster
{
public class MonsterGroundWarning : MonoBehaviour
{
    private const int SegmentCount = 48;
    private static Material s_material;
    private const float CellSize = 1f;
    private const float CellHalfSize = 0.45f;

    private float _remaining;

    // Fill 모드 전용
    private bool        _fillMode;
    private float       _fillDuration;
    private float       _fillElapsed;
    private float       _fillRadius;
    private Color       _fillColor;
    private Color       _fillOuterColor;
    private LineRenderer _fillLine;        // 채워지는 원
    private LineRenderer _outerLine;       // 외곽 경계선 (항상 고정)

    public enum GridShape
    {
        Front1 = 0,
        Front2 = 1,
        FrontWide3 = 2,
        Around8 = 3,
    }

    /// <summary>
    /// 중심에서 바깥쪽으로 원이 채워지는 경고.
    /// duration 동안 반경 0 → radius 로 동심원이 커지며,
    /// 끝나는 순간 가장 큰 원(외곽 경계선)만 짧게 반짝이고 소멸.
    /// </summary>
    public static void SpawnFillCircle(Vector3 worldPos, float radius, float duration, Color color, Color outerColor)
    {
        var go = new GameObject("[MonsterGroundWarning.Fill]");
        var w  = go.AddComponent<MonsterGroundWarning>();
        w.SetupFill(worldPos, radius, duration, color, outerColor);
    }

    public static void Spawn(Vector3 worldPos, float radius, float duration, Color color)
    {
        var go = new GameObject("[MonsterGroundWarning]");
        var warning = go.AddComponent<MonsterGroundWarning>();
        warning.Setup(worldPos, radius, duration, color);
    }

    /// <summary>
    /// 보스 위치(origin)에서 forward 방향으로 length 길이, width 너비인 직사각형 경고를 표시한다.
    /// 직사각형의 뒤쪽 변이 origin에 위치한다.
    /// </summary>
    public static void SpawnRect(Vector3 origin, Vector3 forward, float width, float length, float duration, Color color)
    {
        var go = new GameObject("[MonsterGroundWarning.Rect]");
        var warning = go.AddComponent<MonsterGroundWarning>();
        warning.SetupRect(origin, forward, width, length, duration, color);
    }

    public static void SpawnGrid(Vector3 origin, Vector3 forward, GridShape shape, float duration, Color color)
    {
        var go = new GameObject("[MonsterGroundWarning.Grid]");
        var warning = go.AddComponent<MonsterGroundWarning>();
        warning.SetupGrid(origin, forward, shape, duration, color);
    }

    private void SetupFill(Vector3 worldPos, float radius, float duration, Color fillColor, Color outerColor)
    {
        transform.position = worldPos + Vector3.up * 0.05f;

        // 외곽 경계선 — 자식 오브젝트에 LineRenderer (같은 GO에 두 개 추가 불가)
        _fillMode      = true;
        _fillDuration  = Mathf.Max(0.1f, duration);
        _fillElapsed   = 0f;
        _fillRadius    = radius;
        _fillColor     = fillColor;
        _fillOuterColor = outerColor;
        _remaining     = duration + 0.15f; // 약간 여유
        var outerGo = new GameObject("OuterLine");
        outerGo.transform.SetParent(transform, false);
        _outerLine = outerGo.AddComponent<LineRenderer>();
        _outerLine.loop = true;
        _outerLine.useWorldSpace = false;
        _outerLine.positionCount = SegmentCount;
        _outerLine.widthMultiplier = 0.12f;
        _outerLine.material = GetMaterial();
        _outerLine.startColor = outerColor;
        _outerLine.endColor   = outerColor;
        _outerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _outerLine.receiveShadows    = false;
        for (int i = 0; i < SegmentCount; i++)
        {
            float a = (float)i / SegmentCount * Mathf.PI * 2f;
            _outerLine.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }

        // 채워지는 원 — 별도 자식 오브젝트 (초기 반경=0)
        var fillGo = new GameObject("FillLine");
        fillGo.transform.SetParent(transform, false);
        _fillLine = fillGo.AddComponent<LineRenderer>();
        _fillLine.loop = true;
        _fillLine.useWorldSpace = false;
        _fillLine.positionCount = SegmentCount;
        _fillLine.widthMultiplier = 0.08f;
        _fillLine.material = GetMaterial();
        _fillLine.startColor = fillColor;
        _fillLine.endColor   = fillColor;
        _fillLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _fillLine.receiveShadows    = false;
        UpdateFillLine(0f);
    }

    private void UpdateFillLine(float currentRadius)
    {
        if (_fillLine == null) return;
        for (int i = 0; i < SegmentCount; i++)
        {
            float a = (float)i / SegmentCount * Mathf.PI * 2f;
            _fillLine.SetPosition(i, new Vector3(Mathf.Cos(a) * currentRadius, 0f, Mathf.Sin(a) * currentRadius));
        }
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

    private void SetupRect(Vector3 origin, Vector3 forward, float width, float length, float duration, Color color)
    {
        _remaining = Mathf.Max(0.05f, duration);

        Vector3 fwd = forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        float hw = width * 0.5f;
        float groundY = origin.y + 0.05f;

        // 네 꼭짓점: 뒤쪽 변이 origin
        Vector3 bl = new Vector3((origin - right * hw).x, groundY, (origin - right * hw).z);
        Vector3 br = new Vector3((origin + right * hw).x, groundY, (origin + right * hw).z);
        Vector3 fl = new Vector3((origin + fwd * length - right * hw).x, groundY, (origin + fwd * length - right * hw).z);
        Vector3 fr = new Vector3((origin + fwd * length + right * hw).x, groundY, (origin + fwd * length + right * hw).z);

        var line = gameObject.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = 4;
        line.widthMultiplier = 0.1f;
        line.material = GetMaterial();
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        line.SetPosition(0, bl);
        line.SetPosition(1, br);
        line.SetPosition(2, fr);
        line.SetPosition(3, fl);
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

        if (_fillMode)
        {
            _fillElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_fillElapsed / _fillDuration);

            // 채워지는 원: 반경 0 → _fillRadius
            float curRadius = Mathf.Lerp(0f, _fillRadius, t);
            UpdateFillLine(curRadius);

            // 색상: 처음엔 노란색(주의)→빨간색(위험)으로 그라데이션
            Color curColor = Color.Lerp(_fillColor, _fillOuterColor, t);
            _fillLine.startColor = curColor;
            _fillLine.endColor   = curColor;

            // 채워짐 완료 시 외곽선 깜빡임
            if (t >= 1f && _outerLine != null)
            {
                float blink = Mathf.Sin(_fillElapsed * 20f) > 0f ? 1f : 0.2f;
                Color bc = _fillOuterColor;
                bc.a = blink;
                _outerLine.startColor = bc;
                _outerLine.endColor   = bc;
            }
        }

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
