using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스 봉인(2페이지) 해금 — 메타 영구 상태 접근 창구.
///
/// 각 보스는 '봉인'된 채 시작한다(페이즈2 잠김). 초회 클리어 시 봉인이 해제되며,
/// 이후 조우부터 페이즈2가 열린다. 해금 상태는 각성과 같은 메타 영구 저장(UserGameData.sealBrokenBossIds).
///
/// 사용:
///   · 보스: 페이즈2 게이트 = <see cref="IsSealBroken"/>(bossId)
///   · 초회 클리어(봉인 상태 격파) 시: <see cref="BreakSeal"/>(bossId)
///
/// bossId는 보스마다 고유한 키(예: "lich"). 새 보스는 자기 키로 이 서비스를 호출하면 된다.
/// </summary>
public static class BossSealService
{
    /// <summary>해당 보스의 봉인이 해제됐는지(= 페이즈2 해금). 백엔드 데이터 미로드 시 false(봉인).</summary>
    public static bool IsSealBroken(string bossId)
        => BackendGameData.Instance?.Data?.IsBossSealBroken(bossId) ?? false;

    /// <summary>
    /// 보스 봉인을 해제한다(초회 클리어). 이미 해제됐으면 무시.
    /// 메타 저장은 fire-and-forget — 클리어 연출 흐름을 막지 않는다.
    /// </summary>
    public static void BreakSeal(string bossId)
    {
        var backend = BackendGameData.Instance;
        var data    = backend?.Data;
        if (data == null)
        {
            Debug.LogWarning($"[BossSealService] 백엔드 데이터 없음 — '{bossId}' 봉인 해제 저장 실패");
            return;
        }

        if (data.BreakBossSeal(bossId))
        {
            Debug.Log($"[BossSealService] 봉인 해제: '{bossId}' — 이후 조우부터 페이즈2 해금");
            backend.SaveAsync().Forget();
        }
    }
}
