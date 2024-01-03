﻿using System.Collections.Generic;
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
    public class CameraCullingGroup : MonoBehaviour
    {
        enum CullingGroupFrameState
        {
            Common = 0,
            DoCull,
            CheckEventOnly,
        }

        [Header("Set in prefab")]
        [SerializeField] protected float[] lodLevels;

        Camera targetCamera;
        AbsAABBCullingGroup cullingGroup;

        CullingGroupFrameState frameState = 0;

        protected virtual void Awake()
        {
            targetCamera = GetComponent<Camera>();
            cullingGroup = new JobsAABBCullingGroup
            {
                ReferenceCamera = targetCamera
            };
            cullingGroup.InitInternalBuffers(0);
            cullingGroup.onStateChanged = CullingGroup_onStateChanged;
        }

        protected virtual void OnEnable()
        {
            CullingGroupVolumeBus.OnAddVolume += CullingGroupVolumeBus_OnAddVolume;
            CullingGroupVolumeBus.OnRemoveVolume += CullingGroupVolumeBus_OnRemoveVolume;
            RenderPipelineManager.beginContextRendering += RenderPipelineManager_beginContextRendering;

            frameState = CullingGroupFrameState.DoCull;
        }

        protected virtual void OnDisable()
        {
            CullingGroupVolumeBus.OnAddVolume -= CullingGroupVolumeBus_OnAddVolume;
            CullingGroupVolumeBus.OnRemoveVolume -= CullingGroupVolumeBus_OnRemoveVolume;
            RenderPipelineManager.beginContextRendering -= RenderPipelineManager_beginContextRendering;

            cullingGroup.InitInternalBuffers(cullingGroup.Count);
        }

        protected virtual void OnDestroy()
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

        [ContextMenu("apply lod levels")]
        public void Apply()
        {
            cullingGroup.SetLodLevels(lodLevels);
        }

        protected virtual void CullingGroup_onStateChanged(AABBCullingGroupEvent eventContext)
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