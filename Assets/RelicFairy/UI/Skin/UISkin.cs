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
    private const string RefineryAddress = "UI/RefinerySkin";

    private static RefinerySkinSO _refinery;
    private static bool _refineryTried;

    /// <summary>정제소 스킨. 미로드/미등록이면 null → 코드로 그린 색 박스가 그대로 보인다.</summary>
    public static RefinerySkinSO Refinery => _refinery;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _refinery = null;
        _refineryTried = false;
    }

    /// <summary>앱 부트에서 1회 호출. 이미 시도했으면 즉시 반환.</summary>
    public static async UniTask PreloadAsync()
    {
        if (_refineryTried) return;
        _refineryTried = true;

        try
        {
            _refinery = await Managers.AddressableManager.TryLoadAssetAsync<RefinerySkinSO>(RefineryAddress);
            if (_refinery == null)
                Debug.Log($"[UISkin] '{RefineryAddress}' 미등록 — 정제소는 색 폴백으로 동작(정상)");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[UISkin] 정제소 스킨 로드 예외: {e.Message}");
        }
    }
}
