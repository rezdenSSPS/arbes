using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Esc (or Start on a gamepad) pauses the game and shows Resume / Main Menu / Quit.</summary>
public class PauseMenu : MonoBehaviour
{
    public GameObject pausePanel;
    public GameObject firstSelected;
    public string mainMenuScene = "MainMenu";

    bool paused;

    void Start()
    {
        SetPaused(false);
    }

    void Update()
    {
        bool toggle = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                      (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        if (toggle) SetPaused(!paused);
    }

    public void SetPaused(bool value)
    {
        paused = value;
        if (pausePanel) pausePanel.SetActive(value);
        Time.timeScale = value ? 0f : 1f;
        ThirdPersonCamera.SetCursorLocked(!value);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(value ? firstSelected : null);
    }

    public void Resume()
    {
        SetPaused(false);
    }

    public void LoadMainMenu()
    {
        Time.timeScale = 1f;
        ThirdPersonCamera.SetCursorLocked(false);
        SceneManager.LoadScene(mainMenuScene);
    }

    public void Quit()
    {
        MainMenu.QuitGame();
    }
}
