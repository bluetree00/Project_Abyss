using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 몬스터 발밑 원형 시인성 마커.
/// MonsterBase.OnEnable/OnDisable 에 연동해 풀 재사용 시 자동으로 켜고 끈다.
/// 프리팹에 컴포넌트로 부착하면 동작 — 별도 코드 연동 불필요.
/// </summary>
[DisallowMultipleComponent]
public class MonsterGroundMarker : MonoBehaviour
{
    // ── Serialized ────────────────────────────────────────────────
    [Header("링 설정")]
    [Tooltip("링 색상 (알파로 투명도 조절)")]
    [SerializeField] private Color _ringColor = new Color(0.85f, 0.92f, 1f, 0.7f);
    [Tooltip("링 반경 (m)")]
    [SerializeField, Range(0.2f, 4f)] private float _radius = 0.75f;
    [Tooltip("링 선 두께 (m)")]
    [SerializeField, Range(0.01f, 0.2f)] private float _lineWidth = 0.045f;
    [Tooltip("링 분절 수 (높을수록 부드러움)")]
    [SerializeField, Range(16, 64)] private int _segments = 36;
    [Tooltip("바닥 위 오프셋 (z-fighting 방지)")]
    [SerializeField] private float _heightOffset = 0.02f;

    // ── Private ───────────────────────────────────────────────────
    private LineRenderer _line;
    private Material     _mat;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()  => Build();
    private void OnEnable()  { if (_line != null) _line.enabled = true; }
    private void OnDisable() { if (_line != null) _line.enabled = false; }
    private void OnDestroy() { if (_mat  != null) Destroy(_mat); }

    // ── Private Methods ───────────────────────────────────────────
    private void Build()
    {
        var go = new GameObject("~GroundRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * _heightOffset;
        go.transform.localRotation = Quaternion.identity;

        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace   = false;
        _line.loop            = true;
        _line.positionCount   = _segments;
        _line.startWidth      = _lineWidth;
        _line.endWidth        = _lineWidth;
        _line.startColor      = _ringColor;
        _line.endColor        = _ringColor;
        _line.shadowCastingMode = ShadowCastingMode.Off;
        _line.receiveShadows  = false;
        _line.numCapVertices  = 4;

        _mat = CreateMaterial();
        _line.material = _mat;

        for (int i = 0; i < _segments; i++)
        {
            float t = i / (float)_segments * Mathf.PI * 2f;
            _line.SetPosition(i, new Vector3(Mathf.Cos(t) * _radius, 0f, Mathf.Sin(t) * _radius));
        }
    }

    private Material CreateMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader) { color = _ringColor };

        if (shader.name.Contains("Universal"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.SetFloat("_ZWrite",  0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        return mat;
    }
}
