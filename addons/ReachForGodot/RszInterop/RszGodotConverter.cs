namespace RGE;

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using RszTool;

public class RszGodotConverter : IDisposable
{
    private static readonly Dictionary<SupportedGame, Dictionary<string, Func<IRszContainerNode, REGameObject, RszInstance, REComponent?>>> perGameFactories = new();

    public AssetConfig AssetConfig { get; }
    public bool FullImport { get; }
    public ScnFile? ScnFile { get; private set; }
    public PfbFile? PfbFile { get; private set; }
    public UserFile? UserFile { get; private set; }

    static RszGodotConverter()
    {
        AssemblyLoadContext.GetLoadContext(typeof(RszGodotConverter).Assembly)!.Unloading += (c) => {
            var assembly = typeof(System.Text.Json.JsonSerializerOptions).Assembly;
            var updateHandlerType = assembly.GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler");
            var clearCacheMethod = updateHandlerType?.GetMethod("ClearCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            clearCacheMethod!.Invoke(null, new object?[] { null });
            perGameFactories.Clear();
        };
        InitComponents(typeof(RszGodotConverter).Assembly);
    }

    public static void InitComponents(Assembly assembly)
    {
        var componentTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<REComponentClassAttribute>() != null && !t.IsAbstract);

        foreach (var type in componentTypes) {
            if (!type.IsAssignableTo(typeof(REComponent))) {
                GD.PrintErr($"Invalid REComponentClass annotated type {type.FullName}.\nMust be a non-abstract REComponent node.");
                continue;
            }

            var attr = type.GetCustomAttribute<REComponentClassAttribute>()!;
            DefineComponentFactory(attr.Classname, (root, obj, instance) => {
                var node = (REComponent)Activator.CreateInstance(type)!;
                node.Name = attr.Classname;
                node.Setup(root, obj, instance);
                return node;
            }, attr.SupportedGames);
        }
    }

    public static void DefineComponentFactory(string componentType, Func<IRszContainerNode, REGameObject, RszInstance, REComponent?> factory, params SupportedGame[] supportedGames)
    {
        if (supportedGames.Length == 0) {
            supportedGames = ReachForGodot.GameList;
        }

        foreach (var game in supportedGames) {
            if (!perGameFactories.TryGetValue(game, out var factories)) {
                perGameFactories[game] = factories = new();
            }

            factories[componentType] = factory;
        }
    }

    public RszGodotConverter(AssetConfig paths, bool fullImport)
    {
        AssetConfig = paths;
        FullImport = fullImport;
    }

    public PackedScene? CreateProxyScene(string sourceFilePath, string importFilepath)
    {
        if (!File.Exists(sourceFilePath)) {
            GD.PrintErr("Invalid scene source file, does not exist: " + sourceFilePath);
            return null;
        }
        var relativeSourceFile = string.IsNullOrEmpty(AssetConfig.Paths.ChunkPath) ? sourceFilePath : sourceFilePath.Replace(AssetConfig.Paths.ChunkPath, "");

        Directory.CreateDirectory(ProjectSettings.GlobalizePath(importFilepath.GetBaseDir()));

        var name = sourceFilePath.GetFile().GetBaseName().GetBaseName();
        // opening the scene file mostly just to verify that it's valid
        using var scn = OpenScn(sourceFilePath);
        scn.Read();

        if (!ResourceLoader.Exists(importFilepath)) {
            var scene = new PackedScene() { ResourcePath = importFilepath };
            scene.Pack(new SceneFolder() { Game = AssetConfig.Game, Name = name, Asset = new AssetReference(relativeSourceFile) });
            ResourceSaver.Save(scene);
            return scene;
        }
        return ResourceLoader.Load<PackedScene>(importFilepath);
    }

