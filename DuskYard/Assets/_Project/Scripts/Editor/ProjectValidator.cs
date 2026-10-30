using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// Batch-mode friendly checks: shader compile errors, missing materials/shaders, missing script references.
    /// Menu: DuskYard/Validate Project.  CLI: -executeMethod DuskYard.EditorTools.ProjectValidator.ValidateCli
    /// </summary>
    public static class ProjectValidator
    {
        [MenuItem("DuskYard/Validate Project")]
        public static bool Validate()
        {
            var report = new StringBuilder();
            int errors = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) { report.AppendLine($"[ERR] cannot load shader {path}"); errors++; continue; }
                var msgs = ShaderUtil.GetShaderMessages(shader);
                bool hasError = ShaderUtil.ShaderHasError(shader) || msgs.Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                report.AppendLine($"[{(hasError ? "ERR" : "OK ")}] {path} -> '{shader.name}' props:{shader.GetPropertyCount()}");
                foreach (var m in msgs) report.AppendLine($"      {m.severity}: {m.message} ({m.file}:{m.line})");
                if (hasError) errors++;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader" || ShaderUtil.ShaderHasError(mat.shader))
                {
                    report.AppendLine($"[ERR] material {path} has broken shader '{mat.shader?.name}'");
                    errors++;
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0) { report.AppendLine($"[ERR] {path}/{t.name}: {missing} missing scripts"); errors++; }
                    foreach (var r in t.GetComponents<Renderer>())
                        foreach (var m in r.sharedMaterials)
                            if (m == null && !(r is ParticleSystemRenderer)) { report.AppendLine($"[ERR] {path}/{t.name}: null material slot"); errors++; }
                }
            }

            report.Insert(0, $"==== DuskYard validation: {errors} error(s)\n");
            Debug.Log(report.ToString());
            return errors == 0;
        }

        public static void ValidateCli()
        {
            bool ok = Validate();
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
