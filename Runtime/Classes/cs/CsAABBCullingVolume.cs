namespace Com.Culling
{
    public class CsAABBCullingVolume : AABBCullingVolumeTemplate<CsAABBCullingGroupKeeper>
    {
        protected override CsAABBCullingGroupKeeper FindGroupKeeper() => FindObjectOfType<CsAABBCullingGroupKeeper>();
    }
}