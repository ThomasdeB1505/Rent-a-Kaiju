using UnityEngine;

/// <summary>
/// A flying car that moves along a path and counts as a forbidden target.
///
/// HOW TO USE
/// 1. Put this on your car prefab (the root object). ForbiddenTarget is added automatically,
///    so the glow, feedback and LoseManager reporting all work with no extra setup.
/// 2. Pick a Mode and tune the numbers. Select the car to see its path drawn in the Scene view.
///    - Patrol:    flies back and forth along the car's FORWARD direction (blue arrow).
///    - Circle:    flies a loop. Starts at the car's position, heading forward.
///    - Waypoints: flies through a list of Transforms (empty GameObjects) you place in the scene.
/// 3. Drag the prefab into the level as many times as you like. Make level 2 harder by
///    raising Speed, adding more cars, or turning on Speed Variance / Random Start Position.
///
/// NOTES
/// - If your car model's front doesn't face +Z, make the model a CHILD of an empty root and
///   put this script on the root (ForbiddenTarget finds renderers in children automatically).
/// - If your kaiju hits things via OnCollisionEnter/OnTriggerEnter, give the car a Collider
///   and a Rigidbody. This script sets any Rigidbody on the car to kinematic with no gravity.
/// - Destruction: your existing kaiju logic just needs to Destroy(gameObject) the car,
///   exactly like it does for the forbidden buildings.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ForbiddenTarget))]
public class ForbiddenCar : MonoBehaviour
{
    public enum MoveMode { Patrol, Circle, Waypoints }

    [Header("Movement")]
    public MoveMode mode = MoveMode.Patrol;
    [Tooltip("Units per second.")]
    public float speed = 6f;
    [Tooltip("Randomly makes each car this fraction faster/slower (0.2 = +/-20%). Good for harder levels.")]
    [Range(0f, 0.5f)] public float speedVariance = 0f;
    [Tooltip("Start somewhere random along the path so copies of the same car don't move in sync.")]
    public bool randomStartPosition = false;

    [Header("Patrol")]
    [Tooltip("How far the car travels along its forward direction before turning back.")]
    public float patrolDistance = 20f;

    [Header("Circle")]
    public float circleRadius = 10f;
    [Tooltip("Off: starts heading forward and curves left. On: starts heading backward and curves right.")]
    public bool clockwise = false;

    [Header("Waypoints")]
    [Tooltip("Empty GameObjects placed in the scene. They can be anywhere (even children of the car).")]
    public Transform[] waypoints;
    [Tooltip("Off: loops 1 > 2 > 3 > 1. On: goes 1 > 2 > 3 > 2 > 1.")]
    public bool pingPong = false;

    [Header("Flying")]
    [Tooltip("Added on top of the car's placed height (and on top of waypoint heights) so it floats above the buildings.")]
    public float hoverHeight = 8f;
    public float bobAmount = 0.5f;
    public float bobSpeed = 2f;

    [Header("Rotation")]
    [Tooltip("How quickly the car turns to face where it's going.")]
    public float turnSpeed = 6f;

    // Runtime state
    private Vector3 origin;
    private Vector3 fwd;
    private Vector3 right;
    private Vector3 basePos;       // position without the bobbing
    private Vector3[] wpPositions;
    private int wpIndex;
    private int wpStep = 1;
    private float travelled;
    private float actualSpeed;
    private float bobPhase;

    void Awake()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        fwd = Flat(transform.forward);
        right = Vector3.Cross(Vector3.up, fwd).normalized;
        origin = transform.position + Vector3.up * hoverHeight;
        basePos = origin;

        actualSpeed = speed * (1f + Random.Range(-speedVariance, speedVariance));
        bobPhase = Random.value * Mathf.PI * 2f;

        switch (mode)
        {
            case MoveMode.Patrol:
                if (randomStartPosition) travelled = Random.Range(0f, Mathf.Max(0.01f, patrolDistance) * 2f);
                basePos = origin + fwd * Mathf.PingPong(travelled, patrolDistance);
                break;

            case MoveMode.Circle:
                if (circleRadius > 0.1f)
                {
                    if (randomStartPosition) travelled = Random.Range(0f, Mathf.PI * 2f * circleRadius);
                    basePos = CirclePoint(travelled);
                }
                break;

            case MoveMode.Waypoints:
                CacheWaypoints();
                if (randomStartPosition && wpPositions.Length > 0)
                {
                    wpIndex = Random.Range(0, wpPositions.Length);
                    basePos = wpPositions[wpIndex];
                    AdvanceWaypoint();
                }
                break;
        }

