using System.Runtime.CompilerServices;
using UnityEngine;

namespace Com.Culling
{
    /// <summary>
    /// 相机控制器保存的状态
    /// </summary>
    internal struct EditorLikeCameraControllerContext
    {
        public Camera cameraRef;
        public Vector3 position;
        public Vector3 eulerAngles;
        public bool orthographics;
        public float orthographicsSize;
        public float referenceDepth;
        public bool reverseRotate;
        public float rotateDegreePerCoordDelta;

        public readonly Vector3 ViewportToWorldPoint(in Vector2 viewportPosition)
        {
            Vector3 p = viewportPosition;
            p.z = referenceDepth;
            return cameraRef.ViewportToWorldPoint(p);
        }

        public readonly Vector3 ScreenToWorldPoint(in Vector2 mousePosition)
        {
            Vector3 p = mousePosition;
            p.z = referenceDepth;
            return cameraRef.ScreenToWorldPoint(p);
        }
    }

    /// <summary>
    /// 相机控制器所需的状态和基础行为，控制动作有平移、旋转、公转和缩放画面
    /// </summary>
    internal struct EditorLikeCameraActionProcess
    {
        public enum ActionType
        {
            None,
            MoveView,
            RotateView,
            RotateAroundView,
            ScaleView,
        }

        public readonly ActionType actionFlag;
        public EditorLikeCameraControllerContext ctx;
        //readonly RECT windowRect;
        readonly Vector2 screenCoord;
        readonly Vector2 mousePosition;
        readonly Vector3 centerWorldPivot;
        readonly Vector3 mouseWorldPivot;
        readonly Vector3 initPosition;
        readonly Vector3 initEulerAngles;

        Vector2 roundCursorDelta;
        Vector2 prevCoordDelta;
        bool beginDrag;

        public EditorLikeCameraActionProcess(ActionType actionFlag, in EditorLikeCameraControllerContext controllerContext)
        {
            this.actionFlag = actionFlag;
            ctx = controllerContext;

            //GetCursorPos(out POINT point);
            //GetMonitorRectByCursorCoord(point, out windowRect);
            Vector2 point = Input.mousePosition;
            point.y = Screen.height - point.y;
            screenCoord = point;

            centerWorldPivot = ctx.ViewportToWorldPoint(new Vector2(0.5f, 0.5f));
            mousePosition = UnityEngine.Input.mousePosition;
            mouseWorldPivot = ctx.ScreenToWorldPoint(mousePosition);

            initPosition = ctx.position;
            initEulerAngles = ctx.eulerAngles;

            roundCursorDelta = Vector2.zero;
            prevCoordDelta = Vector2.zero;
            beginDrag = false;
        }

        public readonly bool Valid => actionFlag != ActionType.None;

        public bool Execute()
        {
            if (!Valid) { return false; }

            //RoundCursor(windowRect, out POINT point, out int deltaX, out int deltaY);
            //roundCursorDelta += new Vector2(deltaX, deltaY);
            Vector2 point = Input.mousePosition;
            point.y = Screen.height - point.y;
            var coord = point - roundCursorDelta;

            var _coordDelta = coord - screenCoord;
            // 矫正突变
            if (BurstDelta(_coordDelta - prevCoordDelta))
            {
                return false;
            }
            prevCoordDelta = _coordDelta;

            beginDrag = beginDrag || (beginDrag | prevCoordDelta.Manhattan() > 8);
            if (beginDrag)
            {
                switch (actionFlag)
                {
                    case ActionType.MoveView: MoveView(_coordDelta); break;
                    case ActionType.RotateView: RotateView(_coordDelta); break;
                    case ActionType.RotateAroundView: RotateAroundView(_coordDelta); break;
                    case ActionType.ScaleView: ScaleView(_coordDelta); break;
                    default: return false;
                }
            }
            return true;
        }

        static readonly Vector2 __scaleReverseY = new Vector2(1, -1);

        private void MoveView(in Vector2 coordDelta)
        {
            var pointCoord = Vector2.Scale(coordDelta, __scaleReverseY) + mousePosition;
            var worldDelta = mouseWorldPivot - ctx.ScreenToWorldPoint(pointCoord);
            ctx.position += worldDelta;
        }

        private void RotateView(in Vector2 coordDelta)
        {
            var delta = coordDelta;
            if (ctx.reverseRotate) { delta *= -1; }
            delta *= ctx.rotateDegreePerCoordDelta;

            float _x = initEulerAngles.x + delta.y;
            float _y = initEulerAngles.y + delta.x;
            ctx.eulerAngles = new Vector3(_x, _y);
        }

        private void RotateAroundView(in Vector2 coordDelta)
        {
            var delta = coordDelta;
            if (ctx.reverseRotate) { delta *= -1; }
            delta *= ctx.rotateDegreePerCoordDelta;

            float _x = initEulerAngles.x + delta.y;
            float _y = initEulerAngles.y + delta.x;
            var euler = new Vector3(_x, _y);

            ctx.position = centerWorldPivot - Forward(euler) * ctx.referenceDepth;
            ctx.eulerAngles = euler;
        }

        private void ScaleView(in Vector2 coordDelta)
        {
            if (ctx.orthographics)
            {
                const float minOrthoSize = 0.05f;
                float sizeDelta = coordDelta.x * 1e-2f;
                ctx.orthographicsSize = Mathf.Max(minOrthoSize, ctx.orthographicsSize + sizeDelta);
            }
            else
            {
                float moveDelta = coordDelta.x * ctx.referenceDepth * 1e-3f;
                ctx.position = initPosition + Forward(ctx.eulerAngles) * moveDelta;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3 Forward(in Vector3 euler) => Quaternion.Euler(euler) * Vector3.forward;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool BurstDelta(in Vector2 ddelta)
        {
            const float maxDDelta = 400;

            return Mathf.Abs(ddelta.x) > maxDDelta
                || Mathf.Abs(ddelta.y) > maxDDelta;
        }
    }

    internal static class EditorLikeCameraInternalExtensions
    {
        public static float Manhattan(this Vector2 pos) => Mathf.Abs(pos.x) + Mathf.Abs(pos.y);
        public static float Manhattan(this in Vector3 pos) => Mathf.Abs(pos.x) + Mathf.Abs(pos.y) + Mathf.Abs(pos.z);
    }
}