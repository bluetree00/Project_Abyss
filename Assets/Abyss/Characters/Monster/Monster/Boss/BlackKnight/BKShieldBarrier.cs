using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BlackKnight RainAttack 무적 구간 방어막 시각 효과.
/// 프로시저럴 반투명 구체 + 박동(pulse) 애니메이션.
/// BKRainAttackState.Enter() 에서 Create(), Exit() 에서 Destroy() 한다.
/// </summary>
public class BKShieldBarrier : MonoBehaviour
{
    private Material _mat;
    private float    _phase;
    private Color    _baseColor;
    private Color    _peakColor;
    private float    _pulsePeriod;

    // ── 생성 ─────────────────────────────────────────────
    /// <summary>보스 자식으로 방어막 구체를 생성한다.</summary>
    public static BKShieldBarrier Create(
        Transform parent,
        float radius,
        Color baseColor,
        Color peakColor,
        float pulsePeriod)
    {
        var go = CreateSphere("[ShieldBarrier]", parent,
                              radius, Vector3.up * radius * 0.5f);

        var mr = go.GetComponent<MeshRenderer>();
        var mat = MakeMaterial(baseColor);
        mr.material          = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        var barrier = go.AddComponent<BKShieldBarrier>();
        barrier._mat         = mat;
        barrier._baseColor   = baseColor;
        barrier._peakColor   = peakColor;
        barrier._pulsePeriod = Mathf.Max(0.1f, pulsePeriod);
        return barrier;
    }

    // ── 페이드아웃 ────────────────────────────────────────
    private bool  _fading;
    private float _fadeTimer;
    private float _fadeDuration;
    private float _startAlpha;

    /// <summary>duration 초 동안 알파를 0으로 줄인 후 자동 파괴.</summary>
    public void FadeOut(float duration = 1.0f)
    {
        _fading      = true;
        _fadeDuration = Mathf.Max(0.05f, duration);
        _fadeTimer   = 0f;
        _startAlpha  = _mat != null ? _mat.color.a : 0f;
    }

    // ── 박동 / 페이드 업데이트 ────────────────────────────
    private void Update()
    {
        if (_mat == null) return;

        if (_fading)
        {
            _fadeTimer += Time.deltaTime;
            float alpha = Mathf.Lerp(_startAlpha, 0f, _fadeTimer / _fadeDuration);
            Color c = _mat.color;
            c.a = alpha;
            _mat.color = c;

            if (_fadeTimer >= _fadeDuration)
                Destroy(gameObject);
            return;
        }

        _phase += Time.deltaTime * (Mathf.PI * 2f / _pulsePeriod);
        float t = (Mathf.Sin(_phase) + 1f) * 0.5f;
        _mat.color = Color.Lerp(_baseColor, _peakColor, t);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    // ── 헬퍼 ─────────────────────────────────────────────
    private static GameObject CreateSphere(string name, Transform parent,
                                           float radius, Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = Vector3.one * (radius * 2f); // 직경 = radius * 2
        return go;
    }

    private static Material MakeMaterial(Color color)
    {
        // "Sprites/Default": 라이팅 없음, 양면 렌더, 알파 투명 지원 — 파이프라인 무관
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color        = color;
        mat.renderQueue  = 3100; // Transparent 이후
        return mat;
    }
}
