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
}
