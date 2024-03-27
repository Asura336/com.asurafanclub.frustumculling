using System.Collections.Generic;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Events;
using static Com.Culling.AABBCullingHelper;

namespace Com.Culling
{
    /// <summary>
    /// 附加在物体上，写入包围盒作为剔除代理体
    /// </summary>
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [AddComponentMenu("Com/Culling/CullingGroupVolume")]
    public class CullingGroupVolume : MonoBehaviour, IAABBCullingVolume
    {
        [SerializeField] Bounds localBounds;
        [SerializeField] int index = -1;

        public UnityEvent<Camera> onBecameVisible;
        public UnityEvent<Camera> onBecameInvisible;
        public UnityEvent onVolumeDisabled;
        public UnityEvent<Camera, IReadOnlyList<float>, int> lodChanged;

        bool volumeUpdated;
        Transform cachedTransform;
        bool destroyed = false;
        bool selfEnabled = false;

        void Awake()
        {
            cachedTransform = transform;
            onBecameVisible ??= new UnityEvent<Camera>();
            onBecameInvisible ??= new UnityEvent<Camera>();
            onVolumeDisabled ??= new UnityEvent();
            lodChanged ??= new UnityEvent<Camera, IReadOnlyList<float>, int>();
            index = -1;

            CullingGroupVolumeBus.OnSetup += CullingGroupVolumeBus_OnSetup;
        }

        private void CullingGroupVolumeBus_OnSetup(CullingGroupVolumeBus obj)
        {
            InternalWakeup();
        }

        void OnEnable()
        {
            InternalWakeup();
        }

        void InternalWakeup()
        {
            if (!selfEnabled && !destroyed)
            {
                selfEnabled = true;
                CullingGroupVolumeBus.Instance.Add(this);
            }
        }

        void OnDisable()
        {
            selfEnabled = false;
            onVolumeDisabled?.Invoke();
            //Debug.Log($"remove: {gameObject.name}({index})");
            CullingGroupVolumeBus.Instance.Remove(this);
            index = -1;  // ensure index as invalid
        }

        void OnDestroy()
        {
            destroyed = true;
            CullingGroupVolumeBus.OnSetup -= CullingGroupVolumeBus_OnSetup;
            onBecameVisible.RemoveAllListeners();
            onBecameInvisible.RemoveAllListeners();
            lodChanged.RemoveAllListeners();
            onVolumeDisabled.RemoveAllListeners();
        }

        unsafe void OnDrawGizmosSelected()
        {
            /* Bounds
             *   m_Center : float3
             *   m_Extents : float3
             */
            if (destroyed)
            {
                return;
            }

            var wb = Volume;
            float* hExtents = (float*)&wb + 3;

            // if Bounds.size != 0:
            if (hExtents[0] != 0 && hExtents[1] != 0 && hExtents[2] != 0)
            {
                var vs8 = stackalloc Vector3[8];
                wb.GetBoundsVerticesUnsafe(vs8);
                //var localToWorld = transform.localToWorldMatrix;
                //for (int i = 0; i < 8; i++)
                //{
                //    Vector3 worldVec = localToWorld.MultiplyPoint3x4(vs8[i]);
                //    vs8[i] = worldVec;
                //}

                /* 0 0 0
                 * 0 0 1
                 * 0 1 0
                 * 0 1 1
                 * 
                 * 1 0 0
                 * 1 0 1
                 * 1 1 0
                 * 1 1 1
                 */

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(vs8[0], vs8[1]);
                Gizmos.DrawLine(vs8[2], vs8[3]);
                Gizmos.DrawLine(vs8[4], vs8[5]);
                Gizmos.DrawLine(vs8[6], vs8[7]);

                Gizmos.DrawLine(vs8[0], vs8[2]);
                Gizmos.DrawLine(vs8[1], vs8[3]);
                Gizmos.DrawLine(vs8[4], vs8[6]);
                Gizmos.DrawLine(vs8[5], vs8[7]);

                Gizmos.DrawLine(vs8[0], vs8[4]);
                Gizmos.DrawLine(vs8[1], vs8[5]);
                Gizmos.DrawLine(vs8[2], vs8[6]);
                Gizmos.DrawLine(vs8[3], vs8[7]);
            }
        }

        unsafe void IAABBCullingVolume.GetLocalBounds(Bounds* dst)
        {
            *dst = localBounds;
        }

        unsafe void IAABBCullingVolume.GetLocalToWorld(Matrix4x4* dst)
        {
            // 667 times, 1.24 ms
            // 2001 times, 3.56 ms
            //*dst = destroyed ? Matrix4x4.identity : cachedTransform.localToWorldMatrix;

            CopyMatrix(cachedTransform.localToWorldMatrix, ref *dst);
        }
        [BurstCompile]
        static void CopyMatrix(in Matrix4x4 src, ref Matrix4x4 dst)
        {
            dst = src;
        }

        public void DoBecameInvisible(Camera targetCamera)
        {
            onBecameInvisible?.Invoke(targetCamera);
        }

        public void DoBecameVisible(Camera targetCamera)
        {
            onBecameVisible?.Invoke(targetCamera);
        }

        public void DoLodChanged(Camera targetCamera, IReadOnlyList<float> lodLevelValues, int level)
        {
            lodChanged?.Invoke(targetCamera, lodLevelValues, level);
        }

        [ContextMenu("update bounds")]
        public void UpdateVolume()
        {
            volumeUpdated = true;
        }

        int IAABBCullingVolume.Index
        {
            get => index;
            set
            {
                //Debug.Log($"set {index} to {value} ({gameObject.GetHashCode()})");
                index = value;
            }
        }

        public bool Valid => index != -1;

        bool IAABBCullingVolume.VolumeUpdated
        {
            get
            {
                if (destroyed) { return false; }
                if (volumeUpdated)
                {
                    volumeUpdated = false;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 本地空间下的轴对齐包围盒
        /// </summary>
        public Bounds LocalBounds
        {
            get => localBounds;
            set
            {
                var prevB = localBounds;
                unsafe
                {
                    if (!EqualsBounds(prevB, value))
                    {
                        localBounds = value;
                        volumeUpdated = true;
                    }
                }
            }
        }

        public Matrix4x4 LocalToWorld
        {
            get
            {
                var t = cachedTransform ? cachedTransform : (cachedTransform = transform);
                return t ? t.localToWorldMatrix : Matrix4x4.identity;
            }
        }

        /// <summary>
        /// 当前世界空间下的轴对齐包围盒
        /// </summary>
        public Bounds Volume
        {
            get
            {
                var b = default(Bounds);
                localBounds.Mul(LocalToWorld, ref b);
                return b;
            }
        }
    }
}