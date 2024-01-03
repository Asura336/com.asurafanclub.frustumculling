using UnityEngine;
using static UnityEngine.Input;
using static UnityEngine.Mathf;

namespace Com.Culling
{
    /// <summary>
    /// 附加在相机上的控制器实现
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    internal class EditorLikeCameraController : MonoBehaviour
    {
        const float DEPTH = 10;

        [Header("配置，对象只读：")]
        public bool enableRotate = true;
        public bool enableScroll = true;
        public bool enableDrag = true;
        public bool enableMove = true;

        public bool enableGUI = true;
        public GUISkin m_skin;
        public Vector2 guiPivot = new Vector2(10, 10);
        public Vector2 guiSize = new Vector2(100, 211);

        public Vector2 sizeLimit = new Vector2(0.01f, 100);
        [Range(0.05f, 1)]
        public float degreePerDelta = 0.1f;
        [Range(0.1f, 3600)]
        public float arrowKeyRotatePower = 90;
        public bool reverseRotate = false;
        public float moveForwardSpeed = 25;
        public float moveSpeed = 10;

        public float maxDepth = 100;

        [Header("配置，对象读写：")]
        public float dragDepth = DEPTH;

        [Header("监测项，对象只写：")]
        [SerializeField] bool enableDragStart = true;
        [SerializeField] bool enableRotateStart = true;

        // hide...
        [HideInInspector] public Vector3 position;
        [HideInInspector] public Vector3 euler;
        [HideInInspector] public Vector3 velocity;

        Camera m_camera;

        EditorLikeCameraActionProcess actionProcess;

        Vector2 cachedMousePosition;

        private void Awake()
        {
            m_camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            position = transform.localPosition;
            euler = transform.localEulerAngles;
            cachedMousePosition = Input.mousePosition;
        }

        private void OnGUI()
        {
            if (!enableGUI) { return; }

            GUI.skin = m_skin;

            var shell = new Rect(guiPivot, guiSize);
            GUI.Box(shell, "相机设置");
            shell.yMin += 20;
            shell.x += 7;
            shell.width -= 14;
            GUILayout.BeginArea(shell);
            GUILayout.BeginVertical();

            m_camera.orthographic = GUILayout.Toggle(m_camera.orthographic, "正交");

            if (GUILayout.Button("俯视 (↓)")) { TopView(); }
            if (GUILayout.Button("底视 (↑)")) { BottomView(); }
            if (GUILayout.Button("左视 (←)")) { LeftView(); }
            if (GUILayout.Button("右视 (→)")) { RightView(); }
            if (GUILayout.Button("前视 (·)")) { FrontView(); }
            if (GUILayout.Button("后视 (o)")) { BackView(); }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void Update()
        {
            bool dragScaleView = AltPushing && CenterPushing;
            bool setDestination = (AltPushing && (RightDown || RightPushing))
                || CenterDown || CenterPushing || CenterRelease;

            bool rotateByKey = MovePower != Vector3.zero && AltPushing;
            bool setVelocity = MovePower != Vector3.zero && !CtrlPushing;

            //var pointOnSpace = MouseTouchInputModule.Instance.PointerOnSpace;
            bool pointOnSpace = true;
            var pos = mousePosition;
            bool inRange = pos.x >= 0 && pos.x <= Screen.width && pos.y >= 0 && pos.y <= Screen.height;
            enableScroll = !dragScaleView && pointOnSpace && inRange;
            enableDragStart = enableDrag && pointOnSpace;
            enableRotateStart = enableRotate && pointOnSpace;

            if (!dragScaleView && setDestination)
            {
                if (enableDrag) { DragOnScreen(); }
                if (enableRotate) { RotateAround(); }
                velocity = Vector3.zero;
            }
            else if (rotateByKey)
            {
                if (enableRotate) { ArrowRotateAround(); }
            }
            else if (setVelocity)
            {
                if (enableMove) { ArrowMove(); }
            }
            else
            {
                velocity = velocity.Manhattan() < 0.001f ? Vector3.zero : velocity * 0.25f;
            }

            if (CenterRelease && ScreenCoordDelta.Manhattan() < 8)
            {
                SetDragDepth();
            }
            if (enableScroll && !AltPushing)
            {
                MoveForward();
            }
            if (dragScaleView)
            {
                DragMoveForward();
            }
            if (enableRotate && !setDestination)
            {
                RotateView();
            }

            if (velocity != Vector3.zero)
            {
                position += velocity * (moveSpeed * Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));

            Vector2 currMousePos = Input.mousePosition;
            ScreenCoordDelta = currMousePos - cachedMousePosition;
            cachedMousePosition = currMousePos;
        }


        static bool CtrlPushing => GetKey(KeyCode.LeftControl) || GetKey(KeyCode.RightControl);
        static bool AltPushing => GetKey(KeyCode.LeftAlt) || GetKey(KeyCode.RightAlt);
        static bool RightDown => GetMouseButtonDown(1);
        static bool RightPushing => GetMouseButton(1);
        static bool RightUp => GetMouseButtonUp(1);
        static bool CenterDown => GetMouseButtonDown(2);
        static bool CenterPushing => GetMouseButton(2);
        static bool CenterRelease => GetMouseButtonUp(2);
        static Vector3 MovePower => new Vector3(GetAxis("Horizontal"), 0, GetAxis("Vertical"));
        static Vector2 ScreenCoordDelta { get; set; }

        EditorLikeCameraControllerContext ControllerContext
        {
            get => new EditorLikeCameraControllerContext
            {
                cameraRef = m_camera,
                position = position,
                eulerAngles = euler,
                orthographics = m_camera.orthographic,
                orthographicsSize = m_camera.orthographicSize,
                referenceDepth = dragDepth,
                reverseRotate = reverseRotate,
                rotateDegreePerCoordDelta = degreePerDelta,
            };
            set
            {
                position = value.position;
                euler = value.eulerAngles;
                m_camera.orthographicSize = value.orthographicsSize;
            }
        }

        public void SetAngleAroundDepthPoint(in Vector3 angle)
        {
            var origin = m_camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, dragDepth));

            var _dir = Quaternion.Euler(angle) * Vector3.forward;
            position = origin - _dir * dragDepth;
            euler = angle;
        }
        public void TopView() => SetAngleAroundDepthPoint(new Vector3(90, 0, 0));
        public void BottomView() => SetAngleAroundDepthPoint(new Vector3(270, 0, 0));
        public void RightView() => SetAngleAroundDepthPoint(new Vector3(0, 270, 0));
        public void LeftView() => SetAngleAroundDepthPoint(new Vector3(0, 90, 0));
        public void FrontView() => SetAngleAroundDepthPoint(new Vector3(0, 180, 0));
        public void BackView() => SetAngleAroundDepthPoint(new Vector3(0, 0, 0));


