using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using static Com.Culling.AABBCullingHelper;

namespace Com.Culling
{
    public interface IAABBCullingGroupKeeper<TVolume>
    {
        void Add(TVolume volume);
        void Remove(TVolume volume);
    }

    public interface IAABBCullingGroupKeeper : IAABBCullingGroupKeeper<IAABBCullingVolume>
    {

    }

    /// <summary>
    /// 托管剔除组的 MonoBehaviour，实现剔除组的自动注册机制并发起轮询。
    ///如果要为项目指定剔除组，不要继承此组件，而应该继承 <see cref="AABBCullingGroupKeeperTemplate{TGroup, TVolume}"/>
    /// </summary>
    [Obsolete("使用或者重写 CameraCullingGroup")]
    public abstract class AbsAABBCullingGroupKeeper : MonoBehaviour, IAABBCullingGroupKeeper
    {
        protected enum CullingGroupFrameState
        {
            Common = 0,
            DoCull,
            CheckEventOnly,
        }

        public const int defaultBufferLength = 1023;

        public Camera referenceCamera;
        protected AbsAABBCullingGroup cullingGroup;
        protected Bounds[] bounds;
        [Header("Set in prefab")]
        [SerializeField] protected float[] lodLevels;
        [Header("Debug and readonly")]
        [SerializeField] protected List<IAABBCullingVolume> volumeInstances;
        protected NativeList<Matrix4x4> instancesLocalToWorld;
        CullingGroupFrameState frameState = 0;

        protected bool hasInit = false;
        protected List<IAABBCullingVolume> addVolumeInstancesBuffer = new List<IAABBCullingVolume>(512);
        void AddIfNotInit(IAABBCullingVolume arg)
        {
            addVolumeInstancesBuffer.Add(arg);
        }
        void RemoveIfNotInit(IAABBCullingVolume arg)
        {
            addVolumeInstancesBuffer.Remove(arg);
        }
        void DoAddFromBuffer()
        {
            int count = addVolumeInstancesBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                Add(addVolumeInstancesBuffer[i]);
            }
        }

        protected virtual void Awake()
        {
            //Debug.Log($"CullingGroupKeeper: ctor at {gameObject.name}");

            cullingGroup = GroupCtor();
            cullingGroup.SetLodLevels(lodLevels);
            bounds = new Bounds[defaultBufferLength];
            cullingGroup.Setup(bounds);
            cullingGroup.InitInternalBuffers(cullingGroup.Count);
            cullingGroup.onStateChanged = CullingGroup_onStateChanged;

            hasInit = true;
            DoAddFromBuffer();
        }

        protected abstract AbsAABBCullingGroup GroupCtor();

        protected virtual void OnEnable()
        {
            cullingGroup.ReferenceCamera = referenceCamera;
            cullingGroup.InitInternalBuffers(cullingGroup.Count);
            cullingGroup.SetLodLevels(lodLevels);
        }

        protected virtual void OnDisable()
        {
            cullingGroup.InitInternalBuffers(cullingGroup.Count);
        }

        protected virtual void OnDestroy()
        {
            cullingGroup.onStateChanged = null;
            if (cullingGroup is IDisposable disposable)
            {
                disposable.Dispose();
            }
            cullingGroup = null;
            if (instancesLocalToWorld.IsCreated)
            {
                instancesLocalToWorld.Dispose();
            }
            cullingGroup.ReleasePersistBuffers();
        }

        protected virtual unsafe void LateUpdate()
        {
            if (volumeInstances != null)
            {
                int count = volumeInstances.Count;
                const int updateSample = 3;
                int start = Time.frameCount % updateSample;
                var pLocalToWorld = (Matrix4x4*)instancesLocalToWorld.GetUnsafePtr();
                for (int i = start; i < count; i += updateSample)
                {
                    if (volumeInstances[i].VolumeUpdated
                        || !EqualsMatrix4x4(volumeInstances[i].LocalToWorld, pLocalToWorld[i]))
                    {
                        bounds[i] = volumeInstances[i].Volume;
                    }
                }
            }

            switch (frameState)
            {
                case CullingGroupFrameState.CheckEventOnly:
                    cullingGroup.CheckEvent();
                    break;
                case CullingGroupFrameState.DoCull:
                    cullingGroup.Cull();
                    break;
                default:
                    cullingGroup.Update();
                    break;
            }
            frameState = 0;
        }

        public virtual void Add(IAABBCullingVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }

            if (!hasInit)
            {
                AddIfNotInit(volume);
                return;
            }

