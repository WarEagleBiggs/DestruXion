using Destruxion.Voxels;
using UnityEngine;

public class FirstPersonShoot : MonoBehaviour
{
    [SerializeField] Camera sourceCamera;
    [SerializeField] float projectileRadius = 0.06f;
    [SerializeField] float impactImpulse = 5f;
    [SerializeField] float projectileMaxDistance = 80f;
    [SerializeField] float spawnForwardOffset = 0.35f;
    [SerializeField] LayerMask hitMask = ~0;

    readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    Collider[] ownerColliders;

    void Reset()
    {
        sourceCamera = GetComponentInChildren<Camera>();
    }

    void Awake()
    {
        if (sourceCamera == null)
            sourceCamera = GetComponentInChildren<Camera>();

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
        FireHitscan(origin, direction);
    }

    void FireHitscan(Vector3 origin, Vector3 direction)
    {
        if (!TryFindFirstHit(origin, direction, out var hit))
            return;

        if (hit.World != null)
        {
            hit.World.ActivateVoxelsAround(hit.Point, direction * impactImpulse, projectileRadius);
            return;
        }

        if (hit.Debris != null)
        {
            hit.Debris.BreakApart(direction * impactImpulse);
            return;
        }
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
