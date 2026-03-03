//============================================================
// HudPresenter.cs
// - Mode/Section 토글 + CanvasGroup Fade + PlayerState 이벤트 구독
//============================================================
using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;

    [Header("Visibility (CanvasGroup)")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("HUD Mode")]
    [SerializeField] private HUDIds.Mode startMode = HUDIds.Mode.Explore;

    private PlayerRunState _state;
    private UIHudDataProvider _provider;

    private int _fadeToken = 0;
    private HUDIds.Mode _currentMode = HUDIds.Mode.None;

    private void Awake()
    {
        if (view == null)
            view = GetComponentInChildren<HudView>(true);

        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);
    }

    private void Start()
    {
        SetMode(startMode);
    }

    public void Construct(GameRunSession run, UIHudDataProvider provider)
    {
        Dispose();

        _provider = provider;
        _state = run?.PlayerState;

        if (view == null)
        {
            Debug.LogError("[HudPresenter] view is null.");
            return;
        }

        if (_state == null)
        {
            Debug.LogWarning("[HudPresenter] Construct ignored: PlayerState is null.");
            return;
        }

        // 초기 스냅샷
        if (_provider != null && _provider.TryGet(out var data))
        {
            view.SetHp(data.Hp, data.MaxHp);
            view.SetGold(data.TempGold);
        }
        else
        {
            view.SetHp(_state.Hp, _state.MaxHp);
            view.SetGold(_state.TempGold);
        }

        // 이벤트 구독
        _state.OnHpChanged += HandleHpChanged;
        _state.OnGoldChanged += HandleGoldChanged;
    }

    public void Dispose()
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state = null;
        }
        _provider = null;
    }

    private void OnDisable() => Dispose();
    private void OnDestroy() => Dispose();

    private void HandleHpChanged(int hp, int maxHp) => view?.SetHp(hp, maxHp);
    private void HandleGoldChanged(int gold) => view?.SetGold(gold);

    public void SetMode(HUDIds.Mode mode)
    {
        if (view == null) return;
        if (_currentMode == mode) return;

        _currentMode = mode;
        view.SetSections(ResolveSections(mode));
    }

    private static HUDIds.Section ResolveSections(HUDIds.Mode mode)
    {
        switch (mode)
        {
            case HUDIds.Mode.Explore:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.ExplorePanel |
                       HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Combat:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.CombatPanel |
                       HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Boss:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.CombatPanel |
                       HUDIds.Section.BossPanel |
                       HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Cutscene:
                return HUDIds.Section.SystemNotices;

            case HUDIds.Mode.Spectate:
                return HUDIds.Section.TopBar |
                       HUDIds.Section.SystemNotices;

            default:
                return HUDIds.Section.None;
        }
    }

    public void SetVisible(bool visible, bool immediate)
    {
        if (canvasGroup == null)
        {
            gameObject.SetActive(visible);
            return;
        }

        if (immediate)
        {
            _fadeToken++;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            return;
        }

        FadeTo(visible ? 1f : 0f).Forget();
    }

    private async UniTaskVoid FadeTo(float targetAlpha)
    {
        if (canvasGroup == null) return;

        int token = ++_fadeToken;

        float startAlpha = canvasGroup.alpha;
        float t = 0f;

        bool willBeVisible = targetAlpha > 0.5f;
        canvasGroup.interactable = willBeVisible;
        canvasGroup.blocksRaycasts = willBeVisible;

        float dur = Mathf.Max(0.0001f, fadeDuration);

        while (t < 1f)
        {
            if (token != _fadeToken) return;

            t += Time.unscaledDeltaTime / dur;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            await UniTask.Yield();
        }

        canvasGroup.alpha = targetAlpha;
    }
}
