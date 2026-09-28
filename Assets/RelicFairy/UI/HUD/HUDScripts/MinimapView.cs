using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 미니맵 뷰.
///
/// 역할:
///   - 방 범위(roomCenter, roomSize) 세팅으로 월드↔미니맵 좌표계 정의
///   - AddMarker / RemoveMarker 로 마커 동적 생성·제거
///   - 플레이어 마커는 별도 Image로 항상 최상단 유지
///   - HudView.SetSections() 의 Section.Minimap 플래그로 패널 ON/OFF
///   - 방 범위가 없으면(베이스캠프 · 런 밖) 판을 투명하게 숨긴다 — 빈 회색 판이 떴다(09-28)
///
/// 연결 순서:
///   1) Initialize(roomCenter, roomSize)  ← GameRunBootstrapper가 맵 빌드 후 호출
///   2) SetPlayerTransform(playerTr)      ← 플레이어 스폰 후 호출
///   3) AddMarker(transform, type)        ← MonsterSpawner.OnMonsterSpawned 구독 시 호출
///   4) RemoveMarker(marker)              ← MonsterBase.OnDied 시 호출
/// </summary>
public sealed class MinimapView : MonoBehaviour
{
    // ── SerializeField ───────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private Vector2 mapDisplayHalfSize = new(55f, 55f);

    // ── Private ──────────────────────────────────────────────
    private RectTransform _markerContainer;
    private RectTransform _playerMarkerRect;
    private CanvasGroup   _group;

    private Vector3 _roomCenter;
    private Vector2 _roomHalfSize;

    private Transform _playerTransform;

    private readonly Dictionary<Transform, MinimapMarker> _markers = new();
    private readonly List<MinimapMarker>                  _toRemove = new();

    // ── Properties ───────────────────────────────────────────
    /// <summary>방 범위를 받았는가. 없으면 판을 숨긴다.</summary>
    public bool HasRoom => _roomHalfSize.x > 0f && _roomHalfSize.y > 0f;

    // ── Lifecycle ────────────────────────────────────────────
    private void Awake()
    {
        var containerTr = FindChildRecursive(transform, "Container_Markers");
        if (containerTr != null)
            _markerContainer = containerTr as RectTransform ?? containerTr.GetComponent<RectTransform>();

        var playerMarkerTr = FindChildRecursive(transform, "Img_PlayerMarker");
        if (playerMarkerTr != null)
            _playerMarkerRect = playerMarkerTr as RectTransform ?? playerMarkerTr.GetComponent<RectTransform>();

        if (_playerMarkerRect != null)
            _playerMarkerRect.gameObject.SetActive(false);

        if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
        ApplyTheme();
        ApplyVisible();
    }

    private void LateUpdate()
    {
        UpdatePlayerMarker();
        CleanupDeadMarkers();
    }

    private void OnDestroy()
    {
        _markers.Clear();
        _playerTransform = null;
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>방 진입 시 호출. 모든 기존 마커를 초기화하고 새 방 범위를 적용.</summary>
    public void Initialize(Vector3 roomCenter, Vector2 roomSize)
    {
        _roomCenter   = roomCenter;
        _roomHalfSize = roomSize * 0.5f;

        ClearAllMarkers();

        // 기존 마커들 범위 갱신 (재사용 케이스 대비)
        foreach (var m in _markers.Values)
            m.UpdateBounds(_roomCenter, _roomHalfSize, mapDisplayHalfSize);

        ApplyVisible();
    }

    /// <summary>런을 떠날 때 호출 — 범위·마커를 비우고 판을 숨긴다(다음 씬에서 지난 방을 그리지 않게).</summary>
    public void ClearRoom()
    {
        _roomHalfSize = Vector2.zero;
        ClearAllMarkers();
        ApplyVisible();
    }

    public void SetPlayerTransform(Transform playerTr)
    {
        _playerTransform = playerTr;
        SetActiveSafe(_playerMarkerRect, playerTr != null);
    }

    /// <summary>마커 추가. 같은 Transform에 대한 중복 추가는 무시.</summary>
    public MinimapMarker AddMarker(Transform worldTransform, MinimapMarkerType type)
    {
        if (worldTransform == null) return null;
        if (_markers.ContainsKey(worldTransform)) return _markers[worldTransform];
        if (_markerContainer == null) return null;

        var go = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MinimapMarker));
        go.transform.SetParent(_markerContainer, false);

        var markerRect = go.GetComponent<RectTransform>();
        markerRect.sizeDelta = new Vector2(7f, 7f);

        var marker = go.GetComponent<MinimapMarker>();

