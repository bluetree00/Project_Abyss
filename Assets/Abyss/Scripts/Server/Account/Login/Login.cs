using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BackEnd;
using Cysharp.Threading.Tasks;

public class Login : MonoBehaviour
{
    [SerializeField] private Image imageID;
    [SerializeField] private TMP_InputField inputFieldID;
    [SerializeField] private Image imagePW;
    [SerializeField] private TMP_InputField inputFieldPW;
    [SerializeField] private Button btnLogin;

    private void Awake()
    {
        btnLogin.onClick.AddListener(OnClickLogin);
    }

    private async void OnClickLogin()
    {
        ResetUI();

        if (IsFieldDataEmpty(imageID, inputFieldID.text, "ID") || 
            IsFieldDataEmpty(imagePW, inputFieldPW.text, "비밀번호"))
        {
            return;
        }

        btnLogin.interactable = false;
        SetMessage("로그인 시도 중...");

        bool loginSuccess = await TryLoginAsync(inputFieldID.text, inputFieldPW.text);

        if (loginSuccess)
        {
            SetMessage("로그인 성공! 몬스터 데이터 불러오는 중...");

            await Managers.MonsterData.InitializeAsync();

            SetMessage("데이터 불러오기 완료. 로비로 이동합니다.");
            AppBootstrapper.Instance.RequestLoad(Define.Scene.Lobby);
        }
        else
        {
            btnLogin.interactable = true;
            SetMessage("로그인 실패");
            // 필요 시 에러 메시지 UI 업데이트 (아래 함수 참고)
        }
    }

    private UniTask<bool> TryLoginAsync(string ID, string PW)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        Backend.BMember.CustomLogin(ID, PW, callback =>
        {
            if (callback.IsSuccess())
            {
                tcs.TrySetResult(true);
            }
            else
            {
                int statusCode = 0;
                int.TryParse(callback.GetStatusCode(), out statusCode);

                HandleLoginError(statusCode, callback.GetMessage());
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }


    private void HandleLoginError(int statusCode, string message)
    {
        btnLogin.interactable = true;

        switch(statusCode)
        {
            case 401:
                if (message.Contains("customID"))
                    GuideForIncorrectlyEnteredData(imageID, "존재하지 않는 아이디 입니다.");
                else
                    GuideForIncorrectlyEnteredData(imagePW, "잘못된 비밀번호 입니다.");
                break;
            case 403:
                if (message.Contains("customID"))
                    GuideForIncorrectlyEnteredData(imageID, "차단된 아이디 입니다.");
                else
                    GuideForIncorrectlyEnteredData(imagePW, "차단된 디바이스 입니다.");
                break;
            case 410:
                SetMessage("탈퇴 진행중입니다.");
                break;
            default:
                SetMessage(message);
                break;
        }
    }

    private void ResetUI()
    {
        // 색상 초기화 등 필요한 UI 초기화 처리
        ResetImageColor(imageID);
        ResetImageColor(imagePW);
        SetMessage("");
    }

    private bool IsFieldDataEmpty(Image image, string text, string fieldName)
    {
        if (string.IsNullOrEmpty(text))
        {
            GuideForIncorrectlyEnteredData(image, $"{fieldName}을(를) 입력하세요.");
            return true;
        }
        return false;
    }

    private void GuideForIncorrectlyEnteredData(Image image, string message)
    {
        // 이미지 색상 변경, 메시지 표시 등 구현
        image.color = Color.red;
        SetMessage(message);
    }

    private void ResetImageColor(Image image)
    {
        image.color = Color.white;
    }

    private void SetMessage(string message)
    {
        // 메시지 출력용 구현 (예: UI 텍스트 업데이트)
        Debug.Log(message);
    }
}
