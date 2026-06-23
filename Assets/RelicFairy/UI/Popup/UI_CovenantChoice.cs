using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 서약 선택 팝업 — 런 시작 또는 분기 구간에서 서약 목록 중 1개 선택.
///
/// 사용법:
///   var popup = await Managers.UI.ShowPopupUIAndGetAsync&lt;UI_CovenantChoice&gt;();
///   popup.Setup(c0, c1, c2);
///   int chosen = await popup.WaitForChoiceAsync();  // 0·1·2
///   Managers.Instance.GameSession.CovenantHandler.TryAdd(options[chosen].CovenantId);
/// </summary>
public class UI_CovenantChoice : UI_Popup
{
    public override bool BlocksGameplay => true; // 서약 선택 중 시간정지 + 입력잠금

    [Header("제목")]
    [SerializeField] private TMP_Text _titleText;

    [Header("카드 (0·1·2)")]
    [SerializeField] private UI_CovenantCard[] _cards;

    private UniTaskCompletionSource<int> _tcs;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// 표시할 서약 목록을 설정한다. params 배열이므로 1~3개 전달 가능.
    /// 남은 카드 슬롯은 자동으로 숨김 처리된다.
    /// </summary>
    public void Setup(params CovenantBase[] covenants)
    {
        SetText(_titleText, "서약을 선택하세요");
        _tcs = new UniTaskCompletionSource<int>();

        for (int i = 0; i < _cards.Length; i++)
        {
            if (_cards[i] == null) continue;

            bool hasData = i < covenants.Length && covenants[i] != null;
            _cards[i].Bind(hasData ? covenants[i] : null);

            if (!hasData) continue;

            int idx = i;
            _cards[i].SelectButton.onClick.RemoveAllListeners();
            _cards[i].SelectButton.onClick.AddListener(() => CompleteAsync(idx).Forget());
        }
    }

    /// <summary>선택 결과 대기. 반환값은 선택된 카드 인덱스 (0·1·2).</summary>
    public UniTask<int> WaitForChoiceAsync() => _tcs.Task;

    // ── 내부 ─────────────────────────────────────────────────────

    private async UniTaskVoid CompleteAsync(int index)
    {
        foreach (var c in _cards)
            if (c != null) c.SelectButton.interactable = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(1f));

        try
        {
            if (index >= 0 && index < _cards.Length && _cards[index] != null)
                await _cards[index].PlaySelectAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        _tcs?.TrySetResult(index);
        ClosePopupUI();
    }

    private static void SetText(TMP_Text t, string v) { if (t) t.text = v; }
}
