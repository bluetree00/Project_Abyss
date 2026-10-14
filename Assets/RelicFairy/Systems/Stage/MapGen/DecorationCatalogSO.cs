using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장식(Decoration) 프리팹을 코드 단위로 매핑하는 카탈로그.
/// grid_csv의 d&lt;code&gt; 토큰과 1:1 대응 — code = "t" → trees, code = "f" → fog 등.
///
/// themeMatch 필드로 방 테마별 카탈로그를 분리해 관리할 수 있다.
///   예) Forest 테마 방은 themeMatch="Forest"인 카탈로그 선택,
///       Cave 테마 방은 themeMatch="Cave" 카탈로그 선택.
///   themeMatch가 비어있거나 "*"이면 범용(fallback) 카탈로그로 간주.
/// </summary>
[CreateAssetMenu(menuName = "Map/DecorationCatalog", fileName = "DecorationCatalog_")]
public sealed class DecorationCatalogSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("grid_csv의 d 다음에 오는 문자(들). 예: \"t\", \"p\", \"f\", \"l\", \"m\".")]
        public string code;

        [Tooltip("이 코드에 매핑되는 장식 프리팹.")]
        public GameObject prefab;

        [Tooltip("Y축 배치 오프셋. 나무는 0, 파티클은 0.1~0.5 등 프리팹별 조정.")]
        public float yOffset;

        [Tooltip("켜면 렌더러 바운즈 최저점을 바닥에 자동 정렬(피벗이 메시 중심인 소품이 뜨는 문제 해결). yOffset은 정렬 후 추가 적용.")]
        public bool snapToGround = true;

        [Tooltip("스케일 조정 (1 = 원본 크기).")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("랜덤 Y 회전 적용 — 같은 프리팹 반복 배치 시 단조로움 제거.")]
        public bool randomYRotation = true;

        [Tooltip("X축 점유 셀 수 (기본 1). 2 이상이면 anchor 셀 기준 오른쪽으로 확장.")]
        [Min(1)] public int sizeX = 1;

        [Tooltip("Z축 점유 셀 수 (기본 1). 2 이상이면 anchor 셀 기준 앞쪽으로 확장.")]
        [Min(1)] public int sizeZ = 1;

        [Header("벽 부착 설정 (조명·횃불 등)")]
        [Tooltip("인접한 Wall 타일을 탐지해 방 안쪽을 향해 자동 회전. 횃불/벽등에 사용.")]
        public bool faceNearestWall;

        [Tooltip("벽에서 방 안쪽으로의 수평 오프셋. faceNearestWall이 true일 때 적용.")]
        public float wallInset = 0.45f;

        [Tooltip("벽을 향해 돌린 뒤 더 돌릴 Y 각도(도). 기본 0 = 프리팹 +Z가 방 안쪽.\n" +
                 "Gothic_Interior 벽걸이는 앞뒤가 X축이다 — 창 · 장미창 -90 · 벽 촛대 +90. 0이면 벽에서 직각으로 튀어나온 판이 된다(10-12 Ch3 점검).")]
        [SerializeField] private float wallYawOffset;

        [Tooltip("켜면 배치한 인스턴스의 (트리거 아닌) 콜라이더를 끈다 — 프리팹은 그대로.\n" +
                 "전투 공간 한가운데 놓이는 작은 소품이 대시 · 몬스터 이동을 걸지 않게(10-12 Ch3 점검 D1).")]
        [SerializeField] private bool disableColliders;

        [Tooltip("켜면 배치한 인스턴스의 LODGroup을 LOD0에 고정한다 — 프리팹은 그대로.\n" +
                 "하위 LOD 메시가 깨진 프리팹용(SM_WallCandleabra_01a: LOD1~4에 24 m 떨어진 정점 3개가 면 6~26개를 잡아당긴다 · 10-12 Ch3 점검).")]
        [SerializeField] private bool forceLod0;

        /// <summary>faceNearestWall 회전 뒤 더하는 Y 각도(도).</summary>
        public float WallYawOffset => wallYawOffset;

        /// <summary>배치 뒤 통행을 막는 콜라이더를 끌지.</summary>
        public bool DisableColliders => disableColliders;

        /// <summary>배치 뒤 LOD0에 고정할지.</summary>
        public bool ForceLod0 => forceLod0;
    }

    [Tooltip("방 테마 문자열. 비어있으면 범용 (모든 테마의 fallback).")]
    [SerializeField] private string themeMatch = "*";

    [SerializeField] private List<Entry> entries = new();

    public string ThemeMatch => themeMatch;

    /// <summary>이 카탈로그가 지정 테마에 매칭되는지. "*" 또는 빈값은 항상 매칭.</summary>
    public bool MatchesTheme(string theme)
    {
        if (string.IsNullOrEmpty(themeMatch) || themeMatch == "*") return true;
        if (string.IsNullOrEmpty(theme)) return false;
        return themeMatch.Trim().Equals(theme.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>code에 해당하는 Entry 반환. 없으면 null.</summary>
    public Entry Get(string code)
    {
        if (string.IsNullOrEmpty(code) || entries == null) return null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e != null && e.code == code) return e;
        }
        return null;
    }
}
