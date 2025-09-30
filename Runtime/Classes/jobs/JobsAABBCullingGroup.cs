using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility;
using static Unity.Mathematics.math;

namespace Com.Culling
{
    /// <summary>
    /// 使用 Job System 实现剔除和检查事件过程的剔除组，应对多物体（> 1000）效率更高。
    /// </summary>
    public unsafe class JobsAABBCullingGroup : SimpleAABBCullingGroup
    {
        const int defBuffLen = 1024;

        //--------
        // Culling
        NativeArray<Plane> planesBuff;
        NativeArray<Bounds> inputBoundsBuff;
        NativeArray<AABBCullingContext> outputCtxBuff; AABBCullingContext* outputCtxBuffPnt;

        //--------
        // CheckEvent
        NativeArray<AABBCullingContext> prevCtxBuff;
        NativeArray<AABBCullingContext> nextCtxBuff;
        NativeArray<byte> prevStatesBuff; byte* prevStatesBuffPnt;
        NativeArray<byte> currStatesBuff; byte* currStatesBuffPnt;
        NativeArray<float> lodsBuffer;

        public override void ReleasePersistBuffers()
        {
            planesBuff.Dispose(); planesBuff = default;
            inputBoundsBuff.Dispose(); inputBoundsBuff = default;
            outputCtxBuff.Dispose(); outputCtxBuff = default; outputCtxBuffPnt = null;

            prevCtxBuff.Dispose(); prevCtxBuff = default;
            nextCtxBuff.Dispose(); nextCtxBuff = default;
            prevStatesBuff.Dispose(); prevStatesBuff = default; prevStatesBuffPnt = null;
            currStatesBuff.Dispose(); currStatesBuff = default; currStatesBuffPnt = null;
            lodsBuffer.Dispose(); lodsBuffer = default;
        }

