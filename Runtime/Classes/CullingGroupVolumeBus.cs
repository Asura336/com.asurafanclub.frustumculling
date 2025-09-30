using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Jobs;
using static Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility;

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
    internal unsafe class CullingGroupVolumeBus : MonoBehaviour, ICullingGroupVolumeBus
    {
        public const int defaultBufferLength = 1024;
        const int updateSample = 13;

        static readonly Bounds Infinity = new Bounds(default, Vector3.one * float.PositiveInfinity);

        int count = 0;
        int unmanagedCapacity = 0;
        readonly List<IAABBCullingVolume> volumeInstances = new List<IAABBCullingVolume>(defaultBufferLength);
        readonly HashSet<IAABBCullingVolume> volumeInstSet = new HashSet<IAABBCullingVolume>(defaultBufferLength);
        Bounds[] bounds = Array.Empty<Bounds>();
        TransformAccessArray instanceTransforms;
        NativeArray<Bounds> instancesLocalBounds;
        Bounds* instancesLocalBoundsPnt;
        NativeArray<Bounds> instancesWorldBounds;
        Bounds* instancesWorldBoundsPnt;

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
            //OnSetup?.Invoke(this);
            InvokeEvent(sbuffer_OnSetup, this);
        }

        void OnDestroy()
        {
            destroyed = true;
            Release(ref instancesLocalBounds); instancesLocalBoundsPnt = null;
            Release(ref instancesWorldBounds); instancesWorldBoundsPnt = null;
            Release(ref instanceTransforms);
        }

        unsafe void LateUpdate()
        {
            if (PauseUpdate || count == 0 || destroyed) { return; }

            // input
            int start = Time.frameCount % updateSample;
            var read_volumeInstances = volumeInstances.UnsafeGetItems();
            for (int i = start; i < count; i += updateSample)
            {
                ref readonly var volumeInst = ref read_volumeInstances[i];
                if (volumeInst.VolumeUpdated)
                {
                    volumeInst.GetLocalBounds(instancesLocalBoundsPnt + i);
                }
            }

            var getWorldBoundsJob = new GetWorldBoundsJobFor
            {
                length = count,
                localBounds = instancesLocalBounds,
                worldBounds = instancesWorldBounds,
            };
            getWorldBoundsJob.ScheduleReadOnly(instanceTransforms, 64, default).Complete();
            fixed (Bounds* pBounds = bounds)
            {
                UnsafeUtility.MemCpy(pBounds, instancesWorldBoundsPnt, sizeof(Bounds) * count);
            }
        }

        public unsafe void Add(IAABBCullingVolume volume)
        {
            if (destroyed) { return; }
            //if (volumeInstances.Contains(volume))
            if (volumeInstSet.Contains(volume))
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
                unmanagedCapacity = Mathf.Max(defaultBufferLength, MathHelpers.CeilPow2(count + 1));
                Realloc(ref instancesLocalBounds, unmanagedCapacity);
                instancesLocalBoundsPnt = (Bounds*)GetUnsafeBufferPointerWithoutChecks(instancesLocalBounds);
                Realloc(ref instancesWorldBounds, unmanagedCapacity);
                instancesWorldBoundsPnt = (Bounds*)GetUnsafeBufferPointerWithoutChecks(instancesWorldBounds);
                Realloc(ref instanceTransforms, unmanagedCapacity);
                AABBCullingHelper.Realloc(ref bounds, unmanagedCapacity);
            }
            volumeInstances.Add(volume);
            instanceTransforms.Add(volume.transform);
            Assert.AreEqual(instanceTransforms.length, count + 1);

            //bounds[addIndex] = volume.Volume;
            bounds[addIndex] = Infinity;

            volume.GetLocalBounds(instancesLocalBoundsPnt + addIndex);

            //instancesWorldBoundsPnt[addIndex] = volume.Volume;
            instancesWorldBoundsPnt[addIndex] = Infinity;

            volume.Index = addIndex;

            count++;
            volumeInstSet.Add(volume);
            Assert.AreEqual(volumeInstSet.Count, count);

            //OnAddVolume?.Invoke(this, addIndex);
            InvokeEvent(sbuffer_OnAddVolume, this, addIndex);
        }

        public unsafe void Remove(IAABBCullingVolume volume)
        {
            if (destroyed) { return; }
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }
            if (!volume.Valid) { return; }
            if (volumeInstances.Count == 0)
            {
                Assert.AreEqual(volumeInstSet.Count, 0);
                return;
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
            PauseUpdate = true;

            // erase
            int lastIndex = volumeInstances.Count - 1;
            var rw_volumeInstances = volumeInstances.UnsafeGetItems();
            rw_volumeInstances[removeIndex] = rw_volumeInstances[lastIndex];
            rw_volumeInstances[removeIndex].Index = removeIndex;
            volumeInstances.RemoveAt(lastIndex);
            instanceTransforms.RemoveAtSwapBack(removeIndex);
            Erase(instancesLocalBoundsPnt, removeIndex, lastIndex);
            Erase(instancesWorldBoundsPnt, removeIndex, lastIndex);
            if (lastIndex == 0)
            {
                Assert.IsTrue(volumeInstances.Count == 0, "ins not empty");
            }

            count--;
            Assert.AreEqual(count, volumeInstances.Count, nameof(count));
            Assert.AreEqual(count, instanceTransforms.length, nameof(instanceTransforms));
            //OnRemoveVolume?.Invoke(this, removeIndex);
            InvokeEvent(sbuffer_OnRemoveVolume, this, removeIndex);
Finally:
            volume.Index = -1;
            volumeInstSet.Remove(volume);

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

        static readonly HashSet<Action<CullingGroupVolumeBus, int>> sbuffer_OnAddVolume = new(defaultBufferLength);
        static readonly HashSet<Action<CullingGroupVolumeBus, int>> sbuffer_OnRemoveVolume = new(defaultBufferLength);
        static readonly HashSet<Action<CullingGroupVolumeBus>> sbuffer_OnSetup = new(defaultBufferLength);
        static void InvokeEvent<TSet, TArg>(TSet events, TArg arg)
            where TSet : HashSet<Action<TArg>>
        {
            foreach (var e in events) { e?.Invoke(arg); }
        }
        static void InvokeEvent<TSet, TArg0, TArg1>(TSet events, TArg0 arg0, TArg1 arg1)
            where TSet : HashSet<Action<TArg0, TArg1>>
        {
            foreach (var e in events) { e?.Invoke(arg0, arg1); }
        }

        /// <summary>
        /// 加入了一个剔除物体，传递总线引用和新加入的序号
        /// </summary>
        public static event Action<CullingGroupVolumeBus, int> OnAddVolume
        {
            add => sbuffer_OnAddVolume.Add(value);
            remove => sbuffer_OnAddVolume.Remove(value);
        }
        /// <summary>
        /// 移除了一个剔除物体，
        /// </summary>
        public static event Action<CullingGroupVolumeBus, int> OnRemoveVolume
        {
            add => sbuffer_OnRemoveVolume.Add(value);
            remove => sbuffer_OnRemoveVolume.Remove(value);
        }
        /// <summary>
        /// 总线被激活
        /// </summary>
        public static event Action<CullingGroupVolumeBus> OnSetup
        {
            add => sbuffer_OnSetup.Add(value);
            remove => sbuffer_OnSetup.Remove(value);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Erase(Bounds* ptr, int index, int last)
        {
            //buffer[index] = buffer[last];
            UnsafeUtility.MemCpy(ptr + index, ptr + last, sizeof(Bounds));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Realloc<T>(ref NativeArray<T> v, int capacity) where T : unmanaged
        {
            if (v.IsCreated)
            {
                var newArr = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                UnsafeUtility.MemCpy(GetUnsafeBufferPointerWithoutChecks(newArr),
                    GetUnsafeBufferPointerWithoutChecks(v),
                    Math.Min(capacity, v.Length) * sizeof(T));
                v.Dispose();
                v = newArr;
            }
            else
            {
                v = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Realloc(ref TransformAccessArray v, int size)
        {
            if (v.isCreated)
            {
                v.capacity = size;
            }
            else
            {
                TransformAccessArray.Allocate(size, -1, out v);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Release<T>(ref NativeArray<T> v) where T : unmanaged
        {
            if (v.IsCreated)
            {
                v.Dispose();
                v = default;
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