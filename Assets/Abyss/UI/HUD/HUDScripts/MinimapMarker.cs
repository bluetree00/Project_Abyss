using UnityEngine;
using UnityEngine.UI;

public enum MinimapMarkerType
{
    Player   = 0,
    Monster  = 1,
    Elite    = 2,
    Boss     = 3,
    Portal   = 4,
    Shop     = 5,
    Prop     = 6,
}

/// <summary>
/// 미니맵 마커 단위 컴포넌트.
/// MinimapView가 마커를 생성한 후 Initialize()로 세팅.
/// LateUpdate에서 TrackedTransform의 월드 좌표를 미니맵 UV로 변환해 위치 갱신.
/// </summary>
public sealed class MinimapMarker : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private static readonly Color ColorMonster = new(1f,   0.25f, 0.25f, 1f);
    private static readonly Color ColorElite   = new(1f,   0.6f,  0.1f,  1f);
    private static readonly Color ColorBoss    = new(0.8f, 0f,    0f,    1f);
    private static readonly Color ColorPortal  = new(0.4f, 0.8f,  1f,    1f);
    private static readonly Color ColorShop    = new(1f,   0.9f,  0.2f,  1f);
    private static readonly Color ColorProp    = new(0.6f, 0.6f,  0.6f,  1f);
    private static readonly Color ColorPlayer  = new(1f,   1f,    1f,    1f);

    // ── Private ─────────────────────────────────────────────
    private RectTransform _rect;
    private Image         _image;

    private Transform     _tracked;
    private MinimapView   _owner;

    // 방 범위 참조 (MinimapView가 갱신)
    private Vector3 _roomCenter;
    private Vector2 _roomHalfSize;
    private Vector2 _mapHalfSize;

    public MinimapMarkerType MarkerType { get; private set; }
    public Transform         TrackedTransform => _tracked;

    // ── Lifecycle ────────────────────────────────────────────
    private void Awake()
    {
        _rect  = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
    }

    private void LateUpdate()
    {
        if (_tracked == null)
        {
            _owner?.RemoveMarker(this);
            return;
        }

        UpdatePosition();
    }

    private void OnDestroy()
    {
        _tracked = null;
        _owner   = null;
    }

    // ── Public Methods ───────────────────────────────────────
    public void Initialize(Transform tracked, MinimapMarkerType type, MinimapView owner,
                           Vector3 roomCenter, Vector2 roomHalfSize, Vector2 mapHalfSize)
    {
        _tracked      = tracked;
        _owner        = owner;
        _roomCenter   = roomCenter;
        _roomHalfSize = roomHalfSize;
        _mapHalfSize  = mapHalfSize;
        MarkerType    = type;

        if (_image != null)
            _image.color = ResolveColor(type);

        UpdatePosition();
    }

    public void UpdateBounds(Vector3 roomCenter, Vector2 roomHalfSize, Vector2 mapHalfSize)
    {
        _roomCenter   = roomCenter;
        _roomHalfSize = roomHalfSize;
        _mapHalfSize  = mapHalfSize;
    }

    // ── Private Methods ──────────────────────────────────────
    private void UpdatePosition()
    {
        if (_rect == null || _tracked == null) return;

        Vector3 world = _tracked.position;
        float nx = (_roomHalfSize.x > 0f) ? (world.x - _roomCenter.x) / _roomHalfSize.x : 0f;
        float ny = (_roomHalfSize.y > 0f) ? (world.z - _roomCenter.z) / _roomHalfSize.y : 0f;

        nx = Mathf.Clamp(nx, -1f, 1f);
        ny = Mathf.Clamp(ny, -1f, 1f);

        _rect.anchoredPosition = new Vector2(nx * _mapHalfSize.x, ny * _mapHalfSize.y);
    }

    private static Color ResolveColor(MinimapMarkerType type) => type switch
    {
        MinimapMarkerType.Monster => ColorMonster,
        MinimapMarkerType.Elite   => ColorElite,
        MinimapMarkerType.Boss    => ColorBoss,
        MinimapMarkerType.Portal  => ColorPortal,
        MinimapMarkerType.Shop    => ColorShop,
        MinimapMarkerType.Prop    => ColorProp,
        _                         => ColorPlayer,
    };
}
