using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 보스방 입장 감지 + 연출 제어.
///
/// 흐름: 플레이어가 입장 Trigger 통과
///       → 배리어 닫힘 + BossSpawner.Trigger() 호출
///       → 보스가 플레이어를 감지(OnEntranceRequested 발행)
///       → 플레이어 입력 차단
///       → 카메라 클로즈업 팬(보스 방향) + 홀드 + 복귀
///       → boss.TriggerEntrance() 호출 → Appear 연출 시작
///       → OnCombatReady 발행 → 플레이어 입력 복구
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
    [SerializeField, Min(0.1f)] private float holdDuration = 2.0f;

    [Tooltip("카메라가 플레이어에게 돌아오는 시간 (초).")]
    [SerializeField, Min(0.1f)] private float returnDuration = 1.0f;

    [Tooltip("보스 클로즈업 카메라 오프셋 (bossZoneCenter 기준 월드 좌표).\n" +
             "z 음수 = 보스 앞에서 바라봄. 방 배치에 따라 조정.")]
    [SerializeField] private Vector3 bossCloseUpOffset = new Vector3(0f, 3f, -7f);

    // ── Private ──────────────────────────────────────────────────
    private bool             _triggered;
    private bool             _playerPassing;
    private Transform        _playerTransform;
    private PlayerController _playerController;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Trigger
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _playerPassing = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (_triggered || !_playerPassing) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        _triggered     = true;
        _playerPassing = false;
        OnPlayerEntered(player);
    }

    private void OnPlayerEntered(PlayerController player)
    {
        _playerTransform  = player.transform;
        _playerController = player;

        if (barrier != null)
            barrier.SetActive(true);

        // 이미 스폰된 보스가 있으면 소급 연결, 없으면 이벤트 구독 후 소환
        if (bossSpawner.SpawnedBoss != null)
        {
            OnBossSpawned(bossSpawner.SpawnedBoss);
        }
        else
        {
            bossSpawner.OnMonsterSpawned += OnBossSpawned;
            bossSpawner?.Trigger();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스 감지 → 입력 차단 → 카메라 팬 → TriggerEntrance → 입력 복구
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnBossSpawned(MonsterBase boss)
    {
        bossSpawner.OnMonsterSpawned -= OnBossSpawned;

        if (boss is not IBossEntrance entrance) return;

        // 전투 준비 완료 시 플레이어 입력 복구 (단발)
        Action combatReadyHandler = null;
        combatReadyHandler = () =>
        {
            entrance.OnCombatReady -= combatReadyHandler;
            if (this == null) return;
            _playerController?.SetInputEnabled(true);
        };
        entrance.OnCombatReady += combatReadyHandler;

        // 보스 감지 시 입력 차단 + 카메라 팬 (단발)
        Action entranceHandler = null;
        entranceHandler = () =>
        {
            entrance.OnEntranceRequested -= entranceHandler;
            if (this == null) return;
            _playerController?.SetInputEnabled(false);
            DoCameraAndTriggerAsync(entrance, destroyCancellationToken).Forget();
        };
        entrance.OnEntranceRequested += entranceHandler;
    }

    private async UniTaskVoid DoCameraAndTriggerAsync(IBossEntrance entrance, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;
        if (cam != null && bossZoneCenter != null)
        {
            try
            {
                // onPanComplete: 줌인 도달 직후 Appear 연출 시작 → 카메라가 줌된 상태에서 보스 등장이 보임
                await cam.PanToZoneAndReturnAsync(
                    bossZoneCenter.position,
                    panDuration,
                    holdDuration,
                    returnDuration,
                    _playerTransform,
                    ct,
                    bossCloseUpOffset,
                    onPanComplete: entrance.TriggerEntrance);
            }
            catch (OperationCanceledException)
            {
                _playerController?.SetInputEnabled(true);
                return;
            }
        }
        else
        {
            try { await UniTask.Delay(TimeSpan.FromSeconds(panDuration), cancellationToken: ct); }
            catch (OperationCanceledException)
            {
                _playerController?.SetInputEnabled(true);
                return;
            }
            entrance.TriggerEntrance();
            try { await UniTask.Delay(TimeSpan.FromSeconds(holdDuration), cancellationToken: ct); }
            catch (OperationCanceledException) { }
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

            // 클로즈업 카메라 위치 미리보기
            Gizmos.color = Color.cyan;
            Vector3 camPreview = bossZoneCenter.position + bossCloseUpOffset;
            Gizmos.DrawLine(bossZoneCenter.position, camPreview);
            Gizmos.DrawWireSphere(camPreview, 0.5f);
        }
    }
}
