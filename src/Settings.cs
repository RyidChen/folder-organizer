using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace DownloadOrganizer
{
    /// <summary>只儲存偏好設定；復原紀錄仍獨立保存在 latest.xml。</summary>
    public class AppSettings
    {
        public string SourceDirectory = "";
        public string DestinationDirectory = "";
        public bool FollowSource = true;
        public List<string> CustomCategories = new List<string>();
    }

    /// <summary>分類名稱同時是目的地底下的資料夾名稱，因此必須限制為單層名稱。</summary>
    public static class CategoryNames
    {
        public static void Validate(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name != name.Trim()
                || name.Length > 100 || name == "." || name == ".."
                || name.EndsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.Equals(".organizer-state", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("請輸入 1–100 字的單層資料夾名稱，不可含路徑符號、結尾句點或前後空白。");
            }

            // Windows 裝置保留名稱即使加上副檔名，也不能用作一般資料夾。
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };
            bool isPortName = stem.Length == 4
                && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                && "123456789¹²³".Contains(stem[3]);
            if (reserved.Contains(stem) || isPortName)
            {
                throw new ArgumentException("這是 Windows 保留名稱，請使用其他資料夾名稱。");
            }
        }

        public static List<string> Combine(IEnumerable<string> customCategories)
        {
            return Engine.Categories.Concat(customCategories)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public class SettingsStore
    {
        private readonly string settingsPath;
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));

        public SettingsStore(string stateDirectory)
        {
            settingsPath = Path.Combine(stateDirectory, "settings.xml");
        }

        public AppSettings Load(out string warning)
        {
            warning = "";
            if (!File.Exists(settingsPath)) return new AppSettings();

            try
            {
                AppSettings settings;
                var readerSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var reader = XmlReader.Create(settingsPath, readerSettings))
                {
                    settings = (AppSettings)serializer.Deserialize(reader);
                }

                ValidateSettings(settings);
                return settings;
            }
            catch (Exception exception)
            {
                // 損毀或無法讀取設定不應阻止開啟程式；把原因交給介面顯示。
                warning = "讀不到上次儲存的設定，已改用預設設定。\n" + exception.Message;
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            ValidateSettings(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            string temporaryPath = settingsPath + ".new";
            using (var stream = new FileStream(temporaryPath, FileMode.Create,
                FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(stream, settings);
                stream.Flush(true);
            }

            if (File.Exists(settingsPath)) File.Replace(temporaryPath, settingsPath, null);
            else File.Move(temporaryPath, settingsPath);
        }

        private static void ValidateSettings(AppSettings settings)
        {
            if (settings == null) throw new InvalidDataException("設定內容為空。");
            if (settings.CustomCategories == null) settings.CustomCategories = new List<string>();

            var names = new HashSet<string>(Engine.Categories, StringComparer.OrdinalIgnoreCase);
            foreach (string name in settings.CustomCategories)
            {
                CategoryNames.Validate(name);
                if (!names.Add(name)) throw new InvalidDataException("分類名稱重複：" + name);
            }
        }
    }
}
