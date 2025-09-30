#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Com.Culling
{
    /// <summary>
    /// https://github.com/DaZombieKiller/Bcl.CollectionsMarshal/tree/main
    /// https://learn.microsoft.com/zh-cn/dotnet/api/system.runtime.interopservices.collectionsmarshal?view=net-9.0
    /// </summary>
    internal static class CollectionsMarshalUtils
    {
        /// <summary>
        /// 获取列表的内部数组
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <returns></returns>
        public static T[] UnsafeGetItems<T>(this List<T> target)
        {
            if (target is null) { return null; }
            var listData = Unsafe.As<List<T>, ListDataHelper<T>>(ref target);
            return listData._items;
        }

        /// <summary>
        /// 获取列表的内部数组，返回 Span
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Span<T> UnsafeGetSpan<T>(this List<T> target, int start, int length)
        {
            if (target.Count > start + length)
            {
                throw new ArgumentException($"argument out of range: start({start}) + length({length}) = {start + length} > count({target.Count})");
            }
            var array = target.UnsafeGetItems();
            return array.AsSpan(start, length);
        }

        /// <summary>
        /// 获取列表的内部数组，返回 Span
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <returns></returns>
        public static Span<T> UnsafeGetSpan<T>(this List<T> target)
        {
            return target.UnsafeGetSpan(0, target.Count);
        }

        /// <summary>
        /// 获取列表的内部数组，返回 Span
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        public static Span<T> UnsafeGetSpan<T>(this List<T> target, int start)
        {
            return target.UnsafeGetSpan(0, target.Count).Slice(start);
        }

        public static void UnsafeSetCount<T>(this List<T> target, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Non-negative number required.");
            }

            // list._version++;
            ref readonly var listData = ref UnsafeUtility.As<List<T>, ListDataHelper<T>>(ref target);
            ref int version = ref listData._version;
            version++;

            ref readonly T[] items = ref listData._items;
            ref int size = ref listData._size;

            if (count > target.Capacity)
            {
                target.Capacity = target.GetNewCapacity(count);
            }

            if (size > count && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(items, count, size - count);
            }

            size = count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNewCapacity<T>(this List<T> list, int capacity)
        {
            const int DefaultCapacity = 4;
            var listData = Unsafe.As<List<T>, ListDataHelper<T>>(ref list);
            T[] _items = listData._items;
            //Debug.Assert(_items.Length < capacity);

            int newCapacity = _items.Length == 0 ? DefaultCapacity : MathHelpers.CeilPow2(_items.Length);

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newCapacity > /* Array.MaxLength */ 0X7FFFFFC7) newCapacity = /* Array.MaxLength */ 0X7FFFFFC7;

            // If the computed capacity is still less than specified, set to the original argument.
            // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
            if (newCapacity < capacity) newCapacity = capacity;

            return newCapacity;
        }



        record ListDataHelper<T>
        {
            public T[] _items;
            public int _size;
            public int _version;
        }
    }

    internal static class MathHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilPow2(int x)
        {
            x -= 1;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x + 1;
        }
    }
}
