using System.Collections.Generic;
using UnityEngine;

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

    Rigidbody rigidbody;
    public List<System.Func<float>> speedOverrides = new List<System.Func<float>>();

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        IsRunning = canRun && Input.GetKey(runningKey);

        float targetMovingSpeed = IsRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
        {
            targetMovingSpeed = speedOverrides[speedOverrides.Count - 1]();
        }

        // Cancel any physics-induced spin (e.g. uneven debris contact torque),
        // without fighting our own script-driven Y rotation below.
        rigidbody.angularVelocity = Vector3.zero;

        float turn = Input.GetAxis("Horizontal");
        transform.Rotate(Vector3.up, turn * turnSpeed * Time.fixedDeltaTime, Space.World);

        float forwardInput = Input.GetAxis("Vertical") * targetMovingSpeed;
        Vector3 targetVelocity = transform.rotation * new Vector3(0, 0, forwardInput);
        targetVelocity.y = rigidbody.linearVelocity.y;

        rigidbody.linearVelocity = targetVelocity;
    }
}