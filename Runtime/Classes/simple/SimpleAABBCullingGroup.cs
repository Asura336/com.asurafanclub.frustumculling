using UnityEngine;
using UnityEngine.Assertions;
using static Com.Culling.AABBCullingHelper;

namespace Com.Culling
{
    /// <summary>
    /// 表示剔除组的公开行为，由持有组的服务等调用的方法和属性
    /// </summary>
    public interface IAABBCullingGroup
    {
        /// <summary>
        /// 剔除组目前参与剔除的实例数目
        /// </summary>
        int Count { get; set; }
        /// <summary>
        /// 剔除组的目标相机
        /// </summary>
        Camera ReferenceCamera { get; set; }

        /// <summary>
        /// 擦除一条数据，会减少 <see cref="Count"/>
        /// </summary>
        /// <param name="index"></param>
        void EraseAt(int index);
        /// <summary>
        /// 传递 LOD 距离，按倒序排列的视口高度
        /// </summary>
        /// <param name="lodLevels"></param>
        void SetLodLevels(float[] lodLevels);
        /// <summary>
        /// 传递包围盒数组作为所有实例的信息，剔除组会引用此数组。不会改变 <see cref="Count"/>
        /// </summary>
        /// <param name="array"></param>
        void Setup(Bounds[] array);
        /// <summary>
        /// 写入 <see cref="Count"/> 并初始化内部缓冲区，会立即检查一次可见性（认为所有实例都变为可见的）
        /// </summary>
        /// <param name="count"></param>
        void InitInternalBuffers(int count);
        /// <summary>
        /// 步进一次，会更新<see cref="ReferenceCamera">目标相机</see>的 VP 矩阵并检查可见性，分发事件，滚动缓冲区
        /// </summary>
        void Update();
        /// <summary>
        /// 获取内部缓冲区，在剔除组每次<see cref="Update">步进</see>后两条缓冲区会交换，不必保存它们的引用
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="curr"></param>
        void GetCurrentBuffer(out AABBCullingContext[] prev, out AABBCullingContext[] curr);
        /// <summary>
        /// 立即更新<see cref="ReferenceCamera">目标相机</see>的 VP 矩阵并检查可见性，分发事件
        /// </summary>
        void Cull();
        /// <summary>
        /// 按照当前缓冲区内容指示的可见性分发可见性变更的事件
        /// </summary>
        void CheckEvent();
    }

    /// <summary>
    /// 用于模式匹配的抽象类，<see cref="AABBCullingGroupKeeperTemplate{TGroup, TVolume}"/>
    /// </summary>
    public abstract class AbsAABBCullingGroup : IAABBCullingGroup
    {
        public AABBCullingStateChanged onStateChanged;

        public abstract int Count { get; set; }
        public abstract Camera ReferenceCamera { get; set; }

        public abstract void EraseAt(int index);
        public abstract void SetLodLevels(float[] lodLevels);
        public abstract void Setup(Bounds[] array);
        public abstract void InitInternalBuffers(int count);
        public abstract void GetCurrentBuffer(out AABBCullingContext[] prev, out AABBCullingContext[] curr);
        public abstract void Cull();
        public abstract void CheckEvent();
        public abstract AABBCullingContext GetInternalVisibleContextAt(int index);
        public abstract void Update();
    }

    /// <summary>
    /// 最基本的剔除组，单线程代码
    /// </summary>
    public class SimpleAABBCullingGroup : AbsAABBCullingGroup
    {
        public static readonly float[] defaultLodLevels = new float[] { 1, 0.5f, 0.25f, 0.025f };
        /* 相机
         *   世界空间到投影空间的变换
         * 
         * 每物体
         *   世界空间包围盒
         *   可见性
         *   视野内高度
         */

        static readonly float[] emptyLodLevels = new float[1] { 0 };

        protected float[] lodLevels = emptyLodLevels;
        protected int bufferCount = 0;

