using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 웨어울프 몬스터.
/// 특수 상태: HP 40% 이하 도달 시 1회 광폭화 — 이후 영구적으로 이동속도·공격력 증가 + 붉은 Tint 유지 (WerewolfEnrageState).
/// </summary>
public class WerewolfMonster : MonsterBase
{
    public const string PrefabAddress = "Werewolf/Werewolf";
    protected override string ConfigAddress => "Werewolf/WerewolfConfig";
    protected override string DataAddress   => string.Empty;

    protected override void OnEnable()
    {
        ResetTint();
        base.OnEnable();
    }

    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");

    private void ResetTint()
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, Color.white);
            if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, Color.white);
            r.SetPropertyBlock(mpb);
        }
    }
}
}
