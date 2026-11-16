using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class LoadingSceneTests
{
    private const string LOADING_SCENE_PATH = "Assets/Scenes/LoadingScene.unity";

    private static Type ResolveRuntimeType(string typeName)
    {
        Assembly asm = Assembly.Load("Assembly-CSharp");
        Type resolved = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
        Assert.IsNotNull(resolved, $"Could not resolve runtime type '{typeName}'.");
        return resolved;
    }

    [Test]
    public void LoadingScene_ContainsStaticCenteredJeepney_VerticalAnimatedRoad_WavingTrees_WindLines_AndNoDeadSpace()
    {
        Assert.IsTrue(
            System.IO.File.Exists(LOADING_SCENE_PATH),
            $"Expected LoadingScene at '{LOADING_SCENE_PATH}'. Run Pamahiin/Build Loading Scene (Jeepney Road).");

        bool inBuildSettings = EditorBuildSettings.scenes.Any(
            s => s.enabled && string.Equals(s.path, LOADING_SCENE_PATH, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(inBuildSettings, "LoadingScene.unity must be enabled in EditorBuildSettings for additive runtime loading.");

        Type controllerType = ResolveRuntimeType("LoadingRoadBackdropController");
        Type animatorType = ResolveRuntimeType("LoadingRoadSpriteAnimator");

        Scene previousScene = SceneManager.GetActiveScene();
        string previousPath = previousScene.path;

        try
        {
            Scene loaded = EditorSceneManager.OpenScene(LOADING_SCENE_PATH, OpenSceneMode.Additive);
            Assert.IsTrue(loaded.IsValid(), "Failed to open LoadingScene.");

            GameObject root = loaded.GetRootGameObjects().FirstOrDefault(go => go.name == "LoadingRoadBackdrop");
            Assert.IsNotNull(root, "LoadingScene should contain 'LoadingRoadBackdrop' root.");

            Component controller = root.GetComponent(controllerType);
            Assert.IsNotNull(controller, "LoadingRoadBackdropController missing on LoadingRoadBackdrop.");

            Camera backdropCam = controllerType.GetProperty("BackdropCamera").GetValue(controller) as Camera;
            Transform jeepneyRoot = controllerType.GetProperty("JeepneyRoot").GetValue(controller) as Transform;
            Transform groundTr = controllerType.GetProperty("RoadsideGroundBackdrop").GetValue(controller) as Transform;
            int treeCount = (int)controllerType.GetProperty("TreeCount").GetValue(controller);

            Assert.IsNotNull(backdropCam, "Backdrop camera missing.");
            Assert.IsTrue(backdropCam.orthographic, "LoadingScene camera must be orthographic.");
            Assert.IsNotNull(jeepneyRoot, "JeepneyRoot reference missing.");

            Vector3 jeepStartPos = jeepneyRoot.localPosition;
            Assert.LessOrEqual(Mathf.Abs(jeepStartPos.x), 0.01f, "Jeepney should be horizontally centered.");
            Assert.LessOrEqual(Mathf.Abs(jeepStartPos.y), 0.01f, "Jeepney should be vertically centered.");

            // Check Headlight cones & Taillights
            Assert.IsNotNull(jeepneyRoot.Find("HeadlightCone_Left"), "Expected HeadlightCone_Left on Jeepney.");
            Assert.IsNotNull(jeepneyRoot.Find("HeadlightCone_Right"), "Expected HeadlightCone_Right on Jeepney.");
            Assert.IsNotNull(jeepneyRoot.Find("Taillight_Left"), "Expected Taillight_Left on Jeepney.");
            Assert.IsNotNull(jeepneyRoot.Find("Taillight_Right"), "Expected Taillight_Right on Jeepney.");

            // Check Full-bleed ground covers well beyond 21:9 camera extents
            Assert.IsNotNull(groundTr, "RoadsideGroundBackdrop missing.");
            SpriteRenderer groundSr = groundTr.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(groundSr);
            float groundWidth = groundSr.drawMode == SpriteDrawMode.Tiled ? groundSr.size.x * groundTr.lossyScale.x : groundSr.bounds.size.x;
            float groundHeight = groundSr.drawMode == SpriteDrawMode.Tiled ? groundSr.size.y * groundTr.lossyScale.y : groundSr.bounds.size.y;
            float minUltrawideWidth = backdropCam.orthographicSize * 2f * (21f / 9f);
            float minHeight = backdropCam.orthographicSize * 2f;
            Assert.GreaterOrEqual(groundWidth, minUltrawideWidth, "Ground backdrop should cover 21:9 width with zero dead space.");
            Assert.GreaterOrEqual(groundHeight, minHeight, "Ground backdrop should cover full vertical viewport.");

            PropertyInfo framesProp = animatorType.GetProperty("Frames");

            // Check animated vertical road tiles
            Transform roadRoot = root.transform.Find("VerticalRoad");
            Assert.IsNotNull(roadRoot, "VerticalRoad group missing.");
            Assert.GreaterOrEqual(roadRoot.childCount, 3, "Expected stacked road tiles for seamless vertical fill.");
            Component firstRoadAnim = roadRoot.GetChild(0).GetComponent(animatorType);
            Assert.IsNotNull(firstRoadAnim, "Road tile missing LoadingRoadSpriteAnimator.");
            Sprite[] roadFrames = framesProp.GetValue(firstRoadAnim) as Sprite[];
            Assert.GreaterOrEqual(roadFrames.Length, 8, "Road animation should have multi-frame sprite sheet.");

            // Check wind lines
            Transform windRoot = root.transform.Find("VerticalWindLines");
            Assert.IsNotNull(windRoot, "VerticalWindLines group missing.");
            Assert.GreaterOrEqual(windRoot.childCount, 4, "Expected vertical wind strips.");
            Component firstWindAnim = windRoot.GetChild(0).GetComponent(animatorType);
            Assert.IsNotNull(firstWindAnim, "Wind strip missing LoadingRoadSpriteAnimator.");
            Sprite[] windFrames = framesProp.GetValue(firstWindAnim) as Sprite[];
            Assert.GreaterOrEqual(windFrames.Length, 4, "Wind lines sheet should have multiple frames.");

            // Check trees animate and scroll strictly top-to-bottom while Jeepney stays static
            Transform treesRoot = root.transform.Find("RoadsideTrees");
            Assert.IsNotNull(treesRoot, "RoadsideTrees group missing.");
            Assert.GreaterOrEqual(treeCount, 8, "Expected roadside trees on left and right sides.");
            Transform firstTree = treesRoot.GetChild(0);
            Component treeAnim = firstTree.GetComponent(animatorType);
            Assert.IsNotNull(treeAnim, "Tree missing LoadingRoadSpriteAnimator for waving foliage.");
            Sprite[] treeFrames = framesProp.GetValue(treeAnim) as Sprite[];
            Assert.GreaterOrEqual(treeFrames.Length, 4, "Tree waving sheet should have at least 4 frames.");

            MethodInfo stepMethod = controllerType.GetMethod("StepScrollingTrees");
            Assert.IsNotNull(stepMethod, "StepScrollingTrees method missing.");

            float startY = firstTree.localPosition.y;
            Vector3 roadStartPos = roadRoot.localPosition;
            stepMethod.Invoke(controller, new object[] { 0.1f });
            Assert.Less(firstTree.localPosition.y, startY, "Tree must move downward (top to bottom).");
            Assert.AreEqual(jeepStartPos, jeepneyRoot.localPosition, "Jeepney must remain static.");
            Assert.AreEqual(roadStartPos, roadRoot.localPosition, "Road transform must remain static.");

            // Verify wrapping from bottom back above top bound
            for (int step = 0; step < 60; step++)
            {
                stepMethod.Invoke(controller, new object[] { 0.1f });
            }
            Assert.GreaterOrEqual(firstTree.localPosition.y, -9.5f);
            Assert.LessOrEqual(firstTree.localPosition.y, 9.5f);

            EditorSceneManager.CloseScene(loaded, removeScene: true);
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousPath) && SceneManager.GetActiveScene().path != previousPath)
            {
                Scene orig = SceneManager.GetSceneByPath(previousPath);
                if (orig.IsValid())
                {
                    SceneManager.SetActiveScene(orig);
                }
            }
        }
    }

    [Test]
    public void LoadingScreenUxml_ContainsBackdropElement_AndSpriteSheetsExist()
    {
        VisualTreeAsset uxml = Resources.Load<VisualTreeAsset>("UI/LoadingScreen");
        Assert.IsNotNull(uxml, "Resources/UI/LoadingScreen.uxml failed to load.");
        VisualElement root = uxml.Instantiate();
        Assert.IsNotNull(root.Q<VisualElement>("loading-scene-backdrop"), "Expected 'loading-scene-backdrop' element in LoadingScreen.uxml.");

        Assert.GreaterOrEqual(Resources.LoadAll<Sprite>("Sprites/Loading/road_moving_sheet").Length, 8);
        Assert.GreaterOrEqual(Resources.LoadAll<Sprite>("Sprites/Loading/trees_waving_sheet").Length, 4);
        Assert.GreaterOrEqual(Resources.LoadAll<Sprite>("Sprites/Loading/wind_lines_sheet").Length, 4);
    }
}
