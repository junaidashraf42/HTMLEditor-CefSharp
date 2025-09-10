using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.IO;
using CefSharp;
using CefSharp.WinForms;
using System.Drawing.Printing;
using System.Text.RegularExpressions;
using System.Text.Json;
using AngleSharp.Dom;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
namespace HTMLEditor
{
    public partial class Form1 : Form
    {
        private ChromiumWebBrowser browser;
        private FindControl findControl;
        public PageBreakManager _pageBreakManager = new PageBreakManager();
        private Label pageNumberLabel;
        private string _currentReportPath;
        private string _currentReportContent;
        private bool _isMultiplePageView = false; // Default to single page view (normal scrollable view)
        private Button _multiplePageViewButton; // Reference to the Multiple Page View button
        private System.Drawing.Image _iconSinglePage;   // Icon for Single Page view
        private System.Drawing.Image _iconMultiplePage; // Icon for Multiple Page view
        int currentPage = 1;
        int totalPages;
        private bool _isUpdatingPageCount = false;

        public Form1()
        {
            Text = "HTML Editor";
            this.WindowState = FormWindowState.Maximized;
            //this.AutoScaleMode = AutoScaleMode.Dpi;   // or AutoScaleMode.Font
            //this.AutoScaleDimensions = new SizeF(96F, 96F);
            //this.HighDpiMode = System.Windows.Forms.HighDpiMode.PerMonitorV2;
            this.KeyPreview = true;
            browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };

            // Using the improved JavaScript bridge with better error handling
            JavaScriptBridge jsBridge = new JavaScriptBridge(this);
            browser.JavascriptObjectRepository.Register("javaScriptBridge", jsBridge, isAsync: false);
            
            // Wait for browser to initialize before setting up notification function
            browser.IsBrowserInitializedChanged += (sender, args) => {
                if (browser.IsBrowserInitialized) {
                    System.Diagnostics.Debug.WriteLine("Browser initialized");
                }
            };

            // Handle frame load end to setup bridge after content is loaded
            browser.FrameLoadEnd += async (sender, args) => {
                if (args.Frame.IsMain) {
                    System.Diagnostics.Debug.WriteLine("Main frame loaded, setting up JavaScript bridge");

                    //reset zoom level each time file loads.
                    this.Invoke(new Action(() =>
                    {
                        browser.SetZoomLevel(0.0);
                    }));
                    // Wait for the browser to be ready to execute scripts
                    await WaitForBrowserReady();
                    
                    // Verify bridge is accessible from JavaScript
                    await VerifyJavaScriptBridge();
                    
                    // Inject the notification function
                    await SetupPageBreakNotificationFunction();
                }
            };

            // Set up direct right-click handler for page break insertion
            browser.MenuHandler = new DirectPageBreakMenuHandler(this);
            
            // Set up find handler for search results
            browser.FindHandler = new FindHandler(this);
            
            // Set up keyboard handler to intercept Ctrl+F
            browser.KeyboardHandler = new KeyboardHandler(this);

            // Create and configure the find control
            findControl = new FindControl();
            findControl.Dock = DockStyle.None; // Remove docking to allow custom positioning
            findControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            findControl.Location = new Point(-70, 160); // Position adjusted for ribbon
            findControl.Visible = false;
            findControl.FindNext += FindControl_FindNext;
            findControl.FindPrevious += FindControl_FindPrevious;
            findControl.CloseFind += FindControl_CloseFind;

            // Add controls to form
            Controls.Add(browser);
            Controls.Add(findControl);
            
            // Set up keyboard handling for Ctrl+F
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            // Initialize the ribbon panel
            //InitializeRibbonPanel();
            InitializeToolbar();

            // Kept for designer compatibility
            Load += async (s, e) => await LoadEditorTemplate();
            
            // Focus the browser when form is activated
            Activated += (s, e) => {
                if (browser != null && browser.IsBrowserInitialized)
                {
                    browser.Focus();
                }
            };
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            this.KeyPreview = true;
            await LoadEditorTemplate();
        }

