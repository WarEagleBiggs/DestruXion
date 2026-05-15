using Destruxion.Voxels;
using UnityEditor;
using UnityEngine;

namespace Destruxion.Editor.Voxels
{
    [CustomEditor(typeof(VoxelGenerationMaster))]
    public sealed class VoxelGenerationMasterEditor : UnityEditor.Editor
    {
        SerializedProperty meshVoxelModels;
        SerializedProperty textVoxelModels;
        SerializedProperty voxelSize;
        SerializedProperty resolutionMode;
        SerializedProperty targetResolution;
        SerializedProperty algorithm;
        SerializedProperty surfaceThickness;
        SerializedProperty bakedShadingStrength;
        SerializedProperty cubeColorVariation;
        SerializedProperty fillSolid;
        SerializedProperty hideSourceRenderers;
        SerializedProperty outputMode;
        SerializedProperty damageProfile;
        SerializedProperty voxelMaterial;
        SerializedProperty applySettingsBeforeBake;
        SerializedProperty includeInactiveWhenFinding;

        void OnEnable()
        {
            meshVoxelModels = serializedObject.FindProperty("meshVoxelModels");
            textVoxelModels = serializedObject.FindProperty("textVoxelModels");
            voxelSize = serializedObject.FindProperty("voxelSize");
            resolutionMode = serializedObject.FindProperty("resolutionMode");
            targetResolution = serializedObject.FindProperty("targetResolution");
            algorithm = serializedObject.FindProperty("algorithm");
            surfaceThickness = serializedObject.FindProperty("surfaceThickness");
            bakedShadingStrength = serializedObject.FindProperty("bakedShadingStrength");
            cubeColorVariation = serializedObject.FindProperty("cubeColorVariation");
            fillSolid = serializedObject.FindProperty("fillSolid");
            hideSourceRenderers = serializedObject.FindProperty("hideSourceRenderers");
            outputMode = serializedObject.FindProperty("outputMode");
            damageProfile = serializedObject.FindProperty("damageProfile");
            voxelMaterial = serializedObject.FindProperty("voxelMaterial");
            applySettingsBeforeBake = serializedObject.FindProperty("applySettingsBeforeBake");
            includeInactiveWhenFinding = serializedObject.FindProperty("includeInactiveWhenFinding");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Voxel Models", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(meshVoxelModels, new GUIContent("Mesh Voxel Models"));
            EditorGUILayout.PropertyField(textVoxelModels, new GUIContent("Text Voxel Models"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shared Build Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(voxelSize, new GUIContent("Voxel Size"));
            EditorGUILayout.PropertyField(resolutionMode, new GUIContent("Resolution Mode"));
            if ((MeshVoxelResolutionMode)resolutionMode.enumValueIndex == MeshVoxelResolutionMode.TargetMaxDimension)
                EditorGUILayout.PropertyField(targetResolution, new GUIContent("Resolution"));
            EditorGUILayout.PropertyField(algorithm, new GUIContent("Algorithm"));
            EditorGUILayout.PropertyField(surfaceThickness, new GUIContent("Surface Thickness"));
            EditorGUILayout.PropertyField(bakedShadingStrength, new GUIContent("Baked Shading"));
            EditorGUILayout.PropertyField(cubeColorVariation, new GUIContent("Cube Color Variation"));
            EditorGUILayout.PropertyField(fillSolid, new GUIContent("Fill Solid Watertight Mesh"));
            EditorGUILayout.PropertyField(hideSourceRenderers, new GUIContent("Hide Source Mesh"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(outputMode, new GUIContent("Output Mode"));
            if ((VoxelOutputMode)outputMode.enumValueIndex == VoxelOutputMode.Destructible)
                EditorGUILayout.PropertyField(damageProfile, new GUIContent("Break Style"));
            EditorGUILayout.PropertyField(voxelMaterial, new GUIContent("Voxel Material"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(applySettingsBeforeBake, new GUIContent("Apply Settings Before Bake"));
            EditorGUILayout.PropertyField(includeInactiveWhenFinding, new GUIContent("Find Inactive Models"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find Voxel Models"))
                    FindVoxelModels((VoxelGenerationMaster)target);

                if (GUILayout.Button("Apply Settings"))
                    ApplySettings((VoxelGenerationMaster)target);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear All"))
                    ClearAll((VoxelGenerationMaster)target);

                if (GUILayout.Button("Bake"))
                    BakeAll((VoxelGenerationMaster)target);
            }
        }

        static void FindVoxelModels(VoxelGenerationMaster master)
        {
            Undo.RecordObject(master, "Find Voxel Models");
            master.MeshVoxelModels.Clear();
            master.TextVoxelModels.Clear();

            var findMode = master.IncludeInactiveWhenFinding ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            foreach (var reconstructor in Object.FindObjectsByType<MeshVoxelReconstructor>(findMode))
            {
                if (reconstructor != null)
                    master.MeshVoxelModels.Add(reconstructor);
            }

            foreach (var reconstructor in Object.FindObjectsByType<VoxelReconstructor>(findMode))
            {
                if (reconstructor != null)
                    master.TextVoxelModels.Add(reconstructor);
            }

            EditorUtility.SetDirty(master);
        }

        static void ApplySettings(VoxelGenerationMaster master)
        {
            Undo.RecordObject(master, "Apply Voxel Generation Settings");
            for (var i = 0; i < master.MeshVoxelModels.Count; i++)
                ApplyMeshSettings(master, master.MeshVoxelModels[i]);

            for (var i = 0; i < master.TextVoxelModels.Count; i++)
                ApplyTextSettings(master, master.TextVoxelModels[i]);
        }

        static void BakeAll(VoxelGenerationMaster master)
        {
            if (master.ApplySettingsBeforeBake)
                ApplySettings(master);

            for (var i = 0; i < master.MeshVoxelModels.Count; i++)
            {
                var reconstructor = master.MeshVoxelModels[i];
                if (reconstructor == null)
                    continue;

                Undo.RecordObject(reconstructor, "Bake Mesh Voxel Model");
                reconstructor.Reconstruct();
                EditorUtility.SetDirty(reconstructor.gameObject);
            }

            for (var i = 0; i < master.TextVoxelModels.Count; i++)
            {
                var reconstructor = master.TextVoxelModels[i];
                if (reconstructor == null)
                    continue;

                Undo.RecordObject(reconstructor, "Bake Text Voxel Model");
                reconstructor.Reconstruct();
                EditorUtility.SetDirty(reconstructor.gameObject);
            }
        }

        static void ClearAll(VoxelGenerationMaster master)
        {
            for (var i = 0; i < master.MeshVoxelModels.Count; i++)
            {
                var reconstructor = master.MeshVoxelModels[i];
                if (reconstructor == null)
                    continue;

                Undo.RecordObject(reconstructor, "Clear Mesh Voxel Model");
                reconstructor.ClearGeneratedChildren();
                EditorUtility.SetDirty(reconstructor.gameObject);
            }

            for (var i = 0; i < master.TextVoxelModels.Count; i++)
            {
                var reconstructor = master.TextVoxelModels[i];
                if (reconstructor == null)
                    continue;

                Undo.RecordObject(reconstructor, "Clear Text Voxel Model");
                reconstructor.ClearGeneratedChildren();
                EditorUtility.SetDirty(reconstructor.gameObject);
            }
        }

        static void ApplyMeshSettings(VoxelGenerationMaster master, MeshVoxelReconstructor reconstructor)
        {
            if (reconstructor == null)
                return;

            var serializedReconstructor = new SerializedObject(reconstructor);
            SetFloat(serializedReconstructor, "voxelSize", master.VoxelSize);
            SetEnum(serializedReconstructor, "resolutionMode", (int)master.ResolutionMode);
            SetInt(serializedReconstructor, "targetResolution", master.TargetResolution);
            SetEnum(serializedReconstructor, "algorithm", (int)master.Algorithm);
            SetInt(serializedReconstructor, "surfaceThickness", master.SurfaceThickness);
            SetFloat(serializedReconstructor, "bakedShadingStrength", master.BakedShadingStrength);
            SetFloat(serializedReconstructor, "cubeColorVariation", master.CubeColorVariation);
            SetBool(serializedReconstructor, "fillSolid", master.FillSolid);
            SetBool(serializedReconstructor, "hideSourceRenderers", master.HideSourceRenderers);
            SetEnum(serializedReconstructor, "outputMode", (int)master.OutputMode);
            SetEnum(serializedReconstructor, "damageProfile", (int)master.DamageProfile);
            SetObject(serializedReconstructor, "voxelMaterial", master.VoxelMaterial);
            serializedReconstructor.ApplyModifiedProperties();
            EditorUtility.SetDirty(reconstructor);
        }

        static void ApplyTextSettings(VoxelGenerationMaster master, VoxelReconstructor reconstructor)
        {
            if (reconstructor == null)
                return;

            var serializedReconstructor = new SerializedObject(reconstructor);
            SetFloat(serializedReconstructor, "voxelSize", master.VoxelSize);
            SetEnum(serializedReconstructor, "damageProfile", (int)master.DamageProfile);
            SetObject(serializedReconstructor, "voxelMaterial", master.VoxelMaterial);
            serializedReconstructor.ApplyModifiedProperties();
            EditorUtility.SetDirty(reconstructor);
        }

        static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        static void SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.intValue = value;
        }

        static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        static void SetEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.enumValueIndex = value;
        }

        static void SetObject(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }
    }
}
