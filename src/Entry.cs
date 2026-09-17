namespace DownloadOrganizer
{
    /// <summary>
    /// 代表一個待整理的檔案，也用來儲存復原所需的資訊。
    /// 這個類別只放資料；真正的檔案操作由 Engine 負責。
    /// </summary>
    public class Entry
    {
        // public 欄位可以從其他類別存取，例如 entry.Source。
        // 這些欄位的名稱也是 XML 紀錄中的名稱，保留它們才能讀取舊紀錄。
        public string Source;       // 整理前的完整路徑。
        public string Target;       // 預計移動到的完整路徑。
        public string Hash;         // 以 SHA-256 計算的內容指紋，用來比對檔案。
        public string Duplicate;    // 顯示用的重複提示；沒有重複時為空字串。
        public string Category;     // 文件、圖片、影音等分類。

        public long Size;           // 檔案大小，單位為 byte（位元組）。
        public long Modified;       // 掃描時的 UTC 修改時間（Ticks），保留作為紀錄。
                                    // 目前實際判斷內容是否變更使用 Hash，而不是這個時間。
    }
}
