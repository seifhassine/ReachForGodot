namespace ReaGE;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Godot;
using RszTool;

public static partial class TypeCache
{
    private sealed class GameClassCache
    {
        public SupportedGame game;
        public readonly Dictionary<string, ClassInfo> serializationCache = new();
        public readonly Dictionary<string, List<REFieldAccessor>> fieldOverrides = new();

        private Dictionary<string, Dictionary<string, PrefabGameObjectRefProperty>>? gameObjectRefProps;
        public Dictionary<string, Dictionary<string, PrefabGameObjectRefProperty>> GameObjectRefProps
            => gameObjectRefProps ??= DeserializeOrNull(ReachForGodot.GetPaths(game)?.PfbGameObjectRefPropsPath, gameObjectRefProps) ?? new(0);

        private RszParser? parser;
        public RszParser Parser => parser ??= LoadRszParser();

        private Il2cppCache? il2CppCache;
        public Il2cppCache Il2cppCache => il2CppCache ??= LoadIl2cppData(ReachForGodot.GetAssetConfig(game).Paths);

        private Dictionary<string, RszClassPatch> rszTypePatches = new();

        public GameClassCache(SupportedGame game)
        {
            this.game = game;
        }

        public Dictionary<string, PrefabGameObjectRefProperty>? GetClassProps(string classname)
        {
            if (GameObjectRefProps.TryGetValue(classname, out var result)) {
                return result;
            }

            return null;
        }

        public RszClassPatch FindOrCreateClassPatch(string classname)
        {
            if (!rszTypePatches.TryGetValue(classname, out var props)) {
                rszTypePatches[classname] = props = new();
            }
            return props;
        }

        public void UpdateRszPatches(AssetConfig config)
        {
            Directory.CreateDirectory(config.Paths.RszPatchPath.GetBaseDir());
            using var file = File.Create(config.Paths.RszPatchPath);
            JsonSerializer.Serialize(file, rszTypePatches, jsonOptions);
        }

        public void UpdateClassProps(string classname, Dictionary<string, PrefabGameObjectRefProperty> propInfoDict)
        {
            var reflist = GameObjectRefProps;
            reflist[classname] = propInfoDict;
            var fn = ReachForGodot.GetPaths(game)?.PfbGameObjectRefPropsPath ?? throw new Exception("Missing pfb cache filepath for " + game);
            using var fs = File.Create(fn);
            JsonSerializer.Serialize<Dictionary<string, Dictionary<string, PrefabGameObjectRefProperty>>>(fs, reflist, jsonOptions);
        }

        private RszParser LoadRszParser()
        {
            var paths = ReachForGodot.GetPaths(game);
            var jsonPath = paths?.RszJsonPath;
            if (jsonPath == null || paths == null) {
                GD.PrintErr("No rsz json defined for game " + game);
                return null!;
            }

            GD.Print("Loading RSZ data...");
            var time = new Stopwatch();
            time.Start();
            var parser = RszParser.GetInstance(jsonPath);
            parser.ReadPatch(GamePaths.RszPatchGlobalPath);
            rszTypePatches = DeserializeOrNull(paths.RszPatchPath, rszTypePatches) ?? new();
            parser.ReadPatch(paths.RszPatchPath);
            foreach (var (cn, accessors) in fieldOverrides) {
                foreach (var acc in accessors) {
                    var cls = parser.GetRSZClass(cn)!;
                    GenerateObjectCache(this, cls);
                }
            }
            time.Stop();
            GD.Print("Loaded RSZ data in " + time.Elapsed);
            return parser;
        }

        private Il2cppCache LoadIl2cppData(GamePaths paths)
        {
            GD.Print("Loading il2cpp data...");
            var time = new Stopwatch();
            time.Start();
            il2CppCache = new Il2cppCache();
            var baseCacheFile = paths.EnumCacheFilename;
            var overrideFile = paths.EnumOverridesFilename;
            if (File.Exists(baseCacheFile)) {
                if (!File.Exists(paths.Il2cppPath)) {
                    var success = TryApplyIl2cppCache(il2CppCache, baseCacheFile);
                    TryApplyIl2cppCache(il2CppCache, overrideFile);
                    if (!success) {
                        GD.PrintErr("Failed to load il2cpp cache data from " + baseCacheFile);
                    }
                    GD.Print("Loaded previously cached il2cpp data in " + time.Elapsed);
                    return il2CppCache;
                }

                var cacheLastUpdate = File.GetLastWriteTimeUtc(paths.Il2cppPath!);
                var il2cppLastUpdate = File.GetLastWriteTimeUtc(paths.Il2cppPath!);
                if (il2cppLastUpdate <= cacheLastUpdate) {
                    var existingCacheWorks = TryApplyIl2cppCache(il2CppCache, baseCacheFile);
                    TryApplyIl2cppCache(il2CppCache, overrideFile);
                    GD.Print("Loaded cached il2cpp data in " + time.Elapsed);
                    if (existingCacheWorks) return il2CppCache;
                }
            }

            if (!File.Exists(paths.Il2cppPath)) {
                GD.PrintErr($"Il2cpp file does not exist, nor do we have an enum cache file yet for {paths.Game}. Enums won't show up properly.");
                return il2CppCache;
            }

            var entries = DeserializeOrNull<REFDumpFormatter.SourceDumpRoot>(paths.Il2cppPath)
                ?? throw new Exception("File is not a valid dump json file");
            il2CppCache.ApplyIl2cppData(entries);
            GD.Print("Loaded source il2cpp data in " + time.Elapsed);

            GD.Print("Updating il2cpp cache... " + baseCacheFile);
            Directory.CreateDirectory(baseCacheFile.GetBaseDir());
            using var outfs = File.Create(baseCacheFile);
            JsonSerializer.Serialize(outfs, il2CppCache.ToCacheData(), jsonOptions);
            outfs.Close();

            TryApplyIl2cppCache(il2CppCache, overrideFile);
            return il2CppCache;
        }

        private bool TryApplyIl2cppCache(Il2cppCache target, string cacheFilename)
        {
            if (TryDeserialize<Il2cppCacheData>(cacheFilename, out var data)) {
                target.ApplyCacheData(data);
                return true;
            }
            return false;
        }

        private T? DeserializeOrNull<T>(string? filepath) where T : class => DeserializeOrNull<T>(filepath, default);
        private T? DeserializeOrNull<T>(string? filepath, T? _) where T : class
        {
            if (File.Exists(filepath)) {
                using var fs = File.OpenRead(filepath);
                return JsonSerializer.Deserialize<T>(fs, jsonOptions);
            }
            return null;
        }

        private bool TryDeserialize<T>(string? filepath, [MaybeNullWhen(false)] out T result)
        {
            if (File.Exists(filepath)) {
                using var fs = File.OpenRead(filepath);
                result = JsonSerializer.Deserialize<T>(fs, jsonOptions);
                return result != null;
            }
            result = default;
            return false;
        }
    }
}
