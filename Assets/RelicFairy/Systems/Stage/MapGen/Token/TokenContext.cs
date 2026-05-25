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
}
