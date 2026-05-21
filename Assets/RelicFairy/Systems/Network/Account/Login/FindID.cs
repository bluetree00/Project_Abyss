using UnityEngine;
using BackEnd;
using UnityEngine.UI;
using TMPro;
using System;
public class FindID : LoginBase
{
    [SerializeField]
    private Image imageEmail; // 이메일 입력 필드 이미지

    [SerializeField]
    private TMP_InputField inputFieldEmail; // 이메일 입력 필드

    [SerializeField]
    private Button btnFindID; // ID 찾기 버튼

    public void OnclickFindID()
    {
        // UI 초기화
        ResetUI(imageEmail);

        // 이메일 필드 데이터 확인
        if (IsFieldDataEmpty(imageEmail, inputFieldEmail.text, "이메일"))
            return;

        if(!inputFieldEmail.text.Contains("@"))
        {
            GuideForIncorrectlyEnteredData(imageEmail, "이메일 형식이 올바르지 않습니다.");
            return;
        }

        //아이디 찾기 버튼 비활성화
        btnFindID.interactable = false;
        SetMessage("메일 발송중 입니다..");

        //뒤끝서버 아이디 찾기 시도
        FindCustomerID();
    }

    private void FindCustomerID()
    {
        //뒤끝서버 아이디 찾기
        Backend.BMember.FindCustomID(inputFieldEmail.text, Callback =>
        {

            btnFindID.interactable = false; //아이디 찾기 버튼 비활성화

            if (Callback.IsSuccess())
            {
                SetMessage("메일이 발송되었습니다.");
            }
            else
            {
                string message = string.Empty;

                switch(int.Parse(Callback.GetStatusCode()))
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
                    GuideForIncorrectlyEnteredData(imageEmail, message);
                }
                else
                {
                    SetMessage(message);
                }
            }
        });
    }
}
