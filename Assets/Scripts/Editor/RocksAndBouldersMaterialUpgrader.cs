using UnityEditor;
using UnityEngine;

namespace Destruxion.Editor.Tools
{
    public static class RocksAndBouldersMaterialUpgrader
    {
        const string MaterialRoot = "Assets/Rocks and Boulders 2/Rocks/Source/Materials";
        const string MenuPath = "Tools/DESTRUXion/Fix Rocks and Boulders Materials";

        [MenuItem(MenuPath)]
        public static void UpgradeRocksAndBouldersMaterials()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogError("Could not find Universal Render Pipeline/Lit. Make sure URP is installed and active before upgrading rock materials.");
                return;
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
            int upgradedCount = 0;

            foreach (string guid in materialGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    continue;
                }

                UpgradeMaterial(material, urpLit, path);
                upgradedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Upgraded {upgradedCount} Rocks and Boulders material(s) to URP/Lit.");
        }

        static void UpgradeMaterial(Material material, Shader urpLit, string assetPath)
        {
            string oldShaderName = material.shader != null ? material.shader.name : string.Empty;
            bool isLegacyBlend = oldShaderName.StartsWith("Enviro/", System.StringComparison.Ordinal) ||
                                 assetPath.Contains("/Legacy/");

            Texture baseMap = isLegacyBlend
                ? FirstTexture(material, "_MainTex2", "_BaseMap", "_MainTex")
                : FirstTexture(material, "_BaseMap", "_MainTex", "_MainTex2");
            string baseMapProperty = isLegacyBlend && HasTexture(material, "_MainTex2") ? "_MainTex2" : FirstTextureProperty(material, "_BaseMap", "_MainTex", "_MainTex2");

            Texture normalMap = isLegacyBlend
                ? FirstTexture(material, "_BumpMap2", "_BumpMap")
                : FirstTexture(material, "_BumpMap", "_BumpMap2");
            Texture occlusionMap = FirstTexture(material, "_OcclusionMap");
            Texture maskMap = FirstTexture(material, "_MetallicGlossMap", "_SpecGlossMap");

            Color baseColor = FirstColor(material, Color.white, "_BaseColor", "_Color");
            float bumpScale = FirstFloat(material, 1f, "_BumpScale");
            float smoothness = Mathf.Clamp01(FirstFloat(material, 0.35f, "_Smoothness", "_Glossiness"));
            float occlusionStrength = Mathf.Clamp01(FirstFloat(material, 1f, "_OcclusionStrength"));
            Vector2 tiling = GetTextureScale(material, baseMapProperty);
            Vector2 offset = GetTextureOffset(material, baseMapProperty);

            material.shader = urpLit;

            SetFloat(material, "_WorkflowMode", 1f);
            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", 2f);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_BumpScale", bumpScale);
            SetFloat(material, "_OcclusionStrength", occlusionStrength);

            SetColor(material, "_BaseColor", baseColor);
            SetColor(material, "_Color", baseColor);
            SetTexture(material, "_BaseMap", baseMap);
            SetTexture(material, "_MainTex", baseMap);
            SetTextureScaleAndOffset(material, "_BaseMap", tiling, offset);
            SetTextureScaleAndOffset(material, "_MainTex", tiling, offset);

            SetTexture(material, "_BumpMap", normalMap);
            SetKeyword(material, "_NORMALMAP", normalMap != null);
            MarkNormalMap(normalMap);

            SetTexture(material, "_OcclusionMap", occlusionMap);
            SetTexture(material, "_MetallicGlossMap", maskMap);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", maskMap != null);

            SetTexture(material, "_ParallaxMap", null);
            SetFloat(material, "_Parallax", 0f);
            SetKeyword(material, "_PARALLAXMAP", false);
            SetKeyword(material, "_DETAIL_MULX2", false);
            SetKeyword(material, "_SPECGLOSSMAP", false);

            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", "Opaque");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        static Texture FirstTexture(Material material, params string[] names)
        {
            foreach (string name in names)
            {
                if (material.HasProperty(name))
                {
                    Texture texture = material.GetTexture(name);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        static string FirstTextureProperty(Material material, params string[] names)
        {
            foreach (string name in names)
            {
                if (HasTexture(material, name))
                {
                    return name;
                }
            }

            return names.Length > 0 ? names[0] : string.Empty;
        }

        static bool HasTexture(Material material, string name)
        {
            return material.HasProperty(name) && material.GetTexture(name) != null;
        }

        static Color FirstColor(Material material, Color fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (material.HasProperty(name))
                {
                    return material.GetColor(name);
                }
            }

            return fallback;
        }

        static float FirstFloat(Material material, float fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (material.HasProperty(name))
                {
                    return material.GetFloat(name);
                }
            }

            return fallback;
        }

        static Vector2 GetTextureScale(Material material, string property)
        {
            return !string.IsNullOrEmpty(property) && material.HasProperty(property)
                ? material.GetTextureScale(property)
                : Vector2.one;
        }

        static Vector2 GetTextureOffset(Material material, string property)
        {
            return !string.IsNullOrEmpty(property) && material.HasProperty(property)
                ? material.GetTextureOffset(property)
                : Vector2.zero;
        }

        static void SetFloat(Material material, string name, float value)
        {
            if (material.HasProperty(name))
            {
                material.SetFloat(name, value);
            }
        }

        static void SetColor(Material material, string name, Color value)
        {
            if (material.HasProperty(name))
            {
                material.SetColor(name, value);
            }
        }

        static void SetTexture(Material material, string name, Texture texture)
        {
            if (material.HasProperty(name))
            {
                material.SetTexture(name, texture);
            }
        }

        static void SetTextureScaleAndOffset(Material material, string name, Vector2 scale, Vector2 offset)
        {
            if (!material.HasProperty(name))
            {
                return;
            }

            material.SetTextureScale(name, scale);
            material.SetTextureOffset(name, offset);
        }

        static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        static void MarkNormalMap(Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            {
                return;
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
    }
}
