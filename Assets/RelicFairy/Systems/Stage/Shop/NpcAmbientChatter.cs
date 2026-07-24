using TMPro;
using UnityEngine;

/// <summary>
/// NPC 머리 위 월드스페이스 잡담 말풍선 — 일정 시간마다 대사를 한 줄씩 띄운다.
/// 상점·재련소·정제소 NPC가 공유한다(서비스 방 3종 동일 규약).
///
/// 프리팹 불필요(TextMeshPro 절차 생성, GambleBoxChallenge/OnboardingGuideArrow와 동일 패턴).
/// 대사는 <see cref="Initialize"/>로 주입 — 방 종류별 컨셉 문구를 컨트롤러가 넘긴다.
/// </summary>
public sealed class NpcAmbientChatter : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float DefaultInterval = 9f;    // 대사 간격(초)
    private const float DefaultHold     = 4.5f;  // 한 대사가 떠 있는 시간(초)
    private const float FadeTime        = 0.35f;
    private const float HeadOffsetY     = 2.35f; // NPC 머리 위
    private const float VisibleRange    = 22f;   // 이 거리 밖이면 표시하지 않음(멀리서 글자만 둥둥 방지)

    // ── Private fields ───────────────────────────────────
    private string[]    _lines;
    private float       _interval = DefaultInterval;
    private float       _timer;
    private float       _holdLeft;
    private int         _index;
    private bool        _shown;
    private TextMeshPro _text;
    private Transform   _camT;
    private Transform   _player;
    private Color       _baseColor = new Color(0.94f, 0.92f, 0.85f);

    // ── Lifecycle ────────────────────────────────────────
    private void Start()
    {
        _camT   = Camera.main != null ? Camera.main.transform : null;
        _player = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        _timer  = _interval * 0.35f;   // 방 입장 직후 한 번 빨리 말하게
    }

    private void Update()
    {
        if (_lines == null || _lines.Length == 0 || _text == null) return;

        // 멀리 있으면 숨기고 타이머도 멈춘다(성능 + 시야 정리).
        if (_player != null &&
            (_player.position - transform.position).sqrMagnitude > VisibleRange * VisibleRange)
        {
            if (_shown) HideNow();
            return;
        }

        Billboard();

        if (_shown)
        {
            _holdLeft -= Time.deltaTime;
            // 표시 구간 양끝을 페이드 — 톡 튀어나오지 않게.
            float a = Mathf.Clamp01(Mathf.Min(DefaultHold - _holdLeft, _holdLeft) / FadeTime);
            SetAlpha(a);
            if (_holdLeft <= 0f) HideNow();
            return;
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f) ShowNext();
    }

    // ── Public Methods ───────────────────────────────────
    /// <summary>대사 목록·간격 주입. 컨트롤러가 NPC 스폰 직후 호출한다.</summary>
    public void Initialize(string[] lines, float interval = DefaultInterval, Color? tint = null)
    {
        _lines    = lines;
        _interval = Mathf.Max(2f, interval);
        if (tint.HasValue) _baseColor = tint.Value;

        if (_text == null) CreateText();
        HideNow();
    }

    // ── Private Methods ──────────────────────────────────
    private void CreateText()
    {
        var go = new GameObject("NpcChatter");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, HeadOffsetY, 0f);

        _text = go.AddComponent<TextMeshPro>();
        _text.fontSize  = 3.2f;
        _text.alignment = TextAlignmentOptions.Center;
        _text.color     = _baseColor;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.sortingOrder = 12;
        TMPOutlineHelper.ApplyDefault(_text);
    }

    private void ShowNext()
    {
        _text.text = _lines[_index % _lines.Length];
        _index++;
        _shown    = true;
        _holdLeft = DefaultHold;
        _text.gameObject.SetActive(true);
        SetAlpha(0f);
    }

    private void HideNow()
    {
        _shown = false;
        _timer = _interval;
        if (_text != null) _text.gameObject.SetActive(false);
    }

    private void SetAlpha(float a)
    {
        var c = _baseColor; c.a = a;
        _text.color = c;
    }

    private void Billboard()
    {
        if (_camT == null) _camT = Camera.main != null ? Camera.main.transform : null;
        if (_camT != null && _text != null) _text.transform.rotation = _camT.rotation;
    }
}
