using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pamahiin.EditModeTests
{
    public class UIThemeAuditTests
    {
        private const string ThemeUssPath = "Assets/Resources/UI/PamahiinTheme.uss";
        private const string RuntimeThemeTssPath = "Assets/Resources/UI/PamahiinRuntimeTheme.tss";
        private const string RuntimePanelSettingsPath = "Assets/Resources/UI/PamahiinPanelSettings.asset";
        private const string ScenePanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
        private const string ExorcismDialPath = "Assets/Resources/UI/Sprites/ExorcismDial.png";
        private const string TmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/PressStart2P-Regular SDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string SourceFontPath = "Assets/Fonts/PressStart2P-Regular.ttf";

        private static readonly string[] BannedUiGlyphs =
        {
            "\u25BA", // ►
            "\u2715", // ✕
            "\u2716", // ✖
            "\u2699", // ⚙
            "\uD83D\uDC65", // 👥
            "\uD83D\uDCD6", // 📖
            "\u26A0", // ⚠
            "\u25CF"  // ●
        };

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string ToProjectRelative(string fullPath)
        {
            string normalizedFull = Path.GetFullPath(fullPath).Replace('\\', '/');
            string normalizedRoot = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedFull.Substring(normalizedRoot.Length)
                : normalizedFull;
        }

        private static IEnumerable<string> EnumerateProjectFiles(string relativeDir, string searchPattern)
        {
            string fullDir = Path.Combine(ProjectRoot, relativeDir);
            if (!Directory.Exists(fullDir)) yield break;

            foreach (string file in Directory.GetFiles(fullDir, searchPattern, SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        [Test]
        public void AllUssFiles_EnforceZeroRadius_NoScale_NoBoxShadow_NoVT323_AndImportMasterTheme()
        {
            var ussFiles = EnumerateProjectFiles("Assets/UI", "*.uss")
                .Concat(EnumerateProjectFiles("Assets/Resources/UI", "*.uss"))
                .Distinct()
                .ToList();

            Assert.GreaterOrEqual(ussFiles.Count, 10, "Expected at least 10 USS files across Assets/UI and Assets/Resources/UI.");

            var radiusRx = new Regex(@"border(?:-\w+)*-radius\s*:\s*([^;]+);", RegexOptions.Compiled);
            var scaleRx = new Regex(@"(^|\s|,)scale(\s*:|\s*,|\s*;)", RegexOptions.Compiled);
            var fontStyleRx = new Regex(@"-unity-font-style\s*:\s*([^;]+);", RegexOptions.Compiled);

            foreach (string file in ussFiles)
            {
                string rel = ToProjectRelative(file);
                string content = File.ReadAllText(file);
                string[] lines = File.ReadAllLines(file);

                Assert.IsFalse(content.Contains("VT323"), $"USS file '{rel}' still references VT323.");
                Assert.IsFalse(content.Contains("box-shadow"), $"USS file '{rel}' contains unsupported box-shadow.");

                if (!string.Equals(rel, ThemeUssPath, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsTrue(content.Contains("PamahiinTheme.uss"),
                        $"USS file '{rel}' does not @import PamahiinTheme.uss.");
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;

                    Match rMatch = radiusRx.Match(trimmed);
                    if (rMatch.Success)
                    {
                        string val = rMatch.Groups[1].Value.Trim();
                        Assert.IsTrue(val == "0" || val == "0px",
                            $"USS '{rel}:{i + 1}' has non-zero radius '{val}'. All corners must be 0px.");
                    }

                    Assert.IsFalse(scaleRx.IsMatch(trimmed),
                        $"USS '{rel}:{i + 1}' uses scale transform/transition ('{trimmed}'), which blurs pixel art.");

                    Match fMatch = fontStyleRx.Match(trimmed);
                    if (fMatch.Success)
                    {
                        string val = fMatch.Groups[1].Value.Trim();
                        Assert.AreEqual("normal", val,
                            $"USS '{rel}:{i + 1}' uses '-unity-font-style: {val}' instead of 'normal'.");
                    }
                }
            }
        }

        [Test]
        public void ThemeAssets_AndPanelSettings_AreCreatedAndConfigured()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeUssPath),
                $"Missing master theme USS at '{ThemeUssPath}'.");

            var runtimeTheme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RuntimeThemeTssPath);
            Assert.IsNotNull(runtimeTheme, $"Missing runtime ThemeStyleSheet at '{RuntimeThemeTssPath}'.");

            var runtimePs = AssetDatabase.LoadAssetAtPath<PanelSettings>(RuntimePanelSettingsPath);
            Assert.IsNotNull(runtimePs, $"Missing runtime PanelSettings at '{RuntimePanelSettingsPath}'.");
            Assert.AreEqual(runtimeTheme, runtimePs.themeStyleSheet, "PamahiinPanelSettings must reference PamahiinRuntimeTheme.tss.");
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, runtimePs.scaleMode);
            Assert.AreEqual(new Vector2Int(1200, 800), runtimePs.referenceResolution);
            Assert.AreEqual(PanelScreenMatchMode.MatchWidthOrHeight, runtimePs.screenMatchMode);
            Assert.AreEqual(0f, runtimePs.match, 0.001f);

            var scenePs = AssetDatabase.LoadAssetAtPath<PanelSettings>(ScenePanelSettingsPath);
            Assert.IsNotNull(scenePs, $"Missing scene PanelSettings at '{ScenePanelSettingsPath}'.");
            Assert.AreEqual(runtimeTheme, scenePs.themeStyleSheet, "Assets/UI Toolkit/PanelSettings must reference PamahiinRuntimeTheme.tss.");
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, scenePs.scaleMode);
            Assert.AreEqual(new Vector2Int(1200, 800), scenePs.referenceResolution);

            var dialTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ExorcismDialPath);
            var dialSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ExorcismDialPath);
            Assert.IsNotNull(dialTex, $"Missing pixel dial texture at '{ExorcismDialPath}'.");
            Assert.IsNotNull(dialSprite, $"Missing pixel dial sprite import at '{ExorcismDialPath}'.");
            Assert.AreEqual(FilterMode.Point, dialTex.filterMode, "ExorcismDial.png must use FilterMode.Point.");

            var importer = AssetImporter.GetAtPath(ExorcismDialPath) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            Assert.IsNotNull(sourceFont, $"Missing PressStart2P source font at '{SourceFontPath}'.");

            var tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
            Assert.IsNotNull(tmpFont, $"Missing TMP pixel font asset at '{TmpFontPath}'.");
            Assert.AreEqual(sourceFont, tmpFont.sourceFontFile, "TMP_FontAsset must source PressStart2P-Regular.ttf.");

            var tmpSettingsObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TmpSettingsPath);
            Assert.IsNotNull(tmpSettingsObj);
            var so = new SerializedObject(tmpSettingsObj);
            var defaultProp = so.FindProperty("m_defaultFontAsset");
            Assert.IsNotNull(defaultProp);
            Assert.AreEqual(tmpFont, defaultProp.objectReferenceValue, "TMP Settings default font must be PressStart2P-Regular SDF.");
        }

        [Test]
        public void EndGameUI_HasSingleSourceOfTruthInResources_AndNoStaleSceneGuid()
        {
            string duplicateDir = Path.Combine(ProjectRoot, "Assets/UI/EndGame");
            Assert.IsFalse(Directory.Exists(duplicateDir), "Duplicate Assets/UI/EndGame directory must be removed.");

            Assert.IsNotNull(Resources.Load<VisualTreeAsset>("UI/EndGameUI"),
                "Resources.Load<VisualTreeAsset>('UI/EndGameUI') failed.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/UI/EndGameUI.uss"),
                "Missing Assets/Resources/UI/EndGameUI.uss.");

            var sceneFiles = EnumerateProjectFiles("Assets/Scenes", "*.unity")
                .Concat(EnumerateProjectFiles("Assets/_Recovery", "*.unity"));

            foreach (string sceneFile in sceneFiles)
            {
                string content = File.ReadAllText(sceneFile);
                string rel = ToProjectRelative(sceneFile);
                Assert.IsFalse(content.Contains("9b3fa342ba8acd04d833b192dba6015f"),
                    $"Scene '{rel}' still references deleted Assets/UI/EndGame/EndGameUI.uxml GUID.");
                Assert.IsFalse(content.Contains("25128ae442016f4478e64f0ffb350bc"),
                    $"Scene '{rel}' still references deleted Assets/UI/EndGame/EndGameUI.uss GUID.");
            }
        }

        [Test]
        public void UiAssetsAndScripts_UseOnlySupportedPressStart2PGlyphs_AndNoVT323()
        {
            var tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
            Assert.IsNotNull(tmpFont, "Run PamahiinUIThemeSetup to generate PressStart2P-Regular SDF before running font coverage checks.");

            var filesToScan = EnumerateProjectFiles("Assets/UI", "*.uxml")
                .Concat(EnumerateProjectFiles("Assets/Resources/UI", "*.uxml"))
                .Concat(EnumerateProjectFiles("Assets/Scripts/UI", "*.cs"))
                .Concat(new[]
                {
                    Path.Combine(ProjectRoot, "Assets/Scripts/ItemHoverIndicator.cs"),
                    Path.Combine(ProjectRoot, "Assets/Scripts/ThermometerItem.cs"),
                    Path.Combine(ProjectRoot, "Assets/Scripts/Equipment/SpiritBoxItem.cs")
                })
                .Distinct()
                .ToList();

            var usedNonAsciiChars = new HashSet<char>();

            foreach (string file in filesToScan)
            {
                Assert.IsTrue(File.Exists(file), $"Expected UI file '{file}' not found.");
                string rel = ToProjectRelative(file);
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("<!--") || trimmed.StartsWith("//")) continue;

                    Assert.IsFalse(line.Contains("VT323"), $"File '{rel}:{i + 1}' still references VT323.");

                    foreach (string banned in BannedUiGlyphs)
                    {
                        Assert.IsFalse(line.Contains(banned),
                            $"File '{rel}:{i + 1}' contains unsupported PressStart2P glyph '{banned}' in: {trimmed}");
                    }

                    foreach (char ch in line)
                    {
                        if (ch > 127)
                        {
                            usedNonAsciiChars.Add(ch);
                        }
                    }
                }
            }

            foreach (char ch in usedNonAsciiChars)
            {
                bool hasChar = tmpFont.HasCharacter(ch, false, true);
                Assert.IsTrue(hasChar,
                    $"PressStart2P-Regular SDF does not contain glyph U+{(int)ch:X4} ('{ch}') used in UI files.");
            }
        }

        [Test]
        public void RuntimeThemeUtility_ResolvesThemedPanelSettings_AndPixelFont()
        {
            Assembly gameAssembly = Assembly.Load("Assembly-CSharp");
            Assert.IsNotNull(gameAssembly);

            Type utilType = gameAssembly.GetType("PamahiinUIThemeUtility");
            Assert.IsNotNull(utilType, "Expected PamahiinUIThemeUtility class in Assembly-CSharp.");

            MethodInfo resolvePs = utilType.GetMethod("ResolveRuntimePanelSettings", BindingFlags.Public | BindingFlags.Static);
            MethodInfo resolveFont = utilType.GetMethod("ResolvePixelFont", BindingFlags.Public | BindingFlags.Static);
            MethodInfo resolveTmpFont = utilType.GetMethod("ResolvePixelTmpFont", BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(resolvePs);
            Assert.IsNotNull(resolveFont);
            Assert.IsNotNull(resolveTmpFont);

            var ps = resolvePs.Invoke(null, new object[] { null }) as PanelSettings;
            Assert.IsNotNull(ps, "ResolveRuntimePanelSettings(null) returned null.");
            Assert.IsNotNull(ps.themeStyleSheet, "Resolved runtime PanelSettings has no themeStyleSheet.");
            Assert.AreEqual(new Vector2Int(1200, 800), ps.referenceResolution);

            var font = resolveFont.Invoke(null, null) as Font;
            Assert.IsNotNull(font, "ResolvePixelFont() returned null.");
            Assert.IsTrue(font.name.Contains("PressStart2P"), $"Expected PressStart2P font, got '{font.name}'.");

            var tmpFont = resolveTmpFont.Invoke(null, null) as TMP_FontAsset;
            Assert.IsNotNull(tmpFont, "ResolvePixelTmpFont() returned null.");
            Assert.IsTrue(tmpFont.name.Contains("PressStart2P"), $"Expected PressStart2P TMP_FontAsset, got '{tmpFont.name}'.");
        }
    }
}
