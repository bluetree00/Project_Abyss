using UnityEngine;

public sealed class UIRootBootstrapper : MonoBehaviour
{
    public static UIRootBootstrapper Instance { get; private set; }

    [Header("Canvases")]
    [SerializeField] private Canvas hudCanvas;
    [SerializeField] private Canvas popupCanvas;

    [Header("Roots")]
    [SerializeField] private Transform hudRoot;   // @HUD
    [SerializeField] private Transform popupRoot; // @Popup

    [Header("Bootstrappers")]
    [SerializeField] private HudBootstrapper hudBootstrapper;

    // 예시: 프로젝트의 Managers 접근 방식에 맞게 교체
    [SerializeField] private CharacterDataManager characterDataManager;

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

        // 필요하면 여기서 null 체크들
        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);

        // HUD 초기화(의존성 주입)
        if (hudBootstrapper != null)
            hudBootstrapper.Bind(characterDataManager);
        else
            Debug.LogWarning("[UIRootBootstrapper] HudBootstrapper not found.");
    }
}
