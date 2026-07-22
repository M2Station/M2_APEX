using System.IO;

namespace Listly.Services;

/// <summary>
/// Maps files, folders and apps to Segoe MDL2 Assets glyphs so results render
/// instantly without extracting per-file icons.
/// </summary>
public static class IconGlyph
{
    public const string Folder = "\uE8B7";
    public const string Drive = "\uEDA2";
    public const string App = "\uE71D";
    public const string GenericFile = "\uE8A5";
    public const string Web = "\uE774";
    public const string Command = "\uE756";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "\uE756",
        [".msi"] = "\uE756",
        [".lnk"] = App,
        [".txt"] = "\uE8A5",
        [".md"] = "\uE8A5",
        [".log"] = "\uE9D9",
        [".pdf"] = "\uEA90",
        [".doc"] = "\uE8A5",
        [".docx"] = "\uE8A5",
        [".xls"] = "\uE9F9",
        [".xlsx"] = "\uE9F9",
        [".csv"] = "\uE9F9",
        [".ppt"] = "\uE8FE",
        [".pptx"] = "\uE8FE",
        [".jpg"] = "\uEB9F",
        [".jpeg"] = "\uEB9F",
        [".png"] = "\uEB9F",
        [".gif"] = "\uEB9F",
        [".bmp"] = "\uEB9F",
        [".svg"] = "\uEB9F",
        [".webp"] = "\uEB9F",
        [".ico"] = "\uEB9F",
        [".mp3"] = "\uEC4F",
        [".wav"] = "\uEC4F",
        [".flac"] = "\uEC4F",
        [".mp4"] = "\uE714",
        [".mkv"] = "\uE714",
        [".avi"] = "\uE714",
        [".mov"] = "\uE714",
        [".zip"] = "\uF012",
        [".rar"] = "\uF012",
        [".7z"] = "\uF012",
        [".tar"] = "\uF012",
        [".gz"] = "\uF012",
        [".cs"] = "\uE943",
        [".js"] = "\uE943",
        [".ts"] = "\uE943",
        [".py"] = "\uE943",
        [".java"] = "\uE943",
        [".cpp"] = "\uE943",
        [".c"] = "\uE943",
        [".h"] = "\uE943",
        [".html"] = "\uE943",
        [".css"] = "\uE943",
        [".json"] = "\uE943",
        [".xml"] = "\uE943",
        [".ps1"] = "\uE756",
        [".bat"] = "\uE756",
        [".cmd"] = "\uE756",
    };

    public static string ForFile(string path, bool isDirectory)
    {
        if (isDirectory)
            return Folder;

        var ext = Path.GetExtension(path);
        return ByExtension.TryGetValue(ext, out var glyph) ? glyph : GenericFile;
    }
}
