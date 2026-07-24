using Cysharp.Threading.Tasks;
using UnityEngine.UI;

/// <summary>
/// 방 클리어 보상 수령 확인 팝업.
/// 아이템 정보는 표시하지 않으며, "수령" 버튼 클릭 시 대기 중인 Task를 완료시킨다.
/// Addressable 프리팹 키: UI/Popup/UI_ClearReward
///
/// 프리팹 계층 구조 (Bind 이름과 정확히 일치해야 함):
///   UI_ClearReward (Root)
///   └── Panel
///         └── ConfirmBtn  (Button)
/// </summary>
public class UI_ClearReward : UI_Popup
{
    private enum Buttons { ConfirmBtn }

    private UniTaskCompletionSource _confirmTcs;

    public override void Init()
    {
        base.Init();
        Bind<Button>(typeof(Buttons));
        GetButton((int)Buttons.ConfirmBtn).onClick.AddListener(OnConfirmClicked);
    }

    /// <summary>수령 버튼 클릭 또는 팝업 파괴 시 완료되는 UniTask.</summary>
    public UniTask WaitForConfirmAsync()
    {
        _confirmTcs = new UniTaskCompletionSource();
        return _confirmTcs.Task;
    }

    private void OnDestroy() => _confirmTcs?.TrySetCanceled();

    private void OnConfirmClicked()
    {
        // ⚠️ 닫기가 먼저다. TrySetResult의 대기자(ClearRewardTrigger)가 <b>동기로 이어져</b>
        // 곧바로 다음 팝업(룬 선택)을 push하는데, ClosePopupUI는 자기가 스택 최상단일 때만 닫힌다.
        // 순서가 반대면 닫기가 조용히 실패해 이 팝업이 스택에 남고, 위 팝업을 닫는 순간 다시 드러난다.
        ClosePopupUI();
        _confirmTcs?.TrySetResult();
    }
}
