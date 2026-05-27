using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 전투 존 진입 시 생성되는 투명 에너지 장벽.
/// 4방향 경계에 BoxCollider(물리 차단) + 수평 스캔라인(시각 효과)를 배치한다.
/// ZoneProgressionService가 존 클리어 후 Open()을 호출하면 페이드 아웃 후 자폭한다.
/// </summary>
public class CombatBarrier : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────
    private const float WallHeight    = 5.0f;
    private const float WallThickness = 0.4f;
    private const float LineSpacing   = 0.65f;
    private const float LineWidth     = 0.035f;
    private const float PulseSpeed    = 1.3f;
    private const float AlphaMin      = 0.05f;
    private const float AlphaMax      = 0.18f;
    private const float FadeDuration  = 0.5f;

    private static readonly Color BarrierColor = new Color(0.35f, 0.82f, 1.00f);

    // ── Private fields ────────────────────────────────────────────
    private readonly List<BoxCollider>  _colliders = new();
    private readonly List<LineRenderer> _lines     = new();
    private readonly List<Material>     _mats      = new();

    private float _phaseOffset;
    private bool  _opening;

    // ── Init ──────────────────────────────────────────────────────

    public void Initialize(float zoneSizeX, float zoneSizeZ)
    {
        _phaseOffset = Random.value * Mathf.PI * 2f;
        BuildWalls(zoneSizeX, zoneSizeZ);
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Update()
    {
        if (_opening || _lines.Count == 0) return;

        float alpha = Mathf.Lerp(AlphaMin, AlphaMax,
            (Mathf.Sin(Time.time * PulseSpeed + _phaseOffset) + 1f) * 0.5f);
        var color = new Color(BarrierColor.r, BarrierColor.g, BarrierColor.b, alpha);

        foreach (var lr in _lines)
        {
            if (lr == null) continue;
            lr.startColor = color;
            lr.endColor   = color;
        }
    }

    private void OnDestroy()
    {
        foreach (var mat in _mats)
            if (mat != null) Destroy(mat);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>존 클리어 시 ZoneProgressionService가 호출. 물리 차단 해제 후 페이드 아웃.</summary>
    public void Open()
    {
        if (_opening) return;
        OpenAsync(gameObject.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── Private ───────────────────────────────────────────────────

    private void BuildWalls(float zoneSizeX, float zoneSizeZ)
    {
        float hx = zoneSizeX * 0.5f;
        float hz = zoneSizeZ * 0.5f;

        // North / South  (X 방향으로 뻗는 벽)
        CreatePhysicalWall(new Vector3( 0f, WallHeight * 0.5f,  hz), new Vector3(zoneSizeX, WallHeight, WallThickness));
        CreatePhysicalWall(new Vector3( 0f, WallHeight * 0.5f, -hz), new Vector3(zoneSizeX, WallHeight, WallThickness));
        AddScanLines_XWall( hz, hx);
        AddScanLines_XWall(-hz, hx);

        // East / West  (Z 방향으로 뻗는 벽)
        CreatePhysicalWall(new Vector3( hx, WallHeight * 0.5f,  0f), new Vector3(WallThickness, WallHeight, zoneSizeZ));
        CreatePhysicalWall(new Vector3(-hx, WallHeight * 0.5f,  0f), new Vector3(WallThickness, WallHeight, zoneSizeZ));
        AddScanLines_ZWall( hx, hz);
        AddScanLines_ZWall(-hx, hz);
    }

    private void CreatePhysicalWall(Vector3 localCenter, Vector3 size)
    {
        var go = new GameObject("BarrierWall");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localCenter;

        var col    = go.AddComponent<BoxCollider>();
        col.size   = size;
        col.center = Vector3.zero;
        _colliders.Add(col);
    }

    // 수평 스캔라인 — North/South 벽 (라인이 X축 방향)
    private void AddScanLines_XWall(float z, float xHalf)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(WallHeight / LineSpacing));
        for (int i = 0; i <= count; i++)
        {
            float y = (i / (float)count) * WallHeight;
            AddLine(new Vector3(-xHalf, y, z), new Vector3(xHalf, y, z));
        }
    }

    // 수평 스캔라인 — East/West 벽 (라인이 Z축 방향)
    private void AddScanLines_ZWall(float x, float zHalf)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(WallHeight / LineSpacing));
        for (int i = 0; i <= count; i++)
        {
            float y = (i / (float)count) * WallHeight;
            AddLine(new Vector3(x, y, -zHalf), new Vector3(x, y, zHalf));
        }
    }

    private void AddLine(Vector3 from, Vector3 to)
    {
        var go = new GameObject("Line");
        go.transform.SetParent(transform, false);

        var lr             = go.AddComponent<LineRenderer>();
        lr.useWorldSpace   = false;
        lr.positionCount   = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth          = LineWidth;
        lr.endWidth            = LineWidth;
        lr.shadowCastingMode   = ShadowCastingMode.Off;
        lr.receiveShadows      = false;
        lr.generateLightingData = false;

        var shader = Shader.Find("Sprites/Default");
        var mat    = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        lr.material    = mat;
        lr.startColor  = new Color(BarrierColor.r, BarrierColor.g, BarrierColor.b, AlphaMin);
        lr.endColor    = lr.startColor;

        _mats.Add(mat);
        _lines.Add(lr);
    }

    private async UniTaskVoid OpenAsync(CancellationToken ct)
    {
        _opening = true;

        foreach (var col in _colliders)
            if (col != null) col.enabled = false;

        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(AlphaMax, 0f, elapsed / FadeDuration);
            var color = new Color(BarrierColor.r, BarrierColor.g, BarrierColor.b, alpha);
            foreach (var lr in _lines)
            {
                if (lr == null) continue;
                lr.startColor = color;
                lr.endColor   = color;
            }
            try { await UniTask.NextFrame(ct); }
            catch (System.OperationCanceledException) { return; }
        }

        Destroy(gameObject);
    }
}
