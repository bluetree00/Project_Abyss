using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BackEnd;
using UnityEditor.Compilation;

public class Login : LoginBase
{
    //ID 필드 색상 변경
    [SerializeField]
    private Image imageID;
    
    [SerializeField]
    private TMP_InputField inputFieldID; // ID 입력 필드

    [SerializeField]
    private Image imagePW; // 비밀번호 입력 필드 색상 변경

    [SerializeField]
    private TMP_InputField inputFieldPW; // 비밀번호 입력 필드


    [SerializeField]
    private Button btnLogin; // 로그인 버튼

    public void OnclickLogin()
    {

        ResetUI(imageID, imagePW); // UI 초기화

        // 필드 데이터 비어있는지 확인
        if (IsFieldDataEmpty(imageID, inputFieldID.text, "ID") || IsFieldDataEmpty(imagePW, inputFieldPW.text, "비밀번호"))
        {
            return; // 필드 데이터가 비어있으면 메서드 종료
        }

        btnLogin.interactable = false; // 로그인 버튼 비활성화

        StartCoroutine(nameof(LoginProcess)); // 로그인 프로세스 시작

        ResponseToLogin(inputFieldID.text, inputFieldPW.text); // 로그인 응답 처리
    }

    private void ResponseToLogin(string ID, string PW)
    {
        Backend.BMember.CustomLogin(ID, PW, callback =>
        {
            StopCoroutine(nameof(LoginProcess)); // 로그인 프로세스 중지

            if (callback.IsSuccess())
            {
                SetMessage($"{inputFieldID.text}님 환영합니다."); // 로그인 성공 메시지 설정
                
                SceneUtilitys.LoadScene(SceneNames.Lobby); // 로비 씬으로 이동

            }
            else
            {
                btnLogin.interactable = true; // 로그인 버튼 활성화

                string message = string.Empty; // 메시지 초기화

                switch( int.Parse(callback.GetStatusCode()))
                {
                    case 401: // 비밀번호 오류
                        message = callback.GetMessage().Contains("customID") ? "존재하지 않는 아이디 입니다." : "잘못된 비밀번호 입니다.";
                        break;
                    case 403: //유저 or 디바이스 차단
                        message = callback.GetMessage().Contains("customID") ? "차단된 아이디 입니다." : "차단된 디바이스 입니다.";
                        break;
                    case 410: //탈퇴 진행중
                        message = "탈퇴 진행중입니다.";
                        break;
                    default:
                        message = callback.GetMessage(); // 기본 메시지 설정
                        break;
                   
                }

                if(message.Contains("비밀번호"))
                {
                    GuideForIncorrectlyEnteredData(imagePW, message); // 비밀번호 오류 메시지 설정
                }
                else
                {
                    GuideForIncorrectlyEnteredData(imageID, message); // ID 오류 메시지 설정
                }
           
            }
        });
         
    }

    private IEnumerator LoginProcess()
    {
        float time = 0f; // 시간 초기화

        while(true)
        {
            time += Time.deltaTime; // 시간 증가

            SetMessage($"로그인 중...{time:F1}"); // 로그인 중 메시지 설정

            yield return null; // 다음 프레임까지 대기
        }
        // 로그인 성공 시 처리할 내용
        Debug.Log("로그인 성공!");
    }
}
