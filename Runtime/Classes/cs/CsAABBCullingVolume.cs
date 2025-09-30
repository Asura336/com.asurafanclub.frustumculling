using System;

namespace Com.Culling
{
    [Obsolete("从计算着色器回读结果效率太低，用作业系统替代")]
    public class CsAABBCullingVolume : AABBCullingVolumeTemplate<CsAABBCullingGroupKeeper>
    {
        protected override CsAABBCullingGroupKeeper FindGroupKeeper() => FindObjectOfType<CsAABBCullingGroupKeeper>();
    }
}