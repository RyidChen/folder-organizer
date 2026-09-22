using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    /// <summary>編輯分類選項的副本。只有按「套用」才交回主視窗，不在這裡操作磁碟。</summary>
    public class CategoryDialog : Form
    {
        private readonly ListBox categoryList = new ListBox();
        private readonly TextBox nameTextBox = new TextBox();
        private readonly List<string> customCategories;

        public List<string> CustomCategories
        {
            get { return new List<string>(customCategories); }
        }

        public CategoryDialog(IEnumerable<string> existingCategories)
        {
            customCategories = new List<string>(existingCategories);
            Text = "管理自訂分類";
            Font = new Font("Microsoft JhengHei UI", 10);
            ClientSize = new Size(560, 440);
            MinimumSize = new Size(540, 420);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimizeBox = false;
            MaximizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "加入常用分類，例如「帳單」或「工作資料」。\n"
                    + "開始整理時才會建立資料夾。移除分類不會刪除檔案，\n"
                    + "已選用這個分類的檔案會改為「其他」。\n"
                    + "文件、圖片等預設分類無法移除。"
            }, 0, 0);

            categoryList.Name = "customCategoryList";
            categoryList.Dock = DockStyle.Fill;
            categoryList.IntegralHeight = false;
            layout.Controls.Add(categoryList, 0, 1);

            var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            inputRow.Controls.Add(new Label { Text = "資料夾名稱", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            nameTextBox.Name = "categoryName";
            nameTextBox.Dock = DockStyle.Fill;
            nameTextBox.MaxLength = 100;
            inputRow.Controls.Add(nameTextBox, 1, 0);
            var addButton = new Button { Name = "addCategory", Text = "新增", Dock = DockStyle.Fill };
            addButton.Click += (sender, eventArgs) => AddCategory();
            inputRow.Controls.Add(addButton, 2, 0);
            layout.Controls.Add(inputRow, 0, 2);

            var removeButton = new Button { Name = "removeCategory", Text = "移除這個分類",
                Width = 180, Height = 34, Enabled = false };
            categoryList.SelectedIndexChanged += (sender, eventArgs) =>
                removeButton.Enabled = categoryList.SelectedIndex >= 0;
            removeButton.Click += (sender, eventArgs) =>
            {
                if (categoryList.SelectedIndex < 0) return;
                customCategories.RemoveAt(categoryList.SelectedIndex);
                RefreshList();
            };
            layout.Controls.Add(removeButton, 0, 3);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft };
            var applyButton = new Button { Text = "套用", DialogResult = DialogResult.OK,
                Width = 90, Height = 34 };
            var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel,
                Width = 90, Height = 34 };
            actions.Controls.Add(applyButton);
            actions.Controls.Add(cancelButton);
            layout.Controls.Add(actions, 0, 4);
            CancelButton = cancelButton;
            // Enter 在名稱欄輸入時優先新增，避免尚未新增就關閉視窗。
            AcceptButton = addButton;
            RefreshList();
        }

        private void AddCategory()
        {
            string name = nameTextBox.Text.Trim();
            try
            {
                CategoryNames.Validate(name);
                if (CategoryNames.Combine(customCategories).Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("這個分類已經存在。");
                customCategories.Add(name);
                RefreshList();
                categoryList.SelectedItem = name;
                nameTextBox.Clear();
                nameTextBox.Focus();
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(this, exception.Message, "無法新增分類",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RefreshList()
        {
            categoryList.Items.Clear();
            categoryList.Items.AddRange(customCategories.ToArray());
        }
    }
}
