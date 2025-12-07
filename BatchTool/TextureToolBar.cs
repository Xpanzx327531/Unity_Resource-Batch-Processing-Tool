using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Profiling;
using System;
using Object = UnityEngine.Object;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement; 
using GeneralToolFunction;


namespace BatchResourceTool
{
    [System.Serializable]
    public class TextureInfo
    {
        public Texture texture;
        public string assetPath;
        public string fileName;
        public int width;
        public int height;
        public long pixelCount => (long)width * height;
        public string sizeDisplay => $"{width}×{height}";
        public long memoryBytes;
        public string memoryDisplay => memoryBytes >= 1024 * 1024
            ? $"{memoryBytes / (1024f * 1024f):F2} MB"
            : memoryBytes >= 1024 ? $"{memoryBytes / 1024f:F1} KB" : $"{memoryBytes} B";

        public TextureImporterType importerType;
        public TextureImporterShape shape;
        public string typeDisplay;
        public int referenceCount;
    }
    
    public enum TextureSortFilterMode
    {
        None = 0,
        SizeAscending,
        SizeDescending,
        SizeSpecific,
        TypeFilter,
        FolderFilter,
        ReferenceCountAsc,
        ReferenceCountDesc,
        ReferenceCountZero 
    }

    public class TextureToolbar : BaseToolbar
    {
        private readonly List<string> _searchPaths = new List<string> { "Assets" };
        private string _nameFilter = ""; // 保留名称过滤字段
        
        public List<TextureInfo> AllTextures = new List<TextureInfo>();
        public Dictionary<Texture, int> _refCountDict = new Dictionary<Texture, int>();
        private bool hasSelectedCategory = false;
        
        public TextureSortFilterMode _currentMode = TextureSortFilterMode.None;
        public string CurrentViewName { get; private set; } = "全部贴图"; 
        private int _dragStartIndex = -1;
        private Vector2 _dragStartPosition;
        public string _currentFilterValue = ""; 
        private int _lastClickedIndex = -1; // 添加这个字段


        public override void Init()
        {
            SearchResults.Clear();
            AllTextures.Clear();
            _refCountDict.Clear();
            hasSelectedCategory = false;
            
            SelectedIndices.Clear(); 
            
            _currentMode = TextureSortFilterMode.None;
            _currentFilterValue = "";
            CurrentViewName = "全部贴图";
        }

        public override void OnGUI()
        {
            GUILayout.BeginVertical();
            DrawHeader();
            GUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            if (GUILayout.Button("添加文件夹", GUILayout.Width(180))) AddSelectedFolders();

            for (int i = _searchPaths.Count - 1; i >= 0; i--)
            {
                GUILayout.BeginHorizontal(GUILayout.Width(200));
                GUILayout.Label(_searchPaths[i], GUILayout.ExpandWidth(true));
                if (GUILayout.Button("×", GUILayout.Width(20)) && _searchPaths.Count > 1)
                    _searchPaths.RemoveAt(i);
                GUILayout.EndHorizontal();
            }


            GUILayout.Space(10);

            GUILayout.Space(10);
            if (GUILayout.Button("执行搜索（刷新）", GUILayout.Height(32)))
                SearchTextures();
        }

