using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Serilog;

namespace Dpz.ServiceHub.Services;

public sealed record S3Connection(
    string Endpoint,
    string Bucket,
    string? Region,
    string AccessKey,
    string SecretKey,
    bool ForcePathStyle
);

public sealed record S3UploadFile(string LocalPath, string RelativePath, long Length);

public sealed record S3UploadProgress(
    string CurrentFileName,
    double CurrentFilePercent,
    double TotalPercent,
    int CompletedFiles,
    int TotalFiles
);

public sealed class S3UploadService
{
    public static IReadOnlyList<S3UploadFile> CollectArtifacts(IEnumerable<string> artifactPaths)
    {
        var files = new List<S3UploadFile>();
        foreach (var rawPath in artifactPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var path = rawPath.Trim().Trim('"');
            if (File.Exists(path))
            {
                files.Add(
                    new S3UploadFile(path, Path.GetFileName(path), new FileInfo(path).Length)
                );
                continue;
            }

            if (!Directory.Exists(path))
            {
                throw new FileNotFoundException("Artifact path does not exist.", path);
            }

            foreach (
                var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            )
            {
                var relative = Path.GetRelativePath(path, filePath).Replace('\\', '/');
                files.Add(new S3UploadFile(filePath, relative, new FileInfo(filePath).Length));
            }
        }

        return files;
    }

    public static string CombineKey(string remotePrefix, string relativePath)
    {
        var prefix = remotePrefix.Trim().Replace('\\', '/').Trim('/');
        var relative = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new ArgumentException("Relative path cannot be empty.", nameof(relativePath));
        }

        return string.IsNullOrWhiteSpace(prefix) ? relative : $"{prefix}/{relative}";
    }

    public async Task UploadAsync(
        S3Connection connection,
        string remotePrefix,
        IReadOnlyList<S3UploadFile> files,
        IProgress<S3UploadProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            throw new InvalidOperationException("No artifact files were found to upload.");
        }

        using var client = CreateClient(connection);
        var totalBytes = files.Sum(file => file.Length);
        var completedBytes = 0L;
        var completedFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = CombineKey(remotePrefix, file.RelativePath);

            Report(
                progress,
                file.RelativePath,
                transferred: 0,
                fileLength: file.Length,
                completedBytes,
                totalBytes,
                completedFiles,
                files.Count
            );

            await using var stream = new FileStream(
                file.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );

            var request = new PutObjectRequest
            {
                BucketName = connection.Bucket.Trim(),
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
            };
            request.Headers.ContentLength = file.Length;
            request.StreamTransferProgress += (_, args) =>
            {
                Report(
                    progress,
                    file.RelativePath,
                    args.TransferredBytes,
                    file.Length,
                    completedBytes,
                    totalBytes,
                    completedFiles,
                    files.Count
                );
            };

            try
            {
                await client.PutObjectAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Failed to upload file {LocalPath} to S3 key {ObjectKey}.",
                    file.LocalPath,
                    key
                );
                throw;
            }

            completedFiles++;
            completedBytes += file.Length;
            Report(
                progress,
                file.RelativePath,
                file.Length,
                file.Length,
                completedBytes - file.Length,
                totalBytes,
                completedFiles,
                files.Count
            );
        }
    }

    internal static IAmazonS3 CreateClient(S3Connection connection)
    {
        var endpoint = connection.Endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("S3 endpoint is invalid.");
        }

        var region = string.IsNullOrWhiteSpace(connection.Region)
            ? InferRegion(endpoint)
            : connection.Region.Trim();

        var credentials = new BasicAWSCredentials(
            connection.AccessKey.Trim(),
            connection.SecretKey
        );
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = region,
            ForcePathStyle = connection.ForcePathStyle,
        };

        return new AmazonS3Client(credentials, config);
    }

    private static string InferRegion(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return "us-east-1";
        }

        var parts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var cosIndex = Array.FindIndex(
            parts,
            x => x.Equals("cos", StringComparison.OrdinalIgnoreCase)
        );
        if (cosIndex >= 0 && cosIndex + 1 < parts.Length)
        {
            return parts[cosIndex + 1];
        }

        return "us-east-1";
    }

    private static void Report(
        IProgress<S3UploadProgress>? progress,
        string currentFileName,
        long transferred,
        long fileLength,
        long completedBytes,
        long totalBytes,
        int completedFiles,
        int totalFiles
    )
    {
        if (progress == null)
        {
            return;
        }

        var safeFileLength = Math.Max(1, fileLength);
        var clampedTransferred = Math.Clamp(transferred, 0, safeFileLength);
        var uploaded = completedBytes + clampedTransferred;

        progress.Report(
            new S3UploadProgress(
                currentFileName,
                ToPercent(clampedTransferred, safeFileLength),
                ToPercent(uploaded, Math.Max(1, totalBytes)),
                completedFiles,
                totalFiles
            )
        );
    }

    private static double ToPercent(long value, long total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp(value * 100.0 / total, 0, 100);
    }
}
