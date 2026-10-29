using UnityEngine;
using UnityEngine.UI;

/// <summary>마커 종류 — 넘기는 곳은 몬스터·보스뿐이다(플레이어 점은 MinimapView의 Img_PlayerMarker).
/// 0·2·4·5·6(Player·Elite·Portal·Shop·Prop)은 쓰는 곳이 없어 걷었다(09-28) — 번호는 다시 쓰지 않는다.</summary>
public enum MinimapMarkerType
{
    Monster  = 1,
    Boss     = 3,
}

/// <summary>
/// 미니맵 마커 단위 컴포넌트.
/// MinimapView가 마커를 생성한 후 Initialize()로 세팅.
/// LateUpdate에서 TrackedTransform의 월드 좌표를 판 좌표로 옮긴다(<see cref="MinimapView.ToMap"/> — 판의 위 = 카메라가 보는 쪽).
/// </summary>
public sealed class MinimapMarker : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private static readonly Color ColorMonster = new(1f,   0.25f, 0.25f, 1f);
    private static readonly Color ColorBoss    = new(0.8f, 0f,    0f,    1f);

    // ── Private ─────────────────────────────────────────────
    private RectTransform _rect;
    private Image         _image;

    private Transform     _tracked;
    private MinimapView   _owner;

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
    public void Initialize(Transform tracked, MinimapMarkerType type, MinimapView owner)
    {
        _tracked   = tracked;
        _owner     = owner;
        MarkerType = type;

        if (_image != null)
            _image.color = ResolveColor(type);

        UpdatePosition();
    }

    // ── Private Methods ──────────────────────────────────────
    private void UpdatePosition()
    {
        if (_rect == null || _tracked == null || _owner == null) return;
        _rect.anchoredPosition = _owner.ToMap(_tracked.position);
    }

    private static Color ResolveColor(MinimapMarkerType type) =>
        type == MinimapMarkerType.Boss ? ColorBoss : ColorMonster;
}