        #region toolbarregion
        private void InitializeToolbar()
        {
            // Suspend layout to prevent flicker during setup
            this.SuspendLayout();

            // Create the main toolbar panel (light gray background) with fixed height
            Panel toolbarPanel = new Panel();
            toolbarPanel.Dock = DockStyle.Top;
            toolbarPanel.BackColor = Color.FromArgb(211, 211, 211); // Light gray
            toolbarPanel.Height = 120; // fixed height

            // Create three section panels
            Panel navigationSection = CreateNavigationSection();
            Panel toolsSection = CreateToolsSection();
            Panel sdiSection = CreateSDISection();

            // Create separators
            Panel separator1 = CreateSeparator();
            Panel separator2 = CreateSeparator();

            // Add sections and separators to toolbar panel
            toolbarPanel.Controls.Add(navigationSection);
            toolbarPanel.Controls.Add(separator1);
            toolbarPanel.Controls.Add(toolsSection);
            toolbarPanel.Controls.Add(separator2);
            toolbarPanel.Controls.Add(sdiSection);

            // Add the toolbar panel to the form
            try
            {
                this.Controls.Add(toolbarPanel);

                // Initial centering and resize handling
                this.Load += (s, e) => {
                    CenterSections(toolbarPanel, navigationSection, separator1, toolsSection, separator2, sdiSection);
                };

                this.Resize += (s, e) => {
                    CenterSections(toolbarPanel, navigationSection, separator1, toolsSection, separator2, sdiSection);
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing toolbar: {ex.Message}");
            }

            // Resume layout
            this.ResumeLayout(false);
        }

        private Panel CreateNavigationSection()
        {
            Panel section = new Panel();
            section.Size = new Size(330, 100);
            section.BackColor = Color.Transparent;

            // Navigation label
            Label lblNav = new Label();
            lblNav.Text = "Navigation";
            lblNav.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblNav.AutoSize = true;
            lblNav.Location = new Point(0, 5);
            section.Controls.Add(lblNav);

            // Center the label
            lblNav.Left = (section.Width - lblNav.Width) / 2;

            // Navigation buttons row
            int buttonY = 50;
            int buttonSize = 40;
            int spacing = 10;

            Button btnPrevDouble = new Button();
            btnPrevDouble.Text = "<<";
            btnPrevDouble.Size = new Size(buttonSize, buttonSize - 8);
            btnPrevDouble.Location = new Point(20, buttonY);
            btnPrevDouble.FlatStyle = FlatStyle.Flat;
            btnPrevDouble.Click += (s, e) => { currentPage = 1; NavigateToPage(currentPage); UpdatePageLabel(pageNumberLabel); };
            section.Controls.Add(btnPrevDouble);

            Button btnPrev = new Button();
            btnPrev.Text = "<";
            btnPrev.Size = new Size(buttonSize, buttonSize - 8);
            btnPrev.Location = new Point(btnPrevDouble.Right + spacing + 10, buttonY);
            btnPrev.FlatStyle = FlatStyle.Flat;
            btnPrev.Click += (s, e) => { if (currentPage > 1) { currentPage--; NavigateToPage(currentPage); UpdatePageLabel(pageNumberLabel); } };
            section.Controls.Add(btnPrev);

            pageNumberLabel = new Label();
            pageNumberLabel.Text = $"Page {currentPage} of ...";
            pageNumberLabel.AutoSize = true;
            pageNumberLabel.Location = new Point(btnPrev.Right + spacing, buttonY + 8);
            section.Controls.Add(pageNumberLabel);

            Button btnNext = new Button();
            btnNext.Text = ">";
            btnNext.Size = new Size(buttonSize, buttonSize - 8);
            btnNext.Location = new Point(pageNumberLabel.Right + spacing + 10, buttonY);
            btnNext.FlatStyle = FlatStyle.Flat;
            btnNext.Click += (s, e) => { if (currentPage < totalPages) { currentPage++; NavigateToPage(currentPage); UpdatePageLabel(pageNumberLabel); } };
            section.Controls.Add(btnNext);

            Button btnNextDouble = new Button();
            btnNextDouble.Text = ">>";
            btnNextDouble.Size = new Size(buttonSize, buttonSize - 8);
            btnNextDouble.Location = new Point(btnNext.Right + spacing, buttonY);
            btnNextDouble.FlatStyle = FlatStyle.Flat;
            btnNextDouble.Click += (s, e) => { currentPage = totalPages; NavigateToPage(currentPage); UpdatePageLabel(pageNumberLabel); };
            section.Controls.Add(btnNextDouble);

            return section;
        }

        private Panel CreateToolsSection()
        {
            Panel section = new Panel();
            section.Size = new Size(630, 100); // Increased width to accommodate wider buttons
            section.BackColor = Color.Transparent;

            // Tools label
            Label lblTools = new Label();
            lblTools.Text = "Tools";
            lblTools.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblTools.AutoSize = true;
            lblTools.Location = new Point(0, 5);
            section.Controls.Add(lblTools);

            // Center the label
            lblTools.Left = (section.Width - lblTools.Width) / 2;

            // Tools buttons row
            int buttonY = 30;
            int buttonWidth = 70;
            int buttonHeight = 70;
            int spacing = 15;
            int startX = 20;

            Button btnPrint = new Button();
            btnPrint.Image = Properties.Resources.print.ToBitmap();
            btnPrint.Text = "Print";
            btnPrint.TextImageRelation = TextImageRelation.ImageAboveText;
            btnPrint.ImageAlign = ContentAlignment.TopCenter;
            btnPrint.TextAlign = ContentAlignment.BottomCenter;
            btnPrint.Size = new Size(buttonWidth, buttonHeight);
            btnPrint.Location = new Point(startX, buttonY);
            btnPrint.FlatStyle = FlatStyle.Flat;
            btnPrint.FlatAppearance.BorderSize = 0;
            btnPrint.Padding = new Padding(0, 10, 0, 0); // Increased padding for more spacing
            btnPrint.Click += async (s, e) => await PrintHtmlContent();
            section.Controls.Add(btnPrint);

            Button btnExport = new Button();
            btnExport.Image = Properties.Resources.export.ToBitmap();
            btnExport.Text = "Export";
            btnExport.TextImageRelation = TextImageRelation.ImageAboveText;
            btnExport.ImageAlign = ContentAlignment.TopCenter;
            btnExport.TextAlign = ContentAlignment.BottomCenter;
            btnExport.Size = new Size(buttonWidth, buttonHeight);
            btnExport.Location = new Point(btnPrint.Right + spacing, buttonY);
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.Padding = new Padding(0, 10, 0, 0);
            btnExport.Click += async (s, e) => await GetEditedHtml();
            section.Controls.Add(btnExport);

            _iconMultiplePage = Properties.Resources.multiple.ToBitmap();
            _iconSinglePage = Properties.Resources.single.ToBitmap();

            Button btnMultiPage = new Button();
            btnMultiPage.Image = _iconMultiplePage;
            btnMultiPage.Text = "Multiple Page";
            btnMultiPage.TextImageRelation = TextImageRelation.ImageAboveText;
            btnMultiPage.ImageAlign = ContentAlignment.TopCenter;
            btnMultiPage.TextAlign = ContentAlignment.BottomCenter;
            btnMultiPage.Size = new Size(110, buttonHeight);
            btnMultiPage.Location = new Point(btnExport.Right + spacing, buttonY);
            btnMultiPage.FlatStyle = FlatStyle.Flat;
            btnMultiPage.FlatAppearance.BorderSize = 0;
            btnMultiPage.Padding = new Padding(0, 10, 0, 0);
            btnMultiPage.Click += async (s, e) => await ToggleMultiplePageView();
            section.Controls.Add(btnMultiPage);
            _multiplePageViewButton = btnMultiPage;

            Button btnFind = new Button();
            btnFind.Image = Properties.Resources.find.ToBitmap();
            btnFind.Text = "Find";
            btnFind.TextImageRelation = TextImageRelation.ImageAboveText;
            btnFind.ImageAlign = ContentAlignment.TopCenter;
            btnFind.TextAlign = ContentAlignment.BottomCenter;
            btnFind.Size = new Size(buttonWidth, buttonHeight);
            btnFind.Location = new Point(btnMultiPage.Right + spacing, buttonY);
            btnFind.FlatStyle = FlatStyle.Flat;
            btnFind.FlatAppearance.BorderSize = 0;
            btnFind.Padding = new Padding(0, 10, 0, 0); // Increased padding for more spacing
            btnFind.Click += (s, e) => ShowFindDialog();
            section.Controls.Add(btnFind);

            Button btnZoomIn = new Button();
            btnZoomIn.Image = Properties.Resources.zoom_in.ToBitmap();
            btnZoomIn.Text = "Zoom In";
            btnZoomIn.TextImageRelation = TextImageRelation.ImageAboveText;
            btnZoomIn.ImageAlign = ContentAlignment.TopCenter;
            btnZoomIn.TextAlign = ContentAlignment.BottomCenter;
            btnZoomIn.Size = new Size(buttonWidth, buttonHeight);
            btnZoomIn.Location = new Point(btnFind.Right + spacing, buttonY);
            btnZoomIn.FlatStyle = FlatStyle.Flat;
            btnZoomIn.FlatAppearance.BorderSize = 0;
            btnZoomIn.Padding = new Padding(0, 10, 0, 0); // Increased padding for more spacing
            btnZoomIn.Click += (s, e) =>
            {
                browser.GetZoomLevelAsync().ContinueWith(task =>
                {
                    var currentZoom = task.Result;
                    browser.SetZoomLevel(currentZoom + 0.2);
                });
            };
            section.Controls.Add(btnZoomIn);

            Button btnZoomOut = new Button();
            btnZoomOut.Image = Properties.Resources.zoom_out.ToBitmap();
            btnZoomOut.Text = "Zoom Out";
            btnZoomOut.TextImageRelation = TextImageRelation.ImageAboveText;
            btnZoomOut.ImageAlign = ContentAlignment.TopCenter;
            btnZoomOut.TextAlign = ContentAlignment.BottomCenter;
            btnZoomOut.Size = new Size(85, buttonHeight); // Increased width for longer text
            btnZoomOut.Location = new Point(btnZoomIn.Right + spacing, buttonY);
            btnZoomOut.FlatStyle = FlatStyle.Flat;
            btnZoomOut.FlatAppearance.BorderSize = 0;
            btnZoomOut.Padding = new Padding(0, 10, 0, 0); // Increased padding for more spacing
            btnZoomOut.Click += (s, e) =>
            {
                browser.GetZoomLevelAsync().ContinueWith(task =>
                {
                    var currentZoom = task.Result;
                    browser.SetZoomLevel(currentZoom - 0.2);
                });
            };
            section.Controls.Add(btnZoomOut);

            return section;
        }

        private Panel CreateSDISection()
        {
            Panel section = new Panel();
            section.Size = new Size(150, 100);
            section.BackColor = Color.Transparent;

            // SDI label
            Label lblSdi = new Label();
            lblSdi.Text = "SDI";
            lblSdi.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblSdi.AutoSize = true;
            lblSdi.Location = new Point(0, 5);
            section.Controls.Add(lblSdi);

            // Center the label
            lblSdi.Left = (section.Width - lblSdi.Width) / 2;

            // SDI button
            Button btnSaveSdi = new Button();
            btnSaveSdi.Image = Properties.Resources.SDI.ToBitmap();
            btnSaveSdi.Text = "Save to SDI";
            btnSaveSdi.TextImageRelation = TextImageRelation.ImageAboveText;
            btnSaveSdi.ImageAlign = ContentAlignment.TopCenter;
            btnSaveSdi.TextAlign = ContentAlignment.BottomCenter;
            btnSaveSdi.Size = new Size(120, 65); // Increased height for better spacing
            btnSaveSdi.Location = new Point((section.Width - btnSaveSdi.Width) / 2, 30);
            btnSaveSdi.FlatStyle = FlatStyle.Flat;
            btnSaveSdi.FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            btnSaveSdi.Padding = new Padding(0, 8, 0, 0); // Increased padding for more spacing
            //btnSaveSdi.Click += (s, e) => { MessageBox.Show("Save to SDI clicked"); };
            section.Controls.Add(btnSaveSdi);

            return section;
        }

        private Panel CreateSeparator()
        {
            Panel separator = new Panel();
            separator.Width = 3;
            separator.Height = 90;
            separator.BackColor = Color.Gray; // Darker gray for separator
            return separator;
        }

        private void CenterSections(Panel toolbarPanel, Panel navigationSection, Panel separator1, Panel toolsSection, Panel separator2, Panel sdiSection)
        {
            if (toolbarPanel.Width == 0) return;

            int separatorSpacing = 15; // Space around separators
            int zoomOutToSeparatorSpacing = 10;
            int separator2ShiftLeft = -5; // Shift separator2 left by 5px

            int totalWidth = navigationSection.Width + separator1.Width + toolsSection.Width + separator2.Width + sdiSection.Width + (separatorSpacing * 4);
            int startX = Math.Max(10, (toolbarPanel.Width - totalWidth) / 2);

            navigationSection.Location = new Point(startX, 10);
            separator1.Location = new Point(navigationSection.Right + separatorSpacing, 20);
            toolsSection.Location = new Point(separator1.Right + separatorSpacing, 10);
            separator2.Location = new Point(toolsSection.Right + zoomOutToSeparatorSpacing + separator2ShiftLeft, 20); // shifted left
            sdiSection.Location = new Point(separator2.Right + separatorSpacing, 10);
        }
        #endregion

        private async Task LoadEditorTemplate()
        {
            try
            {
                // Use a file picker dialog to select the report file
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Title = "Select HTML Report File";
                    openFileDialog.Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*";
                    openFileDialog.FilterIndex = 1;
                    openFileDialog.RestoreDirectory = true;
                    
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string reportPath = openFileDialog.FileName;
                        
                        // Store the current report path and content for page break tracking
                        _currentReportPath = reportPath;
                        string reportHtml = File.ReadAllText(reportPath);
                        _currentReportContent = reportHtml;

                        // Load the HTML content directly
                        //browser.ShowDevTools();
                        await SetReportHtml(reportHtml);

                        // Setup the page break notification function
                        await SetupPageBreakNotificationFunction();
                        
                        // Restore page breaks from storage
                        await RestorePageBreaks();

                        // Use centralized page count update with longer delay for initial load
                        await UpdatePageCount(500);
                        return; // Exit the method after successful loading
                    }
                    
                    // If user canceled the file dialog, load the default template
                    string html = GetEditorHtmlTemplate();
                    browser.LoadHtml(html, "http://editor/");
                    MessageBox.Show("Default template loaded - no file selected");
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading template: {ex}");
                MessageBox.Show($"Error loading editor template: {ex.Message}");
            }
        }

