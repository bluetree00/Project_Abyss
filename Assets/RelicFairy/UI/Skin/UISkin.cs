using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 화면별 아트 스킨 SO 접근 창구. Addressable로 1회 로드해 캐싱하고, 실패해도 null을 돌려
/// 호출측이 색 폴백으로 계속 동작하게 한다.
///
/// 기존 <see cref="RuneArt"/> / <see cref="EffectIconRegistry"/>와 동일한 관례를 따른다 —
/// 세 번째 패턴을 만들지 않는다. 앱 부트에서 <see cref="PreloadAsync"/>로 미리 로드한다.
/// </summary>
public static class UISkin
{
    private const string RefineryAddress    = "UI/RefinerySkin";
    private const string RuneSelectAddress  = "UI/RuneSelectSkin";
    private const string WeaponForgeAddress = "UI/WeaponForgeSkin";
    private const string CrucibleAddress    = "UI/CrucibleSkin";
    private const string ShopAddress        = "UI/ShopSkin";

    private static RefinerySkinSO    _refinery;
    private static RuneSelectSkinSO  _runeSelect;
    private static WeaponForgeSkinSO _weaponForge;
    private static CrucibleSkinSO    _crucible;
    private static ShopSkinSO        _shop;
    private static bool _tried;

    /// <summary>정제소 스킨. 미로드/미등록이면 null → 코드로 그린 색 박스가 그대로 보인다.</summary>
    public static RefinerySkinSO Refinery => _refinery;

    /// <summary>룬 획득 팝업 스킨. 미로드/미등록이면 null → 색 폴백.</summary>
    public static RuneSelectSkinSO RuneSelect => _runeSelect;

    /// <summary>무기 선택(모루) 팝업 스킨. 미로드/미등록이면 null → 프리팹 기존 색.</summary>
    public static WeaponForgeSkinSO WeaponForge => _weaponForge;

    /// <summary>재련소 스킨. 미로드/미등록이면 null → 색 폴백.</summary>
    public static CrucibleSkinSO Crucible => _crucible;

    /// <summary>상점(심연의 행상) 스킨. 미로드/미등록이면 null → 색 폴백.</summary>
    public static ShopSkinSO Shop => _shop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _refinery    = null;
        _runeSelect  = null;
        _weaponForge = null;
        _crucible    = null;
        _shop        = null;
        _tried       = false;
    }

    /// <summary>앱 부트에서 1회 호출. 이미 시도했으면 즉시 반환.</summary>
    public static async UniTask PreloadAsync()
    {
        if (_tried) return;
        _tried = true;

        _refinery    = await LoadOrNull<RefinerySkinSO>(RefineryAddress, "정제소");
        _runeSelect  = await LoadOrNull<RuneSelectSkinSO>(RuneSelectAddress, "룬 획득");
        _weaponForge = await LoadOrNull<WeaponForgeSkinSO>(WeaponForgeAddress, "무기 선택");
        _crucible    = await LoadOrNull<CrucibleSkinSO>(CrucibleAddress, "재련소");
        _shop        = await LoadOrNull<ShopSkinSO>(ShopAddress, "상점");
    }

    private static async UniTask<T> LoadOrNull<T>(string address, string label) where T : Object
    {
        try
        {
            var so = await Managers.AddressableManager.TryLoadAssetAsync<T>(address);
            if (so == null)
                Debug.Log($"[UISkin] '{address}' 미등록 — {label}는 색 폴백으로 동작(정상)");
            return so;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[UISkin] {label} 스킨 로드 예외: {e.Message}");
            return null;
        }
    }
}
