namespace AlUmmahNow.BunnyBulkUploader
{
    /// <summary>Validates the <c>--root</c> option.</summary>
    public static class UploadRoot
    {
        /// <summary>
        /// Normalises <paramref name="value"/> to <c>/zone/path/</c>. It must name a folder below the configured
        /// storage zone (Bunny rejects paths that don't start with <c>/zone/</c>), and not the zone root itself,
        /// so separate runs can't drop same-named files into one shared folder by accident.
        /// </summary>
        public static bool TryNormalize(string? value, string? zone, out string root, out string error)
        {
            root = "";
            error = "";
            var v = (value ?? "").Trim().Replace('\\', '/');
            if (v.Length == 0)
            {
                error = "--root is required, e.g. --root /my-zone/channel-name/";
                return false;
            }
            if (!v.StartsWith('/')) v = "/" + v;
            if (!v.EndsWith('/')) v += "/";
            if (v.Contains("//") || v.Split('/').Any(s => s is "." or ".."))
            {
                error = $"--root '{value}' is not a valid storage path.";
                return false;
            }
            if (!string.IsNullOrEmpty(zone))
            {
                var zonePrefix = $"/{zone}/";
                if (!v.StartsWith(zonePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"--root must start with the storage zone '{zonePrefix}'.";
                    return false;
                }
                if (v.Length == zonePrefix.Length)
                {
                    error = $"--root must name a folder inside the zone, e.g. {zonePrefix}channel-name/";
                    return false;
                }
            }
            root = v;
            return true;
        }
    }
}
