using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TNTD.Hierarchy4
{
    [InitializeOnLoad]
    public sealed class HierarchyEditor
    {
        internal const int GLOBAL_SPACE_OFFSET_LEFT = 16 * 2;
        static HierarchyEditor instance;

        public static HierarchyEditor Instance
        {
            get
            {
                instance ??= new HierarchyEditor();
                return instance;
            }
            private set => instance = value;
        }

        Dictionary<int, Object> selectedComponents = new ();
        Dictionary<string, string> dicComponents = new (StringComparer.Ordinal);
        Object activeComponent;

        GUIContent tooltipContent = new();

        HierarchySettings settings;
        HierarchyResources resources;

        HierarchySettings.ThemeData ThemeData => settings.usedTheme;

        int deepestRow = int.MinValue;
        int previousRowIdx = int.MinValue;

        int sceneIdx;
        Scene currentScene;
        Scene previousScene;

        public static bool IsMultiScene => SceneManager.sceneCount > 1;

        bool selectionStyleAfterInvoke;

        Event currentEvent;

        HierarchyObject hierarchyObject = new();
        HierarchyObject previousElement;
        WidthUse widthUse = WidthUse.zero;
        private EditorWindow hierarchyEW;
        private object treeView;
        static HierarchyEditor()
        {
            instance = new HierarchyEditor();
        }

        public HierarchyEditor()
        {
            InternalReflection();
            EditorApplication.update += EditorAwake;
        }

        internal static List<Type> InternalEditorType = new();
        internal static Dictionary<string, Type> dicInternalEditorType = new();
        internal static List<Type> DisplayOnHierarchyScriptType = new();
        internal static Dictionary<string, Type> dicDisplayOnHierarchyScriptType = new();

        internal static Type SceneHierarchyWindow;
        internal static Type SceneHierarchy;
        internal static Type GameObjectTreeViewGUI;

        internal static FieldInfo m_SceneHierarchy;
        internal static FieldInfo m_TreeView;
        // internal static PropertyInfo gui;
        // internal static FieldInfo k_IconWidth;
        internal static MethodInfo FindItemTreeViewMethod;
        internal static Func<SearchableEditorWindow> lastInteractedHierarchyWindowDelegate;
        internal static Func<IEnumerable> GetAllSceneHierarchyWindowsDelegate;
        internal static Func<GameObject, Rect, bool, bool> IconSelectorShowAtPositionDelegate;
        internal static Action<Rect, Object, int> DisplayObjectContextMenuDelegate;

        // public static Action OnRepaintHierarchyWindowCallback;
        // public static Action OnWindowsReorderedCallback;

        static void InternalReflection()
        {
            var arrayInteralEditorType = typeof(Editor).Assembly.GetTypes();
            InternalEditorType = arrayInteralEditorType.ToList();
            dicInternalEditorType = arrayInteralEditorType.ToDictionary(type => type.FullName);

            // FieldInfo refreshHierarchy = typeof(EditorApplication).GetField(nameof(refreshHierarchy), BindingFlags.Static | BindingFlags.NonPublic);
            // MethodInfo OnRepaintHierarchyWindow = typeof(HierarchyEditor).GetMethod(nameof(OnRepaintHierarchyWindow), BindingFlags.NonPublic | BindingFlags.Static);
            // Delegate refreshHierarchyDelegate = Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), OnRepaintHierarchyWindow);
            // refreshHierarchy.SetValue(null, refreshHierarchyDelegate);


            // FieldInfo windowsReordered = typeof(EditorApplication).GetField(nameof(windowsReordered), BindingFlags.Static | BindingFlags.NonPublic);
            // MethodInfo OnWindowsReordered = typeof(HierarchyEditor).GetMethod(nameof(OnWindowsReordered), BindingFlags.NonPublic | BindingFlags.Static);
            // Delegate windowsReorderedDelegate = Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), OnWindowsReordered);
            // windowsReordered.SetValue(null, windowsReorderedDelegate);

            dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(SceneHierarchyWindow), out SceneHierarchyWindow);
            dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(GameObjectTreeViewGUI), out GameObjectTreeViewGUI); //GameObjectTreeViewGUI : TreeViewGUI
            dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(SceneHierarchy), out SceneHierarchy);

            // FieldInfo s_LastInteractedHierarchy = SceneHierarchyWindow.GetField(nameof(s_LastInteractedHierarchy), BindingFlags.NonPublic | BindingFlags.Static);

            // MethodInfo lastInteractedHierarchyWindow = SceneHierarchyWindow.GetProperty(nameof(lastInteractedHierarchyWindow), BindingFlags.Static | BindingFlags.Public).GetGetMethod();
            // lastInteractedHierarchyWindowDelegate = Delegate.CreateDelegate(typeof(Func<SearchableEditorWindow>), lastInteractedHierarchyWindow) as Func<SearchableEditorWindow>;

            MethodInfo GetAllSceneHierarchyWindows = SceneHierarchyWindow!.GetMethod(nameof(GetAllSceneHierarchyWindows), BindingFlags.Static | BindingFlags.Public);
            GetAllSceneHierarchyWindowsDelegate = Delegate.CreateDelegate(typeof(Func<IEnumerable>), GetAllSceneHierarchyWindows!) as Func<IEnumerable>;

            {
                m_SceneHierarchy = SceneHierarchyWindow.GetField(nameof(m_SceneHierarchy), BindingFlags.NonPublic | BindingFlags.Instance);
                m_TreeView = SceneHierarchy!.GetField(nameof(m_TreeView), BindingFlags.NonPublic | BindingFlags.Instance);
                // gui = m_TreeView.FieldType.GetProperty(nameof(gui).ToLower(), BindingFlags.Public | BindingFlags.Instance);
                // k_IconWidth = GameObjectTreeViewGUI.GetField(nameof(k_IconWidth), BindingFlags.Public | BindingFlags.Instance);
            }

            MethodInfo DisplayObjectContextMenu = typeof(EditorUtility).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single
            (
                method => method.Name == nameof(DisplayObjectContextMenu) && method.GetParameters()[1].ParameterType == typeof(UnityEngine.Object)
            );
            DisplayObjectContextMenuDelegate = Delegate.CreateDelegate(typeof(Action<Rect, UnityEngine.Object, int>), DisplayObjectContextMenu) as Action<Rect, UnityEngine.Object, int>;


            Type IconSelector = typeof(EditorWindow).Assembly.GetTypes().Single(type => type.BaseType == typeof(EditorWindow) && type.Name == nameof(IconSelector)) as Type;
            MethodInfo ShowAtPosition = IconSelector.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single
            (
                method => method.Name == nameof(ShowAtPosition) && method.GetParameters()[0].ParameterType == typeof(UnityEngine.Object)
            );
            IconSelectorShowAtPositionDelegate = Delegate.CreateDelegate(typeof(Func<GameObject, Rect, bool, bool>), ShowAtPosition) as Func<GameObject, Rect, bool, bool>;

            GetItemAndRowIndexMethod = m_TreeView?.FieldType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Single(method => method.Name == "GetItemAndRowIndex");
            FindItemTreeViewMethod = m_TreeView?.FieldType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(method => method.Name == "FindItem");

            // m_TreeView_IData = m_TreeView.FieldType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Single(property => property.Name == "data");

            // m_Rows = InternalEditorType.Find(type => type.Name == "TreeViewDataSource").GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(field => field.Name.Contains(nameof(m_Rows)));
        }

        private static MethodInfo GetItemAndRowIndexMethod;
        // private static PropertyInfo m_TreeView_IData;
        // private static FieldInfo m_Rows;

        // static void OnRepaintHierarchyWindow()
        // {
            // OnRepaintHierarchyWindowCallback?.Invoke();
        // }

        // static void OnWindowsReordered()
        // {
            // OnWindowsReorderedCallback?.Invoke();
        // }

        void EditorAwake()
        {
            settings = HierarchySettings.GetAssets();
            if (settings is null) return;
            OnSettingsChanged(nameof(settings.components));
            settings.onSettingsChanged += OnSettingsChanged;

            resources = HierarchyResources.GetAssets();
            if (resources is null) return;
            resources.GenerateKeyForAssets();

            EditorApplication.hierarchyWindowItemOnGUI += FirstHierarchyOnGUI;
            EditorApplication.hierarchyWindowItemOnGUI += HierarchyOnGUI;

            if (settings.activeHierarchy)
                Invoke();
            else
                Dispose();

            EditorApplication.update -= EditorAwake;
        }

        void OnSettingsChanged(string param)
        {
            switch (param)
            {
                case nameof(settings.components):
                    dicComponents.Clear();
                    foreach (var componentType in settings.components)
                    {
                        dicComponents.TryAdd(componentType, componentType);
                    }

                    break;
            }

            EditorApplication.RepaintHierarchyWindow();
        }

        public void Invoke()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            PrefabUtility.prefabInstanceUpdated += OnPrefabUpdated;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            // EditorApplication.update += OnEditorUpdate;

            selectionStyleAfterInvoke = false;

            CachingHierarchyWindow();
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        public void Dispose()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;

            PrefabUtility.prefabInstanceUpdated -= OnPrefabUpdated;
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;

            // EditorApplication.update -= OnEditorUpdate;

            foreach (EditorWindow window in GetAllSceneHierarchyWindowsDelegate())
            {
                window.titleContent.text = "Hierarchy";
            }

            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        double lastTimeSinceStartup = EditorApplication.timeSinceStartup;

        void OnEditorUpdate()
        {
            if (!hierarchyChangedRequireUpdating) return;
            if (EditorApplication.timeSinceStartup - lastTimeSinceStartup >= 1)
            {
                CachingHierarchyWindow();
                lastTimeSinceStartup = EditorApplication.timeSinceStartup;
                hierarchyChangedRequireUpdating = false;
            }
        }

        void CachingHierarchyWindow()
        {
        }

        [DidReloadScripts]
        static void OnEditorCompiled()
        {
        }

        bool hierarchyChangedRequireUpdating;

        void OnHierarchyChanged()
        {
            hierarchyChangedRequireUpdating = true;
        }

        void OnPrefabUpdated(GameObject prefab)
        {
        }

        bool prefabStageChanged;

        void OnPrefabStageOpened(PrefabStage stage)
        {
            prefabStageChanged = true;
        }

        void OnPrefabStageClosing(PrefabStage stage)
        {
            prefabStageChanged = true;
        }

        private double t;
        void FirstHierarchyOnGUI(int selectionID, Rect selectionRect)
        {
            if (EditorApplication.timeSinceStartup - t < 0.6) return;
            t = EditorApplication.timeSinceStartup;
            foreach (EditorWindow window in GetAllSceneHierarchyWindowsDelegate())
            {
                hierarchyEW = window;
                treeView = m_TreeView.GetValue(m_SceneHierarchy.GetValue(hierarchyEW));
            }
        }

        void HierarchyOnGUI(int selectionID, Rect selectionRect)
        {
            currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.H && currentEvent.control)
            {
                if (!settings.activeHierarchy)
                    Invoke();
                else
                    Dispose();

                settings.activeHierarchy = !settings.activeHierarchy;
                currentEvent.Use();
            }

            if (!settings.activeHierarchy)
                return;

            if (currentEvent.control && currentEvent.keyCode == KeyCode.D)
                return;

            if (currentEvent.type == EventType.Layout)
            {
                if (prefabStageChanged)
                {
                    prefabStageChanged = false;
                }

                return;
            }

            hierarchyChangedRequireUpdating = true;

            if (selectionStyleAfterInvoke == false && currentEvent.type == EventType.MouseDown)
            {
                selectionStyleAfterInvoke = true;
            }

            hierarchyObject.Dispose();
            hierarchyObject.ID = selectionID;
            hierarchyObject.gameObject = EditorUtility.InstanceIDToObject(hierarchyObject.ID) as GameObject;
            hierarchyObject.rect = selectionRect;
            hierarchyObject.rowIndex = GetRectIndex(hierarchyObject.rect);
            hierarchyObject.isSelected = InSelection(selectionID);
            hierarchyObject.isFirstRow = hierarchyObject.rowIndex == 0;
            hierarchyObject.isFirstElement = IsFirstElement(hierarchyObject.rect);
            hierarchyObject.treeViewItem = FindItemTreeViewMethod.Invoke(treeView, new[] {GetTreeViewIdentifier(hierarchyObject.ID)}) as TreeViewItem;

            hierarchyObject.isNull = hierarchyObject.gameObject == null;

            // Caching some data if is a GameObject.
            if (!hierarchyObject.isNull)
            {
                hierarchyObject.hierarchyFolder = hierarchyObject.gameObject!.GetComponent<HierarchyFolder>();
                if (!(hierarchyObject.isFolder = hierarchyObject.hierarchyFolder))
                    hierarchyObject.isSeparator = hierarchyObject.Name.StartsWith(settings.separatorStartWith);

                hierarchyObject.isDirty = EditorUtility.IsDirty(selectionID);

                // Need re-write code below.
                if (true && !hierarchyObject.isSeparator && hierarchyObject.isDirty)
                {
                    hierarchyObject.isPrefab = PrefabUtility.IsPartOfAnyPrefab(hierarchyObject.gameObject);

                    if (hierarchyObject.isPrefab)
                        hierarchyObject.isPrefabMissing = PrefabUtility.IsPrefabAssetMissing(hierarchyObject.gameObject);
                }
            }

            hierarchyObject.isRootObject = hierarchyObject.isNull || hierarchyObject.gameObject!.transform.parent == null;
            hierarchyObject.isMouseHovering = selectionRect.Contains(currentEvent.mousePosition);

            if (hierarchyObject.isFirstRow)
            {
                sceneIdx = 0;

                if (deepestRow > previousRowIdx)
                    deepestRow = previousRowIdx;
            }

            if (hierarchyObject.isNull)
            {
                if (!IsMultiScene)
                    currentScene = SceneManager.GetActiveScene();
                else
                {
                    if (!hierarchyObject.isFirstRow && sceneIdx < SceneManager.sceneCount - 1)
                        sceneIdx++;
                    currentScene = SceneManager.GetSceneAt(sceneIdx);
                }

                RenameSceneInHierarchy();

                widthUse = WidthUse.zero;

                if (settings.displayRowBackground)
                {
                    if (deepestRow != hierarchyObject.rowIndex)
                        DisplayRowBackground();
                }

                previousElement = hierarchyObject;
                previousRowIdx = hierarchyObject.rowIndex;
                previousScene = currentScene;

                if (previousRowIdx > deepestRow)
                    deepestRow = previousRowIdx;
                return;
            }
            else
            {
                if (hierarchyObject.isFirstElement)
                {
                    if (deepestRow > previousRowIdx)
                        deepestRow = previousRowIdx;
                    deepestRow -= hierarchyObject.rowIndex;

                    if (IsMultiScene)
                    {
                        if (!previousElement.isNull)
                        {
                            for (int i = 0; i < SceneManager.sceneCount; ++i)
                            {
                                if (SceneManager.GetSceneAt(i) == hierarchyObject.gameObject!.scene)
                                {
                                    sceneIdx = i;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (IsMultiScene)
                {
                }

                hierarchyObject.nameRect = hierarchyObject.rect;
                GUIStyle nameStyle = TreeStyleFromFont(FontStyle.Normal);
                hierarchyObject.nameRect.width = nameStyle.CalcSize(new GUIContent(hierarchyObject.gameObject!.name)).x;

                hierarchyObject.nameRect.x += 16;

                var isPrefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null;

                if (settings.displayRowBackground && deepestRow != hierarchyObject.rowIndex)
                {
                    if (isPrefabMode)
                    {
                        if (hierarchyObject.gameObject.transform.parent == null) //Should use row index instead.
                        {
                            if (deepestRow != 0)
                                DisplayRowBackground();
                        }
                    }
                    else
                        DisplayRowBackground();
                }


                if (hierarchyObject.isFolder)
                {
                    var icon = hierarchyObject.ChildCount > 0 ? Resources.FolderIcon : Resources.EmptyFolderIcon;
                    DisplayCustomObjectIcon(icon);
                }

                if (hierarchyObject.isSeparator && hierarchyObject.isRootObject)
                {
                    ElementAsSeparator();
                    goto FINISH;
                }

                if (settings.useInstantBackground)
                    CustomRowBackground();

                if (settings.displayTreeView && !hierarchyObject.isRootObject)
                    RenderTreeView();

                if (settings.displayCustomObjectIcon)
                    DisplayCustomObjectIcon(null);

                widthUse = WidthUse.zero;
                widthUse.left += GLOBAL_SPACE_OFFSET_LEFT;
                if (isPrefabMode) widthUse.left -= 2;
                widthUse.afterName = hierarchyObject.nameRect.x + hierarchyObject.nameRect.width;

                widthUse.afterName += settings.offSetIconAfterName;

                DisplayEditableIcon();

                // DisplayNoteIcon();

                widthUse.afterName += 8;

                if (settings.displayTag && !hierarchyObject.gameObject.CompareTag("Untagged"))
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Tag) !=
                        HierarchySettings.ContentDisplay.Tag ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Tag) ==
                            HierarchySettings.ContentDisplay.Tag && hierarchyObject.isMouseHovering))
                    {
                        DisplayTag();
                    }
                }

                if (settings.displayLayer && hierarchyObject.gameObject.layer != 0)
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Layer) !=
                        HierarchySettings.ContentDisplay.Layer ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Layer) ==
                            HierarchySettings.ContentDisplay.Layer && hierarchyObject.isMouseHovering))
                    {
                        DisplayLayer();
                    }
                }

                if (settings.displayStaticIcon)
                {
                    // StaticIcon();
                }

                if (settings.displayComponents)
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Component) !=
                        HierarchySettings.ContentDisplay.Component ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Component) ==
                            HierarchySettings.ContentDisplay.Component && hierarchyObject.isMouseHovering))
                    {
                        DisplayComponents();
                    }
                }

                ElementEvent(hierarchyObject);

            FINISH:
                if (settings.displayGrid)
                    RenderGrid();

                previousElement = hierarchyObject;
                previousRowIdx = hierarchyObject.rowIndex;
                previousScene = currentScene;

                if (previousRowIdx > deepestRow)
                {
                    deepestRow = previousRowIdx;
                }
            }
        }

        GUIStyle TreeStyleFromFont(FontStyle fontStyle)
        {
            GUIStyle style;
            switch (fontStyle)
            {
                case FontStyle.Bold:
                    style = new GUIStyle(Styles.TreeBoldLabel);
                    break;

                case FontStyle.Italic:
                    style = new GUIStyle(Styles.TreeLabel);
                    break;

                case FontStyle.BoldAndItalic:
                    style = new GUIStyle(Styles.TreeBoldLabel);
                    break;

                default:
                    style = new GUIStyle(Styles.TreeLabel);
                    break;
            }

            return style;
        }

        void CustomRowBackground()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            HierarchySettings.InstantBackgroundColor instantBackgroundColor = new HierarchySettings.InstantBackgroundColor();
            bool contain = false;
            for (int i = 0; i < settings.instantBackgroundColors.Count; ++i)
            {
                if (!settings.instantBackgroundColors[i].active) continue;
                if
                (
                    (settings.instantBackgroundColors[i].useTag && !string.IsNullOrEmpty(settings.instantBackgroundColors[i].tag) && hierarchyObject.gameObject.CompareTag(settings.instantBackgroundColors[i].tag)) ||
                    (settings.instantBackgroundColors[i].useLayer && (1 << hierarchyObject.gameObject.layer & settings.instantBackgroundColors[i].layer) != 0) ||
                    (settings.instantBackgroundColors[i].useStartWith && !string.IsNullOrEmpty(settings.instantBackgroundColors[i].startWith) && hierarchyObject.Name.StartsWith(settings.instantBackgroundColors[i].startWith))
                )
                {
                    contain = true;
                    instantBackgroundColor = settings.instantBackgroundColors[i];
                }
            }

            if (!contain) return;
            Color guiColor = GUI.color;
            GUI.color = instantBackgroundColor.color;
            Rect rect;
            var texture = Resources.PixelWhite;
            rect = RectFromRight(hierarchyObject.rect, hierarchyObject.rect.width + 16, 0);
            rect.x += 16;
            rect.xMin = 32;

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        void ElementAsSeparator()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            if (!hierarchyObject.gameObject.CompareTag(settings.separatorDefaultTag))
                hierarchyObject.gameObject.tag = settings.separatorDefaultTag;

            var rect = EditorGUIUtility.PixelsToPoints(RectFromLeft(hierarchyObject.rect, Screen.width, 0));
            rect.y = hierarchyObject.rect.y;
            rect.height = hierarchyObject.rect.height;
            rect.x += GLOBAL_SPACE_OFFSET_LEFT;
            rect.width -= GLOBAL_SPACE_OFFSET_LEFT;

            Color guiColor = GUI.color;
            GUI.color = ThemeData.colorHeaderBackground;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);

            var content = new GUIContent(hierarchyObject.Name.Remove(0, settings.separatorStartWith.Length));
            rect.x += (rect.width - Styles.Header.CalcSize(content).x) / 2;
            GUI.color = ThemeData.colorHeaderTitle;
            GUI.Label(rect, content, Styles.Header);
            GUI.color = guiColor;
        }

        void ElementEvent(HierarchyObject element)
        {
            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.control && currentEvent.shift && currentEvent.alt &&
                    currentEvent.keyCode == KeyCode.C && lastInteractedHierarchyWindowDelegate() != null)
                    CollapseAll();
            }

            if (currentEvent.type == EventType.KeyUp &&
                currentEvent.keyCode == KeyCode.F2 &&
                Selection.gameObjects.Length > 1)
            {
                var window = SelectionsRenamePopup.ShowPopup();
                currentEvent.Use();
                return;
            }

            if (element.rect.Contains(currentEvent.mousePosition) && currentEvent.type == EventType.MouseUp &&
                currentEvent.button == 2)
            {
                Undo.RegisterCompleteObjectUndo(element.gameObject,
                    element.gameObject.activeSelf ? "Inactive object" : "Active object");
                element.gameObject.SetActive(!element.gameObject.activeSelf);
                currentEvent.Use();
                return;
            }
        }

        void StaticIcon()
        {
            if (!hierarchyObject.IsStatic) return;
            if (currentEvent.type != EventType.Repaint) return;

            Rect rect = hierarchyObject.rect;
            rect = RectFromRight(rect, 16, ref widthUse.right);
            Color guiColor = GUI.color;
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            Texture image = EditorGUIUtility.ObjectContent(null, typeof(Transform)).image;
            GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
            GUI.color = guiColor;

            if (currentEvent.type == EventType.MouseUp &&
                currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GenericMenu staticMenu = new GenericMenu();
                staticMenu.AddItem(new GUIContent("Apply All Children"), settings.applyStaticTargetAndChild,
                    () => { settings.applyStaticTargetAndChild = !settings.applyStaticTargetAndChild; });
                staticMenu.AddSeparator("");
                staticMenu.AddItem(new GUIContent("True"), hierarchyObject.gameObject.isStatic ? true : false,
                    () => { hierarchyObject.gameObject.isStatic = !hierarchyObject.gameObject.isStatic; });
                staticMenu.AddItem(new GUIContent("False"), !hierarchyObject.gameObject.isStatic ? true : false,
                    () => { hierarchyObject.gameObject.isStatic = !hierarchyObject.gameObject.isStatic; });
                staticMenu.ShowAsContext();
                currentEvent.Use();
            }
        }

        void ApplyStaticTargetAndChild(Transform target, bool value)
        {
            target.gameObject.isStatic = value;

            for (int i = 0; i < target.childCount; ++i)
                ApplyStaticTargetAndChild(target.GetChild(i), value);
        }

        void DisplayCustomObjectIcon(Texture icon)
        {
            var rect = RectFromRight(hierarchyObject.nameRect, 16, hierarchyObject.nameRect.width + 1);
            rect.height = 16;

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                IconSelectorShowAtPositionDelegate(hierarchyObject.gameObject, rect, true);
                currentEvent.Use();
            }

            if (currentEvent.type == EventType.Repaint)
            {
                if (hierarchyObject.treeViewItem == null)
                {
                    return;
                }

                var icon2D = icon as Texture2D;

                if (icon2D == null)
                {
                    icon2D = AssetPreview.GetMiniThumbnail(hierarchyObject.gameObject);
                }

                hierarchyObject.treeViewItem.icon = icon2D;
            }
        }

        void DisplayEditableIcon()
        {
            if (hierarchyObject.gameObject.hideFlags == HideFlags.NotEditable)
            {
                Rect lockRect = RectFromLeft(hierarchyObject.nameRect, 12, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    Color guiColor = GUI.color;
                    GUI.color = ThemeData.colorLockIcon;
                    GUI.DrawTexture(lockRect, Resources.lockIconOn, ScaleMode.ScaleToFit);
                    GUI.color = guiColor;
                }

                if (currentEvent.type == EventType.MouseUp &&
                    currentEvent.button == 1 &&
                    lockRect.Contains(currentEvent.mousePosition))
                {
                    GenericMenu lockMenu = new GenericMenu();

                    GameObject gameObject = hierarchyObject.gameObject;

                    lockMenu.AddItem(new GUIContent("Unlock"), false, () =>
                    {
                        Undo.RegisterCompleteObjectUndo(gameObject, "Unlock...");
                        foreach (Component component in gameObject.GetComponents<Component>())
                        {
                            if (component)
                            {
                                Undo.RegisterCompleteObjectUndo(component, "Unlock...");
                                component.hideFlags = HideFlags.None;
                            }
                        }

                        gameObject.hideFlags = HideFlags.None;

                        InternalEditorUtility.RepaintAllViews();
                    });
                    lockMenu.ShowAsContext();
                    currentEvent.Use();
                }
            }
        }

        void DisplayNoteIcon()
        {
            // if (!element.hasLocalData || element.data.note == "")
            //     return;

            // var iconRect = RectFromLeft(element.nameRect, 14, ref widthUse.afterName);
            // if (currentEvent.type == EventType.Repaint)
            // {
            //     GUIContent content = new GUIContent("", element.data.note);
            //     GUI.Box(iconRect, content, GUIStyle.none);
            //     GUI.color = Color.yellow;
            //     GUI.DrawTexture(iconRect, Resources.NoteIcon, ScaleMode.ScaleToFit);
            //     GUI.color = Color.white;
            // }

            // if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 && iconRect.Contains(currentEvent.mousePosition))
            // {
            //     GenericMenu noteMenu = new GenericMenu();
            //     noteMenu.AddItem(new GUIContent("Remove Note"), false, () =>
            //     {
            //         element.data.note = "";
            //     });
            //     noteMenu.ShowAsContext();
            //     currentEvent.Use();
            // }

            // widthUse.afterName += 2;
        }

        void DisplayComponents()
        {
            var components = hierarchyObject.gameObject.GetComponents(typeof(Component)).ToList<UnityEngine.Object>();
            var rendererComponent = hierarchyObject.gameObject.GetComponent<Renderer>();
            var hasMaterial = rendererComponent != null && rendererComponent.sharedMaterial != null;

            if (hasMaterial)
            {
                components.AddRange(rendererComponent.sharedMaterials);
            }

            var length = components.Count;
            var separator = false;
            float widthUsedCached;
            if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                widthUsedCached = widthUse.afterName;
                widthUse.afterName += 4;
            }
            else
            {
                widthUsedCached = widthUse.right;
                widthUse.right += 2;
            }

            for (var i = 0; i < length; ++i)
            {
                var component = components[i];
                var componentNotNull = component != null;

                Type comType = null;
                if (componentNotNull) comType = component.GetType();

                {
                    var isMono = false;
                    if (componentNotNull && comType.BaseType == typeof(MonoBehaviour)) isMono = true;
                    if (isMono)
                    {
                        //TODO: ???
                        bool shouldIgnoreThisMono = false;
                        if (shouldIgnoreThisMono) continue;
                    }

                    switch (settings.componentDisplayMode)
                    {
                        case HierarchySettings.ComponentDisplayMode.ScriptOnly:
                            if (!isMono)
                                continue;
                            break;

                        case HierarchySettings.ComponentDisplayMode.Specified:
                            if (componentNotNull && !dicComponents.ContainsKey(comType.Name))
                                continue;
                            break;

                        case HierarchySettings.ComponentDisplayMode.Ignore:
                            if (componentNotNull && dicComponents.ContainsKey(comType.Name))
                                continue;
                            break;
                    }

                    var rect = Rect.zero;

                    if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                        rect = RectFromLeft(hierarchyObject.nameRect, settings.componentSize, ref widthUse.afterName);
                    else
                        rect = RectFromRight(hierarchyObject.rect, settings.componentSize, ref widthUse.right);


                    if (hasMaterial && i == length - rendererComponent.sharedMaterials.Length &&
                        settings.componentDisplayMode != HierarchySettings.ComponentDisplayMode.ScriptOnly)
                    {
                        for (var m = 0; m < rendererComponent.sharedMaterials.Length; ++m)
                        {
                            var sharedMaterial = rendererComponent.sharedMaterials[m];

                            if (sharedMaterial == null) continue;
                            ComponentIcon(sharedMaterial, comType, rect, true);

                            if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                                rect = RectFromLeft(hierarchyObject.nameRect, settings.componentSize,
                                    ref widthUse.afterName);
                            else
                                rect = RectFromRight(hierarchyObject.rect, settings.componentSize, ref widthUse.right);
                        }

                        separator = true;
                        break;
                    }

                    ComponentIcon(component, comType, rect);

                    if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                        widthUse.afterName += settings.componentSpacing;
                    else
                        widthUse.right += settings.componentSpacing;

                    separator = true;
                }
            }

            if (separator && currentEvent.type == EventType.Repaint)
            {
                if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                    GUISeparator(RectFromLeft(hierarchyObject.nameRect, 1, widthUsedCached), ThemeData.colorGrid);
                else
                    GUISeparator(RectFromRight(hierarchyObject.rect, 1, widthUsedCached), ThemeData.colorGrid);
            }
        }

        void ComponentIcon(Object component, Type componentType, Rect rect, bool isMaterial = false)
        {
            var componentNotNull = component != null;
            var compID = componentNotNull ? component.GetInstanceID() : -1;

            if (currentEvent.type == EventType.Repaint)
            {
                Texture image;
                if (componentNotNull)
                    image = EditorGUIUtility.ObjectContent(component, componentType).image;
                else
                    image = Resources.MissingScriptIcon;

                if (selectedComponents.ContainsKey(compID))
                {
                    var guiColor = GUI.color;
                    GUI.color = ThemeData.comSelBGColor;
                    GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
                    GUI.color = guiColor;
                }

                var tooltip = "Missing Script";
                if (componentNotNull)
                    tooltip = isMaterial ? component.name : componentType.Name;
                tooltipContent.tooltip = tooltip;
                GUI.Box(rect, tooltipContent, GUIStyle.none);

                GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
            }


            if (componentNotNull && rect.Contains(currentEvent.mousePosition))
            {
                if (currentEvent.type == EventType.MouseDown)
                {
                    if (currentEvent.button == 0)
                    {
                        if (currentEvent.control)
                        {
                            if (selectedComponents.TryAdd(compID, component))
                            {
                                activeComponent = component;
                            }
                            else
                            {
                                selectedComponents.Remove(compID);
                            }

                            currentEvent.Use();
                            return;
                        }

                        selectedComponents.Clear();
                        selectedComponents.Add(compID, component);
                        activeComponent = component;
                        currentEvent.Use();
                        return;
                    }

                    if (currentEvent.button == 1)
                    {
                        if (currentEvent.control)
                        {
                            var componentGenericMenu = new GenericMenu();

                            componentGenericMenu.AddItem(new GUIContent("Remove All Component"), false, () =>
                            {
                                selectedComponents.TryAdd(compID, component);

                                foreach (var selectedComponent in selectedComponents.ToList())
                                {
                                    if (selectedComponent.Value is Material)
                                        continue;

                                    selectedComponents.Remove(selectedComponent.Key);
                                    Undo.DestroyObjectImmediate(selectedComponent.Value);
                                }

                                selectedComponents.Clear();
                            });
                            componentGenericMenu.ShowAsContext();
                        }
                        else
                        {
                            DisplayObjectContextMenuDelegate(rect, component, 0);
                        }

                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.MouseUp)
                {
                    if (currentEvent.button == 2)
                    {
                        var inspectorComponents = selectedComponents.Select(selectedComponent => selectedComponent.Value).ToList();

                        if (!selectedComponents.ContainsKey(compID))
                            inspectorComponents.Add(component);

                        foreach (var comp in inspectorComponents) EditorUtility.OpenPropertyEditor(comp);
                        currentEvent.Use();
                        return;
                    }
                }
            }

            if (selectedComponents.Count > 0 &&
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                !currentEvent.control &&
                !rect.Contains(currentEvent.mousePosition))
            {
                selectedComponents.Clear();
                activeComponent = null;
            }
        }

        void DisplayTag()
        {
            var tagContent = new GUIContent(hierarchyObject.gameObject.tag);

            var style = Styles.Tag;
            style.normal.textColor = ThemeData.tagColor;
            Rect rect;

            if (settings.tagAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                rect = RectFromLeft(hierarchyObject.nameRect, style.CalcSize(tagContent).x, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromLeft(hierarchyObject.nameRect, 1, widthUse.afterName), ThemeData.colorGrid);
                    GUI.Label(rect, tagContent, style);
                }
            }
            else
            {
                rect = RectFromRight(hierarchyObject.rect, style.CalcSize(tagContent).x, ref widthUse.right);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromRight(hierarchyObject.rect, 1, widthUse.right), ThemeData.colorGrid);
                    GUI.Label(rect, tagContent, style);
                }
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                var menuTags = new GenericMenu();
                var gameObject = hierarchyObject.gameObject;

                menuTags.AddItem(new GUIContent("Apply All Children"), settings.applyTagTargetAndChild,
                    () => { settings.applyTagTargetAndChild = !settings.applyTagTargetAndChild; });
                menuTags.AddSeparator("");

                foreach (var tag in InternalEditorUtility.tags)
                {
                    menuTags.AddItem(new GUIContent(tag), gameObject.CompareTag(tag), () =>
                    {
                        if (settings.applyTagTargetAndChild)
                            ApplyTagTargetAndChild(gameObject.transform, tag);
                        else
                        {
                            Undo.RegisterCompleteObjectUndo(gameObject, "Change Tag");
                            gameObject.tag = tag;
                        }
                    });
                }

                menuTags.ShowAsContext();
                currentEvent.Use();
            }
        }

        void ApplyTagTargetAndChild(Transform target, string tag)
        {
            Undo.RegisterCompleteObjectUndo(target.gameObject, "Change Tag");
            target.gameObject.tag = tag;

            for (var i = 0; i < target.childCount; ++i)
                ApplyTagTargetAndChild(target.GetChild(i), tag);
        }

        void DisplayLayer()
        {
            var layerContent = new GUIContent(LayerMask.LayerToName(hierarchyObject.gameObject.layer));
            var style = Styles.Layer;
            style.normal.textColor = ThemeData.layerColor;
            Rect rect;

            if (settings.layerAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                rect = RectFromLeft(hierarchyObject.nameRect, style.CalcSize(layerContent).x, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromLeft(hierarchyObject.nameRect, 1, widthUse.afterName), ThemeData.colorGrid);
                    GUI.Label(rect, layerContent, style);
                }
            }
            else
            {
                rect = RectFromRight(hierarchyObject.rect, style.CalcSize(layerContent).x, ref widthUse.right);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromRight(hierarchyObject.rect, 1, widthUse.right), ThemeData.colorGrid);
                    GUI.Label(rect, layerContent, style);
                }
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                var menuLayers = new GenericMenu();
                var gameObject = hierarchyObject.gameObject;

                menuLayers.AddItem(new GUIContent("Apply All Children"), settings.applyLayerTargetAndChild,
                    () => { settings.applyLayerTargetAndChild = !settings.applyLayerTargetAndChild; });
                menuLayers.AddSeparator("");

                foreach (var layer in InternalEditorUtility.layers)
                {
                    menuLayers.AddItem(new GUIContent(layer),
                        LayerMask.NameToLayer(layer) == gameObject.layer, () =>
                        {
                            if (settings.applyLayerTargetAndChild)
                                ApplyLayerTargetAndChild(gameObject.transform, LayerMask.NameToLayer(layer));
                            else
                            {
                                Undo.RegisterCompleteObjectUndo(gameObject, "Change Layer");
                                gameObject.layer = LayerMask.NameToLayer(layer);
                            }
                        });
                }

                menuLayers.ShowAsContext();
                currentEvent.Use();
            }
        }

        void ApplyLayerTargetAndChild(Transform target, int layer)
        {
            Undo.RegisterCompleteObjectUndo(target.gameObject, "Change Layer");
            target.gameObject.layer = layer;

            for (var i = 0; i < target.childCount; ++i)
                ApplyLayerTargetAndChild(target.GetChild(i), layer);
        }

        void DisplayRowBackground(bool nextRow = true)
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            var rect = hierarchyObject.rect;
            rect.xMin = -1;
            rect.width += 16;

            var color = (rect.y / rect.height) % 2 == 0 ? ThemeData.colorRowEven : ThemeData.colorRowOdd;

            if (nextRow)
                rect.y += rect.height;

            var guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        void RenderGrid()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            var rect = hierarchyObject.rect;

            rect.xMin = GLOBAL_SPACE_OFFSET_LEFT;
            rect.y += 15 + .5f;
            rect.width += 16;
            rect.height = 1 / 2f;

            var guiColor = GUI.color;
            GUI.color = ThemeData.colorGrid;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        void RenderTreeView()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            var rect = hierarchyObject.rect;

            rect.width = 40;
            rect.x -= 34;
            var transform = hierarchyObject.gameObject.transform.parent;

            var guiColor = GUI.color;
            GUI.color = ThemeData.colorTreeView;

            if (transform.childCount == 1 || transform.GetChild(transform.childCount - 1) == hierarchyObject.gameObject.transform)
            {
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_L"), ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_T"), ScaleMode.ScaleToFit);
            }

            while (transform != null)
            {
                if (transform.parent == null)
                    break;

                if (transform == transform.parent.GetChild(transform.parent.childCount - 1))
                {
                    transform = transform.parent;
                    rect.x -= 14;
                    continue;
                }

                rect.x -= 14;
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_I"), ScaleMode.ScaleToFit);
                transform = transform.parent;
            }

            GUI.color = guiColor;
        }

        GUIContent tmpSceneContent = new();

        void RenameSceneInHierarchy()
        {
            var name = currentScene.name;
            if (name == "")
                return;

            if (!currentScene.isLoaded)
                name = $"{name} (not loaded)";

            tmpSceneContent.text = name == "" ? "Untitled" : name;

            if (currentEvent.type == EventType.KeyDown &&
                currentEvent.keyCode == KeyCode.F2 &&
                hierarchyObject.rect.Contains(currentEvent.mousePosition))
            {
                SceneRenamePopup.ShowPopup(currentScene);
            }
        }

        void CollapseAll()
        {
        }

        void DirtyScene(Scene scene)
        {
            if (EditorApplication.isPlaying)
                return;

            EditorSceneManager.MarkSceneDirty(scene);
        }

        bool IsFirstElement(Rect rect) => previousRowIdx > rect.y / rect.height;

        int GetRectIndex(Rect rect) => (int)(rect.y / rect.height);

        bool InSelection(int ID) => Selection.Contains(ID);

        bool IsElementDirty(int ID) => EditorUtility.IsDirty(ID);

        static object GetTreeViewIdentifier(int id)
        {
#if UNITY_6000_4_OR_NEWER
            return (EntityId)id;
#else
            return id;
#endif
        }

        Rect RectFromRight(Rect rect, float width, float usedWidth)
        {
            usedWidth += width;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        Rect RectFromRight(Rect rect, float width, ref float usedWidth)
        {
            usedWidth += width;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        Rect RectFromRight(Rect rect, Vector2 offset, float width, ref float usedWidth)
        {
            usedWidth += width;
            rect.position += offset;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        Rect RectFromLeft(Rect rect, float width, float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        Rect RectFromLeft(Rect rect, float width, ref float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        Rect RectFromLeft(Rect rect, Vector2 offset, float width, ref float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.position += offset;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        void GUISeparator(Rect rect, Color color)
        {
            var guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        struct WidthUse
        {
            public float left;
            public float right;
            public float afterName;

            public WidthUse(float left, float right, float afterName)
            {
                this.left = left;
                this.right = right;
                this.afterName = afterName;
            }

            public static WidthUse zero => new(0, 0, 0);
        }

        sealed class HierarchyWindow
        {
            public static Dictionary<int, EditorWindow> instances = new();
            public static List<HierarchyWindow> windows = new();

            public bool isFocused => editorWindow.hasFocus;

            private int instanceID;
            private EditorWindow editorWindow;
            private object treeview;

            public HierarchyWindow(EditorWindow editorWindow)
            {
                this.editorWindow = editorWindow;

                instanceID = this.editorWindow.GetInstanceID();

                instances.Add(instanceID, this.editorWindow);
                windows.Add(this);

                Debug.Log($"HierarchyWindow {instanceID} Instanced.");

                Reflection();
            }

            public void Reflection()
            {
                treeview = m_TreeView.GetValue(m_SceneHierarchy.GetValue(editorWindow));
            }

            public void Dispose()
            {
                editorWindow = null;
                treeview = null;
                instances.Remove(instanceID);
                windows.Remove(this);

                Debug.Log($"HierarchyWindow {instanceID} Disposed.");
            }

            public static HierarchyWindow GetFocused()
            {
                HierarchyWindow hierarchyWindow = null;
                foreach (var window in windows)
                {
                    if (window.isFocused) hierarchyWindow = window;
                    return hierarchyWindow;
                }

                return null;
            }
            public TreeViewItem GetItemAndRowIndex(int id, out int row)
            {
                row = -1;
                if (treeview == null) return null;
                var item = GetItemAndRowIndexMethod.Invoke(treeview, new[] {GetTreeViewIdentifier(id), row}) as TreeViewItem;
                return item;
            }

            public void SetWindowTitle(string value)
            {
                if (editorWindow == null)
                    return;

                editorWindow.titleContent.text = value;
            }
        }

        sealed class HierarchyObject
        {
            public int ID = int.MinValue;
            public Rect rect;
            public Rect nameRect;
            public int rowIndex;
            public GameObject gameObject;
            public bool isNull = true;
            public bool isPrefab;
            public bool isPrefabMissing;
            public bool isRootObject;
            public bool isSelected;
            public bool isFirstRow;
            public bool isFirstElement;
            public bool isSeparator;
            public bool isFolder;
            public bool isDirty;
            public bool isMouseHovering;
            public HierarchyFolder hierarchyFolder;
            public TreeViewItem treeViewItem;

            public string Name => isNull ? "Null" : gameObject.name;

            public int ChildCount => gameObject.transform.childCount;

            public Scene Scene => gameObject.scene;

            public bool IsStatic => !isNull && gameObject.isStatic;

            public HierarchyObject()
            {
            }

            public void Dispose()
            {
                ID = int.MinValue;
                gameObject = null;
                rect = Rect.zero;
                nameRect = Rect.zero;
                rowIndex = 0;
                isNull = true;
                isRootObject = false;
                isSelected = false;
                isFirstRow = false;
                isFirstElement = false;
                isSeparator = false;
                isFolder = false;
                isDirty = false;
                isMouseHovering = false;
            }
        }

        internal sealed class Resources
        {
            private static Texture2D pixelWhite;

            public static Texture2D PixelWhite
            {
                get
                {
                    if (pixelWhite != null) return pixelWhite;
                    pixelWhite = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    pixelWhite.SetPixel(0, 0, Color.white);
                    pixelWhite.Apply();

                    return pixelWhite;
                }
            }

            private static Texture2D alphaTexture;

            public static Texture2D AlphaTexture
            {
                get
                {
                    if (alphaTexture != null) return alphaTexture;
                    alphaTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    for (var x = 0; x < 16; ++x)
                    for (var y = 0; y < 16; ++y)
                        alphaTexture.SetPixel(x, y, Color.clear);
                    alphaTexture.Apply();

                    return alphaTexture;
                }
            }

            internal static readonly Texture lockIconOn = EditorGUIUtility.IconContent("LockIcon-On").image;

            private static Texture folderIcon;

            public static Texture FolderIcon
            {
                get
                {
                    if (folderIcon == null)
                        folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
                    return folderIcon;
                }
            }

            private static Texture emptyFolderIcon;

            public static Texture EmptyFolderIcon
            {
                get
                {
                    if (emptyFolderIcon == null)
                        emptyFolderIcon = EditorGUIUtility.IconContent("FolderEmpty Icon").image;
                    return emptyFolderIcon;
                }
            }
            private static Texture missingScriptIcon;
            public static Texture MissingScriptIcon
            {
                get
                {
                    if (missingScriptIcon == null)
                        missingScriptIcon = EditorGUIUtility.IconContent("Warning").image;
                    return missingScriptIcon;
                }
            }
        }

        internal static class Styles
        {
            internal static GUIStyle lineStyle = new GUIStyle("TV Line");

            internal static GUIStyle PR_DisabledLabel = new GUIStyle("PR DisabledLabel");

            internal static GUIStyle PR_PrefabLabel = new GUIStyle("PR PrefabLabel");

            internal static GUIStyle PR_DisabledPrefabLabel = new GUIStyle("PR DisabledPrefabLabel");

            internal static GUIStyle PR_BrokenPrefabLabel = new GUIStyle("PR BrokenPrefabLabel");

            internal static GUIStyle PR_DisabledBrokenPrefabLabel = new GUIStyle("PR DisabledBrokenPrefabLabel");

            internal static readonly GUIStyle Tag = new()
            {
                padding = new RectOffset(3, 4, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 8,
                richText = true,
                border = new RectOffset(12, 12, 8, 8),
            };

            internal static readonly GUIStyle Layer = new()
            {
                padding = new RectOffset(3, 4, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 8,
                richText = true,
                border = new RectOffset(12, 12, 8, 8),
            };

            [Obsolete]
            internal static GUIStyle DirtyLabel = new(EditorStyles.label)
            {
                padding = new RectOffset(-1, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.UpperLeft,
            };

            internal static readonly GUIStyle Header = new(TreeBoldLabel)
            {
                richText = true,
                normal = new GUIStyleState { textColor = Color.white }
            };

            internal static GUIStyle TreeBoldLabel => TreeView.DefaultStyles.boldLabel;

            internal static readonly GUIStyle TreeLabel = new(TreeView.DefaultStyles.label)
            {
                richText = true,
                normal = new GUIStyleState() { textColor = Color.white }
            };
        }

        internal sealed class MenuCommand
        {
            const int priority = 200;

            [MenuItem("Tools/Hierarchy 4/Lock Selection %l", false, priority)]
            static void SetNotEditableObject()
            {
                Undo.RegisterCompleteObjectUndo(Selection.gameObjects, "Set Selections Flag NotEditable");
                foreach (var gameObject in Selection.gameObjects)
                {
                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (!component) continue;
                        Undo.RegisterCompleteObjectUndo(component, "Set Selections Flag NotEditable");
                        component.hideFlags = HideFlags.NotEditable;
                    }
                }

                foreach (var gameObject in Selection.gameObjects)
                    gameObject.hideFlags = HideFlags.NotEditable;

                InternalEditorUtility.RepaintAllViews();
            }

            [MenuItem("Tools/Hierarchy 4/Lock Selection %l", true, priority)]
            static bool ValidateSetNotEditableObject() => Selection.gameObjects.Length > 0;

            [MenuItem("Tools/Hierarchy 4/Unlock Selection %&l", false, priority)]
            static void SetEditableObject()
            {
                Undo.RegisterCompleteObjectUndo(Selection.gameObjects, "Set Selections Flag Editable");
                foreach (var gameObject in Selection.gameObjects)
                {
                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (!component) continue;
                        Undo.RegisterCompleteObjectUndo(component, "Set Selections Flag Editable");
                        component.hideFlags = HideFlags.None;
                    }
                }

                foreach (var gameObject in Selection.gameObjects)
                    gameObject.hideFlags = HideFlags.None;

                InternalEditorUtility.RepaintAllViews();
            }

            [MenuItem("Tools/Hierarchy 4/Unlock Selection %&l", true, priority)]
            static bool ValidateSetEditableObject() => Selection.gameObjects.Length > 0;


            [MenuItem("Tools/Hierarchy 4/Move Selection Up #w", false, priority)]
            static void QuickSiblingUp()
            {
                var gameObject = Selection.activeGameObject;
                if (gameObject == null)
                    return;

                var index = gameObject.transform.GetSiblingIndex();
                if (index <= 0) return;
                var parent = gameObject.transform.parent?.gameObject;
                Undo.RegisterChildrenOrderUndo(parent != null ? parent : gameObject, $"{gameObject.name} sibling change");
                gameObject.transform.SetSiblingIndex(--index);
                EditorUtility.SetDirty(gameObject);
            }

            [MenuItem("Tools/Hierarchy 4/Move Selection Up #w", true)]
            static bool ValidateQuickSiblingUp() => Selection.activeTransform != null;

            [MenuItem("Tools/Hierarchy 4/Move Selection Down #s", false, priority)]
            static void QuickSiblingDown()
            {
                var gameObject = Selection.activeGameObject;
                if (gameObject == null)
                    return;

                var parent = gameObject.transform.parent?.gameObject;
                Undo.RegisterChildrenOrderUndo(parent != null ? parent : gameObject, $"{gameObject.name} sibling change");

                var index = gameObject.transform.GetSiblingIndex();
                gameObject.transform.SetSiblingIndex(++index);
                EditorUtility.SetDirty(gameObject);
            }

            [MenuItem("Tools/Hierarchy 4/Move Selection Down #s", true, priority)]
            static bool ValidateQuickSiblingDown() => Selection.activeTransform != null;

            [MenuItem("Tools/Hierarchy 4/Separator", priority = 0)]
            static void CreateHeaderInstance(UnityEditor.MenuCommand command)
            {
                var gameObject = new GameObject($"{instance.settings.separatorStartWith}Separator");

                Undo.RegisterCreatedObjectUndo(gameObject, "Create Separator");
                // Don't create headers as children of the selected objects because only root headers are drawn with background
                //if(command.context)
                //    Undo.SetTransformParent(gameObject.transform, ( (GameObject) command.context ).transform, "Create Header");

                Selection.activeTransform = gameObject.transform;
            }

            private static int hiddenObjectCount = 0;
            [MenuItem("Tools/Hierarchy 4/Count Hidden Object", false)]
            static void HiddenObjectCount()
            {
                hiddenObjectCount = 0;
                var scene = SceneManager.GetActiveScene();
                Debug.Log(scene.name);
                var rootGameObjects = scene.GetRootGameObjects();
                foreach (var go in rootGameObjects)
                    RecursiveHiddenCheck(go);

                Debug.Log("Total found: " + hiddenObjectCount);
            }

            static void RecursiveHiddenCheck(GameObject gameObject)
            {
                if (gameObject.hideFlags != HideFlags.None)
                {
                    Debug.Log("Found: " + gameObject.name + " " + gameObject.hideFlags.ToString());
                    ++hiddenObjectCount;
                }

                for (var i = 0; i < gameObject.transform.childCount; ++i)
                    RecursiveHiddenCheck(gameObject.transform.GetChild(i).gameObject);
            }
        }
    }
}
