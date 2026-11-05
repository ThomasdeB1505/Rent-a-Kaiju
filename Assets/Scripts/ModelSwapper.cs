using UnityEngine;

public class ModelSwapper : MonoBehaviour
{
    public GameObject idleModel;
    public GameObject animatedModel;
    public Animator animator;               // the one on AnimatedModel
    public float walkSpeedThreshold = 0.5f;

    FirstPersonMovement movement;
    Rigidbody rb;

    void Awake()
    {
        movement = GetComponent<FirstPersonMovement>();
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        Vector3 v = rb.linearVelocity;
        bool walking = new Vector2(v.x, v.z).magnitude > walkSpeedThreshold;
        bool attacking = movement.movementLocked;
        bool showAnimated = walking || attacking;

        idleModel.SetActive(!showAnimated);
        animatedModel.SetActive(showAnimated);

        if (showAnimated)
            animator.SetBool("Walking", walking && !attacking);
    }
}