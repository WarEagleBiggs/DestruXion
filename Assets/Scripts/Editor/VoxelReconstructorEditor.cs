using Destruxion.Voxels;
using UnityEditor;
using UnityEngine;

namespace Destruxion.Editor.Voxels
{
    [CustomEditor(typeof(VoxelReconstructor))]
    public sealed class VoxelReconstructorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

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

        static void Reconstruct(VoxelReconstructor reconstructor)
        {
            Undo.RegisterFullObjectHierarchyUndo(reconstructor.gameObject, "Reconstruct Voxels");
            reconstructor.Reconstruct();
            EditorUtility.SetDirty(reconstructor.gameObject);
        }

        static void Clear(VoxelReconstructor reconstructor)
        {
            Undo.RegisterFullObjectHierarchyUndo(reconstructor.gameObject, "Clear Voxels");
            reconstructor.ClearGeneratedChildren();
            EditorUtility.SetDirty(reconstructor.gameObject);
        }
    }
}
