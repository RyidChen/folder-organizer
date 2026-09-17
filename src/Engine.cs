using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Serialization;
using System.Threading;
using System.Diagnostics;
using System.Security;

namespace DownloadOrganizer
{
    /// <summary>
    /// 檔案整理的核心：Scan 建立預覽，Execute 執行移動，Undo 執行復原。
    /// 這裡沒有視窗或按鈕，因此測試程式也能直接使用這個類別。
    /// </summary>
    public class Engine
    {
        // static：所有 Engine 共用這份資料。
        // readonly：欄位初始化後不能換成另一個陣列；陣列內容本身仍可修改。
        public static readonly string[] Categories =
        {
            "文件", "圖片", "影音", "壓縮檔", "安裝程式", "其他"
        };

        private readonly string journalPath;
        private readonly XmlSerializer journalSerializer =
            new XmlSerializer(typeof(List<Entry>));

        /// <summary>建構子：new Engine(...) 時執行，設定復原紀錄的位置。</summary>
        public Engine(string stateDirectory)
        {
            journalPath = Path.Combine(Path.GetFullPath(stateDirectory), "latest.xml");
        }

        /// <summary>唯讀屬性：每次讀取時，重新檢查是否存在復原紀錄。</summary>
        public bool HasUndo
        {
            get { return File.Exists(journalPath); }
        }

        /// <summary>只讀取來源檔案並產生整理建議；不建立分類資料夾或移動檔案。</summary>
        public List<Entry> Scan(string sourceDirectory, string destinationDirectory)
        {
            // 保留舊呼叫方式；介面使用 ScanDetailed 取得進度與失敗明細。
            return ScanDetailed(sourceDirectory, destinationDirectory, null,
                CancellationToken.None).Entries;
        }

