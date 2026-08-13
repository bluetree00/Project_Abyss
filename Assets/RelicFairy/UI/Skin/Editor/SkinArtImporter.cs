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
    private const string ShopFolder  = "Assets/RelicFairy/UI/Skin/Shop/Art/Final";
    private const string ForgeFolder = "Assets/RelicFairy/UI/Skin/WeaponForge/Art/Final";

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

    [MenuItem("RelicFairy/UI/Import Skin Art (Shop · WeaponForge)")]
    public static void ImportAll()
    {
        int changed = 0;
        changed += Apply(ShopFolder,  ShopRules);
        changed += Apply(ForgeFolder, ForgeRules);

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
