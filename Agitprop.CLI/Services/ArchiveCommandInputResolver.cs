using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace Agitprop.CLI.Services;

public interface IArchiveCommandInputResolver
{
    ArchiveCommandInputResolution Resolve(ArchiveCommandRawInput input);
}

public sealed record ArchiveCommandRawInput(
    string Date,
    string? From,
    string? To,
    string[] Newsites,
    string FeedConfigPath,
    bool VerboseAlias,
    string Verbosity);

public sealed record ArchiveCommandResolvedInput(
    List<DateOnly> Dates,
    List<SourceId> Sites,
    CliOutputVerbosity Verbosity,
    bool IsRangeMode,
    string ResolvedFeedConfigPath,
    List<string> Warnings);

public sealed record ArchiveCommandInputResolution(
    bool Success,
    ArchiveCommandResolvedInput? ResolvedInput,
    string? ErrorMessage)
{
    public static ArchiveCommandInputResolution Failed(string errorMessage)
    {
        return new ArchiveCommandInputResolution(false, null, errorMessage);
    }

    public static ArchiveCommandInputResolution Succeeded(ArchiveCommandResolvedInput resolvedInput)
    {
        return new ArchiveCommandInputResolution(true, resolvedInput, null);
    }
}

public enum CliOutputVerbosity
{
    Quiet,
    Normal,
    Detailed,
}

