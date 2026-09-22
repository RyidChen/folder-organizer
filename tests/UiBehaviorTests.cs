using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using DownloadOrganizer;

// UI 整合檢查：只使用獨立的測試資料與設定，不操作下載資料夾。
internal static class UiBehaviorTests
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "ui-polish-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using (var window = new MainWindow(Path.Combine(root, "state")))
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(-30000, -30000);
            window.Shown += async (sender, eventArgs) =>
            {
                try
                {
                    await Run(window, root);
                    Console.WriteLine("ALL UI BEHAVIOR CHECKS PASSED");
                }
                catch (Exception exception)
                {
                    Console.WriteLine("FAIL " + exception);
                    Environment.ExitCode = 1;
                }
                finally { window.Close(); }
            };
            Application.Run(window);
        }
    }

    private static T Field<T>(MainWindow window, string name)
    {
        var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new Exception("Missing UI state: " + name);
        return (T)field.GetValue(window);
    }

    private static object Call(MainWindow window, string name, params object[] arguments)
    {
        return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(window, arguments);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }

    private static async Task Run(MainWindow window, string root)
    {
        SaveScreenshot(window, Path.Combine(root, "initial.png"));
        var formatSize = typeof(MainWindow).GetMethod("FormatFileSize",
            BindingFlags.Static | BindingFlags.NonPublic);
        // 實際大小不能因為格式化而顯示成 0 KB。
        Check((string)formatSize.Invoke(null, new object[] { 20L }) == "20 B",
            "small files show their actual byte size");
        Check((string)formatSize.Invoke(null, new object[] { 1073741824L }) == "1.0 GB",
            "large files use readable GB units");
        var grid = Field<DataGridView>(window, "previewGrid");
        var status = Field<Label>(window, "statusLabel");
        var plan = Field<List<Entry>>(window, "currentPlan");
        // 模擬大型預覽清單；全選只應在整批結束時更新一次統計。
        for (int index = 0; index < 1000; index++)
        {
            var entry = new Entry { Source = Path.Combine(root, index + ".txt"),
                Category = "文件", Target = Path.Combine(root, "文件", index + ".txt") };
            plan.Add(entry);
            int rowIndex = grid.Rows.Add(true, index + ".txt", "1 KB", "文件", "", entry.Target);
            grid.Rows[rowIndex].Tag = entry;
        }
        int statusUpdates = 0;
        EventHandler countUpdates = (sender, eventArgs) => statusUpdates++;
        status.TextChanged += countUpdates;
        Call(window, "SetAllSelections", false);
        status.TextChanged -= countUpdates;
        Check(statusUpdates <= 1, "bulk deselection updates summary only once (actual " + statusUpdates + ")");
        Check(grid.Rows.Cast<DataGridViewRow>().All(row => !Convert.ToBoolean(row.Cells["pick"].Value))
            && !Field<Button>(window, "organizeButton").Enabled, "deselection disables organize");
        Call(window, "SetAllSelections", true);
        Check(Field<Button>(window, "organizeButton").Enabled, "select all enables organize");

        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        Field<TextBox>(window, "sourceTextBox").Text = empty;
        Field<TextBox>(window, "destinationTextBox").Text = empty;
        await (Task)Call(window, "ScanAsync");
        Check(Field<Label>(window, "emptyPreviewLabel").Visible && !grid.Visible && grid.Rows.Count == 0
            && !Field<Button>(window, "organizeButton").Enabled, "empty scan shows guidance and disables organize");
        SaveScreenshot(window, Path.Combine(root, "empty.png"));

        File.WriteAllText(Path.Combine(empty, "readme.txt"), "sample");
        await (Task)Call(window, "ScanAsync");
        Check(!Field<Label>(window, "emptyPreviewLabel").Visible && grid.Visible && grid.Rows.Count == 1,
            "files replace empty guidance after a scan");
        Check(!status.Text.Contains("失敗 0") && !status.Text.Contains("略過 0"),
            "successful scan omits zero-error clutter");

        // 使用現有工作委派入口回傳多筆失敗，驗證介面能保留所有原因供稍後查看。
        Func<List<string>> failureOperation = () => Enumerable.Range(1, 20)
            .Select(index => "file-" + index + ".txt: test failure").ToList();
        await (Task)Call(window, "RunFileOperationAsync", failureOperation, "整理中", "整理完成");
        Check(Field<List<string>>(window, "issueDetails").Count == 20
            && Field<Button>(window, "detailsButton").Enabled, "all operation errors remain available after preview clears");
        Check(!status.Text.StartsWith("整理完成"), "partial failure does not claim complete success");
        SaveScreenshot(window, Path.Combine(root, "errors.png"));
        Console.WriteLine("UI screenshots: " + root);
    }

    private static void SaveScreenshot(Form window, string path)
    {
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(path);
        }
    }
}
