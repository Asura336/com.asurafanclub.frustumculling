using UnityEngine;

namespace Com.Culling
{
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