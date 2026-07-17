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

    [Header("스타트 방 게이트 너머 복도 (절차 방과 동일한 통로→다음 방 연출)")]
    [SerializeField, Tooltip("복도/방을 지을 팔레트. 미지정 시 복도를 만들지 않음. 절차 방과 동일한 BlockPalette 사용")]
    private BlockPalette startCorridorPalette;
    [SerializeField, Tooltip("게이트 로컬 기준 바깥 방향. 복도가 허공 쪽으로 뻗도록 North/South 중 맞는 쪽 선택")]
    private DoorEdge startCorridorEdge = DoorEdge.North;
    [SerializeField, Min(0), Tooltip("복도 길이(칸). 0이면 방만, 절차 방 기본=12")]
    private int startCorridorLength = 12;
    [SerializeField, Min(1), Tooltip("복도/방 벽 높이(칸). 게이트 높이에 맞춰 조정")]
    private int startCorridorWallLayers = 6;

    [Header("게이트 열림 연출 (서약 선택 완료 후 카메라 집중 + 통로 조망)")]
    [SerializeField, Tooltip("켜짐: 서약 없이 로드아웃(유물+무기)만으로 자동 연출. 끔(기본): 시작방 서약 제단 선택 완료 시 연출")]
    private bool revealOnLoadoutReady = false;
    [SerializeField, Min(0f), Tooltip("선택 완료 후 연출 시작까지 대기(초). 서약 알림/팝업이 정리될 여유")]
    private float gateRevealDelay = 2.2f;
    [SerializeField, Tooltip("연출 시 카메라가 게이트를 바라보는 위치 오프셋(게이트 로컬). 뒤/위로 빼서 통로를 조망")]
    private Vector3 gateRevealViewOffset = new Vector3(0f, 9f, -11f);
    [SerializeField, Tooltip("연출 카메라가 바라보는 지점의 높이(게이트 바닥 기준)")]
    private float gateRevealLookHeight = 2f;
    [SerializeField, Tooltip("연출: 이동/유지/복귀 시간(초)")]
    private Vector3 gateRevealDurations = new Vector3(1.1f, 1.9f, 0.9f);

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

    // 봉인 석문(Gothic) — 스타트 방 모드에서 준비되면 위로 올라가며 열림. 절차 방 게이트와 일관.
    private Transform       _sealDoor;
    private const float     SealDoorMeshW = 7f;
    private const float     SealDoorMeshH = 11.5f;

    private bool _startCorridorBuilt;
    private bool _gateRevealStarted;   // 열림 연출 1회 가드

    // ── Init ──────────────────────────────────────────────────────

    private void Awake()
    {
        ResizeTriggerCollider();
        ResizeGate();
    }

    private void OnEnable()  => WorldCovenantAltar.OnCovenantAssembled += HandleCovenantAssembled;
    private void OnDisable() => WorldCovenantAltar.OnCovenantAssembled -= HandleCovenantAssembled;

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

        EnsureSealDoor();          // 석문 보장(최초 1회, 항상 닫힘으로 시작)
        EnsureStartCorridor();     // 게이트 너머 복도+방 보장(최초 1회)

        bool ready = IsLoadoutReady();
        if (ready == _gateOpen) return;
        _gateOpen = ready;
        if (portalActive != null) portalActive.SetActive(ready);

        // 준비 완료 → 몇 초 뒤 카메라가 게이트에 집중되며 문이 열리고 통로를 조망하는 연출(1회).
        // 서약 오브젝트 배치 후엔 revealOnLoadoutReady를 끄고 선택 완료 흐름에서 TriggerGateReveal() 호출.
        if (ready)
        {
            if (revealOnLoadoutReady) TriggerGateReveal();
        }
        else
        {
            SetSealDoorClosed();
        }
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

    // ── 봉인 석문 (스타트 방 모드) ──────────────────────────────────
    private Vector3 SealDoorClosedPos => new Vector3(-_gateWidth * 0.5f, 0f, -0.25f); // 개구부 덮음(코너 피벗 중앙정렬)

    private void EnsureSealDoor()
    {
        if (_sealDoor != null) return;
        var prefab = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.GateSealDoorPrefab : null;
        if (prefab == null) return;
        var door = Instantiate(prefab, transform);
        door.name = "StartSealDoor";
        door.transform.localRotation = Quaternion.identity;
        door.transform.localScale    = new Vector3(_gateWidth / SealDoorMeshW, _gateHeight / SealDoorMeshH, 1f);
        door.transform.localPosition = SealDoorClosedPos; // 닫힘으로 시작
        SetSealDoorLayer(door, 8);
        foreach (var col in door.GetComponentsInChildren<Collider>()) Destroy(col); // 콜라이더 불필요(게이트가 차단) — 비용 절감
        _sealDoor = door.transform;

        // 석문이 시각을 담당하므로 레거시 포탈 비주얼(portalActive)은 렌더러를 꺼서 제거.
        // (준비 토글 로직은 유지되나 화면엔 안 보임 → 열리면 문 뒤로 개구부/맵이 드러남)
        if (portalActive != null)
            foreach (var r in portalActive.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    private void SetSealDoorClosed()
    {
        if (_sealDoor != null) _sealDoor.localPosition = SealDoorClosedPos;
    }

    private async UniTaskVoid OpenSealDoorAsync()
    {
        if (_sealDoor == null) return;
        Vector3 closed = SealDoorClosedPos;
        Vector3 open   = closed + Vector3.up * _gateHeight;

        // 열림 임팩트 — 먼지 + 문 열림 사운드
        var dust = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.GateSealDustVfx : null;
        if (dust != null) { var fx = Instantiate(dust, transform.position, Quaternion.identity); Destroy(fx, 3f); }
        Managers.Sound?.PlayEvent(SoundEvent.DoorOpen);

        var ct = this.GetCancellationTokenOnDestroy();
        float dur = 0.6f, t = 0f;
        try
        {
            while (t < dur)
            {
                if (_sealDoor == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - k) * (1f - k); // ease-out
                _sealDoor.localPosition = Vector3.Lerp(closed, open, e);
                await UniTask.Yield();
            }
            _sealDoor.localPosition = open;
        }
        catch (System.OperationCanceledException) { }
    }

    private static void SetSealDoorLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetSealDoorLayer(c.gameObject, layer);
    }

    // ── 게이트 너머 복도 + 다음 방 (절차 방과 동일한 통로 연출) ──────────
    private void EnsureStartCorridor()
    {
        if (_startCorridorBuilt) return;
        _startCorridorBuilt = true; // 팔레트 미지정이어도 매 프레임 재시도 방지(1회만)
        if (startCorridorPalette == null) return;

        var root = new GameObject("StartGateCorridor").transform;
        root.SetParent(transform, false); // 게이트 하위 → 게이트 회전이 복도 방향을 잡음
        int widthCells = Mathf.Max(1, Mathf.RoundToInt(_gateWidth)); // 게이트 폭과 개구부 일치
        MapBuilder.BuildDoorCorridor(
            startCorridorPalette, root, Vector3.zero, startCorridorEdge, widthCells,
            startCorridorLength, 1f, 0f, startCorridorWallLayers);
    }

    /// <summary>
    /// 게이트 열림 연출을 1회 시작한다. 로드아웃 자동 트리거(revealOnLoadoutReady) 외에,
    /// 시작방 서약 오브젝트 선택 완료 등 외부 흐름에서 직접 호출하는 진입점.
    /// </summary>
    public void TriggerGateReveal()
    {
        if (_gateRevealStarted) return;
        _gateRevealStarted = true;
        PlayGateRevealCinematicAsync().Forget();
    }

    // ── 로드아웃 완료 시 열림 연출 (카메라 집중 + 문 열림 + 통로 조망) ──────
    private async UniTaskVoid PlayGateRevealCinematicAsync()
    {
        var ct     = this.GetCancellationTokenOnDestroy();
        var player = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        var pc     = player != null ? player.GetComponentInParent<PlayerController>() : null;

        // 연출 내내 입력 잠금 — 연출 도중 플레이어가 게이트로 걸어가 다이브가 조기 발동/카메라 충돌하는 것 방지.
        // finally에서 반드시 해제(폴백·취소 포함).
        if (pc != null) FreezePlayer(pc);
        try
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(gateRevealDelay), cancellationToken: ct);

            var cam = GameCameraController.Instance;
            if (cam == null || player == null)
            {
                OpenSealDoorAsync().Forget(); // 폴백: 카메라/플레이어 없으면 문만 열기
                return;
            }

            // 카메라가 게이트로 이동 → 도착 즈음 문 열림 → 통로 조망 유지 → 플레이어로 복귀
            Vector3 target      = transform.position;
            Vector3 worldOffset = transform.TransformVector(gateRevealViewOffset); // 게이트 로컬 오프셋을 월드로
            OpenSealDoorAtAsync(gateRevealDurations.x * 0.85f, ct).Forget();

            await cam.PlayOnboardingRevealAsync(
                target, worldOffset, gateRevealLookHeight,
                gateRevealDurations.x, gateRevealDurations.y, gateRevealDurations.z, player, ct);
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (pc != null) UnfreezePlayer();
        }
    }

    private async UniTaskVoid OpenSealDoorAtAsync(float delaySec, System.Threading.CancellationToken ct)
    {
        try { await UniTask.Delay(System.TimeSpan.FromSeconds(delaySec), cancellationToken: ct); }
        catch (System.OperationCanceledException) { return; }
        OpenSealDoorAsync().Forget();
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

        // 스타트 방 게이트: 챕터 진입 연출/로딩 동안 플레이어 이동 고정
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

    // ── Event Handlers ────────────────────────────────────────────
    /// <summary>시작방 서약 제단 조립 완료 시 호출 — 몇 초 뒤 게이트 열림 연출(시작방 모드만).</summary>
    private void HandleCovenantAssembled()
    {
        if (_fromZoneIndex != -1) return; // 시작방(Zone 0) 모드에서만
        TriggerGateReveal();              // 딜레이(gateRevealDelay)는 연출 내부에서 적용
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
