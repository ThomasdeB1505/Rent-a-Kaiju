
using UnityEngine;

public class HologramRotation : MonoBehaviour
{
    public float speed = 15f;

    void Update()
    {
        transform.Rotate(
            0f,
            speed * Time.deltaTime,
            0f
        );
    }
}