        protected Camera referenceCamera;
        protected Matrix4x4 vpMatrix;
        protected Matrix4x4 cameraLocalToWorldMatrix;
        protected readonly Plane[] frustumPlanes = new Plane[6];

        protected int capacity;
        protected Bounds[] bounds;

        // 两个缓冲区在来回交换，作为“当前帧”和“上一帧”的缓冲区
        protected AABBCullingContext[] ctx0 = new AABBCullingContext[0];
        protected AABBCullingContext[] ctx1 = new AABBCullingContext[0];

        protected int revertCtxBufferFrame = 0;

        public override int Count
        {
            get => bufferCount;
            set
            {
                Assert.IsTrue(value < bounds.Length + 1, $"length <= {bounds.Length}");
                bufferCount = value;
            }
        }

        public override Camera ReferenceCamera
        {
            get => referenceCamera;
            set
            {
                referenceCamera = value;
                UpdateMatrix();
            }
        }

        public override void EraseAt(int index)
        {
            Assert.IsTrue(index > -1, "invalid index");
            Assert.IsTrue(index < bufferCount, $"remove ({index}) but count= {bufferCount}");

            --bufferCount;
            bounds[index] = bounds[bufferCount];
            ctx0[index] = ctx0[bufferCount];
            ctx1[index] = ctx1[bufferCount];
            // buffer[bufferCount]: index changed
            // bufferCount => index
        }

        /// <summary>
        /// 重置缓冲区状态，当前帧保存的内容全部设置为可见且视口中高度为 1
        /// </summary>
        /// <param name="count"></param>
        public override void InitInternalBuffers(int count)
        {
            bufferCount = count;
            //bool rev = revertCtxBufferFrame % 2 != 0;
            //AABBCullingContext[] before = rev ? ctx0 : ctx1,
            //    after = rev ? ctx1 : ctx0;
            // ctx0 => after
            // ctx1 => before
            revertCtxBufferFrame = 0;
            GetCurrentBuffer(out var prev, out var curr);
            for (int i = 0; i < count; i++)
            {
                prev[i] = AABBCullingContext.Invisible;
                curr[i] = AABBCullingContext.Visible;
            }
            // 立即检查一次事件？
            CheckEvent(prev, curr, bufferCount);
        }

        public override void GetCurrentBuffer(out AABBCullingContext[] prev, out AABBCullingContext[] curr)
        {
            bool rev = revertCtxBufferFrame % 2 != 0;
            prev = rev ? ctx0 : ctx1;
            curr = rev ? ctx1 : ctx0;
        }

        public override void SetLodLevels(float[] lodLevels)
        {
            if (lodLevels != null)
            {
                Assert.IsTrue(lodLevels.Length < 128, "最多 127 段");
                Assert.IsTrue(IsDescending(lodLevels), "需要倒序");
            }
            this.lodLevels = lodLevels ?? emptyLodLevels;
        }

        public override void Setup(Bounds[] array)
        {
            Assert.IsTrue(array != null);
            bounds = array;
            //...
            capacity = bounds.Length;
            Realloc(ref ctx0, capacity);
            Realloc(ref ctx1, capacity);
        }

        /* 计算
         *   计算可见性
         * 检查
         *   当前结果比较上一次的结果
         *   交换引用
         *   
         * LOD level?
         *   1..n..0
         */

        public override void Update()
        {
            UpdateMatrix();
            GetCurrentBuffer(out var prev, out var curr);
            Culling(curr, bounds, bufferCount);
            CheckEvent(prev, curr, bufferCount);
            unchecked
            {
                revertCtxBufferFrame++;
            }
        }

        public override void Cull()
        {
            GetCurrentBuffer(out var prev, out var curr);
            for (int i = 0; i < bufferCount; i++)
            {
                prev[i] = AABBCullingContext.Visible;
                curr[i] = AABBCullingContext.Visible;
            }
            UpdateMatrix();
            Culling(curr, bounds, bufferCount);
            CheckEvent(prev, curr, bufferCount);
        }