        //renders files content into CefSharp browser
        public async Task SetReportHtml(string reportHtml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportHtml))
                {
                    MessageBox.Show("No HTML content to load.");
                    return;
                }

                // Store the current report content for page break tracking
                _currentReportContent = reportHtml;

                string cleanedReportHtml = CleanHtmlForResponsiveSvg(reportHtml);

                string template = GetEditorHtmlTemplate();

                // Insert this inside <script>...</script> tags
                string jsCode = $"<script></script>";

                string originalContent = cleanedReportHtml;
                string finalHtml = $@"<!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>Report</title>
                    <style>
                        #editor {{
                            display: flex;
                            flex-direction: column;
                            gap: 10px;
                            max-width: 900px;
                            margin: 0 auto;
                        }}

                        #editor:focus {{outline: none;
                            border: none;
                        }}

                        .page {{
                            height: auto !important;
                            padding: 20px;
                            background: #fff;
                            box-sizing: border-box;
                            box-shadow: 0 0 5px 2px #ccc;
                        }}

                        .page-break {{
                              page-break-before: always;
                              margin-top: 0 !important;
                              padding-top: 0 !important;
                              position: relative;
                              height: 20px;
                              margin: 10px 0;
                              padding: 0;
                              background-color: #f0f0f0;
                              border-top: 1px dashed #ccc;
                              border-bottom: 1px dashed #ccc;
                        }}

                        .page-break-remove {{
                              position: absolute;
                              left: 50%;
                              top: 50%;
                              transform: translate(-50%, -50%);
                              background-color: rgba(255, 255, 255, 0.9);
                              color: #333;
                              border-radius: 4px;
                              width: auto;
                              height: auto;
                              padding: 3px 8px;
                              display: none;
                              justify-content: center;
                              align-items: center;
                              font-size: 14px;
                              cursor: pointer;
                              border: 1px solid #ccc;
                              box-shadow: 0 2px 4px rgba(0,0,0,0.2);
                        }}

                        .page-break:hover .page-break-remove {{
                              display: flex;
                        }}

                        svg, svg * {{
                            max-height: 100% !important;
                            height: auto !important;
                            overflow: visible !important;
                            margin: 0 !important;
                            padding: 0 !important;
                            box-sizing: border-box !important;
                        }}

                        .svg-wrapper {{
                            break-inside: avoid;
                        }}

                        @media print {{
                            .page {{
                                page-break-after: page;
                            }}
                        }}
                    </style>
                </head>
                <body style='display: flex; justify-content: center;'>
                    <div contenteditable='true' id='editor' style='width: 100%;'>
                        <div class='page'>
                            {originalContent}
                        </div>
                    </div>
                    {jsCode}
                </body>
                </html>";

                await WaitForBrowserReady();
                browser.LoadHtml(finalHtml, "http://editor/");

                // Wait for the DOM to be fully loaded before attempting to split content
                await Task.Delay(500); // Give the browser a moment to render the DOM
                
                // Focus the browser control immediately after loading
                this.Invoke((MethodInvoker)delegate {
                    browser.Focus();
                });

                ///Add a DOM ready check before running AutoSplitInitialContent
                var domReadyScript = @"
                    (function() {
                        return document.readyState === 'complete' && !!document.getElementById('editor');
                    })();
                ";

                //Try up to 5 times with increasing delays to ensure DOM is ready
                bool domReady = false;
                for (int attempt = 0; attempt < 5 && !domReady; attempt++)
                {
                    // Check if we can execute JavaScript first
                    if (!browser.CanExecuteJavascriptInMainFrame)
                    {
                        Console.WriteLine($"V8Context not ready yet, waiting... (attempt {attempt + 1}/5)");
                        await Task.Delay(1000 * (attempt + 1)); // Wait longer for V8Context
                        continue;
                    }

                    try
                    {
                        var domReadyResult = await browser.EvaluateScriptAsync(domReadyScript);
                        if (domReadyResult.Success && domReadyResult.Result is bool ready && ready)
                        {
                            domReady = true;
                            Console.WriteLine("DOM is ready for content splitting");
                        }
                        else
                        {
                            Console.WriteLine($"DOM not ready yet, waiting... (attempt {attempt + 1}/5)");
                            await Task.Delay(500 * (attempt + 1)); // Increasing delay for each attempt
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error checking DOM readiness (attempt {attempt + 1}/5): {ex.Message}");
                        await Task.Delay(1000 * (attempt + 1));
                    }
                }

                //Automatically split the initial content into A4 - sized pages after loading
                if (domReady)
                {
                    await AutoSplitInitialContent(browser);
                    
                    // Update page count after auto-split with appropriate delay
                    await UpdatePageCount(300);

                    // Focus the browser control itself so Ctrl+F works immediately
                    this.Invoke((MethodInvoker)delegate {
                        browser.Focus();
                    });
                }
                else
                {
                    Console.WriteLine("Failed to detect DOM ready state, skipping auto-split");
                }

                // After loading the HTML, restore any saved page breaks
                //await RestorePageBreaks();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in SetReportHtml: {ex}");
                MessageBox.Show($"Unexpected error setting HTML: {ex.Message}\n\nStack Trace: {ex.StackTrace}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //Automatically split the initial content into A4 - sized pages
        private async Task AutoSplitInitialContent(ChromiumWebBrowser browser)
        {
            try
            {
                // Load all JavaScript modules first
                string allScripts = JavaScriptLoader.LoadAllScripts();
                await browser.EvaluateScriptAsync(allScripts);
                
                // Execute the auto split script
                string script = JavaScriptLoader.GetAutoSplitScript();
                await browser.EvaluateScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AutoSplitInitialContent: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

        }

        //cleans external JS, style(height, width) from section tag, and SVG tags
        public string CleanHtmlForResponsiveSvg(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
                return htmlContent;

            // 1. Remove external JS <script src="..."></script>
            string scriptSrcPattern = @"<script[^>]*\s+src\s*=\s*['""][^'""]+['""][^>]*>\s*</script>";
            htmlContent = Regex.Replace(htmlContent, scriptSrcPattern, string.Empty, RegexOptions.IgnoreCase);

            // 2. Remove width/height from <section style="...">
            string sectionStylePattern = @"(<section[^>]*style\s*=\s*[""'])([^""']*)([""'])";
            htmlContent = Regex.Replace(htmlContent, sectionStylePattern, match =>
            {
                string before = match.Groups[1].Value;
                string style = match.Groups[2].Value;
                string after = match.Groups[3].Value;

                string cleanedStyle = Regex.Replace(style, @"\b(width|height)\s*:\s*[^;]+;?", string.Empty, RegexOptions.IgnoreCase).Trim();

                return $"{before}{cleanedStyle}{after}";
            }, RegexOptions.IgnoreCase);

            // 3. Replace SVGs with responsive wrappers and inject viewBox if missing
            string svgPattern = @"<svg([^>]*)>(.*?)</svg>";
            htmlContent = Regex.Replace(htmlContent, svgPattern, match =>
            {
                string originalAttributes = match.Groups[1].Value;
                string content = match.Groups[2].Value;

                // Extract width and height for fallback viewBox
                string width = Regex.Match(originalAttributes, @"\bwidth\s*=\s*[""'](\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
                string height = Regex.Match(originalAttributes, @"\bheight\s*=\s*[""'](\d+)", RegexOptions.IgnoreCase).Groups[1].Value;

                // Remove width and height attributes
                string cleanedAttributes = Regex.Replace(originalAttributes, @"\s*(width|height)\s*=\s*[""'][^""']+[""']", "", RegexOptions.IgnoreCase).Trim();

                // Check for viewBox
                bool hasViewBox = Regex.IsMatch(cleanedAttributes, @"viewBox\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase);
                if (!hasViewBox && !string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
                {
                    cleanedAttributes += $@" viewBox=""0 0 {width} {height}""";
                }

                return $@"
                        <div style=""width: 100%; height: auto; overflow: visible;"">
                            <svg {cleanedAttributes} style=""width: 100%; height: auto; display: block;"">
                                {content}
                            </svg>
                        </div>";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return htmlContent;
        }

        public void TogglePageBreakAsync()
        {
            // Call the TogglePageBreak method asynchronously
            Task.Run(async () => await TogglePageBreak(browser));
        }

        //Inserts page break asyncronusly
        public async Task TogglePageBreak(ChromiumWebBrowser browser)
        {
            try
            {
                if (_isMultiplePageView) return;
                // Load all JavaScript modules first
                string allScripts = JavaScriptLoader.LoadAllScripts();
                await browser.EvaluateScriptAsync(allScripts);
                
                string script = JavaScriptLoader.GetTogglePageBreakScript();
                await browser.EvaluateScriptAsync(script);

                // Retrieve saved break data
                var result = await browser.EvaluateScriptAsync("window.pageBreakPositionsData");
                if (result.Success && result.Result != null)
                {
                    string breakDataJson = result.Result.ToString();
                    _pageBreakManager.SavePageBreakData(_currentReportPath, _currentReportContent, breakDataJson);
                }
                // Use centralized page count update after page break operation
                await UpdatePageCount(200);
            } catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TogglePageBreak: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
        }

        // Restore page breaks from storage after loading a file
        public async Task RestorePageBreaks()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentReportPath) || string.IsNullOrEmpty(_currentReportContent))
                {
                    System.Diagnostics.Debug.WriteLine("No report path or content provided, skipping restore.");
                    return;
                }

                PageBreakData pageBreakData = _pageBreakManager.GetPageBreakData(_currentReportPath, _currentReportContent);
                if (pageBreakData?.BreakData == null || pageBreakData.BreakData.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No page break data found, skipping restore.");
                    return;
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                };
                string breakDataJson = JsonSerializer.Serialize(pageBreakData.BreakData, serializerOptions);
                System.Diagnostics.Debug.WriteLine($"Restoring page breaks with data: {breakDataJson}");

                // Use the external JavaScript file via JavaScriptLoader
                string script = JavaScriptLoader.GetRestorePageBreaksScript(breakDataJson);
                await WaitForBrowserReady();
                var result = await browser.EvaluateScriptAsync(script);
                if (result.Success)
                {
                    System.Diagnostics.Debug.WriteLine("RestorePageBreaks script executed successfully");
                    // Update page count after restoring page breaks
                    await UpdatePageCount(300);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"RestorePageBreaks script failed: {result.Message}");
                    await Task.Delay(1000);
                    System.Diagnostics.Debug.WriteLine("Retrying RestorePageBreaks script execution...");
                    result = await browser.EvaluateScriptAsync(script);
                    System.Diagnostics.Debug.WriteLine(result.Success ? "Retry succeeded" : $"Retry failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring page breaks: {ex.Message}");
            }
        }

        private string GetEditorHtmlTemplate()
        {
            return @"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='utf-8'>
                <title>HTML Editor</title>
                <style>
                    #editor {
                        width: 100%;
                        height: auto;
                        padding: 20px;
                        background-color: #fff;
                        overflow-y: auto;
                        outline: none;
                    }

                    .page * {
                          height: auto !important;
                          min-height: 0 !important;
                          margin-top: 0 !important;
                          margin-bottom: 0 !important;
                          padding-top: 0 !important;
                          padding-bottom: 0 !important;
                          box-sizing: border-box;
                        }
    
                        .page svg,
                        .page section,
                        .componentone-specific-class,
                        .page div {
                          height: auto !important;
                          min-height: 0 !important;
                          max-height: none;
                        }

                    .page-break {
                        height: 20px;
                    }
                </style>
            </head>
            <body>
                <div id='editor' contenteditable='true'>
                    <div class='page'>
                        <p>Click inside this area to edit. Use Insert Break Mode to add page breaks.</p>
                    </div>
                </div>

                <script>
                    // Load external JavaScript files
                    window.addEventListener('DOMContentLoaded', function() {
                        // External scripts will be loaded by the browser
                        console.log('HTML Editor template loaded');
                    });
                </script>
            </body>
            </html>";
        }   
        
        public async Task<string> GetEditedHtml()
        {
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "edited_report.html");
            try
            {
                
                // 1. Capture current zoom to restore later (optional safety)
                var zoomLevel = await browser.GetZoomLevelAsync();

                // 2. Define JavaScript cleanup script as a single string
                string cleanupScript = @"
                (function() {
                    const editor = document.getElementById('editor');

                    // Clean up nested .page elements
                    document.querySelectorAll('.page .page').forEach(nested => {
                        const parent = nested.closest('.page');
                        if (parent) {
                            const grandParent = parent.parentNode;
                            const nextSibling = parent.nextSibling;
                            while (parent.firstChild) {
                                grandParent.insertBefore(parent.firstChild, nextSibling);
                            }
                            grandParent.removeChild(parent);
                        }
                    });

                    // Flatten .page elements into one container
                    const flat = document.createElement('div');
                    flat.id = 'editor';
                    flat.style.width = '100%';

                    const pages = editor.querySelectorAll('.page');
                    pages.forEach((p, index) => {
                        flat.appendChild(p.cloneNode(true));
                        if (index < pages.length - 1) {
                            const breakEl = document.createElement('div');
                            breakEl.className = 'page-break';
                            flat.appendChild(breakEl);
                        }
                    });

                    // Replace editor contents only, not the node
                    editor.innerHTML = flat.innerHTML;

                    // Estimate SVG height
                    function estimateSvgHeight(svg) {
                        const parseAttr = (el, attr) => parseFloat(el.getAttribute(attr)) || 0;
                        let maxBottom = 0;

                        svg.querySelectorAll('text').forEach(el => {
                            let fontSize = parseFloat(getComputedStyle(el).fontSize) || 16;
                            let lines = el.querySelectorAll('tspan').length || el.textContent.split('\\n').length;
                            const y = parseAttr(el, 'y');
                            maxBottom = Math.max(maxBottom, y + lines * fontSize * 1.2);
                        });

                        svg.querySelectorAll('rect').forEach(el => {
                            const y = parseAttr(el, 'y');
                            const h = parseAttr(el, 'height');
                            maxBottom = Math.max(maxBottom, y + h);
                        });

                        svg.querySelectorAll('line').forEach(el => {
                            const y1 = parseAttr(el, 'y1');
                            const y2 = parseAttr(el, 'y2');
                            maxBottom = Math.max(maxBottom, Math.max(y1, y2));
                        });

                        return maxBottom + 10;
                    }

                    // Update SVG viewBox and height
                    document.querySelectorAll('svg').forEach(svg => {
                        try {
                            const width = (svg.viewBox && svg.viewBox.baseVal && svg.viewBox.baseVal.width) || svg.clientWidth || 100;
                            const estHeight = estimateSvgHeight(svg);
                            svg.removeAttribute('height');
                            svg.removeAttribute('width');
                            svg.style.height = estHeight + 'px';
                            svg.style.width = '100%';
                            svg.setAttribute('viewBox', '0 0 ' + width + ' ' + estHeight);
                        } catch (e) {
                            console.warn('SVG fix error:', e);
                        }
                    });

                    // Expose cleaned HTML
                    window.getHtmlContent = function() {
                        const editor = document.getElementById('editor');
                        return editor ? editor.innerHTML : '';
                    };
                })();
                ";

                // 3. Evaluate cleanup JS
                await browser.EvaluateScriptAsync(cleanupScript);

                // 4. Fetch cleaned HTML string
                var getInner = await browser.EvaluateScriptAsync("getHtmlContent();");
                var innerHtml = getInner.Success ? getInner.Result?.ToString() : "";

                // 5. Wrap in full HTML for saving
                string finalHtml = $@"<!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <title>Edited Report</title>
                    <style>
                        #editor {{
                            display: flex;
                            flex-direction: column;
                            gap: 10px;
                            max-width: 800px;
                            margin: 0 auto;
                        }}
                        .page {{
                            padding: 20px;
                            background: #fff;
                            box-shadow: 0 0 5px 2px #ccc;
                        }}
                        .page-break {{
                            page-break-before: always;
                            margin-top: 0;
                            padding-top: 0;
                        }}
                        svg, svg * {{
                            max-height: 100% !important;
                            height: auto !important;
                            overflow: visible !important;
                        }}
                        .svg-wrapper {{
                            break-inside: avoid;
                        }}
                        @media print {{
                            .page {{
                                page-break-after: page;
                            }}
                        }}
                    </style>
                </head>
                <body>
                    <div id='editor'>
                        {innerHtml}
                    </div>
                </body>
                </html>";

                // 6. Restore previous zoom level (if needed)
                browser.SetZoomLevel(zoomLevel);
                File.WriteAllText(filePath, finalHtml);
                MessageBox.Show($"Edited HTML saved to {filePath}");
                return finalHtml;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetEditedHtml error: {ex}");
                return "";
            }
        }

        private async Task WaitForBrowserReady()
        {
            // Wait for the browser to be ready to execute scripts
            for (int i = 0; i < 20; i++)
            {
                if (browser.CanExecuteJavascriptInMainFrame)
                {
                    // Additional check to ensure DOM is ready
                    try
                    {
                        var domReadyResult = await browser.EvaluateScriptAsync("document.readyState");
                        if (domReadyResult.Success)
                        {
                            var readyState = domReadyResult.Result?.ToString();
                            System.Diagnostics.Debug.WriteLine($"DOM ready state: {readyState}");
                            
                            if (readyState == "complete" || readyState == "interactive")
                            {
                                System.Diagnostics.Debug.WriteLine("Browser is fully ready for script execution");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking DOM ready state: {ex.Message}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Waiting for browser to be ready (attempt {i+1}/20)...");
                await Task.Delay(500); // Wait 500ms between checks
            }
            
            System.Diagnostics.Debug.WriteLine("WARNING: Browser readiness timeout - proceeding anyway");
            // Instead of throwing exception, we'll try to continue
            // throw new TimeoutException("Browser was not ready to execute scripts within the timeout period.");
        }

        // Verify that the JavaScript bridge is accessible from JavaScript
        private async Task VerifyJavaScriptBridge()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Verifying JavaScript bridge accessibility...");
                
                var bridgeCheckScript = @"
                    (function() {
                        console.log('Checking for JavaScript bridge...');
                        
                        // Check if javaScriptBridge is available
                        if (typeof javaScriptBridge !== 'undefined' && javaScriptBridge !== null) {
                            console.log('javaScriptBridge found directly');
                            window.javaScriptBridge = javaScriptBridge;
                            return 'direct';
                        }
                        
                        // Check if it's available on window
                        if (typeof window.javaScriptBridge !== 'undefined' && window.javaScriptBridge !== null) {
                            console.log('javaScriptBridge found on window');
                            return 'window';
                        }
                        
                        console.error('javaScriptBridge not found - checking CefSharp bound objects');
                        
                        // Check if CefSharp has bound the object
                        if (typeof CefSharp !== 'undefined' && CefSharp.BindObjectAsync) {
                            console.log('CefSharp found, attempting to bind javaScriptBridge');
                            CefSharp.BindObjectAsync('javaScriptBridge');
                            return 'binding';
                        }
                        
                        return 'not_found';
                    })();
                ";

                var result = await browser.EvaluateScriptAsync(bridgeCheckScript);
                if (result.Success)
                {
                    string bridgeStatus = result.Result?.ToString() ?? "unknown";
                    System.Diagnostics.Debug.WriteLine($"JavaScript bridge status: {bridgeStatus}");
                    
                    if (bridgeStatus == "binding" || bridgeStatus == "not_found")
                    {
                        System.Diagnostics.Debug.WriteLine("Attempting to manually bind JavaScript bridge...");
                        
                        // Wait a moment for binding to complete
                        await Task.Delay(1000);
                        
                        // Try again to make it available
                        var retryScript = @"
                            (function() {
                                if (typeof javaScriptBridge !== 'undefined') {
                                    window.javaScriptBridge = javaScriptBridge;
                                    console.log('Successfully bound javaScriptBridge to window');                                    
                                    return true;
                                }
                                console.error('javaScriptBridge still not available after binding attempt');
                                return false;
                            })();
                        ";
                        
                        var retryResult = await browser.EvaluateScriptAsync(retryScript);
                        if (retryResult.Success && retryResult.Result is bool success && success)
                        {
                            System.Diagnostics.Debug.WriteLine("JavaScript bridge successfully bound after retry");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to bind JavaScript bridge after retry");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to verify JavaScript bridge: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying JavaScript bridge: {ex.Message}");
            }
        }

        // Setup the notification function in JavaScript to handle page break position updates
        private async Task SetupPageBreakNotificationFunction()
        {
            // Load all JavaScript modules first
            string allScripts = JavaScriptLoader.LoadAllScripts();
            await browser.EvaluateScriptAsync(allScripts);
            
            // Setup notification function
            string notificationScript = JavaScriptLoader.GetPageBreakNotificationScript();
            await browser.EvaluateScriptAsync(notificationScript);
        }

        // Check if page break positions have been updated in JavaScript
        public async Task CheckForPageBreakPositionUpdates()
        {
            if (browser == null || !browser.IsBrowserInitialized || !browser.CanExecuteJavascriptInMainFrame)
            {
                System.Diagnostics.Debug.WriteLine("Browser not ready for script execution");
                return;
            }

            try
            {
                // Check if the pageBreakPositionsData variable exists and has data
                var result = await browser.EvaluateScriptAsync(
                    "(function() { return window.pageBreakPositionsData || null; })();");
                if (result.Success && result.Result != null && result.Result.ToString() != "null")
                {
                    string positionsJson = result.Result.ToString();
                    // Use a flag to track if we've already processed this data instead of clearing it
                    var flagResult = await browser.EvaluateScriptAsync("window.pageBreakPositionsProcessed || false");
                    bool alreadyProcessed = flagResult.Success && flagResult.Result != null &&
                                          (bool.TryParse(flagResult.Result.ToString(), out bool processed) ? processed : false);

                    if (!alreadyProcessed)
                    {
                        // Mark as processed to avoid duplicate processing
                        await browser.EvaluateScriptAsync("window.pageBreakPositionsProcessed = true;");
                        // Process the positions
                        SavePageBreakPositions(positionsJson);
                    }
                }
                else
                {
                    // No need to log this every time
                    // System.Diagnostics.Debug.WriteLine("No page break positions found in JavaScript");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for page break positions: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Save page break positions to storage
        public void SavePageBreakPositions(string positionsJson)
        {
            try
            {                
                // Check if we have the necessary information to save page breaks
                if (string.IsNullOrEmpty(_currentReportPath) || _currentReportContent == null)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot save page break positions: missing report path or content");
                    return;
                }
                
                // The positionsJson coming from JS is the full break data array (with anchorHash, splitIndex, context, etc.).
                // Forward it directly to SavePageBreakData so we preserve all fields instead of truncating to just offsets.
                if (positionsJson.StartsWith("\"") && positionsJson.EndsWith("\""))
                {
                    positionsJson = positionsJson.Substring(1, positionsJson.Length - 2);
                }
                _pageBreakManager.SavePageBreakData(_currentReportPath, _currentReportContent, positionsJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SavePageBreakPositions: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Method called by the improved JavaScript bridge
        public bool SavePageBreakPositionsFromJS(string positionsJson)
        {
            try
            {
                SavePageBreakPositions(positionsJson);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SavePageBreakPositionsFromJS: {ex.Message}");
                return false;
            }
        }

        // Toggle between single page view and multiple page view
        public async Task ToggleMultiplePageView()
        {
            try
            {
                // Use current state before toggling
                bool showMultipleView = !_isMultiplePageView;
                _isMultiplePageView = showMultipleView;

                // Update button label to show the target view (next action)
                // If it will be single page after this click, show "Single Page", else "Multiple Page".
                if (_multiplePageViewButton != null)
                {
                    // Ensure UI thread update
                    if (this.InvokeRequired)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            _multiplePageViewButton.Text = _isMultiplePageView ? "Single Page" : "Multiple Page";
                            _multiplePageViewButton.Image = _isMultiplePageView ? _iconSinglePage : _iconMultiplePage;
                            _multiplePageViewButton.Refresh();
                        });
                    }
                    else
                    {
                        _multiplePageViewButton.Text = _isMultiplePageView ? "Single Page" : "Multiple Page";
                        _multiplePageViewButton.Image = _isMultiplePageView ? _iconSinglePage : _iconMultiplePage;
                        _multiplePageViewButton.Refresh();
                    }
                }
                
                var script = $@"
                (function() {{
                    const editor = document.getElementById('editor');
                    if (!editor) return;
                    
                    // Remove any existing navigation controls first
                    const existingNavControls = document.getElementById('page-nav-controls');
                    if (existingNavControls) {{
                        existingNavControls.remove();
                    }}
                    
                    // Get all pages and page breaks
                    const pages = Array.from(editor.querySelectorAll('.page'));
                    const pageBreaks = Array.from(editor.querySelectorAll('.page-break'));
                    
                    if ({showMultipleView.ToString().ToLower()}) {{
                        // Multiple page view - show pages side by side (2 columns)
                        console.log('Switching to multiple page view with', pages.length, 'pages');
                        
                        // Reset editor styles
                        editor.style.display = 'block';
                        editor.style.maxWidth = 'none';
                        editor.style.margin = '0';
                        editor.style.padding = '20px';
                        
                        // Create main container
                        const mainContainer = document.createElement('div');
                        mainContainer.style.maxWidth = '1500px';
                        mainContainer.style.margin = '0 auto';
                        mainContainer.style.display = 'flex';
                        mainContainer.style.flexDirection = 'column';
                        mainContainer.style.gap = '30px';
                        
                        // Hide page breaks in multiple view and preserve them
                        pageBreaks.forEach(pageBreak => {{
                            pageBreak.style.display = 'none';
                        }});
                        
                        // Group pages in pairs and create rows
                        for (let i = 0; i < pages.length; i += 2) {{
                            const rowDiv = document.createElement('div');
                            rowDiv.style.display = 'flex';
                            rowDiv.style.gap = '20px';
                            rowDiv.style.justifyContent = 'center';
                            rowDiv.style.alignItems = 'flex-start';
                            
                            // Left page
                            const leftPage = pages[i];
                            if (leftPage) {{
                                leftPage.style.display = 'block';
                                leftPage.style.width = '80%';
                                leftPage.style.minWidth = '300px';
                                leftPage.style.boxShadow = '0 4px 8px rgba(0,0,0,0.1)';
                                leftPage.style.border = '1px solid #ddd';
                                leftPage.style.backgroundColor = 'white';
                                leftPage.style.margin = '0';
                                leftPage.style.flex = '1';
                                
                                // Remove from original position and add to row
                                if (leftPage.parentNode) leftPage.parentNode.removeChild(leftPage);
                                rowDiv.appendChild(leftPage);
                            }}
                            
                            // Right page
                            const rightPage = pages[i + 1];
                            if (rightPage) {{
                                rightPage.style.display = 'block';
                                rightPage.style.width = '80%';
                                rightPage.style.minWidth = '300px';
                                rightPage.style.boxShadow = '0 4px 8px rgba(0,0,0,0.1)';
                                rightPage.style.border = '1px solid #ddd';
                                rightPage.style.backgroundColor = 'white';
                                rightPage.style.margin = '0';
                                rightPage.style.flex = '1';
                                
                                // Remove from original position and add to row
                                if (rightPage.parentNode) rightPage.parentNode.removeChild(rightPage);
                                rowDiv.appendChild(rightPage);
                            }} else {{
                                // Add spacer for odd number of pages
                                const spacer = document.createElement('div');
                                spacer.style.width = '50%';
                                spacer.style.flex = '1';
                                rowDiv.appendChild(spacer);
                            }}
                            
                            mainContainer.appendChild(rowDiv);
                        }}
                        
                        // Clear editor and add new layout, but preserve page breaks
                        editor.innerHTML = '';
                        editor.appendChild(mainContainer);
                        
                        // Re-add hidden page breaks to preserve them for single view toggle
                        pageBreaks.forEach(pageBreak => {{
                            editor.appendChild(pageBreak);
                        }});
                        
                        // Scroll to top when switching to multiple page view
                        window.scrollTo(0, 0);
                        
                    }} else {{
                        
                        // Reset editor to normal view styles
                        editor.style.display = 'flex';
                        editor.style.flexDirection = 'column';
                        editor.style.gap = '10px';
                        editor.style.maxWidth = '900px';
                        editor.style.margin = '0 auto';
                        editor.style.padding = '20px';
                        
                        // Show page breaks in single view (they were just hidden, not removed)
                        pageBreaks.forEach(pageBreak => {{
                            pageBreak.style.display = 'block';
                        }});
                        
                        // Show all pages in normal scrollable layout
                        pages.forEach(page => {{
                            page.style.display = 'block';
                            page.style.width = 'auto';
                            page.style.maxWidth = 'none';
                            page.style.minWidth = 'auto';
                            page.style.flex = 'none';
                            page.style.marginBottom = '20px';
                            page.style.boxShadow = '0 2px 10px rgba(0,0,0,0.1)';
                            page.style.border = '1px solid #ddd';
                            page.style.backgroundColor = 'white';
                            page.style.margin = '0 0 20px 0';
                        }});
                        
                        // Restore original DOM structure for normal scrollable view
                        const originalContainer = document.createElement('div');
                        pages.forEach((page, index) => {{
                            if (page.parentNode) page.parentNode.removeChild(page);
                            originalContainer.appendChild(page);
                            
                            // Add page break after each page except the last
                            if (index < pages.length - 1 && pageBreaks[index]) {{
                                if (pageBreaks[index].parentNode) pageBreaks[index].parentNode.removeChild(pageBreaks[index]);
                                originalContainer.appendChild(pageBreaks[index]);
                            }}
                        }});
                        
                        editor.innerHTML = '';
                        editor.appendChild(originalContainer);
                    }}
                }})();
                ";
                
                await browser.EvaluateScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TogglePageView: {ex.Message}");
                MessageBox.Show($"Error toggling page view: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NavigateToPage(int pageNumber)
        {
            string script = $@"
            (function() {{
                var pages = document.querySelectorAll('.page');
                if (pages.length >= {pageNumber}) {{
                    var y = pages[{pageNumber - 1}].offsetTop;
                    window.scrollTo({{ top: y - 20, behavior: 'smooth' }}); // 👈 add 20px offset
                }}
            }})();
            ";

            browser.ExecuteScriptAsync(script);
        }

        private void UpdatePageLabel(Label label)
        {
            label.Text = $"Page {currentPage} of {totalPages}";
        }

        // Centralized method to update page count with proper timing
        private async Task UpdatePageCount(int delayMs = 100)
        {
            if (_isUpdatingPageCount) return; // Prevent concurrent updates
            _isUpdatingPageCount = true;

            try
            {
                // Wait for DOM to settle
                await Task.Delay(delayMs);

                // Retry mechanism for page count
                int maxRetries = 3;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        string pageScript = @"
                            (function() {
                                var pages = document.querySelectorAll('.page');
                                return pages.length;
                            })();
                        ";

                        var res = await browser.EvaluateScriptAsync(pageScript);
                        if (res.Success && res.Result != null)
                        {
                            int newTotalPages = Convert.ToInt32(res.Result);

                            // Only update if the count has changed or this is the first update
                            if (newTotalPages != totalPages || totalPages == 0)
                            {
                                totalPages = newTotalPages;

                                // Ensure current page is within bounds
                                if (currentPage > totalPages && totalPages > 0)
                                {
                                    currentPage = totalPages;
                                }
                                else if (currentPage < 1)
                                {
                                    currentPage = 1;
                                }

                                // Update UI on main thread
                                if (pageNumberLabel != null)
                                {
                                    pageNumberLabel.Invoke((Action)(() =>
                                    {
                                        pageNumberLabel.Text = $"Page {currentPage} of {totalPages}";
                                    }));
                                }

                                System.Diagnostics.Debug.WriteLine($"Page count updated: {totalPages} pages, current: {currentPage}");
                            }
                            break; // Success, exit retry loop
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Page count script failed (attempt {retry + 1}): {res.Message}");
                            if (retry < maxRetries - 1)
                            {
                                await Task.Delay(200 * (retry + 1)); // Increasing delay
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating page count (attempt {retry + 1}): {ex.Message}");
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(200 * (retry + 1));
                        }
                    }
                }
            }
            finally
            {
                _isUpdatingPageCount = false;
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+F to show find dialog
            if (e.Control && e.KeyCode == Keys.F)
            {
                ShowFindDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            // Handle Escape to close find dialog if it's visible
            else if (e.KeyCode == Keys.Escape && findControl.Visible)
            {
                findControl.HideFind();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void ShowFindDialog()
        {
            // Make sure the find control is visible and properly positioned
            findControl.BringToFront();
            findControl.ShowFind();

            // Log that the find dialog was shown
            System.Diagnostics.Debug.WriteLine("Find dialog shown");
        }

        public void HideFindDialogIfVisible()
        {
            if (findControl.Visible)
            {
                findControl.HideFind();
                // Cancel any ongoing find operation
                if (browser != null && browser.IsBrowserInitialized)
                {
                    browser.GetBrowser().GetHost().StopFinding(true);
                    findControl.ClearResults();
                }
            }
        }

        private void FindControl_FindNext(object sender, FindEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.SearchText))
            {
                ExecuteFind(e.SearchText, e.Forward, e.MatchCase, true);
            }
        }

        private void FindControl_FindPrevious(object sender, FindEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.SearchText))
            {
                ExecuteFind(e.SearchText, e.Forward, e.MatchCase, true);
            }
        }

        private void FindControl_CloseFind(object sender, EventArgs e)
        {
            findControl.HideFind();
            // Cancel any ongoing find operation
            if (browser != null && browser.IsBrowserInitialized)
            {
                browser.GetBrowser().GetHost().StopFinding(true);
                findControl.ClearResults();
            }
        }

        private void ExecuteFind(string searchText, bool forward, bool matchCase, bool findNext)
        {
            if (browser != null && browser.IsBrowserInitialized)
            {
                var host = browser.GetBrowser().GetHost();
                host.Find(searchText, forward, matchCase, findNext);
            }
        }

        /// <summary>
        /// Updates the find results display with the current search status
        /// </summary>
        /// <param name="identifier">The search identifier</param>
        /// <param name="count">Total number of matches found</param>
        /// <param name="activeMatchOrdinal">Current match index (0-based)</param>
        /// <param name="finalUpdate">Whether this is the final update for the current search</param>
        public void UpdateFindResults(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
        {
            // Only update UI if this is the final update or we have results to show
            if (finalUpdate || count > 0)
            {
                findControl.UpdateResults(count, activeMatchOrdinal);
            }
        }

        private async Task PrintHtmlContent()
        {
            try
            {
                // Get the clean HTML content from the editor first
                string cleanHtml = await GetCleanHtmlForPrinting();

                if (string.IsNullOrEmpty(cleanHtml))
                {
                    MessageBox.Show("No content available for printing.", "Print Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Generate PDF directly without showing print dialog first
                string tempPdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"HTMLEditor_Print_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                // Create a temporary form to host the browser (required for proper initialization)
                using (var tempForm = new Form())
                {
                    tempForm.WindowState = FormWindowState.Minimized;
                    tempForm.ShowInTaskbar = false;
                    tempForm.Size = new Size(1024, 768);

                    // Create browser instance
                    var printBrowser = new ChromiumWebBrowser();
                    printBrowser.Size = new Size(1000, 700);
                    printBrowser.Dock = DockStyle.Fill;
                    tempForm.Controls.Add(printBrowser);

                    // Show form (required for browser initialization)
                    tempForm.Show();
                    tempForm.Hide(); // Hide immediately

                    try
                    {
                        // Wait for browser to initialize properly
                        int maxWaitTime = 10000; // 10 seconds max
                        int waitTime = 0;
                        while (!printBrowser.IsBrowserInitialized && waitTime < maxWaitTime)
                        {
                            await Task.Delay(100);
                            waitTime += 100;
                            System.Windows.Forms.Application.DoEvents(); // Allow UI updates
                        }

                        if (!printBrowser.IsBrowserInitialized)
                        {
                            MessageBox.Show("Failed to initialize browser for printing.", "Print Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // Load HTML content
                        printBrowser.LoadHtml(cleanHtml, "http://print-temp/");

                        // Wait for content to load completely
                        bool contentLoaded = false;
                        var loadEndHandler = new EventHandler<FrameLoadEndEventArgs>((sender, args) =>
                        {
                            if (args.Frame.IsMain)
                                contentLoaded = true;
                        });

                        printBrowser.FrameLoadEnd += loadEndHandler;

                        // Wait for load to complete
                        waitTime = 0;
                        while (!contentLoaded && waitTime < maxWaitTime)
                        {
                            await Task.Delay(100);
                            waitTime += 100;
                            System.Windows.Forms.Application.DoEvents();
                        }

                        printBrowser.FrameLoadEnd -= loadEndHandler;

                        if (!contentLoaded)
                        {
                            MessageBox.Show("Content failed to load for printing.", "Print Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // Increased wait for full rendering/layout (breaks need time to compute)
                        await Task.Delay(3000); // 3 seconds
                        System.Windows.Forms.Application.DoEvents();

                        // Configure PDF print settings - FIXED: Increased top/bottom margins to 1.0 inch for padding
                        var printSettings = new PdfPrintSettings()
                        {
                            MarginType = CefPdfPrintMarginType.Custom,
                            MarginTop = 5.0,
                            MarginBottom = 1.0,
                            MarginLeft = 0,
                            MarginRight = 0,
                            PageRanges = "",
                            DisplayHeaderFooter = false,
                            PrintBackground = true,
                            Landscape = false,
                            Scale = 1.0 // Slight scale to fit content without overflows
                        };

                        System.Diagnostics.Debug.WriteLine($"Attempting to generate PDF: {tempPdfPath}");

                        // Generate PDF
                        bool success = await printBrowser.PrintToPdfAsync(tempPdfPath, printSettings);

                        System.Diagnostics.Debug.WriteLine($"PDF generation result: {success}");

                        if (success && File.Exists(tempPdfPath))
                        {
                            // Add simple page count validation (non-blank pages)
                            FileInfo pdfInfo = new FileInfo(tempPdfPath);
                            if (pdfInfo.Length < 10000) // Rough check for "empty-ish" file
                            {
                                MessageBox.Show("PDF generated but appears too small/empty. Check logs.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                            System.Diagnostics.Debug.WriteLine($"PDF size: {pdfInfo.Length} bytes");

                            // Show print dialog for the generated PDF
                            using (PrintDialog printDialog = new PrintDialog())
                            {
                                printDialog.UseEXDialog = true;
                                printDialog.AllowPrintToFile = true;

                                var result = MessageBox.Show(
                                    $"PDF generated successfully at:\n{tempPdfPath}\n\nWould you like to open it for printing?",
                                    "Print Ready",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);

                                if (result == DialogResult.Yes)
                                {
                                    // Open the PDF with default PDF viewer
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                                    {
                                        FileName = tempPdfPath,
                                        UseShellExecute = true
                                    });
                                }
                            }
                        }
                        else
                        {
                            string errorMsg = "Failed to generate PDF for printing.";
                            if (!File.Exists(tempPdfPath))
                                errorMsg += " PDF file was not created.";

                            System.Diagnostics.Debug.WriteLine(errorMsg);
                            MessageBox.Show(errorMsg, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    finally
                    {
                        // Clean up browser
                        printBrowser?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrintHtmlContent: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error printing content: {ex.Message}", "Print Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task<string> GetCleanHtmlForPrinting()
        {
            try
            {
                // Updated JS: Remove pageBreakInside (conflicts with breaks). Add explicit page-break-after: always only for non-last pages.
                // Also, set explicit height on pages to match A4 (842pt) to prevent overflows causing blanks.
                string extractScript = @"
        (function() {
            const editor = document.getElementById('editor');
            if (!editor) return '';
            
            // Clone the editor content to avoid modifying the original
            const clone = editor.cloneNode(true);
            
            // Remove ALL page break elements completely (any element with 'page-break' class)
            const allPageBreaks = Array.from(clone.querySelectorAll('[class*=""page-break""]'));
            console.log('Removing page break elements:', allPageBreaks.length);
            allPageBreaks.forEach(pageBreak => {
                pageBreak.remove();
            });
            
            // Get only page elements that have actual content
            const pages = Array.from(clone.querySelectorAll('.page'));
            console.log('Total pages found:', pages.length);
            
            // Create a new container for the cleaned content
            const cleanContainer = document.createElement('div');
            let pageCount = 0;
            let hasPreviousPage = false;
            
            pages.forEach((page, index) => {
                // Check if page has actual content (not just whitespace)
                const pageText = page.textContent.trim();
                const hasImages = page.querySelectorAll('img, svg').length > 0;
                const hasOtherContent = page.querySelectorAll('div, p, span, table, ul, ol').length > 0;
                
                if (pageText.length > 0 || hasImages || hasOtherContent) {
                    // This page has content, include it
                    pageCount++;
                    hasPreviousPage = true;
                    
                    // Clean the page styling for print - FIXED: Remove pageBreakInside to avoid conflicts.
                    // Add page-break-after: always only if not the last page to chain without extra blanks.
                    page.style.pageBreakAfter = (index < pages.length - 1) ? 'always' : 'auto';
                    page.style.pageBreakBefore = 'auto'; // Rely on after from previous
                    page.style.margin = '0';
                    page.style.padding = '20px';
                    page.style.boxShadow = 'none';
                    page.style.border = 'none';
                    page.style.background = 'white';
                    // FIXED: Cap height to ~A4 to prevent single-page overflow (842pt = A4 height).
                    page.style.maxHeight = '842pt';
                    page.style.overflow = 'visible'; // Allow content to flow if needed, but breaks will handle.
                    
                    // Remove any nested page break elements within the page
                    const nestedPageBreaks = Array.from(page.querySelectorAll('[class*=""page-break""]'));
                    nestedPageBreaks.forEach(nested => nested.remove());
                    
                    cleanContainer.appendChild(page.cloneNode(true));
                    console.log('Added page', pageCount, 'with content length:', pageText.length);
                } else {
                    console.log('Skipped empty page at index:', index);
                }
            });
            
            console.log('Final pages with content for print:', pageCount);
            
            return cleanContainer.innerHTML;
        })();
        ";

                var result = await browser.EvaluateScriptAsync(extractScript);
                if (result.Success && result.Result != null)
                {
                    string contentHtml = result.Result.ToString();

                    // Updated CSS: Remove page-break-inside: avoid (causes blanks). Add widows/orphans to minimize bad breaks.
                    // Ensure body flows continuously with breaks only where specified.
                    string printHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Print Document</title>
    <style>
        @page {{
            size: A4;
            margin: 0;
        }}
        
        body {{
            font-family: 'Segoe UI', Arial, sans-serif;
            margin: 0;
            padding: 0;
            background: white;
            color: black;
        }}
        
        .page {{
            margin: 0;
            padding: 20px;
            background: white;
            box-shadow: none !important;
            border: none !important;
            /* FIXED: Removed page-break-inside: avoid to prevent extra blanks on overflows */
            max-height: 842pt; /* A4 height in points */
            widows: 3; /* Min lines before/after break */
            orphans: 3;
        }}
        
        /* Ensure SVGs print properly */
        svg {{
            max-width: 100%;
            height: auto;
        }}
        
        /* Remove any interactive elements for print */
        .page-break-remove,
        .find-highlight,
        [class*='page-break'] {{
            display: none !important;
        }}
        
        /* Ensure no extra spacing that could cause empty pages */
        * {{
            box-sizing: border-box;
        }}
        
        /* FIXED: Global rule to avoid unwanted breaks in children */
        div, p, table, ul, ol {{
            page-break-inside: auto;
        }}
    </style>
</head>
<body>
    {contentHtml}
</body>
</html>";

                    return printHtml;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting clean HTML for printing: {ex.Message}");
                return string.Empty;
            }
        }

    }

    // Direct page break handler - inserts page break on right-click without showing context menu
    public class DirectPageBreakMenuHandler : IContextMenuHandler
    {
        private readonly Form1 _parentForm;

        public DirectPageBreakMenuHandler(Form1 parentForm)
        {
            _parentForm = parentForm;
        }

        public void OnBeforeContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        {
            try
            {
                // Check if the right-click occurred inside the editor div
                bool isInsideEditor = parameters.IsEditable;
                
                if (isInsideEditor)
                {
                    // Inside editor - clear menu and insert page break directly
                    model.Clear(); // Clear the menu so it doesn't show
                    
                    // Trigger page break insertion immediately
                    _parentForm.TogglePageBreakAsync();
                }
                else
                {
                    // Outside editor - keep browser's default menu
                    System.Diagnostics.Debug.WriteLine("Keeping default menu - outside editor");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnBeforeContextMenu: {ex.Message}");
            }
        }

        public bool OnContextMenuCommand(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags)
        {
            // We don't need to handle menu commands since we're not showing a menu
            return false;
        }

        public void OnContextMenuDismissed(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            // Not used in this implementation
        }

        public bool RunContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback)
        {
            // Return false to allow the default context menu to be displayed
            return false;
        }
    }

    public class CustomToolbarColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(211, 211, 211); } }
        public override Color MenuItemSelected { get { return Color.LightGray; } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(211, 211, 211); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(211, 211, 211); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(211, 211, 211); } }
    }

}


