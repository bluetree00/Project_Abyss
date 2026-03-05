using UnityEngine;
using UnityEngine.UI;

public class UI_Lobby : UI_Scene
{
    enum Buttons
    {
        Btn_StartRun,
        Btn_Continue,
        Btn_Settings,
        Btn_Exit,
    }

    public override void Init()
    {
        base.Init();

        Bind<Button>(typeof(Buttons));

        GetButton((int)Buttons.Btn_StartRun).onClick.AddListener(OnClickStartRun);
        GetButton((int)Buttons.Btn_Continue).onClick.AddListener(OnClickContinue);
        GetButton((int)Buttons.Btn_Settings).onClick.AddListener(OnClickSettings);
        GetButton((int)Buttons.Btn_Exit).onClick.AddListener(OnClickExit);
    }

    void OnClickStartRun()
    {
        AppBootstrapper.Instance.RequestStartRun();
    }

    void OnClickContinue()
    {
        Managers.UI.ShowMenuUI<UI_Inven>();
    }

    void OnClickSettings()
    {
        Managers.UI.ShowPopupUI<UI_Pause>();
    }

    void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
