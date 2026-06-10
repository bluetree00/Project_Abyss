using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 서약 3지선다 선택 팝업을 띄우고 선택된 서약 id를 반환하는 공용 헬퍼.
/// 런 핸들러(CovenantHandler)·PlayerLoadout 등 커밋 경로에 의존하지 않는 순수 UI 상호작용이라
/// 던전(WorldCovenantPickup)·베이스캠프(CovenantPickup)·이벤트방에서 공통으로 사용한다.
/// 획득 적용(예약 또는 TryAdd)은 호출 측에서 처리한다.
/// </summary>
public static class CovenantChoiceUI
{
    /// <summary>
    /// 주어진 서약 id들로 선택 팝업을 띄우고, 선택된 서약 id를 반환한다.
    /// 후보 없음·팝업 로드 실패·취소 시 null.
    /// </summary>
    public static async UniTask<string> ChooseAsync(string[] ids, CancellationToken ct)
    {
        if (ids == null || ids.Length == 0) return null;

        var covenants = ids
            .Select(CovenantFactory.Create)
            .Where(c => c != null)
            .ToArray();
        if (covenants.Length == 0) return null;

        UI_CovenantChoice popup;
        try
        {
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_CovenantChoice>();
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e)
        {
            Debug.LogWarning($"[CovenantChoiceUI] 팝업 로드 실패: {e.Message}");
            return null;
        }
        if (popup == null) return null;

        popup.Setup(covenants);

        int chosen;
        try { chosen = await popup.WaitForChoiceAsync(); }
        catch (OperationCanceledException) { return null; }

        return (chosen >= 0 && chosen < covenants.Length) ? covenants[chosen].CovenantId : null;
    }
}
