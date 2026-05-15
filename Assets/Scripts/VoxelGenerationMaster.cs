using System.Collections.Generic;
using UnityEngine;

namespace Destruxion.Voxels
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DESTRUXion/Voxel Generation Master")]
    public sealed class VoxelGenerationMaster : MonoBehaviour
    {
        [Header("Voxel Models")]
        [SerializeField] List<MeshVoxelReconstructor> meshVoxelModels = new();
        [SerializeField] List<VoxelReconstructor> textVoxelModels = new();

        [Header("Shared Build Settings")]
        [SerializeField, Min(0.01f)] float voxelSize = 0.2f;
        [SerializeField] MeshVoxelResolutionMode resolutionMode = MeshVoxelResolutionMode.VoxelSize;
        [SerializeField, Min(8)] int targetResolution = 192;
        [SerializeField] MeshVoxelAlgorithm algorithm = MeshVoxelAlgorithm.AccurateRaycast;
        [SerializeField, Min(1)] int surfaceThickness = 1;
        [SerializeField, Range(0f, 1f)] float bakedShadingStrength = 0.45f;
        [SerializeField, Range(0f, 0.25f)] float cubeColorVariation = 0.08f;
        [SerializeField] bool fillSolid;
        [SerializeField] bool hideSourceRenderers = true;

        [Header("Output")]
        [SerializeField] VoxelOutputMode outputMode = VoxelOutputMode.Destructible;
        [SerializeField] VoxelDamageProfile damageProfile = VoxelDamageProfile.DrywallHammer;
        [SerializeField] Material voxelMaterial;

        [Header("Bake Options")]
        [SerializeField] bool applySettingsBeforeBake = true;
        [SerializeField] bool includeInactiveWhenFinding = true;

        public List<MeshVoxelReconstructor> MeshVoxelModels => meshVoxelModels;
        public List<VoxelReconstructor> TextVoxelModels => textVoxelModels;
        public float VoxelSize => voxelSize;
        public MeshVoxelResolutionMode ResolutionMode => resolutionMode;
        public int TargetResolution => targetResolution;
        public MeshVoxelAlgorithm Algorithm => algorithm;
        public int SurfaceThickness => surfaceThickness;
        public float BakedShadingStrength => bakedShadingStrength;
        public float CubeColorVariation => cubeColorVariation;
        public bool FillSolid => fillSolid;
        public bool HideSourceRenderers => hideSourceRenderers;
        public VoxelOutputMode OutputMode => outputMode;
        public VoxelDamageProfile DamageProfile => damageProfile;
        public Material VoxelMaterial => voxelMaterial;
        public bool ApplySettingsBeforeBake => applySettingsBeforeBake;
        public bool IncludeInactiveWhenFinding => includeInactiveWhenFinding;

        void Reset()
        {
            voxelSize = 0.2f;
            resolutionMode = MeshVoxelResolutionMode.VoxelSize;
        }
    }
}
