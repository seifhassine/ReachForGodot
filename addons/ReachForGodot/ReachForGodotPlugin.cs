using Godot;
using GC = Godot.Collections;

#if TOOLS
namespace ReaGE;
#nullable enable

[Tool]
public partial class ReachForGodotPlugin : EditorPlugin, ISerializationListener
{
    public static ReachForGodotPlugin Instance => _pluginInstance!;

    private const string SettingBase = "reach_for_godot";

    private const string Setting_BlenderPath = "filesystem/import/blender/blender_path";
    private const string Setting_GameChunkPath = $"{SettingBase}/paths/{{game}}/game_chunk_path";
    private const string Setting_Il2cppPath = $"{SettingBase}/paths/{{game}}/il2cpp_dump_file";
    private const string Setting_RszJsonPath = $"{SettingBase}/paths/{{game}}/rsz_json_file";
    private const string Setting_AdditionalPaths = $"{SettingBase}/paths/{{game}}/additional_paths";

    private const string Setting_ImportMeshMaterials = $"{SettingBase}/general/import_mesh_materials";
    private const string Setting_SceneFolderProxyThreshold = $"{SettingBase}/general/create_scene_proxy_node_threshold";

    public static string? BlenderPath => EditorInterface.Singleton.GetEditorSettings().GetSetting(Setting_BlenderPath).AsString();

    public static bool IncludeMeshMaterial { get; private set; }
    public static int SceneFolderProxyThreshold { get; private set; }

    private static string ChunkPathSetting(SupportedGame game) => Setting_GameChunkPath.Replace("{game}", game.ToString());
    private static string Il2cppPathSetting(SupportedGame game) => Setting_Il2cppPath.Replace("{game}", game.ToString());
    private static string RszPathSetting(SupportedGame game) => Setting_RszJsonPath.Replace("{game}", game.ToString());
    private static string AdditionalPathSetting(SupportedGame game) => Setting_AdditionalPaths.Replace("{game}", game.ToString());

    private static ReachForGodotPlugin? _pluginInstance;

    private EditorInspectorPlugin[] inspectors = null!;
    private EditorContextMenuPlugin[] contextMenus = null!;
    private EditorNode3DGizmoPlugin[] gizmos = Array.Empty<EditorNode3DGizmoPlugin>();
    private PopupMenu toolMenu = null!;
    private AssetBrowser? browser;

    public override void _EnterTree()
    {
        _pluginInstance = this;
        AddSettings();

        toolMenu = new PopupMenu() { Title = "RE ENGINE" };
        AddToolSubmenuItem(toolMenu.Title, toolMenu);

        EditorInterface.Singleton.GetEditorSettings().SettingsChanged += OnProjectSettingsChanged;
        OnProjectSettingsChanged();

        inspectors = new EditorInspectorPlugin[] {
            new REObjectInspectorPlugin(),
            new SceneFolderInspectorPlugin(),
            new AssetReferenceInspectorPlugin(),
            new ResourceInspectorPlugin(),
            new AssetExportInspectorPlugin(),
            new GameObjectInspectorPlugin(),
        };
        foreach (var i in inspectors) AddInspectorPlugin(i);

        contextMenus = new EditorContextMenuPlugin[1];
        AddContextMenuPlugin(EditorContextMenuPlugin.ContextMenuSlot.SceneTree, contextMenus[0] = new SceneFolderContextMenuPlugin());

        gizmos = new EditorNode3DGizmoPlugin[] { new MeshGizmo() };
        foreach (var gizmo in gizmos) AddNode3DGizmoPlugin(gizmo);

        RefreshToolMenu();
        toolMenu.IdPressed += (id) => {
            if (id < 100) {
                var game = (SupportedGame)id;
                OpenAssetImporterWindow(game);
            }
            if (id == 100) UpgradeMaterialResources();
            if (id == 101) UpgradeRcolResources();
        };
    }

    private void RefreshToolMenu()
    {
        toolMenu.Clear();
        foreach (var game in ReachForGodot.GameList) {
            var pathSetting = ChunkPathSetting(game);
            if (!string.IsNullOrEmpty(EditorInterface.Singleton.GetEditorSettings().GetSetting(pathSetting).AsString())) {
                toolMenu.AddItem($"Import {game} assets...", (int)game);
            }
        }

        // toolMenu.AddItem("Upgrade all material resources", 100);
        // toolMenu.AddItem("Upgrade all Rcol resources", 101);
    }

    public void OpenAssetImporterWindow(SupportedGame game)
    {
        var config = ReachForGodot.GetAssetConfig(game);
        if (config == null) {
            GD.PrintErr("Could not resolve asset config for game " + game);
            return;
        }

        OpenAssetImporterWindow(config);
    }

    public void OpenAssetImporterWindow(AssetConfig config)
    {
        browser ??= new AssetBrowser();
        browser.Assets = config;
        browser.CallDeferred(AssetBrowser.MethodName.ShowFilePicker);
    }

    private void UpgradeMaterialResources()
    {
        foreach (var (file, current) in FindUpgradeableResources("*.mdf2.*", (current) => current is not MaterialResource)) {
            Importer.ImportMaterial(current.Asset!.AssetFilename, file, ReachForGodot.GetAssetConfig(current.Game));
        }
    }

