using System;

/// <summary>
/// 서약 ID → CovenantBase 생성 팩토리. 조립 서약(asm:) 전용.
/// (사전제작 12종은 2026-07 폐기 — 조립 서약으로 대체.)
/// </summary>
public static class CovenantFactory
{
    /// <summary>"asm:&lt;cause&gt;@&lt;tier&gt;|&lt;effect&gt;@&lt;tier&gt;" = 조립 서약. 그 외/미등록 ID는 null.</summary>
    public static CovenantBase Create(string covenantId)
    {
        if (!string.IsNullOrEmpty(covenantId) && covenantId.StartsWith(AssembledCovenant.Prefix))
        {
            var body = covenantId.Substring(AssembledCovenant.Prefix.Length);
            int sep = body.IndexOf('|');
            if (sep > 0)
                return new AssembledCovenant(body.Substring(0, sep), body.Substring(sep + 1));
            UnityEngine.Debug.LogWarning($"[CovenantFactory] 잘못된 조립 서약 ID: {covenantId}");
            return null;
        }
        UnityEngine.Debug.LogWarning($"[CovenantFactory] 미등록 서약 ID: {covenantId}");
        return null;
    }
}
