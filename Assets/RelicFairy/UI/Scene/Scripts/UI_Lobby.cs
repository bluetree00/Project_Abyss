using UnityEngine;
using UnityEngine.UI;

public class UI_Lobby : UI_Scene
{
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("서브 패널")]
    [SerializeField] private UI_SaveSlotPanel saveSlotPanel;

    // ─────────────────────────────────────────────────────────
    // Enums
    // ─────────────────────────────────────────────────────────

    private enum Buttons
    {
        Btn_StartRun,
        Btn_Settings,
        Btn_Exit,
    }

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();

        Bind<Button>(typeof(Buttons));

        GetButton((int)Buttons.Btn_StartRun).onClick.AddListener(OnClickStartRun);
        GetButton((int)Buttons.Btn_Settings).onClick.AddListener(OnClickSettings);
        GetButton((int)Buttons.Btn_Exit).onClick.AddListener(OnClickExit);

        if (saveSlotPanel != null)
        {
            saveSlotPanel.Init();
            saveSlotPanel.Close();
        }
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStartRun()
    {
        if (saveSlotPanel != null)
            saveSlotPanel.Open();
        else
            AppBootstrapper.Instance?.RequestStartRun();
    }

    private void OnClickSettings()
    {
        Managers.UI.ShowPopupUI<UI_Pause>();
    }

    private void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
