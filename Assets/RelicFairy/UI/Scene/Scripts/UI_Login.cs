using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BackEnd;
using Cysharp.Threading.Tasks;

public class UI_Login : UI_Scene
{
    [SerializeField] private Image imageID;
    [SerializeField] private TMP_InputField inputFieldID;
    [SerializeField] private Image imagePW;
    [SerializeField] private TMP_InputField inputFieldPW;
    [SerializeField] private Button btnLogin;
    [SerializeField] private TextMeshProUGUI textMessage;

    private void Awake()
    {
        btnLogin.onClick.AddListener(OnClickLogin);
    }

    public override void Init()
    {
        base.Init();
        ResetUI();
    }

    private async void OnClickLogin()
    {
        ResetUI();

        if (IsFieldDataEmpty(imageID, inputFieldID.text, "ID") ||
            IsFieldDataEmpty(imagePW, inputFieldPW.text, "비밀번호"))
            return;

        btnLogin.interactable = false;
        SetMessage("로그인 시도 중...");

        bool loginSuccess = await TryLoginAsync(inputFieldID.text, inputFieldPW.text);

        if (loginSuccess)
        {
            SetMessage("로그인 성공! 데이터 불러오는 중...");
            await UniTask.WhenAll(
                Managers.MonsterData.InitializeAsync(),
                BackendGameData.Instance.LoadAsync()
            );
            SetMessage("데이터 불러오기 완료. 로비로 이동합니다.");
            AppBootstrapper.Instance.RequestLoad(Define.Scene.Lobby);
        }
        else
        {
            btnLogin.interactable = true;
        }
    }

    private UniTask<bool> TryLoginAsync(string id, string pw)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        Backend.BMember.CustomLogin(id, pw, callback =>
        {
            if (callback.IsSuccess())
            {
                tcs.TrySetResult(true);
            }
            else
            {
                int.TryParse(callback.GetStatusCode(), out int statusCode);
                HandleLoginError(statusCode, callback.GetMessage());
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    private void HandleLoginError(int statusCode, string message)
    {
        btnLogin.interactable = true;

        switch (statusCode)
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
        imageID.color = Color.white;
        imagePW.color = Color.white;
        SetMessage(string.Empty);
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
        image.color = Color.red;
        SetMessage(message);
    }

    private void SetMessage(string message)
    {
        textMessage.text = message;
    }
}
