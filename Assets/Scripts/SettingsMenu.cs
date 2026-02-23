using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SettingsMenu : MonoBehaviour
{


    public Slider rthSlider;
    public TMP_InputField droneCountInput;
    public TMP_InputField activeDroneCountInput;
    public TMP_InputField altitudeDelateRateInput;
    public TMP_InputField seperationDistanceInput;
    public TMP_Dropdown aggressionDropdown;
    public TMP_Dropdown searchPatternDropdown;


    public void Simulate()
    {
        Debug.Log($"rthSlider: {rthSlider}");
        Debug.Log($"droneCountInput: {droneCountInput}");
        Debug.Log($"activeDroneCountInput: {activeDroneCountInput}");
        Debug.Log($"altitudeDelateRateInput: {altitudeDelateRateInput}");
        Debug.Log($"seperationDistanceInput: {seperationDistanceInput}");
        Debug.Log($"aggressionDropdown: {aggressionDropdown}");
        Debug.Log($"searchPatternDropdown: {searchPatternDropdown}");

        Variables.rth = rthSlider.value;
        Variables.droneCount = int.Parse(droneCountInput.text);
        Variables.activeDroneCount = int.Parse(activeDroneCountInput.text);
        Variables.altitudeDelateRate = float.Parse(altitudeDelateRateInput.text);
        Variables.seperationDistance = float.Parse(seperationDistanceInput.text);
        Variables.aggression = aggressionDropdown.options[aggressionDropdown.value].text;
        Variables.searchPattern = searchPatternDropdown.options[searchPatternDropdown.value].text;
        SceneManager.LoadScene("testenv");
    }

}