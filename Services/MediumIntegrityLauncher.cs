using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using Listly.Native;

namespace Listly.Services;

/// <summary>
/// Launches a process as the interactive, medium-integrity user by borrowing the running Explorer's
/// token, so a program opened from an ELEVATED M2_APEX does not inherit administrator rights. Best
/// effort: returns <c>false</c> when de-elevation is not possible, letting the caller fall back to a
/// normal (elevated) launch rather than failing to open the target at all.
/// </summary>
internal static class MediumIntegrityLauncher
{
    /// <summary>
    /// Starts <paramref name="fileName"/> (an executable, document, folder or URL) as the interactive
    /// user. Documents / folders / URLs are opened through Explorer so their default handler runs at
    /// medium integrity; executables run directly so their <paramref name="arguments"/> are preserved.
    /// </summary>
    public static bool TryStart(string fileName, string? arguments, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string commandLine;
        string? workDir = workingDirectory;

        if (NeedsShellResolution(fileName))
        {
            // Let Explorer resolve the association / URL / folder at medium integrity (arguments, if any,
            // are not applicable to documents, folders or URLs).
            commandLine = $"explorer.exe \"{fileName.TrimEnd('\\')}\"";
            workDir = null;
        }
        else if (IsBatch(fileName))
        {
            string tail = string.IsNullOrEmpty(arguments) ? string.Empty : " " + arguments;
            commandLine = $"cmd.exe /c \"\"{fileName}\"{tail}\"";
        }
        else
        {
            commandLine = string.IsNullOrEmpty(arguments) ? $"\"{fileName}\"" : $"\"{fileName}\" {arguments}";
            if (string.IsNullOrEmpty(workDir) && File.Exists(fileName))
                workDir = Path.GetDirectoryName(fileName);
        }

        return StartWithShellToken(commandLine, workDir);
    }

    /// <summary>
    /// A URL, a folder, or a real file that is not itself runnable must be opened by the shell so its
    /// default handler runs. A bare program name (e.g. "notepad.exe", "explorer.exe") is runnable
    /// directly via the normal PATH search, so it is not shell-resolved.
    /// </summary>
    private static bool NeedsShellResolution(string target)
    {
        if (target.Contains("://", StringComparison.Ordinal))
            return true;

        if (Directory.Exists(target))
            return true;

        string ext = Path.GetExtension(target);
        if (ext.Length == 0)
            return false;

        bool runnable = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                     || ext.Equals(".com", StringComparison.OrdinalIgnoreCase)
                     || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                     || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);

        return !runnable && File.Exists(target);
    }

    private static bool IsBatch(string target)
    {
        string ext = Path.GetExtension(target);
        return ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartWithShellToken(string commandLine, string? workingDirectory)
    {
        IntPtr shell = NativeMethods.GetShellWindow();
        if (shell == IntPtr.Zero)
            return false;

        _ = NativeMethods.GetWindowThreadProcessId(shell, out uint shellPid);
        if (shellPid == 0)
            return false;

        IntPtr hProcess = IntPtr.Zero, hToken = IntPtr.Zero, hPrimary = IntPtr.Zero;
        try
        {
            hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, shellPid);
            if (hProcess == IntPtr.Zero)
                return false;

            const uint tokenAccess = NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY
                | NativeMethods.TOKEN_ASSIGN_PRIMARY | NativeMethods.TOKEN_ADJUST_DEFAULT
                | NativeMethods.TOKEN_ADJUST_SESSIONID;

            if (!NativeMethods.OpenProcessToken(hProcess, tokenAccess, out hToken))
                return false;

            if (!NativeMethods.DuplicateTokenEx(hToken, NativeMethods.MAXIMUM_ALLOWED, IntPtr.Zero,
                    NativeMethods.SecurityImpersonation, NativeMethods.TokenPrimary, out hPrimary))
                return false;

            var si = new NativeMethods.STARTUPINFO
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                lpDesktop = "winsta0\\default",
            };
            var cmd = new StringBuilder(commandLine);

            bool ok = NativeMethods.CreateProcessWithTokenW(
                hPrimary, 0, null, cmd, 0, IntPtr.Zero,
                string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
                ref si, out var pi);

            if (ok)
            {
                if (pi.hProcess != IntPtr.Zero) NativeMethods.CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) NativeMethods.CloseHandle(pi.hThread);
            }

            return ok;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hPrimary != IntPtr.Zero) NativeMethods.CloseHandle(hPrimary);
            if (hToken != IntPtr.Zero) NativeMethods.CloseHandle(hToken);
            if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
        }
    }
}
