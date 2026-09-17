using System;
using System.IO;
using System.Linq;
using DownloadOrganizer;

/// <summary>
/// 不依賴測試框架的整合測試：建立真實測試檔，再檢查整理與復原的結果。
/// 每次執行都建立新的 fixtures-* 資料夾，不會操作使用者的下載資料夾。
/// </summary>
internal static class EngineTests
{
    private static void Main()
    {
        try
        {
            RunScenario();
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + exception.Message);
            Environment.ExitCode = 1;
        }
    }

    // 簡單的 assertion（斷言）：條件不成立就拋出例外，讓測試失敗。
    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new Exception(description);
        }

        Console.WriteLine("PASS " + description);
    }

    private static void RunScenario()
    {
        string fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "fixtures-" + Guid.NewGuid().ToString("N"));
        string sourceDirectory = Path.Combine(fixtureRoot, "input");
        string destinationDirectory = Path.Combine(fixtureRoot, "output");
        string stateDirectory = Path.Combine(fixtureRoot, "state");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        string originalBill = Path.Combine(sourceDirectory, "bill.pdf");
        string originalCopy = Path.Combine(sourceDirectory, "copy.pdf");

        // Arrange（準備）：副檔名是 pdf 即可測試分類，不必是真正的 PDF 格式。
        File.WriteAllText(originalBill, "invoice");
        File.WriteAllText(originalCopy, "invoice");
        File.WriteAllText(Path.Combine(sourceDirectory, "partial.crdownload"), "partial");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        File.WriteAllText(Path.Combine(sourceDirectory, "nested", "keep.txt"), "keep");

        // Act（執行）與 Assert（檢查）：掃描只產生預覽，不改動檔案。
        var engine = new Engine(stateDirectory);
        var plan = engine.Scan(sourceDirectory, destinationDirectory);
        Check(plan.Count == 2 && File.Exists(originalBill),
            "preview leaves files and excludes nested/incomplete");
        Check(plan.All(entry => entry.Duplicate.Length > 0),
            "content duplicates detected");

        // 目的地已經有 bill.pdf 時，預覽應改用 bill (1).pdf。
        string documentDirectory = Path.Combine(destinationDirectory, "文件");
        string existingBill = Path.Combine(documentDirectory, "bill.pdf");
        string renamedBill = Path.Combine(documentDirectory, "bill (1).pdf");
        Directory.CreateDirectory(documentDirectory);
        File.WriteAllText(existingBill, "existing");

        plan = engine.Scan(sourceDirectory, destinationDirectory);
        Entry billEntry = plan.Single(entry => Path.GetFileName(entry.Source) == "bill.pdf");
        Check(billEntry.Target.EndsWith("bill (1).pdf"),
            "preview reserves collision name");

        var errors = engine.Execute(plan);
        foreach (string error in errors)
        {
            Console.WriteLine(error);
        }

        Check(errors.Count == 0 && !File.Exists(originalBill), "custom destination moves");
        Check(File.ReadAllText(existingBill) == "existing"
            && File.ReadAllText(renamedBill) == "invoice", "collision preserves both");

        // 建立新的 Engine 模擬重開程式，確認復原資訊來自磁碟而非記憶體。
        engine = new Engine(stateDirectory);
        Check(engine.HasUndo, "undo survives restart");

        // 模擬原位置又出現新檔案，復原不能覆蓋它。
        File.WriteAllText(originalBill, "new source");
        errors = engine.Undo();
        Check(errors.Count == 1 && File.Exists(renamedBill),
            "undo collision preserves files");
        Check(File.Exists(originalCopy), "undo restores conflict-free items");

        // 刪除的只是上方測試刻意建立的衝突檔，再試一次復原。
        File.Delete(originalBill);
        Check(engine.Undo().Count == 0 && File.ReadAllText(originalBill) == "invoice",
            "retry undo resolves conflict");

        // 模擬掃描完後才被修改：這個項目應略過，其他項目仍可完成。
        plan = engine.Scan(sourceDirectory, sourceDirectory);
        File.AppendAllText(originalBill, "changed");
        errors = engine.Execute(plan);
        string organizedCopy = Path.Combine(sourceDirectory, "文件", "copy.pdf");
        Check(errors.Count == 1 && File.Exists(originalBill) && File.Exists(organizedCopy),
            "stale source rejected and same-folder classification works");

        // 整理後內容已更動時，復原也不應擅自移回。
        File.WriteAllText(organizedCopy, "edited after move");
        Check(engine.Undo().Count == 1 && !File.Exists(originalCopy),
            "undo refuses changed destination");

        Console.WriteLine("ALL CHECKS PASSED; fixtures: " + fixtureRoot);
    }
}
