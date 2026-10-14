using System;
using System.Collections;

namespace UnityAutorun.Scripting
{
    public enum AutoRunScriptKind { Business, Basic }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AutoRunScriptAttribute : Attribute
    {
        public AutoRunScriptAttribute(string id, string name, AutoRunScriptKind kind)
        {
            Id = id;
            Name = name;
            Kind = kind;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public AutoRunScriptKind Kind { get; private set; }
        public string Description { get; set; }
        public string Preconditions { get; set; }
        /// <summary>Prepare Play Mode before the root script's first instruction. Dependency metadata never changes a running flow.</summary>
        public bool PreparePlayMode { get; set; }
        public string[] Dependencies { get; set; }
    }

    public abstract class AutoRunScript
    {
        /// <summary>Returns null when this script can run, or a failure explanation.</summary>
        public virtual string CheckPreconditions(AutoRunScriptContext context) { return null; }
        public abstract IEnumerator Execute(AutoRunScriptContext context);
        /// <summary>Runs exactly once for each instantiated script, including failed preconditions.</summary>
        public virtual void OnExit(AutoRunScriptContext context, AutoRunScriptResult result) { }
    }

    public sealed class AutoRunScriptException : Exception
    {
        public AutoRunScriptException(string code, string message) : base(message)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "script_error" : code;
        }
        public string Code { get; private set; }
    }
}
