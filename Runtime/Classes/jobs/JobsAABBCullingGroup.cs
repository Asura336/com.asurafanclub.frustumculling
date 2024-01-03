using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Com.Culling
{
    /// <summary>
    /// 使用 Job System 实现剔除和检查事件过程的剔除组，应对多物体（> 1000）效率更高。
    /// </summary>
    public class JobsAABBCullingGroup : SimpleAABBCullingGroup
    {
        protected override unsafe void Culling(AABBCullingContext[] dst, Bounds[] src, int count)
        {
            var cameraForward = cameraLocalToWorldMatrix.MultiplyVector(Vector3.forward);
            var cameraPosition = cameraLocalToWorldMatrix.MultiplyPoint(Vector3.zero);
            // 2000 bounds, 0.07 ms
            var inputBounds = new NativeArray<Bounds>(src, Allocator.TempJob)
                .Reinterpret<float3x2>();
            var outputCtxArr = new NativeArray<AABBCullingContext>(count,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var planes = new NativeArray<Plane>(frustumPlanes, Allocator.TempJob);
            var job = new CullingJobFor
            {
                vpMatrix = vpMatrix,
                cameraForward = cameraForward,
                cameraPosition = cameraPosition,
                bounds = inputBounds,
                orthographic = referenceCamera.orthographic,
                planes = planes.Reinterpret<float4>(),
                dst = outputCtxArr.Slice(0, count)
            }.Schedule(count, 64, default);
            job.Complete();

            // copy to
            var pOutputCtxArr = (AABBCullingContext*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(outputCtxArr);
            for (int i = 0; i < count; i++)
            {
                dst[i] = pOutputCtxArr[i];
            }

            outputCtxArr.Dispose();
        }

        [BurstCompile(CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Standard, FloatMode = FloatMode.Fast)]
        struct CullingJobFor : IJobParallelFor
        {
            [ReadOnly] public float4x4 vpMatrix;
            [ReadOnly] public float3 cameraPosition;
            [ReadOnly] public float3 cameraForward;
            [ReadOnly] public bool orthographic;

            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<float3x2> bounds;

            [NativeDisableParallelForRestriction]
            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<float4> planes;

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
                float3 bMin = center - extents, bMax = center + extents;
                var ps = stackalloc float4[8];
                // world pos
                ps[0] = float4(bMin.x, bMin.y, bMin.z, 1);
                ps[1] = float4(bMin.x, bMin.y, bMax.z, 1);
                ps[2] = float4(bMin.x, bMax.y, bMin.z, 1);
                ps[3] = float4(bMin.x, bMax.y, bMax.z, 1);
                ps[4] = float4(bMax.x, bMin.y, bMin.z, 1);
                ps[5] = float4(bMax.x, bMin.y, bMax.z, 1);
                ps[6] = float4(bMax.x, bMax.y, bMin.z, 1);
                ps[7] = float4(bMax.x, bMax.y, bMax.z, 1);

                float2 maxXY = float.MinValue, minXY = float.MaxValue;
                float maxZ = float.MinValue, minZ = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    var screenPos = WorldToViewportPoint(ps[i]);
                    var screenPos_xy = screenPos.xy;
                    maxXY = max(maxXY, screenPos_xy);
                    maxZ = max(maxZ, screenPos.z);
                    minXY = min(minXY, screenPos_xy);
                    minZ = min(minZ, screenPos.z);
                }
                if (maxZ * minZ < 0)
                {
                    // 如果包围盒顶点有的在视野前方有的在视野后方，认为表面始终靠近相机
                    // 设置高度为 1（最近）
                    return 1;
                }
                else
                {
                    // 改进的 LOD 尺寸，以横纵向高度的最大值为依据
                    var delta = maxXY - minXY;
                    return max(delta.x, delta.y);
                }
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
                var viewportPos = float3(clipPos_xy * 0.5f + 0.5f, clipPos.z);
                return viewportPos;
            }
        }

        protected override unsafe void CheckEvent(AABBCullingContext[] before, AABBCullingContext[] after, int count)
        {
            var prevCtx = new NativeArray<AABBCullingContext>(before, Allocator.TempJob);
            var afterCtx = new NativeArray<AABBCullingContext>(after, Allocator.TempJob);
            var prevStates = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var currStates = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var lods = lodLevels is null ? default : new NativeArray<float>(lodLevels, Allocator.TempJob);
            var job = new CheckEventJobFor
            {
                prevCtxs = prevCtx,
                currCtxs = afterCtx,
                prevStates = prevStates,
                currStates = currStates,
                lodLevels = lods
            }.Schedule(count, 128, default);
            job.Complete();

            byte* pPrevStates = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(prevStates);
            byte* pCurrStates = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(currStates);
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
            prevStates.Dispose();
            currStates.Dispose();
        }

        [BurstCompile(CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Standard, FloatMode = FloatMode.Fast)]
        struct CheckEventJobFor : IJobParallelFor
        {
            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<AABBCullingContext> prevCtxs;

            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<AABBCullingContext> currCtxs;

            [WriteOnly] public NativeArray<byte> prevStates;
            [WriteOnly] public NativeArray<byte> currStates;

            [NativeDisableParallelForRestriction]
            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<float> lodLevels;

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
                if (!lodLevels.IsCreated) { return 0; }
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