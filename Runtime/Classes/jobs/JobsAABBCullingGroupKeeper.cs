namespace Com.Culling
{
    /// <summary>
    /// 预设的使用 Job System 的剔除组。
    ///被剔除物体使用 <see cref="UnityEngine.Object.FindObjectOfType(System.Type)"/> 查询剔除组
    ///直接挂载在场景里的话，要确保只有一个同类组件。
    /// </summary>
    public class JobsAABBCullingGroupKeeper : AABBCullingGroupKeeperTemplate<JobsAABBCullingGroup, JobsAABBCullingVolume>
    {

    }
}