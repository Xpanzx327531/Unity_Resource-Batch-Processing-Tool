// Assets/Editor/BatchTool/UIModule.cs
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;  
using Object = UnityEngine.Object;
using static BatchResourceTool.BatchResourceWindow;
using GeneralToolFunction;

namespace BatchResourceTool  /// 纯 UI 工具类
{
    
    public static class UIModule
    {
        private static Texture2D _darkBg;
        private static GUIStyle _centeredStyle;
        private static GUIStyle _pendingHeaderStyle;
        private static readonly GUILayoutOption _itemHeight = GUILayout.Height(40f);
        public static ToolbarType _currentToolbarType = ToolbarType.Material;
        private static event System.Action<ToolbarType> OnToolbarTypeChanged;

        static UIModule()
        {
            _centeredStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            _pendingHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.8f, 0.8f, 1f) : new Color(0.2f, 0.4f, 1f) }
            };
        }
       
        // ====================== 分页控件 ================================
        public static void DrawPagination(ref int currentPage, ref int pageSize, int totalItems)
        {
            if (totalItems <= 0)
            {
                EditorGUILayout.LabelField("暂无数据", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            int totalPages = Mathf.Max(1, (totalItems + pageSize - 1) / pageSize);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = currentPage > 1;
            if (GUILayout.Button("首页", GUILayout.Width(60))) currentPage = 1;
            if (GUILayout.Button("上一页", GUILayout.Width(70))) currentPage--;
            GUI.enabled = true;

            GUILayout.Label($"{currentPage} / {totalPages}", _centeredStyle, GUILayout.Width(100));

            GUI.enabled = currentPage < totalPages;
            if (GUILayout.Button("下一页", GUILayout.Width(70))) currentPage++;
            if (GUILayout.Button("末页", GUILayout.Width(60))) currentPage = totalPages;
            GUI.enabled = true;

            GUILayout.Space(20);
            GUILayout.Label("每页:", GUILayout.Width(40));
            int oldSize = pageSize;
            pageSize = EditorGUILayout.IntField(pageSize, GUILayout.Width(60));
            pageSize = Mathf.Clamp(pageSize, 5, 1000);
            if (oldSize != pageSize) currentPage = 1;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        // ===========================================================


        /// ===============批处理工作区=================
        /// <param name="pendingResources">资源列表</param>
        /// <param name="scrollPos">滚动位置（外部传入 ref）</param>
        /// <param name="onContextMenu">右键菜单回调（传 ShowPendingContextMenu）</param>
        public static void DrawPendingResourcesArea(
        List<Object> pendingResources,
        ref Vector2 pendingScrollPosition,
        float pendingItemHeight,
        BatchResourceWindow window)  
        {
            GUILayout.BeginVertical();
            {
                // ==================== 上半部分：功能按钮区 ====================
                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(200));
                {
                    GUILayout.Label("批量操作功能区", EditorStyles.boldLabel);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("批量压缩选中贴图", GUILayout.Height(28)))
                    {
                        if (window._toolbars[BatchResourceWindow.ToolbarType.Texture] is TextureToolbar texToolbar)
                            texToolbar.BatchChangeCompression();
                    }

                    if (GUILayout.Button("批量设置最大尺寸", GUILayout.Height(28)))
                    {
                        if (window._toolbars[BatchResourceWindow.ToolbarType.Texture] is TextureToolbar texToolbar)
                            texToolbar.BatchSetMaxSize();
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(4);
                }
                GUILayout.EndVertical();

                GUILayout.Space(8);

                // ==================== 标题栏（支持右键） ====================
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    EditorGUILayout.LabelField($"待处理资源 ({pendingResources.Count})", EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    GUIContent tipContent = new GUIContent(" 右键此处批处理 ");
                    Vector2 tipSize = EditorStyles.miniLabel.CalcSize(tipContent);
                    Rect tipRect = GUILayoutUtility.GetRect(tipSize.x + 12, 20, GUILayout.ExpandHeight(true));
                    GUI.Label(tipRect, tipContent, EditorStyles.miniLabel);

                    Event e = Event.current;
                    Rect fullHeaderRect = GUILayoutUtility.GetLastRect();
                    if (e.type == EventType.MouseDown && e.button == 1 && fullHeaderRect.Contains(e.mousePosition))
                    {
                        window.ShowPendingContextMenu(-1);
                        e.Use();
                    }

                    if (pendingResources.Count > 0 && GUILayout.Button("清空列表", GUILayout.Width(80)))
                    {
                        if (EditorUtility.DisplayDialog("确认清空", "是否清空待处理资源区？", "确定", "取消"))
                            pendingResources.Clear();
                    }
                }
                GUILayout.EndHorizontal();

                // ==================== 深色拖拽框 ====================
                var dropBoxStyle = new GUIStyle(GUI.skin.box);
                dropBoxStyle.normal.background = MakeDarkBackground();
                dropBoxStyle.padding = new RectOffset(12, 12, 12, 12);
                dropBoxStyle.border = new RectOffset(12, 12, 12, 12);

                GUILayout.BeginVertical(dropBoxStyle, GUILayout.ExpandHeight(true));
                {
                    Rect dropArea = GUILayoutUtility.GetRect(0, Mathf.Max(180, 200), GUILayout.ExpandHeight(true));
                    window._pendingAreaRect = dropArea;
                    window._pendingAreaRect = GUILayoutUtility.GetLastRect();
                    if (pendingResources.Count == 0)
                    {
                        GUI.Label(dropArea,
                            "将需要处理的资源拖拽到此处",
                            new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                            {
                                fontSize = 13,
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(0.75f, 0.75f, 0.75f, 0.9f) }
                            });
                    }
                    else
                    {
                        float viewWidth = dropArea.width - 2;

                        pendingScrollPosition = GUI.BeginScrollView(dropArea, pendingScrollPosition,
                            new Rect(0, 0, viewWidth, pendingItemHeight * pendingResources.Count + 20),
                            false, false);

                        Rect itemRect = new Rect(1, 1, viewWidth - 1, pendingItemHeight);
                        for (int i = 0; i < pendingResources.Count; i++)
                        {
                            if (pendingResources[i] != null)
                                window.DrawPendingItemWithContextMenu(pendingResources[i], i, itemRect);
                            itemRect.y += pendingItemHeight + 6;
                        }
                        GUI.EndScrollView();
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            // 最后设置整个区域的 Rect（用于拖拽接收）
            window._pendingAreaRect = GUILayoutUtility.GetLastRect();
        }
        private static Texture2D MakeDarkBackground()
        {
            if (_darkBg == null)
            {
                _darkBg = new Texture2D(1, 1);
                Color baseColor = EditorGUIUtility.isProSkin 
                    ? new Color(0.13f, 0.13f, 0.13f, 0.98f)
                    : new Color(0.22f, 0.22f, 0.22f, 0.98f);
                _darkBg.SetPixel(0, 0, baseColor);
                _darkBg.Apply();
            }
            return _darkBg;
        }
        // ==========================================================
 

 
        /// ===============列表绘制=================
        public  static void DrawListItem(BaseToolbar toolbar, int globalIndex)
        {
            var item = toolbar.SearchResults[globalIndex];
            bool isSelected = toolbar.SelectedIndices.Contains(globalIndex);

            // 1. 获取行高并预留空间 (Layout 阶段)
            float rowHeight = 28f;
            Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(rowHeight), GUILayout.ExpandWidth(true));

            // 2. 计算子控件区域 (不依赖 GUILayout，直接计算绝对坐标)
            Rect iconRect = new Rect(rowRect.x + 2, rowRect.y + 2, 24, 24);
            Rect labelRect = new Rect(rowRect.x + 30, rowRect.y, 300, rowHeight); // 名称区域
            Rect pathRect = new Rect(rowRect.x + 335, rowRect.y, rowRect.width - 335 - 70, rowHeight); // 路径区域
            Rect buttonRect = new Rect(rowRect.xMax - 65, rowRect.y + 3, 60, 22); // 删除按钮区域

            // 3. 事件处理 (在绘制之前或之后均可，但要在 Button 之前处理背景点击)
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            EditorListSelectionHelper.HandleRowInteraction(rowRect, new[] { buttonRect }, globalIndex, toolbar.SelectedIndices, toolbar.SearchResults);

            // 4. 绘制 (Repaint 阶段)
            if (e.type == EventType.Repaint)
            {
                // 绘制背景
                var bgStyle = new GUIStyle(EditorStyles.label);
                if (isSelected)
                {
                    Color sel = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.35f, 0.6f) : new Color(0.3f, 0.5f, 0.8f);
                    bgStyle.normal.background = MakeTex(1, 1, sel);
                    bgStyle.normal.textColor = Color.white;
                }
                else
                {
                     // 斑马纹
                    Color bgColor = (globalIndex % 2 == 0) 
                        ? (EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.9f, 0.9f, 0.9f))
                        : new Color(0.1f, 0.1f, 0.1f);
                    bgStyle.normal.background = MakeTex(1, 1, bgColor);
                }
                bgStyle.Draw(rowRect, false, false, false, false);
            }

            // 5. 绘制内容 (使用 GUI 方法，位置基于上面计算的 Rect)
            if (item is Object obj)
            {
                // 图标
                Texture icon = AssetPreview.GetMiniThumbnail(obj);
                if (icon) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

                // 名称和内存 (使用 Label 样式)
                string labelText = obj.name;
                if (obj is Texture2D tex)
                {
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                    string memStr = bytes >= 1024 * 1024 ? $"{bytes / (1024f * 1024f):F2} MB" :
                                    bytes >= 1024 ? $"{bytes / 1024f:F1} KB" : $"{bytes} B";
                    labelText += $" [{memStr}]";
                }
                
                // 设置文字颜色（如果是选中状态）
                var labelStyle = new GUIStyle(EditorStyles.label);
                if(isSelected) labelStyle.normal.textColor = Color.white;
                labelStyle.alignment = TextAnchor.MiddleLeft;
                GUI.Label(labelRect, labelText, labelStyle);

                // 路径
                string path = AssetDatabase.GetAssetPath(obj);
                string shortPath = path.Length > 40 ? "..." + path.Substring(path.Length - 37) : path;
                GUI.Label(pathRect, shortPath, EditorStyles.miniLabel);
            }
            else
            {
                GUI.Label(labelRect, item?.ToString() ?? "", EditorStyles.label);
            }

            // 6. 删除按钮 (放在最后绘制，因为它是交互控件)
            if (GUI.Button(buttonRect, "删除"))
            {
                toolbar.RemoveItemAt(globalIndex);
                GUIUtility.ExitGUI(); // 必须调用，防止布局错误
            }
        }        

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            var cols = new Color[w * h];
            for (int i = 0; i < cols.Length; i++) cols[i] = col;
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        // ==========================================================



        // ===============通用的分页结果列表绘制器=================

        public delegate void DrawItemDelegate<T>(T item, int index, bool isSelected, Rect rect);

        public static void DrawGenericResultList<T>(
            IList<T> items,
            HashSet<int> selectedIndices,
            string searchFilter,
            System.Action<string> onSearchChanged,
            DrawItemDelegate<T> onDrawItem,
            System.Action<int> onDoubleClick = null,
            System.Action<GenericMenu> onContextMenu = null,
            string emptyMessage = "暂无搜索结果",
            int currentPage = 1,
            int pageSize = 20,
            params GUILayoutOption[] headerOptions)
        {
            if (items == null || selectedIndices == null) return;

            // === 1. 名称搜索框 ===
            if (items.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("名称搜索:", GUILayout.Width(70));
                string oldFilter = searchFilter;
                string newFilter = EditorGUILayout.TextField(searchFilter, GUILayout.ExpandWidth(true));
                if (oldFilter != newFilter)
                    onSearchChanged?.Invoke(newFilter);
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
            }

            // === 2. 空状态 ===
            if (items.Count == 0)
            {
                GUILayout.Label(emptyMessage, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // === 3. 表头 ===
            if (headerOptions != null && headerOptions.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                foreach (var opt in headerOptions)
                    GUILayout.Label("", EditorStyles.boldLabel, opt);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // === 4. 分页计算（完全独立！）===
            int start = (currentPage - 1) * pageSize;
            int end = Mathf.Min(start + pageSize, items.Count);

            // === 5. 绘制每一项 ===
            for (int i = start; i < end; i++)
            {
                T item = items[i];
                bool isSelected = selectedIndices.Contains(i);
                float height = EditorGUIUtility.singleLineHeight + 10;

                Rect rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));

                // 背景
                if (Event.current.type == EventType.Repaint)
                {
                    Color bgColor = (i % 2 == 0)
                        ? (EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.92f, 0.92f, 0.92f))
                        : new Color(0.10f, 0.10f, 0.10f);

                    if (isSelected)
                        bgColor = EditorGUIUtility.isProSkin
                            ? new Color(0.24f, 0.40f, 0.70f)
                            : new Color(0.30f, 0.55f, 0.90f);

                    EditorGUI.DrawRect(rect, bgColor);
                }

                // 自定义绘制
                onDrawItem?.Invoke(item, i, isSelected, rect);

                // === 交互逻辑 ===
                Event e = Event.current;
                if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                {
                    if (e.button == 0)
                    {
                        bool ctrl = e.control || e.command;
                        bool shift = e.shift;

                        if (shift && selectedIndices.Count > 0)
                        {
                            int last = selectedIndices.Max();
                            selectedIndices.Clear();
                            int from = Mathf.Min(last, i);
                            int to = Mathf.Max(last, i);
                            for (int j = from; j <= to; j++) selectedIndices.Add(j);
                        }
                        else if (ctrl)
                        {
                            if (selectedIndices.Contains(i))
                                selectedIndices.Remove(i);
                            else
                                selectedIndices.Add(i);
                        }
                        else
                        {
                            if (!selectedIndices.Contains(i) || selectedIndices.Count > 1)
                            {
                                selectedIndices.Clear();
                                selectedIndices.Add(i);
                            }

                            if (e.clickCount == 2 && onDoubleClick != null)
                                onDoubleClick(i);
                        }

                        GUI.changed = true;
                        EditorWindow.GetWindow<BatchResourceWindow>()?.Repaint();
                        e.Use();
                    }
                    else if (e.button == 1 && onContextMenu != null)
                    {
                        if (!selectedIndices.Contains(i))
                        {
                            selectedIndices.Clear();
                            selectedIndices.Add(i);
                            EditorWindow.GetWindow<BatchResourceWindow>()?.Repaint();
                        }

                        GenericMenu menu = new GenericMenu();
                        onContextMenu(menu);
                        menu.ShowAsContext();
                        e.Use();
                    }
                }
            }
        }
    }
}