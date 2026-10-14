using UnityEngine;

/// <summary>
/// 보스 클리어 출구 계단(<see cref="ChapterStairway"/>)의 리소스 묶음 — 10-10 사용자 「보스를 잡은 뒤 포탈 · 계단이 초라하다 —
/// 계단이 느낌 있게 생성되고 올라가는 느낌 · 제대로 된 리소스」. 렌더 비교(바탕화면 기획/보스클리어_계단_후보_20261010/) 뒤 권장안:
/// 디딤판 = Gothic 돌 계단 판(StairStoneKit_02a~c) · 꼭대기 문 = 돌 아치(StoneArch_01a) · 꼭대기 바닥 = 다음 챕터 도착 마법진(대기방과 짝).
/// Addressable 키 <see cref="Address"/> — 메뉴 「RelicFairy/Setup/10-10 보스 클리어 계단 세트 만들기」가 만들고 등록한다. 정적 데이터만.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Run/Stairway Set", fileName = "StairwaySet")]
public sealed class StairwaySetSO : ScriptableObject
{
    public const string Address = "Run/StairwaySet";

    [Header("디딤판 · 아치")]
    [SerializeField] private GameObject[] _stepSlabs;
    [SerializeField] private GameObject   _arch;
    [SerializeField] private float        _archScale = 1.2f;   // 3.8 m — 1.45(4.5 m)는 층계참에 서면 화면을 덮었다(10-10 실측)

    [Header("꼭대기 마법진 — 챕터 1~4(도착 대기방과 같은 짝)")]
    [SerializeField] private GameObject[] _chapterCircles = new GameObject[4];
    [SerializeField] private float        _circleScale = 0.5f;

    public GameObject[] StepSlabs   => _stepSlabs;
    public GameObject   Arch        => _arch;
    public float        ArchScale   => _archScale;
    public float        CircleScale => _circleScale;

    /// <summary>챕터(1~4)의 마법진. 없으면 null.</summary>
    public GameObject CircleFor(ChapterId chapter)
    {
        int i = (int)chapter - 1;
        return _chapterCircles != null && i >= 0 && i < _chapterCircles.Length ? _chapterCircles[i] : null;
    }

#if UNITY_EDITOR
    /// <summary>에디터 메뉴가 채운다.</summary>
    public void EditorAssign(GameObject[] slabs, GameObject arch, GameObject[] circles, float archScale)
    {
        _archScale      = archScale;
        _stepSlabs      = slabs;
        _arch           = arch;
        _chapterCircles = circles;
    }
#endif
}
