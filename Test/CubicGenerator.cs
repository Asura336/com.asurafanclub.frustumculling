using UnityEngine;

namespace Com.Culling.Test
{
    public class CubicGenerator : MonoBehaviour
    {
        [SerializeField] bool createPrimitive = false;
        [SerializeField] PrimitiveType primitiveType = PrimitiveType.Cube;
        [SerializeField] GameObject prefab;
        [SerializeField] int X = 20;
        [SerializeField] int Y = 5;
        [SerializeField] int Z = 20;
        [SerializeField] Vector3 start = new Vector3(0, 0, 0);
        [SerializeField] Vector3 offset = new Vector3(2, 1, 2);
        public GameObject[] instances;

        private void Start()
        {
            instances = Generate();
        }

        GameObject[] Generate()
        {
            var instances = new GameObject[X * Y * Z];

            int index = 0;
            for (int x = 0; x < X; x++)
            {
                for (int y = 0; y < Y; y++)
                {
                    for (int z = 0; z < Z; z++)
                    {
                        var o = createPrimitive || !prefab
                            ? GameObject.CreatePrimitive(primitiveType)
                            : Instantiate(prefab);
                        var t = o.transform;
                        t.SetParent(transform);
                        t.localPosition = start + new Vector3(offset.x * x, offset.y * y, offset.z * z);
                        instances[index] = o;
                        index++;
                    }
                }
            }
            return instances;
        }
    }
}