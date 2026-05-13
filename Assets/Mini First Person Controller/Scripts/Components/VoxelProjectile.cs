using Destruxion.Voxels;
using UnityEngine;

public class VoxelProjectile : MonoBehaviour
{
    float impactImpulse = 5f;
    float radius = 0.06f;
    float bounceSpeedMultiplier = 0.08f;
    bool hasHit;
    Rigidbody body;
    Vector3 previousPosition;

    public void Initialize(float impulse, float lifetime, float projectileRadius)
    {
        impactImpulse = impulse;
        radius = projectileRadius;
        Destroy(gameObject, lifetime);
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void Start()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        previousPosition = transform.position;
    }

    void FixedUpdate()
    {
        if (hasHit)
            return;

        var currentPosition = transform.position;
        var travel = currentPosition - previousPosition;
        var distance = travel.magnitude;
        var direction = distance > 0.001f ? travel / distance : transform.forward;

        if (distance > 0.001f &&
            Physics.SphereCast(previousPosition, radius, travel / distance, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<VoxelWorld>() != null)
                Hit(hit.collider.GetComponentInParent<VoxelWorld>(), hit.point, hit.normal, direction);
            else if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<VoxelDebrisChunk>() != null)
                HitDebris(hit.collider.GetComponentInParent<VoxelDebrisChunk>(), hit.point, hit.normal, direction);
        }

        if (!hasHit && TryFindVoxelDataImpact(previousPosition, currentPosition, direction, out var world, out var hitPoint, out var hitNormal))
        {
            Hit(world, hitPoint, hitNormal, direction);
        }

        previousPosition = currentPosition;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasHit)
            return;

        var contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        var fallbackDirection = body != null && body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized
            : transform.forward;

        var world = collision.collider.GetComponentInParent<VoxelWorld>();
        if (world != null)
            Hit(world, collision.contactCount > 0 ? contact.point : transform.position, collision.contactCount > 0 ? contact.normal : -fallbackDirection, fallbackDirection);

        var debris = collision.collider.GetComponentInParent<VoxelDebrisChunk>();
        if (debris != null)
            HitDebris(debris, collision.contactCount > 0 ? contact.point : transform.position, collision.contactCount > 0 ? contact.normal : -fallbackDirection, fallbackDirection);
    }

    bool TryFindVoxelDataImpact(Vector3 from, Vector3 to, Vector3 direction, out VoxelWorld hitWorld, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var worlds = FindObjectsByType<VoxelWorld>();
        for (var i = 0; i < worlds.Length; i++)
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
            world.ActivateVoxelsAround(hitPoint, direction * impactImpulse);

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
            var speed = body.linearVelocity.magnitude;
            var bounceDirection = Vector3.Reflect(body.linearVelocity.sqrMagnitude > 0.001f ? body.linearVelocity.normalized : transform.forward, hitNormal.normalized);
            body.position = hitPoint + hitNormal.normalized * (radius + 0.02f);
            body.linearVelocity = bounceDirection * speed * bounceSpeedMultiplier;
        }

        if (TryGetComponent<Collider>(out var projectileCollider))
            projectileCollider.enabled = false;

        Destroy(gameObject, 0.35f);
    }
}
