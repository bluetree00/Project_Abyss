using UnityEngine;

/// <summary>
/// 피격자(몬스터) 측 타격 연출 프리셋.
/// 블로그 ⑧ 피격자 반응(색/형태/질감) + ② 화면 번쩍임(Light) 파라미터를 한 SO에 통합.
///
/// ※ 형태(피격 애니메이션)와 넉백은 GetHitState가 기존 처리 → 이 SO 범위 외.
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Combat/Monster Hit Profile", fileName = "MonsterHitProfile")]
public class MonsterHitProfileSO : ScriptableObject
{
    // ── Serialized ────────────────────────────────────────────────
    [Header("⑧ Flash (색 변화)")]
    [SerializeField] private Color _flashColor = Color.white;
    [SerializeField, Range(0f, 10f)] private float _emissionBoost = 2f;
    [SerializeField, Range(0f, 1f)]  private float _flashDuration = 0.06f;
    [SerializeField] private AnimationCurve _flashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("② Point Light (화면 번쩍임)")]
    [SerializeField] private bool  _usePointLight = true;
    [SerializeField] private Color _lightColor = Color.white;
    [SerializeField, Range(0f, 20f)] private float _lightIntensity = 5f;
    [SerializeField, Range(0.1f, 10f)] private float _lightRange = 3f;
    [SerializeField] private Vector3 _lightLocalOffset = new(0f, 1f, 0f);
    [SerializeField, Range(0f, 1f)]  private float _lightDuration = 0.10f;
    [SerializeField] private AnimationCurve _lightCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("⑧ Element Overrides (속성별 Flash 색)")]
    [SerializeField] private Color _lightningFlashColor = new(1f,   0.95f, 0.4f);
    [SerializeField] private Color _waterFlashColor     = new(0.2f, 0.5f,  1f);
    [SerializeField] private Color _fireFlashColor      = new(1f,   0.3f,  0.05f);
    [SerializeField] private Color _grassFlashColor     = new(0.3f, 1f,    0.3f);
    [SerializeField] private Color _earthFlashColor     = new(0.8f, 0.6f,  0.3f);

    // ── Properties ────────────────────────────────────────────────
    public Color          FlashColor        => _flashColor;
    public float          EmissionBoost     => _emissionBoost;
    public float          FlashDuration     => _flashDuration;
    public AnimationCurve FlashCurve        => _flashCurve;

    public bool           UsePointLight     => _usePointLight;
    public Color          LightColor        => _lightColor;
    public float          LightIntensity    => _lightIntensity;
    public float          LightRange        => _lightRange;
    public Vector3        LightLocalOffset  => _lightLocalOffset;
    public float          LightDuration     => _lightDuration;
    public AnimationCurve LightCurve        => _lightCurve;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>원소 속성에 따른 Flash 색상 반환. None이면 기본 flashColor.</summary>
    public Color GetFlashColor(ElementType element)
    {
        return element switch
        {
            ElementType.Lightning => _lightningFlashColor,
            ElementType.Water     => _waterFlashColor,
            ElementType.Fire      => _fireFlashColor,
            ElementType.Grass     => _grassFlashColor,
            ElementType.Earth     => _earthFlashColor,
            _                     => _flashColor,
        };
    }

    /// <summary>원소 속성에 따른 PointLight 색상 반환.</summary>
    public Color GetLightColor(ElementType element)
    {
        // Flash와 동일 색상 채택 — 일관된 시각 인상
        return element == ElementType.None ? _lightColor : GetFlashColor(element);
    }
}
