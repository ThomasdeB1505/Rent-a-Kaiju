using System.Collections.Generic;
using UnityEngine;
using DiabolicalGames;

[RequireComponent(typeof(Rigidbody))]
public class FirstPersonMovement : MonoBehaviour
{
    public float speed = 5;

    [Header("Running")]
    public bool canRun = true;
    public bool IsRunning { get; private set; }
    public float runSpeed = 9;
    public KeyCode runningKey = KeyCode.LeftShift;

    [Header("Tank Turning")]
    public float turnSpeed = 120f;

    [Header("Push Force")]
    [Tooltip("Force applied to things you walk into.")]
    public float walkPushForce = 200f;
    [Tooltip("Force applied to things you run into.")]
    public float runPushForce = 600f;
    [Tooltip("Also physically push non-kinematic rigidbodies you walk into.")]
    public bool pushRigidbodies = true;
    [Tooltip("How directly you must be moving into a surface for force to apply (1 = head-on only, 0 = any angle in front).")]
    [Range(0f, 1f)] public float pushAngleThreshold = 0.5f;

    /// <summary>Force currently applied when walking into something.</summary>
    public float CurrentPushForce => IsRunning ? runPushForce : walkPushForce;

    /// <summary>When true, the player can't move or turn (e.g. during an attack).</summary>
    [HideInInspector] public bool movementLocked;

    [Header("Footsteps")]
    public bool footstepsEnabled = true;
    [Tooltip("AudioSource used to play footsteps. If left empty, one is added automatically.")]
    public AudioSource footstepSource;
    [Tooltip("Pool of footstep clips. A random one is picked each step (never the same twice in a row).")]
    public AudioClip[] footstepClips;
    [Tooltip("Seconds between steps while walking.")]
    public float walkStepInterval = 0.5f;
    [Tooltip("Seconds between steps while running.")]
    public float runStepInterval = 0.3f;
    [Tooltip("Minimum horizontal speed before footsteps play.")]
    public float minStepSpeed = 0.5f;
    [Range(0f, 1f)] public float footstepVolume = 0.8f;
    [Tooltip("Random pitch variation (+/-) so steps don't sound identical.")]
    [Range(0f, 0.5f)] public float pitchVariation = 0.1f;
    [Tooltip("Random volume variation (+/-).")]
    [Range(0f, 0.5f)] public float volumeVariation = 0.1f;

    [Header("Ground Check")]
    [Tooltip("How far below the pivot to look for ground. Increase if your pivot is at the capsule's center.")]
    public float groundCheckDistance = 1.1f;
    public LayerMask groundLayers = ~0;

    Rigidbody rigidbody;
    public List<System.Func<float>> speedOverrides = new List<System.Func<float>>();

    float stepTimer;
    int lastClipIndex = -1;

    // The velocity we *tried* to move at this physics step. Used for push force,
    // because the actual velocity drops to ~0 once we're pressed against a wall.
    Vector3 intendedMoveVelocity;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();

        if (footstepSource == null)
        {
            footstepSource = gameObject.AddComponent<AudioSource>();
            footstepSource.playOnAwake = false;
            footstepSource.spatialBlend = 0f; // 2D: it's the player's own feet
        }
    }

    void FixedUpdate()
    {
        IsRunning = canRun && !movementLocked && Input.GetKey(runningKey);

        float targetMovingSpeed = IsRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
        {
            targetMovingSpeed = speedOverrides[speedOverrides.Count - 1]();
        }

        // Cancel any physics-induced spin (e.g. uneven debris contact torque),
        // without fighting our own script-driven Y rotation below.
        rigidbody.angularVelocity = Vector3.zero;

        float turn = movementLocked ? 0f : Input.GetAxis("Horizontal");
        transform.Rotate(Vector3.up, turn * turnSpeed * Time.fixedDeltaTime, Space.World);

        float forwardInput = movementLocked ? 0f : Input.GetAxis("Vertical") * targetMovingSpeed;
        Vector3 targetVelocity = transform.rotation * new Vector3(0, 0, forwardInput);
        intendedMoveVelocity = targetVelocity;
        targetVelocity.y = rigidbody.linearVelocity.y;

        rigidbody.linearVelocity = targetVelocity;
    }

    void OnCollisionStay(Collision collision)
    {
        ApplyPushForce(collision);
    }

    void ApplyPushForce(Collision collision)
    {
        Vector3 moveDir = intendedMoveVelocity;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.01f) return; // not trying to move
        moveDir.Normalize();

        // Find a contact we're actually walking into (not the floor, not something behind us).
        // Contact normals point from the other object toward us.
        bool found = false;
        Vector3 hitPoint = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (Vector3.Dot(moveDir, -contact.normal) >= pushAngleThreshold)
            {
                hitPoint = contact.point;
                found = true;
                break;
            }
        }
        if (!found) return;

        float force = CurrentPushForce;

        DestructibleObject destructible = collision.collider.GetComponentInParent<DestructibleObject>();
        if (destructible != null)
        {
            destructible.ApplyForce(force, hitPoint, moveDir);
        }

        Rigidbody otherBody = collision.rigidbody;
        if (pushRigidbodies && otherBody != null && !otherBody.isKinematic)
        {
            otherBody.AddForceAtPosition(moveDir * force, hitPoint, ForceMode.Force);
        }
    }

    void Update()
    {
        UpdateFootsteps();
    }

    void UpdateFootsteps()
    {
        if (!footstepsEnabled || footstepClips == null || footstepClips.Length == 0)
            return;

        Vector3 v = rigidbody.linearVelocity;
        float horizontalSpeed = new Vector2(v.x, v.z).magnitude;

        if (horizontalSpeed < minStepSpeed || !IsGrounded())
        {
            // Reset so the first step plays promptly when movement resumes.
            stepTimer = 0f;
            return;
        }

        stepTimer -= Time.deltaTime;
        if (stepTimer <= 0f)
        {
            PlayFootstep();
            stepTimer = IsRunning ? runStepInterval : walkStepInterval;
        }
    }

    bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, groundCheckDistance + 0.1f,
                               groundLayers, QueryTriggerInteraction.Ignore);
    }

    void PlayFootstep()
    {
        int index = Random.Range(0, footstepClips.Length);
        if (footstepClips.Length > 1 && index == lastClipIndex)
            index = (index + 1) % footstepClips.Length;
        lastClipIndex = index;

        AudioClip clip = footstepClips[index];
        if (clip == null) return;

        footstepSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        float volume = footstepVolume + Random.Range(-volumeVariation, volumeVariation);
        footstepSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
}