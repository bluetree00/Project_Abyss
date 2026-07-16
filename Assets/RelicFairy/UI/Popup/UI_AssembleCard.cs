using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 조립 서약 드래프트 카드(원인/효과 공용). 카드는 요약만 표시(이름·태그·티어) —
/// 상세 수치는 팝업 중앙 완성 미리보기/호버에서 노출한다. ↻ 리롤 버튼과 ?? 봉인 오버레이 지원.
///
/// UX 연출: 등급색 글로우(실버/골드/루비) · 선택 시 스케일 팝 + 글로우 점등 · 호버 살짝 확대.
/// 등급 테두리 아트는 팝업이 SetSkin으로 주입(카드별 개별 배선 불필요).
/// </summary>
public class UI_AssembleCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // ── Constants ────────────────────────────────────────
    private const float SelectScale = 1.06f;
    private const float HoverScale  = 1.03f;
    private const float PopScale    = 1.12f;
    private const float TweenTime   = 0.12f;
    private const float RevealTime  = 0.26f;   // 등장(드래프트/리롤) 연출 길이

    // ── [SerializeField] ─────────────────────────────────
    [SerializeField] private Button     _selectButton;
    [SerializeField] private Button     _rerollButton;
    [SerializeField] private TMP_Text   _nameText;
    [SerializeField] private TMP_Text   _subText;
    [SerializeField] private TMP_Text   _tierText;
    [SerializeField] private Image      _tierFrame;
    [SerializeField] private GameObject _selectedMark;
    [SerializeField] private GameObject _sealedOverlay;

    [Header("연출 (선택)")]
    [SerializeField] private Image _cardBg;   // 테두리 바탕@2x (카드 검은 바탕)
    [SerializeField] private Image _glow;     // 등급색 글로우(선택/호버 시 점등). 없으면 스케일만.

    // ── Private ──────────────────────────────────────────
    // 등급 테두리 아트 — 팝업(UI_CovenantAssemble)이 SetSkin으로 주입.
    private Sprite _silverFrame, _goldFrame, _rubyFrame;
    private Color  _gradeColor = Color.white;
    private bool   _selected, _hover;
    private CancellationTokenSource _tweenCts;

    private bool HasFrameSkin => _silverFrame != null || _goldFrame != null || _rubyFrame != null;

    // ── Properties ───────────────────────────────────────
    public Button SelectButton => _selectButton;
    public Button RerollButton => _rerollButton;

    // ── Lifecycle ────────────────────────────────────────
    private void OnDisable()
    {
        _tweenCts?.Cancel();
        transform.localScale = Vector3.one;
    }

    // ── Public Methods ───────────────────────────────────

    /// <summary>팝업이 공용 등급 테두리 아트를 카드에 주입. Bind 전에 1회 호출.</summary>
    public void SetSkin(Sprite silver, Sprite gold, Sprite ruby, Sprite cardBg)
    {
        _silverFrame = silver;
        _goldFrame   = gold;
        _rubyFrame   = ruby;
        if (cardBg != null && _cardBg != null)
        {
            _cardBg.sprite = cardBg;
            _cardBg.type   = Image.Type.Sliced;
            _cardBg.color  = Color.white;
        }
    }

    public void Bind(string title, string sub, CovenantTier tier, Color tierColor)
    {
        if (_nameText) _nameText.text = title;
        if (_subText)  _subText.text  = sub;
        if (_tierText)
        {
            _tierText.text  = tier.DisplayName();
            _tierText.color = tierColor;   // 등급명도 등급색으로
        }

        _gradeColor = tierColor;

        if (_tierFrame)
        {
            // 스킨 지정 시: 등급별 테두리 아트로 스왑(색 백지). 미지정 시: 기존 색상 방식.
            if (HasFrameSkin)
            {
                var s = FrameFor(tier);
                if (s != null) { _tierFrame.sprite = s; _tierFrame.type = Image.Type.Sliced; _tierFrame.color = Color.white; }
            }
            else
            {
                _tierFrame.color = tierColor;
            }
        }

        if (_glow) _glow.color = new Color(tierColor.r, tierColor.g, tierColor.b, 0f);   // 평시 꺼둠
        SetSealed(false);
        _selected = false;
        RefreshVisual(instant: true);
        RevealAsync(tier).Forget();   // 등급별 등장 연출
    }

    public void SetSelected(bool on)
    {
        if (_selectedMark) _selectedMark.SetActive(on);
        bool wasSelected = _selected;
        _selected = on;
        RefreshVisual(instant: false);
        if (on && !wasSelected) PopAsync().Forget();   // 새로 선택된 카드만 팝
    }

    public void SetSealed(bool on) { if (_sealedOverlay) _sealedOverlay.SetActive(on); }

    public void SetInteractable(bool on)
    {
        if (_selectButton) _selectButton.interactable = on;
        if (_rerollButton) _rerollButton.interactable = on;
    }

    // ── Event Handlers (호버 연출) ────────────────────────
    public void OnPointerEnter(PointerEventData e) { _hover = true;  RefreshVisual(false); }
    public void OnPointerExit(PointerEventData e)  { _hover = false; RefreshVisual(false); }

    // ── Private Methods ──────────────────────────────────
    private Sprite FrameFor(CovenantTier tier) => tier switch
    {
        CovenantTier.Gold => _goldFrame,
        CovenantTier.Ruby => _rubyFrame,
        _                 => _silverFrame,
    };

    /// <summary>선택/호버 상태에 맞춰 글로우·스케일을 갱신.</summary>
    private void RefreshVisual(bool instant)
    {
        float target = _selected ? SelectScale : (_hover ? HoverScale : 1f);
        if (_glow)
        {
            // 카드 전체를 채우는 단색 글로우라 너무 진하면 텍스트가 묻힌다 — 은은한 등급 틴트로.
            float a = _selected ? 0.20f : (_hover ? 0.10f : 0f);
            _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, a);
        }
        if (instant) { transform.localScale = Vector3.one * target; return; }
        TweenScaleAsync(target).Forget();
    }

    private async UniTaskVoid TweenScaleAsync(float target)
    {
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;
        Vector3 from = transform.localScale;
        Vector3 to   = Vector3.one * target;
        float t = 0f;
        try
        {
            while (t < TweenTime)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(t / TweenTime));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = to;
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>선택 순간 살짝 튕기는 팝(over-shoot → 정착).</summary>
    private async UniTaskVoid PopAsync()
    {
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;
        try
        {
            transform.localScale = Vector3.one * PopScale;
            float t = 0f;
            while (t < TweenTime)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(Vector3.one * PopScale, Vector3.one * SelectScale, Mathf.Clamp01(t / TweenTime));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = Vector3.one * SelectScale;
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>드래프트/리롤로 카드가 나타날 때 등급별 등장 연출(높은 등급일수록 팝·글로우 강함).</summary>
    private async UniTaskVoid RevealAsync(CovenantTier tier)
    {
        if (!isActiveAndEnabled) return;
        _tweenCts?.Cancel();
        _tweenCts = new CancellationTokenSource();
        var ct = _tweenCts.Token;

        // 등급별 등장 강도 — 루비가 가장 화려하게.
        float startScale = tier switch { CovenantTier.Ruby => 1.20f, CovenantTier.Gold => 1.14f, _ => 1.09f };
        float peakGlow   = tier switch { CovenantTier.Ruby => 0.85f, CovenantTier.Gold => 0.55f, _ => 0.32f };
        float restScale  = _selected ? SelectScale : 1f;
        float restGlow   = _selected ? 0.20f : 0f;

        try
        {
            float t = 0f;
            while (t < RevealTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RevealTime);
                float ease = 1f - (1f - k) * (1f - k);   // ease-out
                transform.localScale = Vector3.one * Mathf.Lerp(startScale, restScale, ease);
                if (_glow)
                    _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, Mathf.Lerp(peakGlow, restGlow, ease));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.localScale = Vector3.one * restScale;
            if (_glow) _glow.color = new Color(_gradeColor.r, _gradeColor.g, _gradeColor.b, restGlow);
        }
        catch (System.OperationCanceledException) { }
    }
}
