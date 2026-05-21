using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 선택 → 플랫폼 상승 → 맵 스폰 → 체크포인트 활성화를 순서대로 조율하는 시퀀서.
///
/// 흐름:
/// 1. OpenRoomAsync(roomIndex) 호출
/// 2. BarrierVolume 파문 이펙트
/// 3. RoomPlatform.RiseAsync() — 플랫폼이 배리어 위로 솟아오름
/// 4. SpawnBlockMapAsync() — 맵 블록 생성 (GameRunBootstrapper 위임)
/// 5. DissolveEntrance 재생
/// 6. CheckpointZone.SetReady() — 이후 플레이어 진입 시 세이브 발동
/// 7. OnRoomOpened 이벤트 발생
/// </summary>
public class RoomOpenSequencer : MonoBehaviour
{
    // ─────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────

    [Header("배리어")]
    [SerializeField] private BarrierVolume _barrierVolume;

    [Header("레이아웃 SO")]
    [SerializeField] private ChapterLayoutSO _layout;

    // ─────────────────────────────────────────
    // Private Fields
    // ─────────────────────────────────────────

    private readonly Dictionary<int, RoomPlatform>   _platforms       = new();
    private readonly Dictionary<int, CheckpointZone> _checkpoints     = new();
    private CancellationTokenSource _cts;

    // ─────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────

    public bool IsOpening { get; private set; }

    // ─────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────

    /// <summary>방 오픈 시퀀스가 완전히 끝난 뒤 발생. int = 열린 방 인덱스.</summary>
    public event Action<int> OnRoomOpened;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void OnDestroy() => _cts?.Cancel();

    // ─────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────

    /// <summary>씬 초기화 시 플랫폼·체크포인트를 등록한다.</summary>
    public void RegisterPlatform(RoomPlatform platform, CheckpointZone checkpoint)
    {
        _platforms[platform.RoomIndex]   = platform;
        _checkpoints[platform.RoomIndex] = checkpoint;
    }

    /// <summary>레이아웃 SO를 런타임에 주입한다 (ChapterWorldBuilder 호출).</summary>
    public void SetLayout(ChapterLayoutSO layout) => _layout = layout;

    /// <summary>
    /// 지정 방을 열어 플레이어가 이동할 수 있는 상태로 만든다.
    /// 이미 오픈 중이라면 무시된다.
    /// </summary>
    public async UniTask OpenRoomAsync(int roomIndex, int saveSlotIndex, CancellationToken externalCt = default)
    {
        if (IsOpening) return;

        var nodeData = _layout?.GetRoom(roomIndex);
        if (nodeData == null)
        {
            Debug.LogWarning($"[RoomOpenSequencer] roomIndex={roomIndex} 데이터 없음");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(
            externalCt,
            this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

        IsOpening = true;
        try
        {
            await RunSequenceAsync(roomIndex, nodeData, saveSlotIndex, ct);
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning($"[RoomOpenSequencer] 방 {roomIndex} 오픈 취소됨");
        }
        finally
        {
            IsOpening = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ─────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────

    private async UniTask RunSequenceAsync(int roomIndex, RoomNodeData nodeData, int saveSlotIndex, CancellationToken ct)
    {
        // ── Step 1: 수면 파문 이펙트 ──────────────────────
        await PlayRippleEffectAsync(nodeData.worldCenter, ct);

        // ── Step 2: 플랫폼 상승 ───────────────────────────
        if (_platforms.TryGetValue(roomIndex, out var platform))
        {
            await platform.RiseAsync(
                _layout.PlatformRiseDuration,
                _layout.PlatformRiseCurve,
                ct);
        }

        // ── Step 3: 맵 블록 스폰 (GameRunBootstrapper 위임) ──
        await SpawnRoomMapAsync(nodeData, ct);

        // ── Step 4: 체크포인트 활성화 ─────────────────────
        if (_checkpoints.TryGetValue(roomIndex, out var checkpoint))
            checkpoint.SetReady(saveSlotIndex);

        // ── Step 5: 완료 알림 ─────────────────────────────
        OnRoomOpened?.Invoke(roomIndex);
    }

    private async UniTask PlayRippleEffectAsync(Vector3 worldCenter, CancellationToken ct)
    {
        if (_barrierVolume == null || string.IsNullOrEmpty(_barrierVolume.RippleEffectKey)) return;

        try
        {
            // 배리어 수면 위치에서 파문 이펙트 스폰
            var go = await Managers.AddressableManager.InstantiateAsync(_barrierVolume.RippleEffectKey);
            if (go != null)
                go.transform.position = new Vector3(worldCenter.x, _layout?.BarrierSurfaceY ?? -0.1f, worldCenter.z);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Debug.LogWarning($"[RoomOpenSequencer] 파문 이펙트 로드 실패: {e.Message}");
        }

        // 파문이 최고조에 달할 때까지 대기 (플랫폼 상승 직전)
        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), cancellationToken: ct);
    }

    private async UniTask SpawnRoomMapAsync(RoomNodeData nodeData, CancellationToken ct)
    {
        var bootstrapper = GameRunBootstrapper.Instance;
        if (bootstrapper == null) return;

        await bootstrapper.SpawnBlockMapAtAsync(nodeData.roomDataKey, nodeData.worldCenter, ct);
    }
}