        public ScanResult ScanDetailed(string sourceDirectory, string destinationDirectory,
            IProgress<ScanProgress> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceDirectory = Path.GetFullPath(sourceDirectory);
            EnsureNoLinkedDirectories(sourceDirectory);
            var result = new ScanResult();
            var sourcePaths = Directory.GetFiles(sourceDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            int completedFiles = 0;

            foreach (string sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(sourcePath);
                ReportScanProgress(progress, completedFiles, sourcePaths.Length, fileName, 0);
                try
                {
                    var fileInfo = new FileInfo(sourcePath);
                    if (ShouldSkipFile(fileInfo))
                    {
                        result.IgnoredCount++;
                        continue;
                    }

                    using (var stream = OpenForContentCheck(sourcePath))
                    {
                        string hash = ComputeContentHash(stream, cancellationToken,
                            percent => ReportScanProgress(progress, completedFiles,
                                sourcePaths.Length, fileName, percent));
                        result.Entries.Add(new Entry
                        {
                            Source = sourcePath,
                            Category = GetCategory(sourcePath),
                            Size = stream.Length,
                            Modified = fileInfo.LastWriteTimeUtc.Ticks,
                            Hash = hash,
                            Duplicate = ""
                        });
                    }
                }
                catch (Exception exception)
                {
                    // 只略過單一檔案的存取問題；取消或程式錯誤不能當成讀檔失敗吞掉。
                    if (!(exception is IOException) && !(exception is UnauthorizedAccessException)
                        && !(exception is SecurityException)) throw;
                    result.Errors.Add(fileName + "：" + exception.Message);
                }
                finally
                {
                    completedFiles++;
                    ReportScanProgress(progress, completedFiles, sourcePaths.Length, "", 0);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            MarkDuplicateFiles(result.Entries);
            SetTargets(result.Entries, destinationDirectory);
            ReportScanProgress(progress, sourcePaths.Length, sourcePaths.Length, "", 0);
            return result;
        }

        private static void ReportScanProgress(IProgress<ScanProgress> progress,
            int completed, int total, string fileName, int filePercent)
        {
            if (progress != null)
            {
                progress.Report(new ScanProgress
                {
                    CompletedFiles = completed,
                    TotalFiles = total,
                    CurrentFile = fileName,
                    CurrentFilePercent = filePercent
                });
            }
        }

        private static string ComputeContentHash(Stream stream, CancellationToken cancellationToken,
            Action<int> reportPercent)
        {
            // 分塊讀取：大型檔案也能回報進度，並在每一塊之間回應取消。
            stream.Position = 0;
            byte[] buffer = new byte[1024 * 1024];
            var reportTimer = Stopwatch.StartNew();
            bool hasReported = false;
            using (var sha256 = SHA256.Create())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    if (!hasReported || reportTimer.ElapsedMilliseconds >= 100)
                    {
                        reportPercent((int)(100d * stream.Position / Math.Max(1, stream.Length)));
                        hasReported = true;
                        reportTimer.Restart();
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                sha256.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(sha256.Hash);
            }
        }

        /// <summary>根據分類計算目的地。同名時加上編號，但此時還不操作檔案。</summary>
        public static void SetTargets(List<Entry> entries, string destinationDirectory)
        {
            destinationDirectory = Path.GetFullPath(destinationDirectory);
            EnsureNoLinkedDirectories(destinationDirectory);

            // HashSet 適合快速查詢「是否已包含」。這裡也避開同一批預覽內的撞名。
            var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Entry entry in entries)
            {
                // 允許自訂分類，但不可用 ../ 或絕對路徑離開目的地。
                CategoryNames.Validate(entry.Category);

                string categoryDirectory = Path.Combine(destinationDirectory, entry.Category);
                EnsureNoLinkedDirectories(categoryDirectory);

                string fileName = Path.GetFileName(entry.Source);
                string targetPath = Path.Combine(categoryDirectory, fileName);
                int suffix = 1;

                while (PathExists(targetPath) || reservedPaths.Contains(targetPath))
                {
                    string numberedName = Path.GetFileNameWithoutExtension(fileName)
                        + " (" + suffix + ")" + Path.GetExtension(fileName);
                    targetPath = Path.Combine(categoryDirectory, numberedName);
                    suffix++;
                }

                entry.Target = targetPath;
                reservedPaths.Add(targetPath);
            }
        }

        /// <summary>移動使用者勾選的檔案。單一檔案失敗時記錄原因，繼續處理其他檔案。</summary>
        public List<string> Execute(List<Entry> entries)
        {
            var errors = new List<string>();
            var journalEntries = new List<Entry>();

            if (entries.Count == 0)
            {
                return errors;
            }

            // 每次整理只保留這一批的紀錄；介面會事先提醒取代上一次紀錄。
            SaveJournal(journalEntries);

            foreach (Entry entry in entries)
            {
                try
                {
                    EnsureNoLinkedDirectories(Path.GetDirectoryName(entry.Source));
                    EnsureNoLinkedDirectories(Path.GetDirectoryName(entry.Target));

                    using (var stream = OpenForContentCheck(entry.Source))
                    {
                        // 預覽與按下整理之間，檔案可能被修改，因此必須再比對一次。
                        if (stream.Length != entry.Size || ComputeContentHash(stream) != entry.Hash)
                        {
                            throw new IOException("檔案已變更，請重新掃描");
                        }

                        if (PathExists(entry.Target))
                        {
                            throw new IOException("目的地已有同名項目，請重新掃描");
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(entry.Target));

                        // 先把移動意圖寫入紀錄，再移動檔案。
                        // 若程式中斷，Undo 可根據兩邊的檔案狀態判斷如何處理。
                        journalEntries.Add(entry);
                        SaveJournal(journalEntries);
                        File.Move(entry.Source, entry.Target);
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(Path.GetFileName(entry.Source) + "：" + exception.Message);
                }
            }

            return errors;
        }

        /// <summary>復原最近一批整理；衝突項目保留在紀錄中，之後可以再次嘗試。</summary>
        public List<string> Undo()
        {
            var errors = new List<string>();
            List<Entry> remainingEntries = LoadJournal();

            // 先複製再反向走訪：稍後會從原清單移除成功項目，不能直接邊走訪邊移除。
            foreach (Entry entry in remainingEntries.ToArray().Reverse())
            {
                try
                {
                    RestoreEntry(entry);
                    remainingEntries.Remove(entry);
                    SaveJournal(remainingEntries);
                }
                catch (Exception exception)
                {
                    errors.Add(Path.GetFileName(entry.Source) + "：" + exception.Message);
                }
            }

            if (remainingEntries.Count == 0 && File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            return errors;
        }

        // 下方是核心流程使用的小方法。private 表示只有 Engine 內部可以呼叫。

        private static void RestoreEntry(Entry entry)
        {
            EnsureNoLinkedDirectories(Path.GetDirectoryName(entry.Source));
            EnsureNoLinkedDirectories(Path.GetDirectoryName(entry.Target));

            if (!PathExists(entry.Target) && File.Exists(entry.Source))
            {
                // 可能是已記錄卻尚未移動，或已復原但來不及更新紀錄。
                // 原位置內容吻合就視為完成，不必再移動一次。
                using (var originalStream = OpenForContentCheck(entry.Source))
                {
                    if (ComputeContentHash(originalStream) != entry.Hash)
                    {
                        throw new IOException("原位置內容已變更，請手動確認");
                    }
                }

                return;
            }

            if (PathExists(entry.Source))
            {
                throw new IOException("原位置已有同名項目，保留兩邊檔案");
            }

            using (var stream = OpenForContentCheck(entry.Target))
            {
                if (ComputeContentHash(stream) != entry.Hash)
                {
                    throw new IOException("整理後內容已變更，請手動確認");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.Source));
                File.Move(entry.Target, entry.Source);
            }
        }

        private static bool ShouldSkipFile(FileInfo fileInfo)
        {
            // FileAttributes 是旗標列舉：| 組合條件，& 檢查是否有其中任一旗標。
            FileAttributes excludedAttributes = FileAttributes.ReparsePoint
                | FileAttributes.Hidden | FileAttributes.System;
            string[] unfinishedExtensions = { ".crdownload", ".part", ".partial", ".tmp", ".download" };

            return (fileInfo.Attributes & excludedAttributes) != 0
                || unfinishedExtensions.Contains(fileInfo.Extension.ToLowerInvariant());
        }

        private static string GetCategory(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            // 順序對應 Categories 的前五種分類；未命中時回傳「其他」。
            string[] extensionGroups =
            {
                ".pdf .txt .doc .docx .xls .xlsx .ppt .pptx .csv .md .rtf .odt .ods .json .xml",
                ".png .jpg .jpeg .gif .webp .svg .bmp .ico .heic .tif .tiff",
                ".mp4 .mov .mkv .avi .mp3 .wav .flac .m4a .webm .aac",
                ".zip .rar .7z .tar .gz .bz2",
                ".exe .msi .msix .appx"
            };

            for (int index = 0; index < extensionGroups.Length; index++)
            {
                if (extensionGroups[index].Split(' ').Contains(extension))
                {
                    return Categories[index];
                }
            }

            return "其他";
        }

        private static void MarkDuplicateFiles(List<Entry> entries)
        {
            // LINQ：依 Hash 分組，再只留下有兩個以上檔案的群組。
            // entry => entry.Hash 是 lambda，意思是「給一個 entry，取它的 Hash」。
            var duplicateGroups = entries
                .GroupBy(entry => entry.Hash)
                .Where(group => group.Count() > 1);

            int groupNumber = 0;
            foreach (var group in duplicateGroups)
            {
                groupNumber++;
                foreach (Entry entry in group)
                {
                    entry.Duplicate = "相同內容 #" + groupNumber;
                }
            }
        }

        private static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static void EnsureNoLinkedDirectories(string path)
        {
            // 不只檢查最後一層，也檢查每一層父資料夾，避免經過連結移到意外位置。
            for (var directory = new DirectoryInfo(Path.GetFullPath(path));
                 directory != null;
                 directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("不支援連結或重新導向的資料夾：" + directory.FullName);
                }
            }
        }

        private static FileStream OpenForContentCheck(string filePath)
        {
            if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("略過連結檔案：" + filePath);
            }

            // 以唯讀方式開啟，允許其他讀取與移動／刪除，但不分享寫入權限。
            // FileShare.Delete 讓我們持有串流時仍能移動檔案；它本身不會刪除檔案。
            return new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
        }

        private static string ComputeContentHash(Stream stream)
        {
            stream.Position = 0;
            using (var sha256 = SHA256.Create())
            {
                return Convert.ToBase64String(sha256.ComputeHash(stream));
            }
        }

        private List<Entry> LoadJournal()
        {
            if (!HasUndo)
            {
                return new List<Entry>();
            }

            using (var stream = File.OpenRead(journalPath))
            {
                // Deserialize 回傳 object，因此轉型回 List<Entry>。
                return (List<Entry>)journalSerializer.Deserialize(stream);
            }
        }

        private void SaveJournal(List<Entry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath));
            string temporaryPath = journalPath + ".new";

            // 先完整寫好暫存檔，再替換正式紀錄，降低留下半份 XML 的機會。
            using (var stream = new FileStream(temporaryPath, FileMode.Create,
                FileAccess.Write, FileShare.None))
            {
                journalSerializer.Serialize(stream, entries);
                stream.Flush(true);
            }

            if (File.Exists(journalPath))
            {
                File.Replace(temporaryPath, journalPath, null);
            }
            else
            {
                File.Move(temporaryPath, journalPath);
            }
        }
    }
}
