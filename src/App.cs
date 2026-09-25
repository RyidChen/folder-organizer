using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading;

namespace DownloadOrganizer
{
    /// <summary>
    /// 主視窗只負責顯示資料、接收操作、呼叫 Engine。
    /// 「: Form」表示繼承 WinForms 的 Form，因此這個類別就是一個視窗。
    /// </summary>
    public class MainWindow : Form
    {
        private readonly Engine engine;
        private readonly SettingsStore settingsStore;
        private readonly AppSettings settings;

        // 把需要在多個方法中使用的控制項，保存在類別欄位。
        private readonly TextBox sourceTextBox = new TextBox();
        private readonly TextBox destinationTextBox = new TextBox();
        private readonly DataGridView previewGrid = new DataGridView();
        private readonly Label statusLabel = new Label();
        private readonly Label emptyPreviewLabel = new Label();
        private readonly Button scanButton = new Button();
        private readonly Button organizeButton = new Button();
        private readonly Button undoButton = new Button();
        private readonly TableLayoutPanel folderPanel = new TableLayoutPanel();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Button manageCategoriesButton = new Button();
        private readonly Button detailsButton = new Button();
        private readonly Button cancelScanButton = new Button();
        private List<string> issueDetails = new List<string>();
        private string issueTitle = "無法讀取的檔案";
        private int ignoredFileCount;
        private CancellationTokenSource scanCancellation;

        private List<Entry> currentPlan = new List<Entry>();
        private bool isBusy;                   // 檔案操作進行中，暫停介面操作。
        private bool hasCustomDestination;     // 使用者是否另外指定了目的地。
        private bool isPopulatingPreview;      // 填入表格時，不處理儲存格變更事件。
        private bool hasScanned;               // 區分尚未掃描與掃描後沒有檔案。