    public PackedScene? CreateProxyPrefab(string sourceFilePath, string importFilepath)
    {
        if (!File.Exists(sourceFilePath)) {
            GD.PrintErr("Invalid prefab source file, does not exist: " + sourceFilePath);
            return null;
        }
        var relativeSourceFile = string.IsNullOrEmpty(AssetConfig.Paths.ChunkPath) ? sourceFilePath : sourceFilePath.Replace(AssetConfig.Paths.ChunkPath, "");

        Directory.CreateDirectory(ProjectSettings.GlobalizePath(importFilepath.GetBaseDir()));

        var name = sourceFilePath.GetFile().GetBaseName().GetBaseName();
        // opening the scene file mostly just to verify that it's valid
        using var file = OpenPfb(sourceFilePath);
        file.Read();

        if (!ResourceLoader.Exists(importFilepath)) {
            var scene = new PackedScene() { ResourcePath = importFilepath };
            scene.Pack(new PrefabNode() { Game = AssetConfig.Game, Name = name, Asset = new AssetReference(relativeSourceFile) });
            ResourceSaver.Save(scene);
            return scene;
        }
        return ResourceLoader.Load<PackedScene>(importFilepath);
    }

    public UserdataResource? CreateUserdata(string sourceFilePath, string importFilepath)
    {
        if (!File.Exists(sourceFilePath)) {
            GD.PrintErr("Invalid prefab source file, does not exist: " + sourceFilePath);
            return null;
        }
        var relativeSourceFile = string.IsNullOrEmpty(AssetConfig.Paths.ChunkPath) ? sourceFilePath : sourceFilePath.Replace(AssetConfig.Paths.ChunkPath, "");

        Directory.CreateDirectory(ProjectSettings.GlobalizePath(importFilepath.GetBaseDir()));

        var name = sourceFilePath.GetFile().GetBaseName().GetBaseName();

        UserdataResource userdata;
        if (ResourceLoader.Exists(importFilepath)) {
            userdata = ResourceLoader.Load<UserdataResource>(importFilepath);
        } else {
            userdata = new UserdataResource() {
                ResourceName = name,
                ResourcePath = importFilepath,
                ResourceType = RESupportedFileFormats.Userdata,
                Game = AssetConfig.Game,
                Asset = new AssetReference(relativeSourceFile)
            };
            ResourceSaver.Save(userdata);
        }
        GenerateUserdata(userdata);
        return userdata;
    }

    public void GenerateSceneTree(SceneFolder root)
    {
        var scnFullPath = Importer.ResolveSourceFilePath(root.Asset!.AssetFilename, AssetConfig);

        ScnFile?.Dispose();
        GD.Print("Opening scn file " + scnFullPath);
        ScnFile = OpenScn(scnFullPath);
        ScnFile.Read();
        ScnFile.SetupGameObjects();

        root.Clear();

        foreach (var folder in ScnFile.FolderDatas!.OrderBy(o => o.Instance!.Index)) {
            Debug.Assert(folder.Info != null);
            GenerateFolder(root, folder);
        }

        GenerateResources(root, ScnFile.ResourceInfoList, AssetConfig);

        foreach (var gameObj in ScnFile.GameObjectDatas!.OrderBy(o => o.Instance!.Index)) {
            Debug.Assert(gameObj.Info != null);
            GenerateGameObject(root, gameObj);
        }
    }

    public void GeneratePrefabTree(PrefabNode root)
    {
        var scnFullPath = Importer.ResolveSourceFilePath(root.Asset!.AssetFilename, AssetConfig);

        PfbFile?.Dispose();
        GD.Print("Opening scn file " + scnFullPath);
        PfbFile = OpenPfb(scnFullPath);
        PfbFile.Read();
        PfbFile.SetupGameObjects();

        ((IRszContainerNode)root).Clear();

        GenerateResources(root, PfbFile.ResourceInfoList, AssetConfig);

        var rootGOs = PfbFile.GameObjectDatas!.OrderBy(o => o.Instance!.Index);
        if (rootGOs.Count() > 1) {
            GD.PrintErr("WTF Capcom, why do you have multiple GameObjects in the PFB root???");
        }
        foreach (var gameObj in rootGOs) {
            Debug.Assert(gameObj.Info != null);
            GenerateGameObject(root, gameObj, root);
        }
    }

