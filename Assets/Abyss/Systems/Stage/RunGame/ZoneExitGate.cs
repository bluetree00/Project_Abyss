using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [Deprecated] 기능이 StartRoomGate로 통합됨.
/// ZoneProgressionService 및 CreateZoneExitGates는 더 이상 이 컴포넌트를 사용하지 않는다.
/// 기존 프리팹 참조 보존을 위해 파일을 유지한다.
/// </summary>
public class ZoneExitGate : MonoBehaviour
{
    private const float TriggerRadius   = 3f;
    private const float IndicatorHeight = 2.5f;
    private const float CanvasScale     = 0.006f;
    private const float GracePeriod     = 0.3f; // 활성화 직후 즉시 발사 방지

    // ── Private fields ─────────────────────────────────────────────────
    private int                    _fromZoneIndex;
    private int                    _toZoneIndex;
    private string                 _targetLabel;
    private ZoneProgressionService _progression;
    private bool                   _triggered;
    private bool                   _isEnabled;
    private float                  _enabledTime = float.MaxValue;
    private SphereCollider         _col;

    private GameObject       _worldIndicatorGO;
    private TextMeshProUGUI  _distanceText;

    // ── Init ───────────────────────────────────────────────────────────

    /// <summary>SpawnZoneByIndexAsync에서 호출. from/to 존 인덱스·라벨·progression 주입 + SphereCollider 설정.</summary>
    public void InitGate(int fromZoneIndex, int toZoneIndex, string targetLabel, ZoneProgressionService progression)
    {
        _fromZoneIndex = fromZoneIndex;
        _toZoneIndex   = toZoneIndex;
        _targetLabel   = string.IsNullOrEmpty(targetLabel) ? "다음 구역" : targetLabel;
        _progression   = progression;

        // Collider를 런타임으로 생성 — 프리팹에 없어도 동작
        if (!TryGetComponent<SphereCollider>(out _col))
            _col = gameObject.AddComponent<SphereCollider>();
        _col.isTrigger = true;
        _col.radius    = TriggerRadius;
    }

    /// <summary>존 클리어(또는 비전투 존 진입) 시 호출. GO를 활성화하고 월드 인디케이터를 생성한다.</summary>
    public void EnableGate()
    {
        if (_isEnabled || _triggered) return; // 중복 활성화 방지
        _isEnabled   = true;
        _enabledTime = Time.time;
        gameObject.SetActive(true);
        CreateWorldIndicator();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────

    private void Update()
    {
        if (_worldIndicatorGO == null || _triggered) return;

        // 빌보드 — 카메라를 향해 회전
        var cam = Camera.main;
        if (cam != null)
            _worldIndicatorGO.transform.rotation = cam.transform.rotation;

        // 거리 갱신
        if (_distanceText == null) return;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        float dist = Vector3.Distance(transform.position, player.transform.position);
        _distanceText.text = $"{Mathf.RoundToInt(dist)}m";
    }

    private void OnDestroy()
    {
        if (_worldIndicatorGO != null) Destroy(_worldIndicatorGO);
    }

    // ── Trigger ────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other) => TryActivate(other);

    // 활성화 직후 플레이어가 이미 범위 안에 있을 때도 처리
    private void OnTriggerStay(Collider other)  => TryActivate(other);

    // ── Private ────────────────────────────────────────────────────────

    private void TryActivate(Collider other)
    {
        if (_triggered) return;
        if (Time.time - _enabledTime < GracePeriod) return; // 즉시 발사 방지
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _triggered = true;
        if (_worldIndicatorGO != null)
            _worldIndicatorGO.SetActive(false);

        ActivateNextZoneAsync().Forget();
    }

    private async UniTaskVoid ActivateNextZoneAsync()
    {
        if (_progression == null)
        {
            Debug.LogWarning("[ZoneExitGate] ZoneProgressionService 없음 — 다음 존 활성화 불가");
            return;
        }

        var ct = gameObject.GetCancellationTokenOnDestroy();
        try
        {
            await _progression.DirectlyEnterZoneAsync(_fromZoneIndex, _toZoneIndex, ct);
        }
        catch (System.OperationCanceledException) { }

        Destroy(gameObject);
    }

    // ── World Indicator ────────────────────────────────────────────────

    private void CreateWorldIndicator()
    {
        _worldIndicatorGO = new GameObject("GateIndicator");
        _worldIndicatorGO.transform.SetParent(transform, false);
        _worldIndicatorGO.transform.localPosition = Vector3.up * IndicatorHeight;

        var canvas = _worldIndicatorGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var rt       = _worldIndicatorGO.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(200f, 70f);
        rt.localScale = Vector3.one * CanvasScale;

        // 배경
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.12f, 0.88f);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // 메인 라벨 "다음 구역"
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text      = _targetLabel;
        labelTMP.fontSize  = 24f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color     = new Color(1f, 0.88f, 0.35f);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin        = new Vector2(0f, 0.45f);
        labelRT.anchorMax        = new Vector2(1f, 1f);
        labelRT.offsetMin        = new Vector2(6f, 0f);
        labelRT.offsetMax        = new Vector2(-6f, -2f);

        // 거리 텍스트
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
