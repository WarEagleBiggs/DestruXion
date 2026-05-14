using Destruxion.Voxels;
using System.Collections;
using UnityEngine;

public class FirstPersonShoot : MonoBehaviour
{
    [SerializeField] Camera sourceCamera;
    [SerializeField] Transform muzzleTransform;
    [SerializeField] float projectileRadius = 0.06f;
    [SerializeField] float impactImpulse = 5f;
    [SerializeField] float projectileMaxDistance = 80f;
    [SerializeField] float spawnForwardOffset = 0.35f;
    [SerializeField] LayerMask hitMask = ~0;
    [Header("Visuals")]
    [SerializeField] bool showBulletTracer = true;
    [SerializeField] Material tracerMaterial;
    [SerializeField] Color tracerColor = new(1f, 0.72f, 0.22f, 1f);
    [SerializeField, Min(0.001f)] float tracerWidth = 0.018f;
    [SerializeField, Min(0.01f)] float tracerDuration = 0.045f;

    readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    Collider[] ownerColliders;
    Material runtimeTracerMaterial;

    void Reset()
    {
        sourceCamera = GetComponentInChildren<Camera>();
    }

    void Awake()
    {
        if (sourceCamera == null)
            sourceCamera = GetComponentInChildren<Camera>();

        if (muzzleTransform == null)
            muzzleTransform = FindMuzzleTransform();

        ownerColliders = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        if (!MiniFirstPersonInput.FirePressed || sourceCamera == null)
            return;

        Fire();
    }

    void Fire()
    {
        var direction = sourceCamera.transform.forward;
        var origin = sourceCamera.transform.position + direction * spawnForwardOffset;
        var visualOrigin = muzzleTransform != null ? muzzleTransform.position : origin;
        var visualEnd = origin + direction * projectileMaxDistance;
        var didHit = FireHitscan(origin, direction, out var hit);

        if (didHit)
            visualEnd = hit.Point;

        SpawnTracer(visualOrigin, visualEnd);
    }

    bool FireHitscan(Vector3 origin, Vector3 direction, out ShotHit hit)
    {
        if (!TryFindFirstHit(origin, direction, out hit))
            return false;

        if (hit.World != null)
        {
            hit.World.ActivateVoxelsAround(hit.Point, direction * impactImpulse, projectileRadius);
            return true;
        }

        if (hit.Debris != null)
        {
            hit.Debris.BreakApart(direction * impactImpulse);
            return true;
        }

        return true;
    }

    bool TryFindFirstHit(Vector3 origin, Vector3 direction, out ShotHit closestHit)
    {
        closestHit = default;
        var hitCount = Physics.SphereCastNonAlloc(
            origin,
            projectileRadius,
            direction,
            hitBuffer,
            projectileMaxDistance,
            hitMask,
            QueryTriggerInteraction.Ignore);

        var closestDistance = float.PositiveInfinity;
        var found = false;
        for (var i = 0; i < hitCount; i++)
        {
            var candidate = hitBuffer[i];
            if (candidate.collider == null || IsOwnerCollider(candidate.collider))
                continue;

            if (candidate.distance >= closestDistance)
                continue;

            closestDistance = candidate.distance;
            closestHit = ShotHit.FromCollider(candidate);
            found = true;
        }

        if (TryFindVoxelDataHit(origin, direction, found ? closestDistance : projectileMaxDistance, out var voxelWorld, out var voxelPoint, out var voxelNormal, out var voxelDistance))
        {
            closestHit = ShotHit.FromVoxelWorld(voxelWorld, voxelPoint, voxelNormal, voxelDistance);
            return true;
        }

        return found;
    }

