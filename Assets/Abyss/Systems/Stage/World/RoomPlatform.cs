using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public enum PlatformState { Submerged, Rising, Active }

/// <summary>
/// 방 하나에 대응하는 지형 플랫폼.
///
/// 초기 상태: sinkDepth만큼 배리어(수면/용암 등) 아래에 잠겨 있음.
/// 방 선택 시: RiseAsync()로 수면 위 activeY까지 솟아오름.
/// 완료 시: OnRiseComplete 이벤트 발생 → RoomOpenSequencer가 맵 생성을 시작.
/// </summary>
public class RoomPlatform : MonoBehaviour
{
    // ─────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────

    [Header("방 식별")]
    [SerializeField] private int _roomIndex;

    [Header("플랫폼 설정")]
    [Tooltip("배리어 아래로 가라앉는 깊이 (m). ChapterLayoutSO.sinkDepth와 동기화 권장.")]
    [SerializeField] private float _sinkDepth = 2f;

    // ─────────────────────────────────────────
    // Private Fields
    // ─────────────────────────────────────────

    private Vector3 _activePosition;   // 수면 위 목표 위치 (Awake에서 캡처)

    // ─────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────

    public int           RoomIndex { get; private set; }
    public PlatformState State     { get; private set; } = PlatformState.Submerged;

    // ─────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────

    /// <summary>플랫폼이 완전히 수면 위로 솟아오른 직후 발생.</summary>
    public event Action<RoomPlatform> OnRiseComplete;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void Awake()
    {
        RoomIndex       = _roomIndex;
        _activePosition = transform.position;
        ApplySubmergedPosition();
    }

    // ─────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────

    /// <summary>즉시 배리어 아래로 가라앉힌다. 씬 초기화·챕터 리셋 시 사용.</summary>
    public void SubmergeImmediate()
    {
        ApplySubmergedPosition();
        State = PlatformState.Submerged;
    }

    /// <summary>
    /// 플랫폼을 배리어 아래에서 수면 위로 솟아오르게 한다.
    /// duration과 curve는 ChapterLayoutSO에서 주입받는다.
    /// </summary>
    public async UniTask RiseAsync(float duration, AnimationCurve curve, CancellationToken ct)
    {
        if (State != PlatformState.Submerged) return;
        State = PlatformState.Rising;

        var startY  = transform.position.y;
        var targetY = _activePosition.y;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            elapsed += Time.deltaTime;

            var t   = Mathf.Clamp01(elapsed / duration);
            var pos = transform.position;
            pos.y   = Mathf.LerpUnclamped(startY, targetY, curve.Evaluate(t));
            transform.position = pos;

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        // 최종 위치 보정
        var finalPos = transform.position;
        finalPos.y   = targetY;
        transform.position = finalPos;

        State = PlatformState.Active;
        OnRiseComplete?.Invoke(this);
    }

    /// <summary>RoomIndex를 런타임에 재설정한다. ChapterWorldBuilder가 플랫폼 풀을 재활용할 때 사용.</summary>
    public void SetRoomIndex(int index) => RoomIndex = index;

    // ─────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────

    private void ApplySubmergedPosition()
    {
        var pos = _activePosition;
        pos.y = _activePosition.y - _sinkDepth;
        transform.position = pos;
    }
}
