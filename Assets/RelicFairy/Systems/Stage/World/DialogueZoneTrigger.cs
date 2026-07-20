using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 콜라이더 진입 시 DialogueSequenceSO를 재생하는 구간별 대사 트리거.
/// BoxCollider(isTrigger=true)와 함께 배치. triggerOnce=true(기본)면 1회만 발동.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class DialogueZoneTrigger : MonoBehaviour
{
    [Header("대사")]
    [SerializeField] private DialogueSequenceSO dialogue;
    [SerializeField] private bool blockPlayerInput = true;
    [SerializeField] private bool triggerOnce = true;

    private bool _triggered;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col))
            col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered && triggerOnce) return;
        if (!other.TryGetComponent<PlayerController>(out var player))
            player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        if (triggerOnce) _triggered = true;
        ShowDialogueAsync(player, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid ShowDialogueAsync(PlayerController player, CancellationToken ct)
    {
        try
        {
            if (blockPlayerInput) player.SetInputEnabled(false);

            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            if (popup != null && dialogue != null)
                await popup.ShowAsync(dialogue);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (blockPlayerInput && player != null)
                player.SetInputEnabled(true);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetComponent<Collider>(out var col)) return;
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
