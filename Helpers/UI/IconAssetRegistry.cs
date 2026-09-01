using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OnlyWar.Helpers.UI
{
    /// <summary>
    /// Resolves logical icon asset keys to Godot textures. A manifest may point
    /// at a standalone image or at a region of an atlas; callers do not depend on
    /// that storage detail.
    /// </summary>
    public static class IconAssetRegistry
    {
        private sealed record IconAssetDefinition(string Key, string ResourcePath,
                                                   Rect2? Region);

        private static readonly Dictionary<string, IconAssetDefinition> Definitions = [];
        private static readonly Dictionary<string, Texture2D> Textures = [];
        private static bool _builtInManifestAttempted;

        public static void RegisterManifest(string manifestPath, string packageId = "core")
        {
            if (string.IsNullOrWhiteSpace(manifestPath)) return;
            packageId = string.IsNullOrWhiteSpace(packageId) ? "core" : packageId.Trim();

            string filePath = ToFilePath(manifestPath);
            if (!File.Exists(filePath))
            {
                GD.PushWarning($"Icon manifest not found: {manifestPath}");
                return;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
                JsonElement root = document.RootElement;
                string atlasPath = ReadString(root, "atlas");
                JsonElement icons = root.TryGetProperty("icons", out JsonElement iconElement)
                    ? iconElement
                    : default;
                if (icons.ValueKind != JsonValueKind.Object) return;

                foreach (JsonProperty icon in icons.EnumerateObject())
                {
                    string key = NamespacedKey(icon.Name, packageId);
                    JsonElement value = icon.Value;
                    string resourcePath = ReadString(value, "path")
                        ?? ReadString(value, "resource")
                        ?? ReadString(value, "atlas")
                        ?? atlasPath;
                    if (string.IsNullOrWhiteSpace(resourcePath)) continue;

                    resourcePath = ResolveResourcePath(manifestPath, resourcePath);
                    Rect2? region = ReadRegion(value);
                    Definitions[key] = new IconAssetDefinition(key, resourcePath, region);

                    // Built-in callers historically use unqualified keys. Keep an
                    // alias for core content while all mod content remains namespaced.
                    if (string.Equals(packageId, "core", StringComparison.OrdinalIgnoreCase))
                    {
                        Definitions[icon.Name] = new IconAssetDefinition(
                            icon.Name, resourcePath, region);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                GD.PushWarning($"Icon manifest failed to load: {manifestPath} ({exception.Message})");
            }
        }

        public static bool HasIcon(string key)
        {
            EnsureBuiltInManifest();
            return Definitions.ContainsKey(NormalizeKey(key));
        }

        public static Texture2D Resolve(string key)
        {
            EnsureBuiltInManifest();
            string normalizedKey = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return null;
            if (Textures.TryGetValue(normalizedKey, out Texture2D cached)) return cached;
            if (!Definitions.TryGetValue(normalizedKey, out IconAssetDefinition definition))
            {
                return null;
            }

            Texture2D source = LoadTexture(definition.ResourcePath);
            if (source == null) return null;

            Texture2D result = source;
            if (definition.Region.HasValue)
            {
                result = new AtlasTexture
                {
                    Atlas = source,
                    Region = definition.Region.Value
                };
            }
            Textures[normalizedKey] = result;
            return result;
        }

        public static void ClearRegisteredMods()
        {
            List<string> modKeys = [];
            foreach (string key in Definitions.Keys)
            {
                if (key.Contains(':')) modKeys.Add(key);
            }
            foreach (string key in modKeys)
            {
                Definitions.Remove(key);
                Textures.Remove(key);
            }
        }

        public static void ClearPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return;
            string prefix = packageId.Trim() + ":";
            List<string> packageKeys = Definitions.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (string key in packageKeys)
            {
                Definitions.Remove(key);
                Textures.Remove(key);
            }
        }

        private static void EnsureBuiltInManifest()
        {
            if (_builtInManifestAttempted) return;
            _builtInManifestAttempted = true;
            RegisterManifest("res://Assets/UI/Icons/icon_atlas_manifest.json");
        }

        private static Texture2D LoadTexture(string path)
        {
            if (path.StartsWith("res://", StringComparison.Ordinal)
                || path.StartsWith("user://", StringComparison.Ordinal))
            {
                return GD.Load<Texture2D>(path);
            }

            Image image = Image.LoadFromFile(path);
            return image == null ? null : ImageTexture.CreateFromImage(image);
        }

        private static string ToFilePath(string path)
        {
            if (path.StartsWith("res://", StringComparison.Ordinal))
            {
                // Keep manifest discovery usable from the plain .NET test host too; invoking
                // ProjectSettings here would initialize Godot's native runtime before tests
                // have created a Godot project context. Runtime texture loading still uses the
                // original res:// path through GD.Load.
                return FindProjectRelativeFile(path[6..]);
            }
            if (path.StartsWith("user://", StringComparison.Ordinal))
            {
                return FindProjectRelativeFile(path[7..]);
            }
            return Path.GetFullPath(path);
        }

        private static string FindProjectRelativeFile(string relativePath)
        {
            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            DirectoryInfo directory = new(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, normalized);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, normalized);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            return Path.GetFullPath(normalized);
        }

        private static string ResolveResourcePath(string manifestPath, string resourcePath)
        {
            if (resourcePath.StartsWith("res://", StringComparison.Ordinal)
                || resourcePath.StartsWith("user://", StringComparison.Ordinal)
                || Path.IsPathRooted(resourcePath))
            {
                return resourcePath;
            }

            if (manifestPath.StartsWith("res://", StringComparison.Ordinal)
                || manifestPath.StartsWith("user://", StringComparison.Ordinal))
            {
                string directory = manifestPath.Replace('\\', '/');
                int separator = directory.LastIndexOf('/');
                return separator < 0
                    ? resourcePath
                    : directory[..(separator + 1)] + resourcePath.Replace('\\', '/');
            }

            return Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(manifestPath) ?? string.Empty, resourcePath));
        }

        private static string NamespacedKey(string key, string packageId) =>
            string.Equals(packageId, "core", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(packageId + ":", StringComparison.Ordinal)
                ? key
                : $"{packageId}:{key}";

        private static string NormalizeKey(string key) =>
            key?.StartsWith("core:", StringComparison.OrdinalIgnoreCase) == true
                ? key[5..]
                : key;

        private static string ReadString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static Rect2? ReadRegion(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("x", out JsonElement x)
                || !element.TryGetProperty("y", out JsonElement y)
                || !element.TryGetProperty("w", out JsonElement w)
                || !element.TryGetProperty("h", out JsonElement h))
            {
                return null;
            }
            return new Rect2(x.GetSingle(), y.GetSingle(), w.GetSingle(), h.GetSingle());
        }
    }
}
