#if TOOLS
#nullable enable

using System.Diagnostics;
using System.Threading.Tasks;
using Godot;

namespace RGE;

[GlobalClass, Tool]
public partial class AssetBrowser : Resource
{
    private AssetConfig? _assets;
    [Export] public AssetConfig? Assets
    {
        get => _assets;
        set {
            var gameChanged = (value != null && (value != _assets || value.Game != _assets.Game) && _dialog != null);
            _assets = value;
            if (gameChanged) {
                ResetFilePickerPath();
            }
        }
    }

    [ExportToolButton("Import Assets")]
    private Callable ImportAssets => Callable.From(ShowFilePicker);

    [ExportToolButton("Open chunk folder")]
    private Callable OpenFolder => Callable.From(() => {
        if (Assets == null || Assets.Game == SupportedGame.Unknown) {
            GD.PrintErr("Pick a game first, please");
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe") {
            UseShellExecute = false,
            Arguments = $"\"{ReachForGodot.GetChunkPath(Assets.Game)!.Replace('/', '\\')}\"",
        });
    });

    private FileDialog? _dialog;

    public void ResetFilePickerPath()
    {
        if (_dialog != null && Assets != null) {
            _dialog.CurrentPath = Assets.Paths.ChunkPath;
        }
    }

    public void ShowFilePicker()
    {
        Assets ??= AssetConfig.DefaultInstance;
        if (Assets.Game == SupportedGame.Unknown) {
            GD.PrintErr($"Please select a game in the asset config file: {Assets.ResourcePath}");
            return;
        }

        var basepath = Assets.Paths.ChunkPath;
        if (basepath == null) {
            GD.PrintErr("Chunk path not configured. Set the path to the game in editor settings and select the game in the asset browser.");
            return;
        }

        if (_dialog == null) {
            _dialog = new FileDialog();
            _dialog.Filters = ["*.mesh.*", "*.tex.*", "*.scn.*", "*.pfb.*", "*.user.*", "*.mdf2.*"];
            _dialog.Access = FileDialog.AccessEnum.Filesystem;
            _dialog.UseNativeDialog = true;
            _dialog.FileMode = FileDialog.FileModeEnum.OpenFiles;
            _dialog.CurrentPath = basepath;
            // _dialog.FileSelected += ImportAssetSync;
            _dialog.FilesSelected += (files) => _ = ImportAssetsAsync(files);
        }

        _dialog.Show();
    }

    private void ImportAssetSync(string filepath)
    {
        Debug.Assert(Assets != null);
        var resource = Importer.Import(filepath, Assets);
        GD.Print("File imported to " + PathUtils.GetLocalizedImportPath(filepath, Assets));
        if (resource is REResourceProxy proxy) {
            proxy.Import(true).Wait();
        }
    }

    private void ImportAssetsSync(string[] files)
    {
        Debug.Assert(Assets != null);
        if (files.Length == 0) {
            GD.PrintErr("Empty import file list");
            return;
        }

        ImportMultipleAssets(files).Wait();
        var importPaths = files.Select(f => PathUtils.GetLocalizedImportPath(f, Assets));
        GD.Print("Files imported to:\n" + string.Join('\n', importPaths));

        if (importPaths.FirstOrDefault(x => x != null) is string str && ResourceLoader.Exists(str)) {
            var fmt = PathUtils.GetFileFormat(files.First(x => x != null));
            if (fmt.format == RESupportedFileFormats.Scene || fmt.format == RESupportedFileFormats.Prefab) {
                EditorInterface.Singleton.CallDeferred(EditorInterface.MethodName.OpenSceneFromPath, str);
            } else {
                EditorInterface.Singleton.CallDeferred(EditorInterface.MethodName.SelectFile, str);
            }
        }
    }

    private async Task ImportAssetsAsync(string[] files)
    {
        Debug.Assert(Assets != null);
        if (files.Length == 0) {
            GD.PrintErr("Empty import file list");
            return;
        }

        await ImportMultipleAssets(files);
        var importPaths = files.Select(f => PathUtils.GetLocalizedImportPath(f, Assets));
        GD.Print("Files imported to:\n" + string.Join('\n', importPaths));

        if (importPaths.FirstOrDefault(x => x != null) is string str && ResourceLoader.Exists(str)) {
            var fmt = PathUtils.GetFileFormat(files.First(x => x != null));
            if (fmt.format == RESupportedFileFormats.Scene || fmt.format == RESupportedFileFormats.Prefab) {
                EditorInterface.Singleton.CallDeferred(EditorInterface.MethodName.OpenSceneFromPath, str);
            } else {
                EditorInterface.Singleton.CallDeferred(EditorInterface.MethodName.SelectFile, str);
            }
        }
    }

    private Task ImportMultipleAssets(string[] files)
    {
        Debug.Assert(Assets != null);
        return Task.WhenAll(files.Select(file => Importer.Import(file, Assets))
            .Select(res => (res as REResourceProxy)?.Import(true) ?? Task.CompletedTask));
    }
}
#endif