using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenClip
{
    internal sealed class UpdatePackage : IDisposable
    {
        public readonly string DirectoryPath;
        public readonly string Commit;

        public UpdatePackage(string directory, string commit) { DirectoryPath = directory; Commit = commit; }
        public void Dispose() { UpdateInstaller.CleanDirectory(DirectoryPath); }
    }

    internal sealed class AutoUpdater : IDisposable
    {
        internal const string Repository = "crono2250/ScreenClip";
        internal const string ArchiveName = "ScreenClip-windows-x64.zip";
        internal const string ManifestUrl = "https://github.com/" + Repository + "/releases/latest/download/update.json";
        private const string ApiRoot = "https://api.github.com/repos/" + Repository;
        private const int MaxArchiveSize = 32 * 1024 * 1024;
        private readonly HttpClient api;
        private readonly HttpClient download;

        public AutoUpdater() : this(new HttpClientHandler { AllowAutoRedirect = false }, new HttpClientHandler { AllowAutoRedirect = false }) { }

        internal AutoUpdater(HttpMessageHandler apiHandler, HttpMessageHandler downloadHandler)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            api = new HttpClient(apiHandler) { Timeout = TimeSpan.FromSeconds(60) };
            download = new HttpClient(downloadHandler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task<UpdatePackage> CheckAsync(string currentCommit, string stagingRoot, CancellationToken cancellation, string skippedCommit = null)
        {
            if (!IsCommit(currentCommit))
                throw new InvalidOperationException("この実行ファイルにはコミット ID がありません。build.ps1 または CI でビルドした版を使用してください。");

            byte[] json = await DownloadAsync(new Uri(ManifestUrl), 16384, cancellation, true).ConfigureAwait(false);
            if (json == null) return null;
            UpdateManifest manifest;
            using (var stream = new MemoryStream(json)) manifest = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(stream);
            ValidateManifest(manifest);
            if (string.Equals(manifest.Commit, currentCommit, StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(manifest.Commit, skippedCommit, StringComparison.OrdinalIgnoreCase)) return null;

            // Public API access is needed only for a changed commit, without authentication.
            CommitComparison comparison = await CompareAsync(currentCommit, manifest.Commit, cancellation).ConfigureAwait(false);
            if (comparison.Status == "behind" || comparison.Status == "identical") return null;
            if (comparison.Status != "ahead")
                throw new InvalidOperationException("現在の版と main の履歴が分岐しているため、自動更新を見送りました。");

            // Use the pinned URL from this manifest even if Latest changes during download.
            byte[] archive = await DownloadAsync(new Uri(manifest.ArchiveUrl), MaxArchiveSize, cancellation).ConfigureAwait(false);
            if (archive.LongLength != manifest.Size || !string.Equals(Hash(archive), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新 ZIP のサイズまたは SHA-256 が一致しません。");

            cancellation.ThrowIfCancellationRequested();
            string stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                ExtractAndVerify(archive, stage, manifest.Commit);
                cancellation.ThrowIfCancellationRequested();
                return new UpdatePackage(stage, manifest.Commit);
            }
            catch { UpdateInstaller.CleanDirectory(stage); throw; }
        }

        internal static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != 1 || manifest.Repository != Repository || manifest.Branch != "main" ||
                !IsCommit(manifest.Commit) || manifest.ReleaseTag == null ||
                !Regex.IsMatch(manifest.ReleaseTag, @"\Abuild-" + manifest.Commit + @"(?:-[0-9]+-[0-9]+)?\z", RegexOptions.IgnoreCase) ||
                manifest.ArchiveUrl != "https://github.com/" + Repository + "/releases/download/" + manifest.ReleaseTag + "/" + ArchiveName ||
                manifest.Size <= 0 || manifest.Size > MaxArchiveSize || manifest.Sha256 == null || !Regex.IsMatch(manifest.Sha256, @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("公開された更新情報の形式または配布元が無効です。");
        }

        private async Task<CommitComparison> CompareAsync(string current, string candidate, CancellationToken cancellation)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + "/compare/" + current + "..." + candidate))
            {
                request.Headers.UserAgent.ParseAdd("ScreenClip-Updater/1.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using (HttpResponseMessage response = await api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                        throw new InvalidOperationException("GitHub の公開 API の利用上限に達した可能性があります。時間をおいて再試行してください。");
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException("公開リリースと現在の版の履歴を確認できませんでした（HTTP " + (int)response.StatusCode + "）。");
                    byte[] json = await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, cancellation).ConfigureAwait(false);
                    using (var stream = new MemoryStream(json))
                    {
                        var result = (CommitComparison)new DataContractJsonSerializer(typeof(CommitComparison)).ReadObject(stream);
                        if (result == null) throw new InvalidDataException("GitHub の履歴情報が空です。");
                        return result;
                    }
                }
            }
        }

        private async Task<byte[]> DownloadAsync(Uri location, int limit, CancellationToken cancellation, bool allowNotFound = false)
        {
            for (int i = 0; i < 5; i++)
            {
                ValidateDownloadUri(location);
                using (var request = new HttpRequestMessage(HttpMethod.Get, location))
                {
                    request.Headers.UserAgent.ParseAdd("ScreenClip-Updater/1.0");
                    request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                    using (HttpResponseMessage response = await download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                        {
                            if (response.Headers.Location == null) throw new InvalidDataException("ダウンロード先がありません。");
                            location = new Uri(location, response.Headers.Location);
                            continue;
                        }
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            if (allowNotFound) return null;
                            throw new InvalidOperationException("更新 ZIP が見つかりません。公開されたリリースの配布ファイルを確認してください。");
                        }
                        if (response.StatusCode != HttpStatusCode.OK)
                            throw new InvalidOperationException("公開された更新ファイルを取得できませんでした（HTTP " + status + "）。");
                        return await ReadLimitedAsync(response.Content, limit, cancellation).ConfigureAwait(false);
                    }
                }
            }
            throw new InvalidDataException("ダウンロードの転送回数が上限を超えました。");
        }

        private static void ValidateDownloadUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
                throw new InvalidDataException("安全な更新ファイルの配布先を取得できませんでした。");
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GitHub 以外の配布先にはアクセスできません。");
        }

        private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellation)
        {
            if (content.Headers.ContentLength > limit) throw new InvalidDataException("更新データのサイズが上限を超えています。");
            // HttpClient.Timeout ends at the response headers when using ResponseHeadersRead.
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                using (Stream source = await content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var result = new MemoryStream())
                {
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = await source.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (result.Length + count > limit) throw new InvalidDataException("更新データのサイズが上限を超えています。");
                        result.Write(buffer, 0, count);
                    }
                    return result.ToArray();
                }
            }
        }

        internal static void ExtractAndVerify(byte[] archive, string directory, string commit)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            using (var stream = new MemoryStream(archive))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    int limit = entry.FullName == "ScreenClip.exe" ? MaxArchiveSize : entry.FullName == "ScreenClip.exe.config" ? 1024 * 1024 : 16384;
                    if (!UpdateInstaller.PayloadFiles.Contains(entry.FullName) || !found.Add(entry.FullName) || entry.Length <= 0 || entry.Length > limit)
                        throw new InvalidDataException("更新 ZIP 内のファイル構成が無効です。");
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(Path.Combine(directory, entry.FullName), FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + count > limit) throw new InvalidDataException("展開したファイルが大きすぎます。");
                            output.Write(buffer, 0, count);
                        }
                    }
                }
            }
            if (found.Count != UpdateInstaller.PayloadFiles.Length) throw new InvalidDataException("更新に必要なファイルが不足しています。");
            VerifyPayload(directory, commit);
        }

        internal static void VerifyPayload(string directory, string commit)
        {
            string[] lines = File.ReadAllLines(Path.Combine(directory, "SHA256SUMS"));
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                Match match = Regex.Match(line, @"\A([0-9a-fA-F]{64})  (ScreenClip\.exe(?:\.config)?)\z");
                if (!match.Success || hashes.ContainsKey(match.Groups[2].Value)) throw new InvalidDataException("SHA256SUMS の形式が無効です。");
                hashes.Add(match.Groups[2].Value, match.Groups[1].Value);
            }
            foreach (string name in UpdateInstaller.InstalledFiles)
            {
                string expected;
                if (!hashes.TryGetValue(name, out expected) || !string.Equals(Hash(File.ReadAllBytes(Path.Combine(directory, name))), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新ファイルの SHA-256 が一致しません。");
            }
            // A separate domain avoids caching assemblies with the same identity across checks.
            AppDomain domain = AppDomain.CreateDomain("ScreenClip update validation");
            try
            {
                var inspector = (UpdatePayloadInspector)domain.CreateInstanceFromAndUnwrap(typeof(UpdatePayloadInspector).Assembly.Location, typeof(UpdatePayloadInspector).FullName);
                inspector.Verify(Path.Combine(directory, "ScreenClip.exe"), commit);
            }
            catch (BadImageFormatException) { throw new InvalidDataException("更新ファイルは有効な実行ファイルではありません。"); }
            finally { AppDomain.Unload(domain); }
        }

        internal static bool IsCommit(string value) { return value != null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{40}\z"); }
        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose() { api.Dispose(); download.Dispose(); }
    }

    public sealed class UpdatePayloadInspector : MarshalByRefObject
    {
        public void Verify(string path, string commit)
        {
            Assembly assembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(path));
            PortableExecutableKinds kind;
            ImageFileMachine machine;
            assembly.ManifestModule.GetPEKind(out kind, out machine);
            if (assembly.GetName().Name != "ScreenClip" || machine != ImageFileMachine.AMD64)
                throw new InvalidDataException("更新ファイルは ScreenClip の x64 ビルドではありません。");
            CustomAttributeData version = CustomAttributeData.GetCustomAttributes(assembly)
                .FirstOrDefault(a => a.AttributeType.FullName == typeof(AssemblyInformationalVersionAttribute).FullName);
            if (!AutoUpdater.IsCommit(commit) || version == null || version.ConstructorArguments.Count != 1 ||
                !string.Equals(version.ConstructorArguments[0].Value as string, commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新ファイルのコミット ID がビルド情報と一致しません。");
        }
    }

    [DataContract] internal sealed class UpdateManifest
    {
        [DataMember(Name = "schema_version")] public int SchemaVersion { get; set; }
        [DataMember(Name = "repository")] public string Repository { get; set; }
        [DataMember(Name = "branch")] public string Branch { get; set; }
        [DataMember(Name = "commit")] public string Commit { get; set; }
        [DataMember(Name = "release_tag")] public string ReleaseTag { get; set; }
        [DataMember(Name = "archive_url")] public string ArchiveUrl { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }
    [DataContract] internal sealed class CommitComparison { [DataMember(Name = "status")] public string Status { get; set; } }
}
