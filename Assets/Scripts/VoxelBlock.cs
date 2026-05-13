using UnityEngine;

namespace Destruxion.Voxels
{
    public enum VoxelSurfaceType
    {
        Default,
        Dark,
        Organic,
        Metal,
        Stone,
        Wood
    }

    public sealed class VoxelBlock : MonoBehaviour
    {
        [field: SerializeField] public Color32 SourceColor { get; private set; }
        [field: SerializeField] public float Mass { get; private set; } = 1f;
        [field: SerializeField] public VoxelSurfaceType SurfaceType { get; private set; }

        public void Initialize(Color32 sourceColor, float mass, VoxelSurfaceType surfaceType)
        {
            SourceColor = sourceColor;
            Mass = mass;
            SurfaceType = surfaceType;
        }
    }
}
