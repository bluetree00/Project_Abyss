using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 환전소 창(11-02 Q18) — 환율 줄마다 [1번] [5번] [전부] · 위에 지금 가진 골드 · 원석 · 강화재료. 누르는 즉시 바꾼다(<see cref="RunExchange"/>).
/// 코드로 짓는 덮개 — 시작 선물과 같은 규약(열려 있는 동안 시간 멈춤 · 플레이어 입력 끔 · [닫기] · Esc). 서비스 방 창(정렬 400) 위에도 뜬다(RunChoice 970).
/// </summary>
public static class ExchangePopupView
{
    private const float RowH = 62f;
    private static readonly object s_timeOwner = new();

    /// <summary>창이 열려 있나 — 여는 쪽(상호작용)이 겹쳐 열지 않게 본다.</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    public static async UniTask OpenAsync(GameRunSession run, CancellationToken outer)
    {
        if (IsOpen || run == null) return;
        var (go, root, group) = UICodeOverlay.Create("@Exchange", UISortingOrder.RunChoice);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer, go.GetCancellationTokenOnDestroy());
        var ct = cts.Token;
        // 상점 · 재련소 · 정제소 창 위에서 열면 그 창이 이미 입력을 막고 있다 — 그때는 플레이어 입력을 건드리지 않는다(닫을 때 켜 버리면 창 뒤에서 움직였다)
        var player = UIInputGate.Blocked ? null : run.Player;

        TimeScaleArbiter.Acquire(s_timeOwner, 0f, TimeScaleArbiter.Priority.Pause);
        if (player != null) player.SetInputEnabled(false);   // 단추를 누르는 클릭이 공격으로 새지 않게
        IsOpen = true;
        bool close = false;
        try
        {
            UICodeOverlay.Img(root, "Veil", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f), stretch: true);
            var panel = (RectTransform)UICodeOverlay.Img(root, "Panel", new Vector2(0f, 0f), new Vector2(900f, 640f), new Color(0.06f, 0.06f, 0.08f, 0.96f)).transform;
            UICodeOverlay.Line(panel, new Color(UIPalette.Gold.r, UIPalette.Gold.g, UIPalette.Gold.b, 0.55f));
            UICodeOverlay.Txt(panel, "환전소", 34f, new Color(0.96f, 0.90f, 0.72f), new Vector2(0f, 270f), 800f);
            UICodeOverlay.Txt(panel, "손해를 보고 다른 재화로 바꾼다 — 누르는 즉시 바뀐다", 19f, UICodeOverlay.Mist, new Vector2(0f, 228f), 800f);
            var wallet = UICodeOverlay.Txt(panel, "", 22f, UICodeOverlay.Ink, new Vector2(0f, 182f), 820f);

            var rates = RunExchange.Rates;
            var buttons = new Button[rates.Length, 3];
            for (int i = 0; i < rates.Length; i++)
            {
                var r = rates[i];
                float y = 118f - i * RowH;
                UICodeOverlay.Img(panel, "Row", new Vector2(0f, y), new Vector2(820f, RowH - 8f), new Color(1f, 1f, 1f, i % 2 == 0 ? 0.03f : 0.015f)).raycastTarget = false;
                UICodeOverlay.Txt(panel, $"{RunExchange.Name(r.From)} {r.Give}  →  {RunExchange.Name(r.To)} {r.Get}", 22f, UICodeOverlay.Ink,
                                  new Vector2(-200f, y), 400f, align: TextAlignmentOptions.MidlineLeft);
                int idx = i;
                buttons[i, 0] = UICodeOverlay.Btn(panel, "1번", new Vector2(150f, y), new Vector2(86f, 40f), 18f, () => Do(run, idx, 1));
                buttons[i, 1] = UICodeOverlay.Btn(panel, "5번", new Vector2(246f, y), new Vector2(86f, 40f), 18f, () => Do(run, idx, 5));
                buttons[i, 2] = UICodeOverlay.Btn(panel, "전부", new Vector2(342f, y), new Vector2(86f, 40f), 18f, () => Do(run, idx, int.MaxValue));
            }
            UICodeOverlay.Btn(panel, "닫기", new Vector2(0f, -272f), new Vector2(200f, 46f), 20f, () => close = true);

            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.2f; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            group.alpha = 1f;

            int g = -1, o = -1, m = -1;
            while (!close && !Input.GetKeyDown(KeyCode.Escape))
            {
                int g1 = RunExchange.Balance(run, RunExchange.Currency.Gold), o1 = RunExchange.Balance(run, RunExchange.Currency.RuneOre), m1 = RunExchange.Balance(run, RunExchange.Currency.EnhanceMaterial);
                if (g1 != g || o1 != o || m1 != m)   // 가진 양이 바뀔 때만 다시 쓴다
                {
                    g = g1; o = o1; m = m1;
                    wallet.text = $"{Loc.T("가진 것")}   {RunExchange.Name(RunExchange.Currency.Gold)} {g}   ·   {RunExchange.Name(RunExchange.Currency.RuneOre)} {o}   ·   {RunExchange.Name(RunExchange.Currency.EnhanceMaterial)} {m}";
                    for (int i = 0; i < rates.Length; i++)
                    {
                        int can = RunExchange.MaxTimes(run, rates[i]);
                        buttons[i, 0].interactable = can >= 1;
                        buttons[i, 1].interactable = can >= 5;
                        buttons[i, 2].interactable = can >= 1;
                    }
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);
            for (float t = 0f; t < 0.15f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.15f; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            IsOpen = false;
            TimeScaleArbiter.Release(s_timeOwner);
            if (player != null) player.SetInputEnabled(true);
            if (go != null) Object.Destroy(go);
        }
    }

    private static void Do(GameRunSession run, int rateIndex, int times)
    {
        if (RunExchange.Trade(run, RunExchange.Rates[rateIndex], times) > 0)
            Managers.Sound?.PlayUiEvent(SoundEvent.UiConfirm);
    }
}
