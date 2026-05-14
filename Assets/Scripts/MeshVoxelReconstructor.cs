using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Destruxion.Voxels
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DESTRUXion/Mesh Voxel Reconstructor")]
    public sealed class MeshVoxelReconstructor : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] Transform sourceRoot;

        [Header("Build")]
        [SerializeField, Min(0.01f)] float voxelSize = 0.05f;
        [SerializeField] MeshVoxelResolutionMode resolutionMode = MeshVoxelResolutionMode.TargetMaxDimension;
        [SerializeField, Min(8)] int targetResolution = 192;
        [SerializeField] MeshVoxelAlgorithm algorithm = MeshVoxelAlgorithm.AccurateRaycast;
        [SerializeField, Min(1)] int surfaceThickness = 1;
        [SerializeField, Range(0f, 1f)] float bakedShadingStrength = 0.45f;
        [SerializeField, Range(0f, 0.25f)] float cubeColorVariation = 0.08f;
        [SerializeField] bool fillSolid;
        [SerializeField] bool hideSourceRenderers = true;

        [Header("Destruction")]
        [SerializeField] VoxelDamageProfile damageProfile = VoxelDamageProfile.DrywallHammer;

        [Header("Material")]
        [SerializeField] Material voxelMaterial;
        [SerializeField, HideInInspector] GameObject generatedRoot;
        [SerializeField, HideInInspector] int lastFingerprint;
        [SerializeField, HideInInspector] float activeVoxelSize = 0.05f;

        void Reset()
        {
            sourceRoot = transform;
        }

        void OnEnable()
        {
            if (sourceRoot == null)
                sourceRoot = transform;
        }

        void OnValidate()
        {
            if (sourceRoot == null)
                sourceRoot = transform;
        }

        public void Reconstruct()
        {
            if (sourceRoot == null)
                sourceRoot = transform;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meshSources = CollectMeshSources();
            if (meshSources.Count == 0)
            {
                Debug.LogWarning("Mesh Voxel Reconstructor could not find any MeshFilter or SkinnedMeshRenderer sources to voxelize.", this);
                return;
            }

            if (!TryBuildTriangles(meshSources, out var triangles, out var bounds))
            {
                Debug.LogWarning("Mesh Voxel Reconstructor could not read triangles from the source meshes.", this);
                return;
            }

            if (triangles.Count == 0)
            {
                Debug.LogWarning("Mesh Voxel Reconstructor found meshes, but no triangles to voxelize.", this);
                return;
            }

            var triangleMilliseconds = stopwatch.ElapsedMilliseconds;
            activeVoxelSize = ResolveVoxelSize(bounds);
            var voxels = Voxelize(triangles, bounds);
            if (voxels.Count == 0)
            {
                Debug.LogWarning("Mesh Voxel Reconstructor produced 0 voxels. Try a larger voxel size or a closed mesh.", this);
                return;
            }

            var voxelizeMilliseconds = stopwatch.ElapsedMilliseconds - triangleMilliseconds;
            ClearGeneratedChildren();

            var settings = VoxelDamageSettings.ForProfile(damageProfile);
            generatedRoot = new GameObject($"{sourceRoot.name}_VoxelWorld");
            generatedRoot.transform.SetParent(transform, false);
            generatedRoot.isStatic = true;

            var world = generatedRoot.AddComponent<VoxelWorld>();
            world.BuildFrom(
                voxels,
                activeVoxelSize,
                settings.ChunkSize,
                Vector3.zero,
                GetVoxelMaterial(),
                true,
                true,
                settings.DamageRadiusMultiplier,
                settings.MaxVoxelsPerHit,
                settings.DebrisChunkSize);

            SetSourceRenderersVisible(!hideSourceRenderers);
            lastFingerprint = CalculateFingerprint();

            Debug.Log($"Voxelized '{sourceRoot.name}' into {voxels.Count.ToString(CultureInfo.InvariantCulture)} voxels across {world.ChunkCount.ToString(CultureInfo.InvariantCulture)} chunks at voxel size {activeVoxelSize.ToString(CultureInfo.InvariantCulture)} in {stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms. Triangles: {triangleMilliseconds.ToString(CultureInfo.InvariantCulture)} ms, voxelize: {voxelizeMilliseconds.ToString(CultureInfo.InvariantCulture)} ms.", generatedRoot);
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
                if (child.name.EndsWith("_VoxelWorld", StringComparison.Ordinal))
                    DestroyUnityObject(child);
            }

            SetSourceRenderersVisible(true);
        }

        List<MeshVoxelSource> CollectMeshSources()
        {
            var sources = new List<MeshVoxelSource>();
            var root = sourceRoot != null ? sourceRoot : transform;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null ||
                    filter.sharedMesh == null ||
                    filter.GetComponentInParent<VoxelWorld>() != null ||
                    generatedRoot != null && filter.transform.IsChildOf(generatedRoot.transform))
                    continue;

                sources.Add(new MeshVoxelSource(filter.sharedMesh, filter.GetComponent<Renderer>(), filter.transform.localToWorldMatrix, filter.name));
            }

            var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedRenderers.Length; i++)
            {
                var renderer = skinnedRenderers[i];
                if (renderer == null ||
                    renderer.sharedMesh == null ||
                    renderer.GetComponentInParent<VoxelWorld>() != null ||
                    generatedRoot != null && renderer.transform.IsChildOf(generatedRoot.transform))
                    continue;

                var bakedMesh = new Mesh {name = $"{renderer.sharedMesh.name}_BakedForVoxels"};
                renderer.BakeMesh(bakedMesh);
                sources.Add(new MeshVoxelSource(bakedMesh, renderer, renderer.transform.localToWorldMatrix, renderer.name));
            }

            return sources;
        }

        bool TryBuildTriangles(List<MeshVoxelSource> meshSources, out List<VoxelTriangle> triangles, out Bounds bounds)
        {
            triangles = new List<VoxelTriangle>();
            bounds = default;
            var hasBounds = false;
            var worldToLocal = transform.worldToLocalMatrix;

            try
            {
                for (var i = 0; i < meshSources.Count; i++)
                {
                    var sourceMesh = meshSources[i];
                    var mesh = sourceMesh.Mesh;
                    var vertices = mesh.vertices;
                    var renderer = sourceMesh.Renderer;
                    var materials = renderer != null ? renderer.sharedMaterials : Array.Empty<Material>();
                    var matrix = worldToLocal * sourceMesh.LocalToWorld;
                    var uvs = mesh.uv;
                    var colors = mesh.colors32;
                    var hasUvs = uvs != null && uvs.Length == vertices.Length;
                    var hasVertexColors = colors != null && colors.Length == vertices.Length;
                    if (vertices == null || vertices.Length == 0)
                    {
                        Debug.LogWarning($"Skipped mesh source '{sourceMesh.Name}' because it has no vertices.", this);
                        continue;
                    }

                    for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        var indices = mesh.GetIndices(submesh);
                        if (indices == null || indices.Length < 3)
                            continue;

                        var source = GetColorSource(materials, submesh);
                        for (var index = 0; index + 2 < indices.Length; index += 3)
                        {
                            var indexA = indices[index];
                            var indexB = indices[index + 1];
                            var indexC = indices[index + 2];
                            var a = matrix.MultiplyPoint3x4(vertices[indexA]);
                            var b = matrix.MultiplyPoint3x4(vertices[indexB]);
                            var c = matrix.MultiplyPoint3x4(vertices[indexC]);
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000001f)
                                continue;

                            var triangle = new VoxelTriangle(
                                a,
                                b,
                                c,
                                hasUvs ? uvs[indexA] : Vector2.zero,
                                hasUvs ? uvs[indexB] : Vector2.zero,
                                hasUvs ? uvs[indexC] : Vector2.zero,
                                hasVertexColors ? colors[indexA] : source.BaseColor,
                                hasVertexColors ? colors[indexB] : source.BaseColor,
                                hasVertexColors ? colors[indexC] : source.BaseColor,
                                hasUvs,
                                hasVertexColors,
                                source);
                            triangles.Add(triangle);

                            if (!hasBounds)
                            {
                                bounds = triangle.Bounds;
                                hasBounds = true;
                            }
                            else
                            {
                                bounds.Encapsulate(triangle.Bounds);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"Mesh voxelization failed while reading mesh data: {exception.Message}", this);
                triangles.Clear();
                return false;
            }

            return triangles.Count > 0 && hasBounds;
        }

        List<VoxelRecord> Voxelize(List<VoxelTriangle> triangles, Bounds bounds)
        {
            var voxelMap = new Dictionary<Vector3Int, VoxelRecord>();
            var expanded = bounds;
            expanded.Expand(activeVoxelSize);

            var surfaceDistance = activeVoxelSize * 0.62f;
            var surfaceDistanceSqr = surfaceDistance * surfaceDistance;
            var yzIntersections = new Dictionary<Vector2Int, List<XIntersection>>();

#if UNITY_EDITOR
            try
            {
#endif
                for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
                {
#if UNITY_EDITOR
                    if (triangleIndex % 200 == 0)
                        EditorUtility.DisplayProgressBar("Voxelizing Mesh", $"{triangleIndex} / {triangles.Count} triangles", (float)triangleIndex / triangles.Count);
#endif

                    var triangle = triangles[triangleIndex];
                    if (algorithm == MeshVoxelAlgorithm.SurfaceDistance)
                    {
                        VoxelizeTriangleByDistance(triangle, surfaceDistance, surfaceDistanceSqr, voxelMap, yzIntersections);
                    }
                    else
                    {
                        VoxelizeTriangleTriplane(triangle, voxelMap, yzIntersections);
                        if (algorithm == MeshVoxelAlgorithm.AccurateRaycast)
                            AddAccurateRayIntersections(triangle, voxelMap, yzIntersections);
                    }
                }

                if (fillSolid)
                    FillInterior(voxelMap, yzIntersections);
#if UNITY_EDITOR
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
#endif

            return new List<VoxelRecord>(voxelMap.Values);
        }

        float ResolveVoxelSize(Bounds bounds)
        {
            if (resolutionMode == MeshVoxelResolutionMode.VoxelSize)
                return Mathf.Max(0.001f, voxelSize);

            var longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            return Mathf.Max(0.001f, longestSide / Mathf.Max(1, targetResolution));
        }

        void FillInterior(Dictionary<Vector3Int, VoxelRecord> voxelMap, Dictionary<Vector2Int, List<XIntersection>> yzIntersections)
        {
            foreach (var pair in yzIntersections)
            {
                var intersections = pair.Value;
                if (intersections.Count < 2)
                    continue;

                intersections.Sort((a, b) => a.X.CompareTo(b.X));
                var unique = new List<XIntersection>(intersections.Count);
                var lastVoxelX = int.MinValue;
                for (var i = 0; i < intersections.Count; i++)
                {
                    var voxelX = Mathf.RoundToInt(intersections[i].X / activeVoxelSize);
                    if (voxelX == lastVoxelX)
                        continue;

                    lastVoxelX = voxelX;
                    unique.Add(intersections[i]);
                }

                for (var i = 0; i + 1 < unique.Count; i += 2)
                {
                    var left = unique[i];
                    var right = unique[i + 1];
                    var minX = Mathf.CeilToInt(Mathf.Min(left.X, right.X) / activeVoxelSize);
                    var maxX = Mathf.FloorToInt(Mathf.Max(left.X, right.X) / activeVoxelSize);
                    var color = Average(left.Color, right.Color);

                    for (var x = minX; x <= maxX; x++)
                        AddVoxel(voxelMap, new Vector3Int(x, pair.Key.x, pair.Key.y), color);
                }
            }
        }

        void VoxelizeTriangleByDistance(
            VoxelTriangle triangle,
            float surfaceDistance,
            float surfaceDistanceSqr,
            Dictionary<Vector3Int, VoxelRecord> voxelMap,
            Dictionary<Vector2Int, List<XIntersection>> yzIntersections)
        {
            var min = WorldToVoxelFloor(triangle.Bounds.min - Vector3.one * surfaceDistance);
            var max = WorldToVoxelCeil(triangle.Bounds.max + Vector3.one * surfaceDistance);

            for (var x = min.x; x <= max.x; x++)
            for (var y = min.y; y <= max.y; y++)
            for (var z = min.z; z <= max.z; z++)
            {
                var position = new Vector3Int(x, y, z);
                var center = VoxelToLocalCenter(position);
                if (PointTriangleDistanceSqr(center, triangle.A, triangle.B, triangle.C) > surfaceDistanceSqr)
                    continue;

                var barycentric = Barycentric(center, triangle.A, triangle.B, triangle.C);
                var color = triangle.SampleColor(barycentric, bakedShadingStrength);
                AddSurfaceVoxel(voxelMap, position, color);
                AddIntersection(yzIntersections, position.y, position.z, center.x, color);
            }
        }

        void VoxelizeTriangleTriplane(
            VoxelTriangle triangle,
            Dictionary<Vector3Int, VoxelRecord> voxelMap,
            Dictionary<Vector2Int, List<XIntersection>> yzIntersections)
        {
            RasterizeProjectedTriangle(triangle, 0, voxelMap, yzIntersections);
            RasterizeProjectedTriangle(triangle, 1, voxelMap, yzIntersections);
            RasterizeProjectedTriangle(triangle, 2, voxelMap, yzIntersections);
        }

        void RasterizeProjectedTriangle(
            VoxelTriangle triangle,
            int projectionAxis,
            Dictionary<Vector3Int, VoxelRecord> voxelMap,
            Dictionary<Vector2Int, List<XIntersection>> yzIntersections)
        {
            var axisU = (projectionAxis + 1) % 3;
            var axisV = (projectionAxis + 2) % 3;

            var a = ToVector3Array(triangle.A);
            var b = ToVector3Array(triangle.B);
            var c = ToVector3Array(triangle.C);
            var planeNormal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            var axisNormal = planeNormal[projectionAxis];
            if (Mathf.Abs(axisNormal) < 0.000001f)
                return;

            var minU = Mathf.FloorToInt(Mathf.Min(a[axisU], Mathf.Min(b[axisU], c[axisU])) / activeVoxelSize) - 1;
            var maxU = Mathf.CeilToInt(Mathf.Max(a[axisU], Mathf.Max(b[axisU], c[axisU])) / activeVoxelSize) + 1;
            var minV = Mathf.FloorToInt(Mathf.Min(a[axisV], Mathf.Min(b[axisV], c[axisV])) / activeVoxelSize) - 1;
            var maxV = Mathf.CeilToInt(Mathf.Max(a[axisV], Mathf.Max(b[axisV], c[axisV])) / activeVoxelSize) + 1;

            var a2 = new Vector2(a[axisU], a[axisV]);
            var b2 = new Vector2(b[axisU], b[axisV]);
            var c2 = new Vector2(c[axisU], c[axisV]);

            for (var u = minU; u <= maxU; u++)
            for (var v = minV; v <= maxV; v++)
            {
                for (var sample = 0; sample < RasterSamples.Length; sample++)
                {
                    var sample2 = new Vector2((u + RasterSamples[sample].x) * activeVoxelSize, (v + RasterSamples[sample].y) * activeVoxelSize);
                    if (!TryBarycentric2D(sample2, a2, b2, c2, out var barycentric))
                        continue;

                    var projected = new float[3];
                    projected[axisU] = sample2.x;
                    projected[axisV] = sample2.y;
                    projected[projectionAxis] =
                        (Vector3.Dot(planeNormal, triangle.A) -
                         planeNormal[axisU] * projected[axisU] -
                         planeNormal[axisV] * projected[axisV]) / axisNormal;

                    var position = new Vector3Int(
                        Mathf.RoundToInt(projected[0] / activeVoxelSize),
                        Mathf.RoundToInt(projected[1] / activeVoxelSize),
                        Mathf.RoundToInt(projected[2] / activeVoxelSize));
                    var color = triangle.SampleColor(barycentric, bakedShadingStrength);
                    AddSurfaceVoxel(voxelMap, position, color);
                    if (projectionAxis == 0)
                        AddIntersection(yzIntersections, position.y, position.z, projected[0], color);
                }
            }
        }

        void AddAccurateRayIntersections(
            VoxelTriangle triangle,
            Dictionary<Vector3Int, VoxelRecord> voxelMap,
            Dictionary<Vector2Int, List<XIntersection>> yzIntersections)
        {
            var planeNormal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            if (Mathf.Abs(planeNormal.x) < 0.000001f)
                return;

            var a2 = new Vector2(triangle.A.y, triangle.A.z);
            var b2 = new Vector2(triangle.B.y, triangle.B.z);
            var c2 = new Vector2(triangle.C.y, triangle.C.z);
            var minY = Mathf.FloorToInt(Mathf.Min(a2.x, Mathf.Min(b2.x, c2.x)) / activeVoxelSize) - 1;
            var maxY = Mathf.CeilToInt(Mathf.Max(a2.x, Mathf.Max(b2.x, c2.x)) / activeVoxelSize) + 1;
            var minZ = Mathf.FloorToInt(Mathf.Min(a2.y, Mathf.Min(b2.y, c2.y)) / activeVoxelSize) - 1;
            var maxZ = Mathf.CeilToInt(Mathf.Max(a2.y, Mathf.Max(b2.y, c2.y)) / activeVoxelSize) + 1;
            var planeDistance = Vector3.Dot(planeNormal, triangle.A);

            for (var y = minY; y <= maxY; y++)
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var sample = 0; sample < RasterSamples.Length; sample++)
                {
                    var sampleY = (y + RasterSamples[sample].x) * activeVoxelSize;
                    var sampleZ = (z + RasterSamples[sample].y) * activeVoxelSize;
                    if (!TryBarycentric2D(new Vector2(sampleY, sampleZ), a2, b2, c2, out var barycentric))
                        continue;

                    var x = (planeDistance - planeNormal.y * sampleY - planeNormal.z * sampleZ) / planeNormal.x;
                    var color = triangle.SampleColor(barycentric, bakedShadingStrength);
                    var position = new Vector3Int(Mathf.RoundToInt(x / activeVoxelSize), y, z);
                    AddSurfaceVoxel(voxelMap, position, color);
                    AddIntersection(yzIntersections, y, z, x, color);
                }
            }
        }

        void AddSurfaceVoxel(Dictionary<Vector3Int, VoxelRecord> voxelMap, Vector3Int position, Color32 color)
        {
            AddVoxel(voxelMap, position, color);

            if (surfaceThickness <= 1)
                return;

            var radius = surfaceThickness - 1;
            for (var x = -radius; x <= radius; x++)
            for (var y = -radius; y <= radius; y++)
            for (var z = -radius; z <= radius; z++)
            {
                var offset = new Vector3Int(x, y, z);
                if (offset == Vector3Int.zero || offset.sqrMagnitude > radius * radius)
                    continue;

                AddVoxel(voxelMap, position + offset, color);
            }
        }

        void AddVoxel(Dictionary<Vector3Int, VoxelRecord> voxelMap, Vector3Int position, Color32 color)
        {
            color = ApplyCubeVariation(color, position);
            if (voxelMap.TryGetValue(position, out var existing))
            {
                var blended = Average(existing.Color, color);
                voxelMap[position] = new VoxelRecord(position, blended, EstimateMass(blended), ClassifySurface(blended));
                return;
            }

            voxelMap.Add(position, new VoxelRecord(position, color, EstimateMass(color), ClassifySurface(color)));
        }

        void AddIntersection(Dictionary<Vector2Int, List<XIntersection>> yzIntersections, int y, int z, float x, Color32 color)
        {
            var key = new Vector2Int(y, z);
            if (!yzIntersections.TryGetValue(key, out var intersections))
            {
                intersections = new List<XIntersection>();
                yzIntersections.Add(key, intersections);
            }

            intersections.Add(new XIntersection(x, color));
        }

        Vector3Int WorldToVoxelFloor(Vector3 localPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(localPosition.x / activeVoxelSize),
                Mathf.FloorToInt(localPosition.y / activeVoxelSize),
                Mathf.FloorToInt(localPosition.z / activeVoxelSize));
        }

        Vector3Int WorldToVoxelCeil(Vector3 localPosition)
        {
            return new Vector3Int(
                Mathf.CeilToInt(localPosition.x / activeVoxelSize),
                Mathf.CeilToInt(localPosition.y / activeVoxelSize),
                Mathf.CeilToInt(localPosition.z / activeVoxelSize));
        }

        Vector3 VoxelToLocalCenter(Vector3Int position) => (Vector3)position * activeVoxelSize;

        void SetSourceRenderersVisible(bool visible)
        {
            var root = sourceRoot != null ? sourceRoot : transform;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (generatedRoot != null && renderers[i].transform.IsChildOf(generatedRoot.transform))
                    continue;

                if (renderers[i].GetComponentInParent<VoxelWorld>() != null)
                    continue;

                renderers[i].enabled = visible;
            }
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

        int CalculateFingerprint()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(voxelSize * 10000f);
                hash = hash * 31 + resolutionMode.GetHashCode();
                hash = hash * 31 + targetResolution;
                hash = hash * 31 + algorithm.GetHashCode();
                hash = hash * 31 + surfaceThickness;
                hash = hash * 31 + Mathf.RoundToInt(bakedShadingStrength * 1000f);
                hash = hash * 31 + Mathf.RoundToInt(cubeColorVariation * 1000f);
                hash = hash * 31 + fillSolid.GetHashCode();
                hash = hash * 31 + hideSourceRenderers.GetHashCode();
                hash = hash * 31 + damageProfile.GetHashCode();

                var filters = CollectMeshSources();
                for (var i = 0; i < filters.Count; i++)
                {
                    var filter = filters[i];
                    hash = hash * 31 + filter.Mesh.name.GetHashCode();
                    hash = hash * 31 + filter.Mesh.vertexCount;
                    hash = hash * 31 + filter.Mesh.triangles.Length;
                    hash = hash * 31 + filter.LocalToWorld.GetHashCode();
                }

                return hash;
            }
        }

        static MeshVoxelColorSource GetColorSource(Material[] materials, int submesh)
        {
            var material = materials.Length > 0 ? materials[Mathf.Clamp(submesh, 0, materials.Length - 1)] : null;
            if (material == null)
                return new MeshVoxelColorSource(new Color32(200, 200, 200, 255), null);

            var baseColor = new Color32(200, 200, 200, 255);

            if (material.HasProperty("_BaseColor"))
                baseColor = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                baseColor = material.GetColor("_Color");

            var texture = default(Texture2D);
            if (material.HasProperty("_BaseMap"))
                texture = material.GetTexture("_BaseMap") as Texture2D;

            if (texture == null && material.HasProperty("_MainTex"))
                texture = material.GetTexture("_MainTex") as Texture2D;

            if (texture == null && material.HasProperty("_BaseColorMap"))
                texture = material.GetTexture("_BaseColorMap") as Texture2D;

            if (texture != null)
                texture = MakeReadableCopy(texture);

            return new MeshVoxelColorSource(baseColor, texture);
        }

        static Color32 Average(Color32 a, Color32 b)
        {
            return new Color32(
                (byte)((a.r + b.r) / 2),
                (byte)((a.g + b.g) / 2),
                (byte)((a.b + b.b) / 2),
                255);
        }

        static Color32 Multiply(Color32 a, Color32 b)
        {
            return new Color32(
                (byte)(a.r * b.r / 255),
                (byte)(a.g * b.g / 255),
                (byte)(a.b * b.b / 255),
                255);
        }

        Color32 ApplyCubeVariation(Color32 color, Vector3Int position)
        {
            if (cubeColorVariation <= 0f)
                return color;

            var noise = Hash01(position);
            var factor = 1f + (noise - 0.5f) * cubeColorVariation;
            return Scale(color, factor);
        }

        static Color32 Scale(Color32 color, float factor)
        {
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

        static Texture2D MakeReadableCopy(Texture2D source)
        {
            if (source == null)
                return null;

            try
            {
                source.GetPixelBilinear(0.5f, 0.5f);
                return source;
            }
            catch (UnityException)
            {
                var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    copy.name = $"{source.name}_ReadableVoxelCopy";
                    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    copy.Apply(false, false);
                    return copy;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        static Shader FindDefaultShader()
        {
            return Shader.Find("Destruxion/Voxel Vertex Color") ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Sprites/Default") ??
                   Shader.Find("Unlit/Color");
        }

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

        static float PointTriangleDistanceSqr(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = point - a;
            var d1 = Vector3.Dot(ab, ap);
            var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return (point - a).sqrMagnitude;

            var bp = point - b;
            var d3 = Vector3.Dot(ab, bp);
            var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
                return (point - b).sqrMagnitude;

            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                var v = d1 / (d1 - d3);
                return (point - (a + v * ab)).sqrMagnitude;
            }

            var cp = point - c;
            var d5 = Vector3.Dot(ab, cp);
            var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
                return (point - c).sqrMagnitude;

            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                var w = d2 / (d2 - d6);
                return (point - (a + w * ac)).sqrMagnitude;
            }

            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                var w = (d4 - d3) / (d4 - d3 + d5 - d6);
                return (point - (b + w * (c - b))).sqrMagnitude;
            }

            var normal = Vector3.Cross(ab, ac).normalized;
            var distance = Vector3.Dot(point - a, normal);
            return distance * distance;
        }

        static Vector3 Barycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var v0 = b - a;
            var v1 = c - a;
            var v2 = point - a;
            var d00 = Vector3.Dot(v0, v0);
            var d01 = Vector3.Dot(v0, v1);
            var d11 = Vector3.Dot(v1, v1);
            var d20 = Vector3.Dot(v2, v0);
            var d21 = Vector3.Dot(v2, v1);
            var denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 0.000001f)
                return new Vector3(1f, 0f, 0f);

            var v = (d11 * d20 - d01 * d21) / denominator;
            var w = (d00 * d21 - d01 * d20) / denominator;
            var u = 1f - v - w;
            return new Vector3(u, v, w);
        }

        static bool TryBarycentric2D(Vector2 point, Vector2 a, Vector2 b, Vector2 c, out Vector3 barycentric)
        {
            barycentric = default;
            var denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < 0.000001f)
                return false;

            var u = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
            var v = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
            var w = 1f - u - v;
            if (u < -0.001f || v < -0.001f || w < -0.001f)
                return false;

            barycentric = new Vector3(u, v, w);
            return true;
        }

        static float[] ToVector3Array(Vector3 value)
        {
            return new[] {value.x, value.y, value.z};
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        static readonly Vector2[] RasterSamples =
        {
            new(0.5f, 0.5f),
            new(0.25f, 0.25f),
            new(0.75f, 0.25f),
            new(0.25f, 0.75f),
            new(0.75f, 0.75f)
        };

        readonly struct MeshVoxelColorSource
        {
            public readonly Color32 BaseColor;
            readonly Texture2D texture;

            public MeshVoxelColorSource(Color32 baseColor, Texture2D texture)
            {
                BaseColor = baseColor;
                this.texture = texture;
            }

            public Color32 Sample(Vector2 uv, Color32 tint)
            {
                if (texture == null)
                    return Multiply(BaseColor, tint);

                try
                {
                    return Multiply(Multiply(BaseColor, texture.GetPixelBilinear(Mathf.Repeat(uv.x, 1f), Mathf.Repeat(uv.y, 1f))), tint);
                }
                catch (UnityException)
                {
                    return Multiply(BaseColor, tint);
                }
            }
        }

        readonly struct MeshVoxelSource
        {
            public readonly Mesh Mesh;
            public readonly Renderer Renderer;
            public readonly Matrix4x4 LocalToWorld;
            public readonly string Name;

            public MeshVoxelSource(Mesh mesh, Renderer renderer, Matrix4x4 localToWorld, string name)
            {
                Mesh = mesh;
                Renderer = renderer;
                LocalToWorld = localToWorld;
                Name = name;
            }
        }

        readonly struct VoxelTriangle
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            readonly Vector3 normal;
            readonly Vector2 uvA;
            readonly Vector2 uvB;
            readonly Vector2 uvC;
            readonly Color32 colorA;
            readonly Color32 colorB;
            readonly Color32 colorC;
            readonly bool hasUvs;
            readonly bool hasVertexColors;
            readonly MeshVoxelColorSource colorSource;
            public readonly Bounds Bounds;

            public VoxelTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector2 uvA,
                Vector2 uvB,
                Vector2 uvC,
                Color32 colorA,
                Color32 colorB,
                Color32 colorC,
                bool hasUvs,
                bool hasVertexColors,
                MeshVoxelColorSource colorSource)
            {
                A = a;
                B = b;
                C = c;
                normal = Vector3.Cross(b - a, c - a).normalized;
                this.uvA = uvA;
                this.uvB = uvB;
                this.uvC = uvC;
                this.colorA = colorA;
                this.colorB = colorB;
                this.colorC = colorC;
                this.hasUvs = hasUvs;
                this.hasVertexColors = hasVertexColors;
                this.colorSource = colorSource;
                Bounds = new Bounds(a, Vector3.zero);
                Bounds.Encapsulate(b);
                Bounds.Encapsulate(c);
            }

            public Color32 SampleColor(Vector3 barycentric, float bakedShadingStrength)
            {
                var vertexColor = Color.white;
                if (hasVertexColors)
                {
                    vertexColor =
                        (Color)colorA * barycentric.x +
                        (Color)colorB * barycentric.y +
                        (Color)colorC * barycentric.z;
                }

                if (hasUvs)
                {
                    var uv = uvA * barycentric.x + uvB * barycentric.y + uvC * barycentric.z;
                    return ApplyBakedShade(colorSource.Sample(uv, vertexColor), normal, bakedShadingStrength);
                }

                if (!hasVertexColors)
                    return ApplyBakedShade(colorSource.BaseColor, normal, bakedShadingStrength);

                return ApplyBakedShade(Multiply(colorSource.BaseColor, vertexColor), normal, bakedShadingStrength);
            }

            static Color32 ApplyBakedShade(Color32 color, Vector3 normal, float strength)
            {
                if (strength <= 0f)
                    return color;

                var lightDirection = new Vector3(-0.35f, 0.8f, -0.45f).normalized;
                var light = Mathf.Clamp01(Vector3.Dot(normal.normalized, lightDirection)) * 0.65f + 0.35f;
                var factor = Mathf.Lerp(1f, light, strength);
                return Scale(color, factor);
            }
        }

        readonly struct XIntersection
        {
            public readonly float X;
            public readonly Color32 Color;

            public XIntersection(float x, Color32 color)
            {
                X = x;
                Color = color;
            }
        }
    }

    public enum MeshVoxelResolutionMode
    {
        TargetMaxDimension,
        VoxelSize
    }

    public enum MeshVoxelAlgorithm
    {
        AccurateRaycast,
        TriplaneRasterizer,
        SurfaceDistance
    }
}
