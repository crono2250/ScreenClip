using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenClip
{
    internal static class UpdateTests
    {
        private const string Current = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Candidate = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private static byte[] executable;
        private static readonly byte[] Config = Encoding.UTF8.GetBytes("<configuration><startup><supportedRuntime version=\"v4.0\" /></startup></configuration>");

        public static void Run()
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "obj", "update-tests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            try
            {
                executable = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "obj", "update-test-fixture", "ScreenClip.exe"));
                TestSettings();
                TestManifest();
                TestGeneratedPackage(root);
                TestPackages(root);
                TestNetworkAsync(root).GetAwaiter().GetResult();
                TestReplacement(root);
                TestProcesses(root);
                Console.WriteLine("OK: token-free releases, settings migration, manifests, generated package, download redirects, checksum/version, archive paths, cancellation, replacement, process startup and rollback");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected rejection: " + typeof(T).Name);
        }

        private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action(); }
            catch (T) { return; }
            throw new Exception("Expected rejection: " + typeof(T).Name);
        }

        private static void TestSettings()
        {
            const string old = "{\"ImageFormat\":\"png\",\"KeyCode\":83,\"Modifiers\":6}";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(old)))
            {
                AppSettings settings = SettingsStore.Read(stream);
                Assert(settings.AutoUpdateEnabled == true && settings.UpdateIntervalMinutes == 30, "Update settings migration failed");
            }
            const string disabled = "{\"ImageFormat\":\"png\",\"KeyCode\":83,\"Modifiers\":6,\"AutoUpdateEnabled\":false,\"UpdateIntervalMinutes\":1}";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(disabled)))
            {
                AppSettings settings = SettingsStore.Read(stream).Copy();
                Assert(settings.AutoUpdateEnabled == false && settings.UpdateIntervalMinutes == 30, "Update settings opt-out/range validation failed");
            }
            const string previousTokenSettings = "{\"ImageFormat\":\"png\",\"KeyCode\":83,\"Modifiers\":6,\"AutoUpdateEnabled\":false,\"UpdateIntervalMinutes\":60,\"ProtectedGitHubToken\":\"obsolete\"}";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(previousTokenSettings)))
            {
                AppSettings settings = SettingsStore.Read(stream).Copy();
                Assert(settings.AutoUpdateEnabled == false && settings.UpdateIntervalMinutes == 60, "Token-era settings were not preserved");
                using (var saved = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(saved, settings);
                    Assert(!Encoding.UTF8.GetString(saved.ToArray()).Contains("ProtectedGitHubToken"), "Obsolete credentials were persisted again");
                }
            }
        }

        private static UpdateManifest ValidManifest(byte[] archive)
        {
            string tag = "build-" + Candidate + "-123-1";
            return new UpdateManifest
            {
                SchemaVersion = 1, Repository = AutoUpdater.Repository, Branch = "main", Commit = Candidate, ReleaseTag = tag,
                ArchiveUrl = "https://github.com/" + AutoUpdater.Repository + "/releases/download/" + tag + "/" + AutoUpdater.ArchiveName,
                Sha256 = AutoUpdater.Hash(archive), Size = archive.LongLength
            };
        }

        private static void TestManifest()
        {
            byte[] archive = { 1, 2, 3 };
            AutoUpdater.ValidateManifest(ValidManifest(archive));
            foreach (Action<UpdateManifest> corrupt in new Action<UpdateManifest>[]
            {
                m => m.SchemaVersion = 2, m => m.Repository = "other/ScreenClip", m => m.Branch = "feature", m => m.Commit = "bad",
                m => m.ReleaseTag = "latest", m => m.ArchiveUrl = "https://example.test/ScreenClip.exe",
                m => m.ArchiveUrl = "https://github.com/" + AutoUpdater.Repository + "/releases/latest/download/" + AutoUpdater.ArchiveName,
                m => m.Sha256 = "bad", m => m.Size = 0, m => m.Size = 33 * 1024 * 1024
            })
            {
                UpdateManifest manifest = ValidManifest(archive);
                corrupt(manifest);
                Reject<InvalidDataException>(() => AutoUpdater.ValidateManifest(manifest));
            }
            Reject<InvalidDataException>(() => AutoUpdater.ValidateManifest(null));
        }

        private static void TestGeneratedPackage(string root)
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "obj", "update-test-fixture");
            UpdateManifest manifest;
            using (Stream stream = File.OpenRead(Path.Combine(fixture, "update.json")))
                manifest = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(stream);
            AutoUpdater.ValidateManifest(manifest);
            byte[] archive = File.ReadAllBytes(Path.Combine(fixture, AutoUpdater.ArchiveName));
            Assert(manifest.Commit == Candidate && manifest.Size == archive.LongLength && manifest.Sha256 == AutoUpdater.Hash(archive), "package.ps1 metadata does not match the archive");
            AutoUpdater.ExtractAndVerify(archive, Subdirectory(root), Candidate);
        }

        private static byte[] MakeArchive(string extraName, bool corrupt, bool missingConfig)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(zip, "ScreenClip.exe", executable);
                    if (!missingConfig) Add(zip, "ScreenClip.exe.config", Config);
                    string checksums = (corrupt ? new string('0', 64) : AutoUpdater.Hash(executable)) + "  ScreenClip.exe\n" +
                        AutoUpdater.Hash(Config) + "  ScreenClip.exe.config\n";
                    Add(zip, "SHA256SUMS", Encoding.ASCII.GetBytes(checksums));
                    if (extraName != null) Add(zip, extraName, executable);
                }
                return stream.ToArray();
            }
        }

        private static void Add(ZipArchive zip, string name, byte[] contents)
        {
            using (Stream entry = zip.CreateEntry(name).Open()) entry.Write(contents, 0, contents.Length);
        }

        private static string Subdirectory(string root)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TestPackages(string root)
        {
            string valid = Subdirectory(root);
            AutoUpdater.ExtractAndVerify(MakeArchive(null, false, false), valid, Candidate);
            Assert(File.ReadAllBytes(Path.Combine(valid, "ScreenClip.exe")).SequenceEqual(executable), "Payload extraction failed");
            Reject<InvalidDataException>(() => AutoUpdater.ExtractAndVerify(MakeArchive(null, true, false), Subdirectory(root), Candidate));
            Reject<InvalidDataException>(() => AutoUpdater.ExtractAndVerify(MakeArchive(null, false, false), Subdirectory(root), Current));
            Reject<InvalidDataException>(() => AutoUpdater.ExtractAndVerify(MakeArchive("../escape.exe", false, false), Subdirectory(root), Candidate));
            Reject<InvalidDataException>(() => AutoUpdater.ExtractAndVerify(MakeArchive("ScreenClip.exe", false, false), Subdirectory(root), Candidate));
            Reject<InvalidDataException>(() => AutoUpdater.ExtractAndVerify(MakeArchive(null, false, true), Subdirectory(root), Candidate));
            Assert(!File.Exists(Path.Combine(root, "escape.exe")), "Archive escaped staging directory");
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            public readonly List<string> Paths = new List<string>();
            public Func<HttpRequestMessage, HttpResponseMessage> Respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Assert(request.Headers.Authorization == null, "An update request sent authentication");
                Paths.Add(request.RequestUri.AbsolutePath);
                return Task.FromResult(Respond(request));
            }
        }

        private static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage ManifestResponse(UpdateManifest manifest)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(UpdateManifest)).WriteObject(stream, manifest);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stream.ToArray()) };
            }
        }

        private static HttpResponseMessage Redirect(string url)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(url);
            return response;
        }

        private static AutoUpdater FakeUpdater(byte[] archive, string comparison, out FakeHandler apiHandler, out FakeHandler downloadHandler, Action<UpdateManifest> configure = null)
        {
            UpdateManifest manifest = ValidManifest(archive);
            if (configure != null) configure(manifest);
            apiHandler = new FakeHandler();
            downloadHandler = new FakeHandler();
            apiHandler.Respond = request =>
            {
                string path = request.RequestUri.AbsolutePath;
                if (path.Contains("/compare/")) return Json("{\"status\":\"" + comparison + "\"}");
                throw new Exception("Unexpected API path " + path);
            };
            downloadHandler.Respond = request =>
            {
                Assert(request.Headers.CacheControl != null && request.Headers.CacheControl.NoCache, "Update request allows stale cached metadata");
                string url = request.RequestUri.AbsoluteUri;
                if (url == AutoUpdater.ManifestUrl)
                    return Redirect("https://github.com/" + AutoUpdater.Repository + "/releases/download/" + manifest.ReleaseTag + "/update.json");
                if (request.RequestUri.AbsolutePath.EndsWith("/update.json")) return ManifestResponse(manifest);
                if (url == manifest.ArchiveUrl) return Redirect("https://release-assets.githubusercontent.com/archive");
                if (url == "https://release-assets.githubusercontent.com/archive")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
                throw new Exception("Unexpected public download path " + url);
            };
            return new AutoUpdater(apiHandler, downloadHandler);
        }

        private static async Task TestNetworkAsync(string root)
        {
            byte[] archive = MakeArchive(null, false, false);
            FakeHandler api, download;
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download))
            {
                Assert(await updater.CheckAsync(Candidate, root, CancellationToken.None) == null && api.Paths.Count == 0 && download.Paths.Count == 2, "Unchanged version used the API or downloaded the ZIP");
                Assert(await updater.CheckAsync(Current, root, CancellationToken.None, Candidate) == null && api.Paths.Count == 0, "Known failed update retried automatically");
                using (UpdatePackage package = await updater.CheckAsync(Current, root, CancellationToken.None))
                    Assert(package != null && package.Commit == Candidate && api.Paths.Count == 1 && download.Paths.Count == 8, "New update was not prepared from its pinned release");
                await RejectAsync<InvalidOperationException>(() => updater.CheckAsync("unversioned", root, CancellationToken.None));
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    await RejectAsync<OperationCanceledException>(() => updater.CheckAsync(Current, root, cancellation.Token));
                }
            }
            using (AutoUpdater updater = FakeUpdater(archive, "behind", out api, out download))
                Assert(await updater.CheckAsync(Current, root, CancellationToken.None) == null && download.Paths.Count == 2, "Updater attempted downgrade");
            using (AutoUpdater updater = FakeUpdater(archive, "diverged", out api, out download))
                await RejectAsync<InvalidOperationException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download, m => m.Sha256 = new string('0', 64)))
                await RejectAsync<InvalidDataException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download, m => m.Size++))
                await RejectAsync<InvalidDataException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download))
            {
                download.Respond = request => Redirect("http://release-assets.githubusercontent.com/archive");
                await RejectAsync<InvalidDataException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
                download.Respond = request => Redirect("https://other.example.test/archive");
                await RejectAsync<InvalidDataException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
                download.Respond = request => Redirect(AutoUpdater.ManifestUrl);
                await RejectAsync<InvalidDataException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
                download.Respond = request => new HttpResponseMessage(HttpStatusCode.NotFound);
                Assert(await updater.CheckAsync(Current, root, CancellationToken.None) == null && api.Paths.Count == 0, "An unpublished manifest should silently skip the update");
            }
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download))
            {
                Func<HttpRequestMessage, HttpResponseMessage> publicDownload = download.Respond;
                download.Respond = request => request.RequestUri.AbsolutePath.EndsWith("/" + AutoUpdater.ArchiveName)
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) : publicDownload(request);
                await RejectAsync<InvalidOperationException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
            }
            using (AutoUpdater updater = FakeUpdater(archive, "ahead", out api, out download))
            {
                api.Respond = request => new HttpResponseMessage(HttpStatusCode.Forbidden);
                await RejectAsync<InvalidOperationException>(() => updater.CheckAsync(Current, root, CancellationToken.None));
            }
        }

        private static void TestReplacement(string root)
        {
            string stage = Subdirectory(root);
            string install = Subdirectory(root);
            string target = Path.Combine(install, "ScreenClip.exe");
            string config = target + ".config";
            File.WriteAllText(Path.Combine(stage, "ScreenClip.exe"), "new exe");
            File.WriteAllText(Path.Combine(stage, "ScreenClip.exe.config"), "new config");
            File.WriteAllText(target, "old exe");
            File.WriteAllText(config, "old config");
            File.WriteAllText(Path.Combine(install, "screenshot.png"), "keep");
            UpdateInstaller.ApplyFiles(stage, target, () => File.ReadAllText(target) == "new exe" && File.ReadAllText(config) == "new config");
            Assert(File.ReadAllText(target) == "new exe" && File.ReadAllText(config) == "new config", "Update did not replace both files");
            Assert(Directory.GetFiles(install).Length == 3 && File.ReadAllText(Path.Combine(install, "screenshot.png")) == "keep", "Update touched unrelated files or retained backups");

            File.WriteAllText(target, "old exe");
            File.WriteAllText(config, "old config");
            Reject<InvalidOperationException>(() => UpdateInstaller.ApplyFiles(stage, target, () => false));
            Assert(File.ReadAllText(target) == "old exe" && File.ReadAllText(config) == "old config", "Failed launch rollback lost original files");
            File.Delete(config);
            Reject<InvalidOperationException>(() => UpdateInstaller.ApplyFiles(stage, target, () => false));
            Assert(File.ReadAllText(target) == "old exe" && !File.Exists(config), "Rollback did not restore missing original config");

            File.WriteAllText(config, "old config");
            using (var locked = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject<IOException>(() => UpdateInstaller.ApplyFiles(stage, target, () => { throw new Exception("Should not launch with locked config"); }));
            Assert(File.ReadAllText(target) == "old exe" && File.ReadAllText(config) == "old config", "Partial replacement rollback failed");
        }

        private static void TestProcesses(string root)
        {
            string stage = Subdirectory(root);
            string install = Subdirectory(root);
            string target = Path.Combine(install, "ScreenClip.exe");
            File.WriteAllBytes(target, executable);
            using (var parent = Process.Start(new ProcessStartInfo(target, "--test-parent") { UseShellExecute = false, CreateNoWindow = true }))
            {
                UpdateInstaller.WaitForParent(new UpdatePlan { ParentId = parent.Id, ParentStartedUtcTicks = parent.StartTime.ToUniversalTime().Ticks, TargetPath = target });
                Assert(parent.HasExited, "Updater did not wait for the original process");
            }
            File.WriteAllText(Path.Combine(install, "update-test-stage.txt"), stage);
            string ready = Path.Combine(stage, "ready");
            try
            {
                Assert(UpdateInstaller.StartAndConfirm(target, Path.GetFileName(stage), stage), "New process startup was not confirmed");
            }
            finally
            {
                if (File.Exists(ready))
                {
                    using (Process child = Process.GetProcessById(int.Parse(File.ReadAllText(ready))))
                    {
                        Assert(child.MainModule.FileName == target, "Unexpected test process");
                        if (!child.HasExited) { child.Kill(); child.WaitForExit(10000); }
                    }
                }
            }
            File.WriteAllBytes(Path.Combine(stage, "ScreenClip.exe"), executable);
            File.WriteAllBytes(Path.Combine(stage, "ScreenClip.exe.config"), Config);
            File.WriteAllText(target, "old exe");
            File.WriteAllText(target + ".config", "old config");
            File.WriteAllText(Path.Combine(install, "fail-startup"), "");
            Reject<InvalidOperationException>(() => UpdateInstaller.ApplyFiles(stage, target, () => UpdateInstaller.StartAndConfirm(target, Path.GetFileName(stage), stage)));
            Assert(File.ReadAllText(target) == "old exe" && File.ReadAllText(target + ".config") == "old config", "Real startup failure did not restore the old version");
        }
    }
}
