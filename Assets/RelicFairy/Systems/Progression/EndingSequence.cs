using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 엔딩 — 악몽기 리치를 처음 쓰러뜨린 런의 보스 클리어 직후에 한 번 튼다.
/// 리치의 죽음(대사창) → 모르가나 정체 공개(대사창) → 엔딩 카드 · 크레딧 → (호출부) 귀환 → 거점 에필로그.
///
/// 대사가 CSV에 없으면 그 단계만 건너뛴다. 끝나면 화면은 검게 덮인 채로 돌아온다(귀환 연출이 이어받는다).
/// 설계: 기획 「리치보스_완전설계」 §4-7 · 「최종장이후_사이클시나리오」 v2 §4 L5.
/// </summary>
public static class EndingSequence
{
    public const string LichDeathKey = "Lich_Death";
    public const string MorganaKey   = "Ending_Morgana";
    public const string CardKey      = "Ending_Card";
    public const string CreditsKey   = "Ending_Credits";

    public static async UniTask PlayAsync(CancellationToken ct)
    {
        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        player?.SetInputEnabled(false);
        try
        {
            UnityEngine.Debug.Log("[Ending] 엔딩 연출 시작");
            await ShowDialogueAsync(dlg.GetLines(LichDeathKey), ct);
            await ShowDialogueAsync(dlg.GetLines(MorganaKey), ct);
            await EndingCardOverlay.PlayAsync(dlg.GetLines(CardKey), dlg.GetLines(CreditsKey), ct);
            UnityEngine.Debug.Log("[Ending] 엔딩 연출 끝");
        }
        finally
        {
            player?.SetInputEnabled(true);
        }
    }

    private static async UniTask ShowDialogueAsync(DialogueLine[] lines, CancellationToken ct)
    {
        if (lines == null || lines.Length == 0) return;

        // 종료(플레이 정지 · 앱 종료) 중이면 UI가 없다 — 앞 대사창이 파괴로 끝난 경우.
        var ui = Managers.UI;
        if (ui == null) return;
        await ui.WaitUntilNoBlockingPopupAsync();
        ct.ThrowIfCancellationRequested();

        var popup = await ui.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup != null) await popup.ShowAsync(lines);
        ct.ThrowIfCancellationRequested();
    }
}
