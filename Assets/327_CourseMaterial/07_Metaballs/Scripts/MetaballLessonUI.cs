using UnityEngine;
using UnityEngine.UI;

public class MetaballLessonUI : MonoBehaviour
{
    public MetaballDemo demo;
    public Button[] modes;

    void Update()
    {
        for (int i = 0; i < modes.Length; i++) modes[i].interactable = i != (int)demo.mode;
    }

    public void SetMode(int mode) { demo.mode = (MetaballDemo.Mode)mode; }
}
