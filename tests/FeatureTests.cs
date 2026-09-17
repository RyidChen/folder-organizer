using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DownloadOrganizer;

internal static class FeatureTests
{
    private static int Main()
    {
        try
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "feature-fixtures-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "bill.pdf"), "invoice");
            var engine = new Engine(Path.Combine(root, "state"));
            var plan = engine.Scan(source, source);
            plan[0].Category = "我的帳單";
            Engine.SetTargets(plan, source);
            Check(plan[0].Target == Path.Combine(source, "我的帳單", "bill.pdf"),
                "custom category produces a child folder");
            Check(!Directory.Exists(Path.Combine(source, "我的帳單")),
                "custom category preview creates no directory");
            Check(engine.Execute(plan).Count == 0 && File.Exists(plan[0].Target),
                "custom category move works");
            Check(engine.Undo().Count == 0 && File.Exists(Path.Combine(source, "bill.pdf")),
                "custom category undo works");
            CheckInvalidCategoryNames(plan, source);
            CheckSettings(root);
            CheckScanFailuresAndProgress(root);
            CheckCancellation(root);
            Console.WriteLine("ALL FEATURE CHECKS PASSED");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error.Message);
            return 1;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }

    private static void CheckInvalidCategoryNames(List<Entry> plan, string source)
    {
        string[] invalidNames = { "..", @"..\escape", @"C:\outside", "folder/name", "帳單.",
            "帳單 ", "CON", "con.txt", "LPT1", "COM¹", ".organizer-state", "bad:name", "" };
        foreach (string name in invalidNames)
        {
            bool rejected = false;
            try
            {
                plan[0].Category = name;
                Engine.SetTargets(plan, source);
            }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "reject invalid category: " + name);
        }
    }

    private static void CheckSettings(string root)
    {
        string state = Path.Combine(root, "settings-state");
        var store = new SettingsStore(state);
        string warning;
        Check(store.Load(out warning).FollowSource && warning == "", "first run follows source");
        var settings = new AppSettings
        {
            SourceDirectory = Path.Combine(root, "source"),
            DestinationDirectory = Path.Combine(root, "destination"),
            FollowSource = false,
            CustomCategories = new List<string> { "我的帳單", "工作資料" }
        };
        store.Save(settings);
        AppSettings restored = new SettingsStore(state).Load(out warning);
        Check(warning == "" && !restored.FollowSource
            && restored.SourceDirectory == settings.SourceDirectory
            && restored.DestinationDirectory == settings.DestinationDirectory
            && restored.CustomCategories.SequenceEqual(settings.CustomCategories),
            "settings and custom folders survive restart");
        restored.FollowSource = true;
        restored.CustomCategories.Remove("我的帳單");
        store.Save(restored);
        restored = store.Load(out warning);
        Check(restored.FollowSource && restored.CustomCategories.SequenceEqual(new[] { "工作資料" }),
            "settings replacement remembers follow-source and removed option");
        restored.CustomCategories.Add("文件");
        bool duplicateRejected = false;
        try { store.Save(restored); }
        catch (InvalidDataException) { duplicateRejected = true; }
        Check(duplicateRejected && store.Load(out warning).CustomCategories.Count == 1,
            "duplicate category rejected without overwriting saved settings");
        File.WriteAllText(Path.Combine(state, "settings.xml"), "not XML");
        restored = store.Load(out warning);
        Check(warning.Length > 0 && restored.FollowSource && restored.CustomCategories.Count == 0,
            "corrupt settings fall back with warning");
    }

    private static void CheckScanFailuresAndProgress(string root)
    {
        string source = Path.Combine(root, "locked-files");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "same contents");
        File.WriteAllText(Path.Combine(source, "z.txt"), "same contents");
        File.WriteAllText(Path.Combine(source, "ignored.tmp"), "unfinished");
        string lockedPath = Path.Combine(source, "locked.txt");
        File.WriteAllText(lockedPath, "locked");
        var reports = new List<ScanProgress>();
        var progress = new InlineProgress(value => reports.Add(value));
        var engine = new Engine(Path.Combine(root, "scan-state"));
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ScanResult result = engine.ScanDetailed(source, source, progress, CancellationToken.None);
            Check(result.Entries.Count == 2 && result.Errors.Count == 1
                && result.Errors[0].Contains("locked.txt") && result.IgnoredCount == 1,
                "locked file skipped while other files finish");
            Check(result.Entries.All(entry => entry.Duplicate.Length > 0),
                "streamed hashes preserve duplicate detection");
        }

        Check(reports.Any(p => p.CurrentFile == "a.txt")
            && reports.Last().CompletedFiles == 4 && reports.Last().TotalFiles == 4,
            "progress reports file names and final count");
        Check(Directory.GetFiles(source).Length == 4 && Directory.GetDirectories(source).Length == 0,
            "scan failures and progress never move files");
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        reports.Clear();
        Check(engine.ScanDetailed(empty, empty, progress, CancellationToken.None).Entries.Count == 0
            && reports.Last().TotalFiles == 0, "empty folder scan completes");
    }

    private static void CheckCancellation(string root)
    {
        string source = Path.Combine(root, "cancel-files");
        Directory.CreateDirectory(source);
        string largeFile = Path.Combine(source, "large.bin");
        using (var file = File.Create(largeFile)) file.SetLength(8 * 1024 * 1024);
        var engine = new Engine(Path.Combine(root, "cancel-state"));
        using (var cancellation = new CancellationTokenSource())
        {
            bool sawPartialFileProgress = false;
            var progress = new InlineProgress(value =>
            {
                if (value.CurrentFilePercent > 0 && value.CurrentFilePercent < 100)
                {
                    sawPartialFileProgress = true;
                    cancellation.Cancel();
                }
            });
            bool cancelled = false;
            try { engine.ScanDetailed(source, source, progress, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && sawPartialFileProgress && File.Exists(largeFile)
                && Directory.GetDirectories(source).Length == 0,
                "cancel during file hashing leaves original untouched");
        }
    }

    // 測試需要同步接收回報，才能精確在讀檔途中取消；UI 則使用 Progress<T>。
    private class InlineProgress : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> callback;
        public InlineProgress(Action<ScanProgress> callback) { this.callback = callback; }
        public void Report(ScanProgress value) { callback(value); }
    }
}
