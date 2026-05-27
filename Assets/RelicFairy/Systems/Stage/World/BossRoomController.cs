using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스방 입장 감지 + 연출 제어.
///
/// 흐름: 플레이어가 입장 Trigger 통과
///       → 배리어 닫힘
///       → 카메라 팬(보스 방향) + 홀드 + 복귀
///       → BossSpawner.Trigger() 호출
///
/// Trigger Collider(isTrigger=true)가 이 GameObject에 붙어 있어야 한다.
/// BossSpawner.waitForExternalTrigger = true로 설정해두면 이 컨트롤러가 소환 시점을 제어한다.
/// </summary>
public class BossRoomController : MonoBehaviour
{
    [Header("연결 컴포넌트")]
    [Tooltip("보스 소환을 담당하는 BossSpawner. waitForExternalTrigger=true 필수.")]
    [SerializeField] private BossSpawner bossSpawner;

    [Tooltip("입장 직후 활성화할 배리어 오브젝트. null이면 배리어 없음.")]
    [SerializeField] private GameObject barrier;

    [Header("카메라 팬 설정")]
    [Tooltip("카메라가 이동할 목표 지점 (보스 주변 Transform).")]
    [SerializeField] private Transform bossZoneCenter;

    [Tooltip("카메라가 목표로 이동하는 시간 (초).")]
    [SerializeField, Min(0.1f)] private float panDuration = 1.5f;

    [Tooltip("목표 지점에서 카메라가 머무는 시간 (초).")]
    [SerializeField, Min(0.1f)] private float holdDuration = 1.5f;

    [Tooltip("카메라가 플레이어에게 돌아오는 시간 (초).")]
    [SerializeField, Min(0.1f)] private float returnDuration = 1.0f;

    // ── Private ──────────────────────────────────────────────────
    private bool _triggered;
    private bool _playerPassing; // Enter 이후 Exit 전까지 true — 문을 통과 중

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Trigger
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 플레이어가 트리거에 들어오면 통과 중 플래그만 세팅 — 배리어는 아직 닫지 않음
    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _playerPassing = true;
    }

    // 플레이어가 트리거를 완전히 빠져나가야(=문 통과 완료) 배리어 닫힘 + 연출 시작
    private void OnTriggerExit(Collider other)
    {
        if (_triggered || !_playerPassing) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        _triggered    = true;
        _playerPassing = false;
        OnPlayerEntered(player);
    }

    private void OnPlayerEntered(PlayerController player)
    {
        if (barrier != null)
            barrier.SetActive(true);

        RunSequenceAsync(player.transform, destroyCancellationToken).Forget();
    }

    private async UniTaskVoid RunSequenceAsync(Transform playerTransform, CancellationToken ct)
    {
        // 카메라 팬과 보스 소환을 동시에 시작한다.
        // BossSpawner.spawnDelay == panDuration 이면 카메라가 보스 앞에 도착하는 순간
        // Appear 연출이 시작되어 holdDuration 동안 연출을 관람할 수 있다.
        bossSpawner?.Trigger();

        var cam = GameCameraController.Instance;
        if (cam != null && bossZoneCenter != null)
        {
            try
            {
                await cam.PanToZoneAndReturnAsync(
                    bossZoneCenter.position,
                    panDuration,
                    holdDuration,
                    returnDuration,
                    playerTransform,
                    ct);
            }
            catch (OperationCanceledException) { return; }
        }
        else
        {
            try { await UniTask.Delay(TimeSpan.FromSeconds(panDuration + holdDuration), cancellationToken: ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Gizmos
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnDrawGizmosSelected()
    {
        if (bossZoneCenter != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
            Gizmos.DrawLine(transform.position, bossZoneCenter.position);
            Gizmos.DrawWireSphere(bossZoneCenter.position, 2f);
        }
    }
}
