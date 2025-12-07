// MaterialToolBar.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace BatchResourceTool
{
    public class MaterialToolbar : BaseToolbar
    {
        private List<string> _searchPaths = new List<string> { "Assets" };
        private string _nameFilter = "";
        private Shader _shaderFilter;
        private bool _includeSubfolders = true;
        private Shader _replaceShader; // 批量替换用的目标Shader

        public override void Init()
        {
            SearchResults.Clear();
            _replaceShader = null;
        }

        public override void OnGUI()
        {
            GUILayout.BeginVertical(); // 根布局容器
            
            
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
                    int index = i;
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label(_searchPaths[index], GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("×", GUILayout.Width(20)))
                        {
                            if (_searchPaths.Count > 1)
                            {
                                _searchPaths.RemoveAt(index);
                            }
                            else
                            {
                                EditorUtility.DisplayDialog("提示", "至少需要保留一个搜索路径！", "确定");
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            
            _includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", _includeSubfolders);

            // 2. 筛选条件
            GUILayout.Space(10);
            GUILayout.Label("筛选条件", EditorStyles.boldLabel);
            _nameFilter = EditorGUILayout.TextField("名称包含", _nameFilter);
            _shaderFilter = (Shader)EditorGUILayout.ObjectField("Shader 筛选", _shaderFilter, typeof(Shader), false);

            // 3. 批量操作
            GUILayout.Space(10);
            GUILayout.Label("批量操作", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            {
                _replaceShader = (Shader)EditorGUILayout.ObjectField("目标Shader", _replaceShader, typeof(Shader), false, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("批量替换", GUILayout.Width(100)))
                {
                    BatchReplaceShader();
                }
            }
            GUILayout.EndHorizontal();

            // 4. 功能按钮
            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("执行搜索", GUILayout.Width(180), GUILayout.Height(30)))
                {
                    SearchMaterials();
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical(); // 根布局结束
        }

        private void AddSelectedFoldersFromProject()
        {
            if (Selection.assetGUIDs == null || Selection.assetGUIDs.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在Project窗口选择文件夹", "确定");
                return;
            }

            int addedCount = 0;
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                if (Directory.Exists(path))
                {
                    string unityPath = path.Replace("\\", "/");
                    if (!_searchPaths.Contains(unityPath))
                    {
                        _searchPaths.Add(unityPath);
                        addedCount++;
                    }
                }
            }

            if (addedCount > 0)
            {
                EditorUtility.DisplayDialog("成功", $"已添加 {addedCount} 个文件夹到搜索路径", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "未找到可添加的有效文件夹（已排除重复路径和文件）", "确定");
            }
        }

        // 搜索符合条件的材质
        private void SearchMaterials()
        {
            SearchResults.Clear();
            var validPaths = new List<string>();

            // 验证所有路径
            foreach (var path in _searchPaths)
            {
                string fullPath = Path.Combine(Application.dataPath, path.Replace("Assets/", ""));
                if (Directory.Exists(fullPath))
                {
                    validPaths.Add(path);
                }
            }

            if (validPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("错误", "所有搜索路径都不存在！", "确定");
                return;
            }

            // 查找所有.mat文件
            foreach (var path in validPaths)
            {
                string[] materialPaths = Directory.GetFiles(path, "*.mat", 
                    _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                foreach (string matPath in materialPaths)
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null) continue;

                    // 应用筛选条件
                    bool nameMatch = string.IsNullOrEmpty(_nameFilter) || mat.name.Contains(_nameFilter, System.StringComparison.OrdinalIgnoreCase);
                    bool shaderMatch = _shaderFilter == null || mat.shader == _shaderFilter;

                    if (nameMatch && shaderMatch && !SearchResults.Contains(mat))
                    {
                        SearchResults.Add(mat);
                    }
                }
            }

            EditorUtility.DisplayDialog("搜索完成", $"找到 {SearchResults.Count} 个符合条件的材质", "确定");
        }

        // 批量替换材质的Shader
        private void BatchReplaceShader()
        {
            if (SearchResults.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先执行搜索获取材质列表！", "确定");
                return;
            }

            if (_replaceShader == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的目标Shader！", "确定");
                return;
            }

            if (EditorUtility.DisplayDialog("确认替换", $"是否将 {SearchResults.Count} 个材质的Shader替换为：\n{_replaceShader.name}？", "确认", "取消"))
            {
                int successCount = 0;
                foreach (var item in SearchResults)
                {
                    if (item is Material mat)
                    {
                        Undo.RecordObject(mat, "Batch Replace Shader");
                        mat.shader = _replaceShader;
                        EditorUtility.SetDirty(mat);
                        successCount++;
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("替换完成", $"成功替换 {successCount} 个材质的Shader", "确定");
            }
        }
    }
}                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       