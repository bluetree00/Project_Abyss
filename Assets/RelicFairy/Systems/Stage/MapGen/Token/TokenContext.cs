using System.Threading;
using UnityEngine;

/// <summary>
/// 토큰 핸들러에 전달되는 실행 컨텍스트.
/// TokenParser가 셀별로 RawToken, Cell, WorldPos를 채우고,
/// 공유 데이터(Theme, DecorationCatalogs 등)는 호출자가 미리 주입한다.
/// </summary>
public class TokenContext
{
    // ── 셀 정보 (TokenParser가 셀마다 설정) ───────────────
    public string RawToken;
    public Vector2Int Cell;
    public Vector3 WorldPos;

    // ── 공간 정보 (TokenParser 생성 시 설정) ──────────────
    public Transform Parent;
    public float CellSize;
    public float BaseY;

    // ── 방/존 메타 (호출자가 주입) ─────────────────────────
    public string Theme;
    public MapRoomEntry RoomEntry;

    // ── 서비스 레퍼런스 (호출자가 주입) ───────────────────
    public DecorationCatalogSO[] DecorationCatalogs;
    public BlockPalette ActivePalette;
    public GameObject[] CharacterPickupPrefabs;
    public GameObject[] WeaponPickupPrefabs;
    public CancellationToken Ct;

    // ── 스포너 핸들러용 (PreBuild 페이즈에서 사용) ────────
    /// <summary>ApplyMonsterSpawnerPlan 적용 후 그리드. MonsterSpawnHandler가 활성 여부 판단에 사용.</summary>
    public TileType[,] Grid;
    /// <summary>MapDataLoader.Parse가 채운 셀별 스폰 설정. MonsterSpawnHandler가 Configure에 사용.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<Vector2Int, MapDataLoader.CellSpawnInfo> SpawnInfos;
    /// <summary>비활성화된 스포너 목록. 입장 연출 후 또는 존 진입 시 re-enable됨.</summary>
    public System.Collections.Generic.List<UnityEngine.MonoBehaviour> DeferredSpawners;

    /// <summary>방 걷기셀들의 월드 AABB(벽/구멍 안쪽으로 inset). TokenParser가 1회 계산.
    /// MonsterSpawner가 이 경계 안으로만 스폰해 게이트/복도로 새는 것을 막는다. null = 미계산(현행 동작).</summary>
    public Bounds? FieldBounds;
}
