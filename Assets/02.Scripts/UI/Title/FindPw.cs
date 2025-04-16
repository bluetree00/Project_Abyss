using UnityEngine;
using BackEnd;
using UnityEngine.UI;
using TMPro;

public class FindPw : LoginBase
{
    [SerializeField]
    private Image imageID; // 이메일 입력 필드 이미지
    [SerializeField]
    private TMP_InputField inputFieldID; // 이메일 입력 필드
    [SerializeField]
    private Image imageEmail; // 이메일 입력 필드 이미지
    [SerializeField]
    private TMP_InputField inputFieldEmail; // 이메일 입력 필드

    [SerializeField]
    private Button btnFindPw; // 비밀번호 찾기 버튼
    
    public void OnClickFindPW()
    {
        ResetUI(imageID, imageEmail); // UI 초기화

        //필드값이 비어있는지 체크
        if (IsFieldDataEmpty(imageID, inputFieldID.text, "아이디") || IsFieldDataEmpty(imageEmail, inputFieldEmail.text, "이메일"))
            return;

        //메일 형식 검사
        if (!inputFieldEmail.text.Contains("@"))
        {
            GuideForIncorrectlyEnteredData(imageEmail, "이메일 형식이 올바르지 않습니다.");
            return;
        }

        //비밀번호 찾기 버튼 비활성화
        btnFindPw.interactable = false;
        SetMessage("메일 발송중 입니다..");

        FindCustomPW(); // 비밀번호 찾기 시도
    }

    private void FindCustomPW()
    {
        //비밀번호를 초기화하고, 초기화된 비밀번호 정보를 이메일로 발송
        Backend.BMember.ResetPassword(inputFieldID.text, inputFieldEmail.text, Callback =>
        {
            btnFindPw.interactable = true; // 비밀번호 찾기 버튼 활성화

            if (Callback.IsSuccess())
            {
                SetMessage("메일이 발송되었습니다.");
            }
            else
            {
                string message = string.Empty;

                switch (int.Parse(Callback.GetStatusCode()))
                {
                    case 404:
                        message = "해당 이메일로 가입된 계정이 없습니다.";
                        break;
                    case 429:
                        message = "24시간 이내에 5회이상 아이디 비밀번호 찾기를 시도했습니다.";
                        break;
                    default:
                        message = Callback.GetMessage();
                        break;
                }

                if(message.Contains("이메일"))
                {
                    GuideForIncorrectlyEnteredData(imageEmail, message); // 이메일 오류 메시지 설정
                }
                else
                {
                    SetMessage(message); // 오류 메시지 설정
                }
            }
        });

    }
}
