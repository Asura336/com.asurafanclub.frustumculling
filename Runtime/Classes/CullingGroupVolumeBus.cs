using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Jobs;

namespace Com.Culling
{
    public interface ICullingGroupVolumeBus
    {
        Bounds[] BoundsRef { get; }
        int Count { get; }
        IReadOnlyList<IAABBCullingVolume> VolumesRef { get; }

        void Add(IAABBCullingVolume volume);
        void Remove(IAABBCullingVolume volume);
    }

    /// <summary>
    /// 保存包围盒与剔除对象的全局总线，全局单例会被激活的 <see cref="CullingGroupVolume"/> 唤醒
    /// </summary>
    internal class CullingGroupVolumeBus : MonoBehaviour, ICullingGroupVolumeBus
    {
        public const int defaultBufferLength = 1024;
        const int updateSample = 7;

        int count = 0;
        int unmanagedCapacity = 0;
        readonly List<IAABBCullingVolume> volumeInstances = new List<IAABBCullingVolume>(defaultBufferLength);
        Bounds[] bounds;
        TransformAccessArray instanceTransforms;
        NativeList<Bounds> instancesLocalBounds;
        NativeList<Bounds> instancesWorldBounds;

        bool destroyed = false;

        bool m_pauseUpdate = false;
        bool PauseUpdate
        {
            get
            {
                bool o = m_pauseUpdate;
                m_pauseUpdate = false;
                return o;
            }
            set => m_pauseUpdate = false;
        }

        static ICullingGroupVolumeBus s_instance;
        public static ICullingGroupVolumeBus Instance
        {
            get
            {
                if (s_instance == null)
                {
                    if (Application.isPlaying)
                    {
                        var gObj = new GameObject(nameof(CullingGroupVolumeBus));
                        DontDestroyOnLoad(gObj);
                        s_instance = gObj.AddComponent<CullingGroupVolumeBus>();
                        return s_instance;
                    }
                    else
                    {
                        return EmptyCullingGroupVolume.instance;
                    }
                }
                return s_instance;
            }
            set => s_instance = value;
        }

        void Awake()
        {
            if (FindAnyObjectByType<CullingGroupVolumeBus>() != this)
            {
                DestroyImmediate(this);
                return;
            }

            Instance = this;
            OnSetup?.Invoke(this);
        }

        void OnDestroy()
        {
            destroyed = true;
            Release(ref instancesLocalBounds);
            Release(ref instanceTransforms);
            Release(ref instancesWorldBounds);
        }

        unsafe void LateUpdate()
        {
            if (PauseUpdate || count == 0 || destroyed) { return; }

            // input
            const int updateSample = 3;
            int start = Time.frameCount % updateSample;
            var pLocalBounds = (Bounds*)instancesLocalBounds.GetUnsafePtr();
            for (int i = start; i < count; i += updateSample)
            {
                if (volumeInstances[i].VolumeUpdated)
                {
                    volumeInstances[i].GetLocalBounds(pLocalBounds + i);
                }
            }

            var getWorldBoundsJob = new GetWorldBoundsJobFor
            {
                length = count,
                localBounds = instancesLocalBounds.AsArray(),
                worldBounds = instancesWorldBounds.AsArray(),
            };
            getWorldBoundsJob.ScheduleReadOnly(instanceTransforms, 64, default).Complete();
            fixed (Bounds* pBounds = bounds)
            {
                UnsafeUtility.MemCpy(pBounds, instancesWorldBounds.GetUnsafePtr(), sizeof(Bounds) * count);
            }
        }

        public unsafe void Add(IAABBCullingVolume volume)
        {
            if (destroyed) { return; }
            if (volumeInstances.Contains(volume))
            {
                Debug.LogWarning($"{volume.transform.name} already exists.");
                return;
            }
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }
            PauseUpdate = true;

            int addIndex = count;
            if (addIndex + 1 > unmanagedCapacity)
            {
                unmanagedCapacity = Mathf.Max(defaultBufferLength, count * 2);
                Realloc(ref instancesLocalBounds, unmanagedCapacity);
                Realloc(ref instancesWorldBounds, unmanagedCapacity);
                Realloc(ref instanceTransforms, unmanagedCapacity);
                AABBCullingHelper.Realloc(ref bounds, unmanagedCapacity);
            }
            volumeInstances.Add(volume);
            instanceTransforms.Add(volume.transform);
            Assert.AreEqual(instanceTransforms.length, count + 1);
            bounds[addIndex] = volume.Volume;
            volume.GetLocalBounds((Bounds*)instancesLocalBounds.GetUnsafePtr() + addIndex);
            instancesWorldBounds.Add(volume.Volume);
            volume.Index = addIndex;

            count++;

