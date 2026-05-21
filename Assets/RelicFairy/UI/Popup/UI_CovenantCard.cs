using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_CovenantCard : MonoBehaviour
{
    [Header("아이콘 / 이름")]
    [SerializeField] private Image    _icon;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _loreText;
    [SerializeField] private TMP_Text _basicDesc;

    [Header("버튼")]
    [SerializeField] private Button _selectButton;

    [Header("이펙트")]
    [SerializeField] private Image _flashOverlay;

    public Button SelectButton => _selectButton;

    private static readonly Regex NumPattern = new Regex(@"\d+(\.\d+)?%?");

    public void Bind(CovenantBase covenant)
    {
        if (covenant == null) { gameObject.SetActive(false); return; }

        gameObject.SetActive(true);
        if (_icon != null)
        {
            _icon.sprite = covenant.Icon;
            _icon.color  = covenant.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        }
        if (_nameText)  _nameText.text  = covenant.DisplayName;
        if (_loreText)  _loreText.text  = covenant.LoreText;
        if (_basicDesc) _basicDesc.text = HighlightNumbers(covenant.BasicDescription);
    }

    public async UniTask PlaySelectAsync(CancellationToken ct)
    {
        if (_flashOverlay == null) return;
        try
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.08f)
            {
                _flashOverlay.color = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 0.6f, t));
                await UniTask.NextFrame(ct);
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.24f)
            {
                _flashOverlay.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.6f, 0f, t));
                await UniTask.NextFrame(ct);
            }
            _flashOverlay.color = new Color(1f, 1f, 1f, 0f);
        }
        catch (System.OperationCanceledException)
        {
            if (_flashOverlay != null) _flashOverlay.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    // 숫자·% 를 금색 볼드로 강조
    private static string HighlightNumbers(string text) =>
        NumPattern.Replace(text ?? string.Empty,
            m => $"<color=#FFD060><b>{m.Value}</b></color>");
}
