using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 닿으면 서약 3지선다 선택 팝업을 띄우는 픽업(트리거, F 아님). GameRunSession이 없는 씬(베이스캠프)에 배치한다.
/// 선택한 서약은 PlayerLoadout에 "예약"되고, 던전 진입(StartRoomGate.ExitStartRoomAsync, 핸들러 Initialize 후)
/// 시 CovenantHandler.TryAdd로 적용된다. 선택 UI는 던전/이벤트방의 WorldCovenantPickup과 공유(CovenantChoiceUI).
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CovenantPickup : MonoBehaviour
{
    private const int CandidateCount = 3;

    [SerializeField, Tooltip("후보에 반드시 포함할 서약 id(선택). 비우면 전부 랜덤. (CovenantFactory 상수: galahad/morrigan/arthur 등)")]
    private string covenantId;

    [SerializeField, Tooltip("획득 시 일회성 VFX(선택, Addressable 키)")]
    private string acquireVfxKey;

    private bool _busy;
    private bool _taken;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_taken || _busy) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        ChooseAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid ChooseAsync(CancellationToken ct)
    {
        _busy = true;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) { _busy = false; return; }

        string[] ids = BuildCandidates(loadout.ReservedCovenants);
        if (ids.Length == 0) { _busy = false; return; }

        string selectedId = await CovenantChoiceUI.ChooseAsync(ids, ct);

        // 취소 — 미획득, 오브젝트 유지(재진입 시 재시도)
        if (string.IsNullOrEmpty(selectedId)) { _busy = false; return; }

        _taken = true;
        loadout.AddCovenant(selectedId);
        Debug.Log($"[CovenantPickup] 서약 예약: {selectedId}");

        if (!string.IsNullOrEmpty(acquireVfxKey))
            ItemEffectVfxHelper.SpawnOneShotAt(acquireVfxKey, transform.position);

        if (this != null && gameObject != null)
            Destroy(gameObject);
    }

    /// <summary>예약분을 제외한 후보 id 배열. covenantId가 지정되어 있고 미예약이면 반드시 포함한다.</summary>
    private string[] BuildCandidates(IReadOnlyList<string> reserved)
    {
        var exclude = new HashSet<string>(reserved);
        var ids = new List<string>(CandidateCount);

        if (!string.IsNullOrEmpty(covenantId) && !exclude.Contains(covenantId))
        {
            ids.Add(covenantId);
            exclude.Add(covenantId);
        }

        int remaining = CandidateCount - ids.Count;
        if (remaining > 0)
            ids.AddRange(WorldCovenantPickup.PickRandomOptionsExcluding(exclude, remaining));

        return ids.ToArray();
    }
}