        private void AddSelectedFolders()
        {
            if (Selection.assetGUIDs == null || Selection.assetGUIDs.Length == 0) return;
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) && !_searchPaths.Contains(path))
                    _searchPaths.Add(path);
            }
        }

        private void SearchTextures()
        {
            AllTextures.Clear();
            _refCountDict.Clear();
            SearchResults.Clear();
            hasSelectedCategory = false;
            _currentMode = TextureSortFilterMode.None; 
            _currentFilterValue = "";
            SelectedIndices.Clear();
            _nameFilter = ""; // 重置名称过滤器

            EditorUtility.DisplayProgressBar("搜索贴图", "扫描中...", 0);

            var candidates = new List<TextureInfo>();
            var paths = new HashSet<string>();
            
            var guidsList = new List<string>();
            foreach (var folder in _searchPaths)
            {
                guidsList.AddRange(AssetDatabase.FindAssets("t:Texture", new[] { folder }));
            }
            var uniqueGuids = guidsList.Distinct().ToList();


            foreach (var guid in uniqueGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path); 
                
                if (tex == null || string.IsNullOrEmpty(path)) continue;
                if (!(tex is Texture2D) && !(tex is Cubemap)) continue;
                
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) continue; 

                candidates.Add(new TextureInfo
                {
                    texture = tex, 
                    assetPath = path,
                    fileName = tex.name,
                    width = tex.width, 
                    height = tex.height, 
                    memoryBytes = Profiler.GetRuntimeMemorySizeLong(tex),
                    importerType = importer.textureType,
                    shape = importer.textureShape,
                    typeDisplay = GetTypeName(importer),
                });
                paths.Add(path);
            }

            // 引用统计 (保持不变)
            var allMatGuids = AssetDatabase.FindAssets("t:Material");
            foreach (var g in allMatGuids)
            {
                var matPath = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat?.shader == null) continue;

                HashSet<Texture> texturesInCurrentMaterial = new HashSet<Texture>();

                int count = mat.shader.GetPropertyCount(); 
                for (int i = 0; i < count; i++)
                {
                    if (mat.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) 
                        continue;

                    var prop = mat.shader.GetPropertyName(i); 
                    if (!mat.HasProperty(prop)) continue;
                    
                    if (mat.GetTexture(prop) is Texture t)
                    {
                        if (paths.Contains(AssetDatabase.GetAssetPath(t)))
                        {
                            if (texturesInCurrentMaterial.Add(t))
                            {
                                _refCountDict[t] = _refCountDict.GetValueOrDefault(t) + 1;
                            }
                        }
                    }
                }
            }

            foreach (var info in candidates)
                info.referenceCount = _refCountDict.GetValueOrDefault(info.texture);

            AllTextures.AddRange(candidates);

            EditorUtility.ClearProgressBar();
            
            BatchResourceWindow window = EditorWindow.GetWindow<BatchResourceWindow>(false);
            if(window != null)
            {
                ApplyFilter(TextureSortFilterMode.None, "", window); // 应用默认筛选
            }
            
            RepaintWindow();
        }


        private void RepaintWindow()
        {
            BatchResourceWindow window = EditorWindow.GetWindow<BatchResourceWindow>(false);
            if (window != null)
            {
                window.Repaint();
            }
        }

        public string GetTypeName(TextureImporter importer)
        {
            if (importer.textureShape == TextureImporterShape.TextureCube) return "cubemap"; 
            return importer.textureType switch
            {
                TextureImporterType.NormalMap => "normal",
                TextureImporterType.Sprite => "sprite",
                TextureImporterType.Lightmap => "lightmap",
                _ => "default"
            };
        }

        private void RebuildResults(List<TextureInfo> list, BatchResourceWindow window)
        {
            SearchResults.Clear();
            foreach (var info in list) SearchResults.Add(info); 
            SelectedIndices.Clear(); 
            _lastClickedIndex = -1;
            hasSelectedCategory = true;
            
            if (window != null)
            {
                window.ResetPagination(); 
                window.Repaint();
            }
        }
        
        private void SelectAllCurrentResults()
        {
            SelectedIndices.Clear(); 
            for (int i = 0; i < SearchResults.Count; i++)
            {
                SelectedIndices.Add(i);
            }
            
            Selection.objects = SearchResults.OfType<TextureInfo>().Select(info => info.texture).ToArray();

            RepaintWindow();
        }

        public void DrawResultHierarchy(BatchResourceWindow window)
        {
            GUILayout.BeginVertical();
            {
                GUILayout.Space(10);

                if (AllTextures == null || AllTextures.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("请先执行搜索", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(200));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.FlexibleSpace();

                    GUILayout.EndVertical(); 
                    return;
                }

                // 2. 核心筛选区 (包含视图信息、全选、排列方式)
                GUILayout.BeginHorizontal();
                {
                    DrawIntegratedFilterPopup(window); 
                    GUILayout.FlexibleSpace();
                }
                GUILayout.EndHorizontal();

                // === 新增名称搜索框 (位于视图信息和列表头部之间) ===
                if (hasSelectedCategory)
                {
                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    
                    GUILayout.Label("名称搜索:", GUILayout.Width(70));
                    string oldFilter = _nameFilter;
                    _nameFilter = EditorGUILayout.TextField(_nameFilter, GUILayout.ExpandWidth(true));
                    
                    // 当名称过滤器改变时，立即重新应用筛选
                    if (oldFilter != _nameFilter)
                    {
                        // 保持当前的排序/筛选模式，重新应用所有筛选条件
                        ApplyFilter(_currentMode, _currentFilterValue, window); 
                    }
                    
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5); 
                }
                // =============================================


                if (hasSelectedCategory)
                {
                    // 列表头部
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Space(30); 
                        GUILayout.Label("名称", EditorStyles.boldLabel, GUILayout.Width(220));
                        GUILayout.Label("尺寸", EditorStyles.boldLabel, GUILayout.Width(100));
                        GUILayout.Label("引用次数", EditorStyles.boldLabel, GUILayout.Width(80));
                        GUILayout.Label("类型", EditorStyles.boldLabel, GUILayout.Width(100));
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();

                    if (SearchResults.Count > 0)
                    {
                        int startIndex = window.GetStartIndex();
                        int endIndex = window.GetEndIndex(SearchResults.Count);

                        for (int i = startIndex; i < endIndex; i++)
                        {
                            if (SearchResults[i] is TextureInfo info)
                            {
                                DrawTextureListItem(info, i, window); 
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Space(20);
                        GUILayout.Label("暂无匹配的贴图", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawIntegratedFilterPopup(BatchResourceWindow window)
        {
            if (hasSelectedCategory)
            {
                // 视图信息和选中数量
                string viewInfoText = $"视图: {CurrentViewName}  ({SelectedIndices.Count}/{SearchResults.Count} 张)";
                GUILayout.Label(viewInfoText, EditorStyles.boldLabel); 

                if (SearchResults.Count > 0)
                {
                    if (SelectedIndices.Count < SearchResults.Count) 
                    {
                        if (GUILayout.Button("全选", GUILayout.Width(60)))
                        {
                            SelectAllCurrentResults();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("取消全选", GUILayout.Width(80))) 
                        {
                            SelectedIndices.Clear();
                            Selection.objects = Array.Empty<UnityEngine.Object>();
                            RepaintWindow();
                        }
                    }
                }
                
                GUILayout.Space(10);
            }
            else
            {
                GUILayout.Label("搜索结果：共", GUILayout.Width(70));
                string totalText = AllTextures.Count > 0 ? $"{AllTextures.Count} 张" : "0 张";
                GUILayout.Label(totalText, EditorStyles.boldLabel, GUILayout.Width(50));
            }
            
            // 下拉菜单按钮
            if (GUILayout.Button("排列方式 ↓", EditorStyles.toolbarDropDown, GUILayout.Width(80)))
            {
                GenericMenu menu = new GenericMenu();

                var sizes = AllTextures.Select(t => Mathf.Max(t.width, t.height))
                                    .Distinct()
                                    .OrderByDescending(sz => sz)
                                    .ToList();
                
                // 尺寸排序
                AddMenuItem(menu, "尺寸/降序 (全部)", TextureSortFilterMode.SizeDescending, "", window);
                AddMenuItem(menu, "尺寸/升序 (全部)", TextureSortFilterMode.SizeAscending, "", window);

                // 尺寸筛选
                var sizeCounts = AllTextures.GroupBy(t => Mathf.Max(t.width, t.height)).ToDictionary(g => g.Key, g => g.Count());
                foreach (var size in sizes)
                {
                    // O(1) 字典查找代替 O(N) 全表遍历
                    if (sizeCounts.TryGetValue(size, out int count))
                    {
                        AddMenuItem(menu, $"尺寸/具体尺寸/{size}x{size} ({count} 张)", TextureSortFilterMode.SizeSpecific, size.ToString(), window);
                    }
                }
                menu.AddSeparator("");
                var distinctTypes = AllTextures.Select(t => t.typeDisplay)
                                            .Distinct()
                                            .OrderBy(type => type)
                                            .ToList();
                
                var typeCounts = AllTextures.GroupBy(t => t.typeDisplay, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase); 
                    
                foreach (var type in distinctTypes)
                {
                    // O(1) 字典查找代替 O(N) 全表遍历
                    if (typeCounts.TryGetValue(type, out int count))
                    {
                        AddMenuItem(menu, $"贴图类型/{type} ({count} 张)", TextureSortFilterMode.TypeFilter, type, window);
                    }
                }
                menu.AddSeparator("");

                var folderGroups = AllTextures.Select(t => System.IO.Path.GetDirectoryName(t.assetPath))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .GroupBy(p => p) // GroupBy the path string
                    .Select(g => new { Folder = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Folder)
                    .ToList();
                
                foreach (var item in folderGroups)
                {
                    // 直接使用一次 GroupBy 得到的 Count
                    string displayFolder = item.Folder.Replace("Assets/", "");
                    AddMenuItem(menu, $"所在文件夹/{displayFolder} ({item.Count} 张)", TextureSortFilterMode.FolderFilter, item.Folder, window);
                }
                menu.AddSeparator("");

                int zeroRefCount = AllTextures.Count(t => t.referenceCount == 0);
                AddMenuItem(menu, "引用情况/引用次数降序", TextureSortFilterMode.ReferenceCountDesc, "", window);
                AddMenuItem(menu, "引用情况/引用次数升序", TextureSortFilterMode.ReferenceCountAsc, "", window);
                AddMenuItem(menu, $"引用情况/未被引用 ({zeroRefCount} 张)", TextureSortFilterMode.ReferenceCountZero, "0", window);
                
                menu.AddSeparator("");
                AddMenuItem(menu, "重置筛选/显示全部", TextureSortFilterMode.None, "", window);

                menu.ShowAsContext();
            }
        }
        
        private void AddMenuItem(GenericMenu menu, string path, TextureSortFilterMode mode, string value, BatchResourceWindow window)
        {
            bool isChecked = (_currentMode == mode && _currentFilterValue == value);

            menu.AddItem(new GUIContent(path), isChecked, () => ApplyFilter(mode, value, window));
        }

        public void ApplyFilter(TextureSortFilterMode mode, string value, BatchResourceWindow window)
        {
            if (AllTextures.Count == 0 && mode != TextureSortFilterMode.None) return;

            _currentMode = mode;
            _currentFilterValue = value;
            
            // 1. 应用主要筛选模式，从全部贴图开始筛选
            List<TextureInfo> filteredList = AllTextures.ToList(); 

            switch (mode)
            {
                case TextureSortFilterMode.SizeSpecific:
                    if (int.TryParse(value, out int size))
                    {
                        filteredList = AllTextures.Where(t => Mathf.Max(t.width, t.height) == size).ToList();
                        CurrentViewName = $"尺寸 {size}x{size}";
                    }
                    else
                    {
                        goto case TextureSortFilterMode.None; 
                    }
                    break;
                case TextureSortFilterMode.TypeFilter:
                    filteredList = AllTextures.Where(t => t.typeDisplay.Equals(value, StringComparison.OrdinalIgnoreCase)).ToList();
                    CurrentViewName = $"类型 {value}";
                    break;
                case TextureSortFilterMode.FolderFilter:
                    filteredList = AllTextures.Where(t => System.IO.Path.GetDirectoryName(t.assetPath) == value).ToList();
                    CurrentViewName = $"文件夹 {value.Replace("Assets/", "")}";
                    break;
                case TextureSortFilterMode.ReferenceCountZero: 
                    filteredList = AllTextures.Where(t => t.referenceCount == 0).ToList();
                    CurrentViewName = "未被引用";
                    break;
                case TextureSortFilterMode.None:
                    CurrentViewName = "全部贴图";
                    break;
                default:
                    break;
            }
            
            // 2. 应用名称过滤器 (无论主筛选模式是什么，都应用名称过滤)
            if (!string.IsNullOrEmpty(_nameFilter))
            {
                filteredList = filteredList.Where(t => t.fileName.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }


            // 3. 应用排序
            IEnumerable<TextureInfo> sortedList = filteredList;
            
            bool isPureSort = mode is TextureSortFilterMode.SizeAscending or TextureSortFilterMode.SizeDescending 
                           or TextureSortFilterMode.ReferenceCountAsc or TextureSortFilterMode.ReferenceCountDesc;

            switch (mode)
            {
                case TextureSortFilterMode.SizeAscending:
                    sortedList = filteredList.OrderBy(t => t.pixelCount);
                    CurrentViewName = isPureSort ? "尺寸升序" : CurrentViewName + " (尺寸升序)";
                    break;
                case TextureSortFilterMode.SizeDescending:
                    sortedList = filteredList.OrderByDescending(t => t.pixelCount);
                    CurrentViewName = isPureSort ? "尺寸降序" : CurrentViewName + " (尺寸降序)";
                    break;
                case TextureSortFilterMode.ReferenceCountAsc:
                    sortedList = filteredList.OrderBy(t => t.referenceCount);
                    CurrentViewName = isPureSort ? "引用次数升序" : CurrentViewName + " (引用次数升序)";
                    break;
                case TextureSortFilterMode.ReferenceCountDesc:
                    sortedList = filteredList.OrderByDescending(t => t.referenceCount);
                    CurrentViewName = isPureSort ? "引用次数降序" : CurrentViewName + " (引用次数降序)";
                    break;
            }

            RebuildResults(sortedList.ToList(), window);
        }
       //列表项绘制
        private void DrawTextureListItem(TextureInfo info, int index, BatchResourceWindow window)
        {
            bool isSelected = SelectedIndices.Contains(index);

            // --- 1. 样式设置 ---
            var itemStyle = new GUIStyle(EditorStyles.label);
            if (index % 2 == 0)
                itemStyle.normal.background = MakeTexCached(1, 1, EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.9f, 0.9f, 0.9f));
            else
                itemStyle.normal.background = MakeTexCached(1, 1, new Color(0.1f, 0.1f, 0.1f));

            if (isSelected)
            {
                Color selColor = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.35f, 0.6f) : new Color(0.3f, 0.5f, 0.8f);
                itemStyle.normal.background = MakeTexCached(1, 1, selColor);
                itemStyle.normal.textColor = Color.white;
            }
            itemStyle.padding = new RectOffset(8, 8, 4, 4);

            // --- 2. 绘制内容 ---
            GUILayout.BeginHorizontal(itemStyle, GUILayout.Height(28));
            {
                GUILayout.Space(4);
                // 缩略图
                Texture thumbnail = AssetPreview.GetMiniThumbnail(info.texture);
                GUILayout.Label(thumbnail, GUILayout.Width(24), GUILayout.Height(24));
                GUILayout.Space(2);

                // 信息文本
                GUILayout.Label(info.fileName, GUILayout.Width(220));
                GUILayout.Label(info.sizeDisplay, GUILayout.Width(100));
                GUILayout.Label($"引用: {info.referenceCount}", GUILayout.Width(80));
                GUILayout.Label(info.typeDisplay, GUILayout.Width(100));
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            // --- 3. 获取交互区域 ---
            Rect itemRect = GUILayoutUtility.GetLastRect();

            // --- 4. 交互逻辑 (补全部分) ---
            Event e = Event.current;

            // A. 处理拖拽启动
            if (e.type == EventType.MouseDown && itemRect.Contains(e.mousePosition) && e.button == 0)
            {
                _dragStartIndex = index;
                _dragStartPosition = e.mousePosition;
                // 注意：这里不要 Use()，否则后面的点击选择逻辑无法执行
            }

            if (e.type == EventType.MouseDrag && _dragStartIndex == index && Vector2.Distance(_dragStartPosition, e.mousePosition) > 6f)
            {
                var selectedTextures = SelectedIndices
                    .Where(i => i < SearchResults.Count)
                    .Select(i => SearchResults[i] as TextureInfo)
                    .Where(inf => inf != null)
                    .Select(inf => inf.texture as Object)
                    .Where(o => o != null)
                    .ToArray();

                if (selectedTextures.Length > 0)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("BatchToolDrag", "1");
                    DragAndDrop.objectReferences = selectedTextures;
                    DragAndDrop.StartDrag(selectedTextures.Length > 1 ? $"拖动 {selectedTextures.Length} 张贴图" : selectedTextures[0].name);
                    _dragStartIndex = -1;
                    e.Use();
                }
            }

            // B. 处理点击选择 (核心补全)
            if (e.type == EventType.MouseDown && itemRect.Contains(e.mousePosition))
            {
                if (e.button == 0) // 左键
                {
                    bool ctrl = e.control || e.command;
                    bool shift = e.shift;

                    if (shift && SelectedIndices.Count > 0)
                    {
                        int last = _lastClickedIndex != -1 ? _lastClickedIndex : (SelectedIndices.Count > 0 ? SelectedIndices.Max() : index);
                        SelectedIndices.Clear();
                        int start = Mathf.Min(last, index);
                        int end = Mathf.Max(last, index);
                        for (int i = start; i <= end; i++) SelectedIndices.Add(i);
                    }
                    else if (ctrl)
                    {
                        if (SelectedIndices.Contains(index))
                            SelectedIndices.Remove(index);
                        else
                            SelectedIndices.Add(index);
                        _lastClickedIndex = index;
                    }
                    else 
                    {
                        if (!SelectedIndices.Contains(index))
                        {
                            SelectedIndices.Clear();
                            SelectedIndices.Add(index);
                        }
                        
                        _lastClickedIndex = index; 
                        
                        if (e.clickCount == 2) 
                        {
                            Selection.activeObject = info.texture; // 设置全局选中对象（定位）
                            EditorGUIUtility.PingObject(info.texture); // Ping（黄色高亮）
                        }
                    }                    
                    // 强制重绘
                    if (window != null) window.Repaint();
                    e.Use();
                }
                else if (e.button == 1) // 右键
                {
                    if (!SelectedIndices.Contains(index))
                    {
                        SelectedIndices.Clear();
                        SelectedIndices.Add(index);
                        _lastClickedIndex = index;
                        if (window != null) window.Repaint();
                    }
                    e.Use();
                }
            }

            // 清理拖拽状态
            if (e.type == EventType.MouseUp || e.type == EventType.Repaint)
            {
                if (_dragStartIndex != -1 && !e.mousePosition.IsNear(_dragStartPosition))
                    _dragStartIndex = -1;
            }

            // 分割线
            if (index < window.GetEndIndex(SearchResults.Count) - 1)
            {
                Rect lineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1));
                if (e.type == EventType.Repaint)
                {
                    Handles.BeginGUI();
                    Handles.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                    Handles.DrawLine(new Vector2(lineRect.x, lineRect.y), new Vector2(lineRect.xMax, lineRect.y));
                    Handles.EndGUI();
                }
            }
        }        
        private readonly Dictionary<Color, Texture2D> _bgCache = new Dictionary<Color, Texture2D>();
        private Texture2D MakeTexCached(int w, int h, Color col)
        {
            if (_bgCache.TryGetValue(col, out var cached) && cached != null)
                return cached;

            var t = new Texture2D(w, h, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.DontSave;
            var cols = new Color[w * h];
            for (int i = 0; i < cols.Length; i++) cols[i] = col;
            t.SetPixels(cols);
            t.Apply();
            _bgCache[col] = t;
            return t;
        }

        public override void RemoveItemAt(int index)
        {
            if (index < 0 || index >= SearchResults.Count) return;
            var info = SearchResults[index] as TextureInfo;
            if (info != null)
            {
                if (EditorUtility.DisplayDialog("确认删除", $"是否永久删除资源：{info.fileName}？\n路径：{info.assetPath}", "删除", "取消"))
                {
                    AssetDatabase.DeleteAsset(info.assetPath);
                    AssetDatabase.Refresh();
                    
                    AllTextures.RemoveAll(x => x.texture == info.texture);
                    
                    ApplyFilter(_currentMode, _currentFilterValue, EditorWindow.GetWindow<BatchResourceWindow>(false));
                }
                
            }
        }
       
        // ====================== 批量压缩格式（极简版）======================
        public void BatchChangeCompression()
        {
            var textures = GetSelectedTextures(); // 现在返回的是待处理区的
            if (textures.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "待处理资源区中没有贴图", "OK");
                return;
            }

            TextureCompressionWindow.ShowWindow(textures, () =>
            {
                var window = EditorWindow.GetWindow<BatchResourceWindow>();
                window?.Repaint();
            });
        }

        // ====================== 批量最大尺寸（极简版）======================
        public void BatchSetMaxSize()
        {
            var textures = GetSelectedTextures(); // 现在返回的是待处理区的
            if (textures.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "待处理资源区中没有贴图", "OK");
                return;
            }

            TextureMaxSizeWindow.ShowWindow(textures, () =>
            {
                var window = EditorWindow.GetWindow<BatchResourceWindow>();
                window?.Repaint();
            });
        }

        // 辅助：获取选中贴图
        private List<TextureInfo> GetSelectedTextures()
        {
            var window = EditorWindow.GetWindow<BatchResourceWindow>();
            if (window == null) return new List<TextureInfo>();

            var pendingTextures = window.PendingResources
                .OfType<Texture2D>()
                .Select(t => new { texture = t, path = AssetDatabase.GetAssetPath(t) })
                .Where(x => !string.IsNullOrEmpty(x.path))
                .ToList();

            return pendingTextures.Select(x => new TextureInfo
            {
                texture = x.texture,
                assetPath = x.path,
                fileName = x.texture.name
            }).ToList();
        }

        public void RefreshPendingOnly()
        {
            var pendingPaths = EditorWindow.GetWindow<BatchResourceWindow>()
                .PendingResources
                .Select(o => AssetDatabase.GetAssetPath(o))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet();

            foreach (var info in AllTextures.Where(i => pendingPaths.Contains(i.assetPath)))
            {
                info.memoryBytes = Profiler.GetRuntimeMemorySizeLong(info.texture);

                var importer = AssetImporter.GetAtPath(info.assetPath) as TextureImporter;
                if (importer != null)
                {
                    info.width = info.texture.width;
                    info.height = info.texture.height;
                    info.typeDisplay = GetTypeName(importer); 
                }
            }

            // 保持当前筛选视图
            var window = EditorWindow.GetWindow<BatchResourceWindow>();
            ApplyFilter(_currentMode, _currentFilterValue, window);
        }


    }
     public static class Vector2Extensions
    {
        public static bool IsNear(this Vector2 a, Vector2 b, float tolerance = 6f)
        {
            // 优化：用平方距离避免开根号，性能更好
            return Vector2.SqrMagnitude(a - b) <= tolerance * tolerance;
        }
    }

    // ============== 压缩格式选择窗口（极简）==============
    class TextureCompressionWindow : EditorWindow
    {
        static List<TextureInfo> textures;
        static Action onComplete;
        int selected = 2; // 默认 Normal

        public static void ShowWindow(List<TextureInfo> list, Action callback)
        {
            textures = list; onComplete = callback;
            var win = CreateInstance<TextureCompressionWindow>();
            win.titleContent = new GUIContent("批量压缩格式");
            win.minSize = win.maxSize = new Vector2(300, 120);
            win.ShowUtility();
        }

        void OnGUI()
        {
            GUILayout.Space(15);
            GUILayout.Label("选择压缩质量：", EditorStyles.boldLabel);
            selected = EditorGUILayout.Popup(selected, new[] { "None (无压缩)", "Low Quality", "Normal Quality", "High Quality" });

            GUILayout.Space(10);
            if (GUILayout.Button("应用", GUILayout.Height(30)))
            {
                Apply((TextureImporterCompression)selected);
                onComplete?.Invoke();
                Close();
            }
        }

        // 压缩格式窗口 Apply
        static void Apply(TextureImporterCompression mode)
        {
            var targets = textures.Select(t => t.texture).ToArray();
            Undo.RecordObjects(targets, "Batch Compression");

            var pathsToImport = new List<string>();

            foreach (var info in textures)
            {
                var importer = AssetImporter.GetAtPath(info.assetPath) as TextureImporter;
                if (!importer) continue;

                // 设置所有平台
                foreach (var p in new[] { "Standalone", "iPhone", "Android", "WebGL" })
                {
                    var s = importer.GetPlatformTextureSettings(p);
                    s.overridden = true;
                    s.textureCompression = mode;
                    importer.SetPlatformTextureSettings(s);
                }
                var def = importer.GetDefaultPlatformTextureSettings();
                def.textureCompression = mode;
                importer.SetPlatformTextureSettings(def);

                pathsToImport.Add(info.assetPath);
                UniversalImportWatcher.WatchPaths(new[] { info.assetPath });
            }

            // 统一批量导入（关键！）
            if (pathsToImport.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                EditorUtility.DisplayProgressBar("批量设置尺寸", "正在导入，请稍候...", 0.0f);
                var pathQueue = new Queue<string>(pathsToImport);
                int totalCount = pathQueue.Count;
                int importedCount = 0;
                void ProcessNextBatch() 
                {
                    // 每次只导入 N 个（例如 1 个），以避免单次操作时间过长
                    int batchSize = 1; 
                    for (int i = 0; i < batchSize && pathQueue.Count > 0; i++)
                    {
                        string p = pathQueue.Dequeue();
                        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                        importedCount++;
                        
                        // 更新进度条
                        float progress = (float)importedCount / totalCount;
                        EditorUtility.DisplayProgressBar("批量设置尺寸", $"正在导入: {p}", progress);
                    }

                    if (pathQueue.Count > 0)
                    {
                        // 队列未空，通过 delayCall 继续下一帧执行
                        EditorApplication.delayCall += ProcessNextBatch;
                    }
                    else
                    {
                        // 队列为空，结束编辑模式并清理
                        AssetDatabase.StopAssetEditing();
                        EditorUtility.ClearProgressBar();
                    }
                }
                EditorApplication.delayCall += ProcessNextBatch;
            }
        }
    }
        // ============== 最大尺寸选择窗口==============
    class TextureMaxSizeWindow : EditorWindow
    {
        static List<TextureInfo> textures;
        static Action onComplete;
        int selected = 3; // 默认 2048

        public static void ShowWindow(List<TextureInfo> list, Action callback)
        {
            textures = list; onComplete = callback;
            var win = CreateInstance<TextureMaxSizeWindow>();
            win.titleContent = new GUIContent("批量最大尺寸");
            win.minSize = win.maxSize = new Vector2(280, 120);
            win.ShowUtility();
        }

        void OnGUI()
        {
            GUILayout.Space(15);
            GUILayout.Label("选择最大尺寸：", EditorStyles.boldLabel);
            selected = EditorGUILayout.Popup(selected, new[] { "256", "512", "1024", "2048", "4096" });

            GUILayout.Space(10);
            if (GUILayout.Button("应用", GUILayout.Height(30)))
            {
                int size = new[] { 256, 512, 1024, 2048, 4096 }[selected];
                Apply(size);
                onComplete?.Invoke();
                Close();
            }
        }
        // 最大尺寸窗口 Apply
        static void Apply(int maxSize)
        {
            var targets = textures.Select(t => t.texture).ToArray();
            Undo.RecordObjects(targets, $"Batch MaxSize {maxSize}");

            var pathsToImport = new List<string>();

            foreach (var info in textures)
            {
                var importer = AssetImporter.GetAtPath(info.assetPath) as TextureImporter;
                if (!importer) continue;

                foreach (var p in new[] { "Standalone", "iPhone", "Android", "WebGL" })
                {
                    var s = importer.GetPlatformTextureSettings(p);
                    s.overridden = true;
                    s.maxTextureSize = maxSize;
                    importer.SetPlatformTextureSettings(s);
                }
                var def = importer.GetDefaultPlatformTextureSettings();
                def.maxTextureSize = maxSize;
                importer.SetPlatformTextureSettings(def);

                pathsToImport.Add(info.assetPath);
                UniversalImportWatcher.WatchPaths(new[] { info.assetPath });
            }
            // 统一批量导入（关键！）
            if (pathsToImport.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                EditorUtility.DisplayProgressBar("批量设置尺寸", "正在导入，请稍候...", 0.0f);
                var pathQueue = new Queue<string>(pathsToImport);
                int totalCount = pathQueue.Count;
                int importedCount = 0;
                void ProcessNextBatch() 
                {
                    // 每次只导入 N 个（例如 1 个），以避免单次操作时间过长
                    int batchSize = 1; 
                    for (int i = 0; i < batchSize && pathQueue.Count > 0; i++)
                    {
                        string p = pathQueue.Dequeue();
                        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                        importedCount++;
                        
                        // 更新进度条
                        float progress = (float)importedCount / totalCount;
                        EditorUtility.DisplayProgressBar("批量设置尺寸", $"正在导入: {p}", progress);
                    }

                    if (pathQueue.Count > 0)
                    {
                        // 队列未空，通过 delayCall 继续下一帧执行
                        EditorApplication.delayCall += ProcessNextBatch;
                    }
                    else
                    {
                        // 队列为空，结束编辑模式并清理
                        AssetDatabase.StopAssetEditing();
                        EditorUtility.ClearProgressBar();
                    }
                }
                EditorApplication.delayCall += ProcessNextBatch;
            }
        }
    }
}

