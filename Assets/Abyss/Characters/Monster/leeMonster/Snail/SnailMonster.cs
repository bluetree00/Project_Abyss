public class SnailMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Snail/Snail";
    protected override string ConfigAddress    => "Snail/SnailConfig";
    protected override string DataAddress      => "Snail/SnailData";
    protected override float  HPBarHeadOffset  => 0.1f;
}
