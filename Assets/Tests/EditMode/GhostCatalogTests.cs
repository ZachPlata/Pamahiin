using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public class GhostCatalogTests
{
    private static Assembly RuntimeAssembly => Assembly.Load("Assembly-CSharp");

    private static Type ResolveRuntimeType(string typeName)
    {
        Type t = RuntimeAssembly.GetType(typeName, throwOnError: true);
        Assert.IsNotNull(t, $"Expected runtime type '{typeName}' in Assembly-CSharp.");
        return t;
    }

    private static IList GetAllGhosts()
    {
        Type catalogType = ResolveRuntimeType("GhostCatalog");
        PropertyInfo allGhostsProp = catalogType.GetProperty("AllGhosts", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(allGhostsProp, "GhostCatalog.AllGhosts property should exist.");
        var list = allGhostsProp.GetValue(null) as IList;
        Assert.IsNotNull(list);
        return list;
    }

    private static T GetProp<T>(object obj, string propName)
    {
        PropertyInfo prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(prop, $"Expected property '{propName}' on {obj.GetType().Name}.");
        return (T)prop.GetValue(obj);
    }

    [Test]
    public void Catalog_HasExactlyTwentyUniqueGhosts_WithExistingPrioritizedFirst()
    {
        IList ghosts = GetAllGhosts();
        Assert.AreEqual(20, ghosts.Count, "GhostCatalog should contain exactly 20 ghost types.");

        Assert.AreEqual("Tikbalang", GetProp<string>(ghosts[0], "Id"));
        Assert.AreEqual("Kapre", GetProp<string>(ghosts[1], "Id"));
        Assert.AreEqual("Manananggal", GetProp<string>(ghosts[2], "Id"));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ghosts.Count; i++)
        {
            string id = GetProp<string>(ghosts[i], "Id");
            Assert.IsFalse(string.IsNullOrWhiteSpace(id), $"Ghost at index {i} has empty Id.");
            Assert.IsTrue(ids.Add(id), $"Duplicate ghost Id '{id}'.");
        }
    }

    [Test]
    public void Catalog_CoversAllTwentyUniqueThreeProofCombinationsOfSixProofs()
    {
        Type catalogType = ResolveRuntimeType("GhostCatalog");
        FieldInfo sixProofsField = catalogType.GetField("SixProofs", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(sixProofsField);
        var sixProofsArray = (Array)sixProofsField.GetValue(null);
        var allowedProofs = new HashSet<int>();
        foreach (object ev in sixProofsArray)
        {
            allowedProofs.Add(Convert.ToInt32(ev));
        }
        Assert.AreEqual(6, allowedProofs.Count, "There should be exactly 6 journal ghost proofs.");

        var combinationKeys = new HashSet<string>();
        foreach (object ghost in GetAllGhosts())
        {
            string id = GetProp<string>(ghost, "Id");
            var evidences = GetProp<Array>(ghost, "Evidences");
            var evidenceKeys = GetProp<string[]>(ghost, "EvidenceKeys");

            Assert.AreEqual(3, evidences.Length, $"{id} must have exactly 3 proofs.");
            Assert.AreEqual(3, evidenceKeys.Length, $"{id} must have exactly 3 evidence keys.");

            var distinctForGhost = new HashSet<int>();
            var proofInts = new List<int>();
            foreach (object ev in evidences)
            {
                int evInt = Convert.ToInt32(ev);
                Assert.IsTrue(allowedProofs.Contains(evInt), $"{id} uses invalid proof '{ev}'.");
                Assert.IsTrue(distinctForGhost.Add(evInt), $"{id} has duplicate proof '{ev}'.");
                proofInts.Add(evInt);
            }

            string canonicalCombo = string.Join(",", proofInts.OrderBy(x => x).Select(x => x.ToString()));
            Assert.IsTrue(combinationKeys.Add(canonicalCombo), $"Duplicate 3-proof combination found on '{id}': {canonicalCombo}");
        }

        Assert.AreEqual(20, combinationKeys.Count, "All C(6,3) = 20 combinations of the 6 proofs must be covered.");
    }

    [Test]
    public void Catalog_PreservesExistingThreeGhostProofAssignments()
    {
        Type catalogType = ResolveRuntimeType("GhostCatalog");
        Type evidenceType = ResolveRuntimeType("EvidenceType");
        MethodInfo tryGet = catalogType.GetMethod("TryGetGhost", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(tryGet);

        int Emf = Convert.ToInt32(Enum.Parse(evidenceType, "EMF"));
        int Freezing = Convert.ToInt32(Enum.Parse(evidenceType, "Freezing"));
        int Writing = Convert.ToInt32(Enum.Parse(evidenceType, "GhostWriting"));
        int Dots = Convert.ToInt32(Enum.Parse(evidenceType, "DOTS"));
        int SpiritBox = Convert.ToInt32(Enum.Parse(evidenceType, "SpiritBox"));
        int Orbs = Convert.ToInt32(Enum.Parse(evidenceType, "GhostOrbs"));

        void AssertGhostProofs(string name, int[] expected)
        {
            object[] args = { name, null };
            bool found = (bool)tryGet.Invoke(null, args);
            Assert.IsTrue(found, $"Expected '{name}' in GhostCatalog.");
            var evs = GetProp<Array>(args[1], "Evidences");
            int[] actual = evs.Cast<object>().Select(Convert.ToInt32).ToArray();
            CollectionAssert.AreEquivalent(expected, actual, $"{name} proofs mismatch.");
        }

        AssertGhostProofs("Tikbalang", new[] { Emf, SpiritBox, Orbs });
        AssertGhostProofs("Kapre", new[] { Emf, Freezing, Dots });
        AssertGhostProofs("Manananggal", new[] { Freezing, Writing, Orbs });
    }

    [Test]
    public void Catalog_AllTwentyGhostsHaveCompleteFolkloreDossiersAndEvidenceMap()
    {
        Type catalogType = ResolveRuntimeType("GhostCatalog");
        MethodInfo buildMap = catalogType.GetMethod("BuildJournalEvidenceMap", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(buildMap);

        var map = (Dictionary<string, HashSet<string>>)buildMap.Invoke(null, null);
        Assert.AreEqual(20, map.Count);

        foreach (object ghost in GetAllGhosts())
        {
            string id = GetProp<string>(ghost, "Id");
            string subtitle = GetProp<string>(ghost, "Subtitle");
            string sciTag = GetProp<string>(ghost, "ScientificTag");
            string lore = GetProp<string>(ghost, "Lore");
            string[] traits = GetProp<string[]>(ghost, "Traits");
            string tip = GetProp<string>(ghost, "Tip");
            string[] evKeys = GetProp<string[]>(ghost, "EvidenceKeys");

            Assert.IsFalse(string.IsNullOrWhiteSpace(subtitle), $"{id} missing Subtitle.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(sciTag), $"{id} missing ScientificTag.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(lore), $"{id} missing Lore.");
            Assert.IsNotNull(traits);
            Assert.GreaterOrEqual(traits.Length, 3, $"{id} should have at least 3 behavior traits.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(tip), $"{id} missing Tip.");

            Assert.IsTrue(map.TryGetValue(id, out var mappedKeys));
            CollectionAssert.AreEquivalent(evKeys, mappedKeys);
        }
    }
}