        marker.Initialize(worldTransform, type, this, _roomCenter, _roomHalfSize, mapDisplayHalfSize);
        _markers[worldTransform] = marker;
        return marker;
    }

    /// <summary>마커 컴포넌트를 직접 지정해 제거. MinimapMarker 자신이 Null 감지 시 호출.</summary>
    public void RemoveMarker(MinimapMarker marker)
    {
        if (marker == null) return;
        _toRemove.Add(marker);
    }

    /// <summary>Transform 키로 마커 제거.</summary>
    public void RemoveMarker(Transform worldTransform)
    {
        if (worldTransform == null) return;
        if (!_markers.TryGetValue(worldTransform, out var marker)) return;

        _markers.Remove(worldTransform);
        if (marker != null)
            Destroy(marker.gameObject);
    }

    public void ClearAllMarkers()
    {
        foreach (var m in _markers.Values)
        {
            if (m != null)
                Destroy(m.gameObject);
        }
        _markers.Clear();
        _toRemove.Clear();
    }

    // ── Private Methods ──────────────────────────────────────

    /// <summary>
    /// 공통 글래스 판(UITheme) — 임시 테두리 아트(하늘색 네모 틀) · 네모 바탕이 월드 위 회색 네모로 떴다
    /// (09-28 UI 톤 통일, 사용자 결정 「공통 글래스 판으로 재질만」). 둥근 인디고 판 + 금 가는 선, 마스크도 둥글게.
    /// 플레이어 점은 원색 초록 → 양피지 잉크(몬스터 빨강과 갈린다).
    /// </summary>
    private void ApplyTheme()
    {
        if (TryGetComponent<Image>(out var plate)) UITheme.StylePanel(plate, UITheme.Glass, UITheme.GoldLine, 12f);

        var frame = FindChildRecursive(transform, "Img_Frame");
        if (frame != null) frame.gameObject.SetActive(false);

        var mask = FindChildRecursive(transform, "Mask_Area");
        if (mask != null && mask.TryGetComponent<Image>(out var maskImg))
        {
            maskImg.sprite = UIProceduralSprites.RoundedRect(radius: 10f, feather: 1.5f);
            maskImg.type   = Image.Type.Sliced;
            if (mask.TryGetComponent<Mask>(out var m)) m.showMaskGraphic = false;
        }

        var bg = FindChildRecursive(transform, "Img_MapBg");
        if (bg != null && bg.TryGetComponent<Image>(out var bgImg)) bgImg.color = new Color(0.02f, 0.02f, 0.04f, 0.35f);

        if (_playerMarkerRect != null && _playerMarkerRect.TryGetComponent<Image>(out var dot)) dot.color = UITheme.Ink;
    }
    /// <summary>범위가 없으면 판 전체(테두리 포함)를 투명하게. 켜기/끄기는 HudView 섹션이 하므로 활성 상태는 두고 알파만 쓴다.</summary>
    private void ApplyVisible()
    {
        if (_group == null) return;
        bool on = HasRoom;
        _group.alpha          = on ? 1f : 0f;
        _group.blocksRaycasts = on;
    }

    private void UpdatePlayerMarker()
    {
        if (_playerTransform == null || _playerMarkerRect == null) return;

        Vector3 world = _playerTransform.position;
        float nx = (_roomHalfSize.x > 0f) ? (world.x - _roomCenter.x) / _roomHalfSize.x : 0f;
        float ny = (_roomHalfSize.y > 0f) ? (world.z - _roomCenter.z) / _roomHalfSize.y : 0f;

        nx = Mathf.Clamp(nx, -1f, 1f);
        ny = Mathf.Clamp(ny, -1f, 1f);

        _playerMarkerRect.anchoredPosition = new Vector2(nx * mapDisplayHalfSize.x, ny * mapDisplayHalfSize.y);

        float yaw = _playerTransform.eulerAngles.y;
        _playerMarkerRect.localEulerAngles = new Vector3(0f, 0f, -yaw);
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root.name == childName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }

    private void CleanupDeadMarkers()
    {
        if (_toRemove.Count == 0) return;

        foreach (var marker in _toRemove)
        {
            if (marker == null) continue;
            // _markers 역방향 탐색으로 키 제거
            var tracked = marker.TrackedTransform;
            if (tracked != null)
                _markers.Remove(tracked);
            Destroy(marker.gameObject);
        }
        _toRemove.Clear();
    }

    private static void SetActiveSafe(Component comp, bool on)
    {
        if (comp == null) return;
        if (comp.gameObject.activeSelf == on) return;
        comp.gameObject.SetActive(on);
    }
}
