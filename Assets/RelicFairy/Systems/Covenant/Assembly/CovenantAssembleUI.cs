using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 조립 서약 팝업을 띄우고 조립된 서약 id를 반환하는 공용 헬퍼(CovenantChoiceUI 대응).
/// 획득 적용(TryAdd)은 호출 측에서 처리 — 던전/베이스캠프/챕터 시작 흐름에서 공통 사용.
/// </summary>
public static class CovenantAssembleUI
{
    /// <summary>
    /// 조립 팝업을 띄우고 결과 서약 id를 반환. forceSilver=true면 첫 서약(실버 고정).
    /// 팝업 로드 실패·취소 시 null.
    /// </summary>
    public static async UniTask<string> ChooseAsync(bool forceSilver, System.Random rng, CancellationToken ct)
    {
        UI_CovenantAssemble popup;
        try
        {
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_CovenantAssemble>();
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e)
        {
            Debug.LogWarning($"[CovenantAssembleUI] 팝업 로드 실패: {e.Message}");
            return null;
        }
        if (popup == null) return null;

        popup.Setup(forceSilver, rng);

        // ct를 대기에 실제로 연결한다 — 미연결이면 호출측 수명 토큰(GetCancellationTokenOnDestroy)이 무력해진다.
        try { return await popup.WaitForResultAsync().AttachExternalCancellation(ct); }
        catch (OperationCanceledException) { return null; }
    }
}
