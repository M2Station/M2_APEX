using System.Diagnostics;
using System.Security.Principal;

namespace Listly.Services;

/// <summary>
/// Central launch point for external processes. When M2_APEX runs elevated (so the global double-Ctrl
/// hotkey still fires while an administrator window — e.g. VS Code — is focused), anything it starts
/// would normally inherit administrator rights. To keep the launcher safe, shell-execute launches are
/// redirected to the interactive, medium-integrity user via <see cref="MediumIntegrityLauncher"/>.
/// When not elevated, behaviour is unchanged — a plain <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public static class ProcessLauncher
{
    /// <summary>True when this process is running elevated (member of the Administrators role).</summary>
    public static bool IsElevated { get; } = ComputeElevated();

    /// <summary>
    /// Starts the process described by <paramref name="psi"/>. De-elevates to the interactive user when
    /// this process is elevated and the launch uses shell-execute semantics; otherwise starts it as-is.
    /// Returns the started <see cref="Process"/>, or <c>null</c> when the launch was de-elevated (the
    /// out-of-process token launch does not surface a managed handle).
    /// </summary>
    public static Process? Start(ProcessStartInfo psi)
    {
        // Leave intentional-elevation, redirected, or non-shell launches exactly as they are: "runas" is
        // an explicit elevation request, and redirected / UseShellExecute=false starts are internal
        // (e.g. capturing a child's output) and must keep their precise semantics.
        bool canDeElevate = IsElevated
            && psi.UseShellExecute
            && !psi.RedirectStandardOutput
            && !psi.RedirectStandardError
            && !psi.RedirectStandardInput
            && !string.Equals(psi.Verb, "runas", StringComparison.OrdinalIgnoreCase);

        if (canDeElevate &&
            MediumIntegrityLauncher.TryStart(psi.FileName, psi.Arguments, NullIfEmpty(psi.WorkingDirectory)))
        {
            return null;
        }

        return Process.Start(psi);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static bool ComputeElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
