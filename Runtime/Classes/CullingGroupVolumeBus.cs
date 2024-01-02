using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

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
    /// 保存包围盒与剔除对象的全局总线
    /// </summary>
    internal class CullingGroupVolumeBus : MonoBehaviour, ICullingGroupVolumeBus
    {
        public const int defaultBufferLength = 1024;

        int count = 0;
        readonly List<IAABBCullingVolume> volumeInstances = new List<IAABBCullingVolume>(defaultBufferLength);
        Bounds[] bounds;
        NativeList<Matrix4x4> instancesLocalToWorld;
        NativeList<Bounds> instancesLocalBounds;


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
            Release(ref instancesLocalToWorld);
            Release(ref instancesLocalBounds);
        }

        unsafe void LateUpdate()
        {
            if (PauseUpdate) { return; }
            if (count == 0) { return; }

            const int updateSample = 3;
            int start = Time.frameCount % updateSample;
            var pLocalToWorld = (Matrix4x4*)instancesLocalToWorld.GetUnsafePtr();
            var pLocalBounds = (Bounds*)instancesLocalBounds.GetUnsafePtr();
            bool anyUpdated = false;
            for (int i = start; i < count; i += updateSample)
            {
                if (volumeInstances[i].VolumeUpdated)
                {
                    //pLocalBounds[i] = volumeInstances[i].LocalBounds;
                    volumeInstances[i].GetLocalBounds(pLocalBounds + i);
                    anyUpdated = true;
                }
                if (!volumeInstances[i].TransformStatic)
                {
                    //pLocalToWorld[i] = volumeInstances[i].LocalToWorld;
                    volumeInstances[i].GetLocalToWorld(pLocalToWorld + i);
                    anyUpdated = true;
                }
            }
            if (anyUpdated)
            {
                // job
                using var volumes = new NativeArray<Bounds>(count, Allocator.TempJob,
                     NativeArrayOptions.UninitializedMemory);
                new TransposeBoundsFor
                {
                    localToWorld = instancesLocalToWorld.AsParallelReader().Reinterpret<float4x4>(),
                    inputLocalBounds = instancesLocalBounds.AsParallelReader().Reinterpret<float3x2>(),
                    outputWorldBounds = volumes.Reinterpret<float3x2>(),
                }.Schedule(count, 64, default).Complete();

                fixed (Bounds* pBounds = bounds)
                {
                    UnsafeUtility.MemCpy(pBounds, volumes.GetUnsafePtr(), count * sizeof(Bounds));
                }
            }
        }

        public unsafe void Add(IAABBCullingVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException("volume is Nothing");
            }
            PauseUpdate = true;

            int addIndex = count;
            if (addIndex + 1 > count)
            {
                int newLength = Mathf.Max(defaultBufferLength, count * 2);
                Realloc(ref instancesLocalToWorld, newLength);
                Realloc(ref instancesLocalBounds, newLength);
                AABBCullingHelper.Realloc(ref bounds, newLength);
            }
            volumeInstances.Add(volume);
            bounds[addIndex] = volume.Volume;
            volume.GetLocalToWorld((Matrix4x4*)instancesLocalToWorld.GetUnsafePtr() + addIndex);
            volume.GetLocalBounds((Bounds*)instancesLocalBounds.GetUnsafePtr() + addIndex);
            volume.Index = addIndex;

            count++;

            OnAddVolume?.Invoke(this, addIndex);
        }

        public void Remove(IAABBCullingVolume volume)
        {
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
            instancesLocalToWorld.RemoveAtSwapBack(removeIndex);
            instancesLocalBounds.RemoveAtSwapBack(removeIndex);
            if (lastIndex == 0)
            {
                Assert.IsTrue(volumeInstances.Count == 0, "ins not empty");
            }

            OnRemoveVolume?.Invoke(this, removeIndex);
Finally:
            volume.Index = -1;
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
        static unsafe void Release<T>(ref NativeList<T> nativeList) where T : unmanaged
        {
            if (nativeList.IsCreated)
            {
                nativeList.Dispose();
                nativeList = default;
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