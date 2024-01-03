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
        [SerializeField] bool transformStatic;
        [SerializeField] Bounds localBounds;
        [SerializeField] int index = -1;

        public UnityEvent<Camera> onBecameVisible;
        public UnityEvent<Camera> onBecameInvisible;
        public UnityEvent onVolumeDisabled;
        public UnityEvent<Camera, IReadOnlyList<float>, int> lodChanged;

        bool volumeUpdated;
        Transform cachedTransform;
        Matrix4x4 cachedLocalToWorld;
        bool destroyed = false;
        bool selfEnabled = false;

        void Awake()
        {
            cachedTransform = transform;
            onBecameVisible ??= new UnityEvent<Camera>();
            onBecameInvisible ??= new UnityEvent<Camera>();
            onVolumeDisabled ??= new UnityEvent();
            lodChanged ??= new UnityEvent<Camera, IReadOnlyList<float>, int>();

            CullingGroupVolumeBus.OnSetup += CullingGroupVolumeBus_OnSetup;

            index = -1;
        }

        private void CullingGroupVolumeBus_OnSetup(CullingGroupVolumeBus obj)
        {
            if (!destroyed && selfEnabled && index == -1)
            {
                CullingGroupVolumeBus.Instance.Add(this);
            }
        }

        void OnEnable()
        {
            selfEnabled = true;
            cachedLocalToWorld = cachedTransform.localToWorldMatrix;
            CullingGroupVolumeBus.Instance.Add(this);
        }

        void OnDisable()
        {
            selfEnabled = false;
            onVolumeDisabled?.Invoke();
            CullingGroupVolumeBus.Instance.Remove(this);
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

        unsafe void IAABBCullingVolume.GetLocalBounds(Bounds* dst)
        {
            *dst = localBounds;
        }

        unsafe void IAABBCullingVolume.GetLocalToWorld(Matrix4x4* dst)
        {
            // 667 times, 1.24 ms
            // 2001 times, 3.56 ms
            //*dst = destroyed ? Matrix4x4.identity : cachedTransform.localToWorldMatrix;

            // 667 times, 1.23 ms
            // 2001 times, 3.51 ms
            if (!transformStatic)
            {
                cachedLocalToWorld = cachedTransform.localToWorldMatrix;
            }
            CopyMatrix(cachedLocalToWorld, ref *dst);
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

        public int Index
        {
            get => index;
            set => index = value;
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

        public Matrix4x4 LocalToWorld => transformStatic
            ? cachedLocalToWorld
            : cachedLocalToWorld = cachedTransform.localToWorldMatrix;

        public bool TransformStatic { get => transformStatic; set => transformStatic = value; }

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