        /// <summary>
        /// 按当前帧的内容立即检查并发送一次事件
        /// </summary>
        public override void CheckEvent()
        {
            GetCurrentBuffer(out var before, out var after);
            CheckEvent(before, after, bufferCount);
        }

        public override AABBCullingContext GetInternalVisibleContextAt(int index)
        {
            if (index < 0 || index > bufferCount - 1) { return default; }
            bool rev = revertCtxBufferFrame % 2 != 0;
            var curr = rev ? ctx1 : ctx0;
            return curr[index];
        }

        protected virtual void CheckEvent(AABBCullingContext[] before, AABBCullingContext[] after, int count)
        {
            // 2000 times, 0.40 ms
            for (int i = 0; i < count; i++)
            {
                byte prevState = (byte)(HeightToLodLevel(before[i].height, lodLevels) & AABBCullingGroupEvent.lodLevelMask);
                byte currState = (byte)(HeightToLodLevel(after[i].height, lodLevels) & AABBCullingGroupEvent.lodLevelMask);

                if (!before[i].visible && after[i].visible) { currState |= AABBCullingGroupEvent.visibleFlag; }
                if (before[i].visible && !after[i].visible) { prevState |= AABBCullingGroupEvent.visibleFlag; }
                if (prevState != currState)
                {
                    // send...
                    AABBCullingGroupEvent ctx = default;
                    ctx.index = i;
                    ctx.prevState = prevState;
                    ctx.currState = currState;
                    onStateChanged?.Invoke(ctx);
                }
            }
        }

        protected virtual void UpdateMatrix()
        {
            if (referenceCamera)
            {
                VpMatrix(ref vpMatrix, referenceCamera);
                GeometryUtility.CalculateFrustumPlanes(vpMatrix, frustumPlanes);
                cameraLocalToWorldMatrix = referenceCamera.transform.localToWorldMatrix;
            }
        }

        protected virtual unsafe void Culling(AABBCullingContext[] dst, Bounds[] src, int count)
        {
            var cameraForward = cameraLocalToWorldMatrix.MultiplyVector(Vector3.forward);
            var cameraPosition = cameraLocalToWorldMatrix.MultiplyPoint(Vector3.zero);
            // 2000 bounds, about 7.0 ms
            var vec8 = stackalloc Vector3[8];
            for (int i = 0; i < count; i++)
            {
                src[i].GetBoundsVerticesUnsafe(vec8);
                float maxX = float.MinValue, minX = float.MaxValue;
                float maxY = float.MinValue, minY = float.MaxValue;
                float minDot = 1, maxDot = -1;
                for (int j = 0; j < 8; j++)
                {
                    Vector4 p = default;
                    ToPoint(vec8[j], ref p);

                    Vector4 clipPos = default;
                    vpMatrix.Mul(p, ref clipPos);
                    // ???: (clipSpace.xy * 0.5 + clipSpace.w * 0.5) / clipSpace.w
                    float viewPosX = 0.5f + 0.5f * clipPos.x / clipPos.w;
                    float viewPosY = 0.5f + 0.5f * clipPos.y / clipPos.w;
                    minX = Min(minX, viewPosX); maxX = Max(maxX, viewPosX);
                    minY = Min(minY, viewPosY); maxY = Max(maxY, viewPosY);

                    float dot = Vector3.Dot(cameraForward, (Vector3)p - cameraPosition);
                    minDot = Mathf.Min(dot, minDot); maxDot = Mathf.Max(dot, maxDot);
                }
                // 如果包围盒顶点有的在视野前方有的在视野后方，认为表面始终靠近相机
                // 设置高度为 1（最近）
                float height = (minDot * maxDot < 0)
                    ? 1
                    : Mathf.Max(maxX - minX, maxY - minY);

                AABBCullingContext cullingResult = default;
                cullingResult.height = height;
                cullingResult.visible = GeometryUtility.TestPlanesAABB(frustumPlanes, src[i]);
                dst[i] = cullingResult;
            }
        }
    }
}