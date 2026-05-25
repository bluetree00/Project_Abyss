using UnityEngine;

/// <summary>
/// WP / WP0 / WP1 … 토큰 처리 — 무기 픽업 프리팹을 배치한다.
/// 접미사 없음(WP) 또는 "0"이면 인덱스 0, "1"이면 인덱스 1로 WeaponPickupPrefabs 배열을 참조한다.
/// </summary>
[TokenHandler("WP", TokenCategory.Special, "무기 픽업 오브젝트", isPrefix: true)]
public sealed class WeaponPickupHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        if (ctx.WeaponPickupPrefabs == null || ctx.WeaponPickupPrefabs.Length == 0)
        {
            Debug.LogWarning("[WeaponPickupHandler] WeaponPickupPrefabs 미할당 — 스킵");
            return;
        }

        int idx = ParseIndex(ctx.RawToken, "WP");
        if (idx >= ctx.WeaponPickupPrefabs.Length || ctx.WeaponPickupPrefabs[idx] == null)
        {
            Debug.LogWarning($"[WeaponPickupHandler] WeaponPickupPrefabs[{idx}] 없음 — 스킵");
            return;
        }

        var pos = new Vector3(ctx.WorldPos.x, 0f, ctx.WorldPos.z);
        var go  = Object.Instantiate(ctx.WeaponPickupPrefabs[idx], pos, Quaternion.identity, ctx.Parent);
        go.name = $"WeaponPickup_{idx}_{ctx.Cell.x}_{ctx.Cell.y}";
    }

    private static int ParseIndex(string rawToken, string prefix)
    {
        string suffix = rawToken.Substring(prefix.Length);
        if (string.IsNullOrEmpty(suffix)) return 0;
        return int.TryParse(suffix, out int n) ? n : 0;
    }
}
