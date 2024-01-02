using UnityEngine;

namespace Com.Culling.Test
{
    public class TestCsVolume : MonoBehaviour
    {
        CsAABBCullingVolume volume;
        Renderer m_renderer;

        private void Awake()
        {
            volume = GetComponent<CsAABBCullingVolume>();
            m_renderer = GetComponent<Renderer>();

            volume.onBecameVisible.AddListener(Volume_onBecameVisible);
            volume.onBecameInvisible.AddListener(Volume_onBecameInvisible);
        }

        void Volume_onBecameVisible(Camera camera)
        {
            m_renderer.enabled = true;
        }

        void Volume_onBecameInvisible(Camera camera)
        {
            m_renderer.enabled = false;
        }
    }
}