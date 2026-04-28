using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 룸 클리어 게이트 — 클리어 시점에 포탈을 등장시키고 행운치 기반으로 ItemSO를 드랍한다.
///
/// 호출 흐름:
///   1) RoomClearController.Initialize → Initialize(run, luckTable)
///   2) RoomClearController.ClearRoomAsync 마지막 단계 → Activate(roomCenter)
///
/// Activate가 수행하는 일:
///   · roomCenter 위치에 포탈을 인스턴스화 (prefab 우선, 없으면 코드 생성 폴백)
///   · LuckRollService.TryRollDrop으로 드롭 여부 추첨
///   · 통과 시 RollRarity → ItemSORegistry.GetByRarity → 무작위 1개 → WorldItemDisplay.SpawnFromData
/// </summary>
public class RoomClearGate : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float ItemDropForwardOffset = -1.5f; // 포탈 앞 1.5m (z 음수 방향)
    private const float DropEffectDurationSec = 0.6f;  // 등장 연출 시간
    private const float DropEffectFallHeight = 1.8f;   // 위에서 떨어지는 시작 높이
    // [임시] 드롭 확률 100% 강제. 추후 LuckRollService.TryRollDrop으로 복구할 때 false로 변경.
    private const bool ForceDropAlways = true;

    // ── [SerializeField] ───────────────────────────────────────
    [Header("Portal")]
    [SerializeField, Tooltip("포탈 prefab. 미할당 시 코드로 임시 큐브 포탈을 생성한다.")]
    private GameObject portalPrefab;

    [Header("Drop Table (옵션 — Initialize에서 주입 권장)")]
    [SerializeField] private LuckRollTableSO luckTable;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private bool _activated;
    private GameObject _spawnedPortal;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>RoomClearController에서 호출. run/luckTable 주입.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table)
    {
        _run = run;
        if (table != null) luckTable = table;
    }

    /// <summary>방 클리어 시점에 호출. 포탈 + 아이템 스폰.</summary>
    public void Activate(Vector3 roomCenterWorld)
    {
        if (_activated) return;
        _activated = true;

        // 1) 포탈 등장
        _spawnedPortal = SpawnPortal(roomCenterWorld);
        Debug.Log($"[RoomClearGate] 포탈 생성 위치={roomCenterWorld}");

        // 2) 아이템 드랍 (확률 추첨)
        Vector3 dropPos = roomCenterWorld + new Vector3(0f, 0f, ItemDropForwardOffset);
        RollAndSpawnItem(dropPos);
    }

    // ── Private Methods ────────────────────────────────────────

    private GameObject SpawnPortal(Vector3 pos)
    {
        if (portalPrefab != null)
        {
            var go = Instantiate(portalPrefab, pos, Quaternion.identity);
            // 트리거 컴포넌트 자동 보장
            if (go.GetComponent<RoomExitTrigger>() == null)
                go.AddComponent<RoomExitTrigger>();
            return go;
        }

        return CreateDefaultPortal(pos);
    }

    /// <summary>포탈 prefab 미할당 시 코드로 임시 큐브 포탈 생성.</summary>
    private GameObject CreateDefaultPortal(Vector3 pos)
    {
        var go = new GameObject("Portal_Temp");
        go.transform.position = pos;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // PrimitiveCube의 자체 콜라이더는 트리거 분리 위해 제거
        var visualCol = visual.GetComponent<Collider>();
        if (visualCol != null) Destroy(visualCol);

        visual.transform.SetParent(go.transform, false);
        visual.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        visual.transform.localScale = new Vector3(2f, 3f, 0.3f);
        visual.name = "Visual";

        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            // URP/HDRP 호환을 위해 sharedMaterial 인스턴스화
            var mat = renderer.material;
            mat.color = new Color(0f, 1f, 1f, 0.7f);
        }

        var trigCol = go.AddComponent<BoxCollider>();
        trigCol.center = new Vector3(0f, 1.5f, 0f);
        trigCol.size = new Vector3(2f, 3f, 2f);
        trigCol.isTrigger = true;

        go.AddComponent<RoomExitTrigger>();
        return go;
    }

    private void RollAndSpawnItem(Vector3 spawnPos)
    {
        if (luckTable == null)
        {
            Debug.LogWarning("[RoomClearGate] luckTable 미할당 — 아이템 드랍 생략");
            return;
        }

        int luck = ResolvePlayerLuck();

        if (!ForceDropAlways && !LuckRollService.TryRollDrop(luck, luckTable))
        {
            Debug.Log($"[RoomClearGate] 드롭 확률 미통과 (luck={luck})");
            return;
        }

        var rarity = LuckRollService.RollRarity(luck, luckTable);
        var candidates = ItemSORegistry.GetByRarity(rarity);
        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning($"[RoomClearGate] 등급 {rarity} 풀 비었음 — 드랍 생략");
            return;
        }

        var itemSO = candidates[Random.Range(0, candidates.Count)];
        var data = RuntimeItemData.FromSO(itemSO);
        if (data == null)
        {
            Debug.LogWarning($"[RoomClearGate] RuntimeItemData 변환 실패 — itemId={itemSO?.itemId}");
            return;
        }

        var display = WorldItemDisplay.SpawnFromData(data, spawnPos, so: itemSO);
        if (display != null)
            PlayDropEffectAsync(display.gameObject, spawnPos).Forget();

        Debug.Log($"[RoomClearGate] 아이템 드랍: {itemSO.itemId} (rarity={rarity}, luck={luck})");
    }

    /// <summary>아이템 등장 연출 — 위에서 떨어지며 스케일 0→1 (ease-out).</summary>
    private static async UniTaskVoid PlayDropEffectAsync(GameObject go, Vector3 endPos)
    {
        if (go == null) return;
        var t = go.transform;
        Vector3 startPos = endPos + new Vector3(0f, DropEffectFallHeight, 0f);
        Vector3 startScale = Vector3.zero;
        Vector3 endScale = t.localScale == Vector3.zero ? Vector3.one : t.localScale;

        t.position = startPos;
        t.localScale = startScale;

        float elapsed = 0f;
        while (elapsed < DropEffectDurationSec)
        {
            if (go == null) return;
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / DropEffectDurationSec);
            float ease = 1f - Mathf.Pow(1f - p, 3f); // ease-out cubic
            t.position = Vector3.Lerp(startPos, endPos, ease);
            t.localScale = Vector3.Lerp(startScale, endScale, ease);
            await UniTask.Yield();
        }

        if (go != null)
        {
            t.position = endPos;
            t.localScale = endScale;
        }
    }

    private int ResolvePlayerLuck()
    {
        var player = _run?.Player;
        if (player == null) return 0;
        var stats = player.RuntimeStats;
        return stats != null ? stats.Luck : 0;
    }
}
