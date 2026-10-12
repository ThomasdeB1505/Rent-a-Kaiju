using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Tooltip("Exact name of the scene to load when Start is pressed (must be in Build Profiles / Build Settings).")]
    [SerializeField] private string gameSceneName = "GameScene";

    // Hook this up to the Start button's OnClick event
    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    // Optional: hook this up to a Quit button
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
