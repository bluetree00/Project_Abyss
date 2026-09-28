using UnityEngine;

/// <summary>
/// 서약 조립 팝업(<see cref="UI_CovenantAssemble"/>) 아트 슬롯.
/// 디자이너 개편본(바탕화면 "서약 UI 리소스", 메모: 양피지 + 등급 테두리 추가)과 1:1.
///
/// 미할당 슬롯은 무시되어 프리팹에 이미 꽂힌 구 아트가 그대로 남는다.
/// Addressable 키 "UI/CovenantSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ <b>등급 테두리는 조각 조립식이다</b> — 가로 장식바(위·아래)와 모서리 조각(네 귀퉁이)을
///   카드 크기에 맞춰 코드가 배치한다. 한 장짜리 액자가 아니라서 9-slice로는 안 된다.
/// ※ 납품본은 7등급(철·그린·블루·보라·루비·골드·빛)인데 <see cref="CovenantTier"/>는 3단계뿐이다.
///   현재는 철→Silver / 골드→Gold / 루비→Ruby만 배선하고 나머지 4종은 대기시킨다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Covenant Skin", fileName = "CovenantSkin")]
public sealed class CovenantSkinSO : ScriptableObject
{
    [Header("판")]
    [Tooltip("서약서 전체 배경(양피지 두루마리). 원본 비율 1292:919를 지켜야 말린 양끝이 안 찌그러진다.")]
    public Sprite panelBg;
    [Tooltip("중앙 결과 두루마리 — 세로로 펼친 양피지.")]
    public Sprite resultScroll;

    [Header("열 머리표 (글자가 아트에 구워져 있다)")]
    public Sprite headerCause;    // 원인
    public Sprite headerResult;   // 결과
    public Sprite headerEffect;   // 효과

    [Header("카드 바탕")]
    public Sprite cardIdle;       // 비선택 바탕(밝은 양피지)
    public Sprite cardSelected;   // 선택 바탕(어두운 판)

    // 바탕 아트는 오른쪽·아래로 그림자를 23px 품고 있다(알파 100→0). 등급 테두리를 카드 rect 끝에 붙이면
    // 왼쪽·위는 몸통에 붙고 오른쪽·아래만 23px 떠서 테두리가 어긋나 보였다. 조각은 이 몸통에 붙인다.
    [Tooltip("카드 바탕에서 그림자·투명 여백을 뺀 불투명 몸통까지의 거리(아트 px) — x=왼 y=아래 z=오른 w=위.\n" +
             "등급 테두리·글로우가 이 몸통에 붙는다. 바탕 아트를 바꾸면 이 값도 다시 잰다(알파 230 기준).")]
    [SerializeField] private Vector4 cardBodyInset = new Vector4(4f, 23f, 23f, 6f);

    [Header("하단 체결 버튼")]
    [Tooltip("「조립」 배너(1352×405 · 3.34:1). 양끝 나침반·밀랍인장이 늘어나면 안 되므로 9-slice로 넣는다.")]
    public Sprite forgeBanner;
    [Tooltip("조립 배너 2안(1348×453 · 2.98:1). 비워 두면 1안만 쓴다.")]
    public Sprite forgeBannerAlt;

    [Header("등급 테두리 조각 — CovenantTier 순서(0=Silver 1=Gold 2=Ruby)")]
    [Tooltip("위·아래 가로 장식바.")]
    public Sprite[] gradeBar    = new Sprite[3];
    [Tooltip("네 귀퉁이 모서리 조각. 좌상단 모양 하나로 나머지 셋은 코드가 뒤집어 쓴다.")]
    public Sprite[] gradeCorner = new Sprite[3];

    public Vector4 CardBodyInset => cardBodyInset;

    public Sprite GradeBar(CovenantTier t)    => Pick(gradeBar, (int)t);
    public Sprite GradeCorner(CovenantTier t) => Pick(gradeCorner, (int)t);

    /// <summary>등급 테두리 조각이 갖춰졌나. 하나라도 없으면 카드는 기존 한 장짜리 액자를 쓴다.</summary>
    public bool HasGradeFrames => gradeBar != null && gradeCorner != null &&
                                  gradeBar.Length > 0 && gradeCorner.Length > 0 &&
                                  gradeBar[0] != null && gradeCorner[0] != null;

    private static Sprite Pick(Sprite[] arr, int i)
        => (arr == null || i < 0 || i >= arr.Length) ? null : arr[i];
}
