using System.Diagnostics;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>跑一个方案：检查、提取到暂存目录、打包到待上传目录、放进上传队列。</summary>
public sealed class BackupRunner(HistoryStore history, UploadQueue queue)
{
    public static string? Validate(BackupPlan plan, AppConfig config, BrowserInstall? install = null)
    {
        var password = SecretProtector.Unprotect(plan.PasswordProtected);
        if (plan.NeedsArchivePassword())
        {
            if (string.IsNullOrEmpty(password)) return $"“{plan.Name}”还没设置解压密码";
            if (password.Length < Packer.MinPasswordLength) return $"“{plan.Name}”{Packer.PasswordLengthRule}";
            if (plan.Formats.HasFlag(ArchiveFormats.Zip) && !Packer.IsZipSafePassword(password)) return Packer.ZipPasswordRule;
        }
        if (plan.Formats == ArchiveFormats.None) return $"“{plan.Name}”至少要选一种打包格式";
        if (!plan.AllProfiles && plan.ProfileDirs.Count == 0) return $"“{plan.Name}”没有选用户";
        var selected = plan.TargetIds
            .Select(id => config.Targets.FirstOrDefault(t => t.Id == id))
            .OfType<UploadTarget>()
            .ToList();
        if (selected.Count == 0) return $"“{plan.Name}”没有选备份到哪里";
        var active = selected.Where(t => !t.Disabled).ToList();
        if (active.Count == 0) return $"“{plan.Name}”的目的地都停用了";
        foreach (var target in active)
        {
            try
            {
                UploaderFactory.Check(target);
            }
            catch (ArgumentException ex)
            {
                return $"“{target.Name}”{ex.Message}";
            }
        }
        if (NameTemplate.Validate(plan.NameTemplate) is { } template) return template;
        if (install is null) return null;
        var rendered = NameTemplate.Render(plan.NameTemplate, new NameValues(
            plan.NamePrefix, plan.Name, plan.Slug, DateTime.Now, plan.IncludeProgram, install.Version, Environment.MachineName),
            plan.AsciiFileName);
        return NameTemplate.LengthError(rendered);
    }

    /// <summary>算当前指纹并和上次成功备份时比较。</summary>
    public bool HasChanged(BrowserInstall install, BackupPlan plan, out string fingerprint)
    {
        var profiles = CollectPlanner.SelectedProfiles(install, plan);
        fingerprint = Fingerprint.Compute(install, plan, CollectPlanner.Plan(install, plan, profiles));
        var current = fingerprint;
        return history.Read(h => h.For(plan.Id).Fingerprint) != current;
    }

