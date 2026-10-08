using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Opus.Shell.Editor
{
    public static class OpusMaterialDiagTool
    {
        public static void BatchDiag()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/OrchardReach.unity", OpenSceneMode.Single);
            void Dump(Transform t, int depth)
            {
                var rend = t.GetComponent<Renderer>();
                string rendInfo = rend != null ? $" [Renderer bounds={rend.bounds}]" : "";
                Debug.Log($"[OPUS][DIAG]{new string(' ', depth*2)} {t.name} pos={t.position}{rendInfo}");
                for (int i=0;i<t.childCount;i++) Dump(t.GetChild(i), depth+1);
            }
            foreach (var root in scene.GetRootGameObjects()) Dump(root.transform, 0);
        }
    }
}
