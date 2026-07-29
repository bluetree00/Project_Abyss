using UnityEngine;

/// <summary>
/// 챕터 세계 전체를 덮는 배리어 면(수면·용암·안개·허공).
/// 비활성 방 플랫폼이 배리어 아래에 잠긴 것처럼 보이게 하는 시각 레이어.
/// ChapterWorldBootstrapper가 Initialize()를 호출해 챕터에 맞는 머티리얼을 적용한다.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class BarrierVolume : MonoBehaviour
{
    // ─────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────

    [Header("배리어 머티리얼 (챕터별)")]
    [SerializeField] private Material _waterMaterial;
    [SerializeField] private Material _lavaMaterial;
    [SerializeField] private Material _fogMaterial;
    [SerializeField] private Material _voidMaterial;

    [Header("수면 파문 이펙트 (선택)")]
    [Tooltip("플랫폼 솟아오를 때 수면에 생성할 이펙트 프리팹 Addressable 키. 비워두면 이펙트 없음.")]
    [SerializeField] private string _rippleEffectKey;

    // ─────────────────────────────────────────
    // Private Fields
    // ─────────────────────────────────────────

    private MeshRenderer _renderer;

    // ─────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────

    public BarrierType ActiveType   { get; private set; }
    public string      RippleEffectKey => _rippleEffectKey;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void Awake() => _renderer = GetComponent<MeshRenderer>();

    // ─────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────

    /// <summary>챕터 배리어 타입에 맞는 머티리얼을 적용한다.</summary>
    public void Initialize(BarrierType type)
    {
        ActiveType = type;
        _renderer.material = type switch
        {
            BarrierType.Water => _waterMaterial,
            BarrierType.Lava  => _lavaMaterial,
            BarrierType.Fog   => _fogMaterial,
            BarrierType.Void  => _voidMaterial,
            _                 => _waterMaterial,
        };
    }

    /// <summary>배리어를 완전히 숨긴다 (Boss 클리어 연출 등).</summary>
    public void Hide() => gameObject.SetActive(false);
}
