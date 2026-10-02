using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 정제소 방 런타임 컨트롤러 (NPC + 정제대).
///
/// 역할: 비전투 방(상점/재련소와 동일 흐름). NPC 상호작용 → 정제소 패널(<see cref="UI_RefineryPanel"/>)을 연다.
/// 뽑은 룬은 패널이 룬판으로 넘긴다. 방 특전은 09-28 정제소 단순화(무작위 뽑기)로 걷었다.
///
/// 결정성: _roomRng는 소품 배치에만 쓴다(전투/보상 롤 없음).
/// </summary>
public sealed class RefineryRoomController : MonoBehaviour
{
    private const float NpcStandHeight = 1f;   // 캡슐 바닥(발)이 지면에 닿도록 — 앵커 · 폴백 모두.

    /// <summary>NPC 앞 정제대까지의 거리(m) — NPC가 제단 뒤에 선 구도.</summary>
    private const float CounterDistance = 2.5f;

    /// <summary>정제사 잡담 — 수정 정령(10-01 NPC 교체). 짧고 멀리서 울리는 말. 원석에서 무엇이 나올지 모르는 뽑기 컨셉.</summary>
    private static readonly string[] ChatterLines =
    {
        "…원석 속에서 무언가 울린다.",
        "응축은 기다림이다. 빛은 서두르지 않는다.",
        "같은 돌에서 같은 룬은 두 번 나오지 않는다.",
        "판을 채워라. 힘은 서로를 부른다.",
        "돌은 거짓말을 하지 않는다.",
    };

    private GameRunSession _run;
    private System.Random  _roomRng;
    private GameObject[]   _decorPrefabs;
    private GameObject     _npcInstance;
    private ShopNpcInteraction _npc;
    private ServiceNpcReactor _reactor;   // 다가오면 빛이 오르고 · 정제하고 나가면 응축으로 답한다
    private int _craftsAtOpen;            // 패널을 열 때 정제 횟수 — 닫을 때 늘었으면 정제한 것
    private bool _initialized;
    private bool _uiOpen;

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

        var (npcPos, npcRot) = ResolveNpcPlacement();
        SpawnNpc(npcPrefab, npcPos, npcRot);
        SpawnDecor(npcPos, npcRot);

        _initialized = true;
        Debug.Log("[Refinery] 초기화 완료(NPC+정제소).");
    }

    // ── NPC ────────────────────────────────────────────────
    private (Vector3 pos, Quaternion rot) ResolveNpcPlacement()
    {
        // 앵커는 바닥 높이 점 — 서는 높이를 더한다(안 더해 NPC·정제대가 1.1 m 박혔다, 09-29).
        var anchor = GetComponentInChildren<ShopNpcAnchor>(true);
        if (anchor != null)
            return (ServiceRoomDecorPlacer.NpcStandPoint(anchor.transform.position, NpcStandHeight), anchor.transform.rotation);

        Vector3 pos = transform.position;
        pos.y += NpcStandHeight;
        // 고정 +Z 대신 가장 트인 쪽 — 벽을 보고 서거나 정제대가 벽에 박히는 것을 방지.
        return (pos, ServiceRoomDecorPlacer.ResolveFacing(pos, Quaternion.identity));
    }

    private void SpawnNpc(GameObject npcPrefab, Vector3 pos, Quaternion rot)
    {
        if (npcPrefab == null)
        {
            Debug.LogWarning("[Refinery] NPC 프리팹 없음 — 정제소를 열 수 없습니다.");
            return;
        }

        _npcInstance = Instantiate(npcPrefab, pos, rot, transform);
        _npc = _npcInstance.GetComponent<ShopNpcInteraction>();
        if (_npc == null) _npc = _npcInstance.GetComponentInChildren<ShopNpcInteraction>(true);

        if (_npc != null) _npc.OnInteract += HandleNpcInteract;
        else Debug.LogWarning("[Refinery] NPC 프리팹에 ShopNpcInteraction 없음");
        _npcInstance.TryGetComponent(out _reactor);

        // 주기적 월드스페이스 잡담 — 정제소 컨셉(원석·속성 응축).
        _npcInstance.AddComponent<NpcAmbientChatter>()
                    .Initialize(ChatterLines, 9f, new Color(0.62f, 0.85f, 1f), _npc != null ? _npc.ChatterHeight : null);
    }

    private void HandleNpcInteract()
    {
        if (_uiOpen) return;   // 연타로 같은 팝업이 겹쳐 쌓이는 것을 막는다(상점/재련소와 동일 규약)
        OpenRefineryAsync().Forget();
    }

    /// <summary>정제소 패널을 연다.</summary>
    private async UniTaskVoid OpenRefineryAsync()
    {
        var svc = _run?.Refinery;
        if (svc == null) { Debug.LogWarning("[Refinery] 정제소 서비스 없음(런 미시작)"); return; }

        _uiOpen = true;
        if (_npc != null) _npc.SetInteractable(false);
        _craftsAtOpen = svc.CraftCount;
        if (_reactor != null) _reactor.BeginTalk();

        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null)
            {
                Debug.LogWarning("[Refinery] UI_RefineryPanel 로드 실패");
                HandlePanelClosed();
                return;
            }
            panel.OnClosed += HandlePanelClosed;   // 팝업은 닫힐 때 파괴되므로 해제는 불필요
        }
        catch (OperationCanceledException) { HandlePanelClosed(); }
    }

    /// <summary>UI_RefineryPanel이 닫힐 때 호출 — 중복 오픈 가드 해제.</summary>
    private void HandlePanelClosed()
    {
        // 팝업의 OnDestroy가 이 이벤트를 쏘므로 씬 언로드/종료 때도 불린다.
        // 그때는 이 컨트롤러가 이미 파괴돼 있을 수 있어, 그대로 진행하면 MissingReferenceException 이 난다.
        if (this == null) return;

        _uiOpen = false;
        if (_npc != null) _npc.SetInteractable(true);
        if (_reactor != null) _reactor.EndTalk((_run?.Refinery?.CraftCount ?? 0) > _craftsAtOpen);
    }

    // ── 소품 배치 (재련소와 동일 규약: 첫 소품=정제대(정면), 나머지=NPC 뒤 반원) ───────
    private void SpawnDecor(Vector3 npcPos, Quaternion npcRot)
    {
        if (_decorPrefabs == null || _decorPrefabs.Length == 0) return;

        ServiceRoomDecorPlacer.SyncPhysics();   // 갓 생성된 벽 콜라이더를 쿼리에 반영

        // grid_csv가 무대를 지정했으면(NC/NP 토큰) 그대로 쓴다. 없으면 아래 탐색 배치로 폴백.
        if (ServiceRoomDecorPlacer.TryPlaceFromAnchors(transform, _decorPrefabs, npcPos,
                                                       npcPos.y - NpcStandHeight, "RefineryCounter"))
            return;

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
