using UnityEngine;

public class FirstPersonShoot : MonoBehaviour
{
    [SerializeField] Camera sourceCamera;
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] float projectileSpeed = 45f;
    [SerializeField] float projectileRadius = 0.06f;
    [SerializeField] float projectileMass = 0.08f;
    [SerializeField] float impactImpulse = 5f;
    [SerializeField] float projectileLifetime = 3f;
    [SerializeField] float spawnForwardOffset = 0.35f;

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
        var spawnPosition = sourceCamera.transform.position + direction * spawnForwardOffset;
        var projectile = CreateProjectile(spawnPosition, Quaternion.LookRotation(direction));

        if (projectile.TryGetComponent<Rigidbody>(out var body))
            body.linearVelocity = direction * projectileSpeed;

        if (projectile.TryGetComponent<VoxelProjectile>(out var voxelProjectile))
            voxelProjectile.Initialize(impactImpulse, projectileLifetime, projectileRadius);

        IgnoreOwner(projectile);
    }

    GameObject CreateProjectile(Vector3 position, Quaternion rotation)
    {
        if (projectilePrefab != null)
            return Instantiate(projectilePrefab, position, rotation);

        var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = "Voxel Projectile";
        projectile.transform.SetPositionAndRotation(position, rotation);
        projectile.transform.localScale = Vector3.one * projectileRadius * 2f;

        var body = projectile.AddComponent<Rigidbody>();
        body.mass = projectileMass;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        projectile.AddComponent<VoxelProjectile>();
        return projectile;
    }

    void IgnoreOwner(GameObject projectile)
    {
        if (ownerColliders == null || projectile == null || !projectile.TryGetComponent<Collider>(out var projectileCollider))
            return;

        for (var i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] != null)
                Physics.IgnoreCollision(projectileCollider, ownerColliders[i]);
        }
    }
}
