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

    /// <summary>NPC 앞 정제대까지의 거리(m) — NPC가 제단 뒤에 선 구도.</summary>
    private const float CounterDistance = 2.5f;

    /// <summary>정제사 잡담 — 원석/존핵 룬 컨셉.</summary>
    private static readonly string[] ChatterLines =
    {
        "원석 속에 잠든 속성을 깨워주지.",
        "판을 채우면… 힘이 공명한다.",
        "존핵은 아무렇게나 벼려지지 않아.",
        "이번엔 어떤 속성을 응축할까?",
        "돌은 거짓말을 하지 않는다.",
    };

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
        // 고정 +Z 대신 가장 트인 쪽 — 벽을 보고 서거나 정제대가 벽에 박히는 것을 방지.
        return (pos, ServiceRoomDecorPlacer.ResolveFacing(pos, Quaternion.identity));
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

        // 주기적 월드스페이스 잡담 — 정제소 컨셉(원석·속성 응축).
        _npcInstance.AddComponent<NpcAmbientChatter>()
                    .Initialize(ChatterLines, 9f, new Color(0.62f, 0.85f, 1f));
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

    // ── 소품 배치 (재련소와 동일 규약: 첫 소품=정제대(정면), 나머지=NPC 뒤 반원) ───────
    private void SpawnDecor(Vector3 npcPos, Quaternion npcRot)
    {
        if (_decorPrefabs == null || _decorPrefabs.Length == 0) return;

        ServiceRoomDecorPlacer.SyncPhysics();   // 갓 생성된 벽 콜라이더를 쿼리에 반영

        Vector3 fwd   = npcRot * Vector3.forward;   // NPC가 바라보는 방향(=플레이어 쪽)
        Vector3 back  = -fwd;
        Vector3 right = npcRot * Vector3.right;
        float groundY = npcPos.y - NpcStandHeight;

        // 1) 정제대 — NPC 앞. 벽이면 빈 자리를 탐색(못 찾으면 생략).
        if (_decorPrefabs[0] != null &&
            ServiceRoomDecorPlacer.TryFindSpot(npcPos, fwd, CounterDistance, groundY, out var tablePos))
        {
            Vector3 faceBack = npcPos - tablePos;
            float tableYaw = Mathf.Atan2(-faceBack.x, -faceBack.z) * Mathf.Rad2Deg;
            ServiceRoomDecorPlacer.Place(_decorPrefabs[0], tablePos, tableYaw, groundY, transform, "RefineryCounter");
        }

        // 2) 나머지 — NPC 뒤쪽 반원
        int n = _decorPrefabs.Length;
        for (int i = 1; i < n; i++)
        {
            var prefab = _decorPrefabs[i];
            if (prefab == null) continue;

            float t     = n > 2 ? (float)(i - 1) / (n - 2) : 0.5f;
            float ang   = Mathf.Lerp(-70f, 70f, t) * Mathf.Deg2Rad;
            float rad   = 3.5f + (float)_roomRng.NextDouble() * 1.2f;
            Vector3 dir = back * Mathf.Cos(ang) + right * Mathf.Sin(ang);

            if (!ServiceRoomDecorPlacer.TryFindSpot(npcPos, dir, rad, groundY, out var pos)) continue;

            Vector3 toNpc = npcPos - pos;
            float yaw = Mathf.Atan2(-toNpc.x, -toNpc.z) * Mathf.Rad2Deg;
            ServiceRoomDecorPlacer.Place(prefab, pos, yaw, groundY, transform);
        }
    }
}
