using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 「9-slice로 쓰겠다」고 코드가 선언했는데 <b>스프라이트에 테두리(border)가 없는</b> 아트에
/// 테두리를 넣어준다.
///
/// <para>border가 0이면 Unity는 <c>Image.type = Sliced</c>를 세워도 <b>아무것도 나누지 않고 그냥 늘린다</b>.
/// 그래서 게이지 테두리가 2.26배, 나무판이 3.18배로 찌그러져 있었다. 진짜 원인은 배치가 아니라
/// <b>임포트 설정</b>이었다.</para>
///
/// <para>값은 <c>Temp/border_targets.json</c>에서 읽는다 — 원본 크기와 <b>실제로 그려지는 상자 크기</b>를
/// 함께 재서 산출한 값이다. 모서리는 원본 픽셀 크기 그대로 그려지므로 border×2가 상자보다 크면
/// 9-slice가 겹쳐 깨진다. 그래서 상자 최소 크기의 35%를 넘지 않게 잡혀 있다.</para>
///
/// <para>.meta를 직접 손대지 않는다 — <see cref="TextureImporter"/>에 값을 주고 Unity가 스스로 쓰게 한다.</para>
/// </summary>
public static class SpriteBorderFixEditor
{
    [MenuItem("RelicFairy/UI/9-slice 테두리 채우기")]
    private static void Apply()
    {
        string json = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "border_targets.json");
        if (!File.Exists(json))
        {
            Debug.LogError($"[SpriteBorderFix] 대상 파일이 없다: {json}");
            return;
        }

        // JSON은 {"path": "...", "b": N} 목록뿐이라 문자 단위로 읽는다(의존성 없이).
        const char Q = '"';
        var entries = new List<(string path, float b)>();
        foreach (string chunk in File.ReadAllText(json).Split('{'))
        {
            int pi = chunk.IndexOf(Q + "path" + Q, System.StringComparison.Ordinal);
            int bi = chunk.IndexOf(Q + "b" + Q, System.StringComparison.Ordinal);
            if (pi < 0 || bi < 0) continue;

            int s1 = chunk.IndexOf(Q, pi + 6);
            int s2 = s1 < 0 ? -1 : chunk.IndexOf(Q, s1 + 1);
            if (s1 < 0 || s2 < 0) continue;

            int colon = chunk.IndexOf(':', bi);
            if (colon < 0) continue;

            string num = chunk.Substring(colon + 1)
                              .Trim(' ', ',', '}', ']', (char)10, (char)13, (char)9);
            if (!float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out float b)) continue;

            entries.Add((chunk.Substring(s1 + 1, s2 - s1 - 1), b));
        }

        int done = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var (path, b) in entries)
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) { Debug.LogWarning($"[SpriteBorderFix] 임포터 없음 — {path}"); skipped++; continue; }
                if (imp.textureType != TextureImporterType.Sprite ||
                    imp.spriteImportMode != SpriteImportMode.Single)
                {
                    Debug.LogWarning($"[SpriteBorderFix] 단일 스프라이트가 아니라 건너뛴다 — {path}");
                    skipped++; continue;
                }

                var want = new Vector4(b, b, b, b);
                if (imp.spriteBorder == want) { skipped++; continue; }

                imp.spriteBorder = want;
                imp.SaveAndReimport();
                done++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        AssetDatabase.Refresh();
        Debug.Log($"[SpriteBorderFix] 테두리 적용 {done}종 · 건너뜀 {skipped}종 (대상 {entries.Count}종)");
    }
}
