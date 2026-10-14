using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class MetaballMR : MonoBehaviour
{
    public Metaball3DDemo demo;
    public Transform head;
    public XRGrabInteractable[] handles;
    public float distance = 0.65f;

    IEnumerator Start()
    {
        // Wait for the headset pose before placing the field.
        yield return new WaitForSeconds(1f);
        Place();
    }

    void Update()
    {
        for (int i = 0; i < 2; i++)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(i == 0 ? XRNode.LeftHand : XRNode.RightHand);
            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) continue;
            device.TryGetFeatureValue(CommonUsages.primaryButton, out bool up);
            device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool down);
            demo.threshold = Mathf.Clamp(demo.threshold + ((up ? 1f : 0f) - (down ? 1f : 0f)) * Time.deltaTime * 0.4f, 0.2f, 1.8f);
        }
    }

    void LateUpdate()
    {
        for (int i = 0; i < handles.Length; i++)
            if (handles[i].isSelected)
            {
                demo.selected = i;
                demo.MoveCenter(i, handles[i].transform.position);
            }
        demo.Draw();
    }

    [ContextMenu("Recenter")]
    public void Place()
    {
        if (handles != null)
            for (int i = 0; i < handles.Length; i++)
                if (handles[i] != null && handles[i].isSelected) return;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        demo.transform.SetPositionAndRotation(head.position + forward * distance + Vector3.down * 0.15f, Quaternion.LookRotation(forward));
    }
}
