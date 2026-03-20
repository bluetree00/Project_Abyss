/// <summary>
/// 외눈슬라임 몬스터.
/// LeeMonsterBase를 상속하며 ConfigAddress만 지정.
/// </summary>
public class SlimeMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Slime/Slime";
    protected override string ConfigAddress    => "Slime/SlimeConfig";
    protected override string DataAddress      => "Slime/SlimeData";
    protected override float  HPBarHeadOffset  => 0.7f;
}
