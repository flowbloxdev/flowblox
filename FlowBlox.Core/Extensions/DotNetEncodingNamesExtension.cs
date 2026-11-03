using FlowBlox.Core.Enums;
using System.Text;

namespace FlowBlox.Core.Extensions
{
    public static class DotNetEncodingNamesExtension
    {
        static DotNetEncodingNamesExtension()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static Encoding ToEncoding(this DotNetEncodingNames encodingName)
        {
            return encodingName switch
            {
                DotNetEncodingNames.Default => Encoding.Default,
                DotNetEncodingNames.UTF8 => Encoding.UTF8,
                DotNetEncodingNames.UTF16 => Encoding.Unicode,
                DotNetEncodingNames.UTF16_LE => Encoding.Unicode,
                DotNetEncodingNames.UTF16_BE => Encoding.BigEndianUnicode,
                DotNetEncodingNames.UTF32 => Encoding.UTF32,
                DotNetEncodingNames.ASCII => Encoding.ASCII,
                DotNetEncodingNames.ISO_8859_1 => Encoding.Latin1,
                DotNetEncodingNames.WINDOWS_1252 => Encoding.GetEncoding(1252),
                _ => throw new ArgumentOutOfRangeException(nameof(encodingName), encodingName, "Unsupported DotNetEncodingNames value.")
            };
        }
    }
}