        void RotateView()
        {
            if (RightDown && enableRotateStart && !actionProcess.Valid)
            {
                actionProcess = new EditorLikeCameraActionProcess(EditorLikeCameraActionProcess.ActionType.RotateView, ControllerContext);
            }
            if (RightPushing && actionProcess.Valid)
            {
                actionProcess.Execute();
                ControllerContext = actionProcess.ctx;
            }
            if (RightUp)
            {
                actionProcess = default;
            }
        }

        void RotateAround()
        {
            if (RightDown && enableRotateStart && !actionProcess.Valid)
            {
                actionProcess = new EditorLikeCameraActionProcess(EditorLikeCameraActionProcess.ActionType.RotateAroundView, ControllerContext);
            }
            if (RightPushing && actionProcess.Valid)
            {
                actionProcess.Execute();
                ControllerContext = actionProcess.ctx;
            }
            if (RightUp)
            {
                actionProcess = default;
            }
        }

        void DragOnScreen()
        {
            if (CenterDown && enableDragStart && !actionProcess.Valid)
            {
                actionProcess = new EditorLikeCameraActionProcess(EditorLikeCameraActionProcess.ActionType.MoveView, ControllerContext);
            }
            if (CenterPushing && actionProcess.Valid)
            {
                actionProcess.Execute();
                ControllerContext = actionProcess.ctx;
            }
            if (CenterRelease)
            {
                actionProcess = default;
            }
        }