        public MainWindow() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".organizer-state"))
        {
        }

        // 額外的建構子讓介面測試使用獨立設定位置，不影響使用者的偏好。
        internal MainWindow(string stateDirectory)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            engine = new Engine(stateDirectory);
            settingsStore = new SettingsStore(stateDirectory);
            string settingsWarning;
            settings = settingsStore.Load(out settingsWarning);
            hasCustomDestination = !settings.FollowSource;
            // 建構子先設定視窗外觀，再組裝各區塊，最後接上操作事件。
            Text = "下載整理助手";
            Size = new Size(1120, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei UI", 10);
            BackColor = Color.FromArgb(246, 248, 251);
            ForeColor = Color.FromArgb(27, 40, 58);

            TableLayoutPanel layout = CreateMainLayout();
            Controls.Add(layout);
            layout.Controls.Add(CreateHeader(), 0, 0);
            layout.Controls.Add(CreateFolderPanel(), 0, 1);
            layout.Controls.Add(CreateSelectionToolbar(), 0, 2);

            ConfigurePreviewGrid();
            layout.Controls.Add(CreatePreviewArea(), 0, 3);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoSize = true;
            statusLabel.Padding = new Padding(0, 10, 0, 10);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "選擇資料夾後，按「掃描檔案」預覽整理建議。";
            layout.Controls.Add(statusLabel, 0, 4);
            layout.Controls.Add(CreateFooter(), 0, 5);

            organizeButton.Enabled = false;
            undoButton.Enabled = engine.HasUndo;
            ConnectEvents();
            ResumeLayout(true);
            if (!String.IsNullOrEmpty(settingsWarning))
            {
                Shown += (sender, eventArgs) => MessageBox.Show(this, settingsWarning, "設定載入提示");
            }
        }

        // ── 介面建立：先閱讀建構子，想了解某一塊畫面時再往下找。 ──

        protected override void OnLoad(EventArgs e)
        {
            FitToWorkingArea(this);
            base.OnLoad(e);
        }

        internal static void FitToWorkingArea(Form window)
        {
            Rectangle area = Screen.FromControl(window).WorkingArea;
            window.MinimumSize = new Size(Math.Min(window.MinimumSize.Width, area.Width),
                Math.Min(window.MinimumSize.Height, area.Height));
            window.Size = new Size(Math.Min(window.Width, area.Width),
                Math.Min(window.Height, area.Height));
        }

        private TableLayoutPanel CreateMainLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(26),
                ColumnCount = 1,
                RowCount = 6
            };

            // 文字區塊依內容決定高度，表格使用剩餘空間。
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        private Panel CreateHeader()
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
                Padding = new Padding(0, 0, 0, 12) };
            header.Controls.Add(new Label
            {
                Text = "整理資料夾",
                Font = new Font(Font.FontFamily, 21, FontStyle.Bold),
                AutoSize = true,
                Dock = DockStyle.Top
            });
            header.Controls.Add(new Label
            {
                Text = "先檢查分類和移動位置，確認後再整理。",
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(73, 89, 109)
            });
            return header;
        }

        private TableLayoutPanel CreateFolderPanel()
        {
            folderPanel.Dock = DockStyle.Top;
            folderPanel.AutoSize = true;
            folderPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            folderPanel.ColumnCount = 4;
            folderPanel.RowCount = 2;
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            folderPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            sourceTextBox.Text = String.IsNullOrWhiteSpace(settings.SourceDirectory)
                ? GetDownloadsDirectory() : settings.SourceDirectory;
            destinationTextBox.Text = hasCustomDestination
                && !String.IsNullOrWhiteSpace(settings.DestinationDirectory)
                ? settings.DestinationDirectory : sourceTextBox.Text;
            AddFolderPicker("來源資料夾", sourceTextBox, false);
            AddFolderPicker("整理目的地", destinationTextBox, true);
            return folderPanel;
        }

        private void AddFolderPicker(string caption, TextBox pathTextBox, bool isDestination)
        {
            int row = isDestination ? 1 : 0;
            folderPanel.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);

            pathTextBox.Dock = DockStyle.Fill;
            pathTextBox.ReadOnly = true;
            pathTextBox.Margin = new Padding(0, 7, 10, 0);
            folderPanel.Controls.Add(pathTextBox, 1, row);

            var chooseButton = new Button { Text = "選擇…", AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
            chooseButton.Click += (sender, eventArgs) =>
                ChooseFolder(caption, pathTextBox, isDestination);
            folderPanel.Controls.Add(chooseButton, 2, row);

            if (isDestination)
            {
                var resetButton = new Button { Text = "與來源相同", AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
                resetButton.Click += (sender, eventArgs) =>
                {
                    hasCustomDestination = false;
                    destinationTextBox.Text = sourceTextBox.Text;
                    ClearPreview();
                    SavePreferences();
                };
                folderPanel.Controls.Add(resetButton, 3, row);
            }

        }

        private FlowLayoutPanel CreateSelectionToolbar()
        {
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 6, 0, 10) };
            var selectAllButton = new Button { Text = "全選", AutoSize = true };
            var deselectAllButton = new Button { Text = "取消全選", AutoSize = true };

            // 簡短的事件處理可以用 lambda；較長的流程則放進有名稱的方法。
            selectAllButton.Click += (sender, eventArgs) => SetAllSelections(true);
            deselectAllButton.Click += (sender, eventArgs) => SetAllSelections(false);
            toolbar.Controls.Add(selectAllButton);
            toolbar.Controls.Add(deselectAllButton);
            manageCategoriesButton.Text = "管理自訂分類";
            manageCategoriesButton.AutoSize = true;
            manageCategoriesButton.Click += (sender, eventArgs) => ManageCategories();
            toolbar.Controls.Add(manageCategoriesButton);
            detailsButton.Text = "查看原因";
            detailsButton.AutoSize = true;
            detailsButton.Enabled = false;
            detailsButton.Click += (sender, eventArgs) => ShowIssueDetails();
            toolbar.Controls.Add(detailsButton);
            toolbar.SetFlowBreak(detailsButton, true);
            toolbar.Controls.Add(new Label
            {
                Text = "不包含子資料夾；隱藏檔、連結和下載中的檔案會略過。",
                AutoSize = true,
                Padding = new Padding(0, 7, 0, 0)
            });
            return toolbar;
        }

        private void ConfigurePreviewGrid()
        {
            previewGrid.Dock = DockStyle.Fill;
            previewGrid.BackgroundColor = Color.White;
            previewGrid.BorderStyle = BorderStyle.None;
            previewGrid.AllowUserToAddRows = false;
            previewGrid.AllowUserToDeleteRows = false;
            previewGrid.RowHeadersVisible = false;
            previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            previewGrid.MultiSelect = false;
            previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            previewGrid.EnableHeadersVisualStyles = false;
            previewGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 237, 245);
            previewGrid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            previewGrid.ColumnHeadersHeight = 40;
            previewGrid.RowTemplate.Height = 37;
            previewGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);

            // Name 是程式使用的欄位識別名稱；HeaderText 是使用者看到的文字。
            previewGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "pick", HeaderText = "整理", FillWeight = 40
            });
            previewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "file", HeaderText = "檔案名稱", ReadOnly = true, FillWeight = 130
            });
            previewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "size", HeaderText = "大小", ReadOnly = true, FillWeight = 55
            });

            var categoryColumn = new DataGridViewComboBoxColumn
            {
                Name = "category", HeaderText = "分類", FillWeight = 70
            };
            categoryColumn.Items.AddRange(CategoryNames.Combine(settings.CustomCategories).ToArray());
            previewGrid.Columns.Add(categoryColumn);
            previewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "duplicate", HeaderText = "重複提示", ReadOnly = true, FillWeight = 80
            });
            previewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "target", HeaderText = "整理後的位置", ReadOnly = true, FillWeight = 240
            });
        }

        private Panel CreatePreviewArea()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(previewGrid);
            emptyPreviewLabel.Name = "emptyPreviewLabel";
            emptyPreviewLabel.Dock = DockStyle.Fill;
            emptyPreviewLabel.BackColor = Color.White;
            emptyPreviewLabel.ForeColor = Color.FromArgb(73, 89, 109);
            emptyPreviewLabel.Font = new Font(Font.FontFamily, 12);
            emptyPreviewLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyPreviewLabel.Padding = new Padding(24);
            panel.Controls.Add(emptyPreviewLabel);
            UpdateEmptyPreview();
            return panel;
        }

        private void UpdateEmptyPreview()
        {
            bool isEmpty = previewGrid.Rows.Count == 0;
            previewGrid.Visible = !isEmpty;
            emptyPreviewLabel.Visible = isEmpty;
            if (!isEmpty) return;

            if (isBusy)
                emptyPreviewLabel.Text = "正在讀取檔案…\n清單會在掃描完成後顯示。";
            else if (issueDetails.Count > 0)
                emptyPreviewLabel.Text = "部分檔案還沒處理完\n請按「查看原因」了解詳情。";
            else if (hasScanned)
                emptyPreviewLabel.Text = "這個資料夾沒有可整理的檔案\n可以選擇其他資料夾，再掃描一次。";
            else
                emptyPreviewLabel.Text = "先看看有哪些檔案\n選擇資料夾，再按「掃描檔案」。";

            emptyPreviewLabel.BringToFront();
        }

        private FlowLayoutPanel CreateFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft
            };

            ConfigureButton(organizeButton, "開始整理", true);
            ConfigureButton(scanButton, "掃描檔案", false);
            ConfigureButton(undoButton, "復原上次整理", false);
            footer.Controls.Add(organizeButton);
            footer.Controls.Add(scanButton);
            footer.Controls.Add(undoButton);
            ConfigureButton(cancelScanButton, "取消掃描", false);
            cancelScanButton.Visible = false;
            cancelScanButton.Click += (sender, eventArgs) =>
            {
                if (scanCancellation == null) return;
                scanCancellation.Cancel();
                cancelScanButton.Enabled = false;
                statusLabel.Text = "正在取消掃描…";
            };
            footer.Controls.Add(cancelScanButton);

            progressBar.Size = new Size(120, 10);
            progressBar.Margin = new Padding(12, 17, 12, 0);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = false;
            footer.Controls.Add(progressBar);
            return footer;
        }

        private void ConfigureButton(Button button, string text, bool isPrimary)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(14, 6, 14, 6);
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(10, 0, 0, 0);
            button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
            button.BackColor = isPrimary ? Color.FromArgb(35, 91, 211) : Color.White;
            button.ForeColor = isPrimary ? Color.White : ForeColor;
        }

        // ── 事件：把「使用者做了什麼」接到對應的處理方法。 ──

        private void ConnectEvents()
        {
            previewGrid.CurrentCellDirtyStateChanged += OnCurrentCellDirtyStateChanged;
            previewGrid.CellValueChanged += OnPreviewCellValueChanged;
            FormClosing += OnWindowClosing;

            // 事件委派本身不回傳 Task，但可以在 async 事件內 await 非同步方法。
            // 一般工作方法使用 async Task，方便呼叫端等待完成與處理例外。
            scanButton.Click += async (sender, eventArgs) => await ScanAsync();
            organizeButton.Click += async (sender, eventArgs) => await OrganizeAsync();
            undoButton.Click += async (sender, eventArgs) => await UndoAsync();
        }

        private void OnCurrentCellDirtyStateChanged(object sender, EventArgs eventArgs)
        {
            // 立即提交勾選或下拉選項，避免要先點到別格才更新結果。
            if (previewGrid.IsCurrentCellDirty)
            {
                previewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnPreviewCellValueChanged(object sender, DataGridViewCellEventArgs eventArgs)
        {
            if (isPopulatingPreview || eventArgs.RowIndex < 0)
            {
                return;
            }

            if (eventArgs.ColumnIndex == previewGrid.Columns["category"].Index)
            {
                DataGridViewRow row = previewGrid.Rows[eventArgs.RowIndex];

                // Tag 是控制項提供的 object 儲存空間，這裡放對應的 Entry。
                // 取出時用 (Entry) 明確轉回原本型別。
                var entry = (Entry)row.Tag;
                entry.Category = Convert.ToString(row.Cells["category"].Value);
                RefreshTargetPaths();
            }

            UpdateSelectionSummary();
        }

        private void OnWindowClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (isBusy)
            {
                eventArgs.Cancel = true;
                string message = scanCancellation != null
                    ? "掃描還沒結束。若要關閉，請先按「取消掃描」。"
                    : "檔案還在移動中，完成後就可以關閉。";
                MessageBox.Show(this, message, "還在處理中");
            }
        }

        private void ChooseFolder(string caption, TextBox pathTextBox, bool isDestination)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = caption,
                SelectedPath = pathTextBox.Text,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                pathTextBox.Text = dialog.SelectedPath;
                if (isDestination)
                {
                    hasCustomDestination = true;
                }
                else if (!hasCustomDestination)
                {
                    destinationTextBox.Text = sourceTextBox.Text;
                }

                ClearPreview();
                SavePreferences();
            }
        }

        // ── 三個主要操作：掃描、整理、復原。 ──

        private async Task ScanAsync()
        {
            // 先在 UI 執行緒取出文字；背景工作不直接讀寫視窗控制項。
            string sourceDirectory = sourceTextBox.Text;
            string destinationDirectory = destinationTextBox.Text;
            ClearPreview();
            SetBusyState(true, "正在掃描並檢查重複檔案…");
            scanCancellation = new CancellationTokenSource();
            cancelScanButton.Visible = true;
            cancelScanButton.Enabled = true;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
            string completionMessage = null;

            // Progress<T> 在 UI 執行緒建立，Report 會把背景通知送回 UI。
            var progress = new Progress<ScanProgress>(UpdateScanProgress);

            try
            {
                // Task.Run 將耗時的同步掃描放到背景執行緒。
                // await 等待完成而不堵住視窗；之後回到 UI 執行緒填入表格。
                CancellationToken token = scanCancellation.Token;
                ScanResult result = await Task.Run(() => engine.ScanDetailed(
                    sourceDirectory, destinationDirectory, progress, token));
                currentPlan = result.Entries;
                issueDetails = result.Errors;
                ignoredFileCount = result.IgnoredCount;
                isPopulatingPreview = true;

                foreach (Entry entry in currentPlan)
                {
                    int rowIndex = previewGrid.Rows.Add(
                        true,
                        Path.GetFileName(entry.Source),
                        FormatFileSize(entry.Size),
                        entry.Category,
                        entry.Duplicate,
                        entry.Target);
                    previewGrid.Rows[rowIndex].Tag = entry;
                }
                hasScanned = true;
            }
            catch (OperationCanceledException)
            {
                currentPlan.Clear();
                previewGrid.Rows.Clear();
                completionMessage = "已取消掃描，未移動任何檔案。請重新掃描。";
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "掃描未完成");
                currentPlan.Clear();
                previewGrid.Rows.Clear();
                completionMessage = "掃描未完成，請確認資料夾位置與權限後重試。";
            }
            finally
            {
                // finally 保證離開 try/catch 時會執行，成功與失敗都要恢復按鈕狀態。
                isPopulatingPreview = false;
                scanCancellation.Dispose();
                scanCancellation = null;
                cancelScanButton.Visible = false;
                SetBusyState(false, "");
                UpdateSelectionSummary();
                if (completionMessage != null) statusLabel.Text = completionMessage;
            }
        }

        private async Task OrganizeAsync()
        {
            previewGrid.EndEdit();
            List<Entry> selectedEntries = GetSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            string confirmation = "將移動選取的 " + selectedEntries.Count + " 個檔案。"
                + "\n目的地：" + destinationTextBox.Text
                + "\n\n檔名與預覽清單相同；重複檔案也會保留。";

            if (engine.HasUndo)
            {
                confirmation += "\n\n這次整理後，只能復原這一批。上一次的復原紀錄會被取代。";
            }

            if (MessageBox.Show(this, confirmation, "確認整理",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            await RunFileOperationAsync(
                () => engine.Execute(selectedEntries),
                "正在整理，請勿關閉程式…",
                "整理完成");
        }

        private async Task UndoAsync()
        {
            const string confirmation = "將上次整理的檔案移回原位置。"
                + "遇到同名或內容已變更的檔案，會保留現況並列出原因。";

            if (MessageBox.Show(this, confirmation, "確認復原",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            await RunFileOperationAsync(
                () => engine.Undo(),
                "正在復原，請勿關閉程式…",
                "復原完成");
        }

        /// <summary>整理與復原共用的背景執行、錯誤顯示及狀態恢復流程。</summary>
        private async Task RunFileOperationAsync(
            Func<List<string>> operation, string runningMessage, string completedMessage)
        {
            // Func<List<string>> 表示「不接受參數，回傳字串清單的方法」。
            // 呼叫端可以傳入整理或復原，這裡不需要知道它是哪一種操作。
            SetBusyState(true, runningMessage);
            string resultMessage = completedMessage;
            var operationErrors = new List<string>();

            try
            {
                operationErrors = await Task.Run(operation);
                if (operationErrors.Count > 0)
                {
                    resultMessage = "有 " + operationErrors.Count
                        + " 個檔案未完成，請按「查看原因」。";
                }
            }
            catch (Exception exception)
            {
                resultMessage = "這次未能完成，請按「查看原因」。";
                operationErrors.Add(exception.Message);
            }
            finally
            {
                ClearPreview();
                // 清單可以重掃，但失敗明細要保留，讓使用者稍後仍能查看全部原因。
                issueDetails = operationErrors;
                issueTitle = "未完成的檔案";
                SetBusyState(false, resultMessage + " 按「掃描檔案」更新清單。");
                emptyPreviewLabel.Text = operationErrors.Count > 0
                    ? "部分檔案還沒處理完\n按「查看原因」了解詳情，或重新掃描。"
                    : completedMessage + "\n按「掃描檔案」查看資料夾目前的內容。";
            }
        }

        // ── 畫面資料與狀態：維持預覽、勾選和按鈕一致。 ──

        private void SavePreferences()
        {
            settings.SourceDirectory = sourceTextBox.Text;
            settings.DestinationDirectory = destinationTextBox.Text;
            settings.FollowSource = !hasCustomDestination;
            try
            {
                settingsStore.Save(settings);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "這次的選擇仍可使用，但設定沒有存下來，下次開啟需要重新選擇。\n" + exception.Message,
                    "設定儲存失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ManageCategories()
        {
            previewGrid.EndEdit();
            using (var dialog = new CategoryDialog(settings.CustomCategories))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyCustomCategories(dialog.CustomCategories);
                SavePreferences();
            }
        }

        private void ApplyCustomCategories(List<string> customCategories)
        {
            List<string> availableNames = CategoryNames.Combine(customCategories);
            var categoryColumn = (DataGridViewComboBoxColumn)previewGrid.Columns["category"];
            isPopulatingPreview = true;
            int reassignedCount = 0;
            try
            {
                // 先加新選項、再處理被移除的值、最後移除舊選項，避免下拉格出現無效值。
                foreach (string name in availableNames)
                {
                    if (!categoryColumn.Items.Contains(name)) categoryColumn.Items.Add(name);
                }

                foreach (DataGridViewRow row in previewGrid.Rows)
                {
                    var entry = (Entry)row.Tag;
                    if (!availableNames.Contains(entry.Category))
                    {
                        entry.Category = "其他";
                        row.Cells["category"].Value = "其他";
                        reassignedCount++;
                    }
                }

                foreach (string oldName in categoryColumn.Items.Cast<string>().ToArray())
                {
                    if (!availableNames.Contains(oldName)) categoryColumn.Items.Remove(oldName);
                }

                settings.CustomCategories = new List<string>(customCategories);
                RefreshTargetPaths();
            }
            finally
            {
                isPopulatingPreview = false;
            }

            UpdateSelectionSummary();
            if (reassignedCount > 0)
                statusLabel.Text += " 已將 " + reassignedCount + " 個檔案的分類改回「其他」。";
        }

        private void UpdateScanProgress(ScanProgress progress)
        {
            // 掃描結束後可能還有排隊中的通知，不能覆蓋最終摘要。
            if (scanCancellation == null || !isBusy || scanCancellation.IsCancellationRequested) return;
            double completed = progress.CompletedFiles;
            if (!String.IsNullOrEmpty(progress.CurrentFile)) completed += progress.CurrentFilePercent / 100d;
            progressBar.Value = progress.TotalFiles == 0 ? 100
                : Math.Max(0, Math.Min(100, (int)(100d * completed / progress.TotalFiles)));
            statusLabel.Text = "已檢查 " + progress.CompletedFiles + " / " + progress.TotalFiles + " 個檔案";
            if (!String.IsNullOrEmpty(progress.CurrentFile))
                statusLabel.Text += "；正在讀取：" + progress.CurrentFile + "（" + progress.CurrentFilePercent + "%）";
        }

        private void ShowIssueDetails()
        {
            using (var dialog = new Form
            {
                Text = issueTitle, Size = new Size(780, 460),
                StartPosition = FormStartPosition.CenterParent, Font = Font
            })
            {
                // 可捲動和複製全部原因，不截斷成固定筆數。
                dialog.Controls.Add(new TextBox
                {
                    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    ScrollBars = ScrollBars.Both, WordWrap = false,
                    Text = String.Join(Environment.NewLine + Environment.NewLine, issueDetails)
                });
                dialog.ShowDialog(this);
            }
        }

        private List<Entry> GetSelectedEntries()
        {
            // 用直觀的 foreach 收集勾選項目，方便逐步追蹤資料從哪裡來。
            var selectedEntries = new List<Entry>();
            foreach (DataGridViewRow row in previewGrid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["pick"].Value))
                {
                    selectedEntries.Add((Entry)row.Tag);
                }
            }

            return selectedEntries;
        }

        private void ClearPreview()
        {
            hasScanned = false;
            issueTitle = "無法讀取的檔案";
            issueDetails.Clear();
            ignoredFileCount = 0;
            detailsButton.Enabled = false;
            currentPlan.Clear();
            previewGrid.Rows.Clear();
            organizeButton.Enabled = false;
            statusLabel.Text = "按「掃描檔案」查看這個資料夾的檔案。";
            UpdateEmptyPreview();
        }

        private void SetAllSelections(bool isSelected)
        {
            if (isBusy)
            {
                return;
            }

            // 全選時先暫停逐列的統計更新，避免 N 個檔案重算 N 次。
            isPopulatingPreview = true;
            try
            {
                foreach (DataGridViewRow row in previewGrid.Rows)
                {
                    row.Cells["pick"].Value = isSelected;
                }
            }
            finally
            {
                isPopulatingPreview = false;
            }

            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            if (isBusy)
            {
                return;
            }

            int selectedCount = GetSelectedEntries().Count;
            organizeButton.Enabled = selectedCount > 0;
            if (!hasScanned && currentPlan.Count == 0)
            {
                statusLabel.Text = issueDetails.Count > 0
                    ? "有 " + issueDetails.Count + " 個檔案未完成，請按「查看原因」。"
                    : "按「掃描檔案」查看這個資料夾的檔案。";
            }
            else
            {
                statusLabel.Text = currentPlan.Count == 0
                    ? "沒有可整理的檔案。"
                    : String.Format("共 {0} 個檔案，已選 {1} 個。", currentPlan.Count, selectedCount);
                if (issueDetails.Count > 0)
                    statusLabel.Text += " " + issueDetails.Count + " 個檔案無法讀取，請查看原因。";
                if (ignoredFileCount > 0)
                    statusLabel.Text += " 另有 " + ignoredFileCount + " 個檔案已略過。";
            }
            detailsButton.Enabled = issueDetails.Count > 0;
            UpdateEmptyPreview();
        }

        private void RefreshTargetPaths()
        {
            try
            {
                Engine.SetTargets(currentPlan, destinationTextBox.Text);
                foreach (DataGridViewRow row in previewGrid.Rows)
                {
                    var entry = (Entry)row.Tag;
                    row.Cells["target"].Value = entry.Target;
                }
            }
            catch (Exception exception)
            {
                ClearPreview();
                MessageBox.Show(this, exception.Message, "無法建立整理建議");
            }
        }

        private void SetBusyState(bool busy, string message)
        {
            isBusy = busy;
            folderPanel.Enabled = !busy;
            previewGrid.Enabled = !busy;
            scanButton.Enabled = !busy;
            manageCategoriesButton.Enabled = !busy;
            detailsButton.Enabled = !busy && issueDetails.Count > 0;
            organizeButton.Enabled = !busy && currentPlan.Count > 0;
            undoButton.Enabled = !busy && engine.HasUndo;
            progressBar.Visible = busy;
            if (busy) progressBar.Style = ProgressBarStyle.Marquee;
            statusLabel.Text = message;
            UpdateEmptyPreview();
        }

        private static string FormatFileSize(long bytes)
        {
            // 除數加上 d 表示 double，避免整數除法截掉小數。
            if (bytes >= 1073741824)
            {
                return (bytes / 1073741824d).ToString("0.0") + " GB";
            }
            if (bytes >= 1048576)
            {
                return (bytes / 1048576d).ToString("0.0") + " MB";
            }

            if (bytes >= 1024)
            {
                return (bytes / 1024d).ToString("0.0") + " KB";
            }

            return bytes + " B";
        }

        private static string GetDownloadsDirectory()
        {
            // Windows 允許搬動「下載」資料夾，因此先讀取系統設定。
            // @ 開頭的字串會保留反斜線，不必每個 \ 都寫成 \\。
            using (var registryKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
            {
                string configuredPath = registryKey == null
                    ? null
                    : registryKey.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;

                if (!String.IsNullOrEmpty(configuredPath))
                {
                    return Environment.ExpandEnvironmentVariables(configuredPath);
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // 開發用的基本介面驗證。初學閱讀時可以先跳過這一段。
        // 不移動檔案，只掃描指定的測試資料、操作預覽並存下畫面。
        public async Task RunSmokeTestAsync(string screenshotPath, string fixtureDirectory,
            string testStateDirectory)
        {
            sourceTextBox.Text = fixtureDirectory;
            destinationTextBox.Text = fixtureDirectory;
            string lockedFixture = Path.Combine(fixtureDirectory, "ui-locked-fixture.txt");
            File.WriteAllText(lockedFixture, "UI failure test");
            using (var locked = new FileStream(lockedFixture, FileMode.Open,
                FileAccess.ReadWrite, FileShare.None))
            {
                await ScanAsync();
                if (issueDetails.Count != 1 || !detailsButton.Enabled || currentPlan.Count == 0)
                    throw new Exception("UI did not retain preview and error details after a locked file");
            }
            File.Delete(lockedFixture);

            Task cancelledScan = ScanAsync();
            cancelScanButton.PerformClick();
            await cancelledScan;
            if (!statusLabel.Text.Contains("已取消") || organizeButton.Enabled || isBusy)
                throw new Exception("Cancel button did not restore idle UI");
            await ScanAsync();

            if (previewGrid.Rows.Count == 0 || !organizeButton.Enabled)
            {
                throw new Exception("Preview did not populate");
            }

            SetAllSelections(false);
            if (organizeButton.Enabled)
            {
                throw new Exception("Empty selection must disable move");
            }

            SetAllSelections(true);
            previewGrid.Rows[0].Cells["category"].Value = "其他";
            if (!Convert.ToString(previewGrid.Rows[0].Cells["target"].Value).Contains("其他"))
            {
                throw new Exception("Category edit did not refresh destination");
            }

            // 模擬實際新增與移除按鈕；使用獨立設定與測試資料，不動正式偏好。
            using (var dialog = new CategoryDialog(new string[0]))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-30000, -30000);
                dialog.Show(this);
                var input = (TextBox)dialog.Controls.Find("categoryName", true)[0];
                var add = (Button)dialog.Controls.Find("addCategory", true)[0];
                var remove = (Button)dialog.Controls.Find("removeCategory", true)[0];
                input.Text = "暫時分類";
                add.PerformClick();
                if (!dialog.CustomCategories.Contains("暫時分類")) throw new Exception("Category add failed");
                remove.PerformClick();
                if (dialog.CustomCategories.Count != 0) throw new Exception("Category remove failed");
                input.Text = "帳單";
                add.PerformClick();
                input.Text = "工作資料";
                add.PerformClick();
                ApplyCustomCategories(dialog.CustomCategories);
                using (var dialogBitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(dialogBitmap, new Rectangle(Point.Empty, dialog.Size));
                    dialogBitmap.Save(screenshotPath + ".categories.png");
                }
                dialog.Close();
            }

            previewGrid.Rows[0].Cells["category"].Value = "帳單";
            string preservedDirectory = Path.Combine(fixtureDirectory, "帳單");
            Directory.CreateDirectory(preservedDirectory);
            string preservedFile = Path.Combine(preservedDirectory, "keep.txt");
            File.WriteAllText(preservedFile, "keep custom folder contents");
            ApplyCustomCategories(new List<string> { "工作資料" });
            if (Convert.ToString(previewGrid.Rows[0].Cells["category"].Value) != "其他"
                || File.ReadAllText(preservedFile) != "keep custom folder contents")
                throw new Exception("Removing category did not preserve files and remap preview");
            ApplyCustomCategories(new List<string> { "帳單", "工作資料" });
            previewGrid.Rows[0].Cells["category"].Value = "帳單";
            hasCustomDestination = true;
            destinationTextBox.Text = Path.Combine(fixtureDirectory, "指定目的地");
            RefreshTargetPaths();
            SavePreferences();
            string settingsWarning;
            AppSettings saved = settingsStore.Load(out settingsWarning);
            if (settingsWarning.Length > 0 || saved.FollowSource
                || saved.SourceDirectory != fixtureDirectory || !saved.CustomCategories.Contains("帳單"))
                throw new Exception("UI preferences were not saved");

            using (var restored = new MainWindow(testStateDirectory))
            {
                var categories = (DataGridViewComboBoxColumn)restored.previewGrid.Columns["category"];
                if (restored.sourceTextBox.Text != fixtureDirectory
                    || restored.destinationTextBox.Text != destinationTextBox.Text
                    || !restored.hasCustomDestination || !categories.Items.Contains("帳單"))
                    throw new Exception("Reopened UI did not restore paths and custom category options");
            }
            hasCustomDestination = false;
            destinationTextBox.Text = sourceTextBox.Text;
            RefreshTargetPaths();
            SavePreferences();
            using (var restored = new MainWindow(testStateDirectory))
            {
                if (restored.hasCustomDestination
                    || restored.destinationTextBox.Text != restored.sourceTextBox.Text)
                    throw new Exception("Follow-source preference did not survive restart");
            }

            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(screenshotPath);
            }
        }
    }
}
