// ShaderToolBar.cs (修正版本：使用 SelectedIndices)
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Object = UnityEngine.Object; 

namespace BatchResourceTool
{
    public class ShaderToolbar : BaseToolbar
    {
        private List<string> _searchPaths = new List<string> { "Assets" };
        private string _nameFilter = "";
        private bool _includeSubfolders = true;
        private bool _onlyBuiltIn = false; 
        private bool _onlySRP = false;     
        
        // 使用 BaseToolbar 的 SelectedIndices 属性

        public override void Init()
        {
            SearchResults.Clear();
            SelectedIndices.Clear(); // 修复: 使用 SelectedIndices
        }

        public override void OnGUI()
        {
            GUILayout.BeginVertical(); 
            
            
            // 添加文件夹按钮
            if (GUILayout.Button("添加文件夹", GUILayout.Width(180)))
            {
                AddSelectedFoldersFromProject();
            }
            
            // 显示已添加的文件夹列表
            if (_searchPaths.Count > 0)
            {
                GUILayout.Label("已添加的搜索路径:", EditorStyles.miniBoldLabel);
                for (int i = 0; i < _searchPaths.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(_searchPaths[i], EditorStyles.miniLabel);
                    if (GUILayout.Button("×", GUILayout.Width(20)))
                    {
                        _searchPaths.RemoveAt(i);
                        break; 
                    }
                    GUILayout.EndHorizontal();
                }
            }

            // 2. 筛选设置
            _nameFilter = EditorGUILayout.TextField("名称包含", _nameFilter);
            _includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", _includeSubfolders);
            _onlyBuiltIn = EditorGUILayout.Toggle("仅内置/标准Shader", _onlyBuiltIn);
            _onlySRP = EditorGUILayout.Toggle("仅SRP（URP/HDRP）", _onlySRP);
            
            GUILayout.Space(10);
            
            // 3. 搜索按钮
            if (GUILayout.Button("执行搜索", GUILayout.Height(32)))
            {
                SearchShaders();
            }
            
            GUILayout.Space(10);

            // 4. 赋值功能
            DrawAssignButton();

            GUILayout.EndVertical();
        }

        private void AddSelectedFoldersFromProject()
        {
            if (Selection.assetGUIDs == null || Selection.assetGUIDs.Length == 0) return;
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) && !_searchPaths.Contains(path))
                    _searchPaths.Add(path);
            }
        }

        private void SearchShaders()
        {
            SearchResults.Clear();
            SelectedIndices.Clear(); // 修复: 使用 SelectedIndices

            var allShaders = new List<Shader>();

            if (_onlyBuiltIn)
            {
                allShaders.Add(Shader.Find("Standard"));
                allShaders.Add(Shader.Find("Standard (Specular setup)"));
            }
            else
            {
                var guidsList = new List<string>();
                string[] searchFolders = _searchPaths.ToArray();
                
                // 查找所有Shader文件
                guidsList.AddRange(AssetDatabase.FindAssets("t:Shader", searchFolders));
                
                var uniquePaths = guidsList.Distinct()
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .ToList();

                foreach (var path in uniquePaths)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader != null)
                        allShaders.Add(shader);
                }
            }

            // 应用筛选条件
            foreach (var shader in allShaders.Distinct())
            {
                if (shader == null) continue;

                bool nameMatch = string.IsNullOrEmpty(_nameFilter) ||
                                 shader.name.Contains(_nameFilter, System.StringComparison.OrdinalIgnoreCase);

                if (!nameMatch) continue;

                bool srpMatch = !_onlySRP;
                if (_onlySRP)
                {
                    string path = AssetDatabase.GetAssetPath(shader);
                    if (path.Contains("Universal") || path.Contains("HDRP") || shader.name.Contains("Universal") || shader.name.Contains("HDRP"))
                    {
                        srpMatch = true;
                    }
                    else
                    {
                        srpMatch = false;
                    }
                }
                
                if (srpMatch)
                {
                    SearchResults.Add(shader);
                }
            }
            
            // 刷新主窗口，显示结果
            EditorWindow.GetWindow<BatchResourceWindow>(false)?.Repaint();
        }

        private void DrawAssignButton()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader 赋值操作:", EditorStyles.boldLabel, GUILayout.Width(120));
            
            // 状态显示：使用 SelectedIndices
            if (SelectedIndices.Count > 0)
            {
                // 修复: 只有在只选中一个Shader时才显示其名称
                if (SelectedIndices.Count == 1)
                {
                    int index = SelectedIndices.First();
                    if (index >= 0 && index < SearchResults.Count && SearchResults[index] is Shader selectedShader)
                    {
                        GUILayout.Label($"目标: {selectedShader.name}", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    GUILayout.Label($"已选中 {SelectedIndices.Count} 个 Shader (将使用第一个)", EditorStyles.miniLabel);
                }
            }
            else
            {
                GUILayout.Label("请在列表中选中一个目标 Shader", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("将Shader赋给选中材质", GUILayout.Width(180), GUILayout.Height(30)))
            {
                AssignToSelectedMaterials();
            }
            
            GUILayout.EndHorizontal();
        }

        private void AssignToSelectedMaterials()
        {
            // 修复: 使用 SelectedIndices
            if (SelectedIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在列表中选中一个目标Shader！", "确定");
                return;
            }

            // 总是使用选中的第一个 Shader 作为目标
            int targetIndex = SelectedIndices.First();
            Shader targetShader = SearchResults[targetIndex] as Shader; 

            if (targetShader == null)
            {
                EditorUtility.DisplayDialog("错误", "选中的不是有效的Shader！", "确定");
                return;
            }

            // 获取选中的材质
            var selectedMaterials = Selection.objects.OfType<Material>().ToList();
            if (selectedMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project窗口选中要赋值的材质！", "确定");
                return;
            }
            
            if (EditorUtility.DisplayDialog("确认赋值", $"是否将 {targetShader.name} 赋值给 {selectedMaterials.Count} 个选中的材质？", "确认", "取消"))
            {
                int successCount = 0;
                foreach (var mat in selectedMaterials)
                {
                    Undo.RecordObject(mat, "Assign Shader to Material");
                    mat.shader = targetShader; 
                    EditorUtility.SetDirty(mat);
                    successCount++;
                }

                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("赋值完成", $"成功为 {successCount} 个材质赋值Shader", "确定");
            }
        }
    }
}