        static unsafe void EnsurePersistBuffer<T>(ref NativeArray<T> na, T[] arr, int minBuffLen = defBuffLen) where T : unmanaged
        {
            int newLength = MathHelpers.CeilPow2(Math.Max(defBuffLen, arr.Length));
            if (na.IsCreated)
            {
                if (na.Length < arr.Length)
                {
                    na.Dispose();
                    na = new NativeArray<T>(newLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }
            else
            {
                na = new NativeArray<T>(newLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            fixed (T* arrPnt = arr)
            {
                UnsafeUtility.MemCpy(GetUnsafeBufferPointerWithoutChecks(na), arrPnt, arr.Length * sizeof(T));
            }
        }
        static unsafe void EnsurePersistBuffer<T>(ref NativeArray<T> na, ref T* pnt, int capacity) where T : unmanaged
        {
            int newLength = MathHelpers.CeilPow2(Math.Max(defBuffLen, capacity));
            if (na.IsCreated)
            {
                if (na.Length < capacity)
                {
                    na.Dispose();
                    na = new NativeArray<T>(newLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }
            else
            {
                na = new NativeArray<T>(newLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            pnt = na.IsCreated ? (T*)GetUnsafeBufferPointerWithoutChecks(na) : null;
        }


        protected override unsafe void Culling(AABBCullingContext[] dst, Bounds[] src, int count)
        {
            Vector3 cameraForward = default;
            cameraLocalToWorldMatrix.MulVector(Vector3.forward, ref cameraForward);
            Vector3 cameraPosition = default;
            cameraLocalToWorldMatrix.MulPoint3x4(Vector3.zero, ref cameraPosition);
            // 2000 bounds, 0.07 ms
            EnsurePersistBuffer(ref inputBoundsBuff, src);
            EnsurePersistBuffer(ref outputCtxBuff, ref outputCtxBuffPnt, count);
            EnsurePersistBuffer(ref planesBuff, frustumPlanes);
            var job = new CullingJobFor
            {
                vpMatrix = vpMatrix,
                cameraForward = cameraForward,
                cameraPosition = cameraPosition,
                bounds = inputBoundsBuff.Reinterpret<float3x2>().Slice(0, count),
                orthographic = referenceCamera.orthographic,
                planes = planesBuff.Reinterpret<float4>().Slice(0, 6),
                dst = outputCtxBuff.Slice(0, count)
            }.Schedule(count, 64, default);
            job.Complete();

            // copy to
            fixed (AABBCullingContext* p_dst = dst)
            {
                UnsafeUtility.MemCpy(p_dst, outputCtxBuffPnt, sizeof(AABBCullingContext) * count);
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Standard, FloatMode = FloatMode.Fast,
            DisableSafetyChecks = true)]
        struct CullingJobFor : IJobParallelFor
        {
            [ReadOnly] public float4x4 vpMatrix;
            [ReadOnly] public float3 cameraPosition;
            [ReadOnly] public float3 cameraForward;
            [ReadOnly] public bool orthographic;
            [ReadOnly] public NativeSlice<float3x2> bounds;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeSlice<float4> planes;

            [WriteOnly] public NativeSlice<AABBCullingContext> dst;

            public unsafe void Execute(int index)
            {
                float3 center = bounds[index].c0, extents = bounds[index].c1;
                AABBCullingContext cullingResult = default;
                cullingResult.height = HeightInViewport(center, extents);
                cullingResult.visible = TestPlanesAABB(center, extents);
                dst[index] = cullingResult;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe bool TestPlanesAABB(in float3 center, in float3 extents)
            {
                int count = planes.Length;
                for (int i = 0; i < count; i++)
                {
                    var normal = planes[i].xyz;
                    float distance = planes[i].w;
                    var testPoint = center + extents * sign(normal);
                    if (dot(normal, testPoint) + distance < -1e-10f)
                    {
                        return false;
                    }
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly unsafe float HeightInViewport(in float3 center, in float3 extents)
            {
                float3 maxXYZ = float.MinValue, minXYZ = float.MaxValue;

                float3 bMin = center - extents, bMax = center + extents;
                MakeMinMaxXYZ(float4(bMin.x, bMin.y, bMin.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMin.x, bMin.y, bMax.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMin.x, bMax.y, bMin.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMin.x, bMax.y, bMax.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMax.x, bMin.y, bMin.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMax.x, bMin.y, bMax.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMax.x, bMax.y, bMin.z, 1), ref maxXYZ, ref minXYZ);
                MakeMinMaxXYZ(float4(bMax.x, bMax.y, bMax.z, 1), ref maxXYZ, ref minXYZ);

                if (maxXYZ.z * minXYZ.z < 0)
                {
                    // 如果包围盒顶点有的在视野前方有的在视野后方，认为表面始终靠近相机
                    // 设置高度为 1（最近）
                    return 1;
                }
                else
                {
                    // 改进的 LOD 尺寸，以横纵向高度的最大值为依据
                    var delta = maxXYZ.xy - minXYZ.xy;
                    return max(delta.x, delta.y);
                }


                //--------
                // Origin
                //float3 bMin = center - extents, bMax = center + extents;
                //var ps = stackalloc float4[8];
                //// world pos
                //ps[0] = float4(bMin.x, bMin.y, bMin.z, 1);
                //ps[1] = float4(bMin.x, bMin.y, bMax.z, 1);
                //ps[2] = float4(bMin.x, bMax.y, bMin.z, 1);
                //ps[3] = float4(bMin.x, bMax.y, bMax.z, 1);
                //ps[4] = float4(bMax.x, bMin.y, bMin.z, 1);
                //ps[5] = float4(bMax.x, bMin.y, bMax.z, 1);
                //ps[6] = float4(bMax.x, bMax.y, bMin.z, 1);
                //ps[7] = float4(bMax.x, bMax.y, bMax.z, 1);

                //float2 maxXY = float.MinValue, minXY = float.MaxValue;
                //float maxZ = float.MinValue, minZ = float.MaxValue;
                //for (int i = 0; i < 8; i++)
                //{
                //    var screenPos = WorldToViewportPoint(ps[i]);
                //    var screenPos_xy = screenPos.xy;
                //    maxXY = max(maxXY, screenPos_xy);
                //    maxZ = max(maxZ, screenPos.z);
                //    minXY = min(minXY, screenPos_xy);
                //    minZ = min(minZ, screenPos.z);
                //}
                //if (maxZ * minZ < 0)
                //{
                //    // 如果包围盒顶点有的在视野前方有的在视野后方，认为表面始终靠近相机
                //    // 设置高度为 1（最近）
                //    return 1;
                //}
                //else
                //{
                //    // 改进的 LOD 尺寸，以横纵向高度的最大值为依据
                //    var delta = maxXY - minXY;
                //    return max(delta.x, delta.y);
                //}
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly float3 WorldToViewportPoint(in float4 pos)
            {
                var clipPos = mul(vpMatrix, pos);
                var clipPos_xy = clipPos.xy;
                if (!orthographic)
                {
                    clipPos_xy /= clipPos.w;
                }
                //var viewportPos = float3(clipPos_xy * 0.5f + 0.5f, clipPos.z);
                var viewportPos = float3(mad(clipPos_xy, 0.5f, 0.5f), clipPos.z);
                return viewportPos;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly void MakeMinMaxXYZ(in float4 p, ref float3 maxXYZ, ref float3 minXYZ)
            {
                float3 screenPos = WorldToViewportPoint(p);
                maxXYZ = max(maxXYZ, screenPos);
                minXYZ = min(minXYZ, screenPos);
            }
        }

        protected override unsafe void CheckEvent(AABBCullingContext[] before, AABBCullingContext[] after, int count)
        {
            if (before.Length == 0 || after.Length == 0) { return; }

            EnsurePersistBuffer(ref prevCtxBuff, before);
            EnsurePersistBuffer(ref nextCtxBuff, after);
            EnsurePersistBuffer(ref prevStatesBuff, ref prevStatesBuffPnt, count);
            EnsurePersistBuffer(ref currStatesBuff, ref currStatesBuffPnt, count);
            if (lodLevels != null)
            {
                EnsurePersistBuffer(ref lodsBuffer, lodLevels, minBuffLen: 4);
            }

            var job = new CheckEventJobFor
            {
                prevCtxs = prevCtxBuff.Slice(0, before.Length),
                currCtxs = nextCtxBuff.Slice(0, after.Length),
                prevStates = prevStatesBuff.Slice(0, count),
                currStates = currStatesBuff.Slice(0, count),
                lodLevels = lodLevels is null ? default : lodsBuffer.Slice(0, lodLevels.Length),
            }.Schedule(count, 128, default);
            job.Complete();

            byte* pPrevStates = prevStatesBuffPnt;
            byte* pCurrStates = currStatesBuffPnt;
            for (int i = 0; i < count; i++)
            {
                if (pPrevStates[i] != pCurrStates[i])
                {
                    // send...
                    AABBCullingGroupEvent ctx = default;
                    ctx.index = i;
                    ctx.prevState = pPrevStates[i];
                    ctx.currState = pCurrStates[i];
                    onStateChanged?.Invoke(ctx);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Standard, FloatMode = FloatMode.Fast)]
        struct CheckEventJobFor : IJobParallelFor
        {
            [ReadOnly] public NativeSlice<AABBCullingContext> prevCtxs;
            [ReadOnly] public NativeSlice<AABBCullingContext> currCtxs;

            [WriteOnly] public NativeSlice<byte> prevStates;
            [WriteOnly] public NativeSlice<byte> currStates;

            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeSlice<float> lodLevels;

            public void Execute(int index)
            {
                byte prevState = (byte)(HeightToLodLevel(prevCtxs[index].height) & AABBCullingGroupEvent.lodLevelMask);
                byte currState = (byte)(HeightToLodLevel(currCtxs[index].height) & AABBCullingGroupEvent.lodLevelMask);

                if (!prevCtxs[index].visible && currCtxs[index].visible) { currState |= AABBCullingGroupEvent.visibleFlag; }
                if (prevCtxs[index].visible && !currCtxs[index].visible) { prevState |= AABBCullingGroupEvent.visibleFlag; }

                prevStates[index] = prevState;
                currStates[index] = currState;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int HeightToLodLevel(float height)
            {
                if (lodLevels.Length == 0) { return 0; }
                // 假设 lod levels 总是降序
                int len = lodLevels.Length;
                for (int i = 0; i < len; i++)
                {
                    if (height > lodLevels[i])
                    {
                        return i;
                    }
                }
                return len - 1;
            }
        }
    }
}