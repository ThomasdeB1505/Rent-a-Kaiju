using UnityEngine;
using UnityEngine.UI;

public class AnyKeyButton : MonoBehaviour
{
    [SerializeField] private Button button;

    void Update()
    {
        if (Input.anyKeyDown)
        {
            button.onClick.Invoke();
        }
    }
}