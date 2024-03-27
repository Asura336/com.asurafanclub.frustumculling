using System.Collections.Generic;
using UnityEngine;

namespace Com.Culling.Test
{
    public class TestCameraVolume : MonoBehaviour
    {
        CullingGroupVolume volume;
        Renderer m_renderer;
        Material m_material;

        [SerializeField] Color[] colors = new Color[] { Color.white, Color.gray, Color.green, Color.red, Color.black };

        private void Awake()
        {
            volume = GetComponent<CullingGroupVolume>();
            m_renderer = GetComponentInChildren<Renderer>();
            m_material = m_renderer.material;

            volume.onBecameVisible.AddListener(Volume_onBecameVisible);
            volume.onBecameInvisible.AddListener(Volume_onBecameInvisible);
            volume.onVolumeDisabled.AddListener(Volume_onVolumeDisabled);
            volume.lodChanged.AddListener(Volume_lodChanged);
        }

        private void OnDestroy()
        {
            Destroy(m_material);
        }

        void Volume_onBecameVisible(Camera camera)
        {
            m_renderer.forceRenderingOff = false;
        }

        void Volume_onBecameInvisible(Camera camera)
        {
            m_renderer.forceRenderingOff = true;
        }

        void Volume_onVolumeDisabled()
        {
            m_renderer.forceRenderingOff = false;
        }

        void Volume_lodChanged(Camera camera, IReadOnlyList<float> lods, int lodLevel)
        {
            m_material.color = colors[lodLevel];
        }
    }
}