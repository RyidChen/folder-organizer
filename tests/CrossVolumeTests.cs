using System;
using System.IO;
using DownloadOrganizer;

/// <summary>
/// 跨磁碟測試。呼叫時傳入兩個不同磁碟的測試根目錄，例如 C: 與 D:。
/// 程式會在兩邊建立獨立的 cross-volume-* 子資料夾。
/// </summary>
internal static class CrossVolumeTests
{
    private static int Main(string[] args)
    {
        try
        {
            string sourceDirectory = Path.Combine(args[0],
                "cross-volume-" + Guid.NewGuid().ToString("N"));
            string destinationDirectory = Path.Combine(args[1],
                "cross-volume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(destinationDirectory);

            string sourceFile = Path.Combine(sourceDirectory, "跨磁碟.txt");
            string destinationFile = Path.Combine(destinationDirectory, "文件", "跨磁碟.txt");
            File.WriteAllText(sourceFile, "cross-volume content");

            var engine = new Engine(Path.Combine(destinationDirectory, "state"));
            var plan = engine.Scan(sourceDirectory, destinationDirectory);
            var errors = engine.Execute(plan);
            if (errors.Count > 0)
            {
                throw new Exception(String.Join("\n", errors));
            }

            if (File.Exists(sourceFile)
                || File.ReadAllText(destinationFile) != "cross-volume content")
            {
                throw new Exception("Cross-volume move failed");
            }

            errors = engine.Undo();
            if (errors.Count > 0 || File.ReadAllText(sourceFile) != "cross-volume content")
            {
                throw new Exception("Cross-volume undo failed: " + String.Join("\n", errors));
            }

            Console.WriteLine("PASS cross-volume move and undo");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return 1;
        }
    }
}
