using System;

public static class Helpers
{
    public static string GetRepositoryUrl(Uri currentUrl, string applicationPath)
    {
        return GetBaseUrl(currentUrl, applicationPath) + "nuget";
    }

    public static string GetPushUrl(Uri currentUrl, string applicationPath)
    {
        return GetBaseUrl(currentUrl, applicationPath);
    }

    public static string GetBaseUrl(Uri currentUrl, string applicationPath)
    {
        var uriBuilder = new UriBuilder(currentUrl);

        string repositoryUrl = uriBuilder.Scheme + "://" + uriBuilder.Host;
        if (uriBuilder.Port != 80)
        {
            repositoryUrl += ":" + uriBuilder.Port;
        }

        repositoryUrl += applicationPath;

        // ApplicationPath for Virtual Apps don't end with /
        return EnsureTrailingSlash(repositoryUrl);
    }

    internal static string EnsureTrailingSlash(string path)
    {
        if (String.IsNullOrEmpty(path))
        {
            return path;
        }

        if (!path.EndsWith("/"))
        {
            return path + "/";
        }
        return path;
    }
}