    public async Task<BackupResult> RunAsync(
        BrowserInstall install,
        BackupPlan plan,
        AppConfig config,
        IProgress<BackupStatus>? progress,
        CancellationToken ct,
        CancellationToken browserStarted = default,
        bool lowPriority = false,
        bool fromAuto = false,
        Action<string>? onBatch = null)
    {
        if (Validate(plan, config, install) is { } problem) throw new BackupException(problem);
        if (BrowserProcess.IsRunning(install)) throw new BackupException("Cent Browser 还开着，关掉后再备份", browserRunning: true);

        var watch = Stopwatch.StartNew();
        var startedUtc = DateTime.UtcNow;
        var profiles = CollectPlanner.SelectedProfiles(install, plan);
        if (profiles.Count == 0) throw new BackupException($"“{plan.Name}”选中的用户在浏览器里都找不到了");
        var items = CollectPlanner.Plan(install, plan, profiles);
        var fingerprint = Fingerprint.Compute(install, plan, items);

        var staging = Path.Combine(AppPaths.StagingDir, plan.Id);
        var batchId = Guid.NewGuid().ToString("N")[..12];
        var outputDir = Path.Combine(AppPaths.PendingDir, batchId);
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            var baseName = NameTemplate.Render(plan.NameTemplate, new NameValues(
                plan.NamePrefix, plan.Name, plan.Slug, DateTime.Now, plan.IncludeProgram, install.Version, Environment.MachineName),
                plan.AsciiFileName);

            // 包里套一层和包同名的文件夹，解压出来就是这个文件夹。
            // 负数表示还在统计大小，界面先不写百分比
            progress?.Report(new BackupStatus(plan.Name, BackupStage.Collecting, -1));
            var collectProgress = progress is null ? null
                : new Progress<int>(p => progress.Report(new BackupStatus(plan.Name, BackupStage.Collecting, p)));
            var collected = await CollectAsync(install, plan, profiles, items, Path.Combine(staging, baseName), ct, browserStarted, collectProgress);
            var packProgress = new Progress<int>(p => progress?.Report(new BackupStatus(plan.Name, BackupStage.Packing, p)));
            var archivePassword = plan.NeedsArchivePassword() ? SecretProtector.Unprotect(plan.PasswordProtected) : null;
            var files = await Packer.PackAsync(staging, outputDir, baseName, plan.Formats, plan.ZipEncryption,
                archivePassword, packProgress, ct, lowPriority);

            var targetIds = plan.TargetIds.Where(id => config.Targets.Any(t => t.Id == id && !t.Disabled)).ToList();
            if (targetIds.Count == 0) throw new BackupException($"“{plan.Name}”的目的地都停用了");
            var jobs = files.Select(f => new UploadJob
            {
                BatchId = batchId,
                PlanId = plan.Id,
                PlanName = plan.Name,
                LocalPath = f,
                FileName = Path.GetFileName(f),
                Size = new FileInfo(f).Length,
                PendingTargetIds = [.. targetIds],
                TargetIds = [.. targetIds],
                TargetNames = targetIds.Select(id => config.Targets.First(t => t.Id == id).Name).ToList(),
                Fingerprint = fingerprint,
                FromAuto = fromAuto,
                Slow = lowPriority,
                StartedUtc = startedUtc,
                NextAttemptUtc = DateTime.UtcNow,
            }).ToList();
            ct.ThrowIfCancellationRequested();
            onBatch?.Invoke(batchId);
            queue.Enqueue(jobs);

            history.Update(h => PackEstimate.Record(h.For(plan.Id).Ratios, files, collected));
            progress?.Report(new BackupStatus(plan.Name, BackupStage.Queued, 100));
            return new BackupResult(plan.Name, files, jobs.Sum(j => j.Size), watch.Elapsed);
        }
        catch (Exception) when (Directory.Exists(outputDir) && !queue.Snapshot().Any(j => j.BatchId == batchId))
        {
            TryDelete(outputDir);
            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>打包进程刚停时文件可能还被占着，多试几次。</summary>
    private static void TryDelete(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException) && i < 2)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException))
            {
                return;
            }
        }
    }

    /// <summary>提取时浏览器可能正在写文件，提取期间浏览器一打开就作废；打包只读暂存目录，不受影响。</summary>
    private static async Task<long> CollectAsync(
        BrowserInstall install,
        BackupPlan plan,
        IReadOnlyList<string> profiles,
        IReadOnlyList<CollectItem> items,
        string stagingRoot,
        CancellationToken ct,
        CancellationToken browserStarted,
        IProgress<int>? progress)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, browserStarted);
        long collected;
        try
        {
            collected = await Task.Run(() => Collector.Collect(install, plan, profiles, items, stagingRoot, linked.Token, progress), linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw BrowserOpenedWhileExtracting(plan.Name);
        }
        catch (IOException) when (BrowserProcess.IsRunning(install))
        {
            throw BrowserOpenedWhileExtracting(plan.Name);
        }
        // 监视器开着时 1 秒才看一次，提取最后一秒打开的浏览器要再查一遍
        if (BrowserProcess.IsRunning(install)) throw BrowserOpenedWhileExtracting(plan.Name);
        return collected;
    }

    private static BackupException BrowserOpenedWhileExtracting(string planName) =>
        new($"「{planName}」提取数据时打开了浏览器，这次没备份", browserRunning: true, stoppedForBrowser: true);
}
