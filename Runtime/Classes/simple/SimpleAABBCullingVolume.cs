using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;

namespace Com.Culling
{
    /// <summary>
    /// 标记被剔除的物体。实现应该继承 <see cref="AABBCullingGroupKeeperTemplate{TGroup, TVolume}"/>
    /// </summary>
    public interface IAABBCullingVolume
    {
        /// <summary>
        /// 在管理器中的索引，不可持久化
        /// </summary>
        int Index { get; internal set; }
        /// <summary>
        /// <see cref="Index">索引</see>是有意义的值，表示此实例在管理器的缓冲区内
        /// </summary>
        bool Valid { get; }
        /// <summary>
        /// 本地包围盒，持有此实例的对象需要更新这个值。
        /// </summary>
        Bounds LocalBounds { get; set; }
        /// <summary>
        /// 立即计算此实例在世界空间下的包围盒
        /// </summary>
        Bounds Volume { get; }
        internal bool VolumeUpdated { get; }
        Matrix4x4 LocalToWorld { get; }
#pragma warning disable IDE1006 // 命名样式
        Transform transform { get; }
#pragma warning restore IDE1006 // 命名样式

        internal unsafe void GetLocalBounds(Bounds* dst);
        internal unsafe void GetLocalToWorld(Matrix4x4* dst);

        void DoBecameInvisible(Camera targetCamera);
        void DoBecameVisible(Camera targetCamera);
        void DoLodChanged(Camera targetCamera, IReadOnlyList<float> lodLevelValues, int level);
        void UpdateVolume();
    }

    /// <summary>
    /// 继承此类实现项目指定的被剔除物体。继承类需要指定寻找 <see cref="AABBCullingGroupKeeperTemplate{TGroup, TVolume}">剔除组</see>
    ///的过程。
    ///简单激活和休眠此组件即可自动注册和注销，调用方只需要更新包围盒与监听事件。
    /// </summary>
    /// <typeparam name="TGroupKeeper"></typeparam>
    public abstract class AABBCullingVolumeTemplate<TGroupKeeper> : MonoBehaviour, IAABBCullingVolume
        where TGroupKeeper : AbsAABBCullingGroupKeeper
    {
        [SerializeField] Bounds localBounds;
        [SerializeField] protected TGroupKeeper groupKeeper;
        [SerializeField] int index = -1;

        public UnityEvent<Camera> onBecameVisible;
        public UnityEvent<Camera> onBecameInvisible;
        public UnityEvent onVolumeDisabled;
        public UnityEvent<Camera, IReadOnlyList<float>, int> lodChanged;

        bool volumeUpdated;
        Transform cachedTransform;
        bool destroyed = false;

        protected abstract TGroupKeeper FindGroupKeeper();

        protected virtual void Awake()
        {
            cachedTransform = transform;
            onBecameVisible ??= new UnityEvent<Camera>();
            onBecameInvisible ??= new UnityEvent<Camera>();
            onVolumeDisabled ??= new UnityEvent();
            lodChanged ??= new UnityEvent<Camera, IReadOnlyList<float>, int>();

            index = -1;
        }

        protected virtual void OnEnable()
        {
            if (groupKeeper) { groupKeeper.Add(this); }
            else
            {
                groupKeeper = FindGroupKeeper();
                StartCoroutine(AddToKeeperNextFrame());
            }
        }

        IEnumerator AddToKeeperNextFrame()
        {
            yield return null;
            groupKeeper.Add(this);
        }

        protected virtual void OnDisable()
        {
            if (groupKeeper) { groupKeeper.Remove(this); }
            onVolumeDisabled?.Invoke();
        }

        protected virtual void OnDestroy()
        {
            destroyed = true;
            onBecameVisible.RemoveAllListeners();
            onBecameInvisible.RemoveAllListeners();
            lodChanged.RemoveAllListeners();
            onVolumeDisabled.RemoveAllListeners();
        }

        public override string ToString()
        {
            return gameObject ? gameObject.name : base.ToString();
        }

        unsafe void IAABBCullingVolume.GetLocalToWorld(Matrix4x4* dst)
        {
            var t = cachedTransform ? cachedTransform : (cachedTransform = transform);
            *dst = t ? t.localToWorldMatrix : Matrix4x4.identity;
        }

        unsafe void IAABBCullingVolume.GetLocalBounds(Bounds* dst)
        {
            *dst = localBounds;
        }

        int IAABBCullingVolume.Index
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

        Matrix4x4 IAABBCullingVolume.LocalToWorld
        {
            get
            {
                var t = cachedTransform ? cachedTransform : (cachedTransform = transform);
                return t ? t.localToWorldMatrix : Matrix4x4.identity;
            }
        }

        public void GetHeightAndVisible(out float height, out bool visible)
        {
            height = default;
            visible = default;
            if (groupKeeper is null) { return; }
            var group = groupKeeper.CullingGroup;
            var ctx = group.GetInternalVisibleContextAt(index);
            height = ctx.height;
            visible = ctx.visible;
        }

        [ContextMenu("update bounds")]
        public void UpdateVolume()
        {
            volumeUpdated = true;
        }

        public void DoBecameVisible(Camera targetCamera)
        {
            onBecameVisible?.Invoke(targetCamera);
        }

        public void DoBecameInvisible(Camera targetCamera)
        {
            onBecameInvisible?.Invoke(targetCamera);
        }

        public void DoLodChanged(Camera targetCamera, IReadOnlyList<float> lodLevelValues, int level)
        {
            lodChanged?.Invoke(targetCamera, lodLevelValues, level);
        }

        /// <summary>
        /// 当前世界空间下的轴对齐包围盒
        /// </summary>
        public Bounds Volume
        {
            get
            {
                var b = default(Bounds);
                localBounds.Mul(cachedTransform.localToWorldMatrix, ref b);
                return b;
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
                    if (!EqualsBounds(&prevB, &value))
                    {
                        localBounds = value;
                        volumeUpdated = true;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe bool EqualsBounds(Bounds* a, Bounds* b)
        {
            ulong* pa = (ulong*)a, pb = (ulong*)b;
            // Bounds 相当于 6 个 float
            for (int i = 0; i < 3; i++)
            {
                if (pa[i] != pb[i]) { return false; }
            }
            return true;
        }
    }

    /// <summary>
    /// 对应 <see cref="SimpleAABBCullingGroup"/> 类型的剔除组，
    ///使用 <see cref="UnityEngine.Object.FindObjectOfType(System.Type)"/> 查询剔除组。
    ///作为算法原型，简单挂在物体上就可以用。需要保证场景里已经存在剔除组。
    /// </summary>
    public class SimpleAABBCullingVolume : AABBCullingVolumeTemplate<SimpleAABBCullingGroupKeeper>
    {
        protected override SimpleAABBCullingGroupKeeper FindGroupKeeper() => FindObjectOfType<SimpleAABBCullingGroupKeeper>();
    }
}