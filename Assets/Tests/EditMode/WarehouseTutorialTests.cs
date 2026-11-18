using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class WarehouseTutorialTests
{
    private static Type ResolveRuntimeType(string typeName)
    {
        Assembly asm = Assembly.Load("Assembly-CSharp");
        Type resolved = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
        Assert.IsNotNull(resolved, $"Could not resolve runtime type '{typeName}'.");
        return resolved;
    }

    private static void InvokeStatic(Type type, string methodName)
    {
        var m = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(m, $"Missing static method {type.Name}.{methodName}");
        m.Invoke(null, null);
    }

    private static T InvokeStatic<T>(Type type, string methodName)
    {
        var m = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(m, $"Missing static method {type.Name}.{methodName}");
        return (T)m.Invoke(null, null);
    }

    [SetUp]
    public void SetUp()
    {
        var progType = ResolveRuntimeType("ProgressionAndLoadoutManager");
        InvokeStatic(progType, "ResetTutorialCompletion");
    }

    [TearDown]
    public void TearDown()
    {
        var progType = ResolveRuntimeType("ProgressionAndLoadoutManager");
        InvokeStatic(progType, "ResetTutorialCompletion");
    }

    [Test]
    public void TutorialPersistence_DefaultsFalse_AndPersistsCompletedOrSkipped()
    {
        var progType = ResolveRuntimeType("ProgressionAndLoadoutManager");
        string prefKey = (string)progType.GetField("PREF_TUTORIAL_COMPLETED", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
        int starterCurrency = (int)progType.GetField("STARTER_CURRENCY", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();

        Assert.IsFalse(InvokeStatic<bool>(progType, "HasCompletedOrSkippedTutorial"));
        Assert.AreEqual(starterCurrency, PlayerPrefs.GetInt("Pamahiin_Currency_v2", 0));

        PlayerPrefs.SetInt("Pamahiin_Currency_v2", 0);
        PlayerPrefs.SetInt("Pamahiin_Currency_Initialized_v2", 1);
        InvokeStatic(progType, "MarkTutorialSkipped");
        Assert.IsTrue(InvokeStatic<bool>(progType, "HasCompletedOrSkippedTutorial"));
        Assert.AreEqual(1, PlayerPrefs.GetInt(prefKey, 0));
        Assert.AreEqual(starterCurrency, PlayerPrefs.GetInt("Pamahiin_Currency_v2", 0));

        PlayerPrefs.SetInt("Pamahiin_Currency_v2", 0);
        InvokeStatic(progType, "ResetTutorialCompletion");
        Assert.IsFalse(InvokeStatic<bool>(progType, "HasCompletedOrSkippedTutorial"));
        Assert.AreEqual(starterCurrency, PlayerPrefs.GetInt("Pamahiin_Currency_v2", 0));

        PlayerPrefs.SetInt("Pamahiin_Currency_v2", 0);
        InvokeStatic(progType, "MarkTutorialCompleted");
        Assert.IsTrue(InvokeStatic<bool>(progType, "HasCompletedOrSkippedTutorial"));
        Assert.AreEqual(starterCurrency, PlayerPrefs.GetInt("Pamahiin_Currency_v2", 0));
    }

    [Test]
    public void WarehouseTutorialManager_ProgressesBay1ThroughBay5_AndUnlocksDoors()
    {
        var progType = ResolveRuntimeType("ProgressionAndLoadoutManager");
        var mgrType = ResolveRuntimeType("WarehouseTutorialManager");
        var bayEnum = ResolveRuntimeType("TutorialBay");

        var go = new GameObject("TestTutorialManager");
        try
        {
            var mgr = go.AddComponent(mgrType);
            var initMethod = mgrType.GetMethod("InitializeState");
            var completeStepMethod = mgrType.GetMethod("CompleteStep");
            var shouldLockMethod = mgrType.GetMethod("ShouldBayDoorBeLocked");
            var currentBayProp = mgrType.GetProperty("CurrentBay");

            string GetStep(string field) => (string)mgrType.GetField(field, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
            int GetBayInt() => Convert.ToInt32(currentBayProp.GetValue(mgr));

            initMethod.Invoke(mgr, null);

            Assert.AreEqual(1, GetBayInt());
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 0 }));
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 1 }));
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 2 }));
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 3 }));

            // Bay 1
            Assert.IsTrue((bool)completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B1_PICKUP_FLASHLIGHT") }));
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 0 }));
            Assert.IsTrue((bool)completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B1_TOGGLE_FLASHLIGHT") }));
            Assert.IsFalse((bool)shouldLockMethod.Invoke(mgr, new object[] { 0 }));
            Assert.IsTrue((bool)completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B1_OPEN_DOOR") }));
            Assert.AreEqual(2, GetBayInt());

            // Bay 2
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 1 }));
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B2_ENTER_DARKNESS") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B2_RESTORE_POWER") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B2_FLIP_LIGHT") });
            Assert.AreEqual(3, GetBayInt());
            Assert.IsFalse((bool)shouldLockMethod.Invoke(mgr, new object[] { 1 }));

            // Bay 3
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 2 }));
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B3_EMF") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B3_THERMOMETER") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B3_SPIRIT_BOX") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B3_WRITING") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B3_CAMERA_DOTS") });
            Assert.AreEqual(4, GetBayInt());
            Assert.IsFalse((bool)shouldLockMethod.Invoke(mgr, new object[] { 2 }));

            // Bay 4
            Assert.IsTrue((bool)shouldLockMethod.Invoke(mgr, new object[] { 3 }));
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B4_CRUCIFIX") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B4_SURVIVE_HUNT") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B4_SANITY_PILLS") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B4_EXORCISM") });
            Assert.AreEqual(5, GetBayInt());
            Assert.IsFalse((bool)shouldLockMethod.Invoke(mgr, new object[] { 3 }));

            // Bay 5
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B5_JOURNAL_DEDUCTION") });
            completeStepMethod.Invoke(mgr, new object[] { GetStep("STEP_B5_EXIT_CONSOLE") });
            Assert.AreEqual(6, GetBayInt());
            Assert.IsTrue(InvokeStatic<bool>(progType, "HasCompletedOrSkippedTutorial"));
            int starterCurrency = (int)progType.GetField("STARTER_CURRENCY", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
            Assert.AreEqual(starterCurrency, PlayerPrefs.GetInt("Pamahiin_Currency_v2", 0));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