    private void UpgradeRcolResources()
    {
        foreach (var (file, current) in FindUpgradeableResources("*.rcol.tres", (current) => current is not RcolResource)) {
            Importer.ImportRcol(current.Asset!.AssetFilename, file, ReachForGodot.GetAssetConfig(current.Game));
        }
    }

    private IEnumerable<(string file, REResource current)> FindUpgradeableResources(string searchPattern, Func<REResource, bool> upgradeCondition)
    {
        var list = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://"), searchPattern, SearchOption.AllDirectories);
        foreach (var file in list) {
            if (file.Contains(".godot")) continue;

            var localized = ProjectSettings.LocalizePath(file);
            var current = ResourceLoader.Load<REResource>(localized);
            if (current?.Asset == null || !upgradeCondition(current)) {
                continue;
            }

            GD.Print(localized);
            yield return (file, current);
        }
        GD.Print("You may need to reopen any scenes referencing the upgraded resources");
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(toolMenu.Title);
        browser = null;
        foreach (var insp in inspectors) RemoveInspectorPlugin(insp);
        foreach (var menu in contextMenus) RemoveContextMenuPlugin(menu);
        foreach (var gizmo in gizmos) RemoveNode3DGizmoPlugin(gizmo);
    }

    private void AddSettings()
    {
        AddEditorSetting(Setting_ImportMeshMaterials, Variant.Type.Bool, false);
        AddEditorSetting(Setting_SceneFolderProxyThreshold, Variant.Type.Int, 500, PropertyHint.Range, "50,5000,or_greater,hide_slider");
        foreach (var game in ReachForGodot.GameList) {
            AddEditorSetting(ChunkPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalDir);
            AddEditorSetting(Il2cppPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalFile, "*.json");
            AddEditorSetting(RszPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalFile, "*.json");
            AddEditorSetting(AdditionalPathSetting(game), Variant.Type.PackedStringArray, string.Empty, PropertyHint.GlobalDir);
        }
    }

    public static void ReloadSettings()
    {
        _pluginInstance?.OnProjectSettingsChanged();
    }

    private void OnProjectSettingsChanged()
    {
        var settings = EditorInterface.Singleton.GetEditorSettings();
        IncludeMeshMaterial = settings.GetSetting(Setting_ImportMeshMaterials).AsBool();
        SceneFolderProxyThreshold = settings.GetSetting(Setting_SceneFolderProxyThreshold).AsInt32();
        foreach (var game in ReachForGodot.GameList) {
            var pathChunks = settings.GetSetting(ChunkPathSetting(game)).AsString() ?? string.Empty;
            var pathIl2cpp = settings.GetSetting(Il2cppPathSetting(game)).AsString();
            var pathRsz = settings.GetSetting(RszPathSetting(game)).AsString();
            var additional = settings.GetSetting(AdditionalPathSetting(game)).AsStringArray()
                .Select(path => {
                    var parts = path.Split('|');
                    string? label = parts.Length >= 2 ? parts[0] : null;
                    var filepath = PathUtils.NormalizeSourceFolderPath((parts.Length >= 2 ? parts[1] : parts[0]));
                    return new LabelledPathSetting(filepath, label);
                }).ToArray();

            if (string.IsNullOrWhiteSpace(pathChunks)) {
                ReachForGodot.SetConfiguration(game, null, null);
            } else {
                pathChunks = PathUtils.NormalizeSourceFolderPath(pathChunks);
                ReachForGodot.SetConfiguration(game, null, new GamePaths(game, pathChunks, pathIl2cpp, pathRsz, additional));
            }
        }
        RefreshToolMenu();
    }

    private void AddProjectSetting(string name, Variant.Type type, Variant initialValue)
    {
        if (ProjectSettings.HasSetting(name)) {
            return;
        }

        var dict = new GC.Dictionary();
        dict.Add("name", name);
        dict.Add("type", (int)type);
        dict.Add("hint", (int)PropertyHint.None);

        ProjectSettings.Singleton.Set(name, initialValue);
        ProjectSettings.SetInitialValue(name, initialValue);
        ProjectSettings.AddPropertyInfo(dict);
    }

    private void AddEditorSetting(string name, Variant.Type type, Variant initialValue, PropertyHint hint = PropertyHint.None, string? hintstring = null)
    {
        var settings = EditorInterface.Singleton.GetEditorSettings();
        if (!settings.HasSetting(name)) {
            settings.Set(name, initialValue);
        }

        var dict = new GC.Dictionary();
        dict.Add("name", name);
        dict.Add("type", (int)type);
        dict.Add("hint", (int)hint);
        if (hintstring != null) {
            dict.Add("hint_string", hintstring);
        }

        settings.SetInitialValue(name, initialValue, false);
        settings.AddPropertyInfo(dict);
    }

    public void OnBeforeSerialize()
    {
        toolMenu.Clear();
    }

    public void OnAfterDeserialize()
    {
        RefreshToolMenu();
        _pluginInstance = this;
        OnProjectSettingsChanged();
    }
}

#endif //TOOLS
