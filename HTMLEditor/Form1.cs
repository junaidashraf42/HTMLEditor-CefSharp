using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.IO;
using CefSharp;
using CefSharp.WinForms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Drawing.Printing;
using System.Text.RegularExpressions;

namespace HTMLEditor
{
    public partial class Form1 : Form
    {
        private ChromiumWebBrowser browser;
        private System.Windows.Forms.Button insertBreakButton, getHtmlButton;
        private ContextMenuStrip editorContextMenu;

        public Form1()
        {
            Text = "HTML Editor";
            //Width = 1000;
            //Height = 700;
            this.WindowState = FormWindowState.Maximized;

            //insertBreakButton = new System.Windows.Forms.Button { Text = "Insert Page Break", Dock = DockStyle.Top, Height = 30 };
            //insertBreakButton.Click += async (s, e) => await InsertPageBreak(browser);

            getHtmlButton = new System.Windows.Forms.Button { Text = "Export Edited HTML", Dock = DockStyle.Top, Height = 30 };
            getHtmlButton.Click += async (s, e) =>
            {
                string html = await GetEditedHtml();
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "edited_report.html");
                File.WriteAllText(filePath, html);
                MessageBox.Show($"Edited HTML saved to {filePath}");
            };

            browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };

            // Initialize and configure the context menu
            editorContextMenu = new ContextMenuStrip();
            var addPageBreakItem = new ToolStripMenuItem("Add page break");
            addPageBreakItem.Click += async (s, e) => await InsertPageBreak(browser);
            editorContextMenu.Items.Add(addPageBreakItem);
            
            // Set up the context menu on right-click
            browser.MenuHandler = new CustomMenuHandler(this);

            Controls.Add(browser);
            Controls.Add(getHtmlButton);
            //Controls.Add(insertBreakButton);

