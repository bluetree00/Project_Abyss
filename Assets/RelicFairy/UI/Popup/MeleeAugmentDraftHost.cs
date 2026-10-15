using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 증강 고르기 창을 여는 쪽(10-15) — <see cref="MeleeAugmentDraft.Offered"/>를 앱 내내 듣는다
/// (듣는 쪽이 있으면 Draft는 왼쪽 카드로 자동 결정하지 않는다 — 04 설계).
/// 재련소에서 강화가 성공하면 Offered는 결과가 확정되는 순간(망치 연출 전) 온다 — 재련소 연출이 끝나고 결과가 읽힌 뒤 연다(판정이 새지 않게).
/// 쉼터 벼리기는 연출이 없어 곧바로 연다. 창이 열려 있으면 다음 화면은 창이 스스로 이어 그린다.
/// 앱 시작 때 한 번 세운다(씬을 넘어 산다 — ZoneTitleBanner와 같은 길).
/// </summary>
public sealed class MeleeAugmentDraftHost : MonoBehaviour
{
    // ── Constants ──
    private const float AfterReveal = 0.35f;   // 재련소 결과 도장이 읽힐 틈

    // ── Private ──
    private bool _opening;

    // ── Static ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<MeleeAugmentDraftHost>() != null) return;
        var go = new GameObject("MeleeAugmentDraftHost");
        DontDestroyOnLoad(go);
        go.AddComponent<MeleeAugmentDraftHost>();
    }

    // ── Lifecycle ──
    private void OnEnable()  => MeleeAugmentDraft.Offered += HandleOffered;
    private void OnDisable() => MeleeAugmentDraft.Offered -= HandleOffered;

    // ── Event Handlers ──
    private void HandleOffered(MeleeAugmentOffer offer)
    {
        if (_opening || UI_MeleeAugmentPopup.Current != null) return;
        OpenAsync().Forget();
    }

    private async UniTaskVoid OpenAsync()
    {
        _opening = true;
        try
        {
            var ct = destroyCancellationToken;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 재련소가 연출을 시작할 틈(Offered는 강화 확정 안에서 온다)
            await UniTask.WaitUntil(() => !UI_CruciblePanel.Revealing, PlayerLoopTiming.Update, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(AfterReveal), ignoreTimeScale: true, cancellationToken: ct);
            if (MeleeAugmentDraft.Pending == null || Managers.UI == null) return;
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_MeleeAugmentPopup>();
            if (popup == null) { MeleeAugmentDraft.AutoChoose(); return; }   // 창을 못 열면 예전처럼 왼쪽 카드
            popup.Show(MeleeAugmentDraft.Pending);
        }
        catch (OperationCanceledException) { }
        finally { _opening = false; }
    }
}
