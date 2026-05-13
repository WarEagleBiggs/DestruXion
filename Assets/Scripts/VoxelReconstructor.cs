using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Destruxion.Voxels
{
    public sealed class VoxelReconstructor : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] TextAsset voxelTextFile;

        [Header("Build")]
        [SerializeField, Min(0.01f)] float voxelSize = 0.1f;
        [SerializeField] bool centerOnOrigin = true;
        [SerializeField] bool generateColliders;
        [SerializeField] bool markStatic = true;
        [SerializeField] GameObject cubePrefab;
        [SerializeField, Min(0)] int maxVoxels;

        [Header("Material")]
        [SerializeField] Material baseMaterial;
        [SerializeField, HideInInspector] GameObject generatedRoot;

        readonly Dictionary<Color32, Material> materialCache = new();

        public TextAsset VoxelTextFile
        {
            get => voxelTextFile;
            set => voxelTextFile = value;
        }

        public void Reconstruct()
        {
            if (voxelTextFile == null)
            {
                Debug.LogError("Voxel Reconstructor needs a voxel text file before reconstructing.", this);
                return;
            }

            if (!TryParse(voxelTextFile.text, out var voxels))
            {
                Debug.LogError($"Voxel Reconstructor could not parse '{voxelTextFile.name}'.", this);
                return;
            }

            ClearGeneratedChildren();
            materialCache.Clear();

            generatedRoot = new GameObject($"{voxelTextFile.name}_Voxels");
            generatedRoot.transform.SetParent(transform, false);

            var offset = centerOnOrigin ? CalculateCenterOffset(voxels) : Vector3.zero;
            var count = maxVoxels > 0 ? Mathf.Min(maxVoxels, voxels.Count) : voxels.Count;

            try
            {
                for (var i = 0; i < count; i++)
                {
#if UNITY_EDITOR
                    if (i % 500 == 0)
                        EditorUtility.DisplayProgressBar("Reconstructing Voxels", $"{i} / {count}", (float)i / count);
#endif

                    var voxel = voxels[i];
                    var cube = CreateCube(generatedRoot.transform, voxel, offset);
                    cube.name = $"Voxel_{voxel.position.x}_{voxel.position.y}_{voxel.position.z}";

                    if (!generateColliders && cube.TryGetComponent<Collider>(out var collider))
                        DestroyObject(collider);

                    cube.isStatic = markStatic;
                    cube.AddComponent<VoxelBlock>().Initialize(
                        voxel.color,
                        EstimateMass(voxel.color),
                        ClassifySurface(voxel.color));
                }
            }
            finally
            {
#if UNITY_EDITOR
                EditorUtility.ClearProgressBar();
#endif
            }

            Debug.Log($"Reconstructed {count.ToString(CultureInfo.InvariantCulture)} voxels from '{voxelTextFile.name}'.", generatedRoot);
        }

        public void ClearGeneratedChildren()
        {
            if (generatedRoot == null)
                return;

            DestroyObject(generatedRoot);
            generatedRoot = null;
        }

        static bool TryParse(string text, out List<VoxelData> voxels)
        {
            voxels = new List<VoxelData>();

            var lines = text.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("position", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split(';');
                if (parts.Length != 2 ||
                    !TryParseVector3Int(parts[0], out var position) ||
                    !TryParseColor(parts[1], out var color))
                {
                    Debug.LogWarning($"Skipped invalid voxel line {i + 1}: {line}");
                    continue;
                }

                voxels.Add(new VoxelData(position, color));
            }

            return voxels.Count > 0;
        }

        static bool TryParseVector3Int(string value, out Vector3Int result)
        {
            result = default;
            if (!TryParseBracketedInts(value, out var numbers) || numbers.Length != 3)
                return false;

            result = new Vector3Int(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        static bool TryParseColor(string value, out Color32 result)
        {
            result = default;
            if (!TryParseBracketedInts(value, out var numbers) || numbers.Length != 3)
                return false;

            result = new Color32(ToByte(numbers[0]), ToByte(numbers[1]), ToByte(numbers[2]), 255);
            return true;
        }

        static bool TryParseBracketedInts(string value, out int[] numbers)
        {
            numbers = Array.Empty<int>();
            value = value.Trim();

            if (value.Length < 5 || value[0] != '[' || value[value.Length - 1] != ']')
                return false;

            var tokens = value.Substring(1, value.Length - 2).Split(',');
            numbers = new int[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }

            return true;
        }

        GameObject CreateCube(Transform root, VoxelData voxel, Vector3 offset)
        {
            var cube = cubePrefab != null
                ? Instantiate(cubePrefab, root)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            cube.transform.SetParent(root, false);
            cube.transform.localPosition = (Vector3)voxel.position * voxelSize + offset;
            cube.transform.localScale = Vector3.one * voxelSize;

            if (cube.TryGetComponent<Renderer>(out var renderer))
                renderer.sharedMaterial = GetMaterial(voxel.color);

            return cube;
        }

        Material GetMaterial(Color32 color)
        {
            if (materialCache.TryGetValue(color, out var material))
                return material;

            material = baseMaterial != null
                ? new Material(baseMaterial)
                : new Material(FindDefaultShader());

            material.name = $"Voxel_{color.r}_{color.g}_{color.b}";
            material.color = color;
            materialCache.Add(color, material);
            return material;
        }

        static Shader FindDefaultShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Standard") ??
                   Shader.Find("Unlit/Color");
        }

        Vector3 CalculateCenterOffset(List<VoxelData> voxels)
        {
            var min = voxels[0].position;
            var max = voxels[0].position;

            for (var i = 1; i < voxels.Count; i++)
            {
                min = Vector3Int.Min(min, voxels[i].position);
                max = Vector3Int.Max(max, voxels[i].position);
            }

            return -((Vector3)(min + max) * 0.5f * voxelSize);
        }

        static byte ToByte(int value) => (byte)Mathf.Clamp(value, byte.MinValue, byte.MaxValue);

        static float EstimateMass(Color32 color)
        {
            var brightness = (color.r + color.g + color.b) / (3f * byte.MaxValue);
            return Mathf.Lerp(1.5f, 0.35f, brightness);
        }

        static VoxelSurfaceType ClassifySurface(Color32 color)
        {
            var brightness = (color.r + color.g + color.b) / 3f;
            if (brightness < 35f) return VoxelSurfaceType.Dark;
            if (color.r > color.g * 1.4f && color.r > color.b * 1.4f) return VoxelSurfaceType.Organic;
            if (Mathf.Abs(color.r - color.g) < 18f && Mathf.Abs(color.g - color.b) < 18f) return VoxelSurfaceType.Metal;
            return VoxelSurfaceType.Default;
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        readonly struct VoxelData
        {
            public readonly Vector3Int position;
            public readonly Color32 color;

            public VoxelData(Vector3Int position, Color32 color)
            {
                this.position = position;
                this.color = color;
            }
        }
    }
}
