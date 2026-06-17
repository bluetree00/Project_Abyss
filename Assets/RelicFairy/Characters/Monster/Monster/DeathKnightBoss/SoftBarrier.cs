using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 플레이어는 막고 투사체는 통과시키는 반투명 장벽.
/// - 콜라이더 includeLayers = Player 레이어만 → 투사체(Default) 무시
/// - OnEnable 시 _barrierMaterial 인스턴스를 생성해 tint/emission 적용
/// - SetTint()로 런타임 색상 변경 (DK 검 색상 연동)
/// </summary>
[DisallowMultipleComponent]
public class SoftBarrier : MonoBehaviour
{
    [SerializeField] private Material _barrierMaterial;
    [SerializeField] private Color _barrierTint     = new Color(0.9f, 0.9f, 1.0f, 0.05f);
    [SerializeField] private Color _barrierEmission = new Color(0f,   0f,   0f,   1f);

    private Renderer[] _renderers;

    private void Awake()
    {
        int playerMask = 1 << LayerMask.NameToLayer("Player");
        foreach (var col in GetComponentsInChildren<Collider>(true))
            col.includeLayers = playerMask;
    }

    private void OnEnable()
    {
        ApplyMaterial();
    }

    public void SetTint(Color tint, Color emission)
    {
        _barrierTint     = tint;
        _barrierEmission = emission;
        if (_renderers == null) return;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
            {
                mat.SetColor("_BaseColor",     _barrierTint);
                mat.SetColor("_Color",         _barrierTint);
                mat.SetColor("_EmissionColor", _barrierEmission);
                if (_barrierEmission.maxColorComponent > 0.01f)
                    mat.EnableKeyword("_EMISSION");
                else
                    mat.DisableKeyword("_EMISSION");
            }
        }
    }

    private void ApplyMaterial()
    {
        if (_barrierMaterial == null)
        {
            Debug.LogWarning("[SoftBarrier] _barrierMaterial 이 할당되지 않았습니다.", this);
            return;
        }

        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
        {
            var newMats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < newMats.Length; i++)
            {
                var mat = new Material(_barrierMaterial);
                mat.SetColor("_BaseColor",     _barrierTint);
                mat.SetColor("_Color",         _barrierTint);
                mat.SetColor("_EmissionColor", _barrierEmission);
                if (_barrierEmission.maxColorComponent > 0.01f)
                    mat.EnableKeyword("_EMISSION");
                newMats[i] = mat;
            }
            r.materials = newMats;
        }
    }
}
}
