using System.Windows.Automation;

using Listly.Native;

namespace Listly.Services;

/// <summary>A file or folder shown in a Windows Explorer window.</summary>
public sealed class ExplorerItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsFolder { get; init; }
}

/// <summary>Snapshot of the folder currently shown by a Windows Explorer window.</summary>
public sealed class ExplorerFolder
{
    public required IntPtr Hwnd { get; init; }
    public required string Path { get; init; }
    public required List<ExplorerItem> Items { get; init; }
}

/// <summary>
/// Bridges to Windows Explorer via the Shell Automation COM object (for reading the
/// current folder and activating items in place) and UI Automation (for selecting and
/// scrolling to an item so it is visibly highlighted).
/// </summary>
public static class ExplorerAccess
{
    private static readonly string[] ExplorerClasses = { "CabinetWClass", "ExploreWClass" };

    private static readonly HashSet<string> TextInputClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "RichEdit", "RichEdit20W", "RichEdit20A", "RICHEDIT50W",
        "ComboBox", "ComboBoxEx32", "SearchEditBoxWrapperClass"
    };

    /// <summary>True when the foreground window is a File Explorer window whose file list has focus.</summary>
    public static bool IsExplorerListFocused(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
            return false;

        var cls = NativeMethods.GetClassName(foreground);
        if (Array.IndexOf(ExplorerClasses, cls) < 0)
            return false;

        var focusClass = GetFocusedClass(foreground);
        if (string.IsNullOrEmpty(focusClass))
            return false;

        // Do not hijack typing when the user is in the address bar, search box or renaming.
        return !TextInputClasses.Contains(focusClass);
    }

    private static string GetFocusedClass(IntPtr foreground)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            return string.Empty;

        return NativeMethods.GetClassName(info.hwndFocus);
    }

    /// <summary>Reads the folder shown by the given Explorer window. Must run on an STA thread.</summary>
    public static ExplorerFolder? GetFolder(IntPtr hwnd)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return null;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic windows = shell!.Windows();
            int count = windows.Count;
            int target = hwnd.ToInt32();

            for (int i = 0; i < count; i++)
            {
                dynamic? window = windows.Item(i);
                if (window is null)
                    continue;

                int windowHandle;
                try { windowHandle = (int)window.HWND; }
                catch { continue; }

                if (windowHandle != target)
                    continue;

                return ReadFolder(hwnd, window);
            }
        }
        catch
        {
            // Shell COM can throw transiently; treat as "no folder".
        }

        return null;
    }

    private static ExplorerFolder? ReadFolder(IntPtr hwnd, dynamic window)
    {
        try
        {
            dynamic document = window.Document;
            dynamic folder = document.Folder;
            string path = folder.Self.Path;
            dynamic items = folder.Items();
            int count = items.Count;

            var list = new List<ExplorerItem>(count);
            for (int i = 0; i < count; i++)
            {
                dynamic? item = items.Item(i);
                if (item is null)
                    continue;

                try
                {
                    list.Add(new ExplorerItem
                    {
                        Name = item.Name,
                        Path = item.Path,
                        IsFolder = item.IsFolder
                    });
                }
                catch
                {
                    // Skip items that cannot be read.
                }
            }

            return new ExplorerFolder { Hwnd = hwnd, Path = path, Items = list };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns only the folder path shown by the given Explorer window (no item enumeration). STA thread only.</summary>
    public static string? GetFolderPath(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return null;

        try
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            int target = hwnd.ToInt32();

            for (int i = 0; i < count; i++)
            {
                dynamic? window = windows.Item(i);
                if (window is null)
                    continue;

                int windowHandle;
                try { windowHandle = (int)window.HWND; }
                catch { continue; }

                if (windowHandle != target)
                    continue;

                try
                {
                    dynamic document = window.Document;
                    dynamic folder = document.Folder;
                    return (string)folder.Self.Path;
                }
                catch
                {
                    return null;
                }
            }
        }
        catch
        {
            // Shell COM can throw transiently; treat as "no folder".
        }

        return null;
    }

    /// <summary>Activates (opens/navigates to) the item with the given path in place. STA thread only.</summary>
    public static bool InvokeItem(IntPtr hwnd, string itemPath)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return false;

        try
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            int target = hwnd.ToInt32();

            for (int i = 0; i < count; i++)
            {
                dynamic? window = windows.Item(i);
                if (window is null)
                    continue;

                int windowHandle;
                try { windowHandle = (int)window.HWND; }
                catch { continue; }

                if (windowHandle != target)
                    continue;

                dynamic items = window.Document.Folder.Items();
                int n = items.Count;
                for (int k = 0; k < n; k++)
                {
                    dynamic? item = items.Item(k);
                    if (item is null)
                        continue;

                    string p;
                    try { p = item.Path; }
                    catch { continue; }

                    if (string.Equals(p, itemPath, StringComparison.OrdinalIgnoreCase))
                    {
                        item.InvokeVerb();
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Fall through to caller's fallback.
        }

        return false;
    }

    /// <summary>Finds the UI Automation element for the Explorer file list.</summary>
    public static AutomationElement? GetItemsList(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
                return null;

            var byId = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "Items View"));

            return root.FindFirst(TreeScope.Descendants, byId)
                ?? root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Selects, focuses and scrolls to the list item with the given display name.</summary>
    public static void SelectByName(AutomationElement list, string name)
    {
        try
        {
            var element = FindItem(list, name);
            if (element is null)
                return;

            // If the item is virtualized (scrolled out of view), realize it first: this brings
            // it on-screen and promotes the placeholder to a fully interactive element, so
            // Explorer actually scrolls to and selects it even when it started off-screen.
            if (element.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var virtObj)
                && virtObj is VirtualizedItemPattern virtualItem)
            {
                try { virtualItem.Realize(); }
                catch { /* already realized, or realization not supported */ }
            }

            if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollObj)
                && scrollObj is ScrollItemPattern scroll)
            {
                scroll.ScrollIntoView();
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectObj)
                && selectObj is SelectionItemPattern selection)
            {
                selection.Select();
            }
        }
        catch
        {
            // Best effort; selection is only a visual aid.
        }
    }

    // Locate a list item by display name, resilient to virtualization and to Windows 11's
    // habit of splitting a mixed folder into "Folders" and "Files" groups — which pushes the
    // items down a level, so a direct-children search alone misses them.
    private static AutomationElement? FindItem(AutomationElement list, string name)
    {
        // ItemContainerPattern realizes virtualized items and spans groups.
        if (list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var containerObj)
            && containerObj is ItemContainerPattern container)
        {
            try
            {
                var found = container.FindItemByProperty(null, AutomationElement.NameProperty, name);
                if (found is not null)
                    return found;
            }
            catch
            {
                // Some shell views throw here; fall back to a tree search.
            }
        }

        var byName = new PropertyCondition(AutomationElement.NameProperty, name);

        // Flat, ungrouped list: items are direct children.
        var direct = list.FindFirst(TreeScope.Children, byName);
        if (direct is not null)
            return direct;

        // Grouped list (folders + files): items sit under group headers, so search deeper.
        return list.FindFirst(TreeScope.Descendants, byName);
    }
}
