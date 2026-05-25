using UnityEngine;

/// <summary>
/// CP / CP0 / CP1 … 토큰 처리 — 캐릭터 픽업 프리팹을 배치한다.
/// 접미사 없음(CP) 또는 "0"이면 인덱스 0, "1"이면 인덱스 1로 CharacterPickupPrefabs 배열을 참조한다.
/// </summary>
[TokenHandler("CP", TokenCategory.Special, "캐릭터 픽업 오브젝트", isPrefix: true)]
public sealed class CharacterPickupHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        if (ctx.CharacterPickupPrefabs == null || ctx.CharacterPickupPrefabs.Length == 0)
        {
            Debug.LogWarning("[CharacterPickupHandler] CharacterPickupPrefabs 미할당 — 스킵");
            return;
        }

        int idx = ParseIndex(ctx.RawToken, "CP");
        if (idx >= ctx.CharacterPickupPrefabs.Length || ctx.CharacterPickupPrefabs[idx] == null)
        {
            Debug.LogWarning($"[CharacterPickupHandler] CharacterPickupPrefabs[{idx}] 없음 — 스킵");
            return;
        }

        var pos = new Vector3(ctx.WorldPos.x, 0f, ctx.WorldPos.z);
        var go  = Object.Instantiate(ctx.CharacterPickupPrefabs[idx], pos, Quaternion.identity, ctx.Parent);
        go.name = $"CharPickup_{idx}_{ctx.Cell.x}_{ctx.Cell.y}";
    }

    private static int ParseIndex(string rawToken, string prefix)
    {
        string suffix = rawToken.Substring(prefix.Length);
        if (string.IsNullOrEmpty(suffix)) return 0;
        return int.TryParse(suffix, out int n) ? n : 0;
    }
}
