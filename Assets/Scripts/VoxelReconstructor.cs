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
        [SerializeField, Min(1)] int chunkSize = 16;
        [SerializeField] bool generateColliders = true;
        [SerializeField] bool markStatic = true;
        [SerializeField, Min(0)] int maxVoxels;
        [SerializeField, Min(0.005f)] float impactRadius = 0.025f;
        [SerializeField, Min(1)] int maxVoxelsPerHit = 6;
        [SerializeField, Min(1)] int debrisChunkSize = 2;

        [Header("Material")]
        [SerializeField] Material voxelMaterial;
        [SerializeField, HideInInspector] GameObject generatedRoot;

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

            var count = maxVoxels > 0 ? Mathf.Min(maxVoxels, voxels.Count) : voxels.Count;
            if (count < voxels.Count)
                voxels.RemoveRange(count, voxels.Count - count);

            ClearGeneratedChildren();

            generatedRoot = new GameObject($"{voxelTextFile.name}_VoxelWorld");
            generatedRoot.transform.SetParent(transform, false);
            generatedRoot.isStatic = markStatic;

            var offset = centerOnOrigin ? CalculateCenterOffset(voxels) : Vector3.zero;
            var world = generatedRoot.AddComponent<VoxelWorld>();
            world.BuildFrom(voxels, voxelSize, chunkSize, offset, GetVoxelMaterial(), generateColliders, markStatic, impactRadius, maxVoxelsPerHit, debrisChunkSize);

            Debug.Log($"Reconstructed {count.ToString(CultureInfo.InvariantCulture)} voxels as {world.ChunkCount.ToString(CultureInfo.InvariantCulture)} optimized chunks from '{voxelTextFile.name}'.", generatedRoot);
        }

        public void ClearGeneratedChildren()
        {
            if (generatedRoot != null)
            {
                DestroyUnityObject(generatedRoot);
                generatedRoot = null;
            }

            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name.StartsWith("VoxelChunk", StringComparison.Ordinal) ||
                    child.name.EndsWith("_VoxelWorld", StringComparison.Ordinal))
                    DestroyUnityObject(child);
            }
        }

        static bool TryParse(string text, out List<VoxelRecord> voxels)
        {
            voxels = new List<VoxelRecord>();

            var lines = text.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                for (var i = 0; i < lines.Length; i++)
                {
#if UNITY_EDITOR
                    if (i % 2000 == 0)
                        EditorUtility.DisplayProgressBar("Parsing Voxels", $"{i} / {lines.Length}", (float)i / lines.Length);
#endif

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

                    voxels.Add(new VoxelRecord(position, color, EstimateMass(color), ClassifySurface(color)));
                }
            }
            finally
            {
#if UNITY_EDITOR
                EditorUtility.ClearProgressBar();
#endif
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

        Material GetVoxelMaterial()
        {
            if (voxelMaterial != null)
                return voxelMaterial;

            var material = new Material(FindDefaultShader())
            {
                name = "Voxel Vertex Color"
            };
            return material;
        }

        static Shader FindDefaultShader()
        {
            return Shader.Find("Destruxion/Voxel Vertex Color") ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Sprites/Default") ??
                   Shader.Find("Unlit/Color");
        }

        Vector3 CalculateCenterOffset(List<VoxelRecord> voxels)
        {
            var min = voxels[0].Position;
            var max = voxels[0].Position;

            for (var i = 1; i < voxels.Count; i++)
            {
                min = Vector3Int.Min(min, voxels[i].Position);
                max = Vector3Int.Max(max, voxels[i].Position);
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

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    public readonly struct VoxelRecord
    {
        public readonly Vector3Int Position;
        public readonly Color32 Color;
        public readonly float Mass;
        public readonly VoxelSurfaceType SurfaceType;

        public VoxelRecord(Vector3Int position, Color32 color, float mass, VoxelSurfaceType surfaceType)
        {
            Position = position;
            Color = color;
            Mass = mass;
            SurfaceType = surfaceType;
        }
    }

    [Serializable]
    struct SerializedVoxelRecord
    {
        public Vector3Int position;
        public Color32 color;
        public float mass;
        public VoxelSurfaceType surfaceType;

        public SerializedVoxelRecord(VoxelRecord voxel)
        {
            position = voxel.Position;
            color = voxel.Color;
            mass = voxel.Mass;
            surfaceType = voxel.SurfaceType;
        }

        public VoxelRecord ToVoxelRecord()
        {
            return new VoxelRecord(position, color, mass, surfaceType);
        }
    }

    public sealed partial class VoxelWorld : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float voxelSize = 0.1f;
        [SerializeField, Min(1)] int chunkSize = 16;
        [SerializeField] float activationRadius = 0.025f;
        [SerializeField, Min(1)] int maxVoxelsPerHit = 6;
        [SerializeField, Min(1)] int debrisChunkSize = 2;
        [SerializeField] float minimumImpactImpulse = 1.5f;
        [SerializeField] float physicsSettleSpeed = 0.04f;
        [SerializeField] float physicsSettleAngularSpeed = 0.08f;
        [SerializeField] float settleDelay = 0.75f;
        [SerializeField] Material voxelMaterial;
        [SerializeField] Vector3 originOffset;
        [SerializeField] bool generateColliders;
        [SerializeField] bool markStatic;
        [SerializeField, HideInInspector] List<SerializedVoxelRecord> serializedVoxels = new();

        readonly Dictionary<Vector3Int, VoxelRecord> voxels = new();
        readonly Dictionary<Vector3Int, VoxelChunk> chunks = new();

        public int ChunkCount => chunks.Count;
        public float VoxelSize => voxelSize;
        public float ActivationRadius => activationRadius;
        public float MinimumImpactImpulse => minimumImpactImpulse;
        public float PhysicsSettleSpeed => physicsSettleSpeed;
        public float PhysicsSettleAngularSpeed => physicsSettleAngularSpeed;
        public float SettleDelay => settleDelay;

        void OnEnable()
        {
            if (voxels.Count == 0 && serializedVoxels.Count > 0)
            {
                RestoreSerializedVoxels();
                CacheExistingChunks();
            }
        }

        public void BuildFrom(
            List<VoxelRecord> sourceVoxels,
            float sourceVoxelSize,
            int sourceChunkSize,
            Vector3 sourceOriginOffset,
            Material sourceMaterial,
            bool sourceGenerateColliders,
            bool sourceMarkStatic,
            float sourceActivationRadius,
            int sourceMaxVoxelsPerHit,
            int sourceDebrisChunkSize)
        {
            voxelSize = sourceVoxelSize;
            chunkSize = sourceChunkSize;
            originOffset = sourceOriginOffset;
            voxelMaterial = sourceMaterial;
            generateColliders = sourceGenerateColliders;
            markStatic = sourceMarkStatic;
            activationRadius = sourceActivationRadius;
            maxVoxelsPerHit = sourceMaxVoxelsPerHit;
            debrisChunkSize = sourceDebrisChunkSize;

            voxels.Clear();
            chunks.Clear();
            serializedVoxels.Clear();

            for (var i = 0; i < sourceVoxels.Count; i++)
            {
                voxels[sourceVoxels[i].Position] = sourceVoxels[i];
                serializedVoxels.Add(new SerializedVoxelRecord(sourceVoxels[i]));
            }

            RebuildAllChunks();
        }

        void RestoreSerializedVoxels()
        {
            voxels.Clear();
            chunks.Clear();

            for (var i = 0; i < serializedVoxels.Count; i++)
            {
                var voxel = serializedVoxels[i].ToVoxelRecord();
                voxels[voxel.Position] = voxel;
            }
        }

        void CacheExistingChunks()
        {
            chunks.Clear();
            var existingChunks = GetComponentsInChildren<VoxelChunk>(true);
            for (var i = 0; i < existingChunks.Length; i++)
            {
                if (existingChunks[i] == null)
                    continue;

                existingChunks[i].SetWorld(this);
                chunks[existingChunks[i].ChunkCoord] = existingChunks[i];
            }
        }

        public bool ContainsVoxel(Vector3Int position) => voxels.ContainsKey(position);

        public Vector3 VoxelToLocalCenter(Vector3Int position) => (Vector3)position * voxelSize + originOffset;

        public Vector3 LocalToWorldCenter(Vector3Int position) => transform.TransformPoint(VoxelToLocalCenter(position));

        public Vector3Int WorldToVoxel(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition) - originOffset;
            return new Vector3Int(
                Mathf.RoundToInt(local.x / voxelSize),
                Mathf.RoundToInt(local.y / voxelSize),
                Mathf.RoundToInt(local.z / voxelSize));
        }

        public bool TryFindVoxelImpact(Vector3 worldFrom, Vector3 worldTo, float sweepRadius, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = worldTo;
            hitNormal = (worldFrom - worldTo).sqrMagnitude > 0.001f ? (worldFrom - worldTo).normalized : -transform.forward;

            if (voxels.Count == 0)
                return false;

            var distance = Vector3.Distance(worldFrom, worldTo);
            var stepDistance = Mathf.Max(voxelSize * 0.25f, 0.01f);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / stepDistance));
            var radiusVoxels = Mathf.Max(1, Mathf.CeilToInt(sweepRadius / voxelSize));

            for (var i = 0; i <= steps; i++)
            {
                var sample = Vector3.Lerp(worldFrom, worldTo, (float)i / steps);
                var center = WorldToVoxel(sample);

                for (var x = -radiusVoxels; x <= radiusVoxels; x++)
                for (var y = -radiusVoxels; y <= radiusVoxels; y++)
                for (var z = -radiusVoxels; z <= radiusVoxels; z++)
                {
                    var candidate = center + new Vector3Int(x, y, z);
                    if (!voxels.ContainsKey(candidate))
                        continue;

                    var voxelCenter = LocalToWorldCenter(candidate);
                    hitPoint = voxelCenter;
                    hitNormal = (sample - voxelCenter).sqrMagnitude > 0.001f
                        ? (sample - voxelCenter).normalized
                        : (worldFrom - worldTo).normalized;
                    return true;
                }
            }

            return false;
        }

        public void ActivateVoxelsAround(Vector3 worldPosition, Vector3 impulse)
        {
            var center = WorldToVoxel(worldPosition);
            var radiusVoxels = Mathf.Max(1, Mathf.CeilToInt(activationRadius / voxelSize));
            var maxRemoved = Mathf.Max(1, maxVoxelsPerHit);
            var changedChunks = new HashSet<Vector3Int>();
            var candidates = new List<Vector3Int>();
            var removedVoxels = new List<VoxelRecord>(maxRemoved);
            var activationImpulse = impulse.sqrMagnitude > 0.001f
                ? impulse
                : UnityEngine.Random.insideUnitSphere * minimumImpactImpulse;

            for (var x = -radiusVoxels; x <= radiusVoxels; x++)
            for (var y = -radiusVoxels; y <= radiusVoxels; y++)
            for (var z = -radiusVoxels; z <= radiusVoxels; z++)
            {
                var offset = new Vector3Int(x, y, z);
                if (offset.sqrMagnitude > radiusVoxels * radiusVoxels)
                    continue;

                var position = center + offset;
                if (voxels.ContainsKey(position))
                    candidates.Add(position);
            }

            candidates.Sort((a, b) =>
                (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));

            for (var i = 0; i < candidates.Count && removedVoxels.Count < maxRemoved; i++)
            {
                var position = candidates[i];
                if (!voxels.TryGetValue(position, out var voxel))
                    continue;

                voxels.Remove(position);
                AddChunkAndNeighbors(position, changedChunks);
                removedVoxels.Add(voxel);
            }

            if (removedVoxels.Count == 0)
                return;

            RebuildChunks(changedChunks);
            SpawnDebrisChunks(removedVoxels, activationImpulse);
        }

        public void Restabilize(VoxelPhysicsBlock block)
        {
            var position = WorldToVoxel(block.transform.position);
            var voxel = new VoxelRecord(position, block.SourceColor, block.Mass, block.SurfaceType);
            voxels[position] = voxel;

            var changedChunks = new HashSet<Vector3Int>();
            AddChunkAndNeighbors(position, changedChunks);
            RebuildChunks(changedChunks);
            UnityEngine.Object.Destroy(block.gameObject);
        }

        void SpawnDebrisChunks(List<VoxelRecord> removedVoxels, Vector3 impulse)
        {
            if (removedVoxels.Count == 0)
                return;

            if (removedVoxels.Count <= debrisChunkSize)
            {
                SpawnPhysicsVoxelGroup(removedVoxels, impulse);
                return;
            }

            var groups = new Dictionary<Vector3Int, List<VoxelRecord>>();
            for (var i = 0; i < removedVoxels.Count; i++)
            {
                var key = GetDebrisGroupCoord(removedVoxels[i].Position);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<VoxelRecord>();
                    groups.Add(key, group);
                }

                group.Add(removedVoxels[i]);
            }

            foreach (var group in groups.Values)
                SpawnPhysicsVoxelGroup(group, impulse);
        }

        Vector3Int GetDebrisGroupCoord(Vector3Int position)
        {
            return new Vector3Int(
                FloorDiv(position.x, debrisChunkSize),
                FloorDiv(position.y, debrisChunkSize),
                FloorDiv(position.z, debrisChunkSize));
        }

        void SpawnPhysicsVoxelGroup(List<VoxelRecord> group, Vector3 impulse)
        {
            if (group.Count == 0)
                return;

            var root = new GameObject($"DebrisChunk_{group.Count}");
            var center = Vector3.zero;
            var totalMass = 0f;

            for (var i = 0; i < group.Count; i++)
            {
                center += LocalToWorldCenter(group[i].Position);
                totalMass += group[i].Mass;
            }

            center /= group.Count;
            root.transform.position = center;
            root.transform.rotation = transform.rotation;

            for (var i = 0; i < group.Count; i++)
                SpawnPhysicsVoxel(group[i], root.transform);

            var body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.05f, totalMass);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.AddForce(impulse, ForceMode.Impulse);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * impulse.magnitude * voxelSize, ForceMode.Impulse);

            root.AddComponent<VoxelDebrisChunk>().Initialize(Mathf.Max(minimumImpactImpulse * 1.25f, impulse.magnitude * 0.2f));
        }

        void SpawnPhysicsVoxel(VoxelRecord voxel, Transform root)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"LooseVoxel_{voxel.Position.x}_{voxel.Position.y}_{voxel.Position.z}";
            cube.transform.SetParent(root, true);
            cube.transform.position = LocalToWorldCenter(voxel.Position);
            cube.transform.rotation = transform.rotation;
            cube.transform.localScale = Vector3.one * voxelSize;

            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = voxelMaterial;
            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor("_BaseColor", voxel.Color);
            propertyBlock.SetColor("_Color", voxel.Color);
            renderer.SetPropertyBlock(propertyBlock);

            cube.AddComponent<VoxelBlock>().Initialize(voxel.Color, voxel.Mass, voxel.SurfaceType);
        }

        void RebuildAllChunks()
        {
            foreach (Transform child in transform)
                DestroyUnityObject(child.gameObject);

            chunks.Clear();

            var chunkCoords = new HashSet<Vector3Int>();
            foreach (var voxel in voxels.Keys)
                chunkCoords.Add(GetChunkCoord(voxel));

            RebuildChunks(chunkCoords);
        }

        void RebuildChunks(HashSet<Vector3Int> chunkCoords)
        {
            foreach (var chunkCoord in chunkCoords)
            {
                var chunkVoxels = GetChunkVoxels(chunkCoord);
                if (chunkVoxels.Count == 0)
                {
                    if (chunks.TryGetValue(chunkCoord, out var emptyChunk))
                    {
                        chunks.Remove(chunkCoord);
                        DestroyUnityObject(emptyChunk.gameObject);
                    }

                    continue;
                }

                if (!chunks.TryGetValue(chunkCoord, out var chunk))
                {
                    var chunkObject = new GameObject($"VoxelChunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
                    chunkObject.transform.SetParent(transform, false);
                    chunkObject.isStatic = markStatic;
                    chunk = chunkObject.AddComponent<VoxelChunk>();
                    chunks.Add(chunkCoord, chunk);
                }

                chunk.Build(this, chunkCoord, chunkVoxels, voxelMaterial, generateColliders);
            }
        }

        List<VoxelRecord> GetChunkVoxels(Vector3Int chunkCoord)
        {
            var chunkVoxels = new List<VoxelRecord>();
            foreach (var voxel in voxels.Values)
            {
                if (GetChunkCoord(voxel.Position) == chunkCoord)
                    chunkVoxels.Add(voxel);
            }

            return chunkVoxels;
        }

        Vector3Int GetChunkCoord(Vector3Int position)
        {
            return new Vector3Int(
                FloorDiv(position.x, chunkSize),
                FloorDiv(position.y, chunkSize),
                FloorDiv(position.z, chunkSize));
        }

        void AddChunkAndNeighbors(Vector3Int voxelPosition, HashSet<Vector3Int> chunkCoords)
        {
            chunkCoords.Add(GetChunkCoord(voxelPosition));
            chunkCoords.Add(GetChunkCoord(voxelPosition + Vector3Int.right));
            chunkCoords.Add(GetChunkCoord(voxelPosition + Vector3Int.left));
            chunkCoords.Add(GetChunkCoord(voxelPosition + Vector3Int.up));
            chunkCoords.Add(GetChunkCoord(voxelPosition + Vector3Int.down));
            chunkCoords.Add(GetChunkCoord(voxelPosition + new Vector3Int(0, 0, 1)));
            chunkCoords.Add(GetChunkCoord(voxelPosition + new Vector3Int(0, 0, -1)));
        }

        static int FloorDiv(int value, int divisor)
        {
            return value >= 0 ? value / divisor : (value - divisor + 1) / divisor;
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed partial class VoxelChunk : MonoBehaviour
    {
        static readonly Vector3Int[] Directions =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new(0, 0, 1),
            new(0, 0, -1)
        };

        static readonly Vector3[,] FaceCorners =
        {
            {new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f)},
            {new(-0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, 0.5f)},
            {new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)},
            {new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f)},
            {new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f)},
            {new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f)}
        };

        static readonly Vector3[] Normals =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back
        };

        VoxelWorld world;
        [SerializeField] Vector3Int chunkCoord;
        Mesh mesh;

        public Vector3Int ChunkCoord => chunkCoord;

        public void SetWorld(VoxelWorld sourceWorld)
        {
            world = sourceWorld;
        }

        public void Build(VoxelWorld sourceWorld, Vector3Int chunkCoord, List<VoxelRecord> voxels, Material material, bool generateCollider)
        {
            world = sourceWorld;
            this.chunkCoord = chunkCoord;

            var vertices = new List<Vector3>(voxels.Count * 12);
            var normals = new List<Vector3>(voxels.Count * 12);
            var colors = new List<Color32>(voxels.Count * 12);
            var triangles = new List<int>(voxels.Count * 18);

            for (var i = 0; i < voxels.Count; i++)
                AddVisibleFaces(voxels[i], vertices, normals, colors, triangles);

            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = $"VoxelChunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}_Mesh"
                };
                mesh.MarkDynamic();
            }
            else
            {
                mesh.Clear();
            }

            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = material;

            if (generateCollider)
            {
                var meshCollider = GetComponent<MeshCollider>();
                if (meshCollider == null)
                    meshCollider = gameObject.AddComponent<MeshCollider>();

                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }
            else if (TryGetComponent<MeshCollider>(out var collider))
            {
                DestroyUnityObject(collider);
            }
        }

        void AddVisibleFaces(
            VoxelRecord voxel,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<int> triangles)
        {
            var center = world.VoxelToLocalCenter(voxel.Position);
            var isSurfaceVoxel = false;

            for (var face = 0; face < Directions.Length; face++)
            {
                if (!world.ContainsVoxel(voxel.Position + Directions[face]))
                {
                    isSurfaceVoxel = true;
                    break;
                }
            }

            if (!isSurfaceVoxel)
                return;

            for (var face = 0; face < Directions.Length; face++)
            {
                var vertexIndex = vertices.Count;
                for (var corner = 0; corner < 4; corner++)
                {
                    vertices.Add(center + FaceCorners[face, corner] * world.VoxelSize);
                    normals.Add(Normals[face]);
                    colors.Add(voxel.Color);
                }

                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 2);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 3);
                triangles.Add(vertexIndex + 2);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (world == null || collision.impulse.magnitude < world.MinimumImpactImpulse || collision.contactCount == 0)
                return;

            if (collision.collider.GetComponent("VoxelProjectile") != null)
                return;

            world.ActivateVoxelsAround(collision.GetContact(0).point, collision.impulse);
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [RequireComponent(typeof(Rigidbody))]
    public sealed partial class VoxelPhysicsBlock : MonoBehaviour
    {
        VoxelWorld world;
        Rigidbody body;
        float stillTimer;

        public Color32 SourceColor { get; private set; }
        public float Mass { get; private set; }
        public VoxelSurfaceType SurfaceType { get; private set; }

        public void Initialize(VoxelWorld sourceWorld, Color32 sourceColor, float mass, VoxelSurfaceType surfaceType)
        {
            world = sourceWorld;
            SourceColor = sourceColor;
            Mass = mass;
            SurfaceType = surfaceType;
            body = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (world == null || body == null)
                return;

            if (body.linearVelocity.sqrMagnitude <= world.PhysicsSettleSpeed * world.PhysicsSettleSpeed &&
                body.angularVelocity.sqrMagnitude <= world.PhysicsSettleAngularSpeed * world.PhysicsSettleAngularSpeed)
            {
                stillTimer += Time.fixedDeltaTime;
                if (stillTimer >= world.SettleDelay)
                    world.Restabilize(this);
            }
            else
            {
                stillTimer = 0f;
            }
        }
    }

    public sealed class VoxelDebrisChunk : MonoBehaviour
    {
        float breakImpulse = 3f;
        bool broken;

        public void Initialize(float impulse)
        {
            breakImpulse = impulse;
        }

        public void BreakApart(Vector3 impulse)
        {
            if (broken)
                return;

            broken = true;
            var parentBody = GetComponent<Rigidbody>();
            var inheritedVelocity = parentBody != null ? parentBody.linearVelocity : Vector3.zero;

            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                child.SetParent(null, true);

                var body = child.gameObject.AddComponent<Rigidbody>();
                body.mass = child.TryGetComponent<VoxelBlock>(out var block) ? block.Mass : 0.25f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = inheritedVelocity;
                body.AddForce(impulse + UnityEngine.Random.insideUnitSphere * impulse.magnitude * 0.35f, ForceMode.Impulse);
            }

            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.impulse.magnitude >= breakImpulse &&
                collision.collider.GetComponent("VoxelProjectile") != null)
                BreakApart(collision.impulse);
        }
    }
}
