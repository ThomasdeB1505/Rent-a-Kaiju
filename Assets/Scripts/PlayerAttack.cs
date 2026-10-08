using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DiabolicalGames;

/// <summary>
/// Press a key to play an attack animation and break everything
/// breakable inside a sphere in front of the player.
/// Put this on the same GameObject as FirstPersonMovement.
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    [Header("Input")]
    public KeyCode attackKey = KeyCode.Mouse0;
    [Tooltip("Seconds before you can attack again (measured from the start of the attack).")]
    public float cooldown = 1f;

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Name of the Trigger parameter in your Animator Controller.")]
    public string attackTrigger = "Attack";

    [Header("Hit Timing")]
    [Tooltip("If true, the hit happens when your animation calls the AttackHit() event. " +
             "If false, it happens automatically after Hit Delay.")]
    public bool useAnimationEvent = false;
    [Tooltip("Seconds after pressing the key until the hit lands (when not using an animation event).")]
    public float hitDelay = 0.3f;

    [Header("Hit Area")]
    [Tooltip("How far in front of the player the center of the hit sphere is.")]
    public float range = 1.5f;
    [Tooltip("Radius of the hit sphere.")]
    public float radius = 1.2f;
    [Tooltip("Height of the hit sphere above the player's pivot.")]
    public float height = 1f;
    [Tooltip("Layers the attack can hit. Exclude the Player layer.")]
    public LayerMask hitLayers = ~0;

    [Header("Force")]
    [Tooltip("Force passed to DestructibleObjects. Set it higher than your buildings' Force Required.")]
    public float attackForce = 5000f;
    [Tooltip("Impulse applied to loose rigidbodies (debris etc.) caught in the attack.")]
    public float rigidbodyImpulse = 10f;

    [Header("Movement")]
    [Tooltip("Stop the player moving while the attack plays.")]
    public bool lockMovement = true;
    [Tooltip("How long movement stays locked.")]
    public float lockDuration = 0.6f;

    [Header("Sound (optional)")]
    public AudioSource audioSource;
    public AudioClip swingSound;
    public AudioClip impactSound;

    FirstPersonMovement movement;
    float nextAttackTime;

    void Awake()
    {
        movement = GetComponent<FirstPersonMovement>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (Input.GetKeyDown(attackKey) && Time.time >= nextAttackTime)
        {
            StartAttack();
        }
    }

    void StartAttack()
    {
        nextAttackTime = Time.time + cooldown;

        if (animator != null && !string.IsNullOrEmpty(attackTrigger))
            animator.SetTrigger(attackTrigger);

        if (audioSource != null && swingSound != null)
            audioSource.PlayOneShot(swingSound);

        if (lockMovement && movement != null)
            StartCoroutine(LockMovementRoutine());

        if (!useAnimationEvent)
            StartCoroutine(DelayedHit());
    }

    IEnumerator DelayedHit()
    {
        yield return new WaitForSeconds(hitDelay);
        AttackHit();
    }

    IEnumerator LockMovementRoutine()
    {
        movement.movementLocked = true;
        yield return new WaitForSeconds(lockDuration);
        movement.movementLocked = false;
    }

    /// <summary>
    /// Deals the damage. Called automatically after hitDelay, or from an
    /// Animation Event if useAnimationEvent is on.
    /// </summary>
    public void AttackHit()
    {
        Vector3 center = GetHitCenter();
        Vector3 forward = transform.forward;

        Collider[] hits = Physics.OverlapSphere(center, radius, hitLayers, QueryTriggerInteraction.Ignore);

        // A building usually has many colliders; only hit each one once.
        var hitDestructibles = new HashSet<DestructibleObject>();
        var hitBodies = new HashSet<Rigidbody>();
        bool hitSomething = false;

        foreach (Collider col in hits)
        {
            if (col.transform.IsChildOf(transform)) continue; // don't hit ourselves

            Vector3 point = col.ClosestPoint(center);

            DestructibleObject destructible = col.GetComponentInParent<DestructibleObject>();
            if (destructible != null && hitDestructibles.Add(destructible))
            {
                destructible.ApplyForce(attackForce, point, forward);
                hitSomething = true;
            }

            Rigidbody body = col.attachedRigidbody;
            if (body != null && !body.isKinematic && hitBodies.Add(body))
            {
                body.AddForceAtPosition(forward * rigidbodyImpulse, point, ForceMode.Impulse);
                hitSomething = true;
            }
        }

        if (hitSomething && audioSource != null && impactSound != null)
            audioSource.PlayOneShot(impactSound);
    }

    Vector3 GetHitCenter()
    {
        return transform.position + Vector3.up * height + transform.forward * range;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(GetHitCenter(), radius);
    }
}
