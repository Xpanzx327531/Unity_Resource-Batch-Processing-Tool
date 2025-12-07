using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;
using System;
using GeneralToolFunction;
using BatchResourceTool;
using static BatchResourceTool.UIModule;

namespace BatchResourceTool
{
    public abstract class BaseToolbar
    {
        public List<object> SearchResults { get; protected set; } = new();
        public HashSet<int> SelectedIndices { get; protected set; } = new HashSet<int>();
        public List<string> SearchPaths { get; protected set; } = new();

        public abstract void Init();
        public abstract void OnGUI();

        public virtual void RemoveItemAt(int index)
        {
            if (index < 0 || index >= SearchResults.Count) return;
            var item = SearchResults[index];
            if (item is Object obj)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) return;

                if (EditorUtility.DisplayDialog("确认删除", $"是否永久删除资源：{obj.name}？\n路径：{assetPath}", "删除", "取消"))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    AssetDatabase.Refresh();

                    SearchResults.RemoveAt(index);
                    SelectedIndices.Remove(index);

                    var toMove = SelectedIndices.Where(i => i > index).ToList();
                    foreach (var i in toMove)
                    {
                        SelectedIndices.Remove(i);
                        SelectedIndices.Add(i - 1);
                    }
                }
            }
        }

        public virtual void AddSearchPaths(List<string> paths)
        {
            foreach (var path in paths)
                if (!string.IsNullOrEmpty(path) && !SearchPaths.Contains(path))
                    SearchPaths.Add(path);
        }

        public virtual void RemoveSearchPath(string path) => SearchPaths.Remove(path);
    }

    public class BatchResourceWindow : EditorWindow
    {
        public enum ToolbarType { Material, Shader, Texture }

        public ToolbarType _currentToolbar = ToolbarType.Material;
        public ToolbarType CurrentToolbar => _currentToolbar;
        public IReadOnlyDictionary<ToolbarType, BaseToolbar> Toolbars => _toolbars;
        public readonly Dictionary<ToolbarType, BaseToolbar> _toolbars = new();
        public int _pageSize = 20;
        public int _currentPage = 1;
        private int _totalPages => _toolbars.ContainsKey(_currentToolbar) && _toolbars[_currentToolbar].SearchResults != null
            ? Mathf.Max(1, Mathf.CeilToInt(_toolbars[_currentToolbar].SearchResults.Count / (float)_pageSize))
            : 1;

        private Vector2 scrollPosition = Vector2.zero;
        private GUIStyle _centeredBoldLabel;

        private Vector2 _pendingScrollPosition;
        private const float _pendingItemHeight = 40f;
        // 待处理资源区
        private readonly List<Object> _pendingResources = new List<Object>();
        public IReadOnlyList<Object> PendingResources => _pendingResources;
        private float _pendingAreaWidth = 300f;
        private const float _minPendingAreaWidth = 200f;
        public Rect _pendingAreaRect;
        private bool _isResizing = false;

        [MenuItem("Tools/批量资源处理工具（贴图+Shader+材质）")]
        public static void ShowWindow()
        {
            var window = GetWindow<BatchResourceWindow>("批量资源处理", true);
            window.minSize = new Vector2(600, 500);
        }

        private void OnEnable()
        {
            if (!_toolbars.ContainsKey(ToolbarType.Material))
                _toolbars.Add(ToolbarType.Material, new MaterialToolbar());
            if (!_toolbars.ContainsKey(ToolbarType.Shader))
                _toolbars.Add(ToolbarType.Shader, new ShaderToolbar());
            if (!_toolbars.ContainsKey(ToolbarType.Texture))
                _toolbars.Add(ToolbarType.Texture, new TextureToolbar());

            foreach (var toolbar in _toolbars.Values)
                toolbar.Init();
        }

        public void OnGUI()
        {
            Event currentEvent = Event.current;
            DrawToolbarSelector();
            GUILayout.Space(3);

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("搜索路径", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Space(5);
                GUILayout.BeginHorizontal(GUILayout.Width(_pendingAreaWidth));
                {
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);
                    {
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                {
                    DrawCurrentToolbarContent();

                    GUILayout.Space(10);

                    scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
                    DrawSearchResults();
                    GUILayout.EndScrollView();

                    GUILayout.Space(8);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (_toolbars.TryGetValue(_currentToolbar, out var toolbar))
                    {
                        UIModule.DrawPagination(ref _currentPage, ref _pageSize, toolbar.SearchResults.Count);
                    }                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();

                DrawSeparator();

                GUILayout.BeginVertical(GUILayout.Width(_pendingAreaWidth));
                {
                    UIModule.DrawPendingResourcesArea(_pendingResources, ref _pendingScrollPosition, _pendingItemHeight, this);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            HandleResizing();
            EditorListSelectionHelper.ReceiveDrop(_pendingAreaRect, _pendingResources, Repaint);
        }
        private void DrawPagination()
        {
            var toolbar = _toolbars.GetValueOrDefault(_currentToolbar);
            int count = toolbar?.SearchResults.Count ?? 0;

            UIModule.DrawPagination(ref _currentPage, ref _pageSize, toolbar?.SearchResults.Count ?? 0);
            if (GUI.changed)
                ResetPagination();
        }
        public void ResetPagination()
        {
            _currentPage = 1;
            scrollPosition = Vector2.zero;
        }

        public int GetStartIndex() => (_currentPage - 1) * _pageSize;
        public int GetEndIndex(int totalCount) => Mathf.Min(_currentPage * _pageSize, totalCount);

        private void DrawToolbarSelector()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(new GUIContent($"资源类型: {_currentToolbar}"), EditorStyles.toolbarDropDown, GUILayout.Width(150), GUILayout.Height(30)))
                ShowToolbarTypeMenu();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ShowToolbarTypeMenu()
        {
            var menu = new GenericMenu();
            foreach (ToolbarType type in System.Enum.GetValues(typeof(ToolbarType)))
            {
                string name = type.ToString();
                menu.AddItem(new GUIContent(name), _currentToolbar == type, () => OnToolbarTypeSelected(type));
            }
            menu.ShowAsContext();
        }

        private void OnToolbarTypeSelected(ToolbarType type)
        {
            if (_currentToolbar != type)
            {
                _currentToolbar = type;
                ResetPagination();
                Repaint();
            }
        }

        private void DrawCurrentToolbarContent()
        {
            if (_toolbars.TryGetValue(_currentToolbar, out var toolbar))
            {
                GUILayout.BeginVertical();
                toolbar.OnGUI();
                GUILayout.EndVertical();
            }
        }

        private void DrawSearchResults()
        {
            if (!_toolbars.TryGetValue(_currentToolbar, out var toolbar)) return;

            if (toolbar is TextureToolbar texTool)
            {
                texTool.DrawResultHierarchy(this);
                return;
            }

            // 其它类型继续走旧逻辑
            if (toolbar.SearchResults.Count == 0)
            {
                GUILayout.Label("暂无搜索结果", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            DrawGenericResults(toolbar);
        }
        private void DrawGenericResults(BaseToolbar toolbar)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"搜索结果：共 {toolbar.SearchResults.Count} 项 (已选中 {toolbar.SelectedIndices.Count} 项)", EditorStyles.boldLabel);
            if (GUILayout.Button("清空列表", GUILayout.Width(100)))
            {
                toolbar.SearchResults.Clear();
                toolbar.SelectedIndices.Clear();
            }
            GUILayout.EndHorizontal();

            int start = GetStartIndex();
            int end = GetEndIndex(toolbar.SearchResults.Count);

            for (int i = start; i < end; i++)
                DrawListItem(toolbar, i);
        }

        public void ShowGenericContextMenu(BaseToolbar toolbar, Object clickedObj, int index)
        {
            var menu = new GenericMenu();
            var selected = toolbar.SelectedIndices
                .Where(i => i < toolbar.SearchResults.Count)
                .Select(i => toolbar.SearchResults[i] as Object)
                .Where(o => o != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o)))
                .ToList();

            string title = selected.Count > 1 ? $"批量删除 {selected.Count} 个资源" : "删除资源";
            menu.AddItem(new GUIContent(title), false, () =>
            {
                if (!EditorUtility.DisplayDialog("确认删除", $"是否永久删除 {selected.Count} 个资源？", "删除", "取消")) return;

                foreach (var o in selected.OrderByDescending(o => toolbar.SearchResults.IndexOf(o)))
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(o));

                AssetDatabase.Refresh();
                toolbar.SelectedIndices.Clear();
                Repaint();
            });
            menu.ShowAsContext();
        }


        private void DrawSeparator()
        {
            Rect r = GUILayoutUtility.GetRect(5, float.MaxValue, 5, float.MaxValue, GUILayout.Width(5));
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.1f, 1f));

            Rect resize = r;
            resize.x -= 2; resize.width += 4;
            EditorGUIUtility.AddCursorRect(resize, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && resize.Contains(Event.current.mousePosition))
            {
                _isResizing = true;
                Event.current.Use();
            }
        }

        private void HandleResizing()
        {
            if (!_isResizing) return;
            if (Event.current.type == EventType.MouseDrag)
            {
                _pendingAreaWidth = position.width - Event.current.mousePosition.x;
                _pendingAreaWidth = Mathf.Clamp(_pendingAreaWidth, _minPendingAreaWidth, position.width - _minPendingAreaWidth);
                Repaint();
            }
            else if (Event.current.type == EventType.MouseUp)
                _isResizing = false;
        }
   
        public void ShowPendingContextMenu(int clickedIndex)
        {
            if (clickedIndex == -1 && _pendingResources.Count == 0)
            return; 
            GenericMenu menu = new GenericMenu();

            //=== 批量重命名 (调用 GeneralToolFunction 中的 BatchRenameWindow) ===
            menu.AddItem(new GUIContent("批量重命名..."), false, () =>
            {
                BatchRenameWindow.ShowWindow(_pendingResources.ToArray(), () =>
                {
                    Repaint();
                });
            });
            // === 批量开关 Mipmap (调用 BatchAssetOperations) ===
            var textures = _pendingResources.Where(o => o is Texture2D).Select(o => o as Texture2D).ToArray();
            if (textures.Length > 0)
            {
                menu.AddItem(new GUIContent("批量开关 Mipmap..."), false, () =>
                {
                    // 【重要修改】将 BatchTextureProcessor 替换为 BatchAssetOperations
                    BatchAssetOperations.BatchToggleMipmap(_pendingResources.ToArray(), () =>
                    {
                        Repaint(); 
                    });
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("批量开关 Mipmap（无贴图）"));
            }
            //=== 批量删除 (调用 BatchAssetOperations) ===
            string title = _pendingResources.Count > 1 
                ? $"删除这 {_pendingResources.Count} 个资源" 
                : "删除此资源";
            
            var resourcesToDelete = _pendingResources.ToArray(); 

            menu.AddItem(new GUIContent(title), false, () =>
                {
                    // 这里现在可以正确识别 BatchAssetOperations
                    BatchAssetOperations.BatchDeleteAssets(resourcesToDelete, () =>
                    {
                        // 删除成功后的回调：清空当前列表并刷新 UI
                        _pendingResources.Clear();
                        Repaint();
                    });
                });

            menu.ShowAsContext();
        }            
    
        public void DrawPendingItemWithContextMenu(Object obj, int index, Rect rect)
        {
            if (obj == null) return;

            // 背景
            Color bg = (index % 2 == 0)
                ? new Color(0.22f, 0.22f, 0.22f, 0.8f)
                : new Color(0.18f, 0.18f, 0.18f, 0.8f);
            EditorGUI.DrawRect(rect, bg);

            // 图标 + 名称 + 路径
            Rect iconR = new Rect(rect.x + 6, rect.y + 6, 32, 32);
            Texture icon = AssetPreview.GetMiniThumbnail(obj);
            if (icon) GUI.DrawTexture(iconR, icon);

            Rect nameR = new Rect(rect.x + 48, rect.y + 6, rect.width - 160, 20);
            GUI.Label(nameR, obj.name, EditorStyles.boldLabel);

            Rect pathR = new Rect(rect.x + 48, rect.y + 24, rect.width - 160, 16);
            string path = AssetDatabase.GetAssetPath(obj);
            string shortPath = path.Length > 60 ? "..." + path.Substring(path.Length - 57) : path;
            GUI.Label(pathR, shortPath, EditorStyles.miniLabel);

            // 移除按钮
            Rect removeR = new Rect(rect.xMax - 80, rect.y + 10, 70, 24);
            if (GUI.Button(removeR, "移除", EditorStyles.miniButton))
            {
                _pendingResources.RemoveAt(index);
                Repaint();
            }


        }
    }
}