    bool TryFindVoxelDataHit(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out VoxelWorld hitWorld,
        out Vector3 hitPoint,
        out Vector3 hitNormal,
        out float hitDistance)
    {
        hitWorld = null;
        hitPoint = origin;
        hitNormal = -direction;
        hitDistance = float.PositiveInfinity;

        var worlds = VoxelWorld.ActiveWorlds;
        for (var i = 0; i < worlds.Count; i++)
        {
            var world = worlds[i];
            if (world == null)
                continue;

            var end = origin + direction * maxDistance;
            if (!world.TryFindVoxelImpact(origin, end, projectileRadius, out var candidatePoint, out var candidateNormal))
                continue;

            var distance = Vector3.Distance(origin, candidatePoint);
            if (distance >= hitDistance)
                continue;

            hitWorld = world;
            hitPoint = candidatePoint;
            hitNormal = candidateNormal;
            hitDistance = distance;
        }

        return hitWorld != null;
    }

    bool IsOwnerCollider(Collider candidate)
    {
        if (ownerColliders == null)
            return false;

        for (var i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] == candidate)
                return true;
        }

        return false;
    }

    void SpawnTracer(Vector3 from, Vector3 to)
    {
        if (!showBulletTracer || (to - from).sqrMagnitude < 0.0001f)
            return;

        var tracerObject = new GameObject("Bullet Tracer");
        var line = tracerObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.useWorldSpace = true;
        line.startWidth = tracerWidth;
        line.endWidth = tracerWidth * 0.35f;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = GetTracerMaterial();
        line.startColor = tracerColor;
        line.endColor = new Color(tracerColor.r, tracerColor.g, tracerColor.b, 0f);

        StartCoroutine(DestroyTracerAfter(line, tracerDuration));
    }

    IEnumerator DestroyTracerAfter(LineRenderer line, float duration)
    {
        var elapsed = 0f;
        var start = tracerColor;
        while (elapsed < duration && line != null)
        {
            elapsed += Time.deltaTime;
            var alpha = Mathf.Clamp01(1f - elapsed / duration);
            line.startColor = new Color(start.r, start.g, start.b, start.a * alpha);
            line.endColor = new Color(start.r, start.g, start.b, 0f);
            yield return null;
        }

        if (line != null)
            Destroy(line.gameObject);
    }

    Material GetTracerMaterial()
    {
        if (tracerMaterial != null)
            return tracerMaterial;

        if (runtimeTracerMaterial != null)
            return runtimeTracerMaterial;

        runtimeTracerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"))
        {
            name = "Runtime Bullet Tracer"
        };

        if (runtimeTracerMaterial.HasProperty("_BaseColor"))
            runtimeTracerMaterial.SetColor("_BaseColor", tracerColor);
        if (runtimeTracerMaterial.HasProperty("_Color"))
            runtimeTracerMaterial.SetColor("_Color", tracerColor);

        return runtimeTracerMaterial;
    }

    Transform FindMuzzleTransform()
    {
        if (sourceCamera == null)
            return null;

        var cameraTransform = sourceCamera.transform;
        var result = FindNamedChild(cameraTransform, "muzzle");
        if (result != null)
            return result;

        result = FindNamedChild(cameraTransform, "barrel");
        if (result != null)
            return result;

        return FindNamedChild(cameraTransform, "tip");
    }

    static Transform FindNamedChild(Transform root, string namePart)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name.ToLowerInvariant().Contains(namePart))
                return child;

            var nested = FindNamedChild(child, namePart);
            if (nested != null)
                return nested;
        }

        return null;
    }

    readonly struct ShotHit
    {
        public readonly VoxelWorld World;
        public readonly VoxelDebrisChunk Debris;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Distance;

        ShotHit(VoxelWorld world, VoxelDebrisChunk debris, Vector3 point, Vector3 normal, float distance)
        {
            World = world;
            Debris = debris;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public static ShotHit FromCollider(RaycastHit hit)
        {
            return new ShotHit(
                hit.collider.GetComponentInParent<VoxelWorld>(),
                hit.collider.GetComponentInParent<VoxelDebrisChunk>(),
                hit.point,
                hit.normal,
                hit.distance);
        }

        public static ShotHit FromVoxelWorld(VoxelWorld world, Vector3 point, Vector3 normal, float distance)
        {
            return new ShotHit(world, null, point, normal, distance);
        }
    }
}
