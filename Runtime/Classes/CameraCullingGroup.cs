using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Culling
{
    /// <summary>
    /// 附加在相机上，持有一个剔除组
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Com/Culling/CameraCullingGroup")]
    public sealed class CameraCullingGroup : MonoBehaviour
    {
        enum CullingGroupFrameState
        {
            Common = 0,
            DoCull,
            CheckEventOnly,
        }

        [Header("Fix it in prefab")]
        [SerializeField] float[] lodLevels;

        Camera targetCamera;
        AbsAABBCullingGroup cullingGroup;

        CullingGroupFrameState frameState = 0;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            cullingGroup = new JobsAABBCullingGroup
            {
                ReferenceCamera = targetCamera
            };
            cullingGroup.InitInternalBuffers(0);
            cullingGroup.onStateChanged = CullingGroup_onStateChanged;
        }

        private void OnEnable()
        {
            CullingGroupVolumeBus.OnAddVolume += CullingGroupVolumeBus_OnAddVolume;
            CullingGroupVolumeBus.OnRemoveVolume += CullingGroupVolumeBus_OnRemoveVolume;
            RenderPipelineManager.beginContextRendering += RenderPipelineManager_beginContextRendering;

            frameState = CullingGroupFrameState.DoCull;
            cullingGroup.SetLodLevels(lodLevels);
        }

        private void OnDisable()
        {
            CullingGroupVolumeBus.OnAddVolume -= CullingGroupVolumeBus_OnAddVolume;
            CullingGroupVolumeBus.OnRemoveVolume -= CullingGroupVolumeBus_OnRemoveVolume;
            RenderPipelineManager.beginContextRendering -= RenderPipelineManager_beginContextRendering;

            cullingGroup.InitInternalBuffers(cullingGroup.Count);
        }

        private void OnDestroy()
        {
            cullingGroup.onStateChanged = null;
        }

        private void CullingGroupVolumeBus_OnAddVolume(CullingGroupVolumeBus bus, int index)
        {
            cullingGroup.Setup(bus.BoundsRef);
            cullingGroup.Count = bus.Count;
            cullingGroup.GetCurrentBuffer(out var prev, out var curr);
            prev[index] = AABBCullingContext.Visible;
            curr[index] = AABBCullingContext.Visible;
            frameState = CullingGroupFrameState.CheckEventOnly;
        }

        private void CullingGroupVolumeBus_OnRemoveVolume(CullingGroupVolumeBus bus, int index)
        {
            cullingGroup.EraseAt(index);
        }

        private void RenderPipelineManager_beginContextRendering(ScriptableRenderContext arg1, List<Camera> arg2)
        {
            if (cullingGroup != null && cullingGroup.Count != 0)
            {
                switch (frameState)
                {
                    case CullingGroupFrameState.DoCull:
                        cullingGroup.Cull();
                        break;
                    case CullingGroupFrameState.CheckEventOnly:
                        cullingGroup.CheckEvent();
                        break;
                    default:
                        cullingGroup.Update();
                        break;
                }
                frameState = 0;
            }
        }

        public void SetLODLevels(float[] lodLevels)
        {
            this.lodLevels = lodLevels;
            cullingGroup?.SetLodLevels(lodLevels);
        }

        [ContextMenu("apply lod levels")]
        void Apply()
        {
            if (Application.isPlaying)
            {
                cullingGroup.SetLodLevels(lodLevels);
            }
            else
            {
                Debug.Log("在播放时调用此方法应用更改的 LOD 层级");
            }
        }

        private void CullingGroup_onStateChanged(AABBCullingGroupEvent eventContext)
        {
            int index = eventContext.index;
            var volumes = CullingGroupVolumeBus.Instance.VolumesRef;
            if (index > volumes.Count - 1) { return; }
            var item = CullingGroupVolumeBus.Instance.VolumesRef[index];
            if (eventContext.HasBecomeVisible)
            {
                item.DoBecameVisible(targetCamera);
            }
            if (eventContext.HasBecomeInvisible)
            {
                item.DoBecameInvisible(targetCamera);
            }
            if (eventContext.CurrentLodLevel != eventContext.PreviousLodLevel)
            {
                item.DoLodChanged(targetCamera, lodLevels, eventContext.CurrentLodLevel);
            }
        }
    }
}