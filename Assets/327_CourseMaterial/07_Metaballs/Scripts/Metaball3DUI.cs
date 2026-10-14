using UnityEngine;
using UnityEngine.UI;

public class Metaball3DUI : MonoBehaviour
{
    public Metaball3DDemo demo;
    public Button[] modes;

    void Update()
    {
        for (int i = 0; i < modes.Length; i++) modes[i].interactable = i != (int)demo.mode;
    }

    public void SetMode(int mode) { demo.mode = (Metaball3DDemo.Mode)mode; }
}
