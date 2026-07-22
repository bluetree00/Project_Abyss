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
    {
        // Awake에서 참조를 못 잡았을 수 있다(HUD가 나중에 활성/생성되는 경우).
        // 여기서 다시 찾지 않으면 ?. 가 억제 요청을 통째로 삼켜, 전투 전인데 HUD가 그대로 뜬다.
        EnsureHudBootstrapper();
        if (hudBootstrapper == null)
        {
            Debug.LogWarning("[UIRoot] HudBootstrapper 없음 — HUD 억제 요청이 무시됐다.");
            return;
        }
        hudBootstrapper.SetStartRoomSuppressed(suppress);
    }

    /// <summary>HUD를 페이드로 표시(전투 진입 연출).</summary>
    public Cysharp.Threading.Tasks.UniTask FadeInHudAsync(float duration)
    {
        EnsureHudBootstrapper();
        return hudBootstrapper != null ? hudBootstrapper.FadeInStartRoomAsync(duration)
                                       : Cysharp.Threading.Tasks.UniTask.CompletedTask;
    }

    private void EnsureHudBootstrapper()
    {
        if (hudBootstrapper == null)
            hudBootstrapper = GetComponentInChildren<HudBootstrapper>(true);
    }

    public MinimapView GetMinimapView() => hudBootstrapper?.MinimapView;

    public void UnbindHud()
    {
        hudBootstrapper?.Unbind();
        _boundRun = null;
    }

    // 룬판 토글은 PlayerController.TogglePuzzleGrid(PuzzleToggle = Tab)가 단독으로 담당한다.
    // 여기서 KeyCode.Tab을 폴링하면 같은 프레임에 두 번 토글되어 상쇄된다(= 런 중 룬판이 안 열림).

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }
}
