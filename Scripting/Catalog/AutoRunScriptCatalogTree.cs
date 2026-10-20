using System;
using System.Collections.Generic;

namespace UnityAutorun.Scripting
{
    /// <summary>In-memory directory grouping for registered scripts; no filesystem queries are required.</summary>
    public sealed class AutoRunScriptCatalogTree
    {
        public readonly AutoRunScriptCategory Root = new AutoRunScriptCategory("", "", -1);
        public readonly List<AutoRunScriptCatalogEntry> Scripts = new List<AutoRunScriptCatalogEntry>();

        public static AutoRunScriptCatalogTree Build(IEnumerable<AutoRunScriptDefinition> definitions, string projectDirectory,
            string search = null, AutoRunScriptKind? kind = null)
        {
            var tree = new AutoRunScriptCatalogTree();
            var directories = new Dictionary<string, AutoRunScriptCategory>(StringComparer.Ordinal);
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string query = (search ?? "").Trim().Replace('\\', '/');
            foreach (AutoRunScriptDefinition definition in definitions)
            {
                if (definition == null || (kind.HasValue && definition.Kind != kind.Value)) continue;
                string directory = GetDirectoryPath(definition.SourcePath, projectDirectory);
                string group = directory != null ? null : definition.Kind == AutoRunScriptKind.Basic ? "Basic" : "Other";
                string displayDirectory = group ?? directory;
                if (!Matches(definition, displayDirectory, query)) continue;
                var entry = new AutoRunScriptCatalogEntry(definition, displayDirectory);
                tree.Scripts.Add(entry);
                string name = definition.Name ?? definition.Id ?? "";
                int count;
                nameCounts.TryGetValue(name, out count);
                nameCounts[name] = count + 1;

                AutoRunScriptCategory parent = tree.Root;
                parent.ScriptCount++;
                if (group != null)
                {
                    parent = GetOrAddCategory(directories, parent, "group:" + group, group);
                    parent.ScriptCount++;
                }
                else if (directory.Length > 0)
                {
                    string key = "directory:";
                    foreach (string part in directory.Split('/'))
                    {
                        key += "/" + part;
                        parent = GetOrAddCategory(directories, parent, key, part);
                        parent.ScriptCount++;
                    }
                }
                parent.Scripts.Add(entry);
            }
            foreach (AutoRunScriptCatalogEntry entry in tree.Scripts)
            {
                string name = entry.Definition.Name ?? entry.Definition.Id ?? "";
                entry.Label = nameCounts[name] > 1 ? name + " [" + entry.Definition.Id + "]" : name;
            }
            Sort(tree.Root);
            tree.Scripts.Sort(CompareEntries);
            return tree;
        }

        /// <summary>Returns the path below either supported asset root, empty for root scripts, or null for other sources.</summary>
        public static string GetDirectoryPath(string sourcePath, string projectDirectory)
        {
            string source = NormalizePath(sourcePath);
            if (source == null) return null;
            string project = NormalizePath(projectDirectory);
            if (!string.IsNullOrEmpty(project) && source.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase))
                source = source.Substring(project.Length + 1);
            string[] roots = { "Assets/Editor/AutorunScripts/", "Assets/AutorunScripts/Editor/" };
            foreach (string root in roots)
            {
                if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                string relative = source.Substring(root.Length);
                if (relative.Length == 0 || !relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;
                int slash = relative.LastIndexOf('/');
                return slash < 0 ? "" : relative.Substring(0, slash);
            }
            return null;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string normalized = path.Replace('\\', '/');
            var parts = new List<string>();
            foreach (string part in normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Do not turn a malformed or traversing source path into a legitimate catalog category.
                if (part == "..") return null;
                if (part != ".") parts.Add(part);
            }
            return (normalized.StartsWith("/", StringComparison.Ordinal) ? "/" : "") + string.Join("/", parts.ToArray());
        }

        private static bool Matches(AutoRunScriptDefinition definition, string directory, string query)
        {
            if (query.Length == 0) return true;
            return (definition.Id + " " + definition.Name + " " + definition.Description + " " + directory
                + " " + (definition.SourcePath ?? "").Replace('\\', '/')).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static AutoRunScriptCategory GetOrAddCategory(Dictionary<string, AutoRunScriptCategory> directories,
            AutoRunScriptCategory parent, string key, string name)
        {
            AutoRunScriptCategory category;
            if (directories.TryGetValue(key, out category)) return category;
            category = new AutoRunScriptCategory(key, name, parent.Depth + 1);
            directories.Add(key, category);
            parent.Children.Add(category);
            return category;
        }

        private static void Sort(AutoRunScriptCategory category)
        {
            category.Children.Sort((left, right) => CompareNames(left.Name, right.Name));
            category.Scripts.Sort(CompareEntries);
            foreach (AutoRunScriptCategory child in category.Children) Sort(child);
        }

        private static int CompareEntries(AutoRunScriptCatalogEntry left, AutoRunScriptCatalogEntry right)
        {
            int comparison = CompareNames(left.Definition.Name ?? left.Definition.Id, right.Definition.Name ?? right.Definition.Id);
            return comparison != 0 ? comparison : string.Compare(left.Definition.Id, right.Definition.Id, StringComparison.Ordinal);
        }

        private static int CompareNames(string left, string right)
        {
            int comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return comparison != 0 ? comparison : string.Compare(left, right, StringComparison.Ordinal);
        }
    }

    public sealed class AutoRunScriptCategory
    {
        public readonly string Key;
        public readonly string Name;
        public readonly int Depth;
        public int ScriptCount;
        public readonly List<AutoRunScriptCategory> Children = new List<AutoRunScriptCategory>();
        public readonly List<AutoRunScriptCatalogEntry> Scripts = new List<AutoRunScriptCatalogEntry>();

        internal AutoRunScriptCategory(string key, string name, int depth)
        {
            Key = key;
            Name = name;
            Depth = depth;
        }
    }

    public sealed class AutoRunScriptCatalogEntry
    {
        public readonly AutoRunScriptDefinition Definition;
        public readonly string DirectoryPath;
        public string Label;

        internal AutoRunScriptCatalogEntry(AutoRunScriptDefinition definition, string directoryPath)
        {
            Definition = definition;
            DirectoryPath = directoryPath;
        }
    }
}
