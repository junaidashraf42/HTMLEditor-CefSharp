using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HTMLEditor
{
    public class FindControl : UserControl
    {
        private TextBox searchTextBox;
        private Button findNextButton;
        private Button findPrevButton;
        private Button matchCaseButton;
        private Button closeButton;
        private Label resultsLabel;
        private Panel searchPanel;
        private bool matchCase = false;

        public event EventHandler<FindEventArgs> FindNext;
        public event EventHandler<FindEventArgs> FindPrevious;
        public event EventHandler CloseFind;

        public FindControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.searchPanel = new Panel();
            this.searchTextBox = new TextBox();
            this.findNextButton = new Button();
            this.findPrevButton = new Button();
            this.matchCaseButton = new Button();
            this.closeButton = new Button();
            this.resultsLabel = new Label();

            // 
            // FindControl - Chrome-style appearance with auto-width
            // 
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.BorderStyle = BorderStyle.None;
            this.Controls.Add(this.searchPanel);
            this.Controls.Add(this.resultsLabel);
            this.Controls.Add(this.closeButton);
            this.Name = "FindControl";
            this.Size = new Size(320, 50); // Reduced width for auto-sizing
            this.Anchor = AnchorStyles.Top | AnchorStyles.Left; // Anchor to top-left
            this.Visible = false;
            this.Paint += FindControl_Paint;

            // 
            // searchPanel - Container for search elements
            // 
            this.searchPanel.BackColor = Color.White;
            this.searchPanel.Location = new Point(8, 8);
            this.searchPanel.Size = new Size(275, 26);
            this.searchPanel.Paint += SearchPanel_Paint;
            this.searchPanel.Controls.Add(this.searchTextBox);
            this.searchPanel.Controls.Add(this.findPrevButton);
            this.searchPanel.Controls.Add(this.findNextButton);
            this.searchPanel.Controls.Add(this.matchCaseButton);

            // 
            // searchTextBox - Chrome-style input
            // 
            this.searchTextBox.Location = new Point(8, 3);
            this.searchTextBox.Name = "searchTextBox";
            this.searchTextBox.Size = new Size(180, 20);
            this.searchTextBox.TabIndex = 0;
            this.searchTextBox.BorderStyle = BorderStyle.None;
            this.searchTextBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.searchTextBox.KeyDown += new KeyEventHandler(this.SearchTextBox_KeyDown);
            this.searchTextBox.TextChanged += new EventHandler(this.SearchTextBox_TextChanged);

            // 
            // findPrevButton - Chrome-style up arrow
            // 
            this.findPrevButton.Location = new Point(195, 2);
            this.findPrevButton.Name = "findPrevButton";
            this.findPrevButton.Size = new Size(22, 22);
            this.findPrevButton.TabIndex = 1;
            this.findPrevButton.Text = "▲";
            this.findPrevButton.Font = new Font("Segoe UI", 8F);
            this.findPrevButton.FlatStyle = FlatStyle.Flat;
            this.findPrevButton.FlatAppearance.BorderSize = 0;
            this.findPrevButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
            this.findPrevButton.BackColor = Color.Transparent;
            this.findPrevButton.UseVisualStyleBackColor = false;
            this.findPrevButton.Click += new EventHandler(this.FindPrevButton_Click);

            // 
            // findNextButton - Chrome-style down arrow
            // 
            this.findNextButton.Location = new Point(220, 2);
            this.findNextButton.Name = "findNextButton";
            this.findNextButton.Size = new Size(22, 22);
            this.findNextButton.TabIndex = 2;
            this.findNextButton.Text = "▼";
            this.findNextButton.Font = new Font("Segoe UI", 8F);
            this.findNextButton.FlatStyle = FlatStyle.Flat;
            this.findNextButton.FlatAppearance.BorderSize = 0;
            this.findNextButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
            this.findNextButton.BackColor = Color.Transparent;
            this.findNextButton.UseVisualStyleBackColor = false;
            this.findNextButton.Click += new EventHandler(this.FindNextButton_Click);

            // 
            // matchCaseButton - Chrome-style match case toggle
            // 
            this.matchCaseButton.Location = new Point(245, 2);
            this.matchCaseButton.Name = "matchCaseButton";
            this.matchCaseButton.Size = new Size(30, 22);
            this.matchCaseButton.TabIndex = 3;
            this.matchCaseButton.Text = "Aa";
            this.matchCaseButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            this.matchCaseButton.FlatStyle = FlatStyle.Flat;
            this.matchCaseButton.FlatAppearance.BorderSize = 0;
            this.matchCaseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
            this.matchCaseButton.BackColor = Color.Transparent;
            this.matchCaseButton.UseVisualStyleBackColor = false;
            this.matchCaseButton.Click += new EventHandler(this.MatchCaseButton_Click);

            // 
            // closeButton - Chrome-style X button
            // 
            this.closeButton.Location = new Point(290, 8);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new Size(26, 26);
            this.closeButton.TabIndex = 4;
            this.closeButton.Text = "✕";
            this.closeButton.Font = new Font("Segoe UI", 10F);
            this.closeButton.FlatStyle = FlatStyle.Flat;
            this.closeButton.FlatAppearance.BorderSize = 0;
            this.closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            this.closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(196, 43, 28);
            this.closeButton.BackColor = Color.Transparent;
            this.closeButton.ForeColor = Color.FromArgb(95, 99, 104);
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new EventHandler(this.CloseButton_Click);
            this.closeButton.MouseEnter += (s, e) => { closeButton.ForeColor = Color.White; };
            this.closeButton.MouseLeave += (s, e) => { closeButton.ForeColor = Color.FromArgb(95, 99, 104); };
            
            // 
            // resultsLabel - Chrome-style results display
            // 
            this.resultsLabel.AutoSize = true;
            this.resultsLabel.Location = new Point(8, 36);
            this.resultsLabel.Name = "resultsLabel";
            this.resultsLabel.Size = new Size(0, 15);
            this.resultsLabel.TabIndex = 5;
            this.resultsLabel.Font = new Font("Segoe UI", 8F);
            this.resultsLabel.ForeColor = Color.FromArgb(95, 99, 104);
            this.resultsLabel.Visible = false;
        }

        private void FindControl_Paint(object sender, PaintEventArgs e)
        {
            // Draw subtle border around the entire control
            using (Pen borderPen = new Pen(Color.FromArgb(218, 220, 224), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void SearchPanel_Paint(object sender, PaintEventArgs e)
        {
            // Draw rounded border around search panel
            using (Pen borderPen = new Pen(Color.FromArgb(218, 220, 224), 1))
            {
                Rectangle rect = new Rectangle(0, 0, searchPanel.Width - 1, searchPanel.Height - 1);
                using (GraphicsPath path = GetRoundedRectangle(rect, 4))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawPath(borderPen, path);
                }
            }
        }

        private GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.X + rect.Width - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.X + rect.Width - radius, rect.Y + rect.Height - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - radius, radius, radius, 90, 90);
            path.CloseAllFigures();
            return path;
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                FindNextButton_Click(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CloseButton_Click(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            // Auto-search as user types (like Chrome)
            if (!string.IsNullOrEmpty(searchTextBox.Text))
            {
                FindNext?.Invoke(this, new FindEventArgs(searchTextBox.Text, matchCase, true));
            }
            else
            {
                ClearResults();
            }
        }

        private void FindNextButton_Click(object sender, EventArgs e)
        {
            FindNext?.Invoke(this, new FindEventArgs(searchTextBox.Text, matchCase, true));
        }

        private void FindPrevButton_Click(object sender, EventArgs e)
        {
            FindNext?.Invoke(this, new FindEventArgs(searchTextBox.Text, matchCase, false));
        }

        private void MatchCaseButton_Click(object sender, EventArgs e)
        {
            matchCase = !matchCase;
            matchCaseButton.BackColor = matchCase ? Color.FromArgb(66, 133, 244) : Color.Transparent;
            matchCaseButton.ForeColor = matchCase ? Color.White : Color.FromArgb(95, 99, 104);
            
            // Re-search with new match case setting
            if (!string.IsNullOrEmpty(searchTextBox.Text))
            {
                FindNext?.Invoke(this, new FindEventArgs(searchTextBox.Text, matchCase, true));
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            CloseFind?.Invoke(this, EventArgs.Empty);
        }

        public void ShowFind()
        {
            this.Visible = true;
            this.searchTextBox.Focus();
            this.searchTextBox.SelectAll();
        }

        public void HideFind()
        {
            this.Visible = false;
        }

        public string SearchText
        {
            get { return searchTextBox.Text; }
            set { searchTextBox.Text = value; }
        }
        
        public void UpdateResults(int count, int currentMatch)
        {
            if (count > 0)
            {
                resultsLabel.Text = $"{currentMatch + 1} of {count}";
                resultsLabel.Visible = true;
                searchTextBox.BackColor = Color.White;
            }
            else if (!string.IsNullOrEmpty(searchTextBox.Text))
            {
                resultsLabel.Text = "0 of 0";
                resultsLabel.Visible = true;
                searchTextBox.BackColor = Color.FromArgb(255, 245, 245); // Light red for no matches
            }
        }
        
        public void ClearResults()
        {
            resultsLabel.Text = "";
            resultsLabel.Visible = false;
            searchTextBox.BackColor = Color.White;
        }

        public bool MatchCase
        {
            get { return matchCase; }
        }
    }

    public class FindEventArgs : EventArgs
    {
        public string SearchText { get; private set; }
        public bool MatchCase { get; private set; }
        public bool Forward { get; private set; }

        public FindEventArgs(string searchText, bool matchCase, bool forward)
        {
            SearchText = searchText;
            MatchCase = matchCase;
            Forward = forward;
        }
    }
}
