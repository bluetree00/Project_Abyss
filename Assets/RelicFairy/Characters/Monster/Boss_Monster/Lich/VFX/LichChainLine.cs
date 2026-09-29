using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 사슬 줄 — 사슬 고리 텍스처가 반복되는 선(LineRenderer). 봉인 사슬(금) · 역류 사슬(보라) · 투척 낫 사슬에 쓴다.
/// 기본 도형 막대(<see cref="PatternGuideHelper.Link"/>)를 대신한다(연출·UX 시나리오 §3 B4).
///
/// · 끝점: <see cref="SetEnds"/>로 직접 놓거나 <see cref="Follow"/>로 두 Transform을 매 프레임 따라간다.
/// · 모양: 느슨하면 아래로 처지고(<see cref="Slack"/>), 팽팽해지면 곧게 펴지며 떨린다(<see cref="Tension"/>).
/// · 색: 선 정점 색 — 금→보라 역류는 <see cref="SetColor"/>를 보간해서 부른다. <see cref="Flash"/>는 잠깐 밝게.
/// 재질이 없으면(목록 미로드) 단색 선으로 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LichChainLine : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int   Segments       = 14;
    private const float DefaultSlack   = 0.08f;   // 길이 대비 처짐
    private const float ShakeAmplitude = 0.12f;
    private const float ShakeFrequency = 38f;
    private const float FlashBoost     = 2.5f;

    private static Material s_fallback;

    // ── Private ───────────────────────────────────────────────────
    private LineRenderer _line;
    private Transform    _from;
    private Transform    _to;
    private Vector3      _fromOffset;
    private Vector3      _toOffset;
    private Vector3      _fromPos;
    private Vector3      _toPos;
    private Color        _color = Color.white;
    private float        _slack = DefaultSlack;
    private float        _tension;
    private float        _flash;
    private float        _flashSeconds;
    private float        _seed;

    // ── Properties ────────────────────────────────────────────────
    public Color Color => _color;

    /// <summary>처짐(길이 대비, 0 = 곧게).</summary>
    public float Slack { get => _slack; set => _slack = Mathf.Max(0f, value); }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_from != null) _fromPos = _from.position + _fromOffset;
        if (_to   != null) _toPos   = _to.position   + _toOffset;

        if (_tension > 0f) _tension = Mathf.MoveTowards(_tension, 0f, Time.deltaTime * 1.5f);
        if (_flash > 0f)
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime / Mathf.Max(0.01f, _flashSeconds));
            ApplyColor();
        }
        Rebuild();
    }

    // ── Public Methods ────────────────────────────────────────────
    public static LichChainLine Create(Vector3 from, Vector3 to, float width, Color color)
    {
        var go   = new GameObject("LichChainLine");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace     = true;
        line.positionCount     = Segments;
        line.widthMultiplier   = width;
        line.textureMode       = LineTextureMode.Tile;
        line.textureScale      = new Vector2(1f / Mathf.Max(0.05f, width), 1f);   // 고리 비율 유지
        line.alignment         = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows    = false;
        line.numCapVertices    = 0;

        var mat = LichVfx.ChainMaterial;
        line.sharedMaterial = mat != null ? mat : Fallback;

        var chain = go.AddComponent<LichChainLine>();
        chain._line    = line;
        chain._fromPos = from;
        chain._toPos   = to;
        chain._seed    = Random.value * 100f;
        chain.SetColor(color);
        chain.Rebuild();
        return chain;
    }

    public void SetEnds(Vector3 from, Vector3 to)
    {
        _from    = null;
        _to      = null;
        _fromPos = from;
        _toPos   = to;
    }

    /// <summary>두 Transform을 따라간다(null이면 그쪽 끝은 마지막 위치에 고정).</summary>
    public void Follow(Transform from, Vector3 fromOffset, Transform to, Vector3 toOffset)
    {
        _from       = from;
        _fromOffset = fromOffset;
        _to         = to;
        _toOffset   = toOffset;
    }

    public void SetColor(Color color)
    {
        _color = color;
        ApplyColor();
    }

    /// <summary>잠깐 밝게 — 사슬이 당겨지는 순간.</summary>
    public void Flash(float seconds = 0.25f)
    {
        _flash        = 1f;
        _flashSeconds = seconds;
        ApplyColor();
    }

    /// <summary>팽팽하게 — 곧게 펴지고 떨린다. 1 = 최대, 1.5초에 걸쳐 풀린다.</summary>
    public void Tension(float amount = 1f) => _tension = Mathf.Clamp01(Mathf.Max(_tension, amount));

    public void Dispose()
    {
        if (this != null) Destroy(gameObject);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Rebuild()
    {
        if (_line == null) return;

        Vector3 delta = _toPos - _fromPos;
        float   len   = delta.magnitude;
        Vector3 side  = len > 0.001f ? Vector3.Cross(delta / len, Vector3.up) : Vector3.right;
        if (side.sqrMagnitude < 0.001f) side = Vector3.right;
        side.Normalize();

        float sag   = _slack * len * (1f - _tension);
        float shake = ShakeAmplitude * _tension;
        float time  = Time.time * ShakeFrequency + _seed;

        for (int i = 0; i < Segments; i++)
        {
            float   t = i / (float)(Segments - 1);
            Vector3 p = _fromPos + delta * t;
            float   w = 4f * t * (1f - t);                       // 양 끝 0, 가운데 1
            p.y -= sag * w;
            if (shake > 0f) p += side * (Mathf.Sin(time + t * 9f) * shake * w);
            _line.SetPosition(i, p);
        }
    }

    private void ApplyColor()
    {
        if (_line == null) return;
        Color c = _color * (1f + _flash * (FlashBoost - 1f));
        c.a = _color.a;
        _line.startColor = c;
        _line.endColor   = c;
    }

    private static Material Fallback
    {
        get
        {
            if (s_fallback == null)
                s_fallback = new Material(Shader.Find("Sprites/Default"));
            return s_fallback;
        }
    }
}
}
