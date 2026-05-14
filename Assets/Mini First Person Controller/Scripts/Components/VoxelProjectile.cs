using Destruxion.Voxels;
using UnityEngine;

public class VoxelProjectile : MonoBehaviour
{
    float impactImpulse = 5f;
    float radius = 0.06f;
    float bounceSpeedMultiplier = 0.08f;
    float maxDistance = 80f;
    float hiddenDistance = 1.1f;
    bool hasHit;
    bool renderersVisible = true;
    Rigidbody body;
    Vector3 previousPosition;
    Vector3 spawnPosition;
    Vector3 direction = Vector3.forward;
    float speed = 45f;
    float traveledDistance;
    bool initialized;
    Renderer[] renderers;

    public void Initialize(float impulse, float lifetime, float projectileRadius, float projectileMaxDistance, float hiddenNearPlayerDistance)
    {
        Initialize(impulse, lifetime, projectileRadius, projectileMaxDistance, hiddenNearPlayerDistance, transform.position, transform.forward, speed);
    }

    public void Initialize(
        float impulse,
        float lifetime,
        float projectileRadius,
        float projectileMaxDistance,
        float hiddenNearPlayerDistance,
        Vector3 startPosition,
        Vector3 shotDirection,
        float projectileSpeed)
    {
        impactImpulse = impulse;
        radius = projectileRadius;
        maxDistance = Mathf.Max(projectileRadius, projectileMaxDistance);
        hiddenDistance = Mathf.Max(0f, hiddenNearPlayerDistance);
        spawnPosition = startPosition;
        previousPosition = startPosition;
        direction = shotDirection.sqrMagnitude > 0.001f ? shotDirection.normalized : transform.forward;
        speed = Mathf.Max(0.01f, projectileSpeed);
        traveledDistance = 0f;
        initialized = true;
        transform.SetPositionAndRotation(startPosition, Quaternion.LookRotation(direction));

        if (body == null)
            body = GetComponent<Rigidbody>();

        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.position = startPosition;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        UpdateVisibility();
        Destroy(gameObject, lifetime);
    }

    public void Initialize(float impulse, float lifetime, float projectileRadius)
    {
        Initialize(impulse, lifetime, projectileRadius, maxDistance, hiddenDistance);
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    void Start()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        if (!initialized)
        {
            spawnPosition = transform.position;
            previousPosition = transform.position;
            direction = transform.forward;
            initialized = true;
        }

        UpdateVisibility();
    }

    void FixedUpdate()
    {
        if (hasHit)
            return;

        if (!initialized)
            return;

        var stepDistance = speed * Time.fixedDeltaTime;
        var currentPosition = previousPosition;
        var targetPosition = currentPosition + direction * stepDistance;

        if (traveledDistance >= maxDistance)
        {
            Destroy(gameObject);
            return;
        }

        UpdateVisibility();

        var travel = targetPosition - currentPosition;
        var distance = travel.magnitude;
        var shotDirection = distance > 0.001f ? travel / distance : direction;

        if (distance > 0.001f &&
            Physics.SphereCast(currentPosition, radius, shotDirection, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
                HitCollider(hit.collider, hit.point, hit.normal, shotDirection);
        }

        if (!hasHit && TryFindVoxelDataImpact(currentPosition, targetPosition, shotDirection, out var world, out var hitPoint, out var hitNormal))
        {
            Hit(world, hitPoint, hitNormal, shotDirection);
        }

        if (hasHit)
            return;

        traveledDistance += distance;
        previousPosition = targetPosition;
        if (body != null)
            body.MovePosition(targetPosition);
        else
            transform.position = targetPosition;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasHit)
            return;

        var contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        var fallbackDirection = body != null && body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized
            : direction;

        var world = collision.collider.GetComponentInParent<VoxelWorld>();
        if (world != null)
        {
            Hit(world, collision.contactCount > 0 ? contact.point : transform.position, collision.contactCount > 0 ? contact.normal : -fallbackDirection, fallbackDirection);
            return;
        }

        var debris = collision.collider.GetComponentInParent<VoxelDebrisChunk>();
        if (debris != null)
        {
            HitDebris(debris, collision.contactCount > 0 ? contact.point : transform.position, collision.contactCount > 0 ? contact.normal : -fallbackDirection, fallbackDirection);
            return;
        }

        DestroyOnCollision();
    }

    void HitCollider(Collider hitCollider, Vector3 hitPoint, Vector3 hitNormal, Vector3 direction)
    {
        var world = hitCollider.GetComponentInParent<VoxelWorld>();
        if (world != null)
        {
            Hit(world, hitPoint, hitNormal, direction);
            return;
        }

        var debris = hitCollider.GetComponentInParent<VoxelDebrisChunk>();
        if (debris != null)
        {
            HitDebris(debris, hitPoint, hitNormal, direction);
            return;
        }

        DestroyOnCollision();
    }

    bool TryFindVoxelDataImpact(Vector3 from, Vector3 to, Vector3 direction, out VoxelWorld hitWorld, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var worlds = VoxelWorld.ActiveWorlds;
        for (var i = 0; i < worlds.Count; i++)
        {
            if (worlds[i] != null && worlds[i].TryFindVoxelImpact(from, to, radius, out hitPoint, out hitNormal))
            {
                hitWorld = worlds[i];
                return true;
            }
        }

        hitWorld = null;
        hitPoint = to;
        hitNormal = -direction;
        return false;
    }

    void Hit(VoxelWorld world, Vector3 hitPoint, Vector3 hitNormal, Vector3 direction)
    {
        if (hasHit)
            return;

        if (world != null)
            world.ActivateVoxelsAround(hitPoint, direction * impactImpulse, radius);

        Bounce(hitPoint, hitNormal);
    }

    void HitDebris(VoxelDebrisChunk debris, Vector3 hitPoint, Vector3 hitNormal, Vector3 direction)
    {
        if (hasHit)
            return;

        if (debris != null)
            debris.BreakApart(direction * impactImpulse);

        Bounce(hitPoint, hitNormal);
    }

    void Bounce(Vector3 hitPoint, Vector3 hitNormal)
    {
        hasHit = true;

        if (body != null)
        {
            var bounceDirection = Vector3.Reflect(direction, hitNormal.normalized);
            body.position = hitPoint + hitNormal.normalized * (radius + 0.02f);
            body.isKinematic = false;
            body.linearVelocity = bounceDirection * speed * bounceSpeedMultiplier;
        }

        if (TryGetComponent<Collider>(out var projectileCollider))
            projectileCollider.enabled = false;

        Destroy(gameObject, 0.18f);
    }

    void DestroyOnCollision()
    {
        hasHit = true;
        Destroy(gameObject);
    }

    void UpdateVisibility()
    {
        var shouldBeVisible = (transform.position - spawnPosition).sqrMagnitude >= hiddenDistance * hiddenDistance;
        if (shouldBeVisible == renderersVisible)
            return;

        renderersVisible = shouldBeVisible;
        if (renderers == null)
            return;

        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = shouldBeVisible;
        }
    }
}
