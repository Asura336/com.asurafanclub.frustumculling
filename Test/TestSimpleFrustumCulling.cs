using System.Linq;
using UnityEngine;

namespace Com.Culling.Test
{
    public class TestSimpleFrustumCulling : MonoBehaviour
    {
        [SerializeField] Camera mainCamera;
        SimpleAABBCullingGroup cullingGroup;
        GameObject[] cubes;
        Renderer[] renderers;
        Material[] materials;

        [SerializeField] float[] lodLevels = new float[] { 0.75f, 0.5f, 0.33f, 0.15f };
        [SerializeField] Color[] lodColors = new Color[] { Color.white, Color.gray, Color.green, Color.red, Color.black };

        private void Start()
        {
            InitCubes();
            cullingGroup = new SimpleAABBCullingGroup
            {
                ReferenceCamera = mainCamera
            };
            cullingGroup.SetLodLevels(lodLevels);
            cullingGroup.Setup(cubes.Select(c =>
            {
                var t = c.transform;
                return new Bounds
                {
                    size = Vector3.one * 1.414f,
                    center = t.position
                };
            }).ToArray());
            cullingGroup.InitInternalBuffers(cubes.Length);

            cullingGroup.onStateChanged = CullingGroup_onStateChanged;
        }

        private void OnDestroy()
        {
            cullingGroup.onStateChanged = null;
            foreach (var mat in materials)
            {
                Destroy(mat);
            }
        }

        private void Update()
        {
            cullingGroup.Update();
        }

        void InitCubes()
        {
            const int X = 20, Y = 5, Z = 20;
            const int length = X * Y * Z;

            cubes = new GameObject[length];
            renderers = new Renderer[length];
            materials = new Material[length];
            var start = new Vector3(0, 0, 0);
            var offset = new Vector3(2, 1, 2);
            int index = 0;
            for (int x = 0; x < X; x++)
            {
                for (int y = 0; y < Y; y++)
                {
                    for (int z = 0; z < Z; z++)
                    {
                        var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        var t = o.transform;
                        t.SetParent(transform);
                        t.localPosition = start + new Vector3(offset.x * x, offset.y * y, offset.z * z);
                        cubes[index] = o;

                        renderers[index] = o.GetComponent<Renderer>();
                        materials[index] = renderers[index].material;
                        index++;
                    }
                }
            }
        }

        void CullingGroup_onStateChanged(AABBCullingGroupEvent eventContext)
        {
            int index = eventContext.index;
            if (eventContext.HasBecomeVisible)
            {
                cubes[index].SetActive(true);
            }
            if (eventContext.HasBecomeInvisible)
            {
                cubes[index].SetActive(false);
            }
            if (eventContext.CurrentLodLevel != eventContext.PreviousLodLevel)
            {
                materials[index].color = lodColors[eventContext.CurrentLodLevel];
            }
        }
    }
}