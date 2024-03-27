using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace Com.Culling
{
    /// <summary>
    /// 剔除组的可见性或者 LOD 等级变化时传递消息的类型
    /// </summary>
    /// <param name="eventContext"></param>
    public delegate void AABBCullingStateChanged(AABBCullingGroupEvent eventContext);

    /// <summary>
    /// 由剔除组件内部传递的剔除信息，包含视口空间下的高度（取值 [0, 1]）和可见性
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AABBCullingContext
    {
        public float height;
        public bool visible;

        public static readonly AABBCullingContext Visible = new AABBCullingContext
        {
            height = 1,
            visible = true
        };

        public static readonly AABBCullingContext Invisible = new AABBCullingContext
        {
            height = 0,
            visible = false
        };
    }

    /// <summary>
    /// 剔除组的可见性或者 LOD 等级变化时传递消息的内容
    /// </summary>
    public struct AABBCullingGroupEvent
    {
        public const byte visibleFlag = 0b_1000_0000;
        public const byte lodLevelMask = 0b_0111_1111;

        public int index;
        public byte prevState;
        public byte currState;

        public static readonly AABBCullingGroupEvent Empty = new AABBCullingGroupEvent
        {
            index = -1,
        };

        public readonly bool IsVisible => (currState & visibleFlag) != 0;
        public readonly bool WasVisible => (prevState & visibleFlag) != 0;
        public readonly bool HasBecomeVisible => IsVisible && !WasVisible;
        public readonly bool HasBecomeInvisible => !IsVisible && WasVisible;
        public readonly int PreviousLodLevel => prevState & lodLevelMask;
        public readonly int CurrentLodLevel => currState & lodLevelMask;
    }

    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    internal static partial class AABBCullingHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Realloc<T>(ref T[] dst, int size)
        {
            if (dst is null) { dst = new T[size]; }
            else if (dst.Length != size) { Array.Resize(ref dst, size); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDescending(float[] values)
        {
            int len = values.Length;
            for (int i = 1; i < len; i++)
            {
                if (values[i] > values[i - 1])
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void VpMatrix(ref Matrix4x4 vpMatrix, Camera camera)
        {
            Matrix4x4 v = camera.worldToCameraMatrix,
                p = camera.projectionMatrix;
            p.Mul(v, ref vpMatrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HeightToLodLevel(float height, float[] levels)
        {
            if (levels is null) { return 0; }
            // 假设 lod levels 总是降序
            int len = levels.Length;
            for (int i = 0; i < len; i++)
            {
                if (height > levels[i])
                {
                    return i;
                }
            }
            return len - 1;
        }

        [BurstCompile]
        public static void ToPoint(in Vector3 p, ref Vector4 o)
        {
            o.x = p.x;
            o.y = p.y;
            o.z = p.z;
            o.w = 1;
        }

        [BurstCompile]
        public static bool EqualsMatrix4x4(in Matrix4x4 lhs, in Matrix4x4 rhs)
        {
            return lhs.m00 == rhs.m00 && lhs.m01 == rhs.m01 && lhs.m02 == rhs.m02 && lhs.m03 == rhs.m03
                && lhs.m10 == rhs.m10 && lhs.m11 == rhs.m11 && lhs.m12 == rhs.m12 && lhs.m13 == rhs.m13
                && lhs.m20 == rhs.m20 && lhs.m21 == rhs.m21 && lhs.m22 == rhs.m22 && lhs.m23 == rhs.m23
                && lhs.m30 == rhs.m30 && lhs.m31 == rhs.m31 && lhs.m32 == rhs.m32 && lhs.m33 == rhs.m33;
        }

        [BurstCompile]
        public static bool EqualsBounds(in Bounds lhs, in Bounds rhs)
        {
            return lhs.center.Equals(rhs.center) && lhs.extents.Equals(rhs.extents);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b) => a > b ? b : a;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b) => a < b ? b : a;

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe static void Mul(this in Bounds bounds, in Matrix4x4 mul, ref Bounds result)
        {
            //var vector8 = stackalloc Vector3[8];
            //Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            //GetBoundsVerticesUnsafe(bounds, vector8);
            //for (int i = 0; i < 8; i++)
            //{
            //    Vector3 point = mul.MultiplyPoint3x4(vector8[i]);
            //    min = Vector3.Min(min, point);
            //    max = Vector3.Max(max, point);
            //}
            //result = new Bounds(Vector3.Lerp(min, max, 0.5f), max - min);

            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;

            Vector3 center = bounds.center, extents = bounds.extents;
            Vector3 _min = center - extents, _max = center + extents;
            Mul_TransformAndMinMax(new Vector3(_min.x, _min.y, _min.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_min.x, _min.y, _max.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_min.x, _max.y, _min.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_min.x, _max.y, _max.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_max.x, _min.y, _min.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_max.x, _min.y, _max.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_max.x, _max.y, _min.z), mul, ref min, ref max);
            Mul_TransformAndMinMax(new Vector3(_max.x, _max.y, _max.z), mul, ref min, ref max);

            result.extents = (max - min) / 2;
            result.center = result.extents + min;
            //result = new Bounds(Vector3.Lerp(min, max, 0.5f), max - min);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Mul_TransformAndMinMax(in Vector3 p, in Matrix4x4 mul, ref Vector3 min, ref Vector3 max)
        {
            Vector3 wp = default;
            MultiplyPoint3x4(mul, p, ref wp);
            min = Vector3.Min(min, wp);
            max = Vector3.Max(max, wp);
        }

        [BurstCompile]
        internal unsafe static void GetBoundsVerticesUnsafe(this in Bounds b, in Vector3* vector8)
        {
            Vector3 center = b.center, extents = b.extents;
            Vector3 min = center - extents, max = center + extents;
            float minX = min.x; float maxX = max.x;
            float minY = min.y; float maxY = max.y;
            float minZ = min.z; float maxZ = max.z;

            for (int i = 0; i < 8; i++)
            {
                float* itemHead = (float*)&vector8[i];
                itemHead[0] = i < 4 ? minX : maxX;
                itemHead[1] = (i / 2) % 2 == 0 ? minY : maxY;
                itemHead[2] = i % 2 == 0 ? minZ : maxZ;
                //vector8[i] = new Vector3(i < 4 ? minX : maxX, (i / 2) % 2 == 0 ? minY : maxY, i % 2 == 0 ? minZ : maxZ);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MultiplyPoint3x4(in Matrix4x4 mul, in Vector3 point, ref Vector3 result)
        {
            result.x = mul.m00 * point.x + mul.m01 * point.y + mul.m02 * point.z + mul.m03;
            result.y = mul.m10 * point.x + mul.m11 * point.y + mul.m12 * point.z + mul.m13;
            result.z = mul.m20 * point.x + mul.m21 * point.y + mul.m22 * point.z + mul.m23;
        }

        [BurstCompile]
        public static void Mul(this in Matrix4x4 lhs, in Matrix4x4 rhs, ref Matrix4x4 result)
        {
            result.m00 = lhs.m00 * rhs.m00 + lhs.m01 * rhs.m10 + lhs.m02 * rhs.m20 + lhs.m03 * rhs.m30;
            result.m01 = lhs.m00 * rhs.m01 + lhs.m01 * rhs.m11 + lhs.m02 * rhs.m21 + lhs.m03 * rhs.m31;
            result.m02 = lhs.m00 * rhs.m02 + lhs.m01 * rhs.m12 + lhs.m02 * rhs.m22 + lhs.m03 * rhs.m32;
            result.m03 = lhs.m00 * rhs.m03 + lhs.m01 * rhs.m13 + lhs.m02 * rhs.m23 + lhs.m03 * rhs.m33;
            result.m10 = lhs.m10 * rhs.m00 + lhs.m11 * rhs.m10 + lhs.m12 * rhs.m20 + lhs.m13 * rhs.m30;
            result.m11 = lhs.m10 * rhs.m01 + lhs.m11 * rhs.m11 + lhs.m12 * rhs.m21 + lhs.m13 * rhs.m31;
            result.m12 = lhs.m10 * rhs.m02 + lhs.m11 * rhs.m12 + lhs.m12 * rhs.m22 + lhs.m13 * rhs.m32;
            result.m13 = lhs.m10 * rhs.m03 + lhs.m11 * rhs.m13 + lhs.m12 * rhs.m23 + lhs.m13 * rhs.m33;
            result.m20 = lhs.m20 * rhs.m00 + lhs.m21 * rhs.m10 + lhs.m22 * rhs.m20 + lhs.m23 * rhs.m30;
            result.m21 = lhs.m20 * rhs.m01 + lhs.m21 * rhs.m11 + lhs.m22 * rhs.m21 + lhs.m23 * rhs.m31;
            result.m22 = lhs.m20 * rhs.m02 + lhs.m21 * rhs.m12 + lhs.m22 * rhs.m22 + lhs.m23 * rhs.m32;
            result.m23 = lhs.m20 * rhs.m03 + lhs.m21 * rhs.m13 + lhs.m22 * rhs.m23 + lhs.m23 * rhs.m33;
            result.m30 = lhs.m30 * rhs.m00 + lhs.m31 * rhs.m10 + lhs.m32 * rhs.m20 + lhs.m33 * rhs.m30;
            result.m31 = lhs.m30 * rhs.m01 + lhs.m31 * rhs.m11 + lhs.m32 * rhs.m21 + lhs.m33 * rhs.m31;
            result.m32 = lhs.m30 * rhs.m02 + lhs.m31 * rhs.m12 + lhs.m32 * rhs.m22 + lhs.m33 * rhs.m32;
            result.m33 = lhs.m30 * rhs.m03 + lhs.m31 * rhs.m13 + lhs.m32 * rhs.m23 + lhs.m33 * rhs.m33;
        }

        [BurstCompile]
        public static void Mul(this in Matrix4x4 lhs, in Vector4 vector, ref Vector4 result)
        {
            result.x = lhs.m00 * vector.x + lhs.m01 * vector.y + lhs.m02 * vector.z + lhs.m03 * vector.w;
            result.y = lhs.m10 * vector.x + lhs.m11 * vector.y + lhs.m12 * vector.z + lhs.m13 * vector.w;
            result.z = lhs.m20 * vector.x + lhs.m21 * vector.y + lhs.m22 * vector.z + lhs.m23 * vector.w;
            result.w = lhs.m30 * vector.x + lhs.m31 * vector.y + lhs.m32 * vector.z + lhs.m33 * vector.w;
        }
    }

    [BurstCompile(CompileSynchronously = true,
       FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    internal struct GetWorldBoundsJobFor : IJobParallelForTransform
    {
        public int length;
        [ReadOnly] public NativeArray<Bounds> localBounds;
        [WriteOnly] public NativeArray<Bounds> worldBounds;

        public void Execute(int index, TransformAccess transform)
        {
            Bounds _b = default;
            localBounds[index].Mul(transform.localToWorldMatrix, ref _b);
            worldBounds[index] = _b;
        }
    }
}
