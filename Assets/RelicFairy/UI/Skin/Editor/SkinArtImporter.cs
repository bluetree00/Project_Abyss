// SkinArtImporter.cs
// 디자이너 납품 스킨 아트(Skin/*/Art/Final)의 임포트 설정을 화면 표시 크기에 맞춰 일괄 적용한다.
//
// 왜 스크립트인가: 납품본은 원본 해상도가 표시 크기의 3~5배라 그대로 두면 메모리·빌드 용량이 낭비되고,
// 9-slice 보더는 .meta에만 있어 코드의 Image.Type.Sliced가 무력해진다(보더 0이면 통째로 늘어난다).
// .meta 직접 편집은 금지라 TextureImporter를 거친다. 재납품이 오면 이 메뉴만 다시 돌리면 된다.
//
// 표의 maxSize는 "표시 크기 이상인 가장 작은 2의 거듭제곱"이다 — 그 아래로 내리면 눈에 띄게 뭉갠다.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SkinArtImporter
{
    private const string ShopFolder     = "Assets/RelicFairy/UI/Skin/Shop/Sprites";
    private const string ForgeFolder    = "Assets/RelicFairy/UI/Skin/WeaponForge/Sprites";
    private const string RefineryFolder  = "Assets/RelicFairy/UI/Skin/Refinery/Sprites";
    private const string RelicInfoFolder = "Assets/RelicFairy/UI/Skin/RelicInfo/Sprites";
    private const string CovenantFolder     = "Assets/RelicFairy/UI/Skin/Covenant/Sprites";
    private const string RuneGridFolder     = "Assets/RelicFairy/UI/Skin/RuneGrid/Sprites";
    private const string RuneSelectFolder   = "Assets/RelicFairy/UI/Skin/RuneSelect/Art";
    private const string RefineryOldFolder  = "Assets/RelicFairy/UI/Skin/Refinery/Art";
    private const string PuzzleRuneFolder   = "Assets/RelicFairy/UI/PuzzleGrid/Sprites/Runes";
    private const string AchievementFolder  = "Assets/RelicFairy/UI/Skin/Achievement/Sprites";
    private const string BamaoFolder        = "Assets/RelicFairy/Prefabs/UI/Bamao/BamaoUIPack/Sprites/Button";
    private const string CrucibleFolder     = "Assets/RelicFairy/UI/Skin/Crucible/Art";

    /// <summary>파일 1장의 목표 설정. border는 9-slice(L,B,R,T) — 0이면 Simple로 쓰는 아트.</summary>
    private readonly struct Rule
    {
        public readonly string File;
        public readonly int    MaxSize;
        public readonly Vector4 Border;

        public Rule(string file, int maxSize, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            File = file; MaxSize = maxSize; Border = new Vector4(l, b, r, t);
        }
    }

    // ── 상점 (창 1272×904 기준) ────────────────────────────────
    private static readonly Rule[] ShopRules =
    {
        new("bg.png",                2048),                        // 창 전체 배경 1272×904
        new("타이틀 검정박스.png",     2048, 240, 0, 240, 0),        // 상단 띠 — 가로만 늘린다
        new("타이틀.png",              512),                        // 간판+등불 일체 389×179
        new("장식.png",                128),                        // 우상단 문장 76×111
        new("좌상단 테두리.png",        128),
        new("우상단테두리.png",         128),
        new("좌하단 테두리.png",        128),
        new("우하단 테두리.png",        128),
        new("특가 페이지.png",         1024, 120, 0, 120, 0),        // 특가 배너 637×161 — 가로 가변
        new("그냥 배경.png",            256,  56, 56, 56, 56),       // 상품카드 193×181
        new("팔린 배경.png",            256,  56, 56, 56, 56),       // 품절 카드(같은 형태)
        new("품절 표시_.png",           256),                        // SOLD OUT 도장 205×108
        new("사각형 5 복사.png",        128,  40, 40, 40, 40),       // 아이콘 칸 채움
        new("사각형 5.png",             128,  40, 40, 40, 40),       // 아이콘 칸 테두리
        new("구매창.png",              1024,  80, 90, 80, 90),       // 우측 양피지 272×572 — 세로 가변
        new("구매버튼.png",             256, 110, 0, 110, 0),        // 빈 명판 — 라벨은 코드가 얹는다
        new("새로고침 버튼.png",         256),                        // "새로고침 ⟳ 10"이 아트에 구워짐 → 늘리면 글자가 깨진다
        new("버프바탕.png",              64,  24, 0, 24, 0),
        new("포션바탕.png",              64,  24, 0, 24, 0),
        new("버프.png",                  64),
        new("포션.png",                  64),
        new("골드.png",                  32),
        // 아이템 아이콘 — 스킨이 아니라 상품 아이콘. 카드 아이콘 칸(78×105)에 들어간다.
        new("체력 물약.png",            128),
        new("생명가호.png",             128),
        new("파괴의 가호.png",           128),
        new("무기강화 재료.png",         128),
    };

    // ── 원거리 장비 선택 팝업 (패널 620×631 기준) ───────────────
    private static readonly Rule[] ForgeRules =
    {
        new("bg.png",                 1024),   // 패널 전체(테두리+상하 문장 포함) 688×714
        new("사각형 3.png",             256),   // 아이콘 홀더 채움 184×184
        new("사각형 3 복사 5.png",       256),   // 홀더 테두리
        new("사각형 3 복사 6.png",       256),   // 홀더 외곽선
        new("좌측 버튼.png",             128),   // 42×102
        new("우측 버튼.png",             128),
        // "장착하기"가 아트에 구워져 있다 — 9-slice로 늘리면 글자가 깨진다.
        // (납품본의 "전환 버튼"·"전환표시"는 기획상 불필요로 정리됐다 — 바탕화면 원본에는 남아 있다.)
        new("장착버튼.png",              128),   // 157×52
        new("체크표시.png",               64),
        new("장식.png",                  256),   // 이름·버튼 사이 구분 장식 280×37
    };

    // ── 정제소 (창 1170×828 기준) ────────────────────────────
    // 기존 Refinery/Art 는 이미 같은 푸른 크리스탈 계열이라 남겨둔다 —
    // 이번 납품은 그중 '평평한 색 도형'이던 룬·타이틀·중앙틀·버튼·사이드 패널만 완성본으로 올린다.
    private static readonly Rule[] RefineryRules =
    {
        new("빨간룬.png",        128),   // 불   92×110
        new("파란룬.png",        128),   // 얼음
        new("노란룬 복사.png",    128),   // 전기(순노랑)
        new("초록룬.png",        128),   // 풀
        new("노란룬.png",        128),   // 빛(금색)
        new("보라룬.png",        128),   // 어둠
        new("초록룬1.png",       128),   // 여분(채도·크기 다른 초록) — 현재 미사용
        new("중앙 최종룬.png",    256),   // 결과 틀 135×135
        new("타이틀 에리어.png", 1024, 300, 0, 300, 0),   // 965×90 — 가로만 늘린다
        new("세부지표 1.png",     512, 120, 100, 120, 100),  // 우측 패널 310×140
        new("세부지표 3.png",     512, 120, 140, 120, 140),  // 좌측 패널 200×280
        new("버튼 중앙.png",      512, 200, 0, 200, 0),   // 돌리기 315×71
        new("버튼 우측.png",      256, 200, 0, 200, 0),   // 무료·재점화 250×69
    };

    // ── 유물 선택 팝업 (패널 폭 1280 고정 · 높이는 내용에 따라 늘어남) ──
    // 이 화면은 ContentSizeFitter로 세로가 변한다 — 폭이 내용에 따라 변하는 태그 칩만 9-slice가 필수다.
    private static readonly Rule[] RelicInfoRules =
    {
        new("테두리.png",        2048),                 // 패널 외곽 1280×855 — 상하 중앙 문장이 있어 통짜
        new("초상화.png",         512),                 // 액자 340×443 (697:907 비율 유지)
        new("사각형 4.png",       256, 40, 0, 40, 0),   // 태그 칩 — 글자 길이에 따라 폭 가변
        new("고유.png",          1024, 60, 60, 60, 60), // 고유 카드
        new("정오.png",           512, 60, 0, 60, 0),   // 상시 능력 칸 1
        new("하루의 순환.png",     512, 60, 0, 60, 0),   // 상시 능력 칸 2
        new("선택 버튼.png",       256),                 // "선택" 글자 구워짐 → 늘리면 깨진다
        new("취소.png",           256),                 // "취소" 글자 구워짐
    };


    // ── 서약 조립 (개편본: 양피지 + 등급 테두리) ────────────────
    // 카드 바탕은 카드 크기를 따라가므로 9-slice, 등급 조각·머리표는 글자/장식이라 통짜.
    private static readonly Rule[] CovenantRules =
    {
        new("바탕.png",             2048),                     // 양피지 전면 — 말린 양끝이 있어 늘리면 안 된다
        new("결과 두루마리.png",       512),                     // 중앙 결과 두루마리
        new("원인.png",              256),                     // 머리표 — 글자 구워짐
        new("결과.png",              256),
        new("효과.png",              256),
        new("선택 바탕.png",          256, 40, 40, 40, 40),      // 카드 바탕(선택) — 카드 크기 가변
        new("비선택 바탕.png",         256, 40, 40, 40, 40),
        new("그룹 1.png",            512),                     // 조립 완성 예시(참고용, 미배선)
        // 등급 조각 — 가로 장식바 + 모서리. 7등급 납품이나 CovenantTier는 3단계뿐이라
        // 철·골드·루비만 배선하고 나머지는 설정만 맞춰 대기.
        new("철.png",                256), new("철 테두리.png",    128),
        new("골드.png",              256), new("골드 테두리.png",   128),
        new("루비.png",              256), new("루비 테두리.png",   128),
        new("그린.png",              256), new("그린 테두리.png",   128),
        new("블루.png",              256), new("블루 테두리.png",   128),
        new("보라.png",              256), new("보라 테두리.png",   128),
        new("빛.png",                256), new("빛 테두리.png",     128),
    };

    // ── 룬 그리드 (판 위 블록 칸 · 빈 칸 · 판 외곽) ───────────────
    // 블록 타일은 한 칸을 꽉 채우는 정사각이라 9-slice가 아니라 통짜로 넣는다.
    // 판 외곽은 조각 8개(상·하·좌·우 + 코너 4)를 코드가 조립한다 — 한 장짜리 액자가 아니다.
    private static readonly Rule[] RuneGridRules =
    {
        new("불룬.png",         128),
        new("물룬.png",         128),   // → ICE(얼음). 글리프는 물방울이지만 색이 맞아 그대로 쓴다
        new("전기룬.png",       128),   // 정식 납품본(781×784 → 형제와 같은 164×166으로 축소)
        new("전기룬_임시.png",   128),   // [사용 안 함] 바람룬 재색 임시본 — 정식본으로 교체 완료
        new("풀룬.png",         128),
        new("빛룬.png",         128),
        new("어둠룬.png",       128),
        new("바람룬.png",       128),   // 원본 보존(정식 전기룬 교체 시 참고)
        new("심연룬.png",       128),   // 대응 속성 없음 — CENTER 존 후보
        new("그리드 바탕.png",   256),   // 빈 칸
        // 변 조각은 코너 사이를 늘려 채우므로 늘어나는 축에 보더가 필요하다(코너는 통짜).
        new("테두리 상단.png",   256, 40, 0, 40, 0), new("테두리 하단.png",   256, 40, 0, 40, 0),
        new("테두리 좌.png",     128, 0, 40, 0, 40), new("테두리 우.png",     128, 0, 40, 0, 40),
        new("테두리 좌상단.png", 128), new("테두리 우상단.png", 128),
        new("테두리 좌하단.png", 128), new("테두리 우하단.png", 128),
    };

    // ── 속성 룬비석 (elementArt/elementBorder) ──────────────────
    // 납품 원본이 2437×2344인데 임포트 상한이 2048로 남아 있어 1장당 약 5MB, 5장 26MB를 먹는다.
    // 실제 최대 표시는 룬 획득 카드의 문양칸 102×102(그것도 기능·등급 아트가 없을 때의 폴백)이고
    // 스테이징 슬롯은 그보다 작다. 256이면 표시 크기의 2.5배로 충분하다.
    private static readonly Rule[] PuzzleRuneRules =
    {
        new("룬1.png", 256), new("룬2.png", 256), new("룬3.png", 256),
        new("룬4.png", 256), new("룬5.png", 256),
        // 테두리는 원본이 44×38이라 그대로 둔다.
        new("룬1 테두리@2x.png", 128), new("룬2 테두리@2x.png", 128), new("룬3 테두리@2x.png", 128),
        new("룬4 테두리@2x.png", 128), new("룬5 테두리@2x.png", 128),
        new("룬 테두리_1@2x.png", 128),
    };

    // ── 기존 화면의 9-slice 보정 ────────────────────────────────
    // 이 아트들은 sliced로 그려지는데 보더가 0이라 액자·테두리가 통째로 늘어난다.
    // 특히 룬 획득 카드는 후보 수에 따라 폭이 변하고(_cardW = Min(300, avail/n)),
    // 확률 막대는 패널 폭을 따라 늘어나 왜곡이 눈에 띈다.
    // maxSize는 현재 값(2048=무축소) 그대로 둬 겉모습은 보더 외에 바뀌지 않는다.
    // ※ 선택/넘기기 버튼은 글자가 구워져 있어 보더를 주면 안 된다(지금 0 = 정상).
    private static readonly Rule[] RuneSelectRules =
    {
        new("룬 획득 카드@2x.png",      2048, 70, 70, 70, 70),   // 장식 모서리 액자
        new("룬 획득 카드 바탕@2x.png",  2048, 30, 30, 30, 30),
        new("룬 획득 타이틀@2x.png",     2048, 40,  0, 40,  0),   // 가로로만 늘어나는 긴 바
        // 둥근 모서리 단색 판 3종 — 카드 안에서 정사각(룬 타일)·세로로 긴 상자(효과 칸)·가로 띠(놓을 자리)로 늘어난다.
        // 경계 없이 Sliced면 그냥 늘어나 ×1.4~3.0 왜곡(2026-09-09 런타임 실측). 모서리 반경(~10px@2x)만 지킨다.
        new("룬 타일@2x.png",           2048, 12, 12, 12, 12),
        new("효과 칸@2x.png",           2048, 12, 12, 12, 12),
        new("놓을 자리 있음@2x.png",     2048, 12, 12, 12, 12),
        new("놓을 자리 없음@2x.png",     2048, 12, 12, 12, 12),
    };

    private static readonly Rule[] RefineryOldRules =
    {
        new("확률 막대 테두리@2x.png",    2048, 20, 0, 20, 0),
        new("확률 막대 검은바탕@2x.png",  2048, 20, 0, 20, 0),
    };

    // ── 업적 (기억의 제단) ─────────────────────────────────
    // ── 재련소 · 원거리 파츠 탭 ────────────────────────────────
    private static readonly Rule[] CrucibleRules =
    {
        // 상세 패널(370×410)에 9-slice로 얹는다 — 316×233 아트의 팔각 모서리(~32px)만 지킨다.
        new("원거리 강화 바탕@2x.png",   512, 32, 32, 32, 32),
        new("원거리 강화 테두리@2x.png", 512, 32, 32, 32, 32),
    };

    private static readonly Rule[] AchievementRules =
    {
        // 「받아갈 것」 띠. ActionBar가 판 폭을 따라 늘어나므로 좌우 모서리(둥근 끝 ~20px)만 지킨다.
        new("하단바.png", 1024, 24, 0, 24, 0),
        // 행 바탕 3상태(853×60). 목록 폭을 따라 늘어난다 — 둥근 끝 ~12px만 지킨다.
        new("행_진행중.png",   1024, 16, 0, 16, 0),
        new("행_수령가능.png", 1024, 16, 0, 16, 0),
        new("행_받음.png",     1024, 16, 0, 16, 0),
        // 버튼·칩 — 글자 길이에 따라 폭이 변할 수 있어 좌우만 지킨다.
        new("버튼_모두받기.png", 256, 14, 0, 14, 0),
        new("버튼_받기.png",     128, 12, 0, 12, 0),
        new("칩_선택.png",       128,  8, 0,  8, 0),
        new("칩_비선택.png",     128,  8, 0,  8, 0),
    };

    // ── 무기 교체 팝업 (Bamao 나무판 — 디자이너 납품 없음, 팩 원본을 9-slice로만 바로잡는다) ──
    private static readonly Rule[] BamaoRules =
    {
        // 판(1217×704): 거친 가장자리 60px만 지키고 가운데 나뭇결을 늘린다.
        new("Popup_wood_bg.png",    1024, 60, 60, 60, 60),
        // 제목 띠(1000×189): 양끝 못 박힌 부분 200px을 지킨다. 세로는 56까지 눌리므로 위아래 경계는 두지 않는다.
        new("Popup_wood_title.png", 1024, 200, 0, 200, 0),
    };

    [MenuItem("RelicFairy/UI/Import Skin Art (All Screens)")]
    public static void ImportAll()
    {
        int changed = 0;
        changed += Apply(ShopFolder,        ShopRules);
        changed += Apply(ForgeFolder,       ForgeRules);
        changed += Apply(RefineryFolder,    RefineryRules);
        changed += Apply(RelicInfoFolder,   RelicInfoRules);
        changed += Apply(CovenantFolder,    CovenantRules);
        changed += Apply(RuneGridFolder,    RuneGridRules);
        changed += Apply(RuneSelectFolder,  RuneSelectRules);
        changed += Apply(RefineryOldFolder, RefineryOldRules);
        changed += Apply(PuzzleRuneFolder,  PuzzleRuneRules);
        changed += Apply(AchievementFolder, AchievementRules);
        changed += Apply(BamaoFolder,       BamaoRules);
        changed += Apply(CrucibleFolder,    CrucibleRules);

        AssetDatabase.Refresh();
        Debug.Log($"[SkinArtImporter] {changed}장 재임포트 완료.");
        // 모달(DisplayDialog)은 쓰지 않는다 — MCP로 실행하면 확인을 누를 사람이 없어 에디터 메인 루프가 그 창에 잡히고,
        // 그 뒤 모든 MCP 명령이 "ping not answered"로 죽는다(2026-09-08 실측). 결과는 콘솔 로그로 남긴다.
        Debug.Log($"[SkinArtImporter] 스킨 아트 {changed}장의 임포트 설정을 적용했습니다.");
    }

    private static int Apply(string folder, Rule[] rules)
    {
        int changed = 0;

        foreach (var rule in rules)
        {
            string path = $"{folder}/{rule.File}";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[SkinArtImporter] 파일 없음: {path}");
                continue;
            }

            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.mipmapEnabled       = false;      // UI는 화면 픽셀 1:1 — 밉맵은 메모리 33% 낭비
            importer.alphaIsTransparency = true;
            importer.filterMode          = FilterMode.Bilinear;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.spriteBorder        = rule.Border;

            // maxTextureSize는 importer 프로퍼티로 주면 플랫폼 설정에 반영되지 않는다(2048 그대로 남는다).
            // 기본 플랫폼 설정을 통째로 읽어 고쳐 되돌려야 한다.
            // 크런치는 디스크(빌드) 용량을 줄인다. 작은 아이콘은 이득이 적고 아티팩트만 남아 큰 것만.
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize     = rule.MaxSize;
            platform.textureCompression = TextureImporterCompression.Compressed;
            platform.crunchedCompression = rule.MaxSize >= 512;
            platform.compressionQuality  = 75;
            importer.SetPlatformTextureSettings(platform);

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }
}
#endif
