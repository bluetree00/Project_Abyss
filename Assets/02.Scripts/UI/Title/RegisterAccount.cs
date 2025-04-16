using UnityEngine;
using UnityEngine.UI;
using BackEnd;
using TMPro;
using UnityEditor.VersionControl;

public class RegisterAccount : LoginBase
{
    [SerializeField]
    private Image imageID; // ID 입력 필드 색상 변경

    [SerializeField]
    private TMP_InputField inputFieldID; // ID 입력 필드

    [SerializeField]
    private Image imagePW; // 비밀번호 입력 필드 색상 변경

    [SerializeField]
    private TMP_InputField inputFieldPW; // 비밀번호 입력 필드

    [SerializeField]
    private Image imageConfirmPW; // 이메일 입력 필드 색상 변경

    [SerializeField]
    private TMP_InputField inputFieldConfirmPW; // 비밀번호 확인 필드

    [SerializeField]
    private Image imageEmail; // 이메일 입력 필드 색상 변경

    [SerializeField]
    private TMP_InputField inputFieldEmail; // 이메일 입력 필드

    [SerializeField]
    private Button btnRegisterAccount; // 회원가입 버튼

    /// <summary>
    /// 회원가입 버튼 클릭 이벤트
    /// </summary>
    public void OnClickRegisterAccount()
    {
        //매개변수로 입력한 필드의 색상 변경과 초기화
        ResetUI(imageID, imagePW, imageConfirmPW, imageEmail); // UI 초기화

        //필드의 값이 비어있는지 체크
        if(IsFieldDataEmpty(imageID, inputFieldID.text, "ID") || IsFieldDataEmpty(imagePW, inputFieldPW.text, "비밀번호") ||
            IsFieldDataEmpty(imageConfirmPW, inputFieldPW.text, "비밀번호 확인") || IsFieldDataEmpty(imageEmail, inputFieldEmail.text, "이메일"))
        {
            return; // 필드 데이터가 비어있으면 메서드 종료
        }

        //비밀번호와 비밀번호 확인이 다를때
        if(!inputFieldPW.text.Equals(inputFieldConfirmPW.text))
        {
            GuideForIncorrectlyEnteredData(imageConfirmPW, "비밀번호가 일치하지 않습니다."); // 비밀번호 불일치 메시지 설정
            return; // 메서드 종료
        }

        //메일 형식 검사
        if(!inputFieldEmail.text.Contains("@"))
        {
            GuideForIncorrectlyEnteredData(imageEmail, "이메일 형식이 올바르지 않습니다."); // 이메일 형식 불일치 메시지 설정
            return; // 메서드 종료
        }

        //계정 생성 버튼 상호작용 비활성화
        btnRegisterAccount.interactable = false; // 회원가입 버튼 비활성화
        SetMessage("회원가입 중입니다."); // 메시지 설정

        //뒤끝 서버 계정 연결 시도
        CustomSignUp();
       
    }

    private void CustomSignUp()
    {
        Backend.BMember.CustomSignUp(inputFieldID.text, inputFieldPW.text, callback =>
        {
            //계정 생성 버튼 활성화
            btnRegisterAccount.interactable = true; // 회원가입 버튼 활성화

            if( callback.IsSuccess())
            {
                Backend.BMember.UpdateCustomEmail(inputFieldEmail.text, callback =>
                {
                    if (callback.IsSuccess())
                    {
                        SetMessage($"회원가입이 완료되었습니다.{inputFieldID.text}님 환영 합니다."); // 회원가입 성공 메시지 설정
                        Debug.Log("회원가입 성공: " + callback.GetMessage()); // 회원가입 성공 메시지 출력
                    }
                 
                });

            }
            else
            {
                string message = string.Empty; // 메시지 초기화

                switch (int.Parse(callback.GetStatusCode()))
                {
                    case 409: // 이미 존재하는 ID
                        message = "이미 존재하는 ID입니다."; // ID 중복 메시지 설정
                        break;
                    case 403: // 유저 or 디바이스 차단
                    case 401: // 프로젝트가 점검 상태일떄
                    case 400: // 디바이스 정보가 null일떄
                    default:
                        message = callback.GetMessage();
                    
                        break;
                        
                }

                if(message.Contains("아이디"))
                {
                    GuideForIncorrectlyEnteredData(imageID, message); // ID 오류 메시지 설정
                }
                else
                {
                    SetMessage(message); // 메시지 설정

                }
            }

        });

    }
}
