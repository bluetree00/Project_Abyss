using UnityEngine;

public sealed class UIRootBootstrapper : MonoBehaviour
{
    public static UIRootBootstrapper Instance { get; private set; }

    [Header("Roots")]
    [SerializeField] private Transform hudRoot;     // Canvas_HUD/@HUD
    [SerializeField] private Transform popupRoot;   // Canvas_Popup/@Popup
    [SerializeField] private Transform menuRoot;    // Canvas_Menu/@Menu
    [SerializeField] private Transform overlayRoot; // Canvas_Overlay/@Overlay
    [SerializeField] private Transform worldRoot;   // WorldSpaceRoot/@WorldUI

    public Transform HudRoot => hudRoot;
    public Transform PopupRoot => popupRoot;
    public Transform MenuRoot => menuRoot;
    public Transform OverlayRoot => overlayRoot;
    public Transform WorldRoot => worldRoot;

    [Header("HUD Bootstrapper (optional)")]
    [SerializeField] private HudBootstrapper hudBootstrapper;

    // ✅ 같은 Run에 중복 바인딩 방지용
    private GameRunManager _boundRun;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);
    }

    public void BindHudToRun(GameRunManager run)
    {
        if (run == null)
        {
            Debug.LogWarning("[UIRoot] BindHudToRun ignored: run is null.");
            return;
        }

        if (hudBootstrapper == null)
        {
            Debug.LogWarning("[UIRoot] HudBootstrapper not found under UIRoot.");
            return;
        }

        if (ReferenceEquals(_boundRun, run))
            return;

        _boundRun = run;
        hudBootstrapper.BindRun(run);
    }

    public void UnbindHud()
    {
        hudBootstrapper?.Unbind();
        _boundRun = null;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }
}