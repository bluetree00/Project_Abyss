using Cysharp.Threading.Tasks;
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
/// </summary>
[RequireComponent(typeof(Collider))]
public class StartRoomGate : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────
    private const float IndicatorHeight  = 2.5f;
    private const float CanvasScale      = 0.006f;
    private const float GracePeriod      = 0.3f;
    private const float TriggerWidth     = 5f;   // 게이트 트리거 너비 (localX)
    private const float TriggerHeight    = 3f;   // 게이트 트리거 높이
    private const float TriggerDepth     = 1.5f; // 게이트 트리거 깊이 (localZ)
    private static readonly Color PassThroughColor = new Color(0.4f, 0.85f, 1f, 0.25f); // 통과 시 연한 청색

    // ── [SerializeField] ─────────────────────────────────────────
    [SerializeField, Tooltip("준비 완료 시 활성화할 포탈 비주얼 오브젝트")]
    private GameObject portalActive;
    [SerializeField, Tooltip("준비 미완료 시 노출할 안내 오브젝트 (스타트 방 전용)")]
    private GameObject notReadyIndicator;

    // ── Private fields ────────────────────────────────────────────
    private int                    _fromZoneIndex = -1; // -1 = 스타트 방 모드
    private int                    _toZoneIndex   = -1;
    private string                 _targetLabel;
    private ZoneProgressionService _progression;

    private bool  _triggered;
    private bool  _gateOpen;      // 스타트 방 모드: portalActive 상태 추적
    private bool  _isEnabled;     // 일반 방 모드: EnableGate() 호출 여부
    private float _enabledTime    = float.MaxValue;

    private GameObject      _worldIndicatorGO;
    private TextMeshProUGUI _distanceText;

    // ── Init ──────────────────────────────────────────────────────

    private void Awake()
    {
        ResizeTriggerCollider();
    }

    /// <summary>
    /// Zone 1+ 게이트 초기화. 호출 시 스타트 방 모드에서 일반 방 모드로 전환된다.
    /// CreateZoneExitGates에서 프리팹 인스턴스화 직후 호출.
    /// </summary>
    public void InitGate(int fromZoneIndex, int toZoneIndex, string targetLabel, ZoneProgressionService progression)
    {
        _fromZoneIndex = fromZoneIndex;
        _toZoneIndex   = toZoneIndex;
        _targetLabel   = string.IsNullOrEmpty(targetLabel) ? "다음 구역" : targetLabel;
        _progression   = progression;

        // 프리팹에 Collider가 없는 경우 런타임 보장 후 크기 재설정
        if (!TryGetComponent<Collider>(out _))
            gameObject.AddComponent<BoxCollider>();
        ResizeTriggerCollider();
    }

    /// <summary>존 클리어(또는 비전투 존 진입) 시 ZoneProgressionService가 호출. 게이트 활성화 + 인디케이터 생성.</summary>
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

        // 스타트 방 모드: 로드아웃 완료 여부 폴링
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
        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;
        if (notReadyIndicator != null) notReadyIndicator.SetActive(false);
    }

    // ── Private ───────────────────────────────────────────────────

    private void ResizeTriggerCollider()
    {
        if (!TryGetComponent<BoxCollider>(out var bc)) return;
        bc.isTrigger = true;
        bc.size      = new Vector3(TriggerWidth, TriggerHeight, TriggerDepth);
        bc.center    = new Vector3(0f, TriggerHeight * 0.5f, 0f);
    }

    /// <summary>
    /// 플레이어가 게이트를 통과할 때 호출.
    /// portalActive의 물리 차단을 해제하고 반투명 색상으로 전환해 통과 가능 상태를 표시한다.
    /// </summary>
    private void SetGatePassable()
    {
        if (portalActive == null) return;

        // 물리 차단 해제: 비트리거 콜라이더를 비활성화
        foreach (var col in portalActive.GetComponentsInChildren<Collider>())
            if (!col.isTrigger) col.enabled = false;

        // 반투명 청색으로 전환 (MaterialPropertyBlock — 원본 에셋 수정 없이 인스턴스별 적용)
        var mpb = new MaterialPropertyBlock();
        foreach (var r in portalActive.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", PassThroughColor); // URP
            mpb.SetColor("_Color",     PassThroughColor); // Standard
            r.SetPropertyBlock(mpb);
        }
    }

    private void TryActivate(Collider other)
    {
        if (_triggered) return;

        if (_fromZoneIndex != -1)
        {
            if (!_isEnabled) return;
            if (Time.time - _enabledTime < GracePeriod) return;
        }

        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;

        if (_fromZoneIndex == -1 && !IsLoadoutReady())
        {
            if (notReadyIndicator != null) notReadyIndicator.SetActive(true);
            Debug.LogWarning("[StartRoomGate] 캐릭터·무기 미선택 — 게이트 통과 불가");
            return;
        }

        _triggered = true;
        if (_worldIndicatorGO != null) _worldIndicatorGO.SetActive(false);
        SetGatePassable();
        GateActivateAsync().Forget();
    }

    private async UniTaskVoid GateActivateAsync()
    {
        var ct = gameObject.GetCancellationTokenOnDestroy();

        if (_fromZoneIndex != -1)
        {
            // 일반 방 모드: 다음 존 직접 진입
            if (_progression != null)
            {
                try { await _progression.DirectlyEnterZoneAsync(_fromZoneIndex, _toZoneIndex, ct); }
                catch (System.OperationCanceledException) { }
            }
            Destroy(gameObject);
            return;
        }

        // 스타트 방 모드
        try { await ExitStartRoomAsync(ct); }
        catch (System.OperationCanceledException) { }
    }

    private async UniTask ExitStartRoomAsync(System.Threading.CancellationToken ct)
    {
        var bootstrapper = GameRunBootstrapper.Instance;

        if (bootstrapper != null && bootstrapper.IsZoneLayoutMode)
        {
            var zoneProgression = bootstrapper.Run?.ZoneProgression;
            if (zoneProgression != null)
                await zoneProgression.DirectlyEnterFirstNextZoneAsync(0, ct);
            else
                await bootstrapper.SpawnRemainingWorldZonesAsync();

            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
            return;
        }

        var rp      = RunProgressManager.Instance;
        var session = bootstrapper?.Run;

        if (rp != null && session != null && session.IsRunning)
        {
            var spm = session.StagePointManager;
            if (spm != null && spm.CurrentPointId >= 0)
                spm.MarkCleared(spm.CurrentPointId);
            await rp.SaveAsync(session, rp.ActiveSlotIndex, isInStartRoom: false);
        }

        AppBootstrapper.Instance?.RequestLoad(Define.Scene.StageMap);
    }

    private static bool IsLoadoutReady()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        return loadout != null && loadout.IsReady && loadout.WeaponSlot0 != null;
    }

    // ── World Indicator ───────────────────────────────────────────

    private void UpdateIndicator()
    {
        if (_worldIndicatorGO == null || _triggered) return;
        var cam = Camera.main;
        if (cam != null) _worldIndicatorGO.transform.rotation = cam.transform.rotation;
        if (_distanceText == null) return;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        _distanceText.text = $"{Mathf.RoundToInt(Vector3.Distance(transform.position, player.transform.position))}m";
    }

    private void CreateWorldIndicator()
    {
        if (_fromZoneIndex == -1) return; // 스타트 방에는 인디케이터 없음

        _worldIndicatorGO = new GameObject("GateIndicator");
        _worldIndicatorGO.transform.SetParent(transform, false);
        _worldIndicatorGO.transform.localPosition = Vector3.up * IndicatorHeight;

        var canvas = _worldIndicatorGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var rt        = _worldIndicatorGO.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(200f, 70f);
        rt.localScale = Vector3.one * CanvasScale;

        var bgGO  = new GameObject("BG");
        bgGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.12f, 0.88f);
        var bgRT  = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        var labelGO  = new GameObject("Label");
        labelGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text      = _targetLabel;
        labelTMP.fontSize  = 24f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color     = new Color(1f, 0.88f, 0.35f);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.45f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(6f, 0f);
        labelRT.offsetMax = new Vector2(-6f, -2f);

        var distGO = new GameObject("Distance");
        distGO.transform.SetParent(_worldIndicatorGO.transform, false);
        _distanceText           = distGO.AddComponent<TextMeshProUGUI>();
        _distanceText.fontSize  = 17f;
        _distanceText.alignment = TextAlignmentOptions.Center;
        _distanceText.color     = new Color(0.85f, 0.85f, 0.85f);
        _distanceText.text      = "";
        var distRT = distGO.GetComponent<RectTransform>();
        distRT.anchorMin = new Vector2(0f, 0f);
        distRT.anchorMax = new Vector2(1f, 0.45f);
        distRT.offsetMin = new Vector2(6f, 2f);
        distRT.offsetMax = new Vector2(-6f, 0f);
    }
}
