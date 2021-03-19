using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class MainIntro : MonoBehaviour
{
    public Button aRButton;
    public Button locationButton;
    public Button navigationButton;
    public Button quitButton;

    private void Awake()
    {
        aRButton.onClick.AddListener(LaunchVRScene);
        locationButton.onClick.AddListener(LaunchVRScene);
        navigationButton.onClick.AddListener(LaunchVRScene);
    }
    public void LaunchVRScene()
    {
        SceneManager.LoadScene(1, LoadSceneMode.Single);
    }
    public void LaunchScene(int sceneBuildIndex)
    {
        SceneManager.LoadScene(sceneBuildIndex, LoadSceneMode.Single);
    }
    public void LaunchScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
    public void QuitApp()
    {
        Application.Quit();
    }
}