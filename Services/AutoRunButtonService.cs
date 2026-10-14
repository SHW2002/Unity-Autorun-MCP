using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public static class AutoRunButtonService
{
    public static List<AutoRunButtonInfo> ListButtons(string framework)
    {
        var buttons = new List<AutoRunButtonInfo>();
        if (ShouldIncludeFramework(framework, "ugui"))
        {
            buttons.AddRange(ListUGUIButtons());
        }

        if (ShouldIncludeFramework(framework, "fairygui"))
        {
            buttons.AddRange(FairyGUIHelper.ListButtons());
        }

        return buttons;
    }

    public static AutoRunButtonResult Click(AutoRunParam param)
    {
        return Click(param, 0);
    }

    public static AutoRunButtonResult Click(AutoRunParam param, int matchIndex)
    {
        return param.isFairyGUI
            ? ClickFairyGUI(param)
            : ClickUGUI(param, matchIndex);
    }

    public static AutoRunButtonResult ClickWithPointer(
        AutoRunParam param,
        int matchIndex)
    {
        if (param == null)
        {
            return AutoRunButtonResult.Fail(
                "button_action_required",
                "err: click action is required.");
        }

        if (param.isFairyGUI)
        {
            return ClickFairyGUI(param);
        }

        Button button = FindUGUIButton(param, matchIndex);
        if (button == null)
        {
            return AutoRunButtonResult.Fail(
                "button_not_found",
                $"err: button '{param.buttonName}' match {matchIndex + 1} not found.");
        }

        AutoRunButtonResult result = AutoRunPointerService.Click(
            button,
            param.buttonName + " match " + (matchIndex + 1));
        if (result.ok)
        {
            result.message += " Path: "
                + GetHierarchyPath(button.transform)
                + ", Text: "
                + GetButtonText(button);
        }

        return result;
    }

    private static AutoRunButtonResult ClickUGUI(
        AutoRunParam param,
        int matchIndex)
    {
        Button btnObject = FindUGUIButton(param, matchIndex);
        if (!btnObject)
        {
            return AutoRunButtonResult.Fail("button_not_found", $"err: button '{param.buttonName}' not found!");
        }

        if (btnObject.onClick == null)
        {
            return AutoRunButtonResult.Fail("no_click_event", $"err: button '{param.buttonName}' has no button click event!");
        }

        btnObject.onClick.Invoke();
        return AutoRunButtonResult.Success(
            $"btn {param.buttonName} match {matchIndex + 1} is clicked. Path: {GetHierarchyPath(btnObject.transform)}, Text: {GetButtonText(btnObject)}");
    }

    public static bool HasButton(AutoRunParam param)
    {
        return HasButton(param, 0);
    }

    public static bool HasButton(AutoRunParam param, int matchIndex)
    {
        return param.isFairyGUI
            ? matchIndex == 0
            : FindUGUIButton(param, matchIndex) != null;
    }

    public static int GetButtonMatchCount(AutoRunParam param)
    {
        return param.isFairyGUI ? 1 : FindUGUIButtons(param).Count;
    }

    public static string GetButtonMatchSignature(AutoRunParam param)
    {
        if (param == null || param.isFairyGUI)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            FindUGUIButtons(param)
                .Select(button =>
                    GetHierarchyPath(button.transform)
                    + "#"
                    + GetSelectorContentSignature(button, param)));
    }

    public static AutoRunButtonResult ScrollForMoreMatches(AutoRunParam param)
    {
        if (param == null || param.isFairyGUI)
        {
            return AutoRunButtonResult.Fail(
                "button_scroll_unsupported",
                "err: repeated-control scrolling requires a uGUI selector.");
        }

        ScrollRect scrollRect = ResolveScrollRect(param);
        if (scrollRect == null)
        {
            return AutoRunButtonResult.Fail(
                "button_scroll_container_not_found",
                $"err: no active ScrollRect ancestor found for '{param.buttonName}'.");
        }

        Canvas.ForceUpdateCanvases();
        scrollRect.StopMovement();
        Vector2 before = scrollRect.normalizedPosition;
        Vector2 after = before;
        if (scrollRect.vertical && before.y > 0.001f)
        {
            after.y = Mathf.Max(
                0f,
                before.y - ResolveNormalizedScrollStep(scrollRect, true));
        }
        else if (scrollRect.horizontal && before.x < 0.999f)
        {
            after.x = Mathf.Min(
                1f,
                before.x + ResolveNormalizedScrollStep(scrollRect, false));
        }
        else
        {
            return AutoRunButtonResult.Fail(
                "button_scroll_end_reached",
                $"err: scroll container for '{param.buttonName}' is already at its end.");
        }

        scrollRect.normalizedPosition = after;
        Canvas.ForceUpdateCanvases();
        return AutoRunButtonResult.Success(
            $"scrolled '{GetHierarchyPath(scrollRect.transform)}' from "
            + FormatVector(before)
            + " to "
            + FormatVector(after)
            + ".");
    }

    public static AutoRunButtonResult ResetScrollToStart(AutoRunParam param)
    {
        if (param == null || param.isFairyGUI)
        {
            return AutoRunButtonResult.Fail(
                "button_scroll_unsupported",
                "err: repeated-control scrolling requires a uGUI selector.");
        }

        ScrollRect scrollRect = ResolveScrollRect(param);
        if (scrollRect == null)
        {
            return AutoRunButtonResult.Fail(
                "button_scroll_container_not_found",
                $"err: no active ScrollRect ancestor found for '{param.buttonName}'.");
        }

        Canvas.ForceUpdateCanvases();
        scrollRect.StopMovement();
        Vector2 before = scrollRect.normalizedPosition;
        Vector2 after = before;
        if (scrollRect.vertical)
        {
            after.y = 1f;
        }

        if (scrollRect.horizontal)
        {
            after.x = 0f;
        }

        scrollRect.normalizedPosition = after;
        Canvas.ForceUpdateCanvases();
        return AutoRunButtonResult.Success(
            $"reset scroll '{GetHierarchyPath(scrollRect.transform)}' from "
            + FormatVector(before)
            + " to "
            + FormatVector(after)
            + ".");
    }

    public static int GetBranchSelectorCount(string scopeRootName)
    {
        return FindBranchSelectorButtons(scopeRootName).Count;
    }

    public static AutoRunButtonResult ClickBranchSelector(
        string scopeRootName,
        int matchIndex)
    {
        List<Button> candidates =
            FindBranchSelectorButtons(scopeRootName);
        if (matchIndex < 0 || matchIndex >= candidates.Count)
        {
            return AutoRunButtonResult.Fail(
                "branch_selector_not_found",
                $"err: branch selector match {matchIndex + 1} was not found under '{scopeRootName}'.");
        }

        Button selected = candidates[matchIndex];
        return AutoRunPointerService.Click(
            selected,
            "branch selector "
                + selected.name
                + " match "
                + (matchIndex + 1)
                + "/"
                + candidates.Count);
    }

    public static AutoRunButtonResult ClickDismissButton(string scopeRootName)
    {
        List<Button> semanticCandidates = UnityEngine.Object.FindObjectsOfType<Button>()
            .Where(IsEffectivelyInteractable)
            .Select(button => new
            {
                Button = button,
                Rank = DismissButtonRank(button.name),
            })
            .Where(item => item.Rank < int.MaxValue)
            .OrderBy(item => item.Rank)
            .ThenBy(
                item => GetHierarchyOrderKey(item.Button.transform),
                StringComparer.Ordinal)
            .Select(item => item.Button)
            .ToList();
        List<Button> scopedCandidates = string.IsNullOrWhiteSpace(scopeRootName)
            ? semanticCandidates
            : semanticCandidates
                .Where(button =>
                    HasAncestorNamed(button.transform, scopeRootName))
                .ToList();
        List<Button> candidates = scopedCandidates.Count > 0
            ? scopedCandidates
            : semanticCandidates;
        if (candidates.Count == 0)
        {
            return AutoRunButtonResult.Fail(
                "dismiss_button_not_found",
                $"err: no active back, close, or return button found for scope '{scopeRootName}'.");
        }

        Button selected = candidates[0];
        string scopeMatch = scopedCandidates.Contains(selected)
            ? "scoped"
            : "global fallback";
        return AutoRunPointerService.Click(
            selected,
            "dismiss button "
                + selected.name
                + " ("
                + scopeMatch
                + ")");
    }

    private static Button FindUGUIButton(AutoRunParam param, int matchIndex)
    {
        List<Button> matches = FindUGUIButtons(param);
        return matchIndex >= 0 && matchIndex < matches.Count
            ? matches[matchIndex]
            : null;
    }

    private static List<Button> FindUGUIButtons(AutoRunParam param)
    {
        Button[] allButtons = UnityEngine.Object.FindObjectsOfType<Button>();
        List<Button> nameMatchedBtns = ApplySelectorContext(
            FindButtonsByName(allButtons, param.buttonName),
            param);
        List<Button> selectableNameMatches = SelectButtons(
            nameMatchedBtns,
            param);
        if (selectableNameMatches.Count > 0)
        {
            return selectableNameMatches;
        }

        List<Button> textMatchedBtns = ApplySelectorContext(
            allButtons
            .Where(b => GetButtonText(b) == param.buttonText)
            .ToList(),
            param);
        return SelectButtons(textMatchedBtns, param);
    }

    private static List<Button> SelectButtons(
        List<Button> matches,
        AutoRunParam param)
    {
        List<Button> interactableMatches = matches
            .Where(IsEffectivelyInteractable)
            .OrderBy(
                button => GetHierarchyOrderKey(button.transform),
                StringComparer.Ordinal)
            .ToList();
        if (string.Equals(
                param.matchPolicy,
                AutoRunParam.MATCH_FIRST_INTERACTABLE,
                StringComparison.OrdinalIgnoreCase))
        {
            return interactableMatches;
        }

        return matches.Count == 1
            ? interactableMatches
            : new List<Button>();
    }

    private static List<Button> ApplySelectorContext(
        IEnumerable<Button> buttons,
        AutoRunParam param)
    {
        List<Button> matches = buttons.Where(button => button != null).ToList();
        if (!string.IsNullOrWhiteSpace(param.scopeRootName))
        {
            matches = matches
                .Where(button => HasAncestorNamed(button.transform, param.scopeRootName))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(param.objectPath))
        {
            matches = matches
                .Where(button => HierarchyPathEndsWith(button.transform, param.objectPath))
                .ToList();
        }

        return matches;
    }

    private static bool IsEffectivelyInteractable(Button button)
    {
        return button != null
            && button.enabled
            && button.gameObject.activeInHierarchy
            && button.IsInteractable();
    }

    private static bool HasAncestorNamed(Transform transform, string expectedName)
    {
        string normalizedExpected = NormalizeHierarchyName(expectedName);
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (NormalizeHierarchyName(current.name) == normalizedExpected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HierarchyPathEndsWith(Transform transform, string expectedPath)
    {
        string[] expectedSegments = expectedPath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeHierarchyName)
            .Where(segment => !string.IsNullOrEmpty(segment))
            .ToArray();
        if (expectedSegments.Length == 0)
        {
            return false;
        }

        if (NormalizeHierarchyName(transform.name)
            != expectedSegments[expectedSegments.Length - 1])
        {
            return false;
        }

        int expectedIndex = expectedSegments.Length - 2;
        for (Transform current = transform.parent;
            current != null && expectedIndex >= 0;
            current = current.parent)
        {
            if (NormalizeHierarchyName(current.name)
                == expectedSegments[expectedIndex])
            {
                expectedIndex--;
            }
        }

        return expectedIndex < 0;
    }

    private static string NormalizeHierarchyName(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\(clone\)$", string.Empty);
        return normalized
            .Replace("_", string.Empty)
            .Replace("*", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
    }

    private static string GetHierarchyOrderKey(Transform transform)
    {
        var segments = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
        {
            segments.Add(
                current.GetSiblingIndex().ToString("D6")
                + ":"
                + NormalizeHierarchyName(current.name));
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var segments = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
        {
            segments.Add(current.name);
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static float ResolveNormalizedScrollStep(
        ScrollRect scrollRect,
        bool vertical)
    {
        RectTransform viewport = scrollRect.viewport
            ? scrollRect.viewport
            : scrollRect.transform as RectTransform;
        RectTransform content = scrollRect.content;
        if (viewport == null || content == null)
        {
            return 0.5f;
        }

        float viewportSize = vertical
            ? viewport.rect.height
            : viewport.rect.width;
        float contentSize = vertical
            ? content.rect.height
            : content.rect.width;
        float scrollableSize = contentSize - viewportSize;
        if (viewportSize <= 0f || scrollableSize <= 0f)
        {
            return 0.5f;
        }

        return Mathf.Clamp(
            viewportSize * 0.8f / scrollableSize,
            0.15f,
            0.8f);
    }

    private static ScrollRect ResolveScrollRect(AutoRunParam param)
    {
        return FindUGUIButtons(param)
            .Select(
                button =>
                    button.GetComponentInParent<ScrollRect>())
            .Where(candidate =>
                candidate != null
                && candidate.enabled
                && candidate.gameObject.activeInHierarchy)
            .GroupBy(candidate => candidate)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static string FormatVector(Vector2 value)
    {
        return $"({value.x:0.###},{value.y:0.###})";
    }

    private static string GetSelectorContentSignature(
        Button button,
        AutoRunParam param)
    {
        Transform owner = ResolveSelectorOwner(
            button.transform,
            param?.objectPath);
        if (owner == null)
        {
            owner = button.transform.parent
                ? button.transform.parent
                : button.transform;
        }

        IEnumerable<string> texts = owner
            .GetComponentsInChildren<Text>(true)
            .Where(text => text != null)
            .OrderBy(
                text => GetHierarchyOrderKey(text.transform),
                StringComparer.Ordinal)
            .Select(text => "t:" + EscapeSignatureValue(text.text));
        IEnumerable<string> sprites = owner
            .GetComponentsInChildren<Image>(true)
            .Where(image => image != null && image.sprite != null)
            .OrderBy(
                image => GetHierarchyOrderKey(image.transform),
                StringComparer.Ordinal)
            .Select(image =>
                "i:" + EscapeSignatureValue(image.sprite.name));
        IEnumerable<string> richTexts = owner
            .GetComponentsInChildren<Component>(true)
            .Where(IsRichTextComponent)
            .OrderBy(
                component =>
                    GetHierarchyOrderKey(component.transform),
                StringComparer.Ordinal)
            .Select(GetRichTextSignature)
            .Where(value => value != null);
        return string.Join(
            ",",
            texts.Concat(richTexts).Concat(sprites));
    }

    private static bool IsRichTextComponent(Component component)
    {
        if (component == null || component is Text)
        {
            return false;
        }

        Type type = component.GetType();
        string fullName = type.FullName ?? type.Name;
        return fullName.StartsWith(
                "TMPro.",
                StringComparison.Ordinal)
            && type.GetProperty("text") != null;
    }

    private static string GetRichTextSignature(Component component)
    {
        try
        {
            object value = component
                .GetType()
                .GetProperty("text")
                ?.GetValue(component, null);
            return value is string text
                ? "t:" + EscapeSignatureValue(text)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Transform ResolveSelectorOwner(
        Transform transform,
        string objectPath)
    {
        if (transform == null || string.IsNullOrWhiteSpace(objectPath))
        {
            return null;
        }

        string[] expectedSegments = objectPath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeHierarchyName)
            .Where(segment => !string.IsNullOrEmpty(segment))
            .ToArray();
        if (expectedSegments.Length < 2)
        {
            return null;
        }

        for (Transform current = transform.parent;
            current != null;
            current = current.parent)
        {
            for (int index = expectedSegments.Length - 2;
                index >= 0;
                index--)
            {
                if (NormalizeHierarchyName(current.name)
                    == expectedSegments[index])
                {
                    return current;
                }
            }
        }

        return null;
    }

    private static string EscapeSignatureValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace(",", "\\,");
    }

    private static List<Button> FindBranchSelectorButtons(
        string scopeRootName)
    {
        return UnityEngine.Object.FindObjectsOfType<Button>()
            .Where(IsEffectivelyInteractable)
            .Where(button =>
                string.IsNullOrWhiteSpace(scopeRootName)
                || HasAncestorNamed(button.transform, scopeRootName))
            .Select(button => new
            {
                Button = button,
                Rank = BranchSelectorRank(button.name),
            })
            .Where(item => item.Rank < int.MaxValue)
            .OrderBy(item => item.Rank)
            .ThenBy(
                item => GetHierarchyOrderKey(item.Button.transform),
                StringComparer.Ordinal)
            .Select(item => item.Button)
            .ToList();
    }

    private static List<Button> FindButtonsByName(IEnumerable<Button> buttons, string buttonName)
    {
        var exactMatches = buttons.Where(b => b.name == buttonName).ToList();
        if (exactMatches.Count > 0)
        {
            return exactMatches;
        }

        string normalizedButtonName = NormalizeButtonName(buttonName);
        if (string.IsNullOrEmpty(normalizedButtonName))
        {
            return new List<Button>();
        }

        return buttons.Where(b => NormalizeButtonName(b.name) == normalizedButtonName).ToList();
    }

    private static string NormalizeButtonName(string buttonName)
    {
        if (string.IsNullOrEmpty(buttonName))
        {
            return string.Empty;
        }

        string normalized = buttonName.ToLowerInvariant();
        normalized = normalized.Replace("_", string.Empty).Replace("*", string.Empty);
        normalized = Regex.Replace(normalized, "gameobject$", string.Empty);
        normalized = Regex.Replace(normalized, "button$", string.Empty);
        return normalized;
    }

    private static int DismissButtonRank(string buttonName)
    {
        string normalized = NormalizeButtonName(buttonName);
        if (normalized.StartsWith("back", StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalized.StartsWith("close", StringComparison.Ordinal))
        {
            return 1;
        }

        if (normalized.StartsWith("return", StringComparison.Ordinal))
        {
            return 2;
        }

        if (normalized.Contains("back"))
        {
            return 3;
        }

        if (normalized.Contains("close"))
        {
            return 4;
        }

        return int.MaxValue;
    }

    private static int BranchSelectorRank(string buttonName)
    {
        string normalized = NormalizeButtonName(buttonName);
        if (normalized.Contains("tab"))
        {
            return 0;
        }

        if (normalized.Contains("category"))
        {
            return 1;
        }

        return int.MaxValue;
    }

    private static AutoRunButtonResult ClickFairyGUI(AutoRunParam param)
    {
        return FairyGUIHelper.ClickButton(param.buttonName, param.buttonText);
    }

    private static List<AutoRunButtonInfo> ListUGUIButtons()
    {
        return UnityEngine.Object.FindObjectsOfType<Button>()
            .Select(button => new AutoRunButtonInfo
            {
                name = button.name,
                text = GetButtonText(button),
                framework = "ugui",
                interactable = button.interactable,
            })
            .ToList();
    }

    private static string GetButtonText(Button button)
    {
        return button.GetComponentInChildren<Text>()?.text;
    }

    private static bool ShouldIncludeFramework(string requested, string framework)
    {
        return string.IsNullOrEmpty(requested)
            || requested == "all"
            || requested.ToLowerInvariant() == framework;
    }
}
