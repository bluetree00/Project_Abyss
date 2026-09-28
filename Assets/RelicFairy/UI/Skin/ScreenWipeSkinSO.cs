using UnityEngine;

/// <summary>
/// 방 전환 와이프(<see cref="ScreenFade.CoverAsync"/>) 아트 — 단색 판 대신 잉크처럼 찢긴 가장자리 · 심연 구름 · 룬 마법진.
/// 전용 납품 아트가 없어 _Imported(Hovl) 텍스처를 조합한다(렌더 비교로 고름, 09-26).
///
/// <para>미등록이면 <see cref="ScreenFade"/>가 예전 단색 판으로 돈다 — 스킨이 0개여도 전환은 그대로 동작한다.</para>
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Screen Wipe Skin", fileName = "ScreenWipeSkin")]
public sealed class ScreenWipeSkinSO : ScriptableObject
{
    [Tooltip("UI/RoomWipe 셰이더 — 스킨이 참조해야 번들에 같이 들어간다.")]
    [SerializeField] private Shader    _shader;
    [Tooltip("가장자리를 찢고 몸통 구름을 그리는 잉크 노이즈(반복 텍스처). 기본 Noise65.")]
    [SerializeField] private Texture2D _inkNoise;
    [Tooltip("가운데 룬 마법진(밝기 = 모양). 기본 MagicCircle23.")]
    [SerializeField] private Texture2D _sigil;

    [Header("가장자리")]
    [SerializeField, Range(0f, 0.4f)]  private float _ragged       = 0.12f;
    [Tooltip("작을수록 결이 크다. 크게 올리면 판 안에 구멍이 뚫린다(시안 1차).")]
    [SerializeField, Range(0.2f, 3f)]  private float _noiseScale   = 0.7f;
    [SerializeField, Range(0f, 0.1f)]  private float _rimWidth     = 0.022f;
    [SerializeField, Range(0f, 3f)]    private float _rimIntensity = 1.1f;

    [Header("몸통")]
    [SerializeField] private Color _abyss = new(0.025f, 0.022f, 0.045f, 1f);
    [Tooltip("방 종류색이 섞이는 하한·상한 — 온통 방 색이면 색종이처럼 보인다.")]
    [SerializeField, Range(0f, 1f)]    private float _tintMin = 0.12f;
    [SerializeField, Range(0f, 1f)]    private float _tintMax = 0.45f;   // 09-26 0.62 → 0.45: 방 색 구름이 과했다
    [SerializeField, Range(0f, 0.2f)]  private float _drift   = 0.04f;

    [Header("마법진")]
    [SerializeField, Range(0.1f, 1.5f)] private float _sigilScale = 0.62f;
    [SerializeField, Range(0f, 1f)]     private float _sigilAlpha = 0.30f;
    [SerializeField, Range(-2f, 2f)]    private float _sigilSpin  = 0.5f;

    public Shader    Shader    => _shader;
    public Texture2D InkNoise  => _inkNoise;
    public Texture2D Sigil     => _sigil;

    /// <summary>스킨 값을 재질에 옮긴다(재질을 만들 때 1회).</summary>
    public void ApplyTo(Material m)
    {
        m.SetTexture("_Noise", _inkNoise);
        m.SetTexture("_Sigil", _sigil);
        m.SetColor("_Abyss", _abyss);
        m.SetFloat("_Ragged", _ragged);
        m.SetFloat("_NoiseScale", _noiseScale);
        m.SetFloat("_RimWidth", _rimWidth);
        m.SetFloat("_RimIntensity", _rimIntensity);
        m.SetFloat("_TintMin", _tintMin);
        m.SetFloat("_TintMax", _tintMax);
        m.SetFloat("_Drift", _drift);
        m.SetFloat("_SigilScale", _sigilScale);
        m.SetFloat("_SigilAlpha", _sigilAlpha);
        m.SetFloat("_SigilSpin", _sigilSpin);
    }
}
