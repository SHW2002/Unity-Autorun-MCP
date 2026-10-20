using System;
using System.Collections.Generic;
using System.Linq;
using UnityAutorun.Scripting;

namespace UnityAutorun.Mcp.Tests
{
    internal static class ScriptCatalogTests
    {
        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("script categories recognize relative and absolute modern and legacy roots", SupportedRoots);
            yield return ("script categories reject traversal and matching prefix siblings", PathBoundaries);
            yield return ("script tree preserves deep root basic and unknown sources with accurate counts", TreeGroups);
            yield return ("script category search matches directories metadata and kind without losing ancestors", Search);
            yield return ("script labels distinguish repeated names by stable ID", DuplicateNames);
            yield return ("host basic scripts retain directories while kind filters remain accurate", HostBasicDirectories);
        }

        private static void SupportedRoots()
        {
            Equal("Startup/Login", AutoRunScriptCatalogTree.GetDirectoryPath(
                "Assets/Editor/AutorunScripts/Startup/Login/Start.cs", "/project"));
            Equal("Multiplayer/HeldItems", AutoRunScriptCatalogTree.GetDirectoryPath(
                "E:\\Project\\Assets\\Editor\\AutorunScripts\\Multiplayer\\HeldItems\\Flow.cs", "e:/project/"));
            Equal("Startup/Popups", AutoRunScriptCatalogTree.GetDirectoryPath(
                "/project/Assets/AutorunScripts/Editor/Startup/Popups/Close.cs", "/project"));
            Equal("", AutoRunScriptCatalogTree.GetDirectoryPath("./Assets/Editor/AutorunScripts/Root.cs", "/project"));
        }

        private static void PathBoundaries()
        {
            foreach (string path in new[]
            {
                "Assets/Editor/AutorunScriptsOther/Flow.cs",
                "Assets/Editor/AutorunScripts/../Outside/Flow.cs",
                "Assets/AutorunScripts/Editor/../../Outside/Flow.cs",
                "/project-other/Assets/Editor/AutorunScripts/Flow.cs",
                "/other/Assets/Editor/AutorunScripts/Flow.cs",
                "Assets/Editor/AutorunScripts/Flow.cs.meta",
                "Packages/package/Flow.cs"
            }) Equal(null, AutoRunScriptCatalogTree.GetDirectoryPath(path, "/project"));
        }

        private static void TreeGroups()
        {
            var definitions = new[]
            {
                Definition("deep", "Assets/Editor/AutorunScripts/Multiplayer/Items/Flows/Deep.cs"),
                Definition("legacy", "Assets/AutorunScripts/Editor/Multiplayer/Items/Legacy.cs"),
                Definition("root", "Assets/Editor/AutorunScripts/Root.cs"),
                Definition("unknown", null),
                Definition("basic", "Packages/tool/Basic.cs", AutoRunScriptKind.Basic)
            };
            AutoRunScriptCatalogTree tree = AutoRunScriptCatalogTree.Build(definitions, "/project");
            Equal(5, tree.Root.ScriptCount);
            Equal(5, tree.Scripts.Count);
            Equal("root", tree.Root.Scripts.Single().Definition.Id);
            AutoRunScriptCategory multiplayer = tree.Root.Children.Single(category => category.Name == "Multiplayer");
            Equal(2, multiplayer.ScriptCount);
            Equal(1, multiplayer.Children.Single().Children.Single().ScriptCount);
            Equal("legacy", multiplayer.Children.Single().Scripts.Single().Definition.Id);
            Equal("unknown", tree.Root.Children.Single(category => category.Name == "Other").Scripts.Single().Definition.Id);
            Equal("basic", tree.Root.Children.Single(category => category.Name == "Basic").Scripts.Single().Definition.Id);
            Equal(5, Flatten(tree.Root).Count());
        }

