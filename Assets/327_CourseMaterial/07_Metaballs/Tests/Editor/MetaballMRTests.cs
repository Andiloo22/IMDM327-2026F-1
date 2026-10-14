using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

public class MetaballMRTests
{
    [Test]
    public void MRSceneHasTemplateRigAndThreeGrabHandles()
    {
        EditorSceneManager.OpenScene("Assets/327_CourseMaterial/07_Metaballs/Scenes/03_Metaballs_MR.unity");
        var origin = Object.FindFirstObjectByType<XROrigin>();
        var mr = Object.FindFirstObjectByType<MetaballMR>();
        Assert.That(origin, Is.Not.Null);
        Assert.That(Object.FindFirstObjectByType<ARSession>(), Is.Not.Null);
        Assert.That(origin.Camera.GetComponent<ARCameraBackground>().enabled, Is.True);
        Assert.That(origin.Camera.GetComponent<ARCameraManager>().enabled, Is.True);
        Assert.That(mr.head, Is.EqualTo(origin.Camera.transform));
        Assert.That(mr.demo.desktopInput, Is.False);
        Assert.That(mr.handles.Length, Is.EqualTo(3));
        var comparison = Object.FindFirstObjectByType<MetaballShaderComparison>();
        Assert.That(comparison.shaderGraph, Is.False);
        Assert.That(comparison.volume, Is.EqualTo(mr.demo));
        Assert.That(comparison.camera, Is.EqualTo(origin.Camera));
        Assert.That(ShaderUtil.ShaderHasError(comparison.graph.shader), Is.False);
        Assert.That(Object.FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None).Length, Is.EqualTo(3));
        for (int i = 0; i < 3; i++)
        {
            Assert.That(mr.handles[i].transform, Is.EqualTo(mr.demo.centers[i]));
            Assert.That(mr.handles[i].unparentTransformOnGrab, Is.False);
            Assert.That(mr.handles[i].throwOnDetach, Is.False);
            Assert.That(mr.handles[i].GetComponent<Rigidbody>().useGravity, Is.False);
            Assert.That(mr.handles[i].GetComponent<Collider>(), Is.Not.Null);
        }
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
        Assert.That(GameObject.Find("UI"), Is.Null);
        Assert.That(GameObject.Find("Object Spawner"), Is.Null);
    }

    [UnityTest]
    public IEnumerator TwoInteractorsMoveCentersAndReleaseWithoutThrowing()
    {
        yield return new EnterPlayMode();
        yield return SceneManager.LoadSceneAsync("03_Metaballs_MR");
        yield return Frames(3);
        var mr = Object.FindFirstObjectByType<MetaballMR>();
        // Test selection separately from hardware tracking.
        mr.StopAllCoroutines();
        Object.FindFirstObjectByType<XROrigin>().gameObject.SetActive(false);
        Object.FindFirstObjectByType<ARSession>().gameObject.SetActive(false);
        var manager = Object.FindFirstObjectByType<XRInteractionManager>();
        var hands = new XRDirectInteractor[2];
        Vector3[] before = { mr.demo.centers[0].position, mr.demo.centers[1].position };
        for (int i = 0; i < 2; i++)
        {
            var hand = new GameObject("Test hand " + i);
            hand.transform.position = before[i];
            hand.AddComponent<SphereCollider>().isTrigger = true;
            hands[i] = hand.AddComponent<XRDirectInteractor>();
            hands[i].interactionManager = manager;
        }
        yield return Frames(2);
        for (int i = 0; i < 2; i++) hands[i].StartManualInteraction((IXRSelectInteractable)mr.handles[i]);
        yield return Frames(2);
        for (int i = 0; i < 2; i++)
        {
            Assert.That(mr.handles[i].isSelected, Is.True);
            hands[i].transform.position += new Vector3(i == 0 ? -0.12f : 0.12f, 0.08f, 0.05f);
        }
        yield return Frames(8);
        for (int i = 0; i < 2; i++)
        {
            Assert.That(Vector3.Distance(before[i], mr.demo.centers[i].position), Is.GreaterThan(0.05f));
            Assert.That(mr.demo.centers[i].parent, Is.EqualTo(mr.demo.transform));
            hands[i].EndManualInteraction();
        }
        Vector3 released = mr.demo.centers[0].position;
        yield return Frames(5);
        Assert.That(Vector3.Distance(released, mr.demo.centers[0].position), Is.LessThan(0.005f));
        Assert.That(mr.handles[0].isSelected, Is.False);
        var properties = new MaterialPropertyBlock(); mr.demo.output.GetPropertyBlock(properties);
        Vector3 center = properties.GetVector("_Center0");
        Assert.That(Vector3.Distance(center, mr.demo.centers[0].localPosition), Is.LessThan(0.001f));
        foreach (var hand in hands) Object.Destroy(hand.gameObject);
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator Cleanup() { if (Application.isPlaying) yield return new ExitPlayMode(); }

    static IEnumerator Frames(int count)
    {
        int target = Time.frameCount + count;
        double limit = EditorApplication.timeSinceStartup + 10;
        while (Time.frameCount < target)
        {
            Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(limit));
            yield return null;
        }
    }
}