    public void GenerateUserdata(UserdataResource root)
    {
        var scnFullPath = Importer.ResolveSourceFilePath(root.Asset!.AssetFilename, AssetConfig);

        UserFile?.Dispose();
        GD.Print("Opening user file " + scnFullPath);
        UserFile = OpenUserdata(scnFullPath);
        UserFile.Read();

        root.Clear();

        GenerateResources(root, UserFile.ResourceInfoList, AssetConfig);

        if (UserFile.RSZ!.ObjectList.Skip(1).Any()) {
            GD.PrintErr("WTF Capcom, why do you have multiple objects in the userfile root???");
        }

        foreach (var instance in UserFile.RSZ!.ObjectList) {
            root.Rebuild(instance.RszClass.name, instance);
            ResourceSaver.Save(root);
            break;
        }
    }

    private void GenerateResources(IRszContainerNode root, List<ResourceInfo> resourceInfos, AssetConfig config)
    {
        var resources = new List<REResource>();
        foreach (var res in resourceInfos) {
            if (!string.IsNullOrWhiteSpace(res.Path)) {
                var resource = Importer.FindOrImportResource<Resource>(res.Path, config);
                if (resource == null) {
                    resource ??= new REResource() {
                        Asset = new AssetReference(res.Path),
                        ResourceType = Importer.GetFileFormat(res.Path).format,
                        Game = AssetConfig.Game,
                        ResourceName = res.Path.GetFile()
                    };
                } else if (resource is REResource reres) {
                    resources.Add(reres);
                } else {
                    resources.Add(new REResourceProxy() {
                        Asset = new AssetReference(res.Path),
                        ResourceType = Importer.GetFileFormat(res.Path).format,
                        ImportedResource = resource,
                        Game = AssetConfig.Game,
                        ResourceName = res.Path.GetFile()
                    });
                }
            } else {
                GD.Print("Found a resource with null path: " + resources.Count);
            }
        }
        root.Resources = resources.ToArray();
    }

    private void GenerateFolder(SceneFolder root, ScnFile.FolderData folder, SceneFolder? parent = null)
    {
        Debug.Assert(folder.Info != null);
        SceneFolder newFolder;
        if (folder.Instance?.GetFieldValue("v5") is string scnPath && !string.IsNullOrWhiteSpace(scnPath)) {
            var importPath = Importer.GetLocalizedImportPath(scnPath, AssetConfig);
            GD.Print("Importing folder " + scnPath);
            GD.Print("Importing generated folder to " + importPath);
            PackedScene scene;
            if (importPath == null) {
                GD.PrintErr("Missing scene file " + scnPath);
                return;
            } else if (!ResourceLoader.Exists(importPath)) {
                scene = Importer.ImportScene(Importer.ResolveSourceFilePath(scnPath, AssetConfig), importPath, AssetConfig)!;
                newFolder = scene.Instantiate<SceneFolder>(PackedScene.GenEditState.Instance);
            } else {
                scene = ResourceLoader.Load<PackedScene>(importPath);
                newFolder = scene.Instantiate<SceneFolder>(PackedScene.GenEditState.Instance);
            }

            if (FullImport && newFolder.IsEmpty) {
                using var childConf = new RszGodotConverter(AssetConfig, FullImport);
                childConf.GenerateSceneTree(newFolder);
                scene.Pack(newFolder);
                ResourceSaver.Save(scene);
            }

            (parent ?? root).AddFolder(newFolder);
        } else {
            newFolder = new SceneFolder() {
                ObjectId = folder.Info.Data.objectId,
                Game = root.Game,
                Name = folder.Name ?? "UnnamedFolder"
            };
            (parent ?? root).AddFolder(newFolder);
        }

        foreach (var child in folder.Children) {
            GenerateFolder(root, child, newFolder);
        }
    }

