using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; 

public class MainMenu : MonoBehaviour
{
   public void StartEnviro()
    {
        SceneManager.LoadScene("PlanScreen"); 
    }
    
    public void QuitApp()
    {
        Application.Quit();
        Debug.Log("Game Quit"); 
    }
}