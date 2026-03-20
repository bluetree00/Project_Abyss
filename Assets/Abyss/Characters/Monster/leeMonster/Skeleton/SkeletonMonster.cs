public class SkeletonMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Skeleton/Skeleton";
    protected override string ConfigAddress    => "Skeleton/SkeletonConfig";
    protected override string DataAddress      => "Skeleton/SkeletonData";
    protected override float  HPBarHeadOffset  => 0.3f;
}
