using System.Collections.Generic;
using System.IO;
using Godot;
using RszTool;
using GC = Godot.Collections;

#if TOOLS
namespace RFG;
#nullable enable

[Tool]
public partial class ReachForGodot : EditorPlugin
{
    public static readonly string[] GameList = ["DragonsDogma2", "DevilMayCry5", "MonsterHunterWilds"];
    private const string SettingBase = "reach_for_godot";
    private const string Setting_BlenderPath = "filesystem/import/blender/blender_path";
    private const string Setting_GameChunkPath = $"{SettingBase}/paths/{{game}}/game_chunk_path";
    private const string Setting_Il2cppPath = $"{SettingBase}/paths/{{game}}/il2cpp_dump_file";
    private const string Setting_RszJsonPath = $"{SettingBase}/paths/{{game}}/rsz_json_file";

    private static readonly Dictionary<string, (AssetConfig? config, GamePaths paths)> assetConfigData = new();

    public static string BlenderPath => EditorInterface.Singleton.GetEditorSettings().GetSetting(Setting_BlenderPath).AsString()
        ?? throw new System.Exception("Blender path not defined in editor settings");

    public static GamePaths? GetPaths(string game)
    {
        if (assetConfigData.Count == 0) OnProjectSettingsChanged();
        return assetConfigData.TryGetValue(game, out var data) ? data.paths : null;
    }

    public static AssetConfig GetAssetConfig(string? game)
    {
        if (assetConfigData.Count == 0) OnProjectSettingsChanged();
        if (game == null) return AssetConfig.DefaultInstance;

        if (assetConfigData.TryGetValue(game, out var data)) {
            if (data.config != null) {
                return data.config;
            }
        }

        var defaultResourcePath = "res://asset_config_" + game.ToSnakeCase();
        if (ResourceLoader.Exists(defaultResourcePath)) {
            data.config = ResourceLoader.Load<AssetConfig>(defaultResourcePath);
        } else {
            var da = DirAccess.Open("res://");
            string file;
            da.ListDirBegin();
            while ((file = da.GetNext()) != string.Empty) {
                if (ResourceLoader.Exists(file, nameof(AssetConfig))) {
                    var cfg = ResourceLoader.Load<Resource>(file) as AssetConfig;
                    if (cfg != null && cfg.Game == game) {
                        da.ListDirEnd();
                        data.config = cfg;
                        break;
                    }
                }
            }
        }

        if (data.config == null) {
            data.config = new AssetConfig() { ResourcePath = defaultResourcePath, AssetDirectory = data.paths.GetRszToolGameEnum().ToString() };
            ResourceSaver.Save(data.config);
        }

        if (data.paths != null) {
            assetConfigData[game] = data;
        }

        return data.config;
    }

    public static string? GetChunkPath(string game) => GetPaths(game)?.ChunkPath;

    private static string ChunkPathSetting(string game) => Setting_GameChunkPath.Replace("{game}", game);
    private static string Il2cppPathSetting(string game) => Setting_Il2cppPath.Replace("{game}", game);
    private static string RszPathSetting(string game) => Setting_RszJsonPath.Replace("{game}", game);

    public override void _EnterTree()
    {
        AddSettings();

        EditorInterface.Singleton.GetEditorSettings().SettingsChanged += OnProjectSettingsChanged;
        OnProjectSettingsChanged();
    }

    private void AddSettings()
    {
        foreach (var game in GameList) {
            AddEditorSetting(ChunkPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalDir);
            AddEditorSetting(Il2cppPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalFile, "*.json");
            AddEditorSetting(RszPathSetting(game), Variant.Type.String, string.Empty, PropertyHint.GlobalFile, "*.json");
        }
    }

    private static void OnProjectSettingsChanged()
    {
        var settings = EditorInterface.Singleton.GetEditorSettings();
        foreach (var game in GameList) {
            var pathChunks = settings.GetSetting(ChunkPathSetting(game)).AsString() ?? string.Empty;
            var pathIl2cpp = settings.GetSetting(Il2cppPathSetting(game)).AsString();
            var pathRsz = settings.GetSetting(RszPathSetting(game)).AsString();

            if (string.IsNullOrWhiteSpace(pathChunks)) {
                assetConfigData.Remove(game);
            } else {
                pathChunks = pathChunks.Replace('\\', '/');
                if (!pathChunks.EndsWith('/')) {
                    pathChunks = pathChunks + '/';
                }

                assetConfigData[game] = (null, new GamePaths(game, pathChunks, pathIl2cpp, pathRsz));
            }
        }
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
}

public record GamePaths(string Game, string ChunkPath, string? Il2cppPath, string? RszJsonPath)
{
    public GameName GetRszToolGameEnum()
    {
        switch (Game) {
            case "DragonsDogma2": return GameName.dd2;
            case "DevilMayCry5": return GameName.dmc5;
            case "ResidentEvil2": return GameName.re2;
            case "ResidentEvil2RT": return GameName.re2rt;
            case "ResidentEvil3": return GameName.re3;
            case "ResidentEvil3RT": return GameName.re3rt;
            case "ResidentEvil4": return GameName.re4;
            case "ResidentEvil7": return GameName.re7;
            case "ResidentEvil7RT": return GameName.re7rt;
            case "ResidentEvil8": return GameName.re8;
            case "MonsterHunterRise": return GameName.mhrise;
            case "StreetFighter6": return GameName.sf6;
            case "MonsterHunterWilds": return GameName.unknown;
            default: return GameName.unknown;
        }
    }
}

#endif //TOOLS
