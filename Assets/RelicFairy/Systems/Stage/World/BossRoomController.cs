using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using RelicFairy.UI;
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

    [Tooltip("보스 연출 시작 시 활성화할 벽 오브젝트 배열.")]
    [SerializeField] private GameObject[] introWalls;

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

    [Tooltip("카메라가 바라볼 시선 대상 (bossZoneCenter 기준 월드 좌표 오프셋).")]
    [SerializeField] private Vector3 bossCloseUpLookOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("true 시 카메라 팬을 생략하고 즉시 보스 등장 연출을 시작한다. 연출 내부에서 카메라를 직접 제어하는 경우 사용.")]
    [SerializeField] private bool _skipCameraPan = false;

    [Tooltip("true 시 콜라이더 자동 트리거를 무시하고, 외부(연출 디렉터)의 BeginBossFightExternally 호출로만 전투를 시작한다. 인트로 프롤로그용.")]
    [SerializeField] private bool externalTriggerOnly = false;

    // ── Private ──────────────────────────────────────────────────
    private bool             _triggered;
    private bool             _unbeatable;
    private bool             _playerPassing;
    private Transform        _playerTransform;
    private PlayerController _playerController;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Trigger
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnTriggerEnter(Collider other)
    {
        if (externalTriggerOnly || _triggered) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;
        _playerPassing    = true;
        _playerController = player;
    }

    private void OnTriggerExit(Collider other)
    {
        if (externalTriggerOnly || _triggered || !_playerPassing) return;
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
        player.SetInputEnabled(false);

        if (barrier != null)
            barrier.SetActive(true);

        if (introWalls != null)
            foreach (var wall in introWalls)
                if (wall != null) wall.SetActive(true);

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

    /// <summary>외부 연출(예: IntroMordredDirector)이 상호작용·컷신 뒤 전투를 시작할 때 호출.
    /// 콜라이더 자동 트리거 대신 이 경로로 barrier·소환·등장 연출을 실행한다. unbeatable=true면 보스 HP 바닥(무적).</summary>
    public void BeginBossFightExternally(PlayerController player, bool unbeatable = false)
    {
        if (_triggered || player == null) return;
        _triggered  = true;
        _unbeatable = unbeatable;
        OnPlayerEntered(player);
    }

    private void OnBossSpawned(MonsterBase boss)
    {
        bossSpawner.OnMonsterSpawned -= OnBossSpawned;

        if (_unbeatable && boss != null) boss.HpFloorMin1 = true;

        // 등장 연출(IBossEntrance) 없는 보스(예: ForestGuardian)는 입장 시 차단한 입력을
        // 복구해줄 OnCombatReady 시퀀스가 없다 — 여기서 즉시 복구하지 않으면 플레이어가 영구 이동 불가.
        if (boss is not IBossEntrance entrance)
        {
            _playerController?.SetInputEnabled(true);
            ShowNonEntranceEncounterBark(boss); // 등장 연출 없는 보스도 인카운터 대사는 띄움
            return;
        }

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

    /// <summary>등장 연출(IBossEntrance) 없는 보스의 인카운터 대사 — 타입명에서 키 유도(예: ForestGuardianMonster → ForestGuardian_Encounter).
    /// 방문 변형(GetVisitLines) 적용. 대사 미작성/미로드 시 조용히 스킵(대사 없어도 정상 진행).</summary>
    private static void ShowNonEntranceEncounterBark(MonsterBase boss)
    {
        if (boss == null || UI_BossBark.Instance == null) return;

        string typeName = boss.GetType().Name;
        const string suffix = "Monster";
        if (typeName.EndsWith(suffix)) typeName = typeName.Substring(0, typeName.Length - suffix.Length);

        var lines = Managers.DialogueData?.GetBossEncounterLines($"{typeName}_Encounter");
        if (lines != null && lines.Length > 0)
            UI_BossBark.Show(lines[0].text, BossBarkType.BossIntro);
    }

    private async UniTaskVoid DoCameraAndTriggerAsync(IBossEntrance entrance, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;

        // 카메라 팬 생략 모드: 즉시 수동 제어 전환 후 연출 시작
        if (_skipCameraPan)
        {
            cam?.TakeManualControl();
            entrance.TriggerEntrance();
            return;
        }

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
                    onPanComplete: entrance.TriggerEntrance,
                    customLookOffset: bossCloseUpLookOffset);
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
