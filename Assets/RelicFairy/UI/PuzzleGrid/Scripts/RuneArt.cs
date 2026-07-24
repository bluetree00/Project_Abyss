using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// <see cref="RuneArtLibrarySO"/> 접근 창구. Addressable "RuneArtLibrary"를 1회 로드해 캐싱한다.
/// 코드 생성 UI(선택 팝업 등)가 동기적으로 아트를 읽을 수 있도록, 앱 부트에서 <see cref="PreloadAsync"/>로 미리 로드한다.
/// 미로드/실패 시 GetArt 등은 null을 반환 → 호출부는 색상 폴백으로 동작.
/// </summary>
public static class RuneArt
{
    private const string Address = "RuneArtLibrary";

    private static RuneArtLibrarySO _lib;
    private static bool _loading;

    public static bool IsLoaded => _lib != null;

    // 도메인리로드 비활성(fast play mode)에서도 정적 상태가 새 세션으로 새로 시작하도록 초기화
    // (라이브러리는 불변이라 성능 폴백일 뿐이지만, UISkin/EffectIconRegistry 관례와 맞춘다).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _lib = null;
        _loading = false;
    }

    /// <summary>앱 부트에서 1회 호출. 이미 로드됐으면 즉시 반환.</summary>
    public static async UniTask PreloadAsync()
    {
        if (_lib != null || _loading) return;
        _loading = true;
        try
        {
            _lib = await Managers.AddressableManager.TryLoadAssetAsync<RuneArtLibrarySO>(Address);
            if (_lib == null)
                Debug.LogWarning("[RuneArt] RuneArtLibrary 로드 실패 — 룬 아트 없이 색상 폴백으로 동작");
        }
        finally { _loading = false; }
    }

    /// <summary>등급 룬 아트(미로드 시 null → 색상 폴백).</summary>
    public static Sprite GetArt(ItemRarity rarity) => _lib != null ? _lib.GetArt(rarity) : null;

    /// <summary>등급 룬 테두리(미로드 시 null).</summary>
    public static Sprite GetBorder(ItemRarity rarity) => _lib != null ? _lib.GetBorder(rarity) : null;

    /// <summary>속성 룬 아트(ElementDef.Order). 미로드/미할당 시 null → 색 틴트 폴백.</summary>
    public static Sprite GetArtByElement(string elementId) => _lib != null ? _lib.GetArtByElement(ElementIndex(elementId)) : null;

    /// <summary>속성 룬 테두리.</summary>
    public static Sprite GetBorderByElement(string elementId) => _lib != null ? _lib.GetBorderByElement(ElementIndex(elementId)) : null;

    private static int ElementIndex(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return -1;
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++) if (order[i] == elementId) return i;
        return -1;
    }
}
