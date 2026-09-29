using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Agitprop.Scraper.NLPService;

/// <summary>
/// Downloads and SHA-256-verifies the pinned Hungarian NER model assets from Hugging Face on
/// first use, so no separate provisioning script or CI step is required. Files already present
/// on disk are reused as-is (only their checksum is verified).
/// </summary>
internal static class NerModelProvisioner
{
    private const string Revision = "ca25da5ba270fa7f626d2291fc9bdae452d9ef64";
    private const string RepositoryBaseUrl =
        $"https://huggingface.co/foltin/nerkor-hubert-hungarian-onnx/resolve/{Revision}";

    private static readonly IReadOnlyDictionary<string, string> AssetChecksums =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["model.onnx"] = "3c8c8dd3f2d53fd7e39e331b14aeacac3fbc32f65956781c9be77662bca06b0a",
            ["tokenizer.json"] = "464f917e97aa6f96a10af4f9f17351b4fb4ba0986270982caf864f8e50690d2d",
            ["config.json"] = "b9aaf13a05a9799bff07fb12375f331f063309c1df1ac1dd6fcba4449ac321c4",
        };

    private static readonly Lazy<HttpClient> HttpClientFactory = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    });

    internal static void EnsureAssets(string directory, ILogger logger)
    {
        Directory.CreateDirectory(directory);

        foreach (var (fileName, expectedHash) in AssetChecksums)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                logger.LogInformation("Downloading Hungarian NER model asset '{FileName}'...", fileName);
                Download(fileName, path);
            }

            var actualHash = ComputeSha256(path);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 validation failed for '{path}': expected {expectedHash}, got {actualHash}. " +
                    "Delete the file to allow it to be re-downloaded.");
            }
        }

        logger.LogInformation("Hungarian NER model assets are verified in '{Directory}'.", directory);
    }

    private static void Download(string fileName, string destinationPath)
    {
        var temporaryPath = destinationPath + ".download";
        try
        {
            using var response = HttpClientFactory.Value
                .GetAsync($"{RepositoryBaseUrl}/{fileName}", HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter()
                .GetResult();
            response.EnsureSuccessStatusCode();

            using (var destination = File.Create(temporaryPath))
            using (var source = response.Content.ReadAsStream())
            {
                source.CopyTo(destination);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
