namespace Com.Culling
{
    /// <summary>
    /// 对应 <see cref="JobsAABBCullingGroupKeeper"/> 类型的剔除组，
    ///使用 <see cref="UnityEngine.Object.FindObjectOfType(System.Type)"/> 查询剔除组。
    ///简单挂在物体上就可以用。需要保证场景里已经存在剔除组。
    /// </summary>
    public class JobsAABBCullingVolume : AABBCullingVolumeTemplate<JobsAABBCullingGroupKeeper>
    {
        protected override JobsAABBCullingGroupKeeper FindGroupKeeper() => FindObjectOfType<JobsAABBCullingGroupKeeper>();
    }
}