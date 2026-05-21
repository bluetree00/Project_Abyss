using UnityEngine;


namespace RelicFairy.Monster
{
/// <summary>
/// 스펙터 몬스터.
/// HP 60% 이하 도달 시 1회 위상 이동(SpecterPhaseState) 발동 — 유령 색상으로 투명화 후 기습.
/// MonsterConfigSO 의 specialStates 슬롯에 SpecterPhaseData.asset 과 HP 조건 SO 를 조합해 할당한다.
/// </summary>
public class SpecterMonster : MonsterBase
{
    public const string PrefabAddress = "Specter/Specter";
    protected override string ConfigAddress => "Specter/SpecterConfig";
    protected override string DataAddress   => string.Empty;

    protected override void OnEnable()
    {
        ResetTint();
        base.OnEnable();
    }

    // ── 틴트 초기화 (풀 재사용 시 위상 색상 잔류 방지) ───────────────
    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");

    private void ResetTint()
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.Clear();
            r.SetPropertyBlock(mpb);
        }
    }
}
}
