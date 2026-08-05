using UnityEngine;
using RelicFairy.Monster;
using System.Collections.Generic;

/// <summary>
/// Ch2 드래곤 보스 아레나 사망 후처리.
/// - 보스 사망 시 SM_PROXY_Tower3 (9) 비활성화 → Ch3 문 노출
/// - 보스 사망 시 SM_ArchFloor_01c_1 머티리얼 복원 (사망 연출 후 언로드로 인한 투명화 방지)
/// </summary>
public class DragonBossArenaController : MonoBehaviour
{
    [SerializeField] private BossSpawner _bossSpawner;
    [SerializeField] private GameObject  _proxyTower9;
    [SerializeField] private Renderer[]  _floorRenderers;

    private Material[][] _savedFloorMats;

    private void Awake()
    {
        if (_floorRenderers != null)
        {
            _savedFloorMats = new Material[_floorRenderers.Length][];
            for (int i = 0; i < _floorRenderers.Length; i++)
                if (_floorRenderers[i] != null)
                    _savedFloorMats[i] = _floorRenderers[i].sharedMaterials;
        }

        RegisterExitMarker();
    }

    // BossExitPath.ResolveExit 가 아레나에서 "Exit" 이름 Transform을 찾아 출구 방향을 결정한다.
    // SM_PROXY_Tower9 위치(가운데 기둥)에 마커를 씬 로드 시 미리 생성해두면
    // 보스 사망 후 어떤 순서로 이벤트가 발행돼도 항상 가운데로 길이 뻗는다.
    private void RegisterExitMarker()
    {
        if (_proxyTower9 == null) return;

        // 아레나 중심 = 바닥 렌더러 경계 중심, 없으면 이 컴포넌트 위치로 대체
        Vector3 arenaCenter = transform.position;
        if (_floorRenderers != null)
            foreach (var r in _floorRenderers)
                if (r != null) { arenaCenter = r.bounds.center; break; }

        var outDir = Vector3.ProjectOnPlane(
            _proxyTower9.transform.position - arenaCenter, Vector3.up);
        if (outDir.sqrMagnitude < 0.01f) return;

        var exitMarker = new GameObject("Exit");
        exitMarker.transform.SetParent(_proxyTower9.transform);
        exitMarker.transform.position = _proxyTower9.transform.position;
        exitMarker.transform.forward  = outDir.normalized;
    }

    private void OnEnable()
    {
        if (_bossSpawner != null)
            _bossSpawner.OnMonsterSpawned += OnBossSpawned;
    }

    private void OnDisable()
    {
        if (_bossSpawner != null)
            _bossSpawner.OnMonsterSpawned -= OnBossSpawned;
    }

    private void OnBossSpawned(MonsterBase boss)
    {
        if (boss != null)
            boss.OnDied += OnBossDied;
    }

    private void OnBossDied(MonsterBase boss)
    {
        boss.OnDied -= OnBossDied;

        if (_proxyTower9 != null)
            _proxyTower9.SetActive(false);

        if (_floorRenderers != null && _savedFloorMats != null)
            for (int i = 0; i < _floorRenderers.Length; i++)
                if (_floorRenderers[i] != null && _savedFloorMats[i] != null)
                    _floorRenderers[i].sharedMaterials = _savedFloorMats[i];

        KillRemainingMiniDragons();
    }

    private static void KillRemainingMiniDragons()
    {
        var minis = new List<DragonMiniDragon>(Object.FindObjectsByType<DragonMiniDragon>(FindObjectsSortMode.None));
        foreach (var mini in minis)
            mini.ForceKill();
    }
}
