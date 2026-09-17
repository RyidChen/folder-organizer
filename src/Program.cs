using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    /// <summary>程式進入點：從 Main 開始執行，再開啟主視窗。</summary>
    internal static class Program
    {
        // WinForms 的檔案選擇視窗等功能需要單執行緒 Apartment（STA）。
        [STAThread]
        private static void Main(string[] args)
        {
            bool isFirstInstance;

            // Mutex 是作業系統提供的同步物件。使用相同名稱，可避免同一工作階段
            // 同時開啟多個整理助手，互相影響檔案與復原紀錄。
            // out 表示建構子會把額外結果寫入 isFirstInstance。
            using (var instanceMutex = new Mutex(true, "Local\\FolderOrganizerDesktop", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("下載整理助手已開啟，請使用現有視窗。");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 開發用入口：使用指定的測試資料產生畫面截圖。
                // 一般使用者雙擊程式時沒有參數，不會進入這個分支。
                if (args.Length > 0 && args[0] == "--smoke")
                {
                    RunUiSmokeTest(args);
                    return;
                }

                // 啟動視窗訊息迴圈，等待使用者點擊、輸入或關閉視窗。
                Application.Run(new MainWindow());
            }
        }

        private static void RunUiSmokeTest(string[] args)
        {
            string testStateDirectory = Path.Combine(Path.GetDirectoryName(args[1]),
                "ui-test-state-" + Guid.NewGuid().ToString("N"));
            using (var window = new MainWindow(testStateDirectory))
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-30000, -30000);

                // += 註冊事件處理函式：視窗顯示後才開始檢查。
                window.Shown += async (sender, eventArgs) =>
                {
                    try
                    {
                        await window.RunSmokeTestAsync(args[1], args[2], testStateDirectory);
                    }
                    catch (Exception exception)
                    {
                        File.WriteAllText(args[1] + ".error.txt", exception.ToString());
                        Environment.ExitCode = 1;
                    }
                    finally
                    {
                        window.Close();
                    }
                };

                Application.Run(window);
            }
        }
    }
}