        void SetDragDepth()
        {
            var mousePos = mousePosition;
            var cameraForward = m_camera.ScreenPointToRay(mousePos);
            float nearClipPlane = m_camera.nearClipPlane, farClipPlane = m_camera.farClipPlane;
            bool _hitAny = Physics.Raycast(cameraForward, out var hitInfo, farClipPlane);

            if (enableDragStart)
            {
                float planecast = new Plane(Vector3.up, 0).Raycast(cameraForward, out float planeEnter) ? Abs(planeEnter) : float.PositiveInfinity;
                if (_hitAny)
                {
                    dragDepth = Min(planecast, Clamp(hitInfo.distance, nearClipPlane, maxDepth));
                }
                else
                {
                    dragDepth = Min(planecast, maxDepth);
                }
            }
        }

        #region move by arrowkey
        void ArrowMove()
        {
            var power = MovePower;
            var rotation = transform.localRotation;
            if (m_camera.orthographic)
            {
                velocity = rotation * (new Vector2(power.x, power.z) * (m_camera.orthographicSize * 0.2f));
            }
            else
            {
                velocity = rotation * (power * (dragDepth / DEPTH));
            }
        }

        void ArrowRotateAround()
        {
            var power = MovePower;
            float rotateMul = arrowKeyRotatePower * Time.deltaTime * (reverseRotate ? -1 : 1);
            float hori = power.x * rotateMul, vert = power.z * rotateMul;

            var origin = m_camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, dragDepth));
            var angle = euler;
            angle.x += vert;
            angle.y += hori;

            var _dir = Quaternion.Euler(angle) * Vector3.forward;
            position = origin - _dir * dragDepth;
            euler = angle;
        }
        #endregion

        #region scale view
        void MoveForward()
        {
            float scrollDelta = mouseScrollDelta.y;
            if (scrollDelta != 0)
            {
                if (m_camera.orthographic)
                {
                    var prev = m_camera.ScreenToWorldPoint(mousePosition);
                    float size = m_camera.orthographicSize;
                    size *= scrollDelta > 0 ? 0.8f : 1.25f;
                    m_camera.orthographicSize = Clamp(size, sizeLimit.x, sizeLimit.y);
                    position += prev - m_camera.ScreenToWorldPoint(mousePosition);
                }
                else
                {
                    float nearClipPlane = m_camera.nearClipPlane, farClipPlane = m_camera.farClipPlane;
                    float _forwardDepth = Physics.Raycast(m_camera.ScreenPointToRay(mousePosition), out var hitInfo, farClipPlane)
                        ? Clamp(hitInfo.distance, nearClipPlane, maxDepth)
                        : maxDepth;
                    float moveSpeed = moveForwardSpeed * Max(_forwardDepth * 0.5f, 1) * Time.deltaTime * Sign(scrollDelta);
                    position += transform.forward * moveSpeed;
                }
            }
        }
        #endregion

        void DragMoveForward()
        {
            if (CenterDown && enableDragStart && !actionProcess.Valid)
            {
                actionProcess = new EditorLikeCameraActionProcess(EditorLikeCameraActionProcess.ActionType.ScaleView, ControllerContext);
            }
            if (CenterPushing && actionProcess.Valid)
            {
                actionProcess.Execute();
                ControllerContext = actionProcess.ctx;
            }
            if (CenterRelease)
            {
                actionProcess = default;
            }
        }
    }
}