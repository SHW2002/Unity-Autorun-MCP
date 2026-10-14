using System;

/// <summary>Framework-neutral button selector shared by the bridge and scripts.</summary>
[Serializable]
public sealed class AutoRunParam
{
    public const string DEFAULT_NAME = "unnamed";
    public const string DEFAULT_TEXT = "untitled";
    public const string MATCH_UNIQUE = "unique";
    public const string MATCH_FIRST_INTERACTABLE = "first-interactable";

    public string buttonName = DEFAULT_NAME;
    public string buttonText = DEFAULT_TEXT;
    public bool isFairyGUI;
    public float delay;
    public bool isTest;
    public string objectPath;
    public string scopeRootName;
    public string matchPolicy = MATCH_UNIQUE;
}
