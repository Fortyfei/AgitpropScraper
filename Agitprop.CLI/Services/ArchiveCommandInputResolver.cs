using Agitprop.Core.Enums;
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
    List<NewsSites> Sites,
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

        if (!TryResolveSites(input.Newsites ?? [], out var sites, out var siteError, out var warnings))
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

    private static bool TryResolveSites(string[] newsites, out List<NewsSites> sites, out string error, out List<string> warnings)
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
            sites = Enum.GetValues<NewsSites>().ToList();
            return true;
        }

        sites = [];
        var invalidTokens = new List<string>();

        if (useDefault || rawTokens.Any(token => token.Equals("default", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultSites=new List<NewsSites>
            {
                NewsSites.Origo,
                NewsSites.Ripost,
                NewsSites.Mandiner,
                NewsSites.Metropol,
                NewsSites.MagyarNemzet,
                NewsSites.PestiSracok,
                NewsSites.MagyarJelen,
                NewsSites.Kurucinfo,
                NewsSites.Alfahir,
                NewsSites.HuszonnegyHu,
                NewsSites.NegyNegyNegy,
                NewsSites.HVG,
                NewsSites.Telex,
                NewsSites.Index,
                NewsSites.Merce
            };

            AddUniqueSites(sites, defaultSites);
        }

        foreach (var token in rawTokens)
        {
            if (token.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Enum.TryParse<NewsSites>(token, true, out var parsedSite))
            {
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

    private static string ResolveConfigPath(string feedConfigPath)
    {
        if (Path.IsPathRooted(feedConfigPath))
        {
            return feedConfigPath;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), feedConfigPath));
    }

    private static void AddUniqueSites(List<NewsSites> target, IEnumerable<NewsSites> toAdd)
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
