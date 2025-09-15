using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Culling.Test
{
    public class TestJobsVolume : MonoBehaviour
    {
        JobsAABBCullingVolume volume;
        Renderer m_renderer;

        public Color[] lodColors = new Color[]
        {
            Color.white,
            Color.yellow,
            Color.green,
            Color.red,
        };

        private void Awake()
        {
            volume = GetComponent<JobsAABBCullingVolume>();
            m_renderer = GetComponent<Renderer>();

            volume.onBecameVisible.AddListener(Volume_onBecameVisible);
            volume.onBecameInvisible.AddListener(Volume_onBecameInvisible);
            volume.lodChanged.AddListener(Volume_lodChanged);
        }

        void Volume_onBecameVisible(Camera camera)
        {
            m_renderer.enabled = true;
        }

        void Volume_onBecameInvisible(Camera camera)
        {
            m_renderer.enabled = false;
        }

        void Volume_lodChanged(Camera camera, IReadOnlyList<float> lods, int lodLevel)
        {
            m_renderer.material.color = lodLevel < lodColors.Length
                ? lodColors[lodLevel]
                : Color.gray;
        }
    }
}