using UnityEngine;

public sealed class UIRootBootstrapper : MonoBehaviour
{
    public static UIRootBootstrapper Instance { get; private set; }

    [Header("Roots")]
    [SerializeField] private Transform hudRoot;     // Canvas_HUD/@HUD
    [SerializeField] private Transform popupRoot;   // Canvas_Popup/@Popup
    [SerializeField] private Transform sceneRoot;   // Canvas_Scene/@Scene
    [SerializeField] private Transform overlayRoot; // Canvas_Overlay/@Overlay
    [SerializeField] private Transform worldRoot;   // WorldSpaceRoot/@WorldUI

    public Transform HudRoot => hudRoot;
    public Transform PopupRoot => popupRoot;
    public Transform SceneRoot => sceneRoot;
    public Transform OverlayRoot => overlayRoot;
    public Transform WorldRoot => worldRoot;

    [Header("HUD Bootstrapper (optional)")]
    [SerializeField] private HudBootstrapper hudBootstrapper;

    // ✅ 같은 Run에 중복 바인딩 방지용
    private GameRunSession _boundRun;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);
    }

    public void BindHudToRun(GameRunSession run)
    {
        if (run == null)
        {
            Debug.LogWarning("[UIRoot] BindHudToRun ignored: run is null.");
            return;
        }

        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);

        if (hudBootstrapper == null)
        {
            Debug.LogWarning("[UIRoot] HudBootstrapper not found under UIRoot.");
            return;
        }

        _boundRun = run;
        hudBootstrapper.BindRun(run);
    }

    /// <summary>스타트 방 구간(대화·위스프) 동안 HUD를 숨긴다. false로 복원하면 즉시 표시.</summary>
    public void SetHudStartRoomSuppressed(bool suppress)
        => hudBootstrapper?.SetStartRoomSuppressed(suppress);

    public MinimapView GetMinimapView() => hudBootstrapper?.MinimapView;

    public void UnbindHud()
    {
        hudBootstrapper?.Unbind();
        _boundRun = null;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Tab)) return;
        var panel = UI_GridPanel.Instance;
        if (panel == null) return;
        if (panel.IsOpen) panel.Close();
        else              panel.Open();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }
}
