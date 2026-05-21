using UnityEngine;

/// <summary>
/// 원소별 쉐이더 주입값(틴트 색, 강도, 에미션 강도 등)을 Inspector로 편집 가능하게 하는 전역 설정.
///
/// 한 게임에 한 SO만 존재하는 것을 전제로 사용 — ElementNativePalette.SetPaletteSO(so)로 전역 주입한다.
/// 몬스터별 개별 튜닝이 필요해지면 엔트리를 추가하거나 별도 SO 계층을 도입해 확장할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "ElementPalette_Default", menuName = "RelicFairy/Elements/Element Palette")]
public class ElementPaletteSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public ElementType element;

        [Header("팔레트 색 (모든 팔레트 Color 프로퍼티에 Lerp로 주입)")]
        public Color tintColor;
        [Range(0f, 1f)] public float tintStrength;

        [Header("Emission 보강 (셰이더가 _EmissionColor 지원 시)")]
        public bool applyEmission;
        [Range(0f, 3f)] public float emissionIntensity;

        [Header("커스텀 에미션 색 (off면 tintColor × intensity 자동 계산)")]
        public bool useCustomEmissionColor;
        [ColorUsage(false, true)] public Color customEmissionColor;
    }

    [SerializeField] private Entry[] entries;

    /// <summary>element에 해당하는 엔트리 반환. 없으면 false.</summary>
    public bool TryGet(ElementType element, out Entry entry)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].element == element)
                {
                    entry = entries[i];
                    return true;
                }
            }
        }
        entry = default;
        return false;
    }

    /// <summary>에디터에서 "Reset" 시 5원소 기본 프리셋으로 초기화.</summary>
    private void Reset()
    {
        entries = new[]
        {
            new Entry { element = ElementType.Lightning, tintColor = new Color(1.00f, 0.92f, 0.23f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f },
            new Entry { element = ElementType.Water,     tintColor = new Color(0.13f, 0.59f, 0.95f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f },
            new Entry { element = ElementType.Fire,      tintColor = new Color(0.96f, 0.26f, 0.21f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f },
            new Entry { element = ElementType.Grass,     tintColor = new Color(0.30f, 0.69f, 0.31f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f },
            new Entry { element = ElementType.Earth,     tintColor = new Color(0.55f, 0.43f, 0.39f, 1f), tintStrength = 0.7f, applyEmission = true, emissionIntensity = 0.6f },
        };
    }
}
