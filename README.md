本工具是为快速查找、清理和优化Unity资源而开发的Unity Editor扩展，当前已具备：

      
Texture 模块：  全项目贴图扫描、尺寸/类型/引用统计、筛选排序、压缩/Mipmap/MaxSize以及重命名的批量操作

Shader   （已预留接口）

Materail （已预留接口）
...


基础操作

导入UnityPackage后在项目顶部Tool栏打开ResourceBatchTool；

1.选择资源类型：在资源类型菜单栏选择所需资源类型（Texturet）；

2.添加搜索路径：在project选择文件夹然后在工具窗口点击“添加文件夹按钮”后再点击搜索按钮即可开始遍历路径中的资源；

3.结果筛选：搜索结束后可以通过点击“排序方式”选择自己想查看的贴图类别，窗口底部可对资源分页进行设置（不建议在同一页展示太多资源，容易造成编辑器卡顿）

4.在筛选结果列表中选择需要处理的资源拖入窗口右边的资源待处理区域后即可开始进行资源的批处理操作（此处的选择操作逻辑与window的单选多选操作逻辑相同），窗口右上方功能区的按钮外，可以右键点击待处理资源列表上方“右键次数批处理”字样执行相关功能；

5.资源刷新：在窗口提示当前的批处理操作已完成后，点击“刷新”即可更新筛选结果列表；



二次开发指南

1.创建toolbar类继承BaseToolbar

2.在BatchResourceWindow中注册新模块

// BatchResourceWindow.cs 的 OnEnable() 中添加

if (!_toolbars.ContainsKey(ToolbarType.Prefab))
    
 _toolbars.Add(ToolbarType.Prefab, new PrefabToolbar());

// 在 enum 中添加

public enum ToolbarType { Material, Shader, Texture, Prefab } 

//在DrawSearchResults中支持新模块

if (toolbar is PrefabToolbar prefabTool) // 新增这一行

 {
 prefabTool.DrawResultHierarchy(this);
         
 return;
 }

3.已经提供的通用功能函数

批量删除

 BatchAssetOperations.BatchDeleteAssets()
 
批量重命名

复用 BatchRenameWindow.ShowWindow()

待处理区

自动支持，通过 EditorListSelectionHelper.ReceiveDrop() 接收拖拽

刷新 

使用UniversalImportWatcher.WatchPaths() 监听变化，调用 RefreshPendingOnly() 或类似

分页

UIModule.DrawPagination()

列表交互

EditorListSelectionHelper.HandleRowInteraction() 和 DrawListItem()

筛选结果列表绘制

DrawGenericResultList
