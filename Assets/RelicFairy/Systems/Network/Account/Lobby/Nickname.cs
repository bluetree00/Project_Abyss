using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BackEnd;


public class Nickname : LoginBase
{
    [System.Serializable]
    public class NicknameEvent : UnityEngine.Events.UnityEvent {}
    public NicknameEvent         onNicknameEvent = new NicknameEvent();

    [SerializeField]
    private Image              imageNickname; // 닉네임 입력 필드 이미지
    [SerializeField]
    private TMP_InputField     inputFieldNickname; // 닉네임 입력 필드

    [SerializeField]
    private Button             btnUpdateNickname; // 닉네임 변경 버튼

    private void Onable()
    {
        ResetUI(imageNickname); // UI 초기화        
        SetMessage("닉네임을 입력하세요"); // 메시지 설정
    }

    public void OnClickUpdateNickname()
    {
        // UI 초기화
        ResetUI(imageNickname);

        // 닉네임 필드 데이터 확인
        if (IsFieldDataEmpty(imageNickname, inputFieldNickname.text, "닉네임"))
            return;

        // 닉네임 변경 버튼 비활성화
        btnUpdateNickname.interactable = false;
        SetMessage("닉네임 변경중 입니다..");

        // 뒤끝서버 닉네임 변경 시도
        UpdateNickname();
    }

    private void UpdateNickname()
    {
        // 뒤끝서버 닉네임 변경
        Backend.BMember.UpdateNickname(inputFieldNickname.text, Callback =>
        {
            btnUpdateNickname.interactable = true; // 닉네임 변경 버튼 활성화

            if (Callback.IsSuccess())
            {
                SetMessage("닉네임이 변경되었습니다.");
                onNicknameEvent.Invoke(); // 닉네임 변경 이벤트 호출
            }
            else
            {
                string message = string.Empty;

                switch (int.Parse(Callback.GetStatusCode()))
                {
                    case 400:
                        message = "닉네임은 2자 이상 20자 이하로 입력해주세요.";
                        break;
                    case 409:
                        message = "이미 사용중인 닉네임입니다.";
                        break;
                    default:
                        message = Callback.GetMessage();
                        break;
                }

                GuideForIncorrectlyEnteredData(imageNickname, message);
            }
        });
    }
}
