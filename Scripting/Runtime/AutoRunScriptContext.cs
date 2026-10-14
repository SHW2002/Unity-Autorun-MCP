using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityAutorun.Scripting
{
    public sealed class AutoRunScriptContext
    {
        private readonly Dictionary<string, string> _parameters;
        private readonly Action<string> _log;
        private readonly Action<string> _artifact;

        internal AutoRunScriptContext(string runId, string scriptId, Dictionary<string, string> parameters,
            IAutoRunScriptHost host, Action<string> log, Action<string> artifact)
        {
            RunId = runId;
            ScriptId = scriptId;
            _parameters = parameters == null ? new Dictionary<string, string>() : new Dictionary<string, string>(parameters);
            Host = host;
            _log = log;
            _artifact = artifact;
        }

        public string RunId { get; private set; }
        public string ScriptId { get; private set; }
        public IAutoRunScriptHost Host { get; private set; }
        public string GetString(string name, string defaultValue = null)
        {
            string value;
            return _parameters.TryGetValue(name, out value) ? value : defaultValue;
        }
        public int GetInt(string name, int defaultValue = 0)
        {
            int value;
            return int.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }
        public double GetNumber(string name, double defaultValue = 0)
        {
            double value;
            return double.TryParse(GetString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }
        public bool GetBool(string name, bool defaultValue = false)
        {
            bool value;
            return bool.TryParse(GetString(name), out value) ? value : defaultValue;
        }
        public void Log(string message) { _log("[" + ScriptId + "] " + (message ?? string.Empty)); }
        public void AddArtifact(string path) { if (!string.IsNullOrWhiteSpace(path)) _artifact(path); }
        public void Fail(string code, string message) { throw new AutoRunScriptException(code, message); }
        public void Require(AutoRunScriptOperationResult result)
        {
            if (result == null) Fail("operation_no_result", "Host operation returned no result.");
            if (!result.Ok) Fail(result.Code, result.Message);
            if (!string.IsNullOrWhiteSpace(result.Message)) Log(result.Message);
            AddArtifact(result.ArtifactPath);
        }
        public object Wait(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            return new AutoRunScriptWait { Seconds = seconds };
        }
        public object WaitUntil(Func<bool> condition, double timeoutSeconds = 15, string message = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            return new AutoRunScriptWait { Condition = condition, Seconds = timeoutSeconds, Message = message };
        }
        /// <summary>Yield this token to run a declared dependency. Parameters are explicitly passed, not inherited.</summary>
        public object Call(string scriptId, Dictionary<string, string> parameters = null)
        {
            return new AutoRunScriptCall { ScriptId = scriptId, Parameters = parameters };
        }
    }

    internal sealed class AutoRunScriptWait
    {
        public double Seconds;
        public Func<bool> Condition;
        public string Message;
        public double Deadline;
    }
    internal sealed class AutoRunScriptCall
    {
        public string ScriptId;
        public Dictionary<string, string> Parameters;
    }
}
