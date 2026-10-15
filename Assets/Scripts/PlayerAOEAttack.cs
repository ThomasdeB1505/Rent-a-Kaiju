using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DiabolicalGames;

/// <summary>
/// Press a key to play an area attack (stomp, roar, tail spin...) that breaks
/// everything within a radius around the kaiju and throws the debris outward.
/// Put this on the same GameObject as FirstPersonMovement.
/// </summary>
public class PlayerAOEAttack : MonoBehaviour
{
    [Header("Input")]
    public KeyCode attackKey = KeyCode.Mouse1;
    [Tooltip("Seconds before you can use the AOE attack again (measured from the start of the attack).")]
    public float cooldown = 3f;

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Name of the Trigger parameter in your Animator Controller. Must be a Trigger, not a Bool.")]
    public string attackTrigger = "AOEAttack";

    [Header("Hit Timing")]
    [Tooltip("If true, the hit happens when your animation calls the AOEHit() event. " +
             "If false, it happens automatically after Hit Delay.")]
    public bool useAnimationEvent = false;
    [Tooltip("Seconds after pressing the key until the hit lands, e.g. when the foot hits the ground.")]
    public float hitDelay = 0.5f;

    [Header("Hit Area")]
    [Tooltip("Radius around the kaiju that gets hit.")]
    public float radius = 8f;
    [Tooltip("Height of the sphere's center above the kaiju's pivot.")]
    public float height = 1f;
    [Tooltip("Layers the attack can hit. Exclude the Player layer.")]
    public LayerMask hitLayers = ~0;

    [Header("Force")]
    [Tooltip("Force passed to DestructibleObjects at the center. Set it higher than your buildings' Force Required.")]
    public float attackForce = 1000f;
    [Tooltip("If on, force gets weaker further from the kaiju, so only nearby or weaker buildings break at the edge.")]
    public bool forceFalloff = false;
    [Tooltip("Fraction of the force left at the edge of the radius when Force Falloff is on.")]
    [Range(0f, 1f)] public float edgeForceMultiplier = 0.3f;
    [Tooltip("Impulse applied to loose rigidbodies (debris etc.), pushing them away from the kaiju.")]
    public float rigidbodyImpulse = 20f;
    [Tooltip("How much the pushed rigidbodies are thrown upward as well as outward.")]
    public float upwardModifier = 1f;

    [Header("Movement")]
    [Tooltip("Stop the kaiju moving while the attack plays.")]
    public bool lockMovement = true;
    [Tooltip("How long movement stays locked. Roughly the length of the animation.")]
    public float lockDuration = 1.2f;

    [Header("Effects (optional)")]
    [Tooltip("Spawned at the kaiju's feet when the hit lands, e.g. a dust ring or shockwave particle system.")]
    public GameObject impactEffectPrefab;
    [Tooltip("Seconds before the spawned effect is destroyed. 0 = never.")]
    public float effectLifetime = 3f;

    [Header("Sound (optional)")]
    public AudioSource audioSource;
    public AudioClip windupSound;
    public AudioClip impactSound;

    FirstPersonMovement movement;
    float nextAttackTime;

    void Awake()
    {
        movement = GetComponent<FirstPersonMovement>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public void StartAttack()   // was: void StartAttack()
    {
        nextAttackTime = Time.time + cooldown;

        if (animator != null && !string.IsNullOrEmpty(attackTrigger))
            animator.SetTrigger(attackTrigger);

        if (audioSource != null && windupSound != null)
            audioSource.PlayOneShot(windupSound);

        if (lockMovement && movement != null)
            movement.LockMovement(lockDuration);

        if (!useAnimationEvent)
            StartCoroutine(DelayedHit());
    }

    IEnumerator DelayedHit()
    {
        yield return new WaitForSeconds(hitDelay);
        AOEHit();
    }

    /// <summary>
    /// Deals the damage. Called automatically after hitDelay, or from an
    /// Animation Event if useAnimationEvent is on.
    /// </summary>
    public void AOEHit()
    {
        Vector3 center = GetHitCenter();

        Collider[] hits = Physics.OverlapSphere(center, radius, hitLayers, QueryTriggerInteraction.Ignore);

        // A building usually has many colliders; only hit each one once.
        var hitDestructibles = new HashSet<DestructibleObject>();
        var hitBodies = new HashSet<Rigidbody>();

        foreach (Collider col in hits)
        {
            if (col.transform.IsChildOf(transform)) continue; // don't hit ourselves

            Vector3 point = col.ClosestPoint(center);

            DestructibleObject destructible = col.GetComponentInParent<DestructibleObject>();
            if (destructible != null && hitDestructibles.Add(destructible))
            {
                float distance = Vector3.Distance(center, point);
                destructible.ApplyForce(GetForceAtDistance(distance), point, GetOutwardDirection(center, point));
            }

            Rigidbody body = col.attachedRigidbody;
            if (body != null && !body.isKinematic && hitBodies.Add(body))
            {
                body.AddExplosionForce(rigidbodyImpulse, center, radius, upwardModifier, ForceMode.Impulse);
            }
        }

        if (impactEffectPrefab != null)
        {
            GameObject effect = Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
            if (effectLifetime > 0f) Destroy(effect, effectLifetime);
        }

        if (audioSource != null && impactSound != null)
            audioSource.PlayOneShot(impactSound);
    }

    float GetForceAtDistance(float distance)
    {
        if (!forceFalloff) return attackForce;
        float t = Mathf.Clamp01(distance / radius);
        return attackForce * Mathf.Lerp(1f, edgeForceMultiplier, t);
    }

    Vector3 GetOutwardDirection(Vector3 center, Vector3 point)
    {
        // Horizontal direction away from the kaiju, so debris flies outward.
        Vector3 dir = point - center;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return transform.forward;
        return dir.normalized;
    }

    Vector3 GetHitCenter()
    {
        return transform.position + Vector3.up * height;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(GetHitCenter(), radius);
    }
}
