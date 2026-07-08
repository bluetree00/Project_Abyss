using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 통합 존 게이트. 스타트 방(Zone 0)과 일반 전투/코리도 방(Zone 1+) 모두 이 컴포넌트를 사용한다.
///
/// 스타트 방 모드 (InitGate 미호출):
///   Update()로 IsLoadoutReady()를 폴링해 portalActive를 토글한다.
///   트리거 진입 시 ShowZoneSelectionAsync(0) → StageMap 전환 흐름.
///
/// 일반 방 모드 (InitGate 호출 후):
///   ZoneProgressionService.EnableExitGateForZone() 호출 시 활성화.
///   트리거 진입 시 DirectlyEnterZoneAsync(from, to) 호출.
///   현재 존이 클리어되지 않은 전투 존이면 통과 불가.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StartRoomGate : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────
    private const float IndicatorHeight    = 5f;
    private const float CanvasScale        = 0.007f;
    private const float GracePeriod        = 0.3f;
    private const float TriggerDepth       = 2f;
    private const float DefaultGateWidth   = 5f;  // OpenWallsForConnections gateWidth(5) × blockCellSize(1)
    private const float DefaultGateHeight  = 5f;

    // 통과 후 포털 색상 — 거의 투명하게
    private static readonly Color PassThroughColor = new Color(0.4f, 0.85f, 1f, 0.04f);

    // 카테고리별 포털 기본 색상
    private static readonly Color ColorBattle   = new Color(0.25f, 0.55f, 1.00f, 0.85f);
    private static readonly Color ColorElite    = new Color(0.65f, 0.20f, 1.00f, 0.85f);
    private static readonly Color ColorBoss     = new Color(1.00f, 0.18f, 0.18f, 0.85f);
    private static readonly Color ColorCorridor = new Color(0.55f, 0.85f, 0.55f, 0.85f);
    private static readonly Color ColorDefault  = new Color(0.70f, 0.70f, 0.70f, 0.85f);

    // ── [SerializeField] ─────────────────────────────────────────
    [SerializeField, Tooltip("준비 완료 시 활성화할 포탈 비주얼 오브젝트")]
    private GameObject portalActive;
    [SerializeField, Tooltip("준비 미완료 시 노출할 안내 오브젝트 (스타트 방 전용)")]
    private GameObject notReadyIndicator;

    // ── Private fields ────────────────────────────────────────────
    private int                    _fromZoneIndex = -1;
    private int                    _toZoneIndex   = -1;
    private string                 _targetLabel;
    private string                 _category;
    private ZoneProgressionService _progression;

    private float _gateWidth  = DefaultGateWidth;
    private float _gateHeight = DefaultGateHeight;

    private bool             _triggered;
    private bool             _gateOpen;
    private bool             _isEnabled;
    private float            _enabledTime  = float.MaxValue;
    private PlayerController _frozenPlayer;

    private GameObject      _worldIndicatorGO;
    private TextMeshProUGUI _distanceText;
    private bool            _flashActive;

    // ── Init ──────────────────────────────────────────────────────

    private void Awake()
    {
        ResizeTriggerCollider();
        ResizeGate();
    }

    /// <summary>Zone 1+ 게이트 초기화. 호출 시 스타트 방 모드에서 일반 방 모드로 전환된다.</summary>
    /// <param name="openingWidth">벽 개구부 월드 너비 (GateWidth × blockCellSize). 0 이하면 기본값 사용.</param>
    public void InitGate(int fromZoneIndex, int toZoneIndex, string targetLabel, string category,
        ZoneProgressionService progression, float openingWidth = DefaultGateWidth)
    {
        _fromZoneIndex = fromZoneIndex;
        _toZoneIndex   = toZoneIndex;
        _targetLabel   = string.IsNullOrEmpty(targetLabel) ? "다음 구역" : targetLabel;
        _category      = category ?? string.Empty;
        _progression   = progression;
        if (openingWidth > 0f) _gateWidth = openingWidth;

        if (!TryGetComponent<Collider>(out _))
            gameObject.AddComponent<BoxCollider>();
        ResizeTriggerCollider();

        ApplyCategoryColor();
        ResizeGate();
    }

    /// <summary>존 클리어(또는 비전투 존 진입) 시 ZoneProgressionService가 호출. 게이트 활성화.</summary>
    public void EnableGate()
    {
        if (_isEnabled || _triggered) return;
        _isEnabled   = true;
        _enabledTime = Time.time;
        gameObject.SetActive(true);
        if (portalActive != null) portalActive.SetActive(true);
        CreateWorldIndicator();
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Update()
    {
        if (_fromZoneIndex != -1)
        {
            UpdateIndicator();
            return;
        }

        bool ready = IsLoadoutReady();
        if (ready == _gateOpen) return;
        _gateOpen = ready;
        if (portalActive != null) portalActive.SetActive(ready);
    }

    private void OnDestroy()
    {
        if (_worldIndicatorGO != null) Destroy(_worldIndicatorGO);
    }

    // ── Trigger ───────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other) => TryActivate(other);
    private void OnTriggerStay(Collider other)  => TryActivate(other);

    private void OnTriggerExit(Collider other)
    {
        if (_fromZoneIndex != -1) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        if (notReadyIndicator != null) notReadyIndicator.SetActive(false);
    }

    // ── Private ───────────────────────────────────────────────────

    private void ResizeTriggerCollider()
    {
        if (!TryGetComponent<BoxCollider>(out var bc)) return;
        bc.isTrigger = true;
        bc.size      = new Vector3(_gateWidth, _gateHeight, TriggerDepth);
        bc.center    = new Vector3(0f, _gateHeight * 0.5f, 0f);
    }

    private void ResizeGate()
    {
        float halfW = _gateWidth  * 0.5f;
        float halfH = _gateHeight * 0.5f;

        if (portalActive != null)
        {
            var ps = portalActive.transform.localScale;
            portalActive.transform.localPosition = new Vector3(0f, halfH, 0f);
            portalActive.transform.localScale    = new Vector3(_gateWidth, _gateHeight, ps.z);
        }

        const float PillarHalfWidth   = 0.15f;
        const float CapsuleMeshHeight = 2f;
        float pillarScaleY = _gateHeight / CapsuleMeshHeight;

        var pillarL = transform.Find("Pillar_L");
        if (pillarL != null)
        {
            var ps = pillarL.localScale;
            pillarL.localPosition = new Vector3(-(halfW - PillarHalfWidth), halfH, 0f);
            pillarL.localScale    = new Vector3(ps.x, pillarScaleY, ps.z);
        }

        var pillarR = transform.Find("Pillar_R");
        if (pillarR != null)
        {
            var ps = pillarR.localScale;
            pillarR.localPosition = new Vector3(+(halfW - PillarHalfWidth), halfH, 0f);
            pillarR.localScale    = new Vector3(ps.x, pillarScaleY, ps.z);
        }

        var lintel = transform.Find("Lintel");
        if (lintel != null)
        {
            var ps = lintel.localScale;
            lintel.localPosition = new Vector3(0f, _gateHeight + ps.y * 0.5f, 0f);
            lintel.localScale    = new Vector3(_gateWidth, ps.y, ps.z);
        }
    }

    private void ApplyCategoryColor()
    {
        if (portalActive == null) return;
        var color = CategoryColor(_category);
        var mpb   = new MaterialPropertyBlock();
        foreach (var r in portalActive.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color",     color);
            r.SetPropertyBlock(mpb);
        }
    }

    private void SetGatePassable()
    {
        if (portalActive == null) return;

        foreach (var col in portalActive.GetComponentsInChildren<Collider>())
            if (!col.isTrigger) col.enabled = false;

        portalActive.SetActive(false);
    }

    private void TryActivate(Collider other)
    {
        if (_triggered) return;

        bool isPlayer = other.GetComponentInParent<PlayerController>() != null;

        if (_fromZoneIndex != -1)
        {
            if (!_isEnabled)
            {
                if (isPlayer) ShowLockedFeedback();
                return;
            }
            if (Time.time - _enabledTime < GracePeriod) return;

            // 현재 존이 클리어되지 않은 전투 존이면 다른 방 게이트 통과 불가
            if (_progression != null)
            {
                int currentZone = _progression.CurrentZoneIndex;
                if (currentZone != _fromZoneIndex && !_progression.IsExitEnabled(currentZone))
                {
                    if (isPlayer) ShowLockedFeedback();
                    return;
                }
            }
        }

        if (!isPlayer) return;

        if (_fromZoneIndex == -1 && !IsLoadoutReady())
        {
            if (notReadyIndicator != null) notReadyIndicator.SetActive(true);
            Debug.LogWarning("[StartRoomGate] 캐릭터·무기 미선택 — 게이트 통과 불가");
            return;
        }

        _triggered = true;
        if (_worldIndicatorGO != null) _worldIndicatorGO.SetActive(false);
        SetGatePassable();

        // 스타트 방 게이트: 서약 선택 UI 동안 플레이어 이동 고정
        if (_fromZoneIndex == -1)
        {
            // 퀘스트: 게이트로 챕터 입장 보고 (target='*')
            QuestEvents.Report("Gate", "*");

            var pc = other.GetComponentInParent<PlayerController>();
            if (pc != null) FreezePlayer(pc);
        }

        GateActivateAsync().Forget();
    }

    private void ShowLockedFeedback()
    {
        if (_flashActive || portalActive == null) return;
        FlashLockedAsync().Forget();
    }

    private async UniTaskVoid FlashLockedAsync()
    {
        _flashActive = true;
        var mpb         = new MaterialPropertyBlock();
        var lockedColor = new Color(1f, 0.15f, 0.15f, 0.80f);
        var baseColor   = CategoryColor(_category);

        foreach (var r in portalActive.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", lockedColor);
            mpb.SetColor("_Color",     lockedColor);
            r.SetPropertyBlock(mpb);
        }

        try
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(0.35f),
                cancellationToken: gameObject.GetCancellationTokenOnDestroy());
        }
        catch (System.OperationCanceledException) { _flashActive = false; return; }

        foreach (var r in portalActive.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", baseColor);
            mpb.SetColor("_Color",     baseColor);
            r.SetPropertyBlock(mpb);
        }

        _flashActive = false;
    }

    private async UniTaskVoid GateActivateAsync()
    {
        var ct = gameObject.GetCancellationTokenOnDestroy();

        if (_fromZoneIndex != -1)
        {
            if (_progression != null)
            {
                try { await _progression.DirectlyEnterZoneAsync(_fromZoneIndex, _toZoneIndex, ct); }
                catch (System.OperationCanceledException) { }
            }
            Destroy(gameObject);
            return;
        }

        try { await ExitStartRoomAsync(ct); }
        catch (System.OperationCanceledException) { }
    }

    private async UniTask ExitStartRoomAsync(System.Threading.CancellationToken ct)
    {
        var bootstrapper = GameRunBootstrapper.Instance;

        // [서약 폐기] 사전제작 서약 예약(PlayerLoadout.ReservedCovenants) 적용을 폐기.
        // 서약 획득은 챕터 시작 대기방의 조립 서약 제단(WorldCovenantAltar)으로 일원화됨.

        // 플레이어 이동 복구
        UnfreezePlayer();

        // 절차 진행(하데스형): RunFlowController로 런 시작
        if (bootstrapper != null)
            await bootstrapper.StartProcGenRunAsync();

        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
    }

    // ── Player freeze helpers ─────────────────────────────────────

    private void FreezePlayer(PlayerController pc)
    {
        _frozenPlayer = pc;
        pc.SetInputEnabled(false);
        if (pc.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
        }
    }

    private void UnfreezePlayer()
    {
        if (_frozenPlayer == null) return;
        if (_frozenPlayer.TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = false;
        _frozenPlayer.SetInputEnabled(true);
        _frozenPlayer = null;
    }

    private static bool IsLoadoutReady()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        return loadout != null && loadout.IsReady && loadout.WeaponSlot0 != null;
    }

    // ── Category helpers ──────────────────────────────────────────

    private static Color CategoryColor(string cat) => cat?.ToLower() switch
    {
        "boss"     => ColorBoss,
        "elite"    => ColorElite,
        "battle"   => ColorBattle,
        "corridor" => ColorCorridor,
        "start"    => ColorCorridor,
        _          => ColorDefault,
    };

    private static string CategoryKor(string cat) => cat?.ToLower() switch
    {
        "battle"   => "전투",
        "elite"    => "정예",
        "boss"     => "보스",
        "corridor" => "통로",
        "start"    => "시작",
        _          => cat ?? "?",
    };

    // ── World Indicator ───────────────────────────────────────────

    private void UpdateIndicator()
    {
        if (_worldIndicatorGO == null || _triggered) return;
        var cam = Camera.main;
        if (cam != null) _worldIndicatorGO.transform.rotation = cam.transform.rotation;
        UpdateIndicatorText();
    }

    private void CreateWorldIndicator()
    {
        if (_fromZoneIndex == -1) return;

        _worldIndicatorGO = new GameObject("GateIndicator");
        _worldIndicatorGO.transform.SetParent(transform, false);
        _worldIndicatorGO.transform.localPosition = Vector3.up * IndicatorHeight;

        var canvas = _worldIndicatorGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var rt       = _worldIndicatorGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280f, 100f);
        rt.localScale = Vector3.one * CanvasScale;

        // 배경
        var bgGO  = new GameObject("BG");
        bgGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.04f, 0.10f, 0.95f);
        var bgRT  = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // 카테고리 색 사이드바
        var sideGO  = new GameObject("Side");
        sideGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var sideImg = sideGO.AddComponent<Image>();
        sideImg.color = CategoryColor(_category);
        var sideRT  = sideGO.GetComponent<RectTransform>();
        sideRT.anchorMin = new Vector2(0f, 0f);
        sideRT.anchorMax = new Vector2(0f, 1f);
        sideRT.pivot     = new Vector2(0f, 0.5f);
        sideRT.offsetMin = new Vector2(0f,  0f);
        sideRT.offsetMax = new Vector2(6f,  0f);

        // 목적지 라벨
        var labelGO  = new GameObject("Label");
        labelGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text      = $"<b>{_targetLabel}</b>";
        labelTMP.fontSize  = 28f;
        labelTMP.alignment = TextAlignmentOptions.Left;
        labelTMP.color     = Color.white;
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.45f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(14f,  2f);
        labelRT.offsetMax = new Vector2(-8f, -4f);

        // 카테고리 + 거리 행
        var subGO  = new GameObject("Sub");
        subGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var subTMP = subGO.AddComponent<TextMeshProUGUI>();
        subTMP.fontSize  = 19f;
        subTMP.alignment = TextAlignmentOptions.Left;
        subTMP.color     = new Color(0.75f, 0.75f, 0.75f);
        var subRT = subGO.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0f, 0f);
        subRT.anchorMax = new Vector2(1f, 0.45f);
        subRT.offsetMin = new Vector2(14f, 4f);
        subRT.offsetMax = new Vector2(-8f, 0f);

        string catKor = CategoryKor(_category);
        subTMP.text = catKor;
        _distanceText = subTMP; // 거리 업데이트용으로 재활용
        // 매 프레임 UpdateIndicator에서 distance만 갱신
        subTMP.text = $"{catKor}   <color=#aaaaaa>--m</color>";
        _distanceText = subTMP;
    }

    private void UpdateIndicatorText()
    {
        if (_distanceText == null) return;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        int dist = Mathf.RoundToInt(Vector3.Distance(transform.position, player.transform.position));
        _distanceText.text = $"{CategoryKor(_category)}   <color=#aaaaaa>{dist}m</color>";
    }
}
