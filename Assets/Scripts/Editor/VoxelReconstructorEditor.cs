using Destruxion.Voxels;
using UnityEditor;
using UnityEngine;

namespace Destruxion.Editor.Voxels
{
    [CustomEditor(typeof(VoxelReconstructor))]
    public sealed class VoxelReconstructorEditor : UnityEditor.Editor
    {
        SerializedProperty voxelTextFile;
        SerializedProperty voxelSize;
        SerializedProperty centerOnOrigin;
        SerializedProperty damageProfile;
        SerializedProperty voxelMaterial;

        void OnEnable()
        {
            voxelTextFile = serializedObject.FindProperty("voxelTextFile");
            voxelSize = serializedObject.FindProperty("voxelSize");
            centerOnOrigin = serializedObject.FindProperty("centerOnOrigin");
            damageProfile = serializedObject.FindProperty("damageProfile");
            voxelMaterial = serializedObject.FindProperty("voxelMaterial");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(voxelTextFile, new GUIContent("Voxel Text File"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(voxelSize, new GUIContent("Voxel Size"));
            EditorGUILayout.PropertyField(centerOnOrigin, new GUIContent("Center On Origin"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Destruction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(damageProfile, new GUIContent("Break Style"));
            EditorGUILayout.HelpBox(GetProfileSummary(damageProfile.enumValueIndex), MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(voxelMaterial, new GUIContent("Voxel Material"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            var reconstructor = (VoxelReconstructor)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reconstruct Voxels"))
                    Reconstruct(reconstructor);

                if (GUILayout.Button("Clear Voxels"))
                    Clear(reconstructor);
            }
        }

        static string GetProfileSummary(int profileIndex)
        {
            var profile = (VoxelDamageProfile)profileIndex;
            return profile switch
            {
                VoxelDamageProfile.LightChips => "Small bullet chips. Best for tiny impacts and careful testing.",
                VoxelDamageProfile.HeavyBreach => "Large breaches. Use for bigger cannon shots or stress testing.",
                _ => "Large bonded breakaway chunks, like drywall hit with a hammer. Projectile size still controls how big the break is."
            };
        }

        static void Reconstruct(VoxelReconstructor reconstructor)
        {
            Undo.RecordObject(reconstructor, "Reconstruct Voxels");
            reconstructor.Reconstruct();
            EditorUtility.SetDirty(reconstructor.gameObject);
        }

        static void Clear(VoxelReconstructor reconstructor)
        {
            Undo.RecordObject(reconstructor, "Clear Voxels");
            reconstructor.ClearGeneratedChildren();
            EditorUtility.SetDirty(reconstructor.gameObject);
        }
    }

    [CustomEditor(typeof(VoxelWorld))]
    public sealed class VoxelWorldEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var world = (VoxelWorld)target;
            EditorGUILayout.HelpBox(
                $"Generated voxel world. {world.ChunkCount} chunks are active. Change build and break settings on the parent Voxel Reconstructor, then press Reconstruct Voxels.",
                MessageType.Info);

            var reconstructor = world.GetComponentInParent<VoxelReconstructor>();
            using (new EditorGUI.DisabledScope(reconstructor == null))
            {
                if (GUILayout.Button("Select Voxel Reconstructor"))
                    Selection.activeObject = reconstructor;
            }
        }
    }
}
