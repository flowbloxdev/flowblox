using Microsoft.Win32;
using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FlowBlox.AvaloniaDesktop.Interop.Windows
{
    public static class FontInstaller
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, nint pdv);

        private const string FontsRegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        private static bool IsFontInstalled(string fontName)
        {
            // Includes both machine-wide and per-user fonts; avoid font substitution.
            using var fonts = new InstalledFontCollection();
            return fonts.Families.Any(family =>
                string.Equals(family.Name, fontName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool InstallFontForCurrentUser(string fontPath)
        { 
            string fontName = GetFontName(fontPath);
            // The MSI installs this family system-wide. Do not create user files or
            // registry entries when either a system or user installation exists.
            if (IsFontInstalled(fontName))
                return false;

            string fontFileName = Path.GetFileName(fontPath);
            string userFontDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Microsoft", "Windows", "Fonts");

            Directory.CreateDirectory(userFontDir);
            string targetPath = Path.Combine(userFontDir, fontFileName);

            File.Copy(fontPath, targetPath, overwrite: true);

            if (AddFontResourceEx(targetPath, 0, nint.Zero) == 0)
                throw new InvalidOperationException($"Unable to load font: '{targetPath}'.");

            using (var regKey = Registry.CurrentUser.CreateSubKey(FontsRegistryPath))
            {
                if (regKey == null)
                    throw new InvalidOperationException("Unable to open the per-user font registry.");

                var fontType = Path.GetExtension(fontPath).Equals(".otf", StringComparison.OrdinalIgnoreCase)
                    ? "OpenType" : "TrueType";
                regKey.SetValue($"{fontName} ({fontType})", targetPath, RegistryValueKind.String);
            }

            return true;
        }

        private static string GetFontName(string fontPath)
        {
            using var collection = new System.Drawing.Text.PrivateFontCollection();
            collection.AddFontFile(fontPath);
            if (collection.Families.Length > 0)
                return collection.Families[0].Name;

            throw new InvalidOperationException($"Unable to retrieve display name from font file: '{fontPath}'. The file may be corrupted or not a valid font.");
        }
    }
}
