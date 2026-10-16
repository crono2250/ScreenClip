using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace ScreenClip
{
    internal static class UpdateInstaller
    {
        internal const string ApplyArgument = "--apply-update";
        internal const string UpdatedArgument = "--updated";
        internal const string FailedArgument = "--update-failed";
        internal static readonly string[] InstalledFiles = { "ScreenClip.exe", "ScreenClip.exe.config" };
        internal static readonly string[] PayloadFiles = { "ScreenClip.exe", "ScreenClip.exe.config", "SHA256SUMS" };
        internal static readonly string StagingRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenClip", "Updates");
        private static readonly string LogPath = Path.Combine(StagingRoot, "last-update.log");
        private static readonly string FailedCommitPath = Path.Combine(StagingRoot, "failed-commit");

        public static string FailedCommit
        {
            get
            {
                try { return File.Exists(FailedCommitPath) ? File.ReadAllText(FailedCommitPath).Trim() : null; }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
        }

        public static void Begin(UpdatePackage package)
        {
            string target = System.Windows.Forms.Application.ExecutablePath;
            if (!string.Equals(Path.GetFileName(target), InstalledFiles[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("自動更新には実行ファイル名 ScreenClip.exe を使用してください。");
            // Check write access before closing the running app. No administrator prompt is needed.
            string probe = Path.Combine(Path.GetDirectoryName(target), ".screenclip-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
            File.Delete(probe);
            AutoUpdater.VerifyPayload(package.DirectoryPath, package.Commit);
            string helper = Path.Combine(package.DirectoryPath, "UpdateHost.exe");
            File.Copy(target, helper, true);
            if (File.Exists(target + ".config")) File.Copy(target + ".config", helper + ".config", true);
            using (Process current = Process.GetCurrentProcess())
            {
                var plan = new UpdatePlan
                {
                    TargetPath = target, ParentId = current.Id,
                    ParentStartedUtcTicks = current.StartTime.ToUniversalTime().Ticks, Commit = package.Commit
                };
                using (var stream = File.Create(Path.Combine(package.DirectoryPath, "plan.json")))
                    new DataContractJsonSerializer(typeof(UpdatePlan)).WriteObject(stream, plan);
            }
            using (Process process = Process.Start(new ProcessStartInfo(helper, ApplyArgument + " " + Path.GetFileName(package.DirectoryPath))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = package.DirectoryPath
            }))
            {
                if (process == null) throw new InvalidOperationException("更新プロセスを起動できませんでした。");
            }
        }

        public static void Run(string id)
        {
            string stage = StageFor(id);
            if (stage == null) return;
            UpdatePlan plan = null;
            bool parentExited = false;
            try
            {
                if (!string.Equals(Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath), Path.Combine(stage, "UpdateHost.exe"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新プロセスの場所が無効です。");
                using (var stream = File.OpenRead(Path.Combine(stage, "plan.json")))
                    plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(stream);
                if (plan == null || !Path.IsPathRooted(plan.TargetPath) ||
                    !string.Equals(Path.GetFileName(plan.TargetPath), InstalledFiles[0], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetDirectoryName(plan.TargetPath), stage, StringComparison.OrdinalIgnoreCase) ||
                    plan.ParentId <= 0 || !AutoUpdater.IsCommit(plan.Commit))
                    throw new InvalidDataException("更新計画が無効です。");
                WaitForParent(plan);
                parentExited = true;
                AutoUpdater.VerifyPayload(stage, plan.Commit);
                ApplyFiles(stage, plan.TargetPath, () => StartAndConfirm(plan.TargetPath, id, stage));
                TryDelete(FailedCommitPath);
                WriteLog("更新完了: " + plan.Commit);
                CleanDirectory(stage); // The running helper itself is removed on a later cleanup.
            }
            catch (Exception ex)
            {
                WriteLog("更新失敗 (" + ex.GetType().Name + "): " + ex.Message);
                if (parentExited && plan != null)
                {
                    try { File.WriteAllText(FailedCommitPath, plan.Commit); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    try { using (Process previous = StartApplication(plan.TargetPath, FailedArgument + " " + id)) { } }
                    catch (Exception restartError) { WriteLog("旧版の再起動失敗: " + restartError.GetType().Name); }
                }
            }
        }

        internal static void WaitForParent(UpdatePlan plan)
        {
            Process parent;
            try { parent = Process.GetProcessById(plan.ParentId); }
            catch (ArgumentException) { return; }
            using (parent)
            {
                if (parent.HasExited) return;
                try
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartedUtcTicks ||
                        !string.Equals(parent.MainModule.FileName, plan.TargetPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("更新前のアプリを確認できませんでした。");
                    if (!parent.WaitForExit(60000)) throw new TimeoutException("更新前のアプリの終了を待機しましたが、終了しませんでした。");
                }
                catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                catch (System.ComponentModel.Win32Exception) { if (!parent.HasExited) throw; }
            }
        }

        internal static bool StartAndConfirm(string target, string id, string stage)
        {
            string ready = Path.Combine(stage, "ready");
            if (File.Exists(ready)) File.Delete(ready);
            using (Process process = StartApplication(target, UpdatedArgument + " " + id))
            {
                bool confirmed = false;
                try
                {
                    for (int i = 0; i < 150; i++)
                    {
                        if (process.HasExited) throw new InvalidOperationException("更新版が起動直後に終了しました。");
                        if (File.Exists(ready) && File.ReadAllText(ready) == process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        {
                            confirmed = true;
                            return true;
                        }
                        Thread.Sleep(100);
                    }
                    throw new TimeoutException("更新版の起動を確認できませんでした。");
                }
                finally
                {
                    if (!confirmed && !process.HasExited)
                    {
                        process.Kill();
                        if (!process.WaitForExit(10000)) throw new TimeoutException("更新版を停止できませんでした。");
                    }
                }
            }
        }

        private static Process StartApplication(string target, string arguments)
        {
            Process process = Process.Start(new ProcessStartInfo(target, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(target)
            });
            if (process == null) throw new InvalidOperationException("アプリを起動できませんでした。");
            return process;
        }

        // Keep both backups until the new app has entered its message loop.
        internal static void ApplyFiles(string stage, string targetExecutable, Func<bool> startAndConfirm)
        {
            string targetDirectory = Path.GetDirectoryName(targetExecutable);
            string suffix = ".update-" + Guid.NewGuid().ToString("N");
            var changes = new List<FileChange>();
            try
            {
                foreach (string name in InstalledFiles)
                {
                    string target = Path.Combine(targetDirectory, name);
                    var change = new FileChange { Target = target, Incoming = target + suffix, Backup = target + suffix + ".bak", Existed = File.Exists(target) };
                    changes.Add(change);
                    File.Copy(Path.Combine(stage, name), change.Incoming, false);
                    RetryFileOperation(() =>
                    {
                        if (change.Existed) File.Replace(change.Incoming, change.Target, change.Backup);
                        else File.Move(change.Incoming, change.Target);
                    });
                    change.Applied = true;
                }
                if (!startAndConfirm()) throw new InvalidOperationException("更新版の起動を確認できませんでした。");
            }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    FileChange change = changes[i];
                    if (!change.Applied) continue;
                    try
                    {
                        RetryFileOperation(() =>
                        {
                            if (change.Existed) File.Replace(change.Backup, change.Target, null);
                            else File.Delete(change.Target);
                        });
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                }
                if (errors.Count > 1) throw new AggregateException("旧版への復元に失敗しました。インストール先の .bak ファイルを保管してください。", errors);
                throw;
            }
            finally
            {
                foreach (FileChange change in changes) TryDelete(change.Incoming);
            }
            foreach (FileChange change in changes) TryDelete(change.Backup);
        }

        private static void RetryFileOperation(Action action)
        {
            for (int i = 0; ; i++)
            {
                try { action(); return; }
                catch (IOException) { if (i >= 9) throw; Thread.Sleep(200); }
            }
        }

        public static void ConfirmStartup(string id)
        {
            string stage = StageFor(id);
            if (stage == null || !Directory.Exists(stage)) return;
            try
            {
                using (Process process = Process.GetCurrentProcess())
                    File.WriteAllText(Path.Combine(stage, "ready"), process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string StageFor(string id)
        {
            Guid value;
            return Guid.TryParseExact(id, "N", out value) ? Path.Combine(StagingRoot, value.ToString("N")) : null;
        }

        internal static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(StagingRoot);
                File.WriteAllText(LogPath, DateTime.Now.ToString("s") + " " + message);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void CleanOldDirectories()
        {
            try
            {
                if (!Directory.Exists(StagingRoot)) return;
                foreach (string directory in Directory.GetDirectories(StagingRoot))
                {
                    Guid id;
                    if (Guid.TryParseExact(Path.GetFileName(directory), "N", out id) && Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                        CleanDirectory(directory);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void CleanDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                foreach (string name in PayloadFiles) TryDelete(Path.Combine(directory, name));
                foreach (string name in new[] { "UpdateHost.exe", "UpdateHost.exe.config", "plan.json", "ready" }) TryDelete(Path.Combine(directory, name));
                Directory.Delete(directory, false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class FileChange
        {
            public string Target, Incoming, Backup;
            public bool Existed, Applied;
        }
    }

    [DataContract] internal sealed class UpdatePlan
    {
        [DataMember] public string TargetPath;
        [DataMember] public int ParentId;
        [DataMember] public long ParentStartedUtcTicks;
        [DataMember] public string Commit;
    }
}