    private void GenerateGameObject(PrefabNode root, PfbFile.GameObjectData data, REGameObject? parent = null)
    {
        Debug.Assert(data.Info != null);

        var newGameobj = new REGameObject() {
            ObjectId = data.Info.Data.objectId,
            Name = data.Name ?? "UnnamedGameobject",
            Uuid = Guid.NewGuid().ToString(),
            Enabled = true, // TODO which gameobject field is enabled?
            // Enabled = gameObj.Instance.GetFieldValue("v2")
        };
        ((IRszContainerNode)root).AddGameObject(newGameobj, parent);

        foreach (var comp in data.Components.OrderBy(o => o.Index)) {
            SetupComponent(root, comp, newGameobj);
        }

        foreach (var child in data.Children.OrderBy(o => o.Instance!.Index)) {
            GenerateGameObject(root, child, newGameobj);
        }
    }

    private void GenerateGameObject(IRszContainerNode root, ScnFile.GameObjectData data, REGameObject? parent = null)
    {
        Debug.Assert(data.Info != null);

        REGameObject? newGameobj = null;
        if (data.Prefab?.Path != null) {
            var res = Importer.FindOrImportResource<PackedScene>(data.Prefab.Path, AssetConfig);
            // TODO should we require instantiation here?
            if (res != null) {
                newGameobj = res.Instantiate<PrefabNode>(PackedScene.GenEditState.Instance);
            }
        }

        newGameobj ??= new REGameObject() {
            ObjectId = data.Info.Data.objectId,
            Name = data.Name ?? "UnnamedObject",
            Uuid = data.Info.Data.guid.ToString(),
            Prefab = data.Prefab?.Path,
            Enabled = true, // TODO which gameobject field is enabled?
            // Enabled = gameObj.Instance.GetFieldValue("v2")
        };
        root.AddGameObject(newGameobj, parent);

        foreach (var comp in data.Components.OrderBy(o => o.Index)) {
            SetupComponent(root, comp, newGameobj);
        }

        foreach (var child in data.Children.OrderBy(o => o.Instance!.Index)) {
            GenerateGameObject(root, child, newGameobj);
        }
    }

    private RszFileOption CreateFileOption() => new RszFileOption(
        AssetConfig.Paths.GetRszToolGameEnum(),
        AssetConfig.Paths.RszJsonPath ?? throw new Exception("Rsz json file not specified for game " + AssetConfig.Game));

    private ScnFile OpenScn(string filename)
    {
        return new ScnFile(CreateFileOption(), new FileHandler(filename));
    }

    private PfbFile OpenPfb(string filename)
    {
        return new PfbFile(CreateFileOption(), new FileHandler(filename));
    }

    private UserFile OpenUserdata(string filename)
    {
        return new UserFile(CreateFileOption(), new FileHandler(filename));
    }

    private void SetupComponent(IRszContainerNode root, RszInstance instance, REGameObject gameObject)
    {
        if (root.Game == SupportedGame.Unknown) {
            GD.PrintErr("Game required on rsz container root for SetupComponent");
            return;
        }

        REComponent? componentInfo;
        if (!perGameFactories.TryGetValue(root.Game, out var factories)) {
            return;
        }
        if (factories.TryGetValue(instance.RszClass.name, out var factory)) {
            componentInfo = factory.Invoke(root, gameObject, instance);
            if (componentInfo == null) {
                componentInfo = new REComponentPlaceholder() { Name = instance.RszClass.name };
                gameObject.AddComponent(componentInfo);
            } else if (gameObject.GetComponent(instance.RszClass.name) == null) {
                gameObject.AddComponent(componentInfo);
            }
        } else {
            componentInfo = new REComponentPlaceholder() { Name = instance.RszClass.name };
            gameObject.AddComponent(componentInfo);
        }

        componentInfo.Data = new REObject(root.Game, instance.RszClass.name, instance);
        componentInfo.ObjectId = instance.Index;
    }

    public void Dispose()
    {
        ScnFile?.Dispose();
        GC.SuppressFinalize(this);
    }
}
