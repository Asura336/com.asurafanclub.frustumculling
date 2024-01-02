using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Com.Culling.AABBCullingHelper;

namespace Com.Culling
{
    public class CsAABBCullingGroup : SimpleAABBCullingGroup, IDisposable
    {
        const int sizeofBounds = sizeof(float) * 6;

        static readonly int id_vpMatrix = Shader.PropertyToID("vpMatrix");
        static readonly int id_inputBounds = Shader.PropertyToID("inputBounds");
        static readonly int id_outputHeight = Shader.PropertyToID("outputHeight");
        static readonly int id_outputVisible = Shader.PropertyToID("outputVisible");

        /* bounds:
         *   center : float3
         *   extents : float3
         * 
         * outputHeight : float
         * outputVisible : uint
         */

        public ComputeShader cullingCs;

        int kernel_Culling;
        ComputeBuffer cullingInputBounds;
        ComputeBuffer cullingOutputHeight;
        ComputeBuffer cullingOutputVisible;
        float[] cullingOutputHeightData;
        uint[] cullingOutputVisibleData;

        public void Dispose()
        {
            Realloc(ref cullingInputBounds, 0, 0);
            Realloc(ref cullingOutputHeight, 0, 0);
            Realloc(ref cullingOutputVisible, 0, 0);
        }

        public override void Setup(Bounds[] array)
        {
            base.Setup(array);

            Realloc(ref cullingInputBounds, capacity, sizeofBounds);
            Realloc(ref cullingOutputHeight, capacity, sizeof(float));
            Realloc(ref cullingOutputVisible, capacity, sizeof(uint));

            Realloc<float>(ref cullingOutputHeightData, capacity);
            Realloc<uint>(ref cullingOutputVisibleData, capacity);

            kernel_Culling = cullingCs.FindKernel("Culling");
            cullingCs.SetBuffer(kernel_Culling, id_inputBounds, cullingInputBounds);
            cullingCs.SetBuffer(kernel_Culling, id_outputHeight, cullingOutputHeight);
            cullingCs.SetBuffer(kernel_Culling, id_outputVisible, cullingOutputVisible);
        }

        protected override void Culling(AABBCullingContext[] dst, Bounds[] src, int count)
        {
            cullingInputBounds.SetData(bounds, 0, 0, count);
            cullingCs.SetMatrix(id_vpMatrix, vpMatrix);

            const int groupSizeX = 64;
            int threadNumberX = count / groupSizeX + (count % groupSizeX != 0 ? 1 : 0);
            cullingCs.Dispatch(kernel_Culling, threadNumberX, 1, 1);

            /* 计算着色器很高效，但性能亏在读取回传数据上
             * 主要开销在这个 GetData 上
             * 
             * count = 2000
             * SetData: about 0.01 ms
             * GetData: 2 times, total about 0.3~1.0 ms
             */

            cullingOutputHeight.GetData(cullingOutputHeightData, 0, 0, count);
            cullingOutputVisible.GetData(cullingOutputVisibleData, 0, 0, count);
            for (int i = 0; i < count; i++)
            {
                AABBCullingContext o = default;
                o.height = cullingOutputHeightData[i];
                o.visible = cullingOutputVisibleData[i] != 0;
                dst[i] = o;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Realloc(ref ComputeBuffer buffer, int count, int stride)
        {
            buffer?.Dispose();
            buffer = count != 0
                ? new ComputeBuffer(count, stride)
                : null;
        }
    }
}
