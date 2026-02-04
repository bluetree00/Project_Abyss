using UnityEngine;

public sealed class UIRootBootstrapper : MonoBehaviour
{
    public static UIRootBootstrapper Instance { get; private set; }

    [Header("Canvases")]
    [SerializeField] private Canvas hudCanvas;
    [SerializeField] private Canvas popupCanvas;

    [Header("Roots")]
    [SerializeField] private Transform hudRoot;
    [SerializeField] private Transform popupRoot;

    [Header("Bootstrappers")]
    [SerializeField] private HudBootstrapper hudBootstrapper;

    public Canvas HudCanvas => hudCanvas;
    public Canvas PopupCanvas => popupCanvas;
    public Transform HudRoot => hudRoot;
    public Transform PopupRoot => popupRoot;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);

        if (hudBootstrapper == null)
            Debug.LogWarning("[UIRootBootstrapper] HudBootstrapper not found.");
    }

    // ✅ 런이 준비된 뒤 외부에서 호출
    public void BindHudToRun(GameRunManager run)
    {
        if (hudBootstrapper == null)
        {
            Debug.LogWarning("[UIRootBootstrapper] BindHudToRun ignored: hudBootstrapper is null");
            return;
        }

        hudBootstrapper.BindRun(run);
    }
}