            volumeInstances ??= new List<IAABBCullingVolume>(defaultBufferLength);
            volumeInstances.Add(volume);
            if (!instancesLocalToWorld.IsCreated)
            {
                instancesLocalToWorld = new NativeList<Matrix4x4>(defaultBufferLength, AllocatorManager.Persistent);
            }
            instancesLocalToWorld.Add(volume.LocalToWorld);
            int currentCount = volumeInstances.Count;
            if (currentCount > bounds.Length)
            {
                int size = bounds.Length + defaultBufferLength + 1;
                Realloc(ref bounds, size);
                cullingGroup.Setup(bounds);
            }
            cullingGroup.Count = currentCount;
            int lastIndex = currentCount - 1;
            bounds[lastIndex] = volume.Volume;
            volume.Index = lastIndex;

            cullingGroup.GetCurrentBuffer(out var prev, out var curr);
            prev[lastIndex] = AABBCullingContext.Visible;
            curr[lastIndex] = AABBCullingContext.Visible;
            frameState = CullingGroupFrameState.CheckEventOnly;
        }

        public virtual unsafe void Remove(IAABBCullingVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }
            if (!hasInit)
            {
                RemoveIfNotInit(volume);
                return;
            }
            if (!volume.Valid)
            {
                return;
            }
            if (cullingGroup.Count == 0)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("尝试移除一个剔除物体，但剔除组是空的。");
                }
                goto Finally;
            }

            int removeIndex = volume.Index;
            if (!ReferenceEquals(volume, volumeInstances[removeIndex]))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"{volume}(index = {removeIndex}) != {volumeInstances[removeIndex]}(index = {volume.Index})");
                }
                goto Finally;
            }
            cullingGroup.EraseAt(removeIndex);

            int lastIndex = volumeInstances.Count - 1;
            Assert.AreEqual(lastIndex, cullingGroup.Count, "lastIndex");

            // erase
            volumeInstances[removeIndex] = volumeInstances[lastIndex];
            volumeInstances[removeIndex].Index = removeIndex;
            volumeInstances.RemoveAt(lastIndex);
            instancesLocalToWorld.RemoveAtSwapBack(removeIndex);
            if (lastIndex == 0)
            {
                Assert.IsTrue(volumeInstances.Count == 0, "ins not empty");
            }

Finally:
            volume.Index = -1;
        }

        protected virtual void CullingGroup_onStateChanged(AABBCullingGroupEvent eventContext)
        {
            if (!hasInit || volumeInstances is null)
            {
                return;
            }

            int index = eventContext.index;
            var item = volumeInstances[index];
            if (eventContext.HasBecomeVisible)
            {
                item.DoBecameVisible(referenceCamera);
            }
            if (eventContext.HasBecomeInvisible)
            {
                item.DoBecameInvisible(referenceCamera);
            }
            if (eventContext.CurrentLodLevel != eventContext.PreviousLodLevel)
            {
                item.DoLodChanged(referenceCamera, lodLevels, eventContext.CurrentLodLevel);
            }
        }

        public AbsAABBCullingGroup CullingGroup => cullingGroup;
    }

    /// <summary>
    /// 继承此类通过类型参数配置项目指定的剔除组类型。
    ///剔除组由 MonoBehaviour 托管，<typeparamref name="TVolume"/> 激活和休眠时会自动注册和注销，
    ///只需要更新包围盒和监听事件，无需关心内部的索引变化。
    /// 另见：<see cref="AABBCullingVolumeTemplate{TGroupKeeper}"/>
    /// </summary>
    /// <typeparam name="TGroup"></typeparam>
    /// <typeparam name="TVolume"></typeparam>
    [Obsolete("使用或者重写 CameraCullingGroup")]
    public abstract class AABBCullingGroupKeeperTemplate<TGroup, TVolume> : AbsAABBCullingGroupKeeper
        where TGroup : AbsAABBCullingGroup, new()
        where TVolume : IAABBCullingVolume
    {
        protected override AbsAABBCullingGroup GroupCtor() => new TGroup();
    }

    /// <summary>
    /// 最简单的剔除组实现，使用单线程代码，被剔除物体使用 <see cref="UnityEngine.Object.FindObjectOfType(Type)"/> 查询剔除组
    ///直接挂载在场景里的话，要确保只有一个同类组件。
    /// </summary>
    [Obsolete("如非调试用途，使用或者重写 CameraCullingGroup")]
    public class SimpleAABBCullingGroupKeeper : AABBCullingGroupKeeperTemplate<SimpleAABBCullingGroup, SimpleAABBCullingVolume>
    {

    }
}