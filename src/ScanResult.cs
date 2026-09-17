using System.Collections.Generic;

namespace DownloadOrganizer
{
    /// <summary>將成功項目與失敗原因分開，單一檔案失敗不會丟掉整份預覽。</summary>
    public class ScanResult
    {
        public List<Entry> Entries = new List<Entry>();
        public List<string> Errors = new List<string>();
        public int IgnoredCount;
    }

    /// <summary>背景工作傳給 UI 的進度快照，不包含任何視窗控制項。</summary>
    public class ScanProgress
    {
        public int CompletedFiles;
        public int TotalFiles;
        public string CurrentFile;
        public int CurrentFilePercent;
    }
}
