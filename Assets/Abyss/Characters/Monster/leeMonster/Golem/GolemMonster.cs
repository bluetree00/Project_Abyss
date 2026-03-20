public class GolemMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Golem/Golem";
    protected override string ConfigAddress    => "Golem/GolemConfig";
    protected override string DataAddress      => "Golem/GolemData";
    protected override float  HPBarHeadOffset  => 1.0f;
}
