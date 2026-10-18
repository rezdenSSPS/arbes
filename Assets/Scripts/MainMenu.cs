using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Start screen: Start Game, Controls, Quit.</summary>
public class MainMenu : MonoBehaviour
{
    public string gameScene = "Game";
    public GameObject mainPanel;
    public GameObject controlsPanel;
    public GameObject mainFirstSelected;
    public GameObject controlsFirstSelected;

    void Start()
    {
        Time.timeScale = 1f;
        ThirdPersonCamera.SetCursorLocked(false);
        ShowMain();
    }

    public void StartGame()
    {
        SceneManager.LoadScene(gameScene);
    }

    public void ShowControls()
    {
        if (mainPanel) mainPanel.SetActive(false);
        if (controlsPanel) controlsPanel.SetActive(true);
        Select(controlsFirstSelected);
    }

    public void ShowMain()
    {
        if (controlsPanel) controlsPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);
        Select(mainFirstSelected);
    }

    public void Quit()
    {
        QuitGame();
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static void Select(GameObject go)
    {
        if (go != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(go);
    }
}
