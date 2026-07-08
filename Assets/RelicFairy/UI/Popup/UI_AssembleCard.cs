using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조립 서약 드래프트 카드(원인/효과 공용). 카드는 요약만 표시(이름·태그·티어) —
/// 상세 수치는 팝업 중앙 완성 미리보기/호버에서 노출한다. ↻ 리롤 버튼과 ?? 봉인 오버레이 지원.
/// </summary>
public class UI_AssembleCard : MonoBehaviour
{
    // ── [SerializeField] ─────────────────────────────────
    [SerializeField] private Button     _selectButton;
    [SerializeField] private Button     _rerollButton;
    [SerializeField] private TMP_Text   _nameText;
    [SerializeField] private TMP_Text   _subText;
    [SerializeField] private TMP_Text   _tierText;
    [SerializeField] private Image      _tierFrame;
    [SerializeField] private GameObject _selectedMark;
    [SerializeField] private GameObject _sealedOverlay;

    // ── Properties ───────────────────────────────────────
    public Button SelectButton => _selectButton;
    public Button RerollButton => _rerollButton;

    // ── Public Methods ───────────────────────────────────
    public void Bind(string title, string sub, CovenantTier tier, Color tierColor)
    {
        if (_nameText)  _nameText.text  = title;
        if (_subText)   _subText.text   = sub;
        if (_tierText)  _tierText.text  = tier.DisplayName();
        if (_tierFrame) _tierFrame.color = tierColor;
        SetSealed(false);
    }

    public void SetSelected(bool on)     { if (_selectedMark)  _selectedMark.SetActive(on); }
    public void SetSealed(bool on)       { if (_sealedOverlay) _sealedOverlay.SetActive(on); }

    public void SetInteractable(bool on)
    {
        if (_selectButton) _selectButton.interactable = on;
        if (_rerollButton) _rerollButton.interactable = on;
    }
}
