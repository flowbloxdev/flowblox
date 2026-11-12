namespace FlowBlox.Core.Util
{
    public static class UriHelper
    {
        /// <summary>
        /// Collapses repeated slashes in an absolute URL's path, preserving its query and fragment.
        /// </summary>
        public static string NormalizePathSeparators(string url)
        {
            var uri = new Uri(url, UriKind.Absolute);
            var path = System.Text.RegularExpressions.Regex.Replace(uri.AbsolutePath, @"/{2,}", "/");
            return uri.GetLeftPart(UriPartial.Authority) + path + uri.Query + uri.Fragment;
        }

        public static string ConcatUri(string uri1, string uri2)
        {
            if (string.IsNullOrEmpty(uri1))
                return uri2 ?? string.Empty;

            if (string.IsNullOrEmpty(uri2))
                return uri1 ?? string.Empty;

            uri1 = uri1.TrimEnd('/');
            uri2 = uri2.TrimStart('/');

            return $"{uri1}/{uri2}";
        }
    }
}