            // Kept for designer compatibility
            Load += async (s, e) => await LoadEditorTemplate();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await LoadEditorTemplate();
        }

        //loads file from the specified file path
        private async Task LoadEditorTemplate()
        {
            try
            {
                // Try to load a specific report file directly
                string reportPath = @"C:\Users\jashraf\Downloads\Documentation_Exceptions By Customer1.html";
                if (File.Exists(reportPath))
                {
                    string reportHtml = File.ReadAllText(reportPath);

                    // Load the HTML content directly
                    await SetReportHtml(reportHtml);
                }
                else
                {
                    // Load default template if file not found
                    string html = GetEditorHtmlTemplate();
                    browser.LoadHtml(GetEditorHtmlTemplate(), "http://editor/");
                    //browser.LoadHtml(html, "http://example/");
                    MessageBox.Show("Default template loaded - report file not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading template: {ex}");
                MessageBox.Show($"Error loading editor template: {ex.Message}");
            }
        }

        //renders files content into CefSharo browser
        public async Task SetReportHtml(string reportHtml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportHtml))
                {
                    MessageBox.Show("No HTML content to load.");
                    return;
                }

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in SetReportHtml: {ex}");
                MessageBox.Show($"Unexpected error setting HTML: {ex.Message}\n\nStack Trace: {ex.StackTrace}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        public void InsertPageBreakAsync()
        {
            // Call the InsertPageBreak method asynchronously
            Task.Run(async () => await InsertPageBreak(browser));
        }

        //Inserts page break asyncronusly
        public async Task InsertPageBreak(ChromiumWebBrowser browser)
        {
            var script = @"
            (function () {
                const editor = document.getElementById('editor');
                if (!editor || !window.getSelection) return;

                // Flatten nested .page containers
                document.querySelectorAll('.page .page').forEach(nested => {
                    const outer = nested.closest('.page');
                    if (outer) {
                        while (nested.firstChild) {
                            outer.parentNode.insertBefore(nested.firstChild, outer);
                        }
                        outer.remove();
                    }
                });

                const allPages = Array.from(editor.children).filter(c => c.classList.contains('page'));

                const sel = window.getSelection();
                if (!sel.rangeCount) return;
                const range = sel.getRangeAt(0);

                let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
                if (pageIndex < 0) pageIndex = 0;
                const targetPage = allPages[pageIndex];

                // Ensure target page is split cleanly
                const beforeRange = document.createRange();
                beforeRange.setStart(targetPage, 0);
                beforeRange.setEnd(range.startContainer, range.startOffset);
                const beforeFrag = beforeRange.extractContents();

                const afterRange = document.createRange();
                afterRange.setStart(range.endContainer, range.endOffset);
                afterRange.setEnd(targetPage, targetPage.childNodes.length);
                const afterFrag = afterRange.extractContents();

                // Create first split page with pre-break content
                const newPage1 = document.createElement('div');
                newPage1.className = 'page';
                newPage1.appendChild(beforeFrag);

                // Add page break marker
                const breakDiv = document.createElement('div');
                breakDiv.className = 'page-break';
                breakDiv.style.height = '0';
                breakDiv.style.margin = '0';
                breakDiv.style.padding = '0';
                breakDiv.style.pageBreakBefore = 'always';

                // Create second split page with post-break content
                const newPage2 = document.createElement('div');
                newPage2.className = 'page';
                newPage2.style.marginTop = '0';
                newPage2.style.paddingTop = '0';
                newPage2.style.borderTop = 'none';
                newPage2.appendChild(afterFrag);

                // Replace original page with new split pages
                targetPage.replaceWith(newPage1, breakDiv, newPage2);

                //method which make sure that content is pasted on top on new page without any space left
                // Fix SVG viewBox and shift elements for new page (newPage2)
                function adjustSvg(svg, desiredY) {
                    const rect = svg.querySelector('rect');
                    if (!rect) return;

                    const oldY = parseFloat(rect.getAttribute('y') || '0');
                    const deltaY = desiredY - oldY;
                    rect.setAttribute('y', desiredY);

                    svg.querySelectorAll('*').forEach(el => {
                        if (el === rect) return;
                        ['y', 'y1', 'y2'].forEach(attr => {
                            if (el.hasAttribute(attr)) {
                                const val = parseFloat(el.getAttribute(attr));
                                el.setAttribute(attr, val + deltaY);
                            }
                        });
                    });
                }

                //calculates manually height for viewBox based on rect, line and text tags in SVG element
                // Fix SVG viewBox issues, even if viewBox is missing
                function estimateSvgHeight(svg) {
                const textElements = svg.querySelectorAll('text');
                const rectElements = svg.querySelectorAll('rect');
                const lineElements = svg.querySelectorAll('line');

                let maxBottom = 0;

                // Helper to parse attribute safely and default to 0
                const parseAttr = (el, attr) => {
                    const val = el.getAttribute(attr);
                    return val ? parseFloat(val) : 0;
                };

                // Process text elements
                textElements.forEach(textEl => {
                    // Get font size or fallback
                    let fontSize = window.getComputedStyle(textEl).fontSize;
                    fontSize = fontSize ? parseFloat(fontSize) : 16;
                    const lineHeight = fontSize * 1.2;

                    // Count lines from tspans or newlines
                    let lines = 1;
                    const tspans = textEl.querySelectorAll('tspan');
                    if (tspans.length > 0) {
                        lines = tspans.length;
                    } else {
                        lines = textEl.textContent.split('\n').length;
                    }

                    // Get vertical position
                    const y = parseAttr(textEl, 'y');

                    // Calculate bottom position
                    const bottom = y + lines * lineHeight;
                    if (bottom > maxBottom) maxBottom = bottom;
                });

                // Process rect elements
                rectElements.forEach(rectEl => {
                    const y = parseAttr(rectEl, 'y');
                    const height = parseAttr(rectEl, 'height');
                    const bottom = y + height;
                    if (bottom > maxBottom) maxBottom = bottom;
                });

                // Process line elements
                lineElements.forEach(lineEl => {
                    const y1 = parseAttr(lineEl, 'y1');
                    const y2 = parseAttr(lineEl, 'y2');
                    const bottom = Math.max(y1, y2);
                    if (bottom > maxBottom) maxBottom = bottom;
                });

                // Add some padding if needed
                maxBottom += 10;

                return maxBottom;
            }

                //paste text on top of new page
                // Apply adjustments only to SVGs in newPage2
                const svgList = newPage2.querySelectorAll('svg');
                svgList.forEach(svg => {
                    const estHeight = estimateSvgHeight(svg);
                    svg.removeAttribute('height');
                    svg.style.height = estHeight + 'px';
                    svg.style.width = '100%';

                    const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 100;
                    svg.setAttribute('viewBox', `0 0 ${width} ${estHeight}`);

                    adjustSvg(svg, 40); // Align to y=20
                });

            //Allocate viewBox height dynamically
            const svgs = document.querySelectorAll('svg');
            svgs.forEach(svg => {
                const estimatedHeight = estimateSvgHeight(svg);
                svg.removeAttribute('height');
                svg.style.height = estimatedHeight + 'px';
                svg.style.width = '100%';

                // Optionally, update viewBox width from existing or default width (e.g. 100)
                const width = svg.viewBox.baseVal.width || svg.clientWidth || 100;
                svg.setAttribute('viewBox', `0 0 ${width} ${estimatedHeight}`);
            });


                insertBreakMode = false;
                editor.style.cursor = 'text';
            })();
            ";
            await browser.EvaluateScriptAsync(script);
        }

        //loads template for browser
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
                    let insertBreakMode = false;

                    function enableInsertBreakMode() {
                        insertBreakMode = !insertBreakMode;
                        document.getElementById('editor').style.cursor = insertBreakMode ? 'crosshair' : 'text';
                        return insertBreakMode;
                    }

                    function getHtmlContent() {
                        return document.getElementById('editor').innerHTML;
                    }

                    function setHtmlContent(html) {
                        document.getElementById('editor').innerHTML = html;
                    }

                    // Helper to remove leading/trailing whitespace nodes in a page
                    function trimPageWhitespace(page) {
                        if (!page) return;

                        const isWhitespaceNode = (node) =>
                            (node.nodeType === Node.TEXT_NODE && !/\S/.test(node.nodeValue)) ||
                            (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'BR') ||
                            (node.nodeType === Node.ELEMENT_NODE && node.innerHTML.trim() === '');

                        // Trim bottom
                        let last = page.lastChild;
                        while (last && isWhitespaceNode(last)) {
                            const toRemove = last;
                            last = last.previousSibling;
                            page.removeChild(toRemove);
                        }

                        // Trim top
                        let first = page.firstChild;
                        while (first && isWhitespaceNode(first)) {
                            const toRemove = first;
                            first = first.nextSibling;
                            page.removeChild(toRemove);
                        }
                    }

                    document.addEventListener('DOMContentLoaded', function () {
                        const editor = document.getElementById('editor');

                        editor.addEventListener('click', function (e) {
                            if (!insertBreakMode) return;

                            const sel = window.getSelection();
                            if (!sel.rangeCount) return;
                            const range = sel.getRangeAt(0);

                            // Flatten any nested pages
                            document.querySelectorAll('.page .page').forEach(nested => {
                                const outer = nested.closest('.page');
                                outer.replaceWith(...outer.childNodes);
                            });

                            // Remove inline heights
                            editor.querySelectorAll('*').forEach(el => {
                                if (el.style && el.style.height) {
                                    el.style.height = 'auto';
                                    el.removeAttribute('height');
                                }
                            });

                            // Find current page
                            const allPages = Array.from(editor.children).filter(c => c.classList.contains('page'));
                            let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
                            if (pageIndex < 0) pageIndex = 0;
                            const targetPage = allPages[pageIndex];

                            // Remove whitespace text nodes
                            Array.from(targetPage.childNodes).forEach(node => {
                                if (node.nodeType === Node.TEXT_NODE && !/\S/.test(node.textContent)) {
                                    node.remove();
                                }
                            });

                            // Extract before/after content
                            const beforeRange = document.createRange();
                            beforeRange.setStart(targetPage, 0);
                            beforeRange.setEnd(range.startContainer, range.startOffset);
                            const beforeFrag = beforeRange.extractContents();

                            const afterRange = document.createRange();
                            afterRange.setStart(range.endContainer, range.endOffset);
                            afterRange.setEnd(targetPage, targetPage.childNodes.length);
                            const afterFrag = afterRange.extractContents();

                            targetPage.remove(); // Remove empty original page

                            // Build new pages
                            const newPage1 = document.createElement('div');
                            newPage1.className = 'page';
                            newPage1.appendChild(beforeFrag);

                            const breakDiv = document.createElement('div');
                            breakDiv.className = 'page-break';

                            const newPage2 = document.createElement('div');
                            newPage2.className = 'page';
                            newPage2.appendChild(afterFrag);

                            // Trim empty space inside the new pages
                            trimPageWhitespace(newPage1);
                            trimPageWhitespace(newPage2);

                            const beforePages = allPages.slice(0, pageIndex);
                            const afterPages  = allPages.slice(pageIndex + 1);

                            editor.innerHTML = '';
                            beforePages.forEach(p => editor.appendChild(p.cloneNode(true)));
                            editor.appendChild(newPage1);
                            editor.appendChild(breakDiv);
                            editor.appendChild(newPage2);
                            afterPages.forEach(p => editor.appendChild(p.cloneNode(true)));

                            insertBreakMode = false;
                            editor.style.cursor = 'text';
                            e.preventDefault();
                        });
                    });
                </script>
            </body>
            </html>";
        }

        //download changes into downloadable file
        public async Task<string> GetEditedHtml()
        {
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
                            parent.replaceWith(...parent.childNodes);
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
                            const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 100;
                            const estHeight = estimateSvgHeight(svg);
                            svg.removeAttribute('height');
                            svg.removeAttribute('width');
                            svg.style.height = estHeight + 'px';
                            svg.style.width = '100%';
                            svg.setAttribute('viewBox', `0 0 ${width} ${estHeight}`);
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
            for (int i = 0; i < 10; i++)
            {
                if (browser.CanExecuteJavascriptInMainFrame)
                {
                    return;
                }
                await Task.Delay(500); // Wait 500ms between checks
            }
            throw new TimeoutException("Browser was not ready to execute scripts within the timeout period.");
        }
  
    }

    // Custom menu handler to handle right-clicks for the context menu
    public class CustomMenuHandler : IContextMenuHandler
    {
        private readonly Form1 _parentForm;

        public CustomMenuHandler(Form1 parentForm)
        {
            _parentForm = parentForm;
        }

        public void OnBeforeContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        {
            try
            {
                // Check if the right-click occurred inside the editor div
                // We'll use the IsEditable property to determine if it's inside the editor
                bool isInsideEditor = parameters.IsEditable;
                
                System.Diagnostics.Debug.WriteLine($"IsEditable: {parameters.IsEditable}");
                
                if (isInsideEditor)
                {
                    // Inside editor - show our custom menu
                    model.Clear();
                    model.AddItem(CefMenuCommand.UserFirst, "Add page break");
                    System.Diagnostics.Debug.WriteLine("Showing custom menu - inside editor");
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
            if (commandId == CefMenuCommand.UserFirst)
            {
                // Execute the page break insertion
                _parentForm.InsertPageBreakAsync();
                return true;
            }

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
}
