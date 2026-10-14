# Shader basics

Five small URP examples for the first shader lesson. Each scene compares HLSL (left) with Shader Graph (right). The graphs use ordinary nodes, without custom HLSL functions.

| Scene | Idea | Main nodes |
| --- | --- | --- |
| `01_Color` | One color for every fragment | Color → Base Color |
| `02_UV` | U = red, V = green | UV → Multiply → Add → Split → Combine |
| `03_Gradient` | Mix two colors using U | UV → Split → Lerp |
| `04_Circle` | Distance from a center | Distance → Smoothstep → One Minus → Multiply |
| `05_Time` | Pulse the circle's radius | Time → Multiply → Sine → Multiply → Add |

Open a scene, press Play, and select **Controls**. Change its Inspector values to update both examples. There are no on-screen controls.

Read the matching `.shader`, `.shadergraph`, and `Control.cs` files. `Start` gets a material copy for each panel. `Update` sends values with `SetColor`, `SetFloat`, or `SetVector`. Property names such as `_Radius` match the shader's Properties and the graph's **Reference** names. `OnDestroy` removes those material copies when the scene closes.

In `TimeControl.cs`, C# sets Speed and Amount; the shader uses Unity's time to animate the pixels. Set Speed or Amount to zero to stop the pulse.

Continue with the examples in `../07_Metaballs` after these lessons.

[Course screenshots](../../../Docs/ShaderBasics/screenshots/README.md) are saved at 1920 × 1080.

[English lecture handout](../../../Docs/ShaderBasics/IMDM327_Shader_Basics_Lecture.docx) covers the examples, Shader Graph connections, C# controls, and short exercises.

Checked in Unity 6000.3.0f1: all five shader pairs matched at default and changed settings, and each C# controller updated both panels in Play mode.

[Material.SetFloat](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/material/setfloat)

For classroom use. Course code uses the repository's MIT license.
