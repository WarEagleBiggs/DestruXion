using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        [SerializeField, Min(0.01f)] float voxelSize = 0.02f;
        [SerializeField] bool centerOnOrigin = true;

        [Header("Destruction")]
        [SerializeField] VoxelDamageProfile damageProfile = VoxelDamageProfile.DrywallHammer;

        [Header("Material")]
        [SerializeField] Material voxelMaterial;
        [SerializeField, HideInInspector, Min(1)] int chunkSize = 24;
        [SerializeField, HideInInspector] bool generateColliders = true;
        [SerializeField, HideInInspector] bool markStatic = true;
        [SerializeField, HideInInspector, Min(0)] int maxVoxels;
        [SerializeField, HideInInspector, Min(0.25f)] float damageRadiusMultiplier = 1.35f;
        [SerializeField, HideInInspector, Min(1)] int maxVoxelsPerHit = 24;
        [SerializeField, HideInInspector, Min(1)] int debrisChunkSize = 64;
        [SerializeField, HideInInspector] GameObject generatedRoot;

        public TextAsset VoxelTextFile
        {
            get => voxelTextFile;
            set => voxelTextFile = value;
        }

        public void Reconstruct()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
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

            var parseMilliseconds = stopwatch.ElapsedMilliseconds;
            var count = maxVoxels > 0 ? Mathf.Min(maxVoxels, voxels.Count) : voxels.Count;
            if (count < voxels.Count)
                voxels.RemoveRange(count, voxels.Count - count);

            ClearGeneratedChildren();
            ApplyDamageProfile();

            generatedRoot = new GameObject($"{voxelTextFile.name}_VoxelWorld");
            generatedRoot.transform.SetParent(transform, false);
            generatedRoot.isStatic = markStatic;

            var offset = centerOnOrigin ? CalculateCenterOffset(voxels) : Vector3.zero;
            var world = generatedRoot.AddComponent<VoxelWorld>();
            world.BuildFrom(voxels, voxelSize, chunkSize, offset, GetVoxelMaterial(), generateColliders, markStatic, damageRadiusMultiplier, maxVoxelsPerHit, debrisChunkSize);

            Debug.Log($"Reconstructed {count.ToString(CultureInfo.InvariantCulture)} voxels as {world.ChunkCount.ToString(CultureInfo.InvariantCulture)} optimized chunks from '{voxelTextFile.name}' in {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms. Parse: {parseMilliseconds.ToString(CultureInfo.InvariantCulture)} ms, build: {(stopwatch.ElapsedMilliseconds - parseMilliseconds).ToString(CultureInfo.InvariantCulture)} ms.", generatedRoot);
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
            voxels = new List<VoxelRecord>(Mathf.Max(1024, text.Length / 32));

            var lineNumber = 0;
            using var reader = new StringReader(text);
            try
            {
                while (reader.ReadLine() is { } rawLine)
                {
                    lineNumber++;
#if UNITY_EDITOR
                    if (lineNumber % 5000 == 0)
                        EditorUtility.DisplayProgressBar("Parsing Voxels", $"{voxels.Count.ToString(CultureInfo.InvariantCulture)} voxels read", Mathf.Repeat(lineNumber * 0.0002f, 1f));
#endif

                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("position", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var separator = line.IndexOf(';');
                    if (separator <= 0 ||
                        separator >= line.Length - 1 ||
                        !TryParseVector3Int(line, 0, separator, out var position) ||
                        !TryParseColor(line, separator + 1, line.Length - separator - 1, out var color))
                    {
                        Debug.LogWarning($"Skipped invalid voxel line {lineNumber}: {line}");
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

        static bool TryParseVector3Int(string value, int start, int length, out Vector3Int result)
        {
            result = default;
            if (!TryParseBracketedInt3(value, start, length, out var x, out var y, out var z))
                return false;

            result = new Vector3Int(x, y, z);
            return true;
        }

        static bool TryParseColor(string value, int start, int length, out Color32 result)
        {
            result = default;
            if (!TryParseBracketedInt3(value, start, length, out var r, out var g, out var b))
                return false;

            result = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
            return true;
        }

        static bool TryParseBracketedInt3(string value, int start, int length, out int first, out int second, out int third)
        {
            first = default;
            second = default;
            third = default;

            var end = start + length - 1;
            while (start <= end && char.IsWhiteSpace(value[start]))
                start++;

            while (end >= start && char.IsWhiteSpace(value[end]))
                end--;

            if (end - start < 4 || value[start] != '[' || value[end] != ']')
                return false;

            var cursor = start + 1;
            if (!TryReadInt(value, ref cursor, end, out first))
                return false;

            if (!TryReadSeparator(value, ref cursor, end))
                return false;

            if (!TryReadInt(value, ref cursor, end, out second))
                return false;

            if (!TryReadSeparator(value, ref cursor, end))
                return false;

            if (!TryReadInt(value, ref cursor, end, out third))
                return false;

            while (cursor < end && char.IsWhiteSpace(value[cursor]))
                cursor++;

            return cursor == end;
        }

        static bool TryReadSeparator(string value, ref int cursor, int end)
        {
            while (cursor < end && char.IsWhiteSpace(value[cursor]))
                cursor++;

            if (cursor >= end || value[cursor] != ',')
                return false;

            cursor++;
            return true;
        }

        static bool TryReadInt(string value, ref int cursor, int end, out int number)
        {
            number = 0;
            while (cursor < end && char.IsWhiteSpace(value[cursor]))
                cursor++;

            var sign = 1;
            if (cursor < end && value[cursor] == '-')
            {
                sign = -1;
                cursor++;
            }

            var foundDigit = false;
            while (cursor < end && char.IsDigit(value[cursor]))
            {
                foundDigit = true;
                number = number * 10 + value[cursor] - '0';
                cursor++;
            }

            number *= sign;
            return foundDigit;
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

        void ApplyDamageProfile()
        {
            var settings = VoxelDamageSettings.ForProfile(damageProfile);
            chunkSize = settings.ChunkSize;
            damageRadiusMultiplier = settings.DamageRadiusMultiplier;
            maxVoxelsPerHit = settings.MaxVoxelsPerHit;
            debrisChunkSize = settings.DebrisChunkSize;
            generateColliders = true;
            markStatic = true;
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

    public enum VoxelDamageProfile
    {
        DrywallHammer,
        LightChips,
        HeavyBreach
    }

    readonly struct VoxelDamageSettings
    {
        public readonly int ChunkSize;
        public readonly float DamageRadiusMultiplier;
        public readonly int MaxVoxelsPerHit;
        public readonly int DebrisChunkSize;

        VoxelDamageSettings(int chunkSize, float damageRadiusMultiplier, int maxVoxelsPerHit, int debrisChunkSize)
        {
            ChunkSize = chunkSize;
            DamageRadiusMultiplier = damageRadiusMultiplier;
            MaxVoxelsPerHit = maxVoxelsPerHit;
            DebrisChunkSize = debrisChunkSize;
        }

        public static VoxelDamageSettings ForProfile(VoxelDamageProfile profile)
        {
            return profile switch
            {
                VoxelDamageProfile.LightChips => new VoxelDamageSettings(32, 0.9f, 12, 32),
                VoxelDamageProfile.HeavyBreach => new VoxelDamageSettings(32, 2.8f, 220, 512),
                _ => new VoxelDamageSettings(32, 2.2f, 96, 256)
            };
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
        static readonly List<VoxelWorld> activeWorlds = new();

        [SerializeField, Min(0.01f)] float voxelSize = 0.1f;
        [SerializeField, Min(1)] int chunkSize = 16;
        [SerializeField] float damageRadiusMultiplier = 1.35f;
        [SerializeField, Min(1)] int maxVoxelsPerHit = 6;
        [SerializeField, Min(1)] int debrisChunkSize = 64;
        [SerializeField, Min(0)] int unsupportedSearchPadding = 8;
        [SerializeField, Min(1)] int maxUnsupportedVoxelsPerHit = 180;
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
        readonly Dictionary<Vector3Int, List<VoxelRecord>> chunkVoxelCache = new();

        public static IReadOnlyList<VoxelWorld> ActiveWorlds => activeWorlds;
        public int ChunkCount => chunks.Count;
        public float VoxelSize => voxelSize;
        public float DamageRadiusMultiplier => damageRadiusMultiplier;
        public float MinimumImpactImpulse => minimumImpactImpulse;
        public float PhysicsSettleSpeed => physicsSettleSpeed;
        public float PhysicsSettleAngularSpeed => physicsSettleAngularSpeed;
        public float SettleDelay => settleDelay;

        void OnEnable()
        {
            if (!activeWorlds.Contains(this))
                activeWorlds.Add(this);

            if (voxels.Count == 0 && serializedVoxels.Count > 0)
            {
                RestoreSerializedVoxels();
                CacheExistingChunks();
            }
        }

        void OnDisable()
        {
            activeWorlds.Remove(this);
        }

        public void BuildFrom(
            List<VoxelRecord> sourceVoxels,
            float sourceVoxelSize,
            int sourceChunkSize,
            Vector3 sourceOriginOffset,
            Material sourceMaterial,
            bool sourceGenerateColliders,
            bool sourceMarkStatic,
            float sourceDamageRadiusMultiplier,
            int sourceMaxVoxelsPerHit,
            int sourceDebrisChunkSize)
        {
            voxelSize = sourceVoxelSize;
            chunkSize = sourceChunkSize;
            originOffset = sourceOriginOffset;
            voxelMaterial = sourceMaterial;
            generateColliders = sourceGenerateColliders;
            markStatic = sourceMarkStatic;
            damageRadiusMultiplier = sourceDamageRadiusMultiplier;
            maxVoxelsPerHit = sourceMaxVoxelsPerHit;
            debrisChunkSize = sourceDebrisChunkSize;

            voxels.Clear();
            chunks.Clear();
            chunkVoxelCache.Clear();
            serializedVoxels.Clear();

            for (var i = 0; i < sourceVoxels.Count; i++)
            {
                AddVoxel(sourceVoxels[i]);
                serializedVoxels.Add(new SerializedVoxelRecord(sourceVoxels[i]));
            }

            RebuildAllChunks();
        }

        void RestoreSerializedVoxels()
        {
            voxels.Clear();
            chunks.Clear();
            chunkVoxelCache.Clear();

            for (var i = 0; i < serializedVoxels.Count; i++)
            {
                var voxel = serializedVoxels[i].ToVoxelRecord();
                AddVoxel(voxel);
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

        float WorldVoxelSize => voxelSize * WorldScale;

        float WorldScale
        {
            get
            {
                var scale = transform.lossyScale;
                return Mathf.Max(0.0001f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
            }
        }

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
            var stepDistance = Mathf.Max(WorldVoxelSize * 0.25f, 0.01f);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / stepDistance));
            var radiusVoxels = Mathf.Max(1, Mathf.CeilToInt(sweepRadius / WorldVoxelSize));

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

        public void ActivateVoxelsAround(Vector3 worldPosition, Vector3 impulse, float projectileRadius)
        {
            var center = WorldToVoxel(worldPosition);
            var effectiveRadius = Mathf.Max(voxelSize * 0.5f, projectileRadius * damageRadiusMultiplier / WorldScale);
            var radiusVoxels = Mathf.Max(1, Mathf.CeilToInt(effectiveRadius / voxelSize));
            var projectileVoxelRadius = Mathf.Max(1f, effectiveRadius / voxelSize);
            var projectileScaledLimit = Mathf.CeilToInt(projectileVoxelRadius * projectileVoxelRadius * 2f);
            var maxRemoved = Mathf.Clamp(projectileScaledLimit, 1, Mathf.Max(1, maxVoxelsPerHit));
            var changedChunks = new HashSet<Vector3Int>();
            var candidates = new List<Vector3Int>();
            var removedVoxels = new List<VoxelRecord>(maxRemoved);
            var removedPositions = new List<Vector3Int>(maxRemoved);
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

                RemoveVoxel(position);
                AddChunkAndNeighbors(position, changedChunks);
                removedVoxels.Add(voxel);
                removedPositions.Add(position);
            }

            if (removedVoxels.Count == 0)
                return;

            ReleaseUnsupportedVoxels(center, radiusVoxels + unsupportedSearchPadding, removedPositions, removedVoxels, changedChunks);
            RebuildChunks(changedChunks);
            SpawnDebrisChunks(removedVoxels, activationImpulse);
        }

        void ReleaseUnsupportedVoxels(
            Vector3Int center,
            int searchRadius,
            List<Vector3Int> removedPositions,
            List<VoxelRecord> releasedVoxels,
            HashSet<Vector3Int> changedChunks)
        {
            if (removedPositions.Count == 0 || maxUnsupportedVoxelsPerHit <= 0)
                return;

            var visited = new HashSet<Vector3Int>();
            var starts = new Queue<Vector3Int>();
            var component = new List<Vector3Int>();
            var queue = new Queue<Vector3Int>();

            for (var i = 0; i < removedPositions.Count; i++)
            {
                for (var direction = 0; direction < SixDirections.Length; direction++)
                {
                    var neighbor = removedPositions[i] + SixDirections[direction];
                    if (voxels.ContainsKey(neighbor))
                        starts.Enqueue(neighbor);
                }
            }

            while (starts.Count > 0 && releasedVoxels.Count < maxVoxelsPerHit + maxUnsupportedVoxelsPerHit)
            {
                var start = starts.Dequeue();
                if (visited.Contains(start) || !voxels.ContainsKey(start))
                    continue;

                component.Clear();
                queue.Clear();
                queue.Enqueue(start);
                visited.Add(start);

                var anchored = false;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);

                    if (IsOutsideLocalSearch(current, center, searchRadius))
                        anchored = true;

                    if (component.Count > maxUnsupportedVoxelsPerHit)
                    {
                        anchored = true;
                        break;
                    }

                    for (var direction = 0; direction < SixDirections.Length; direction++)
                    {
                        var neighbor = current + SixDirections[direction];
                        if (visited.Contains(neighbor) || !voxels.ContainsKey(neighbor))
                            continue;

                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                if (anchored)
                    continue;

                for (var i = 0; i < component.Count; i++)
                {
                    if (!voxels.TryGetValue(component[i], out var voxel))
                        continue;

                    RemoveVoxel(component[i]);
                    AddChunkAndNeighbors(component[i], changedChunks);
                    releasedVoxels.Add(voxel);
                }
            }
        }

        static bool IsOutsideLocalSearch(Vector3Int position, Vector3Int center, int searchRadius)
        {
            return Mathf.Abs(position.x - center.x) >= searchRadius ||
                   Mathf.Abs(position.y - center.y) >= searchRadius ||
                   Mathf.Abs(position.z - center.z) >= searchRadius;
        }

        public void Restabilize(VoxelPhysicsBlock block)
        {
            var position = WorldToVoxel(block.transform.position);
            var voxel = new VoxelRecord(position, block.SourceColor, block.Mass, block.SurfaceType);
            AddVoxel(voxel);

            var changedChunks = new HashSet<Vector3Int>();
            AddChunkAndNeighbors(position, changedChunks);
            RebuildChunks(changedChunks);
            UnityEngine.Object.Destroy(block.gameObject);
        }

        void SpawnDebrisChunks(List<VoxelRecord> removedVoxels, Vector3 impulse)
        {
            if (removedVoxels.Count == 0)
                return;

            var byPosition = new Dictionary<Vector3Int, VoxelRecord>(removedVoxels.Count);
            for (var i = 0; i < removedVoxels.Count; i++)
                byPosition[removedVoxels[i].Position] = removedVoxels[i];

            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            var connectedGroup = new List<VoxelRecord>();

            for (var i = 0; i < removedVoxels.Count; i++)
            {
                var start = removedVoxels[i].Position;
                if (visited.Contains(start))
                    continue;

                connectedGroup.Clear();
                queue.Clear();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    connectedGroup.Add(byPosition[current]);

                    for (var direction = 0; direction < SixDirections.Length; direction++)
                    {
                        var neighbor = current + SixDirections[direction];
                        if (visited.Contains(neighbor) || !byPosition.ContainsKey(neighbor))
                            continue;

                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                SpawnConnectedDebrisGroup(connectedGroup, impulse);
            }
        }

        void SpawnConnectedDebrisGroup(List<VoxelRecord> connectedGroup, Vector3 impulse)
        {
            if (connectedGroup.Count == 0)
                return;

            if (connectedGroup.Count <= debrisChunkSize)
            {
                SpawnPhysicsVoxelGroup(connectedGroup, impulse);
                return;
            }

            var groups = new Dictionary<Vector3Int, List<VoxelRecord>>();
            for (var i = 0; i < connectedGroup.Count; i++)
            {
                var key = GetDebrisGroupCoord(connectedGroup[i].Position);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<VoxelRecord>();
                    groups.Add(key, group);
                }

                group.Add(connectedGroup[i]);
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

            BuildDebrisMesh(root, group);
            AddDebrisColliders(root, group);

            var body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.05f, totalMass);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.AddForce(impulse, ForceMode.Impulse);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * impulse.magnitude * WorldVoxelSize, ForceMode.Impulse);

            root.AddComponent<VoxelDebrisChunk>().Initialize(
                Mathf.Max(minimumImpactImpulse * 1.25f, impulse.magnitude * 0.2f),
                group.ToArray(),
                WorldVoxelSize,
                voxelMaterial);
        }

        void BuildDebrisMesh(GameObject root, List<VoxelRecord> group)
        {
            var groupPositions = new HashSet<Vector3Int>();
            for (var i = 0; i < group.Count; i++)
                groupPositions.Add(group[i].Position);

            var vertices = new List<Vector3>(group.Count * 12);
            var normals = new List<Vector3>(group.Count * 12);
            var colors = new List<Color32>(group.Count * 12);
            var triangles = new List<int>(group.Count * 18);

            for (var i = 0; i < group.Count; i++)
            {
                var localCenter = root.transform.InverseTransformPoint(LocalToWorldCenter(group[i].Position));
                for (var face = 0; face < DebrisDirections.Length; face++)
                {
                    if (groupPositions.Contains(group[i].Position + DebrisDirections[face]))
                        continue;

                    var vertexIndex = vertices.Count;
                    for (var corner = 0; corner < 4; corner++)
                    {
                        vertices.Add(localCenter + DebrisFaceCorners[face, corner] * WorldVoxelSize);
                        normals.Add(DebrisNormals[face]);
                        colors.Add(group[i].Color);
                    }

                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 2);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 3);
                    triangles.Add(vertexIndex + 2);
                }
            }

            var mesh = new Mesh
            {
                name = $"{root.name}_Mesh"
            };

            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = voxelMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        void AddDebrisColliders(GameObject root, List<VoxelRecord> group)
        {
            for (var i = 0; i < group.Count; i++)
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.center = root.transform.InverseTransformPoint(LocalToWorldCenter(group[i].Position));
                collider.size = Vector3.one * WorldVoxelSize;
            }
        }

        static readonly Vector3Int[] DebrisDirections =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new(0, 0, 1),
            new(0, 0, -1)
        };

        static readonly Vector3[,] DebrisFaceCorners =
        {
            {new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f)},
            {new(-0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, 0.5f)},
            {new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)},
            {new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f)},
            {new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f)},
            {new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f)}
        };

        static readonly Vector3[] DebrisNormals =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back
        };

        void RebuildAllChunks()
        {
            foreach (Transform child in transform)
                DestroyUnityObject(child.gameObject);

            chunks.Clear();

            var chunkCoords = new HashSet<Vector3Int>();
            foreach (var chunkCoord in chunkVoxelCache.Keys)
                chunkCoords.Add(chunkCoord);

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
            return chunkVoxelCache.TryGetValue(chunkCoord, out var chunkVoxels)
                ? chunkVoxels
                : new List<VoxelRecord>(0);
        }

        void AddVoxel(VoxelRecord voxel)
        {
            voxels[voxel.Position] = voxel;

            var chunkCoord = GetChunkCoord(voxel.Position);
            if (!chunkVoxelCache.TryGetValue(chunkCoord, out var chunkVoxels))
            {
                chunkVoxels = new List<VoxelRecord>();
                chunkVoxelCache.Add(chunkCoord, chunkVoxels);
            }

            for (var i = 0; i < chunkVoxels.Count; i++)
            {
                if (chunkVoxels[i].Position == voxel.Position)
                {
                    chunkVoxels[i] = voxel;
                    return;
                }
            }

            chunkVoxels.Add(voxel);
        }

        void RemoveVoxel(Vector3Int position)
        {
            voxels.Remove(position);

            var chunkCoord = GetChunkCoord(position);
            if (!chunkVoxelCache.TryGetValue(chunkCoord, out var chunkVoxels))
                return;

            for (var i = chunkVoxels.Count - 1; i >= 0; i--)
            {
                if (chunkVoxels[i].Position != position)
                    continue;

                var lastIndex = chunkVoxels.Count - 1;
                chunkVoxels[i] = chunkVoxels[lastIndex];
                chunkVoxels.RemoveAt(lastIndex);
                break;
            }

            if (chunkVoxels.Count == 0)
                chunkVoxelCache.Remove(chunkCoord);
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

        static readonly Vector3Int[] SixDirections =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new(0, 0, 1),
            new(0, 0, -1)
        };
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

            var vertices = new List<Vector3>(voxels.Count * 8);
            var normals = new List<Vector3>(voxels.Count * 8);
            var colors = new List<Color32>(voxels.Count * 8);
            var triangles = new List<int>(voxels.Count * 12);

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
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

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

            for (var face = 0; face < Directions.Length; face++)
            {
                if (world.ContainsVoxel(voxel.Position + Directions[face]))
                    continue;

                var vertexIndex = vertices.Count;
                for (var corner = 0; corner < 4; corner++)
                {
                    vertices.Add(center + FaceCorners[face, corner] * world.VoxelSize);
                    normals.Add(Normals[face]);
                    colors.Add(ShadeFace(voxel.Color, Normals[face], voxel.Position));
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
        }

        static Color32 ShadeFace(Color32 color, Vector3 normal, Vector3Int position)
        {
            var lightDirection = new Vector3(-0.35f, 0.8f, -0.45f).normalized;
            var light = Mathf.Clamp01(Vector3.Dot(normal, lightDirection)) * 0.35f + 0.78f;
            var variation = 1f + (Hash01(position) - 0.5f) * 0.05f;
            var factor = light * variation;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * factor), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * factor), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * factor), 0, 255),
                color.a);
        }

        static float Hash01(Vector3Int position)
        {
            unchecked
            {
                var hash = position.x * 73856093 ^ position.y * 19349663 ^ position.z * 83492791;
                hash ^= hash >> 13;
                hash *= 1274126177;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
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
        float voxelSize = 0.1f;
        Material voxelMaterial;
        VoxelRecord[] sourceVoxels = Array.Empty<VoxelRecord>();
        bool broken;

        public void Initialize(float impulse, VoxelRecord[] voxels, float sourceVoxelSize, Material sourceMaterial)
        {
            breakImpulse = impulse;
            sourceVoxels = voxels ?? Array.Empty<VoxelRecord>();
            voxelSize = sourceVoxelSize;
            voxelMaterial = sourceMaterial;
        }

        public void BreakApart(Vector3 impulse)
        {
            if (broken)
                return;

            broken = true;
            var parentBody = GetComponent<Rigidbody>();
            var inheritedVelocity = parentBody != null ? parentBody.linearVelocity : Vector3.zero;

            if (sourceVoxels.Length > 0)
            {
                for (var i = 0; i < sourceVoxels.Length; i++)
                    SpawnLooseVoxel(sourceVoxels[i], inheritedVelocity, impulse);
            }
            else
            {
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
            }

            Destroy(gameObject);
        }

        void SpawnLooseVoxel(VoxelRecord voxel, Vector3 inheritedVelocity, Vector3 impulse)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"LooseVoxel_{voxel.Position.x}_{voxel.Position.y}_{voxel.Position.z}";
            cube.transform.position = transform.position + transform.rotation * (((Vector3)voxel.Position * voxelSize) - GetAverageLocalPosition());
            cube.transform.rotation = transform.rotation;
            cube.transform.localScale = Vector3.one * voxelSize;

            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = voxelMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor("_BaseColor", voxel.Color);
            propertyBlock.SetColor("_Color", voxel.Color);
            renderer.SetPropertyBlock(propertyBlock);

            var body = cube.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.05f, voxel.Mass);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = inheritedVelocity;
            body.AddForce(impulse + UnityEngine.Random.insideUnitSphere * impulse.magnitude * 0.35f, ForceMode.Impulse);

            cube.AddComponent<VoxelBlock>().Initialize(voxel.Color, voxel.Mass, voxel.SurfaceType);
        }

        Vector3 GetAverageLocalPosition()
        {
            if (sourceVoxels.Length == 0)
                return Vector3.zero;

            var center = Vector3.zero;
            for (var i = 0; i < sourceVoxels.Length; i++)
                center += (Vector3)sourceVoxels[i].Position * voxelSize;

            return center / sourceVoxels.Length;
        }

        void OnCollisionEnter(Collision collision)
        {
            // Debris chunks stay bonded during normal physics collisions.
            // VoxelProjectile calls BreakApart directly when hit by a bullet.
        }
    }
}
