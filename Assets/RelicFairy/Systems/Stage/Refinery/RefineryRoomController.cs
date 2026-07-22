using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 정제소 방 런타임 컨트롤러 (NPC + 룬판).
///
/// 역할: 룬(아이템) 지급/관리 스테이션. 비전투 방(상점/재련소와 동일 흐름).
/// NPC 상호작용 → 룬판(<see cref="UI_GridPanel"/>)을 연다. 지급 로직은 후속(임의 내용 단계).
///
/// 결정성: _roomRng는 소품 배치에만 쓴다(전투/보상 롤 없음).
/// </summary>
public sealed class RefineryRoomController : MonoBehaviour
{
    private const float NpcStandHeight = 1f;   // 앵커 없는 폴백 스폰 시 캡슐 바닥이 지면에 닿도록.

    private GameRunSession _run;
    private System.Random  _roomRng;
    private GameObject[]   _decorPrefabs;
    private GameObject     _npcInstance;
    private ShopNpcInteraction _npc;
    private bool _initialized;

    /// <summary>이 방의 특전(입장 시 결정성 롤로 1종). 방은 "좋은 조건으로 정제하는 곳".</summary>
    private RefineryPerk _perk = RefineryPerk.None;

    private void OnDestroy()
    {
        if (_npc != null) _npc.OnInteract -= HandleNpcInteract;
    }

    /// <summary>Initialize 전에 호출 — NPC 주변에 배치할 룬 소품·VFX 프리팹.</summary>
    public void SetDecorPrefabs(GameObject[] prefabs) => _decorPrefabs = prefabs;

    /// <param name="npcPrefab">정제소 NPC(Addressable). null이면 NPC 없이 방만 존재.</param>
    public void Initialize(GameRunSession run, System.Random roomRng = null, GameObject npcPrefab = null)
    {
        if (_initialized) { Debug.LogWarning("[Refinery] 이미 초기화됨"); return; }

        _run     = run;
        _roomRng = roomRng ?? new System.Random();

        // 방 특전 결정 — 같은 시드면 같은 특전(결정성 유지).
        _perk = (RefineryPerk)(1 + _roomRng.Next(0, 3));   // Discount / Lucky / FirstFree

        var (npcPos, npcRot) = ResolveNpcPlacement();
        SpawnNpc(npcPrefab, npcPos, npcRot);
        SpawnDecor(npcPos, npcRot);

        _initialized = true;
        Debug.Log($"[Refinery] 초기화 완료(NPC+정제소). 방 특전={_perk}");
    }

    // ── NPC ────────────────────────────────────────────────
    private (Vector3 pos, Quaternion rot) ResolveNpcPlacement()
    {
        var anchor = GetComponentInChildren<ShopNpcAnchor>(true);
        if (anchor != null)
            return (anchor.transform.position, anchor.transform.rotation);

        Vector3 pos = transform.position;
        pos.y += NpcStandHeight;
        return (pos, Quaternion.identity);
    }

    private void SpawnNpc(GameObject npcPrefab, Vector3 pos, Quaternion rot)
    {
        if (npcPrefab == null)
        {
            Debug.LogWarning("[Refinery] NPC 프리팹 없음 — 룬판을 열 수 없습니다.");
            return;
        }

        _npcInstance = Instantiate(npcPrefab, pos, rot, transform);
        _npc = _npcInstance.GetComponent<ShopNpcInteraction>();
        if (_npc == null) _npc = _npcInstance.GetComponentInChildren<ShopNpcInteraction>(true);

        if (_npc != null) _npc.OnInteract += HandleNpcInteract;
        else Debug.LogWarning("[Refinery] NPC 프리팹에 ShopNpcInteraction 없음");
    }

    private void HandleNpcInteract() => OpenRefineryAsync().Forget();

    /// <summary>정제소 패널을 방 특전과 함께 연다. 특전은 패널이 닫힐 때 해제된다(상시 탭엔 안 붙음).</summary>
    private async UniTaskVoid OpenRefineryAsync()
    {
        var svc = _run?.Refinery;
        if (svc == null) { Debug.LogWarning("[Refinery] 정제소 서비스 없음(런 미시작)"); return; }

        svc.SetRoomPerk(_perk);

        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null)
            {
                Debug.LogWarning("[Refinery] UI_RefineryPanel 로드 실패");
                svc.ClearRoomPerk();
            }
        }
        catch (OperationCanceledException) { svc.ClearRoomPerk(); }
    }

    // ── 소품 배치 (재련소와 동일 규약: NPC 뒤 반원 + 바닥 스냅) ───────
    private void SpawnDecor(Vector3 npcPos, Quaternion npcRot)
    {
        if (_decorPrefabs == null || _decorPrefabs.Length == 0) return;

        Vector3 back  = npcRot * Vector3.back;
        Vector3 right = npcRot * Vector3.right;
        int n = _decorPrefabs.Length;
        for (int i = 0; i < n; i++)
        {
            var prefab = _decorPrefabs[i];
            if (prefab == null) continue;

            float t     = n > 1 ? (float)i / (n - 1) : 0.5f;
            float ang   = Mathf.Lerp(-70f, 70f, t) * Mathf.Deg2Rad;
            float rad   = 3.5f + (float)_roomRng.NextDouble() * 1.2f;
            Vector3 dir = back * Mathf.Cos(ang) + right * Mathf.Sin(ang);
            Vector3 pos = npcPos + dir * rad;
            pos.y = npcPos.y - NpcStandHeight;

            float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), transform);

            // 바닥 스냅 — 피벗이 메시 중심인 소품이 뜨지 않게. VFX(렌더러 없음/파티클만)는 원위치 유지.
            var rends = go.GetComponentsInChildren<MeshRenderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int r = 1; r < rends.Length; r++) b.Encapsulate(rends[r].bounds);
                go.transform.position += new Vector3(0f, pos.y - b.min.y, 0f);
            }
        }
    }
}
