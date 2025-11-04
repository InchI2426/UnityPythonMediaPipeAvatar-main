using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public class MenuManager : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("CalibrationScene");
        Debug.Log("เข้าเกม");
    }
    public void OpenSettings()
    {
        Debug.Log("เปิดหน้าSettings");
    }
    public void OpenLeaderboard()
    {
        Debug.Log("เปิดLeaderboard");
    }
    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("ปิดเกม...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}