using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;
using System;
using System.Reflection;

namespace GeneralToolFunction
{
    // ===============导入监听器=====================
     public class UniversalImportWatcher : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingPaths = new HashSet<string>();

        
        public static void WatchPaths(IEnumerable<string> paths)
        {
            lock (PendingPaths)
            {
                foreach (var p in paths) PendingPaths.Add(p);
            }
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool needRefresh = false;
            lock (PendingPaths)
            {
                foreach (var path in imported)
                    if (PendingPaths.Remove(path))
                        needRefresh = true;
            }

            if (needRefresh)
            {
                var window = EditorWindow.GetWindow(typeof(BatchResourceTool.BatchResourceWindow), false);
                if (window == null) return;
                EditorApplication.delayCall += () =>
                {
                    

                    window.Repaint();


                    // 未来支持其他模块：else if (toolbar is MaterialToolbar m) m.RefreshPendingOnly();
                };
            }
        }

    }
    // =================================================


    // ====================== 批量纹理处理器（mipmap、compression）======================
    public  class BatchAssetOperations
    {
        // 【批量Mipmap】
        public static void BatchToggleMipmap(Object[] targets, Action onComplete)
        {
            var textures = targets
                .Where(o => o is Texture2D)
                .Select(o => o as Texture2D)
                .ToArray();

            if (textures.Length == 0)
            {
                EditorUtility.DisplayDialog("Mipmap 操作失败", "待处理资源中没有贴图。", "确定");
                return;
            }

            var importers = textures
                .Select(t => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter)
                .Where(importer => importer != null)
                .ToList();
            
            int enabledCount = importers.Count(importer => importer.mipmapEnabled);

            bool allEnabled = enabledCount == textures.Length;
            bool allDisabled = enabledCount == 0;
            bool targetState = !allEnabled; // 如果全开，则目标是关；否则目标是开

            string stateName = targetState ? "开启" : "关闭";
            
            if (allEnabled && !targetState)
            {
                // 如果全开且目标是关，直接走流程
            } 
            else if (allDisabled && targetState)
            {
                // 如果全关且目标是开，直接走流程
            }
            else if (allEnabled || allDisabled)
            {
                // 状态一致，无需操作
            }
            else
            {
                // 状态不一致，询问是全开还是全关
                if (EditorUtility.DisplayDialog("Mipmap 批量设置", 
                    $"当前选中贴图 Mipmap 状态不一致 ({enabledCount} 开，{textures.Length - enabledCount} 关)。\n\n是否统一设置为 **{stateName}**？", 
                    $"统一{stateName}", "取消"))
                {
                    // 使用计算出的 targetState
                }
                else
                {
                    return;
                }
            }
            

            int changed = 0;
            var pathsToImport = new List<string>(); 

            // 1. 批量设置参数
            foreach (var tex in textures)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                
                if (importer != null && importer.mipmapEnabled != targetState)
                {
                    importer.mipmapEnabled = targetState;
                    
                    if (targetState)
                    {
                        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter; 
                    }
                    
                    // Note: 不要在这里调用 SaveAndReimport，稍后分帧导入
                    pathsToImport.Add(path);
                }
            }
            changed = pathsToImport.Count;
            
            // 2. 启用分帧导入逻辑
            if (pathsToImport.Count > 0)
            {
                AssetDatabase.StartAssetEditing(); // 开启批量编辑
                EditorUtility.DisplayProgressBar($"批量设置 Mipmap ({stateName})", "正在导入，请稍候...", 0.0f);

                // 局部变量：队列和计数器
                var pathQueue = new Queue<string>(pathsToImport); 
                int totalCount = pathQueue.Count;
                int importedCount = 0;

                // 局部匿名方法：分帧处理导入
                void ProcessNextBatch() 
                {
                    int batchSize = 1; // 保持每帧处理一个，避免卡顿
                    for (int i = 0; i < batchSize && pathQueue.Count > 0; i++)
                    {
                        string p = pathQueue.Dequeue();
                        
                        // 触发重新导入
                        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); 
                        importedCount++;
                        
                        float progress = (float)importedCount / totalCount;
                        EditorUtility.DisplayProgressBar("批量设置 Mipmap", $"正在导入: {p}", progress);
                    }

                    if (pathQueue.Count > 0)
                    {
                        // 递归调用，下一帧继续
                        EditorApplication.delayCall += ProcessNextBatch; 
                    }
                    else
                    {
                        // 全部完成
                        AssetDatabase.StopAssetEditing();
                        EditorUtility.ClearProgressBar();
                        
                        // 延迟 Refresh 和回调
                        EditorApplication.delayCall += () =>
                        {
                            AssetDatabase.Refresh(); 
                            onComplete?.Invoke();
                            EditorUtility.DisplayDialog("Mipmap 操作完成", 
                                $"已为 {changed} 张贴图 {stateName} Mipmap", "确定");
                        };
                    }
                }
                
                // 启动导入流程
                EditorApplication.delayCall += ProcessNextBatch;
            }
            else
            {
                EditorUtility.DisplayDialog("Mipmap 操作完成", 
                    "所有选中的贴图 Mipmap 设置已一致，无需更改。", "确定");
                onComplete?.Invoke();
            }
        }
    
        // 【批量删除逻辑】
        public static void BatchDeleteAssets(Object[] targets, Action onComplete)
        {
            if (targets == null || targets.Length == 0) return;

            string title = targets.Length > 1 
                ? $"删除这 {targets.Length} 个资源" 
                : "删除此资源";

            string msg = targets.Length > 10
                ? $"确定要删除 {targets.Length} 个资源文件吗？\n\n前10项预览：\n" +
                string.Join("\n", targets.Take(10).Select(o => "• " + o.name)) + "\n..."
                : $"确定要删除以下 {targets.Length} 个资源吗？\n\n" +
                string.Join("\n", targets.Select(o => "• " + o.name));

            bool confirm = EditorUtility.DisplayDialog(
                "彻底删除资源",
                msg,
                "确认",
                "取消");

            if (confirm)
            {
                int deleted = 0;
                foreach (Object obj in targets)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    // 仅删除有路径的资源 (即项目中的资产文件)
                    if (!string.IsNullOrEmpty(path) && AssetDatabase.DeleteAsset(path))
                        deleted++;
                }

                AssetDatabase.Refresh();
                
                // 执行完成后的回调，用于清空待处理列表和刷新 UI
                onComplete?.Invoke(); 

                EditorUtility.DisplayDialog("完成",  $"已删除 {deleted} 个资源", "确定");
            }
        }
    }
    
    // =================================================================



    // ====================== 批量重命名 ================================
    class BatchRenameWindow : EditorWindow
    {
        private Object[] targets;
        private Action onComplete;
        private int mode = 0;           // 0=前缀 1=后缀 2=查找替换
        private string prefix = "";
        private string suffix = "";
        private string find = "";
        private string replace = "";
        private Vector2 scroll;

        public static void ShowWindow(Object[] objects, Action callback = null)
        {
            var win = CreateInstance<BatchRenameWindow>();
            win.targets = objects;
            win.onComplete = callback;
            win.titleContent = new GUIContent($"批量重命名 ({objects.Length} 个资源)");
            win.minSize = new Vector2(420, 300);
            win.ShowAuxWindow();
        }

        private void OnGUI()
        {
            GUILayout.Label("批量重命名方式", EditorStyles.boldLabel);
            mode = GUILayout.Toolbar(mode, new[] { "添加前缀", "添加后缀", "查找替换" });

            GUILayout.Space(10);

            if (mode == 0) // 前缀
            {
                prefix = EditorGUILayout.TextField("前缀文字", prefix);
            }
            else if (mode == 1) // 后缀
            {
                suffix = EditorGUILayout.TextField("后缀文字", suffix);
            }
            else // 查找替换
            {
                find = EditorGUILayout.TextField("查找文字", find);
                replace = EditorGUILayout.TextField("替换为", replace);
            }

            GUILayout.Space(10);
            GUILayout.Label("预览：", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
            foreach (var obj in targets)
            {
                string oldName = obj.name;
                string newName = GetNewName(oldName);
                string color = newName == oldName ? "#888888" : "#00FF00";
                EditorGUILayout.LabelField($"• {oldName}  →  <color={color}>{newName}</color>", new GUIStyle(EditorStyles.label) { richText = true });
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("应用", GUILayout.Height(40)))
            {
                int success = 0;
                var renamedPaths = new List<string>();
            foreach (var obj in targets)
            {
                string oldPath = AssetDatabase.GetAssetPath(obj);
                string dir = System.IO.Path.GetDirectoryName(oldPath).Replace("\\", "/");
                string ext = System.IO.Path.GetExtension(oldPath);
                string newName = GetNewName(obj.name);
                string newPath = $"{dir}/{newName}{ext}";

                if (newName != obj.name)
                {
                    string error = AssetDatabase.MoveAsset(oldPath, newPath);
                    if (string.IsNullOrEmpty(error))
                    {
                        success++;
                        renamedPaths.Add(newPath); // 收集新路径
                        UniversalImportWatcher.WatchPaths(new[] { newPath }); // 登记自动刷新
                    }
                }
            }

            if (renamedPaths.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                
                // 1. 启动进度条
                EditorUtility.DisplayProgressBar("批量重命名", "正在导入，请稍候...", 0.0f);

                // 2. 准备局部变量用于分帧处理
                var pathQueue = new Queue<string>(renamedPaths); // 局部变量：导入队列
                int totalCount = pathQueue.Count;              // 局部变量：总数
                int importedCount = 0;                         // 局部变量：已导入数
                
                // 3. 定义局部匿名方法
                void ProcessNextBatch() 
                {
                    if (pathQueue.Count > 0)
                    {
                        string p = pathQueue.Dequeue();
                        
                        // --- 核心优化点 ---
                        // 我们将导入操作本身包裹在一个新的延迟调用中
                        EditorApplication.delayCall += () =>
                        {
                            // 确保窗口和上下文仍然有效
                            if (p == null) return; 
                            
                            // ✅ 在单独的延迟调用中执行 ImportAsset
                            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                            importedCount++;
                            
                            // 更新进度条
                            float progress = (float)importedCount / totalCount;
                            EditorUtility.DisplayProgressBar("批量导入", $"正在导入: {p}", progress);
                            
                            // 导入完成后，继续下一个批次
                            // ❗ 重点：递归调用 ProcessNextBatch 来处理下一个资源
                            EditorApplication.delayCall += ProcessNextBatch;
                        };
                    }
                else
                {
                    // 队列为空，结束编辑模式并清理
                    AssetDatabase.StopAssetEditing();
                    EditorUtility.ClearProgressBar();

                    // 延迟 Refresh 和 Repaint
                    EditorApplication.delayCall += () =>
                    {
                        AssetDatabase.Refresh(); 
                        onComplete?.Invoke();
                        EditorUtility.DisplayDialog("完成", $"成功重命名 {success} 个资源", "确定");
                        Repaint();
                        Close();

                    };
                }
            }   
                // 4. 启动第一个批次
            EditorApplication.delayCall += ProcessNextBatch;
     }
            }

            if (GUILayout.Button("取消", GUILayout.Height(40))) Close();
            GUILayout.EndHorizontal();
        }

        private string GetNewName(string oldName)
        {
            if (string.IsNullOrEmpty(oldName)) return oldName;

            switch (mode)
            {
                // 添加前缀
                case 0: return prefix + oldName;
                // 添加后缀
                case 1: return oldName + suffix;
                // 查找替换
                case 2: if (string.IsNullOrEmpty(find))return oldName;
                    return oldName.Replace(find, replace);
                default:
                    return oldName;
            }
        }
    }

    // =================================================================


// ====================== 列表交互 + 拖拽发起 + 拖拽接收 完整合一（推荐永久使用这个版本）======================
public static class EditorListSelectionHelper
{
    private static int _dragStartIndex = -1;
    private static Vector2 _dragStartPos;

    // =================================== 1. 列表行交互：单击、多选、双击Ping、拖拽发起 ===================================
    public static void HandleRowInteraction(
        Rect rowRect,
        Rect[] excludeRects,
        int index,
        HashSet<int> selectedIndices,
        System.Collections.IList items)
    {
        Event e = Event.current;
        int controlID = GUIUtility.GetControlID(FocusType.Passive);

        bool inRow = rowRect.Contains(e.mousePosition);
        bool inExclude = excludeRects != null && excludeRects.Any(r => r.Contains(e.mousePosition));
        if (!inRow || inExclude) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            GUIUtility.hotControl = controlID;
            e.Use();

            _dragStartIndex = index;
            _dragStartPos = e.mousePosition;

            if (e.clickCount == 2)
            {
                if (items[index] is Object obj && obj != null)
                {
                    Selection.objects = new Object[] { obj };
                    EditorGUIUtility.PingObject(obj);
                }
                selectedIndices.Clear();
                selectedIndices.Add(index);
            }
            else
            {
                bool ctrl = e.control || e.command;
                bool shift = e.shift;

                if (shift && selectedIndices.Count > 0)
                {
                    int last = selectedIndices.Max();
                    selectedIndices.Clear();
                    for (int i = Mathf.Min(last, index); i <= Mathf.Max(last, index); i++)
                        selectedIndices.Add(i);
                }
                else if (ctrl)
                {
                    if (selectedIndices.Contains(index))
                        selectedIndices.Remove(index);
                    else
                        selectedIndices.Add(index);
                }
                else
                {
                    if (!selectedIndices.Contains(index))
                    {
                        selectedIndices.Clear();
                        selectedIndices.Add(index);
                        Selection.objects = new Object[0];
                    }
                }
            }

            RepaintFocusedWindow();
            return;
        }

        // 拖拽发起
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
        {
            if (_dragStartIndex == index && Vector2.Distance(_dragStartPos, e.mousePosition) > 8f)
            {
                var dragged = selectedIndices
                    .Select(i => items[i] as Object)
                    .Where(o => o != null)
                    .ToArray();

                if (dragged.Length > 0)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("BatchToolDrag", "1");     // 关键标识
                    DragAndDrop.objectReferences = dragged;
                    DragAndDrop.StartDrag(dragged.Length > 1 ? $"拖动 {dragged.Length} 个资源" : dragged[0].name);
                    e.Use();
                }
                _dragStartIndex = -1;
            }
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
        {
            GUIUtility.hotControl = 0;
            _dragStartIndex = -1;
        }
    }

    // =================================== 2. 拖拽接收：任何地方都能接收本工具拖出的资源 ===================================
    /// <summary>
    /// 在 OnGUI 最后调用即可接收拖拽进来的资源（自动去重 + 自动刷新）
    /// </summary>
    /// <example>
    /// EditorListSelectionHelper.ReceiveDrop(_pendingAreaRect, _pendingResources, Repaint);
    /// </example>
    public static void ReceiveDrop(Rect dropArea, List<Object> targetList, Action onRepaint = null)
    {
        Event e = Event.current;

        if (!dropArea.Contains(e.mousePosition))
            return;

        if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
        {
            if (DragAndDrop.GetGenericData("BatchToolDrag") != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    bool added = false;
                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj != null && !targetList.Contains(obj))
                        {
                            targetList.Add(obj);
                            added = true;
                        }
                    }

                    if (added)
                        onRepaint?.Invoke();
                }

                e.Use();
            }
        }
    }

    // =================================== 内部工具方法 ===================================
    private static void RepaintFocusedWindow()
    {
        if (EditorWindow.focusedWindow != null)
            EditorWindow.focusedWindow.Repaint();
    }

    // =================================== 扩展说明（给以后自己或队友看的） ===================================
    /*
     * 如何让别的窗口也支持接收拖拽？
     * → 只需要在目标窗口的 OnGUI() 最后加一行：
     *     EditorListSelectionHelper.ReceiveDrop(myRect, myList, () => Repaint());
     * 
     * 如何让别的工具也能发出可被接收的拖拽？
     * → 拖拽发起时加上这句：
     *     DragAndDrop.SetGenericData("BatchToolDrag", "1");
     */
    }
}