            OnAddVolume?.Invoke(this, addIndex);
        }

        public unsafe void Remove(IAABBCullingVolume volume)
        {
            if (destroyed) { return; }
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }
            if (!volume.Valid) { return; }
            if (volumeInstances.Count == 0) { return; }

            int removeIndex = volume.Index;
            if (!ReferenceEquals(volume, volumeInstances[removeIndex]))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"{volume}(index = {removeIndex}) != {volumeInstances[removeIndex]}(index = {volume.Index})");
                }
                goto Finally;
            }
            PauseUpdate = true;

            // erase
            int lastIndex = volumeInstances.Count - 1;
            volumeInstances[removeIndex] = volumeInstances[lastIndex];
            volumeInstances[removeIndex].Index = removeIndex;
            volumeInstances.RemoveAt(lastIndex);
            instanceTransforms.RemoveAtSwapBack(removeIndex);
            Erase(instancesLocalBounds, removeIndex, lastIndex);
            Erase(instancesWorldBounds, removeIndex, lastIndex);
            if (lastIndex == 0)
            {
                Assert.IsTrue(volumeInstances.Count == 0, "ins not empty");
            }

            count--;
            Assert.AreEqual(count, volumeInstances.Count, nameof(count));
            Assert.AreEqual(count, instanceTransforms.length, nameof(instanceTransforms));
            OnRemoveVolume?.Invoke(this, removeIndex);
Finally:
            volume.Index = -1;


            //if (UnityEngine.Application.isEditor)
            //{
            //    Debug.Log($"remove => {count}");
            //    for (int i = 0; i < count; i++)
            //    {
            //        Debug.Log(volumeInstances[i].transform.name);
            //        Assert.AreEqual(volumeInstances[i].Index, i);
            //        Assert.AreEqual(volumeInstances[i].transform, instanceTransforms[i]);
            //        Assert.AreEqual(volumeInstances[i].transform.localToWorldMatrix, instancesLocalToWorld[i]);
            //        Bounds _lb = default;
            //        volumeInstances[i].GetLocalBounds(&_lb);
            //        Assert.AreEqual(_lb, instancesLocalBounds[i]);
            //    }
            //}
        }


        public int Count => count;
        public Bounds[] BoundsRef => bounds;
        public IReadOnlyList<IAABBCullingVolume> VolumesRef => volumeInstances != null
            ? volumeInstances : Array.Empty<IAABBCullingVolume>();

        /// <summary>
        /// 加入了一个剔除物体，传递总线引用和新加入的序号
        /// </summary>
        public static event Action<CullingGroupVolumeBus, int> OnAddVolume;
        /// <summary>
        /// 移除了一个剔除物体，
        /// </summary>
        public static event Action<CullingGroupVolumeBus, int> OnRemoveVolume;
        /// <summary>
        /// 总线被激活
        /// </summary>
        public static event Action<CullingGroupVolumeBus> OnSetup;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Erase<T>(NativeList<T> buffer, int index, int last) where T : unmanaged
        {
            //buffer[index] = buffer[last];
            var ptr = (T*)buffer.GetUnsafePtr();
            ptr[index] = ptr[last];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Realloc<T>(ref NativeList<T> nativeList, int capacity) where T : unmanaged
        {
            if (nativeList.IsCreated)
            {
                nativeList.ResizeUninitialized(capacity);
                nativeList.Length = capacity;
                if (nativeList.Capacity > capacity)
                {
                    nativeList.TrimExcess();
                }
            }
            else
            {
                nativeList = new NativeList<T>(capacity, AllocatorManager.Persistent)
                {
                    Length = capacity
                };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Realloc(ref TransformAccessArray transformAccessArray, int size)
        {
            if (transformAccessArray.isCreated)
            {
                transformAccessArray.capacity = size;
            }
            else
            {
                TransformAccessArray.Allocate(size, -1, out transformAccessArray);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Release<T>(ref NativeList<T> nativeList) where T : unmanaged
        {
            if (nativeList.IsCreated)
            {
                nativeList.Dispose();
                nativeList = default;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Release(ref TransformAccessArray array)
        {
            if (array.isCreated)
            {
                array.Dispose();
                array = default;
            }
        }
    }

    internal class EmptyCullingGroupVolume : ICullingGroupVolumeBus
    {
        public static readonly EmptyCullingGroupVolume instance = new EmptyCullingGroupVolume();

        public Bounds[] BoundsRef => Array.Empty<Bounds>();

        public int Count => 0;

        public IReadOnlyList<IAABBCullingVolume> VolumesRef => Array.Empty<IAABBCullingVolume>();

        public void Add(IAABBCullingVolume volume)
        {
            // pass
        }

        public void Remove(IAABBCullingVolume volume)
        {
            // pass
        }
    }
}