using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;

public class StagePointUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Graph Config (Inspector 입력)")]
    [SerializeField] private int pointId;
    [SerializeField] private StageCategory stageCategory;
    [SerializeField] private List<int> nextPointIds = new();

    [Header("Layer Meta (동적 생성 시 Generator가 주입)")]
    [SerializeField] private int layerIndex = -1;
    [SerializeField] private int indexInLayer = -1;

    [Header("Normal 노드 룸 카테고리(Inspector에서 결정)")]
    [SerializeField] private NormalRoomCategory normalRoomCategory = NormalRoomCategory.Battle;

    [Header("아이콘")]
    [SerializeField] private StageNodeIconMap iconMap;

    [Header("필터(선택) - -1이면 미사용")]
    [SerializeField] private int minDifficulty = -1;
    [SerializeField] private int maxDifficulty = -1;
    [SerializeField] private List<string> requiredTags = new();

    [Header("Resolved (Runtime - Inspector 확인용)")]
    [SerializeField] private string resolvedRoomId;
    [SerializeField] private string resolvedRoomName;
    [SerializeField] private string resolvedRoomCategory;
    [SerializeField] private int resolvedDifficulty;
    [SerializeField] private string resolvedPrefab;
    [SerializeField] private string resolvedTags;

    // ── Properties (외부 읽기용) ──
    public int PointId => pointId;
    public IReadOnlyList<int> NextPointIds => nextPointIds;
    public StageCategory StageCategoryValue => stageCategory;
    public NormalRoomCategory NormalRoomCategoryValue => normalRoomCategory;
    public int LayerIndex => layerIndex;
    public int IndexInLayer => indexInLayer;

    private StagePointManager _mgr;
    private RoomManager _roomMgr;
    private Image _image;
    private CanvasGroup _canvasGroup;

    // ── Glow ──
    private Outline _glowOutline;
    private bool _glowActive;
    private float _glowPhase;

    private void Awake()
    {
        _image = GetComponent<Image>();

        // 아이콘 로드 전: 완전 투명 + 클릭 차단
        if (_image != null)
            _image.color = Color.clear;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.blocksRaycasts = false;
    }

    /// <summary>동적 생성 시 초기 설정. iconMapRef가 null이면 기존 값 유지.</summary>
    public void Init(int id, StageCategory category, NormalRoomCategory normal, StageNodeIconMap iconMapRef = null)
    {
        pointId = id;
        stageCategory = category;
        normalRoomCategory = normal;
        nextPointIds = new List<int>();
        if (iconMapRef != null) iconMap = iconMapRef;
    }

    /// <summary>Generator가 만든 레이어 메타데이터 주입. Layout이 X/Y 배치 시 사용.</summary>
    public void SetLayerMeta(int layerIdx, int indexInLyr)
    {
        layerIndex = layerIdx;
        indexInLayer = indexInLyr;
    }

    /// <summary>동적 생성 시 다음 노드 연결 추가.</summary>
    public void AddNextPointId(int nextId)
    {
        if (!nextPointIds.Contains(nextId))
            nextPointIds.Add(nextId);
    }

    /// <summary>런타임에 방 카테고리를 변경 (랜덤 배정 시).</summary>
    public void SetNormalRoomCategory(NormalRoomCategory category)
    {
        normalRoomCategory = category;
    }

    /// <summary>아이콘 로드가 완료되었는지 여부.</summary>
    public bool IsIconReady { get; private set; }

    /// <summary>현재 카테고리 기준으로 아이콘을 Addressable에서 비동기 로드.</summary>
    public void RefreshIcon()
    {
        IsIconReady = false;
        LoadIconAsync().Forget();
    }

    /// <summary>Resolved된 방 카테고리 기준으로 아이콘 갱신 (복귀 시).</summary>
    public void RefreshIconFromResolved()
    {
        if (string.IsNullOrEmpty(resolvedRoomCategory))
        {
            RefreshIcon();
            return;
        }

        // resolvedRoomCategory → NormalRoomCategory 변환
        var resolved = resolvedRoomCategory switch
        {
            "Battle" => NormalRoomCategory.Battle,
            "Elite"  => NormalRoomCategory.Elite,
            "Event"  => NormalRoomCategory.Event,
            "Shop"   => NormalRoomCategory.Shop,
            "Rest"   => NormalRoomCategory.Random, // Rest: 쉬어가는 방 (iconMap.restIconKey 사용)
            _        => normalRoomCategory,
        };
        normalRoomCategory = resolved;

        // Rest/Start/Boss는 iconMap에 별도 키가 있으면 우선 사용
        var overrideKey = iconMap?.GetIconKeyOverride(resolvedRoomCategory);
        if (!string.IsNullOrEmpty(overrideKey))
        {
            LoadIconByKeyAsync(overrideKey).Forget();
            return;
        }
        RefreshIcon();
    }

    private async UniTaskVoid LoadIconAsync()
    {
        if (iconMap == null || _image == null) return;

        var key = iconMap.GetIconKey(stageCategory, normalRoomCategory);
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            // Sprite 직접 로드 시도
            Sprite sprite = null;
            try
            {
                sprite = await Managers.AddressableManager.LoadAssetAsync<Sprite>(key);
            }
            catch { }

            // Sprite 실패 시 Texture2D로 폴백 → Sprite 생성
            if (sprite == null)
            {
                var tex = await Managers.AddressableManager.LoadAssetAsync<Texture2D>(key);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }

            if (sprite != null && _image != null)
            {
                _image.sprite = sprite;
                _image.SetNativeSize();
                _image.color = Color.white;
            }

            IsIconReady = true;
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = true;
                FadeInAsync().Forget();
            }
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[StagePointUI] 아이콘 로드 실패: key={key}, err={e.Message}");
            IsIconReady = true;
            if (_canvasGroup != null)
                _canvasGroup.blocksRaycasts = true;
        }
    }

    private async UniTaskVoid LoadIconByKeyAsync(string key)
    {
        if (string.IsNullOrEmpty(key) || _image == null) return;
        try
        {
            Sprite sprite = null;
            try { sprite = await Managers.AddressableManager.LoadAssetAsync<Sprite>(key); } catch { }
            if (sprite == null)
            {
                var tex = await Managers.AddressableManager.LoadAssetAsync<Texture2D>(key);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            if (sprite != null && _image != null)
            {
                _image.sprite = sprite;
                _image.SetNativeSize();
                _image.color = Color.white;
            }
            IsIconReady = true;
            if (_canvasGroup != null) { _canvasGroup.blocksRaycasts = true; FadeInAsync().Forget(); }
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[StagePointUI] override 아이콘 로드 실패: key={key}, err={e.Message}");
            IsIconReady = true;
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = true;
        }
    }

    public void Register(StagePointManager mgr, RoomManager roomMgr)
    {
        _mgr = mgr;
        _roomMgr = roomMgr;

        mgr.Register(
            pointId: pointId,
            stageCategory: stageCategory,
            nextPointIds: nextPointIds,
            normalRoomCategory: normalRoomCategory,
            minDifficulty: (minDifficulty >= 0) ? (int?)minDifficulty : null,
            maxDifficulty: (maxDifficulty >= 0) ? (int?)maxDifficulty : null,
            requiredTags: (requiredTags != null && requiredTags.Count > 0) ? requiredTags : null
        );

        mgr.OnPointResolved -= HandleResolved;
        mgr.OnPointResolved += HandleResolved;

        // 이미 Resolve된 상태면 즉시 아이콘 갱신 (복귀 시)
        var ctx = mgr.GetContext(pointId);
        if (ctx != null && ctx.IsResolved)
            HandleResolved(pointId, ctx.ResolvedRoomId);
        else if (!IsIconReady)
            RefreshIcon(); // Resolve 전이라도 stageCategory 기반 기본 아이콘 로드
    }

    private void OnDisable()
    {
        if (_mgr != null)
            _mgr.OnPointResolved -= HandleResolved;
    }

    // ─────────────────────────────────────────────────────────
    // 클릭 (Button 컴포넌트의 OnClick 또는 코드에서 직접 호출)
    // ─────────────────────────────────────────────────────────
    public void OnPointClicked()
    {
        Debug.Log($"[StagePointUI] Clicked pointId={pointId}");

        var app = AppBootstrapper.Instance;
        if (app == null) return;

        var run = app.CurrentRun;
        if (run == null || !run.IsRunning)
        {
            Debug.LogWarning($"[StagePointUI] pointId={pointId} → run null or not running.");
            return;
        }
        if (run.StagePointManager == null)
        {
            Debug.LogWarning($"[StagePointUI] pointId={pointId} → StagePointManager is null");
            return;
        }

        // StageMap 씬: 포인트 선택만 기록 후 GameScene으로 전환
        bool isStageMapScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            == Define.Scene.StageMap.ToString();

        if (isStageMapScene)
        {
            if (!run.SelectPoint(pointId))
            {
                Debug.LogWarning($"[StagePointUI] pointId={pointId} → SelectPoint 실패");
                return;
            }

            Debug.Log($"[StagePointUI] pointId={pointId} → GameScene 전환");
            PlayExitAndLoadAsync(app).Forget();
        }
        else
        {
            // GameScene 내부: 기존처럼 맵 즉시 스폰
            if (!run.StagePointManager.CanMove(pointId))
            {
                Debug.LogWarning($"[StagePointUI] pointId={pointId} → CanMove=false");
                return;
            }

            Debug.Log($"[StagePointUI] pointId={pointId} → RequestMoveTo 호출");
            if (TransitionOverlay.Instance != null)
                TransitionOverlay.Instance.PlayAsync(() => run.RequestMoveTo(pointId)).Forget();
            else
                run.RequestMoveTo(pointId);
        }
    }

    private void HandleResolved(int resolvedPointId, string roomId)
    {
        if (resolvedPointId != pointId) return;

        resolvedRoomId = roomId;

        var room = _roomMgr?.GetById(roomId);
        if (room == null)
        {
            resolvedRoomName = "(not found)";
            resolvedRoomCategory = "";
            resolvedDifficulty = 0;
            resolvedPrefab = "";
            resolvedTags = "";
            RefreshIcon();
            return;
        }

        resolvedRoomName = room.name;
        resolvedRoomCategory = room.category;
        resolvedDifficulty = room.difficulty;
        resolvedPrefab = room.prefab;
        resolvedTags = (room.tags == null) ? "" : string.Join(", ", room.tags);

        // Resolve 결과의 카테고리로 아이콘 갱신
        RefreshIconFromResolved();
    }

    // ── 퇴장 연출 + 씬 전환 ──

    private async UniTaskVoid PlayExitAndLoadAsync(AppBootstrapper app)
    {
        // 입력 차단
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;

        // 선택 노드로 포커스
        var scroller = GetComponentInParent<StageMapScroller>(true);
        if (scroller == null)
            scroller = FindObjectOfType<StageMapScroller>(true);

        if (scroller != null)
            scroller.FocusOn(GetComponent<RectTransform>());

        // 줌인 연출 (클릭한 노드 중심으로)
        var content = scroller != null ? scroller.ContentTransform : null;
        var myRT = GetComponent<RectTransform>();

        if (content != null)
            await ZoomIntoNodeAsync(content, myRT);

        // 페이드 → 씬 전환
        if (TransitionOverlay.Instance != null)
            await TransitionOverlay.Instance.PlayAsync(() => app.RequestLoad(Define.Scene.GameScene));
        else
            app.RequestLoad(Define.Scene.GameScene);
    }

    /// <summary>클릭한 노드를 중심으로 맵을 확대. 노드가 화면 중앙에 유지됨.</summary>
    private async UniTask ZoomIntoNodeAsync(RectTransform content, RectTransform node)
    {
        if (content == null || node == null) return;

        float duration = 0.8f;
        float targetScale = 2.2f;
        float elapsed = 0f;

        Vector2 nodePos = node.anchoredPosition;

        while (elapsed < duration && content != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t;
            float scale = Mathf.Lerp(1f, targetScale, ease);

            content.localScale = Vector3.one * scale;
            content.anchoredPosition = -nodePos * scale;

            await UniTask.Yield(PlayerLoopTiming.Update);
        }
    }

    // ── 노드 페이드인 ──

    private async UniTaskVoid FadeInAsync()
    {
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 0f;

        float duration = 0.4f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (_canvasGroup == null) return;
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;
    }

    // ── Glow 연출 (도달 가능 노드 — 테두리만 펄스) ──

    private void Update()
    {
        if (!_glowActive || _glowOutline == null) return;

        _glowPhase += Time.unscaledDeltaTime * 3f;
        float alpha = 0.15f + 0.25f * (0.5f + 0.5f * Mathf.Sin(_glowPhase));
        _glowOutline.effectColor = new Color(1f, 0.85f, 0.3f, alpha);
    }

    /// <summary>도달 가능 여부에 따라 glow on/off.</summary>
    public void SetGlow(bool active)
    {
        _glowActive = active;

        if (active)
        {
            EnsureGlow();
            _glowOutline.enabled = true;
        }
        else if (_glowOutline != null)
        {
            _glowOutline.enabled = false;
        }
    }

    private void EnsureGlow()
    {
        if (_glowOutline != null) return;
        if (_image == null) return;

        _glowOutline = _image.gameObject.AddComponent<Outline>();
        _glowOutline.effectColor = new Color(1f, 0.85f, 0.3f, 0.25f);
        _glowOutline.effectDistance = new Vector2(2f, -2f);
        _glowOutline.useGraphicAlpha = false;
    }

    // ── 호버 툴팁 ──

    private static GameObject _tooltipGO;
    private static TMPro.TextMeshProUGUI _tooltipText;
    private static RectTransform _tooltipRT;

    public void OnPointerEnter(PointerEventData eventData)
    {
        EnsureTooltip();
        if (_tooltipGO == null) return;

        string category = stageCategory switch
        {
            StageCategory.Start => "Start",
            StageCategory.Boss => "Boss",
            _ => normalRoomCategory.ToString(),
        };

        string info = stageCategory == StageCategory.Normal
            ? $"{category}\nID: {pointId}"
            : $"{category}";

        _tooltipText.text = info;
        _tooltipGO.SetActive(true);

        // 노드 위쪽에 배치
        var rt = GetComponent<RectTransform>();
        if (rt != null && _tooltipRT != null)
        {
            _tooltipRT.SetParent(rt.parent, false);
            _tooltipRT.anchoredPosition = rt.anchoredPosition + new Vector2(0f, 60f);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_tooltipGO != null)
            _tooltipGO.SetActive(false);
    }

    private static void EnsureTooltip()
    {
        if (_tooltipGO != null) return;

        _tooltipGO = new GameObject("NodeTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _tooltipRT = _tooltipGO.GetComponent<RectTransform>();
        _tooltipRT.sizeDelta = new Vector2(180f, 60f);
        _tooltipRT.pivot = new Vector2(0.5f, 0f);

        var bg = _tooltipGO.GetComponent<Image>();
        bg.color = new Color(0.15f, 0.12f, 0.08f, 0.85f);
        bg.raycastTarget = false;

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        textGO.transform.SetParent(_tooltipGO.transform, false);

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8f, 4f);
        textRT.offsetMax = new Vector2(-8f, -4f);

        _tooltipText = textGO.GetComponent<TMPro.TextMeshProUGUI>();
        _tooltipText.fontSize = 22f;
        _tooltipText.color = new Color(0.95f, 0.9f, 0.75f);
        _tooltipText.alignment = TMPro.TextAlignmentOptions.Center;
        _tooltipText.raycastTarget = false;

        _tooltipGO.SetActive(false);
    }
}
