using System;
using System.Collections.Generic;

[Serializable]
public sealed class AutoRunBridgeRequest
{
    public string id;
    public string command;
    public AutoRunBridgePayload payload = new();
}

[Serializable]
public sealed class AutoRunBridgePayload
{
    public string name;
    public string text;
    public string framework;
    public string query;
    public string scope;
    public string property;
    public string comparison;
    public string expected;
    public string targetViewId;
    public string scriptId;
    public string runId;
    public string kind;
    public string parametersJson;
    public bool includeSource = true;
    public double timeoutSeconds = 120;
    public bool exact;
    public bool namesOnly;
    public bool includeInactive;
    public bool includeSensitive;
    public bool ensurePlayMode;
    public int limit = 100;
    public int timeoutMilliseconds = 10000;
    public int waitMilliseconds;
    public int pollMilliseconds;
    public List<string> types = new();
    public List<AutoRunParam> actions = new();
}

[Serializable]
public sealed class AutoRunBridgeResponse
{
    public string id;
    public bool ok;
    public string code;
    public string message;
    public AutoRunBridgeData data = new();
}

[Serializable]
public sealed class AutoRunBridgeData
{
    public bool isPlaying;
    public string unityVersion;
    public string bridgeVersion;
    public string framework;
    public List<AutoRunButtonInfo> buttons = new();
    public List<string> buttonNames = new();
    public List<string> openViews = new();
    public List<AutoRunUiElementState> uiElements = new();
    public List<string> messages = new();
    public int buttonCount;
    public int openViewCount;
    public int uiElementCount;
    public bool truncated;
    public bool viewOpen;
    public bool uiStateMatched;
    public string uiStateProperty;
    public string uiStateComparison;
    public string uiStateExpected;
    public string uiStateActual;
    public bool isCompiling;
    public List<string> compilerMessages = new();
    public List<AutoRunBridgeScriptInfo> scripts = new();
    public AutoRunBridgeScriptInfo script;
    public AutoRunBridgeScriptRun scriptRun;
    public List<string> catalogErrors = new();
    public bool terminal;
    public long elapsedMilliseconds;
}

[Serializable]
public sealed class AutoRunBridgeScriptInfo
{
    public string id;
    public string name;
    public string kind;
    public string description;
    public string preconditions;
    public bool preparePlayMode;
    public string[] dependencies;
    public string sourcePath;
    public string source;
    public string validationError;
}

[Serializable]
public sealed class AutoRunBridgeScriptRun
{
    public string runId;
    public string scriptId;
    public string state;
    public string code;
    public string message;
    public double startedAt;
    public double completedAt;
    public bool terminal;
    public long elapsedMilliseconds;
    public List<string> callStack;
    public List<string> logs;
    public List<string> artifacts;
    public List<AutoRunBridgeScriptCall> calls;
}

[Serializable]
public sealed class AutoRunBridgeScriptCall
{
    public string scriptId;
    public string parentScriptId;
    public int depth;
    public string state;
    public string code;
    public string message;
}
