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
    private const string DialogueFolder  = "Assets/RelicFairy/UI/Skin/Dialogue/Sprites";
    private const string CovenantFolder     = "Assets/RelicFairy/UI/Skin/Covenant/Sprites";
    private const string RuneGridFolder     = "Assets/RelicFairy/UI/Skin/RuneGrid/Sprites";
    private const string RuneSelectFolder   = "Assets/RelicFairy/UI/Skin/RuneSelect/Art";
    private const string RefineryOldFolder  = "Assets/RelicFairy/UI/Skin/Refinery/Art";

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

    // ── 대화창 (대사 상자 1383×300, 화면 폭을 따라 늘어남) ──
    // 버튼 6종은 대응하는 자리가 없어 배선하지 않았지만(AdvanceButton은 투명 클릭 캐처),
    // 나중에 선택지 UI가 생기면 바로 쓰도록 임포트 설정만 맞춰둔다.
    private static readonly Rule[] DialogueRules =
    {
        new("바탕1.png",            1024, 60, 60, 60, 60),   // 대사 상자 바탕 — 폭 가변이라 9-slice
        new("바탕2.png",            1024, 60, 60, 60, 60),   // 대체 바탕(미사용)
        new("대화창 좌상단.png",       256),
        new("대화창 우상단.png",       256),
        new("대화창 좌하단.png",       256),
        new("대화창 우하단.png",       256),
        new("대화창 상단.png",        1024),   // 대부분 투명 — 통짜로 놓아야 위치가 맞는다
        new("대화창 하단.png",        1024),
        new("버튼1.png",             256),   // 이하 미사용(자리 미정)
        new("버튼2.png",             256),
        new("버튼3.png",             256),
        new("버튼4.png",             256),
        new("버튼5.png",             512),
        new("긴버튼1.png",            512),
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
        new("전기룬_임시.png",   128),   // 바람룬을 노랑으로 재색한 임시본 — 정식 전기룬 오면 교체
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
    };

    private static readonly Rule[] RefineryOldRules =
    {
        new("확률 막대 테두리@2x.png",    2048, 20, 0, 20, 0),
        new("확률 막대 검은바탕@2x.png",  2048, 20, 0, 20, 0),
    };

    [MenuItem("RelicFairy/UI/Import Skin Art (All Screens)")]
    public static void ImportAll()
    {
        int changed = 0;
        changed += Apply(ShopFolder,        ShopRules);
        changed += Apply(ForgeFolder,       ForgeRules);
        changed += Apply(RefineryFolder,    RefineryRules);
        changed += Apply(RelicInfoFolder,   RelicInfoRules);
        changed += Apply(DialogueFolder,    DialogueRules);
        changed += Apply(CovenantFolder,    CovenantRules);
        changed += Apply(RuneGridFolder,    RuneGridRules);
        changed += Apply(RuneSelectFolder,  RuneSelectRules);
        changed += Apply(RefineryOldFolder, RefineryOldRules);

        AssetDatabase.Refresh();
        Debug.Log($"[SkinArtImporter] {changed}장 재임포트 완료.");
        EditorUtility.DisplayDialog("완료", $"스킨 아트 {changed}장의 임포트 설정을 적용했습니다.", "확인");
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
