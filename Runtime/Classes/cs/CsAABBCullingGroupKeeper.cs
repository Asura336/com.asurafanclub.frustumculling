using System;
using UnityEngine;

namespace Com.Culling
{
    [Obsolete("从计算着色器回读结果效率太低，用作业系统替代")]
    public class CsAABBCullingGroupKeeper : AABBCullingGroupKeeperTemplate<CsAABBCullingGroup, CsAABBCullingVolume>
    {
        [SerializeField] ComputeShader cs;

        protected override AbsAABBCullingGroup GroupCtor()
        {
            return new CsAABBCullingGroup
            {
                cullingCs = cs
            };
        }
    }
}