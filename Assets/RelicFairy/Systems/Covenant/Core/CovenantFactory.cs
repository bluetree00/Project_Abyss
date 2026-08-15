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
            {
                var made = new AssembledCovenant(body.Substring(0, sep), body.Substring(sep + 1));

                // 팔레트에서 원인·효과를 못 찾으면(옛 세이브의 사라진 id 등) 껍데기가 남는다.
                // 그대로 넘기면 아무 일도 안 하는 서약이 4칸 중 한 칸을 영구히 차지한다 —
                // 차라리 슬롯을 돌려주고 경고를 남긴다. 개명이라면 CovenantPalette의 별칭 표에 등록할 것.
                if (!made.Resolved)
                {
                    UnityEngine.Debug.LogWarning($"[CovenantFactory] 미해결 조립 서약 ID(원인/효과 없음): {covenantId}");
                    return null;
                }
                return made;
            }
            UnityEngine.Debug.LogWarning($"[CovenantFactory] 잘못된 조립 서약 ID: {covenantId}");
            return null;
        }
        UnityEngine.Debug.LogWarning($"[CovenantFactory] 미등록 서약 ID: {covenantId}");
        return null;
    }
}