        transform.position = basePos;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 prev = basePos;

        switch (mode)
        {
            case MoveMode.Patrol:
                travelled += actualSpeed * dt;
                basePos = origin + fwd * Mathf.PingPong(travelled, patrolDistance);
                break;

            case MoveMode.Circle:
                if (circleRadius > 0.1f)
                {
                    travelled += actualSpeed * dt;
                    basePos = CirclePoint(travelled);
                }
                break;

            case MoveMode.Waypoints:
                StepWaypoints(dt);
                break;
        }

        // Face the direction of travel (ignoring height changes)
        Vector3 move = basePos - prev;
        move.y = 0f;
        if (move.sqrMagnitude > 1e-8f)
        {
            Quaternion target = Quaternion.LookRotation(move.normalized, Vector3.up);
            float t = 1f - Mathf.Exp(-turnSpeed * dt); // frame-rate independent smoothing
            transform.rotation = Quaternion.Slerp(transform.rotation, target, t);
        }

        float bob = Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobAmount;
        transform.position = basePos + Vector3.up * bob;
    }

    // ---------- Path helpers ----------

    Vector3 CirclePoint(float distanceTravelled)
    {
        float angle = distanceTravelled / circleRadius * (clockwise ? -1f : 1f);
        Vector3 center = origin - right * circleRadius;
        return center + (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * circleRadius;
    }

    void CacheWaypoints()
    {
        int count = 0;
        if (waypoints != null)
            foreach (var w in waypoints) if (w != null) count++;

        wpPositions = new Vector3[count];
        int i = 0;
        if (waypoints != null)
        {
            foreach (var w in waypoints)
            {
                if (w == null) continue;
                // Cached once, so waypoints can safely be children of the car.
                wpPositions[i++] = w.position + Vector3.up * hoverHeight;
            }
        }

        if (count == 0)
            Debug.LogWarning($"{name}: Mode is Waypoints but no waypoints are assigned.", this);
    }

    void StepWaypoints(float dt)
    {
        if (wpPositions == null || wpPositions.Length == 0) return;

        Vector3 target = wpPositions[wpIndex];
        basePos = Vector3.MoveTowards(basePos, target, actualSpeed * dt);

        if ((basePos - target).sqrMagnitude < 0.0001f)
            AdvanceWaypoint();
    }

    void AdvanceWaypoint()
    {
        int n = wpPositions.Length;
        if (n < 2) return;

        if (pingPong)
        {
            if (wpIndex + wpStep >= n || wpIndex + wpStep < 0) wpStep = -wpStep;
            wpIndex += wpStep;
        }
        else
        {
            wpIndex = (wpIndex + 1) % n;
        }
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    // ---------- Scene view preview ----------

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.35f);

        bool playing = Application.isPlaying;
        Vector3 o = playing ? origin : transform.position + Vector3.up * hoverHeight;
        Vector3 f = playing ? fwd : Flat(transform.forward);
        Vector3 r = Vector3.Cross(Vector3.up, f).normalized;

        switch (mode)
        {
            case MoveMode.Patrol:
                Gizmos.DrawLine(o, o + f * patrolDistance);
                Gizmos.DrawWireSphere(o, 0.5f);
                Gizmos.DrawWireSphere(o + f * patrolDistance, 0.5f);
                break;

            case MoveMode.Circle:
                if (circleRadius <= 0.1f) break;
                Vector3 c = o - r * circleRadius;
                Vector3 prev = c + r * circleRadius;
                for (int i = 1; i <= 48; i++)
                {
                    float a = i / 48f * Mathf.PI * 2f;
                    Vector3 p = c + (r * Mathf.Cos(a) + f * Mathf.Sin(a)) * circleRadius;
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
                break;

            case MoveMode.Waypoints:
                if (waypoints == null) break;
                Vector3? last = null;
                Vector3? first = null;
                foreach (var w in waypoints)
                {
                    if (w == null) continue;
                    Vector3 p = w.position + Vector3.up * hoverHeight;
                    Gizmos.DrawWireSphere(p, 0.5f);
                    if (last.HasValue) Gizmos.DrawLine(last.Value, p);
                    if (!first.HasValue) first = p;
                    last = p;
                }
                if (!pingPong && first.HasValue && last.HasValue && first != last)
                    Gizmos.DrawLine(last.Value, first.Value);
                break;
        }
    }
}
