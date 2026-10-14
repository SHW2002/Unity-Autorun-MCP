using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace UnityAutorun.Scripting
{
    /// <summary>Resolves only relevant source candidates; never reads every script in the host project.</summary>
    internal sealed class AutoRunScriptSourceResolver
    {
        private const string GeneratedDirectory = "Assets/AutorunScripts/Editor";
        private readonly Dictionary<string, SourceEntry> _entries = new Dictionary<string, SourceEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, SourceEntry> _refreshEntries = new Dictionary<string, SourceEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _nameCandidates = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _assemblyCandidates = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly HashSet<string> _scopedCandidates = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, SourceEntry> _resolvedEntries = new Dictionary<string, SourceEntry>(StringComparer.Ordinal);
        private static readonly Regex NonCode = new Regex(@"//[^\r\n]*|/\*[\s\S]*?\*/|@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Compiled);
        private static readonly Regex NamespaceDeclaration = new Regex(@"\bnamespace\s+([A-Za-z_][A-Za-z_0-9]*(?:\s*\.\s*[A-Za-z_][A-Za-z_0-9]*)*)\s*[;{]", RegexOptions.Compiled);
        private static readonly Regex ClassDeclaration = new Regex(@"\bclass\s+([A-Za-z_][A-Za-z_0-9]*)\b", RegexOptions.Compiled);
        private static readonly Regex TypeDeclaration = new Regex(@"\b(?:class|struct|interface|enum|record)\s+(?:(?:class|struct)\s+)?[A-Za-z_][A-Za-z_0-9]*\b", RegexOptions.Compiled);

        public void BeginRefresh()
        {
            _nameCandidates.Clear();
            _refreshEntries.Clear();
            _assemblyCandidates.Clear();
            _scopedCandidates.Clear();
            _resolvedEntries.Clear();
            AddDirectory(GeneratedDirectory);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AutoRunScript).Assembly);
            if (package != null)
            {
                AddToolDirectories(package.assetPath);
                return;
            }

            // Asset-installed copies have no PackageInfo. Locate the contract file by its
            // asset name, then derive only this tool's two scripting directories.
            foreach (string candidate in FindByName(nameof(AutoRunScript)))
            {
                if (!candidate.EndsWith("/Scripting/Contracts/AutoRunScript.cs", StringComparison.Ordinal)) continue;
                SourceEntry source = ReadEntry(candidate);
                if (source == null || (source.Type != typeof(AutoRunScript) && !MatchesFallback(source, typeof(AutoRunScript)))) continue;
                AddToolDirectories(candidate.Substring(0, candidate.Length - "/Scripting/Contracts/AutoRunScript.cs".Length));
            }
        }

        public string Resolve(Type type)
        {
            var candidates = new HashSet<string>(_scopedCandidates, StringComparer.Ordinal);
            foreach (string candidate in FindByName(type.Name)) candidates.Add(candidate);
            var direct = new List<SourceEntry>();
            var fallback = new List<SourceEntry>();
            FindMatches(type, candidates, direct, fallback);
            if (direct.Count == 0 && fallback.Count == 0)
            {
                // A registered class can share an arbitrarily named file outside the
                // default directory. Only then inspect its own compiled assembly's
                // source list, with timestamp-cached text; never the entire project.
                FindMatches(type, GetAssemblyCandidates(type), direct, fallback);
            }
            // Partial declarations or ambiguous namespace/name evidence are not enough
            // to identify a unique editable file. Preserve null instead of guessing.
            var matches = direct.Concat(fallback).GroupBy(source => source.AbsolutePath, PathComparer).Select(group => group.First()).ToArray();
            if (matches.Length != 1) return null;
            _resolvedEntries[matches[0].AbsolutePath] = matches[0];
            return matches[0].AbsolutePath;
        }

        public bool IsSingleClassFile(string absolutePath)
        {
            SourceEntry source;
            if (absolutePath == null || !_resolvedEntries.TryGetValue(absolutePath, out source)) return false;
            // Recheck its timestamp on a delete request; an editor may have added another
            // class since catalog discovery without compiling it yet.
            source = ReadEntry(source.AssetPath, true);
            if (source == null) return false;
            EnsureText(source);
            return source.ClassNames.Length == 1 && source.TypeDeclarationCount == 1
                && !Regex.IsMatch(source.Code, @"\bpartial\s+class\b");
        }

        private void FindMatches(Type type, IEnumerable<string> candidates, List<SourceEntry> direct, List<SourceEntry> fallback)
        {
            foreach (string path in candidates)
            {
                SourceEntry source = ReadEntry(path);
                if (source == null) continue;
                if (source.Type == type) direct.Add(source);
                else if (MatchesFallback(source, type)) fallback.Add(source);
            }
        }

        private string[] GetAssemblyCandidates(Type type)
        {
            string name = type.Assembly.GetName().Name;
            string[] paths;
            if (_assemblyCandidates.TryGetValue(name, out paths)) return paths;
            var assembly = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor)
                .FirstOrDefault(candidate => candidate.name == name);
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")) + Path.DirectorySeparatorChar;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(type.Assembly);
            var candidates = new List<string>();
            foreach (string file in assembly == null ? new string[0] : assembly.sourceFiles)
            {
                if (!Path.IsPathRooted(file)) { candidates.Add(file.Replace('\\', '/')); continue; }
                string full = Path.GetFullPath(file);
                if (full.StartsWith(projectRoot, PathComparison)) candidates.Add(full.Substring(projectRoot.Length).Replace('\\', '/'));
                else if (package != null && full.StartsWith(package.resolvedPath + Path.DirectorySeparatorChar, PathComparison))
                    candidates.Add(package.assetPath + "/" + full.Substring(package.resolvedPath.Length + 1).Replace('\\', '/'));
            }
            paths = candidates.ToArray();
            _assemblyCandidates[name] = paths;
            return paths;
        }

        private void AddToolDirectories(string root)
        {
            AddDirectory(root + "/Scripting");
            AddDirectory(root + "/Editor/Scripting");
        }

        private void AddDirectory(string directory)
        {
            if (!AssetDatabase.IsValidFolder(directory)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { directory }))
                _scopedCandidates.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        private string[] FindByName(string name)
        {
            string[] candidates;
            if (_nameCandidates.TryGetValue(name, out candidates)) return candidates;
            candidates = AssetDatabase.FindAssets(name + " t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal))
                .ToArray();
            _nameCandidates[name] = candidates;
            return candidates;
        }

        private SourceEntry ReadEntry(string assetPath, bool recheckFile = false)
        {
            SourceEntry refreshed;
            if (!recheckFile && _refreshEntries.TryGetValue(assetPath, out refreshed)) return refreshed;
            string absolutePath = ResolveAbsolutePath(assetPath);
            if (!File.Exists(absolutePath)) { _entries.Remove(assetPath); _refreshEntries[assetPath] = null; return null; }
            var file = new FileInfo(absolutePath);
            SourceEntry cached;
            if (_entries.TryGetValue(assetPath, out cached) && cached.AbsolutePath == absolutePath
                && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
            { _refreshEntries[assetPath] = cached; return cached; }
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (script == null) return null;
            cached = new SourceEntry { AssetPath = assetPath, AbsolutePath = absolutePath, Type = script.GetClass(),
                LastWriteUtc = file.LastWriteTimeUtc, Length = file.Length };
            _entries[assetPath] = cached;
            _refreshEntries[assetPath] = cached;
            return cached;
        }

        private bool MatchesFallback(SourceEntry source, Type type)
        {
            if (type.IsNested || type.IsGenericType) return false;
            string assemblyName = UnityEditor.Compilation.CompilationPipeline.GetAssemblyNameFromScriptPath(source.AssetPath);
            if (string.IsNullOrEmpty(assemblyName)
                || Path.GetFileNameWithoutExtension(assemblyName) != type.Assembly.GetName().Name) return false;
            EnsureText(source);
            if (source.ClassNames.Count(name => name == type.Name) != 1) return false;
            Match declaration = ClassDeclaration.Matches(source.Code).Cast<Match>().First(match => match.Groups[1].Value == type.Name);
            var namespaces = new List<string>();
            int namespaceBraceDepth = 0;
            foreach (Match candidate in NamespaceDeclaration.Matches(source.Code))
            {
                int start = candidate.Index + candidate.Length - 1;
                bool contains = source.Code[start] == ';' ? declaration.Index > start : declaration.Index > start && declaration.Index < ClosingBrace(source.Code, start);
                if (!contains) continue;
                namespaces.Add(Regex.Replace(candidate.Groups[1].Value, @"\s", string.Empty));
                if (source.Code[start] == '{') namespaceBraceDepth++;
            }
            // Exclude nested classes, global declarations outside a namespace and
            // equal simple names belonging to a different lexical scope.
            return BraceDepth(source.Code, declaration.Index) == namespaceBraceDepth
                && string.Join(".", namespaces.ToArray()) == (type.Namespace ?? string.Empty);
        }

        private static int ClosingBrace(string code, int open)
        {
            int depth = 0;
            for (int index = open; index < code.Length; index++)
            {
                if (code[index] == '{') depth++;
                else if (code[index] == '}' && --depth == 0) return index;
            }
            return -1;
        }
        private static int BraceDepth(string code, int before)
        {
            int depth = 0;
            for (int index = 0; index < before; index++)
            {
                if (code[index] == '{') depth++;
                else if (code[index] == '}') depth--;
            }
            return depth;
        }

        private static void EnsureText(SourceEntry source)
        {
            if (source.ClassNames != null) return;
            string code = NonCode.Replace(File.ReadAllText(source.AbsolutePath), " ");
            source.Code = code;
            source.TypeDeclarationCount = TypeDeclaration.Matches(code).Count;
            source.ClassNames = ClassDeclaration.Matches(code).Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            source.Namespaces = NamespaceDeclaration.Matches(code).Cast<Match>()
                .Select(match => Regex.Replace(match.Groups[1].Value, @"\s", string.Empty)).ToArray();
        }

        private static string ResolveAbsolutePath(string assetPath)
        {
            string path = assetPath;
            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (package != null) path = Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/', '\\'));
            }
            if (!Path.IsPathRooted(path)) path = Path.Combine(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")), path);
            return Path.GetFullPath(path);
        }

        private static StringComparer PathComparer
        {
            get { return Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; }
        }
        private static StringComparison PathComparison
        {
            get { return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
        }
        private sealed class SourceEntry
        {
            public string AssetPath;
            public string AbsolutePath;
            public Type Type;
            public DateTime LastWriteUtc;
            public long Length;
            public string[] ClassNames;
            public string[] Namespaces;
            public string Code;
            public int TypeDeclarationCount;
        }
    }
}
