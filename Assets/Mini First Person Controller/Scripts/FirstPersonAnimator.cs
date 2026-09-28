using UnityEngine;

public class FirstPersonAnimator : MonoBehaviour
{
    public Animator animator;
    public FirstPersonMovement movement;

    void Reset()
    {
        // Auto-fill references when the script is added.
        animator = GetComponentInChildren<Animator>();
        movement = GetComponentInParent<FirstPersonMovement>();
    }

    void Update()
    {
        float forwardInput = Mathf.Abs(Input.GetAxis("Vertical"));
        animator.SetFloat("Speed", forwardInput);
        animator.SetBool("IsRunning", movement.IsRunning);
    }
}