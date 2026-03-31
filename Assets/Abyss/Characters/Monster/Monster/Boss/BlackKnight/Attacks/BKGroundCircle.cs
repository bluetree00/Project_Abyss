using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 독립형 지면 경고 원형 장판.
/// - BKGroundCirclePool 풀링 지원: Awake 에서 컴포넌트 생성, ResetForPool 로 재활용.
/// - 풀 없이 사용 시: Spawn() 정적 메서드 — 타이머 만료 후 자동 Destroy.
/// </summary>
public class BKGroundCircle : MonoBehaviour
{
    private const int Segments = 48;

    private LineRenderer _lr;
    private MeshFilter   _mf;
    private Transform    _fillTransform;
    private Material     _lrMat;
    private Material     _fillMat;
    private float        _duration;
    private float        _timer;
    private bool         _running;

    /// <summary>풀에서 사용 시 반드시 설정. null 이면 타이머 만료 시 Destroy.</summary>
    public BKGroundCirclePool Pool { get; set; }

    private static readonly Color OutlineFaint  = new Color(1f, 0.55f, 0f, 0.40f);
    private static readonly Color OutlineBright = new Color(1f, 0.00f, 0f, 1.00f);
    private static readonly Color FillFaint     = new Color(1f, 0.45f, 0f, 0.10f);
    private static readonly Color FillBright    = new Color(1f, 0.00f, 0f, 0.32f);

    // ── 컴포넌트 일회성 생성 ──────────────────────────────
    private void Awake()
    {
        // 아웃라인 (LineRenderer)
        _lr = gameObject.AddComponent<LineRenderer>();
        _lrMat = new Material(Shader.Find("Sprites/Default"));
        _lr.material          = _lrMat;
        _lr.positionCount     = Segments + 1;
        _lr.widthMultiplier   = 0.15f;
        _lr.useWorldSpace     = false;   // GO 위치 기준 로컬 좌표 → Fill 과 항상 일치
        _lr.shadowCastingMode = ShadowCastingMode.Off;
        _lr.receiveShadows    = false;

        // 채우기 디스크 (MeshRenderer)
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(transform, false);
        _fillTransform = fillGO.transform;
        _mf = fillGO.AddComponent<MeshFilter>();
        var mr = fillGO.AddComponent<MeshRenderer>();
        _fillMat = new Material(Shader.Find("Sprites/Default")) { color = FillFaint };
        mr.material          = _fillMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
    }

    // ── 풀에서 꺼낼 때 초기화 ────────────────────────────
    public void ResetForPool(Vector3 worldPos, float radius, float duration)
    {
        Apply(worldPos, radius, duration);
        gameObject.SetActive(true);
    }

    // ── 풀 없이 사용 시 초기화 ───────────────────────────
    public void Init(Vector3 worldPos, float radius, float duration)
    {
        Apply(worldPos, radius, duration);
    }

    /// <summary>지정 위치에 경고 원을 스폰하고 duration 후 자동 제거한다 (풀 미사용).</summary>
    public static void Spawn(Vector3 worldPos, float radius, float duration)
    {
        var go = new GameObject("[DropWarning]");
        go.AddComponent<BKGroundCircle>().Init(worldPos, radius, duration);
    }

    // ── 공통 적용 ────────────────────────────────────────
    private void Apply(Vector3 worldPos, float radius, float duration)
    {
        _duration = _timer = duration;
        _running  = true;

        // GO 자체를 대상 위치로 이동 (보스 계층 내 로컬 좌표로 자동 변환)
        transform.position = worldPos;

        const float yOff = 0.05f;

        // 아웃라인: useWorldSpace=false 이므로 GO 기준 로컬 좌표
        for (int i = 0; i <= Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, yOff, Mathf.Sin(a) * radius));
        }
        _lrMat.color = OutlineFaint;

        // 채우기 디스크: 로컬 좌표 (GO 기준)
        _fillTransform.localPosition = new Vector3(0f, yOff, 0f);
        if (_mf.mesh != null) Destroy(_mf.mesh);
        _mf.mesh       = BuildDiscMesh(radius);
        _fillMat.color = FillFaint;
    }

    // ── 타이머 + 색상 보간 ────────────────────────────────
    private void Update()
    {
        if (!_running) return;
        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            _running = false;
            if (Pool != null) Pool.Return(this);
            else              Destroy(gameObject);
            return;
        }

        float t = 1f - Mathf.Clamp01(_timer / _duration);
        if (_lrMat   != null) _lrMat.color   = Color.Lerp(OutlineFaint, OutlineBright, t);
        if (_fillMat != null) _fillMat.color  = Color.Lerp(FillFaint,   FillBright,    t);
    }

    private void OnDestroy()
    {
        if (_lrMat   != null) Destroy(_lrMat);
        if (_fillMat != null) Destroy(_fillMat);
        if (_mf != null && _mf.mesh != null) Destroy(_mf.mesh);
    }

    // ─────────────────────────────────────────────────────
    private static Mesh BuildDiscMesh(float radius)
    {
        var mesh  = new Mesh();
        var verts = new Vector3[Segments + 1];
        var tris  = new int[Segments * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }
        for (int i = 0; i < Segments; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % Segments + 1;
        }
        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
}