        private static void Search()
        {
            AutoRunScriptDefinition matching = Definition("network.exchange", "Assets/Editor/AutorunScripts/Multiplayer/HeldItems/Flow.cs");
            matching.Name = "Switch object";
            matching.Description = "Uses the real inventory";
            var definitions = new[]
            {
                matching,
                Definition("login", "Assets/Editor/AutorunScripts/Startup/Login/Flow.cs"),
                Definition("basic", "Packages/tool/Flow.cs", AutoRunScriptKind.Basic)
            };
            foreach (string query in new[] { "multiplayer/helditems", "Multiplayer\\HeldItems", "exchange", "SWITCH OBJECT", "real inventory" })
            {
                AutoRunScriptCatalogTree tree = AutoRunScriptCatalogTree.Build(definitions, "/project", query, AutoRunScriptKind.Business);
                Equal(1, tree.Root.ScriptCount);
                Equal("network.exchange", tree.Scripts.Single().Definition.Id);
                Equal("Multiplayer", tree.Root.Children.Single().Name);
                Equal("HeldItems", tree.Root.Children.Single().Children.Single().Name);
            }
            Equal(0, AutoRunScriptCatalogTree.Build(definitions, "/project", "inventory", AutoRunScriptKind.Basic).Scripts.Count);
            Equal(1, AutoRunScriptCatalogTree.Build(definitions, "/project", "Basic", AutoRunScriptKind.Basic).Scripts.Count);
        }

        private static void DuplicateNames()
        {
            AutoRunScriptDefinition first = Definition("a", "Assets/Editor/AutorunScripts/Startup/Login/A.cs");
            AutoRunScriptDefinition second = Definition("b", "Assets/Editor/AutorunScripts/Multiplayer/HeldItems/B.cs");
            first.Name = second.Name = "Run";
            AutoRunScriptCatalogTree tree = AutoRunScriptCatalogTree.Build(new[] { second, first }, "/project");
            Equal("Run [a]", tree.Scripts[0].Label);
            Equal("Run [b]", tree.Scripts[1].Label);
            Equal("a", first.Id);
            Equal("b", second.Id);
        }

        private static void HostBasicDirectories()
        {
            var definitions = new[]
            {
                Definition("record", "Assets/Editor/AutorunScripts/Audio/Recording/Record.cs", AutoRunScriptKind.Basic),
                Definition("package-basic", "Packages/tool/Basic.cs", AutoRunScriptKind.Basic),
                Definition("business", "Assets/Editor/AutorunScripts/Audio/Validation/Validate.cs")
            };
            AutoRunScriptCatalogTree basic = AutoRunScriptCatalogTree.Build(definitions, "/project", null, AutoRunScriptKind.Basic);
            Equal(2, basic.Scripts.Count);
            Equal("record", basic.Root.Children.Single(category => category.Name == "Audio")
                .Children.Single(category => category.Name == "Recording").Scripts.Single().Definition.Id);
            Equal("package-basic", basic.Root.Children.Single(category => category.Name == "Basic").Scripts.Single().Definition.Id);
            Equal(1, AutoRunScriptCatalogTree.Build(definitions, "/project", "Audio", AutoRunScriptKind.Basic).Scripts.Count);
            AutoRunScriptCatalogTree business = AutoRunScriptCatalogTree.Build(definitions, "/project", null, AutoRunScriptKind.Business);
            Equal("business", business.Scripts.Single().Definition.Id);
            Equal("Validation", business.Root.Children.Single().Children.Single().Name);
        }

        private static IEnumerable<AutoRunScriptCatalogEntry> Flatten(AutoRunScriptCategory category)
        {
            foreach (AutoRunScriptCatalogEntry entry in category.Scripts) yield return entry;
            foreach (AutoRunScriptCategory child in category.Children)
                foreach (AutoRunScriptCatalogEntry entry in Flatten(child)) yield return entry;
        }

        private static AutoRunScriptDefinition Definition(string id, string sourcePath, AutoRunScriptKind kind = AutoRunScriptKind.Business)
        {
            return new AutoRunScriptDefinition { Id = id, Name = id, Kind = kind, SourcePath = sourcePath };
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected '" + expected + "' but found '" + actual + "'.");
        }
    }
}