public sealed class ArchiveCommandInputResolver : IArchiveCommandInputResolver
{
    private static readonly string[] SupportedDateFormats = ["yyyy-MM-dd", "yyyy.MM.dd"];
    private static readonly INewsfeedSourceCatalog Catalog = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());
    private static readonly List<SourceId> DateArchiveFallbackSites =
        [.. Catalog.All.Where(source => source.GetArchiveUrl(DateOnly.MinValue) is not null).Select(source => source.Id)];

    public ArchiveCommandInputResolution Resolve(ArchiveCommandRawInput input)
    {
        if (!TryResolveVerbosity(input.Verbosity, input.VerboseAlias, out var resolvedVerbosity))
        {
            return ArchiveCommandInputResolution.Failed("Invalid verbosity. Use quiet, normal, or detailed.");
        }

        if (!TryResolveDates(input.Date, input.From, input.To, out var dates, out var dateError))
        {
            return ArchiveCommandInputResolution.Failed(dateError);
        }

        if (!TryResolveSites(input.Newsites ?? [], input.FeedConfigPath, out var sites, out var siteError, out var warnings))
        {
            return ArchiveCommandInputResolution.Failed(siteError);
        }

        if (sites.Count == 0)
        {
            return ArchiveCommandInputResolution.Failed("No valid news sites specified.");
        }

        var resolvedInput = new ArchiveCommandResolvedInput(
            dates,
            sites,
            resolvedVerbosity,
            !string.IsNullOrWhiteSpace(input.From) || !string.IsNullOrWhiteSpace(input.To),
            ResolveConfigPath(input.FeedConfigPath),
            warnings);

        return ArchiveCommandInputResolution.Succeeded(resolvedInput);
    }

    private static bool TryResolveVerbosity(string verbosity, bool verboseAlias, out CliOutputVerbosity resolved)
    {
        resolved = CliOutputVerbosity.Normal;
        if (verboseAlias)
        {
            resolved = CliOutputVerbosity.Detailed;
            return true;
        }

        return verbosity.Trim().ToLowerInvariant() switch
        {
            "quiet" => SetVerbosity(CliOutputVerbosity.Quiet, out resolved),
            "normal" => SetVerbosity(CliOutputVerbosity.Normal, out resolved),
            "detailed" => SetVerbosity(CliOutputVerbosity.Detailed, out resolved),
            _ => false,
        };
    }

    private static bool SetVerbosity(CliOutputVerbosity value, out CliOutputVerbosity resolved)
    {
        resolved = value;
        return true;
    }

    private static bool TryResolveDates(string date, string? from, string? to, out List<DateOnly> dates, out string error)
    {
        dates = [];
        error = string.Empty;

        bool hasFrom = !string.IsNullOrWhiteSpace(from);
        bool hasTo = !string.IsNullOrWhiteSpace(to);
        bool hasRange = hasFrom || hasTo;

        if (hasRange && (hasFrom != hasTo))
        {
            error = "Both --from and --to must be provided for range mode.";
            return false;
        }

        if (hasRange && !string.IsNullOrWhiteSpace(date) && !IsTodayDefault(date))
        {
            error = "--date cannot be combined with --from/--to.";
            return false;
        }

        if (hasRange)
        {
            if (!TryParseDate(from!, out var fromDate) || !TryParseDate(to!, out var toDate))
            {
                error = "Invalid range date format. Use yyyy-mm-dd or yyyy.mm.dd.";
                return false;
            }

            if (fromDate > toDate)
            {
                error = "--from must be less than or equal to --to.";
                return false;
            }

            for (var current = fromDate; current <= toDate; current = current.AddDays(1))
            {
                dates.Add(current);
            }

            return true;
        }

        if (!TryParseDate(date, out var parsedDate))
        {
            error = "Invalid date format. Use yyyy-mm-dd or yyyy.mm.dd.";
            return false;
        }

        dates.Add(parsedDate);
        return true;
    }

    private static bool IsTodayDefault(string date)
    {
        if (!TryParseDate(date, out var parsedDate))
        {
            return false;
        }

        return parsedDate == DateOnly.FromDateTime(DateTime.Today);
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        return DateOnly.TryParseExact(value, SupportedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(value, out date);
    }

    private static bool TryResolveSites(string[] newsites, string feedConfigPath, out List<SourceId> sites, out string error, out List<string> warnings)
    {
        error = string.Empty;
        warnings = [];
        var rawTokens = newsites
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var useDefault = rawTokens.Count == 0;
        if (!useDefault && rawTokens.Any(token => token.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            sites = Catalog.All.Select(source => source.Id).ToList();
            return true;
        }

        sites = [];
        var invalidTokens = new List<string>();

        if (useDefault || rawTokens.Any(token => token.Equals("default", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryResolveDefaultSites(feedConfigPath, out var defaultSites, out var defaultError, out var defaultWarnings))
            {
                warnings.Add($"Warning: {defaultError} Falling back to built-in default site list.");
                defaultSites = [.. DateArchiveFallbackSites];
            }

            foreach (var warning in defaultWarnings)
            {
                warnings.Add(warning);
            }

            AddUniqueSites(sites, defaultSites);
        }

        foreach (var token in rawTokens)
        {
            if (token.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Catalog.TryGetById(token, out var parsedSource))
            {
                var parsedSite = parsedSource.Id;
                if (!IsDateArchiveSupported(parsedSite))
                {
                    warnings.Add($"Warning: '{parsedSite}' does not support date archive scraping and will be skipped.");
                    continue;
                }

                AddUniqueSites(sites, [parsedSite]);
                continue;
            }

            invalidTokens.Add(token);
        }

        if (invalidTokens.Count > 0 && sites.Count == 0)
        {
            error = $"Unknown news sites: {string.Join(", ", invalidTokens)}.";
            return false;
        }

        foreach (var invalid in invalidTokens)
        {
            warnings.Add($"Warning: Unknown news site '{invalid}' will be ignored.");
        }

        return true;
    }

    private static bool TryResolveDefaultSites(string feedConfigPath, out List<SourceId> sites, out string error, out List<string> warnings)
    {
        sites = [];
        warnings = [];
        error = string.Empty;

        var configPath = ResolveConfigPath(feedConfigPath);
        if (!File.Exists(configPath))
        {
            error = $"Default site resolution failed because feed config was not found at '{configPath}'.";
            return false;
        }

        try
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: false)
                .Build();

            var feeds = config.GetSection("Feeds").Get<string[]>() ?? [];
            if (feeds.Length == 0)
            {
                error = $"Default site resolution failed because Feeds is empty in '{configPath}'.";
                return false;
            }

            foreach (var feedUrl in feeds)
            {
                if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
                {
                    warnings.Add($"Warning: Ignoring invalid feed URL '{feedUrl}'.");
                    continue;
                }

                if (!TryMapHostToSite(uri.Host, out var site))
                {
                    warnings.Add($"Warning: Feed host '{uri.Host}' is not mapped to a known site and will be ignored.");
                    continue;
                }

                if (!IsDateArchiveSupported(site))
                {
                    warnings.Add($"Warning: '{site}' from feed config does not support date archive scraping and will be skipped.");
                    continue;
                }

                AddUniqueSites(sites, [site]);
            }

            if (sites.Count == 0)
            {
                error = $"Default site resolution failed because no date-compatible mapped sites were found in '{configPath}'.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Default site resolution failed: {ex.Message}";
            return false;
        }
    }

    private static string ResolveConfigPath(string feedConfigPath)
    {
        if (Path.IsPathRooted(feedConfigPath))
        {
            return feedConfigPath;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), feedConfigPath));
    }

    private static bool TryMapHostToSite(string host, out SourceId site)
    {
        if (Catalog.TryGetByFeedHost(host.Trim(), out var source))
        {
            site = source.Id;
            return true;
        }

        site = default;
        return false;
    }

    private static bool IsDateArchiveSupported(SourceId site)
    {
        return Catalog.TryGetById(site.Value, out var source)
            && source.GetArchiveUrl(DateOnly.MinValue) is not null;
    }

    private static void AddUniqueSites(List<SourceId> target, IEnumerable<SourceId> toAdd)
    {
        foreach (var site in toAdd)
        {
            if (!target.Contains(site))
            {
                target.Add(site);
            }
        }
    }
}
