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
using System.Text.Json;
using AngleSharp.Dom;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using System.Text.Encodings.Web;
namespace HTMLEditor
{
    public partial class Form1 : Form
    {
        private ChromiumWebBrowser browser;
        private System.Windows.Forms.Button  getHtmlButton;
        private System.Windows.Forms.Button  pageViewToggleButton;
        public PageBreakManager _pageBreakManager = new PageBreakManager();
        //private ContextMenuStrip editorContextMenu;
        private string _currentReportPath;
        private string _currentReportContent;
        private bool _isMultiplePageView = false; // Default to single page view (normal scrollable view)

        public Form1()
        {
            Text = "HTML Editor";
            //Width = 1000;
            //Height = 700;
            this.WindowState = FormWindowState.Maximized;

            getHtmlButton = new System.Windows.Forms.Button { Text = "Export Edited HTML", Dock = DockStyle.Top, Height = 30 };
            getHtmlButton.Click += async (s, e) =>
            {
                string html = await GetEditedHtml();
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "edited_report.html");
                File.WriteAllText(filePath, html);
                MessageBox.Show($"Edited HTML saved to {filePath}");
            };

            pageViewToggleButton = new System.Windows.Forms.Button { Text = "Multiple Page View", Dock = DockStyle.Top, Height = 30 };
            pageViewToggleButton.Click += async (s, e) =>
            {
                await ToggleMultiplePageView();
            };

            browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };
            
            // Wait for browser to initialize before setting up notification function
            browser.IsBrowserInitializedChanged += async (sender, args) => {
                if (browser.IsBrowserInitialized) {
                    System.Diagnostics.Debug.WriteLine("Browser initialized, setting up notification function");
                    
                    // Wait for the browser to be ready to execute scripts
                    await WaitForBrowserReady();
                    
                    // Inject the notification function
                    await SetupPageBreakNotificationFunction();
                }
            };

            // Set up direct right-click handler for page break insertion
            browser.MenuHandler = new DirectPageBreakMenuHandler(this);

            Controls.Add(browser);
            Controls.Add(pageViewToggleButton);
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
                        browser.ShowDevTools();
                        await SetReportHtml(reportHtml);

                        // Setup the page break notification function
                        await SetupPageBreakNotificationFunction();
                        
                        // Restore page breaks from storage
                        await RestorePageBreaks();
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

                //Automatically split the initial content into A4 - sized pages after loading
                if (domReady)
                {
                    await AutoSplitInitialContent(browser);
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
            var script = @"
(function () {
    const editor = document.getElementById('editor');
    if (!editor || !window.getSelection) {
        console.error('Editor or selection not available');
        return;
    }

    const sel = window.getSelection();
    if (!sel.rangeCount) {
        console.error('No selection range');
        return;
    }
    const range = sel.getRangeAt(0);

    const allPages = Array.from(editor.querySelectorAll('.page'));
    if (!allPages.length) {
        console.error('No pages found');
        return;
    }

    let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
    if (pageIndex < 0) pageIndex = 0;
    const targetPage = allPages[pageIndex];
    console.log('Target page index:', pageIndex);

    function normalizeRangeToElementBoundary(range) {
        let startContainer = range.startContainer;
        let startOffset = range.startOffset;
        if (startContainer.nodeType === Node.TEXT_NODE && startOffset > 0 && startOffset <= startContainer.textContent.length) {
            const text = startContainer.textContent;
            let splitPoint = startOffset;
            for (let i = Math.max(0, startOffset - 10); i <= Math.min(text.length, startOffset + 10); i++) {
                if (text[i] === ' ' || text[i] === '\n' || text[i] === '\t') {
                    splitPoint = i;
                    break;
                }
            }
            if (splitPoint !== startOffset) {
                range.setStart(startContainer, splitPoint);
            }
        }
    }

    normalizeRangeToElementBoundary(range);

    const beforeRange = document.createRange();
    beforeRange.setStart(targetPage, 0);
    beforeRange.setEnd(range.startContainer, range.startOffset);
    const beforeFrag = beforeRange.extractContents();

    const afterRange = document.createRange();
    afterRange.setStart(range.endContainer, range.endOffset);
    afterRange.setEnd(targetPage, targetPage.childNodes.length);
    const afterFrag = afterRange.extractContents();
    const maxPageHeight = 1120;

    function parseAttr(el, attr) {
        const val = el.getAttribute(attr);
        return val ? parseFloat(val) : 0;
    }

    function getMinY(el) {
        let minY = Infinity;
        if (el.tagName.toLowerCase() === 'text') {
            const y = parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                tspans.forEach(tspan => {
                    const tspanY = parseAttr(tspan, 'y') || y;
                    minY = Math.min(minY, tspanY);
                });
            } else {
                minY = y;
            }
        } else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {
            minY = parseAttr(el, 'y');
        } else if (el.tagName.toLowerCase() === 'line') {
            minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        }
        return minY === Infinity ? 0 : minY;
    }

    function getMaxBottom(el) {
        let maxBottom = 0;
        let fontSize = window.getComputedStyle(el).fontSize;
        fontSize = parseFloat(fontSize) || 16;
        const lineHeight = fontSize * 1.2;

        if (el.tagName.toLowerCase() === 'text') {
            const y = parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                let maxTspanY = 0;
                tspans.forEach(tspan => {
                    const tspanY = parseAttr(tspan, 'y') || y;
                    maxTspanY = Math.max(maxTspanY, tspanY);
                });
                maxBottom = maxTspanY + lineHeight;
            } else {
                const lines = (el.textContent || '').split('\n').length;
                maxBottom = y + lines * lineHeight;
            }
        } else if (el.tagName.toLowerCase() === 'rect') {
            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'image') {
            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'line') {
            maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        }
        return maxBottom;
    }

    function estimateSvgHeight(svg) {
        const elements = svg.querySelectorAll('text, rect, line, image');
        let maxBottom = 0;
        let minY = Infinity;

        elements.forEach(el => {
            const y = getMinY(el);
            const bottom = getMaxBottom(el);
            minY = Math.min(minY, y);
            maxBottom = Math.max(maxBottom, bottom);
        });

        if (maxBottom <= 0 || minY === Infinity) {
            const viewBox = svg.getAttribute('viewBox');
            if (viewBox) {
                const parts = viewBox.split(/\s+/).map(parseFloat);
                if (parts.length >= 4) {
                    minY = parts[1];
                    maxBottom = parts[1] + parts[3];
                }
            } else {
                minY = 0;
                maxBottom = svg.clientHeight || 1000;
            }
        }

        return {
            height: Math.max(maxBottom - minY + 20, 50),
            minY: minY
        };
    }

    function adjustElementY(el, delta) {
        ['y', 'y1', 'y2'].forEach(attr => {
            if (el.hasAttribute(attr)) {
                el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
            }
        });
        if (el.tagName.toLowerCase() === 'text') {
            el.querySelectorAll('tspan').forEach(tspan => {
                const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
                tspan.setAttribute('y', y + delta);
            });
        }
    }

    function adjustSvg(svg, minY) {
        const elements = svg.querySelectorAll('rect, line, text, image');
        const deltaY = -minY;
        elements.forEach(el => adjustElementY(el, deltaY));
    }

    function isPageEmptyOrRectOnly(page) {
        const svg = page.querySelector('svg');
        if (!svg) {
            return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
        }
        const elements = svg.querySelectorAll('text, line, image');
        return !elements.length;
    }

    function splitIntoPages(frag) {
        if (!frag.hasChildNodes()) {
            console.warn('Fragment is empty, returning no pages');
            return [];
        }

        const tempPage = document.createElement('div');
        tempPage.className = 'page';
        tempPage.appendChild(frag);

        const svgs = tempPage.querySelectorAll('svg');
        if (svgs.length === 0) {
            editor.appendChild(tempPage);
            let height = tempPage.offsetHeight;
            editor.removeChild(tempPage);
            if (height > maxPageHeight) {
                tempPage.style.maxHeight = maxPageHeight + 'px';
                tempPage.style.overflow = 'hidden';
                height = maxPageHeight;
            }
            tempPage.style.minHeight = maxPageHeight + 'px';
            console.log('Non-SVG page height:', height);
            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        if (svgs.length > 1) {
            return splitMultipleSvgsIntoPages(Array.from(svgs));
        }

        const svg = svgs[0];
        const { height, minY } = estimateSvgHeight(svg);
        
        adjustSvg(svg, minY);

        if (height <= maxPageHeight) {
            const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            tempPage.style.minHeight = maxPageHeight + 'px';
            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        const pages = [];
        const elements = Array.from(svg.children).sort((a, b) => getMinY(a) - getMinY(b));
        let currentGroup = [];
        let currentMinY = Infinity;
        let currentMaxBottom = -Infinity;
        let processedElements = 0;

        console.log('Splitting', elements.length, 'elements across pages with maxPageHeight:', maxPageHeight);

        elements.forEach((el, index) => {
            const elMinY = getMinY(el);
            const elMaxBottom = getMaxBottom(el);
            
            const potentialMinY = currentGroup.length === 0 ? elMinY : Math.min(currentMinY, elMinY);
            const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
            const potentialHeight = potentialMaxBottom - potentialMinY;

            if (currentGroup.length > 0 && potentialHeight > maxPageHeight) {
                const groupHeight = currentMaxBottom - currentMinY;
                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
                if (!isPageEmptyOrRectOnly(newPage)) {
                    pages.push(newPage);
                }
                currentGroup = [el];
                currentMinY = elMinY;
                currentMaxBottom = elMaxBottom;
            } else {
                currentGroup.push(el);
                currentMinY = potentialMinY;
                currentMaxBottom = potentialMaxBottom;
            }
            processedElements = index + 1;
        });

        if (currentGroup.length > 0) {
            const groupHeight = currentMaxBottom - currentMinY;
            const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
            if (!isPageEmptyOrRectOnly(newPage)) {
                pages.push(newPage);
            }
        }

        console.log('Split complete: created', pages.length, 'pages from', processedElements, 'elements');
        return pages;
    }

    function splitMultipleSvgsIntoPages(svgs) {
        let totalHeight = 0;
        let cumulativeY = 0;
        const svgData = [];
        
        svgs.forEach((svg, index) => {
            const { height, minY } = estimateSvgHeight(svg);
            
            svgData.push({
                svg: svg,
                height: height,
                minY: minY,
                startY: cumulativeY,
                endY: cumulativeY + height,
                elements: Array.from(svg.children)
            });
            
            totalHeight += height;
            cumulativeY += height;
        });
        
        if (totalHeight <= maxPageHeight) {
            const combinedPage = document.createElement('div');
            combinedPage.className = 'page';
            combinedPage.style.minHeight = maxPageHeight + 'px';
            
            const combinedSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            const width = svgs[0].viewBox?.baseVal?.width || 816;
            
            let currentY = 0;
            svgData.forEach(data => {
                adjustSvg(data.svg, data.minY);
                data.elements.forEach(el => {
                    const cloned = el.cloneNode(true);
                    adjustElementY(cloned, currentY);
                    combinedSvg.appendChild(cloned);
                });
                currentY += data.height;
            });
            
            combinedSvg.setAttribute('viewBox', `0 0 ${width} ${totalHeight}`);
            combinedSvg.style.height = totalHeight + 'px';
            combinedSvg.style.width = '100%';
            combinedSvg.style.display = 'block';
            combinedSvg.style.margin = '0';
            combinedSvg.style.padding = '0';
            combinedPage.appendChild(combinedSvg);
            
            console.log('All SVGs combined into single page');
            return [combinedPage];
        }
        
        const pages = [];
        let currentPageElements = [];
        let currentPageHeight = 0;
        let currentPageMinY = 0;
        
        let globalYOffset = 0;

        svgData.forEach((data, svgIndex) => {
            adjustSvg(data.svg, data.minY);
            
            data.elements.forEach(el => {
                const elMinY = getMinY(el);
                const elMaxBottom = getMaxBottom(el);
                
                adjustElementY(el, globalYOffset);
                
                const adjustedMinY = elMinY + globalYOffset;
                const adjustedMaxBottom = elMaxBottom + globalYOffset;
                const elHeight = adjustedMaxBottom - adjustedMinY;
                
                if (currentPageElements.length > 0 && (adjustedMaxBottom - currentPageMinY) > maxPageHeight) {
                    const page = createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
                    if (!isPageEmptyOrRectOnly(page)) {
                        pages.push(page);
                    }
                    
                    currentPageElements = [el];
                    currentPageMinY = adjustedMinY;
                    currentPageHeight = adjustedMaxBottom - adjustedMinY;
                } else {
                    currentPageElements.push(el);
                    if (currentPageElements.length === 1) {
                        currentPageMinY = adjustedMinY;
                        currentPageHeight = adjustedMaxBottom - adjustedMinY;
                    } else {
                        currentPageMinY = Math.min(currentPageMinY, adjustedMinY);
                        currentPageHeight = Math.max(currentPageHeight, adjustedMaxBottom - currentPageMinY);
                    }
                }
            });
            
            globalYOffset += data.height;
        });
        
        if (currentPageElements.length > 0) {
            const page = createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
            if (!isPageEmptyOrRectOnly(page)) {
                pages.push(page);
            }
        }
        
        return pages;
    }

    function createPageFromElements(elements, pageMinY, pageHeight) {
        const newPage = document.createElement('div');
        newPage.className = 'page';
        newPage.style.minHeight = maxPageHeight + 'px';
        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const width = elements[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

        elements.forEach(el => {
            const cloned = el.cloneNode(true);
            adjustElementY(cloned, -pageMinY);
            newSvg.appendChild(cloned);
        });

        newSvg.setAttribute('viewBox', `0 0 ${width} ${pageHeight}`);
        newSvg.style.height = pageHeight + 'px';
        newSvg.style.width = '100%';
        newSvg.style.display = 'block';
        newSvg.style.margin = '0';
        newSvg.style.padding = '0';
        newPage.appendChild(newSvg);
        return newPage;
    }

    function createPageFromGroup(group, groupMinY, groupHeight) {
        const newPage = document.createElement('div');
        newPage.className = 'page';
        newPage.style.minHeight = maxPageHeight + 'px';
        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

        group.forEach(el => {
            const cloned = el.cloneNode(true);
            adjustElementY(cloned, -groupMinY);
            newSvg.appendChild(cloned);
        });

        newSvg.setAttribute('viewBox', `0 0 ${width} ${groupHeight}`);
        newSvg.style.height = groupHeight + 'px';
        newSvg.style.width = '100%';
        newSvg.style.display = 'block';
        newSvg.style.margin = '0';
        newSvg.style.padding = '0';
        newPage.appendChild(newSvg);
        return newPage;
    }

    function createBreakDiv(isUser) {
        const breakDiv = document.createElement('div');
        breakDiv.className = isUser ? 'page-break' : 'page-break auto-page-break';
        breakDiv.style.position = 'relative';
        breakDiv.style.height = '20px';
        breakDiv.style.margin = '10px 0';
        breakDiv.style.backgroundColor = isUser ? '#e0e0e0' : 'gray';
        breakDiv.style.borderTop = '1px dashed #ccc';
        breakDiv.style.borderBottom = '1px dashed #ccc';
        breakDiv.style.pageBreakBefore = 'always';

        const removeBtn = document.createElement('div');
        removeBtn.className = 'page-break-remove';
        removeBtn.innerHTML = '✂️';
        removeBtn.style.position = 'absolute';
        removeBtn.style.left = '50%';
        removeBtn.style.top = '50%';
        removeBtn.style.transform = 'translate(-50%, -50%)';
        removeBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.9)';
        removeBtn.style.color = '#333';
        removeBtn.style.borderRadius = '4px';
        removeBtn.style.padding = '3px 8px';
        removeBtn.style.display = 'none';
        removeBtn.style.cursor = 'pointer';
        breakDiv.appendChild(removeBtn);

        if (isUser) {
            removeBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                removePageBreak(breakDiv);
            });
            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
        }

        return breakDiv;
    }

    function insertBreaks(pages, isAuto) {
        if (pages.length <= 1) return pages;
        const result = [pages[0]];
        for (let i = 1; i < pages.length; i++) {
            result.push(createBreakDiv(!isAuto), pages[i]);
        }
        return result;
    }

    const subsequentContent = document.createDocumentFragment();
    const elementsToRemove = [];
    
    let current = targetPage.nextElementSibling;
    const manualBreaksAfter = [];
    
    while (current) {
        if (current.classList.contains('page')) {
            Array.from(current.childNodes).forEach(child => {
                if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.TEXT_NODE) {
                    subsequentContent.appendChild(child.cloneNode(true));
                }
            });
            elementsToRemove.push(current);
        } else if (current.classList.contains('page-break')) {
            if (!current.classList.contains('auto-page-break')) {
                const prevPage = current.previousElementSibling;
                if (prevPage && prevPage.classList.contains('page')) {
                    manualBreaksAfter.push({
                        element: current.cloneNode(true),
                        afterPageIndex: Array.from(editor.querySelectorAll('.page')).indexOf(prevPage)
                    });
                }
            }
            elementsToRemove.push(current);
        }
        current = current.nextElementSibling;
    }

    const combinedFrag = document.createDocumentFragment();
    while (afterFrag.firstChild) {
        combinedFrag.appendChild(afterFrag.firstChild);
    }
    
    while (subsequentContent.firstChild) {
        combinedFrag.appendChild(subsequentContent.firstChild);
    }

    const tempDiv = document.createElement('div');
    tempDiv.appendChild(combinedFrag.cloneNode(true));
    const svgInCombined = tempDiv.querySelector('svg');
    if (svgInCombined) {
        const { height } = estimateSvgHeight(svgInCombined);
        if (height > maxPageHeight) {
        }
    }

    Array.from(combinedFrag.childNodes).forEach((node, index) => {
        if (node.nodeType === Node.ELEMENT_NODE) {
            if (node.tagName.toLowerCase() === 'svg') {
            }
        }
    });

    const beforePages = splitIntoPages(beforeFrag);
    const afterPages = splitIntoPages(combinedFrag);
    
    if (afterPages.length === 1) {
        const singlePage = afterPages[0];
        const svg = singlePage.querySelector('svg');
        if (svg) {
            const { height } = estimateSvgHeight(svg);
            if (height > maxPageHeight) {
                console.warn('Single after page exceeds height limit, forcing re-split...');
                const contentFrag = document.createDocumentFragment();
                while (singlePage.firstChild) {
                    contentFrag.appendChild(singlePage.firstChild);
                }
                const reSplitPages = splitIntoPages(contentFrag);
                afterPages.splice.apply(afterPages, [0, 1].concat(reSplitPages));
            }
        }
    }

    const beforeWithBreaks = insertBreaks(beforePages, true);
    const afterWithBreaks = insertBreaks(afterPages, true);
    
    const userBreak = createBreakDiv(true);

    let allElements = [];
    
    if (beforePages.length > 0) {
        allElements = beforeWithBreaks.slice();
        allElements.push(userBreak);
    } else {
        allElements.push(userBreak);
    }
    
    if (afterPages.length > 0) {
        allElements = allElements.concat(afterWithBreaks);
    }

    elementsToRemove.forEach(el => {
        if (el && el.parentNode) {
            el.remove();
        }
    });

    try {
        if (targetPage.parentNode) {
            // Replace spread operator with forEach insertion
            const parent = targetPage.parentNode;
            const nextSibling = targetPage.nextSibling;
            parent.removeChild(targetPage);
            allElements.forEach(function(element) {
                parent.insertBefore(element, nextSibling);
            });
        }
    } catch (e) {
        console.error('Error replacing target page:', e);
    }

    function adjustAllSvgs() {
        const allSvgs = document.querySelectorAll('svg');
        allSvgs.forEach(svg => {
            svg.removeAttribute('height');
            svg.removeAttribute('style');
            const { height, minY } = estimateSvgHeight(svg);
            adjustSvg(svg, minY);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            const width = svg.viewBox?.baseVal?.width || 816;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        });
    }

    function cleanupEmptyOrRectOnlyPages() {
        const pages = Array.from(editor.querySelectorAll('.page'));
        pages.forEach(page => {
            if (isPageEmptyOrRectOnly(page)) {
                const prevSibling = page.previousElementSibling;
                if (prevSibling && prevSibling.classList.contains('page-break')) {
                    prevSibling.remove();
                }
                page.remove();
            }
        });
    }

    function removePageBreak(pageBreakElement) {
        if (pageBreakElement.classList.contains('auto-page-break')) {
            console.warn('Attempted to remove auto page break; ignoring');
            return false;
        }
        const prevPage = pageBreakElement.previousElementSibling;
        const nextPage = pageBreakElement.nextElementSibling;

        if (!prevPage || !nextPage || !prevPage.classList.contains('page') || !nextPage.classList.contains('page')) {
            console.error('Cannot find adjacent pages to merge');
            return false;
        }

        const prevSvg = prevPage.querySelector('svg');
        const nextSvg = nextPage.querySelector('svg');

        if (prevSvg && nextSvg) {
            const { height: prevHeight } = estimateSvgHeight(prevSvg);
            const nextElements = Array.from(nextSvg.children);
            nextElements.forEach(el => {
                adjustElementY(el, prevHeight);
                prevSvg.appendChild(el);
            });
            const newHeight = prevHeight + estimateSvgHeight(nextSvg).height;
            const width = prevSvg.viewBox?.baseVal?.width || 816;
            prevSvg.setAttribute('viewBox', `0 0 ${width} ${newHeight}`);
            prevSvg.style.height = newHeight + 'px';
        } else {
            while (nextPage.firstChild) {
                prevPage.appendChild(nextPage.firstChild);
            }
        }

        pageBreakElement.remove();
        nextPage.remove();

        const frag = document.createDocumentFragment();
        if (prevSvg) {
            while (prevPage.firstChild) {
                frag.appendChild(prevPage.firstChild);
            }
        } else {
            while (prevPage.firstChild) {
                frag.appendChild(prevPage.firstChild);
            }
        }
        const newPages = splitIntoPages(frag);
        const pagesWithBreaks = insertBreaks(newPages, true);
        // Replace spread operator with forEach insertion
        const parent = prevPage.parentNode;
        const nextSibling = prevPage.nextSibling;
        parent.removeChild(prevPage);
        pagesWithBreaks.forEach(function(element) {
            parent.insertBefore(element, nextSibling);
        });

        adjustAllSvgs();
        cleanupEmptyOrRectOnlyPages();
        savePageBreakPositions();
        return true;
    }

    // Helper function to calculate global Y position from document top
    function getGlobalYPosition(textEl) {
        try {
            let globalY = 0;
            
            // Find the page containing this element
            let currentPage = textEl.closest('.page');
            if (!currentPage) {
                // Fallback to bounding box if not in a page
                const bbox = textEl.getBoundingClientRect();
                const editorBbox = editor.getBoundingClientRect();
                return bbox.top - editorBbox.top;
            }
            
            // Calculate cumulative height of all pages before this one
            const allPages = Array.from(editor.querySelectorAll('.page'));
            const currentPageIndex = allPages.indexOf(currentPage);
            
            for (let i = 0; i < currentPageIndex; i++) {
                const pageHeight = allPages[i].offsetHeight || 0;
                globalY += pageHeight;
            }
            
            // Add the local Y position within the current page
            let localY = 0;
            if (textEl.hasAttribute('y')) {
                localY = parseFloat(textEl.getAttribute('y')) || 0;
            } else {
                // Fallback to bounding box relative to page
                const bbox = textEl.getBoundingClientRect();
                const pageBox = currentPage.getBoundingClientRect();
                localY = bbox.top - pageBox.top;
            }
            
            globalY += localY;
            return globalY;
        } catch (e) {
            console.warn('Error calculating global Y position:', e);
            return 0;
        }
    }

    function savePageBreakPositions() {
        const pageBreaks = Array.from(editor.querySelectorAll('.page-break:not(.auto-page-break)'));
        const breakData = [];
        
        pageBreaks.forEach((pageBreak, index) => {
            // Find all text elements before this page break
            const allTextElements = Array.from(editor.querySelectorAll('text'));
            const breakPosition = Array.from(editor.children).indexOf(pageBreak);
            
            // Find text elements that appear before this break in the DOM
            const precedingTexts = [];
            allTextElements.forEach(textEl => {
                const textPage = textEl.closest('.page');
                if (textPage) {
                    const pagePosition = Array.from(editor.children).indexOf(textPage);
                    if (pagePosition < breakPosition) {
                        precedingTexts.push(textEl);
                    }
                }
            });
            
            // Get the last 2 text elements before the break
            const lastTwoTexts = precedingTexts.slice(-2);
            
            if (lastTwoTexts.length >= 2) {
                const text1Content = lastTwoTexts[0].textContent.trim();
                const text2Content = lastTwoTexts[1].textContent.trim();
                
                // Calculate GLOBAL yOffset and xOffset based on the second text element's position
                const text2Element = lastTwoTexts[1];
                let yOffset = 0;
                let xOffset = 0;
                
                try {
                    // Calculate global Y position by finding cumulative height from document top
                    yOffset = getGlobalYPosition(text2Element);
                    
                    // Get x position from text element or its x attribute  
                    if (text2Element.hasAttribute('x')) {
                        xOffset = parseFloat(text2Element.getAttribute('x')) || 0;
                    } else {
                        // Fallback to bounding box for x coordinate
                        const bbox = text2Element.getBoundingClientRect();
                        const editorBbox = editor.getBoundingClientRect();
                        xOffset = bbox.left - editorBbox.left;
                    }
                } catch (e) {
                    console.warn('Error calculating global offset for text element:', e);
                }
                
                // Find which page this break is closest to
                const allPages = Array.from(editor.querySelectorAll('.page'));
                let pageIndex = 0;
                for (let i = 0; i < allPages.length; i++) {
                    const pagePosition = Array.from(editor.children).indexOf(allPages[i]);
                    if (pagePosition < breakPosition) {
                        pageIndex = i;
                    } else {
                        break;
                    }
                }
                
                const breakInfo = {
                    text1Hash: computeSimpleTextHash(text1Content),
                    text1Content: text1Content.substring(0, 50),
                    text2Hash: computeSimpleTextHash(text2Content), 
                    text2Content: text2Content.substring(0, 50),
                    breakId: `break_` + Date.now() + `_` + index,
                    yOffset: yOffset,
                    xOffset: xOffset,
                    pageIndex: pageIndex
                };
                
                breakData.push(breakInfo);
            }
        });
        
        console.log('Saving simplified page break data:', breakData);
        window.pageBreakPositionsData = JSON.stringify(breakData);
        if (typeof window.notifyPageBreakPositionsReady === 'function') {
            window.notifyPageBreakPositionsReady();
        }
    }
    
    // Simple hash function for text content
    function computeSimpleTextHash(text) {
        let hash = 0;
        if (!text || text.length === 0) return 'empty_text';
        for (let i = 0; i < text.length; i++) {
            const chr = text.charCodeAt(i);
            hash = ((hash << 5) - hash) + chr;
            hash |= 0; // Convert to 32-bit integer
        }
        return 'txt_' + Math.abs(hash).toString(36);
    }

    adjustAllSvgs();
    cleanupEmptyOrRectOnlyPages();
    savePageBreakPositions();

    insertBreakMode = false;
    editor.style.cursor = 'text';
})();
";

            await browser.EvaluateScriptAsync(script);

            // Retrieve saved break data
            var result = await browser.EvaluateScriptAsync("window.pageBreakPositionsData");
            if (result.Success && result.Result != null)
            {
                string breakDataJson = result.Result.ToString();
                System.Diagnostics.Debug.WriteLine("Saved page break data: " + breakDataJson);
                // Update _pageBreakManager with the new data
                _pageBreakManager.SavePageBreakData(_currentReportPath, _currentReportContent, breakDataJson);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Failed to retrieve page break data");
            }
        }

        //        public async Task TogglePageBreak(ChromiumWebBrowser browser)
        //        {
        //            var script = @"
        //(function () {
        //    const editor = document.getElementById('editor');
        //    if (!editor || !window.getSelection) {
        //        console.error('Editor or selection not available');
        //        return;
        //    }

        //    const sel = window.getSelection();
        //    if (!sel.rangeCount) {
        //        console.error('No selection range');
        //        return;
        //    }
        //    const range = sel.getRangeAt(0);

        //    const allPages = Array.from(editor.querySelectorAll('.page'));
        //    if (!allPages.length) {
        //        console.error('No pages found');
        //        return;
        //    }

        //    let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
        //    if (pageIndex < 0) pageIndex = 0;
        //    const targetPage = allPages[pageIndex];
        //    console.log('Target page index:', pageIndex);

        //    function normalizeRangeToElementBoundary(range) {
        //        let startContainer = range.startContainer;
        //        let startOffset = range.startOffset;
        //        if (startContainer.nodeType === Node.TEXT_NODE && startOffset > 0 && startOffset <= startContainer.textContent.length) {
        //            const text = startContainer.textContent;
        //            let splitPoint = startOffset;
        //            for (let i = Math.max(0, startOffset - 10); i <= Math.min(text.length, startOffset + 10); i++) {
        //                if (text[i] === ' ' || text[i] === '\n' || text[i] === '\t') {
        //                    splitPoint = i;
        //                    break;
        //                }
        //            }
        //            if (splitPoint !== startOffset) {
        //                range.setStart(startContainer, splitPoint);
        //            }
        //        }
        //    }

        //    normalizeRangeToElementBoundary(range);

        //    const beforeRange = document.createRange();
        //    beforeRange.setStart(targetPage, 0);
        //    beforeRange.setEnd(range.startContainer, range.startOffset);
        //    const beforeFrag = beforeRange.extractContents();

        //    const afterRange = document.createRange();
        //    afterRange.setStart(range.endContainer, range.endOffset);
        //    afterRange.setEnd(targetPage, targetPage.childNodes.length);
        //    const afterFrag = afterRange.extractContents();
        //    const maxPageHeight = 1120;

        //    function parseAttr(el, attr) {
        //        const val = el.getAttribute(attr);
        //        return val ? parseFloat(val) : 0;
        //    }

        //    function getMinY(el) {
        //        let minY = Infinity;
        //        if (el.tagName.toLowerCase() === 'text') {
        //            const y = parseAttr(el, 'y');
        //            const tspans = el.querySelectorAll('tspan');
        //            if (tspans.length > 0) {
        //                tspans.forEach(tspan => {
        //                    const tspanY = parseAttr(tspan, 'y') || y;
        //                    minY = Math.min(minY, tspanY);
        //                });
        //            } else {
        //                minY = y;
        //            }
        //        } else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {
        //            minY = parseAttr(el, 'y');
        //        } else if (el.tagName.toLowerCase() === 'line') {
        //            minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        //        }
        //        return minY === Infinity ? 0 : minY;
        //    }

        //    function getMaxBottom(el) {
        //        let maxBottom = 0;
        //        let fontSize = window.getComputedStyle(el).fontSize;
        //        fontSize = parseFloat(fontSize) || 16;
        //        const lineHeight = fontSize * 1.2;

        //        if (el.tagName.toLowerCase() === 'text') {
        //            const y = parseAttr(el, 'y');
        //            const tspans = el.querySelectorAll('tspan');
        //            if (tspans.length > 0) {
        //                let maxTspanY = 0;
        //                tspans.forEach(tspan => {
        //                    const tspanY = parseAttr(tspan, 'y') || y;
        //                    maxTspanY = Math.max(maxTspanY, tspanY);
        //                });
        //                maxBottom = maxTspanY + lineHeight;
        //            } else {
        //                const lines = (el.textContent || '').split('\n').length;
        //                maxBottom = y + lines * lineHeight;
        //            }
        //        } else if (el.tagName.toLowerCase() === 'rect') {
        //            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        //        } else if (el.tagName.toLowerCase() === 'image') {
        //            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        //        } else if (el.tagName.toLowerCase() === 'line') {
        //            maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        //        }
        //        return maxBottom;
        //    }

        //    function estimateSvgHeight(svg) {
        //        const elements = svg.querySelectorAll('text, rect, line, image');
        //        let maxBottom = 0;
        //        let minY = Infinity;

        //        elements.forEach(el => {
        //            const y = getMinY(el);
        //            const bottom = getMaxBottom(el);
        //            minY = Math.min(minY, y);
        //            maxBottom = Math.max(maxBottom, bottom);
        //        });

        //        if (maxBottom <= 0 || minY === Infinity) {
        //            const viewBox = svg.getAttribute('viewBox');
        //            if (viewBox) {
        //                const parts = viewBox.split(/\s+/).map(parseFloat);
        //                if (parts.length >= 4) {
        //                    minY = parts[1];
        //                    maxBottom = parts[1] + parts[3];
        //                }
        //            } else {
        //                minY = 0;
        //                maxBottom = svg.clientHeight || 1000;
        //            }
        //        }

        //        return {
        //            height: Math.max(maxBottom - minY + 20, 50),
        //            minY: minY
        //        };
        //    }

        //    function adjustElementY(el, delta) {
        //        ['y', 'y1', 'y2'].forEach(attr => {
        //            if (el.hasAttribute(attr)) {
        //                el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
        //            }
        //        });
        //        if (el.tagName.toLowerCase() === 'text') {
        //            el.querySelectorAll('tspan').forEach(tspan => {
        //                const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
        //                tspan.setAttribute('y', y + delta);
        //            });
        //        }
        //    }

        //    function adjustSvg(svg, minY) {
        //        const elements = svg.querySelectorAll('rect, line, text, image');
        //        const deltaY = -minY;
        //        elements.forEach(el => adjustElementY(el, deltaY));
        //    }

        //    function isPageEmptyOrRectOnly(page) {
        //        const svg = page.querySelector('svg');
        //        if (!svg) {
        //            return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
        //        }
        //        const elements = svg.querySelectorAll('text, line, image');
        //        return !elements.length;
        //    }

        //    function splitIntoPages(frag) {
        //        if (!frag.hasChildNodes()) {
        //            console.warn('Fragment is empty, returning no pages');
        //            return [];
        //        }

        //        const tempPage = document.createElement('div');
        //        tempPage.className = 'page';
        //        tempPage.appendChild(frag);

        //         Handle multiple SVG elements in the fragment
        //        const svgs = tempPage.querySelectorAll('svg');
        //        if (svgs.length === 0) {
        //            editor.appendChild(tempPage);
        //            let height = tempPage.offsetHeight;
        //            editor.removeChild(tempPage);
        //            if (height > maxPageHeight) {
        //                tempPage.style.maxHeight = maxPageHeight + 'px';
        //                tempPage.style.overflow = 'hidden';
        //                height = maxPageHeight;
        //            }
        //            tempPage.style.minHeight = maxPageHeight + 'px';
        //            console.log('Non-SVG page height:', height);
        //            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        //        }

        //         If we have multiple SVGs, we need to process them together
        //        if (svgs.length > 1) {
        //            return splitMultipleSvgsIntoPages(Array.from(svgs));
        //        }

        //         Single SVG processing (existing logic)
        //        const svg = svgs[0];
        //        const { height, minY } = estimateSvgHeight(svg);

        //         First normalize all Y coordinates to start from 0
        //        adjustSvg(svg, minY);

        //         If content fits in one page, return it
        //        if (height <= maxPageHeight) {
        //            const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
        //            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        //            svg.style.height = height + 'px';
        //            svg.style.width = '100%';
        //            svg.style.display = 'block';
        //            svg.style.margin = '0';
        //            svg.style.padding = '0';
        //            tempPage.style.minHeight = maxPageHeight + 'px';
        //            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        //        }

        //         Content needs to be split across multiple pages
        //        const pages = [];
        //        const elements = Array.from(svg.children).sort((a, b) => getMinY(a) - getMinY(b));
        //        let currentGroup = [];
        //        let currentMinY = Infinity;
        //        let currentMaxBottom = -Infinity;
        //        let processedElements = 0;

        //        console.log('Splitting', elements.length, 'elements across pages with maxPageHeight:', maxPageHeight);

        //        elements.forEach((el, index) => {
        //            const elMinY = getMinY(el);
        //            const elMaxBottom = getMaxBottom(el);

        //             Check if adding this element would exceed page height
        //            const potentialMinY = currentGroup.length === 0 ? elMinY : Math.min(currentMinY, elMinY);
        //            const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
        //            const potentialHeight = potentialMaxBottom - potentialMinY;


        //            if (currentGroup.length > 0 && potentialHeight > maxPageHeight) {
        //                 Current group is full, create a page
        //                const groupHeight = currentMaxBottom - currentMinY;
        //                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
        //                if (!isPageEmptyOrRectOnly(newPage)) {
        //                    pages.push(newPage);
        //                }
        //                 Start new group with current element
        //                currentGroup = [el];
        //                currentMinY = elMinY;
        //                currentMaxBottom = elMaxBottom;
        //            } else {
        //                 Add element to current group
        //                currentGroup.push(el);
        //                currentMinY = potentialMinY;
        //                currentMaxBottom = potentialMaxBottom;
        //            }
        //            processedElements = index + 1;
        //        });

        //         Handle remaining elements
        //        if (currentGroup.length > 0) {
        //            const groupHeight = currentMaxBottom - currentMinY;
        //            const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
        //            if (!isPageEmptyOrRectOnly(newPage)) {
        //                pages.push(newPage);
        //            }
        //        }

        //        console.log('Split complete: created', pages.length, 'pages from', processedElements, 'elements');
        //        return pages;
        //    }

        //    function splitMultipleSvgsIntoPages(svgs) {

        //         Calculate total height of all SVGs combined
        //        let totalHeight = 0;
        //        let cumulativeY = 0;
        //        const svgData = [];

        //        svgs.forEach((svg, index) => {
        //            const { height, minY } = estimateSvgHeight(svg);

        //            svgData.push({
        //                svg: svg,
        //                height: height,
        //                minY: minY,
        //                startY: cumulativeY,
        //                endY: cumulativeY + height,
        //                elements: Array.from(svg.children)
        //            });

        //            totalHeight += height;
        //            cumulativeY += height;
        //        });


        //        if (totalHeight <= maxPageHeight) {
        //             All SVGs fit in one page - combine them
        //            const combinedPage = document.createElement('div');
        //            combinedPage.className = 'page';
        //            combinedPage.style.minHeight = maxPageHeight + 'px';

        //            const combinedSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //            const width = svgs[0].viewBox?.baseVal?.width || 816;

        //            let currentY = 0;
        //            svgData.forEach(data => {
        //                adjustSvg(data.svg, data.minY);
        //                data.elements.forEach(el => {
        //                    const cloned = el.cloneNode(true);
        //                    adjustElementY(cloned, currentY);
        //                    combinedSvg.appendChild(cloned);
        //                });
        //                currentY += data.height;
        //            });

        //            combinedSvg.setAttribute('viewBox', `0 0 ${width} ${totalHeight}`);
        //            combinedSvg.style.height = totalHeight + 'px';
        //            combinedSvg.style.width = '100%';
        //            combinedSvg.style.display = 'block';
        //            combinedSvg.style.margin = '0';
        //            combinedSvg.style.padding = '0';
        //            combinedPage.appendChild(combinedSvg);

        //            console.log('All SVGs combined into single page');
        //            return [combinedPage];
        //        }

        //         SVGs need to be split across multiple pages
        //        const pages = [];
        //        let currentPageElements = [];
        //        let currentPageHeight = 0;
        //        let currentPageMinY = 0;

        //        let globalYOffset = 0; // Track cumulative Y position across all SVGs

        //        svgData.forEach((data, svgIndex) => {

        //             Adjust all elements in this SVG to start from 0, then offset by globalYOffset
        //            adjustSvg(data.svg, data.minY);

        //            data.elements.forEach(el => {
        //                 Get element position after initial adjustment
        //                const elMinY = getMinY(el);
        //                const elMaxBottom = getMaxBottom(el);

        //                 Apply global offset to position this element correctly in the sequence
        //                adjustElementY(el, globalYOffset);

        //                const adjustedMinY = elMinY + globalYOffset;
        //                const adjustedMaxBottom = elMaxBottom + globalYOffset;
        //                const elHeight = adjustedMaxBottom - adjustedMinY;


        //                 Check if adding this element would exceed page height
        //                if (currentPageElements.length > 0 && (adjustedMaxBottom - currentPageMinY) > maxPageHeight) {
        //                     Create page from current elements
        //                    const page = createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
        //                    if (!isPageEmptyOrRectOnly(page)) {
        //                        pages.push(page);
        //                    }

        //                     Start new page with current element
        //                    currentPageElements = [el];
        //                    currentPageMinY = adjustedMinY;
        //                    currentPageHeight = adjustedMaxBottom - adjustedMinY;
        //                } else {
        //                     Add to current page
        //                    currentPageElements.push(el);
        //                    if (currentPageElements.length === 1) {
        //                        currentPageMinY = adjustedMinY;
        //                        currentPageHeight = adjustedMaxBottom - adjustedMinY;
        //                    } else {
        //                        currentPageMinY = Math.min(currentPageMinY, adjustedMinY);
        //                        currentPageHeight = Math.max(currentPageHeight, adjustedMaxBottom - currentPageMinY);
        //                    }
        //                }
        //            });

        //             Update global offset for next SVG
        //            globalYOffset += data.height;
        //        });

        //         Handle remaining elements
        //        if (currentPageElements.length > 0) {
        //            const page = createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
        //            if (!isPageEmptyOrRectOnly(page)) {
        //                pages.push(page);
        //            }
        //        }

        //        return pages;
        //    }

        //    function createPageFromElements(elements, pageMinY, pageHeight) {
        //        const newPage = document.createElement('div');
        //        newPage.className = 'page';
        //        newPage.style.minHeight = maxPageHeight + 'px';
        //        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //        const width = elements[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

        //        elements.forEach(el => {
        //            const cloned = el.cloneNode(true);
        //            adjustElementY(cloned, -pageMinY);
        //            newSvg.appendChild(cloned);
        //        });

        //        newSvg.setAttribute('viewBox', `0 0 ${width} ${pageHeight}`);
        //        newSvg.style.height = pageHeight + 'px';
        //        newSvg.style.width = '100%';
        //        newSvg.style.display = 'block';
        //        newSvg.style.margin = '0';
        //        newSvg.style.padding = '0';
        //        newPage.appendChild(newSvg);
        //        return newPage;
        //    }

        //    function createPageFromGroup(group, groupMinY, groupHeight) {
        //        const newPage = document.createElement('div');
        //        newPage.className = 'page';
        //        newPage.style.minHeight = maxPageHeight + 'px';
        //        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //        const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

        //        group.forEach(el => {
        //            const cloned = el.cloneNode(true);
        //            adjustElementY(cloned, -groupMinY);
        //            newSvg.appendChild(cloned);
        //        });

        //        newSvg.setAttribute('viewBox', `0 0 ${width} ${groupHeight}`);
        //        newSvg.style.height = groupHeight + 'px';
        //        newSvg.style.width = '100%';
        //        newSvg.style.display = 'block';
        //        newSvg.style.margin = '0';
        //        newSvg.style.padding = '0';
        //        newPage.appendChild(newSvg);
        //        return newPage;
        //    }

        //    function createBreakDiv(isUser) {
        //        const breakDiv = document.createElement('div');
        //        breakDiv.className = isUser ? 'page-break' : 'page-break auto-page-break';
        //        breakDiv.style.position = 'relative';
        //        breakDiv.style.height = '20px';
        //        breakDiv.style.margin = '10px 0';
        //        breakDiv.style.backgroundColor = isUser ? '#e0e0e0' : 'gray';
        //        breakDiv.style.borderTop = '1px dashed #ccc';
        //        breakDiv.style.borderBottom = '1px dashed #ccc';
        //        breakDiv.style.pageBreakBefore = 'always';

        //        const removeBtn = document.createElement('div');
        //        removeBtn.className = 'page-break-remove';
        //        removeBtn.innerHTML = '✂️';
        //        removeBtn.style.position = 'absolute';
        //        removeBtn.style.left = '50%';
        //        removeBtn.style.top = '50%';
        //        removeBtn.style.transform = 'translate(-50%, -50%)';
        //        removeBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.9)';
        //        removeBtn.style.color = '#333';
        //        removeBtn.style.borderRadius = '4px';
        //        removeBtn.style.padding = '3px 8px';
        //        removeBtn.style.display = 'none';
        //        removeBtn.style.cursor = 'pointer';
        //        breakDiv.appendChild(removeBtn);

        //        if (isUser) {
        //            removeBtn.addEventListener('click', function(e) {
        //                e.stopPropagation();
        //                removePageBreak(breakDiv);
        //            });
        //            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
        //            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
        //        }

        //        return breakDiv;
        //    }

        //    function insertBreaks(pages, isAuto) {
        //        if (pages.length <= 1) return pages;
        //        const result = [pages[0]];
        //        for (let i = 1; i < pages.length; i++) {
        //            result.push(createBreakDiv(!isAuto), pages[i]);
        //        }
        //        return result;
        //    }

        //     Collect ALL subsequent content until end of document (not stopping at manual breaks)
        //    const subsequentContent = document.createDocumentFragment();
        //    let current = targetPage.nextElementSibling;
        //    const elementsToRemove = [];

        //     Track if we need to preserve any manual page breaks that come after
        //    const manualBreaksAfter = [];

        //    while (current) {
        //        if (current.classList.contains('page')) {
        //             Collect content from this page - preserve the entire page structure

        //             Clone the entire page content to preserve structure
        //            Array.from(current.childNodes).forEach(child => {
        //                if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.TEXT_NODE) {
        //                    subsequentContent.appendChild(child.cloneNode(true));
        //                }
        //            });
        //            elementsToRemove.push(current);
        //        } else if (current.classList.contains('page-break')) {
        //             Track manual breaks but remove ALL breaks for now
        //            if (!current.classList.contains('auto-page-break')) {
        //                 Store position info for manual breaks to potentially restore later
        //                const prevPage = current.previousElementSibling;
        //                if (prevPage && prevPage.classList.contains('page')) {
        //                    manualBreaksAfter.push({
        //                        element: current.cloneNode(true),
        //                        afterPageIndex: Array.from(editor.querySelectorAll('.page')).indexOf(prevPage)
        //                    });
        //                }
        //            }
        //            elementsToRemove.push(current);
        //        }
        //        current = current.nextElementSibling;
        //    }

        //     Combine afterFrag with ALL subsequent content for proper reflow
        //    const combinedFrag = document.createDocumentFragment();

        //     First add the content immediately after the break point
        //    while (afterFrag.firstChild) {
        //        combinedFrag.appendChild(afterFrag.firstChild);
        //    }

        //     Then add all subsequent content from the rest of the document
        //    while (subsequentContent.firstChild) {
        //        combinedFrag.appendChild(subsequentContent.firstChild);
        //    }


        //     Check if we have SVG content that needs auto-splitting
        //    const tempDiv = document.createElement('div');
        //    tempDiv.appendChild(combinedFrag.cloneNode(true));
        //    const svgInCombined = tempDiv.querySelector('svg');
        //    if (svgInCombined) {
        //        const { height } = estimateSvgHeight(svgInCombined);
        //        if (height > maxPageHeight) {
        //        }
        //    }

        //     Debug: Log what type of content we have
        //    Array.from(combinedFrag.childNodes).forEach((node, index) => {
        //        if (node.nodeType === Node.ELEMENT_NODE) {
        //            if (node.tagName.toLowerCase() === 'svg') {
        //            }
        //        }
        //    });

        //     Split content into pages with proper auto page break calculation
        //    const beforePages = splitIntoPages(beforeFrag);
        //    const afterPages = splitIntoPages(combinedFrag);

        //     Ensure auto-splitting worked correctly for after pages
        //    if (afterPages.length === 1) {
        //        const singlePage = afterPages[0];
        //        const svg = singlePage.querySelector('svg');
        //        if (svg) {
        //            const { height } = estimateSvgHeight(svg);
        //            if (height > maxPageHeight) {
        //                console.warn('Single after page exceeds height limit, forcing re-split...');
        //                 Re-extract content and force split
        //                const contentFrag = document.createDocumentFragment();
        //                while (singlePage.firstChild) {
        //                    contentFrag.appendChild(singlePage.firstChild);
        //                }
        //                const reSplitPages = splitIntoPages(contentFrag);
        //                afterPages.splice(0, 1, ...reSplitPages);
        //            }
        //        }
        //    }


        //     Insert auto breaks between pages
        //    const beforeWithBreaks = insertBreaks(beforePages, true);
        //    const afterWithBreaks = insertBreaks(afterPages, true);

        //     Create the manual page break that user inserted
        //    const userBreak = createBreakDiv(true);

        //    let allElements = [];

        //     Assemble all elements: before pages + manual break + after pages (with proper reflow)
        //    if (beforePages.length > 0) {
        //        allElements = [...beforeWithBreaks];
        //        allElements.push(userBreak);
        //    } else {
        //         If no content before, still insert the manual break
        //        allElements.push(userBreak);
        //    }

        //    if (afterPages.length > 0) {
        //        allElements.push(...afterWithBreaks);
        //    }

        //     Note: We're not restoring manual breaks that were after the insertion point
        //     because the content has been completely reflowed. If needed, user can re-add them.
        //     This ensures clean page layout without empty spaces.

        //     Remove ALL old elements (pages and breaks) to ensure clean slate
        //    elementsToRemove.forEach(el => {
        //        if (el && el.parentNode) {
        //            el.remove();
        //        }
        //    });

        //    try {
        //         Replace the target page with the new properly reflowed structure
        //        if (targetPage.parentNode) {
        //            targetPage.replaceWith(...allElements);
        //        }
        //    } catch (e) {
        //        console.error('Error replacing target page:', e);
        //    }

        //    function adjustAllSvgs() {
        //        const allSvgs = document.querySelectorAll('svg');
        //        allSvgs.forEach(svg => {
        //            svg.removeAttribute('height');
        //            svg.removeAttribute('style');
        //            const { height, minY } = estimateSvgHeight(svg);
        //            adjustSvg(svg, minY);
        //            svg.style.height = height + 'px';
        //            svg.style.width = '100%';
        //            svg.style.display = 'block';
        //            svg.style.margin = '0';
        //            svg.style.padding = '0';
        //            const width = svg.viewBox?.baseVal?.width || 816;
        //            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        //        });
        //    }

        //    function cleanupEmptyOrRectOnlyPages() {
        //        const pages = Array.from(editor.querySelectorAll('.page'));
        //        pages.forEach(page => {
        //            if (isPageEmptyOrRectOnly(page)) {
        //                const prevSibling = page.previousElementSibling;
        //                if (prevSibling && prevSibling.classList.contains('page-break')) {
        //                    prevSibling.remove();
        //                }
        //                page.remove();
        //            }
        //        });
        //    }

        //    function removePageBreak(pageBreakElement) {
        //        if (pageBreakElement.classList.contains('auto-page-break')) {
        //            console.warn('Attempted to remove auto page break; ignoring');
        //            return false;
        //        }
        //        const prevPage = pageBreakElement.previousElementSibling;
        //        const nextPage = pageBreakElement.nextElementSibling;

        //        if (!prevPage || !nextPage || !prevPage.classList.contains('page') || !nextPage.classList.contains('page')) {
        //            console.error('Cannot find adjacent pages to merge');
        //            return false;
        //        }

        //        const prevSvg = prevPage.querySelector('svg');
        //        const nextSvg = nextPage.querySelector('svg');

        //        if (prevSvg && nextSvg) {
        //            const { height: prevHeight } = estimateSvgHeight(prevSvg);
        //            const nextElements = Array.from(nextSvg.children);
        //            nextElements.forEach(el => {
        //                adjustElementY(el, prevHeight);
        //                prevSvg.appendChild(el);
        //            });
        //            const newHeight = prevHeight + estimateSvgHeight(nextSvg).height;
        //            const width = prevSvg.viewBox?.baseVal?.width || 816;
        //            prevSvg.setAttribute('viewBox', `0 0 ${width} ${newHeight}`);
        //            prevSvg.style.height = newHeight + 'px';
        //        } else {
        //            while (nextPage.firstChild) {
        //                prevPage.appendChild(nextPage.firstChild);
        //            }
        //        }

        //        pageBreakElement.remove();
        //        nextPage.remove();

        //        const frag = document.createDocumentFragment();
        //        if (prevSvg) {
        //            while (prevPage.firstChild) {
        //                frag.appendChild(prevPage.firstChild);
        //            }
        //        } else {
        //            while (prevPage.firstChild) {
        //                frag.appendChild(prevPage.firstChild);
        //            }
        //        }
        //        const newPages = splitIntoPages(frag);
        //        const pagesWithBreaks = insertBreaks(newPages, true);
        //        prevPage.replaceWith(...pagesWithBreaks);

        //        adjustAllSvgs();
        //        cleanupEmptyOrRectOnlyPages();
        //        savePageBreakPositions();
        //        return true;
        //    }

        //        function savePageBreakPositions() {
        //        const pageBreaks = Array.from(editor.querySelectorAll('.page-break:not(.auto-page-break)'));
        //        const allPages = Array.from(editor.children).filter(p => p.classList.contains('page'));
        //        const pageBreakPositions = [];

        //        pageBreaks.forEach(breakEl => {
        //            const breakIndex = Array.from(editor.children).indexOf(breakEl);
        //            let pageBeforeIndex = -1;
        //            for (let i = breakIndex - 1; i >= 0; i--) {
        //                if (allPages.includes(editor.children[i])) {
        //                    pageBeforeIndex = allPages.indexOf(editor.children[i]);
        //                    break;
        //                }
        //            }

        //            if (pageBeforeIndex >= 0) {
        //                const pageBefore = allPages[pageBeforeIndex];
        //                const svg = pageBefore.querySelector('svg');
        //                let height = 0;
        //                let xOffset = 0;

        //                if (svg) {
        //                    const vb = svg.getAttribute('viewBox');
        //                    const vbParts = vb ? vb.split(' ').map(parseFloat) : [0, 0, 816, 1000];
        //                    height = vbParts[3];

        //                     Estimate xOffset based on the last element in the page
        //                    const elements = Array.from(svg.querySelectorAll('text, rect, line, image')).sort((a, b) => getMinY(a) - getMinY(b));
        //                    const lastElement = elements[elements.length - 1];
        //                    if (lastElement) {
        //                        xOffset = parseAttr(lastElement, 'x') || parseAttr(lastElement, 'x1') || 0;
        //                        if (lastElement.tagName.toLowerCase() === 'text' && window.getSelection) {
        //                            const sel = window.getSelection();
        //                            if (sel.rangeCount && sel.getRangeAt(0).startContainer === lastElement) {
        //                                 Approximate xOffset from selection if available
        //                                const range = sel.getRangeAt(0);
        //                                const rect = range.getBoundingClientRect();
        //                                xOffset = rect.left - svg.getBoundingClientRect().left;
        //                            }
        //                        }
        //                    }
        //                }

        //                let cumulativeHeight = 0;
        //                for (let i = 0; i < pageBeforeIndex; i++) {
        //                    const prevSvg = allPages[i].querySelector('svg');
        //                    if (prevSvg) {
        //                        const prevVb = prevSvg.getAttribute('viewBox');
        //                        const prevVbParts = prevVb ? prevVb.split(' ').map(parseFloat) : [0, 0, 816, 1000];
        //                        cumulativeHeight += prevVbParts[3];
        //                    }
        //                }

        //                pageBreakPositions.push({ yOffset: cumulativeHeight + height, xOffset });
        //            }
        //        });

        //        window.pageBreakPositionsData = JSON.stringify(pageBreakPositions);
        //        if (typeof window.notifyPageBreakPositionsReady === 'function') {
        //            window.notifyPageBreakPositionsReady();
        //        }
        //    }

        //    adjustAllSvgs();
        //    cleanupEmptyOrRectOnlyPages();
        //    savePageBreakPositions();

        //    insertBreakMode = false;
        //    editor.style.cursor = 'text';
        //})();
        //";
        //            await browser.EvaluateScriptAsync(script);
        //        }

        private async Task AutoSplitInitialContent(ChromiumWebBrowser browser)
        {
            var autoSplitScript = @"
(function () {
    console.log('Starting AutoSplitInitialContent...');
    
    const editor = document.getElementById('editor');
    if (!editor) {
        console.error('Editor element not found!');
        return;
    }

    const initialPage = editor.querySelector('.page');
    if (!initialPage) {
        console.error('No initial page found!');
        return;
    }

    console.log('Initial page children:', initialPage.children.length);

    const frag = document.createDocumentFragment();
    while (initialPage.firstChild) {
        frag.appendChild(initialPage.firstChild);
    }

    if (!frag.hasChildNodes()) {
        console.error('No content in initial page!');
        initialPage.remove();
        return;
    }

    const maxPageHeight = 1120;

    function parseAttr(el, attr) {
        const val = el.getAttribute(attr);
        return val ? parseFloat(val) : 0;
    }

    function getMinY(el) {
        let minY = Infinity;
        if (el.tagName.toLowerCase() === 'text') {
            const y = parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                tspans.forEach(tspan => {
                    const tspanY = parseAttr(tspan, 'y') || y;
                    minY = Math.min(minY, tspanY);
                });
            } else {
                minY = y;
            }
        } else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {
            minY = parseAttr(el, 'y');
        } else if (el.tagName.toLowerCase() === 'line') {
            minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        }
        return minY === Infinity ? 0 : minY;
    }

    function getMaxBottom(el) {
        let maxBottom = 0;
        let fontSize = window.getComputedStyle(el).fontSize;
        fontSize = parseFloat(fontSize) || 16;
        const lineHeight = fontSize * 1.2;

        if (el.tagName.toLowerCase() === 'text') {
            const y = parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                let maxTspanY = 0;
                tspans.forEach(tspan => {
                    const tspanY = parseAttr(tspan, 'y') || y;
                    maxTspanY = Math.max(maxTspanY, tspanY);
                });
                maxBottom = maxTspanY + lineHeight;
            } else {
                const lines = (el.textContent || '').split('\n').length;
                maxBottom = y + lines * lineHeight;
            }
        } else if (el.tagName.toLowerCase() === 'rect') {
            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'image') {
            maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'line') {
            maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        }
        return maxBottom;
    }

    function estimateSvgHeight(svg) {
        const elements = svg.querySelectorAll('text, rect, line, image');
        let maxBottom = 0;
        let minY = Infinity;

        elements.forEach(el => {
            const y = getMinY(el);
            const bottom = getMaxBottom(el);
            minY = Math.min(minY, y);
            maxBottom = Math.max(maxBottom, bottom);
        });

        if (maxBottom <= 0 || minY === Infinity) {
            const viewBox = svg.getAttribute('viewBox');
            if (viewBox) {
                const parts = viewBox.split(/\s+/).map(parseFloat);
                if (parts.length >= 4) {
                    minY = parts[1];
                    maxBottom = parts[1] + parts[3];
                }
            } else {
                minY = 0;
                maxBottom = svg.clientHeight || 1000;
            }
        }

        return {
            height: Math.max(maxBottom - minY + 20, 50),
            minY: minY
        };
    }

    function adjustElementY(el, delta) {
        ['y', 'y1', 'y2'].forEach(attr => {
            if (el.hasAttribute(attr)) {
                el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
            }
        });
        if (el.tagName.toLowerCase() === 'text') {
            el.querySelectorAll('tspan').forEach(tspan => {
                const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
                tspan.setAttribute('y', y + delta);
            });
        }
    }

    function adjustSvg(svg, minY) {
        const elements = svg.querySelectorAll('rect, line, text, image');
        const deltaY = -minY;
        elements.forEach(el => adjustElementY(el, deltaY));
    }

    function isPageEmptyOrRectOnly(page) {
        const svg = page.querySelector('svg');
        if (!svg) {
            return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
        }
        const elements = svg.querySelectorAll('text, line, image');
        return !elements.length;
    }

    function splitIntoPages(frag) {
        if (!frag.hasChildNodes()) {
            console.warn('Fragment is empty, returning no pages');
            return [];
        }

        const tempPage = document.createElement('div');
        tempPage.className = 'page';
        tempPage.appendChild(frag);

        const svg = tempPage.querySelector('svg');
        if (!svg) {
            editor.appendChild(tempPage);
            let height = tempPage.offsetHeight;
            editor.removeChild(tempPage);
            if (height > maxPageHeight) {
                tempPage.style.maxHeight = maxPageHeight + 'px';
                tempPage.style.overflow = 'hidden';
                height = maxPageHeight;
            }
            tempPage.style.minHeight = maxPageHeight + 'px';
            console.log('Non-SVG page height:', height);
            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        const { height, minY } = estimateSvgHeight(svg);
        console.log('SVG height:', height, 'minY:', minY);
        adjustSvg(svg, minY);

        if (height <= maxPageHeight) {
            const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            tempPage.style.minHeight = maxPageHeight + 'px';
            return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        const pages = [];
        const elements = Array.from(svg.children).sort((a, b) => getMinY(a) - getMinY(b));
        let currentGroup = [];
        let currentMinY = Infinity;
        let currentMaxBottom = -Infinity;

        elements.forEach(el => {
            const elMinY = getMinY(el);
            const elMaxBottom = getMaxBottom(el);
            const potentialMinY = Math.min(currentMinY, elMinY);
            const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
            const potentialHeight = potentialMaxBottom - potentialMinY;

            if (currentGroup.length > 0 && potentialHeight > maxPageHeight) {
                const groupHeight = currentMaxBottom - currentMinY;
                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
                if (!isPageEmptyOrRectOnly(newPage)) {
                    pages.push(newPage);
                }
                currentGroup = [el];
                currentMinY = elMinY;
                currentMaxBottom = elMaxBottom;
            } else {
                currentGroup.push(el);
                currentMinY = potentialMinY;
                currentMaxBottom = potentialMaxBottom;
            }
        });

        if (currentGroup.length > 0) {
            const groupHeight = currentMaxBottom - currentMinY;
            const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
            if (!isPageEmptyOrRectOnly(newPage)) {
                pages.push(newPage);
            }
        }

        console.log('Split into pages:', pages.length, 'elements processed:', elements.length);
        return pages;
    }

    function createPageFromGroup(group, groupMinY, groupHeight) {
        const newPage = document.createElement('div');
        newPage.className = 'page';
        newPage.style.minHeight = maxPageHeight + 'px';
        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const width = group[0].ownerSVGElement.viewBox?.baseVal?.width || 816;

        group.forEach(el => {
            const cloned = el.cloneNode(true);
            adjustElementY(cloned, -groupMinY);
            newSvg.appendChild(cloned);
        });

        newSvg.setAttribute('viewBox', `0 0 ${width} ${groupHeight}`);
        newSvg.style.height = groupHeight + 'px';
        newSvg.style.width = '100%';
        newSvg.style.display = 'block';
        newSvg.style.margin = '0';
        newSvg.style.padding = '0';
        newPage.appendChild(newSvg);
        return newPage;
    }

    function createBreakDiv(isUser) {
        const breakDiv = document.createElement('div');
        breakDiv.className = isUser ? 'page-break' : 'page-break auto-page-break';
        breakDiv.style.position = 'relative';
        breakDiv.style.height = '20px';
        breakDiv.style.margin = '10px 0';
        breakDiv.style.backgroundColor = isUser ? '#e0e0e0' : 'gray';
        breakDiv.style.borderTop = '1px dashed #ccc';
        breakDiv.style.borderBottom = '1px dashed #ccc';
        breakDiv.style.pageBreakBefore = 'always';

        const removeBtn = document.createElement('div');
        removeBtn.className = 'page-break-remove';
        removeBtn.innerHTML = '✂️';
        removeBtn.style.position = 'absolute';
        removeBtn.style.left = '50%';
        removeBtn.style.top = '50%';
        removeBtn.style.transform = 'translate(-50%, -50%)';
        removeBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.9)';
        removeBtn.style.color = '#333';
        removeBtn.style.borderRadius = '4px';
        removeBtn.style.padding = '3px 8px';
        removeBtn.style.display = 'none';
        removeBtn.style.cursor = 'pointer';
        breakDiv.appendChild(removeBtn);

        if (isUser) {
            removeBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                removePageBreak(breakDiv);
            });
            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
        }

        return breakDiv;
    }

    function insertBreaks(pages, isAuto) {
        if (pages.length <= 1) return pages;
        const result = [pages[0]];
        for (let i = 1; i < pages.length; i++) {
            result.push(createBreakDiv(!isAuto), pages[i]);
        }
        return result;
    }

    const initialPages = splitIntoPages(frag);
    if (initialPages.length === 0) {
        console.error('No pages created; restoring initial content');
        initialPage.appendChild(frag);
        return;
    }

    const pagesWithBreaks = insertBreaks(initialPages, true);
    try {
        // Replace spread operator with forEach insertion
        const parent = initialPage.parentNode;
        const nextSibling = initialPage.nextSibling;
        parent.removeChild(initialPage);
        pagesWithBreaks.forEach(function(element) {
            parent.insertBefore(element, nextSibling);
        });
        console.log('Replaced initial page with', pagesWithBreaks.length, 'elements');
    } catch (e) {
        console.error('Error replacing initial page:', e);
        initialPage.appendChild(frag);
    }

    function adjustAllSvgs() {
        const allSvgs = document.querySelectorAll('svg');
        allSvgs.forEach(svg => {
            svg.removeAttribute('height');
            svg.removeAttribute('style');
            const { height, minY } = estimateSvgHeight(svg);
            adjustSvg(svg, minY);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            const width = svg.viewBox?.baseVal?.width || 816;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        });
    }

    function cleanupEmptyOrRectOnlyPages() {
        const pages = Array.from(editor.querySelectorAll('.page'));
        pages.forEach(page => {
            if (isPageEmptyOrRectOnly(page)) {
                const prevSibling = page.previousElementSibling;
                if (prevSibling && prevSibling.classList.contains('page-break')) {
                    prevSibling.remove();
                }
                page.remove();
            }
        });
    }

    adjustAllSvgs();
    cleanupEmptyOrRectOnlyPages();
    savePageBreakPositions();
})();
";
            var result = await browser.EvaluateScriptAsync(autoSplitScript);
            //if (result.Success) console.log("AutoSplitInitialContent executed: " + result.Result);
            //else console.log("AutoSplitInitialContent failed: " + result.Message);

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
                                const parent = outer.parentNode;
                                const nextSibling = outer.nextSibling;
                                while (outer.firstChild) {
                                    parent.insertBefore(outer.firstChild, nextSibling);
                                }
                                parent.removeChild(outer);
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

                return finalHtml;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetEditedHtml error: {ex}");
                return "";
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

                // Escape special characters for JavaScript string literal
                breakDataJson = breakDataJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");

                string script = $@"
(async function() {{
    try {{
        console.log('RestorePageBreaks script started');

        // Get the editor element
        const editor = document.getElementById('editor');
        if (!editor) {{
            console.error('Editor element not found');
            return;
        }}

        // Flatten nested .page containers
        document.querySelectorAll('.page .page').forEach(nested => {{
            const outer = nested.closest('.page');
            if (outer) {{
                while (nested.firstChild) {{
                    outer.parentNode.insertBefore(nested.firstChild, outer);
                }}
                nested.remove();
            }}
        }});

        // Helper: Simple string hash function
        function computeHash(str) {{
            //console.log('Computing hash for:', str);
            let hash = 0;
            if (str.length === 0) return 'hash_0';
            for (let i = 0; i < str.length; i++) {{
                const chr = str.charCodeAt(i);
                hash = ((hash << 5) - hash) + chr;
                hash |= 0; // Convert to 32-bit integer
            }}
            return 'hash_' + Math.abs(hash).toString(36);
        }}

        // Helper: Get element signature
        async function getElementSignature(el) {{
            const type = el.tagName.toLowerCase();
            let content = '';
            let attributes = {{}};
            if (type === 'text') {{
                content = el.textContent.trim();
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    content = Array.from(tspans).map(t => t.textContent.trim()).join('|');
                }}
                attributes = {{
                    x: el.getAttribute('x'),
                    y: el.getAttribute('y')
                }};
            }} else if (['rect', 'image'].includes(type)) {{
                attributes = {{
                    x: el.getAttribute('x'),
                    y: el.getAttribute('y'),
                    width: el.getAttribute('width'),
                    height: el.getAttribute('height')
                }};
            }} else if (type === 'line') {{
                attributes = {{
                    x1: el.getAttribute('x1'),
                    y1: el.getAttribute('y1'),
                    x2: el.getAttribute('x2'),
                    y2: el.getAttribute('y2')
                }};
            }}
            const attrStr = JSON.stringify(attributes, Object.keys(attributes).sort());
            const hash = computeHash(content + attrStr);
            //console.log('Element signature:', {{ hash, type, content: content.substring(0, 50), attributes }});
            return {{ hash, type, content: content.substring(0, 50), attributes }};
        }}

        // Parse break data
        const breakDataJson = '{breakDataJson}';
        let breakData;
        try {{
            console.log('Attempting to parse breakDataJson:', breakDataJson);
            breakData = JSON.parse(breakDataJson);
            console.log('Parsed simplified breakData:', breakData);
        }} catch (e) {{
            console.error('Failed to parse breakDataJson:', e, 'Raw JSON:', breakDataJson);
            return;
        }}

        if (!Array.isArray(breakData)) {{
            console.error('breakData is not an array:', breakData);
            return;
        }}

        console.log('breakData length:', breakData.length);

        // Get all text elements for simplified matching
        const allTextElements = Array.from(editor.querySelectorAll('text'));
        console.log(`Found ${{allTextElements.length}} text elements in current DOM`);

        if (allTextElements.length === 0) {{
            console.warn('No text elements found in editor to match against break data.');
            return;
        }}

        // Process each break with simplified matching
        async function restoreBreakSequentially(breakIndex) {{
            if (breakIndex >= breakData.length) {{
                console.log('All breaks restored successfully');
                return;
            }}

            const breakPoint = breakData[breakIndex];
            console.log('Processing break ' + (breakIndex + 1) + '/' + breakData.length + ': ' + breakPoint.breakId);
            

            // Skip invalid breaks
            if (!breakPoint || !breakPoint.text1Hash || !breakPoint.text2Hash) {{
                console.warn('Invalid break at index ' + breakIndex + ':', breakPoint);
                return await restoreBreakSequentially(breakIndex + 1);
            }}

            // Find the two text elements by their hashes
            let text1Element = null;
            let text2Element = null;
            
            // Re-scan text elements for fresh DOM state
            const currentTextElements = Array.from(editor.querySelectorAll('text'));
            let text1Candidates = [];
            let text2Candidates = [];
            
            currentTextElements.forEach((textEl, index) => {{
                const textContent = textEl.textContent.trim();
                const textHash = computeSimpleTextHash(textContent);
                
                // Debug logging for hash matching
                if (index < 10) {{ // Log first 10 elements for debugging
                }}
                
                if (textHash === breakPoint.text1Hash) {{
                    text1Candidates.push(textEl);
                }}
                if (textHash === breakPoint.text2Hash) {{
                    text2Candidates.push(textEl);
                }} else if (false) {{
                    // If multiple elements with same hash, prefer one closest to the yOffset
                    if (!text2Element || (breakPoint.yOffset > 0 && 
                        Math.abs(getElementYPosition(textEl) - breakPoint.yOffset) < 
                        Math.abs(getElementYPosition(text2Element) - breakPoint.yOffset))) {{
                        text2Element = textEl;
                    }}
                }}
            }});
            
            // Select closest elements to yOffset
            if (text1Candidates.length === 1) {{
                text1Element = text1Candidates[0];
            }} else if (text1Candidates.length > 1 && breakPoint.yOffset > 0) {{
                let minDistance = Infinity;
                text1Candidates.forEach(candidate => {{
                    const distance = Math.abs(getElementYPosition(candidate) - breakPoint.yOffset);
                    if (distance < minDistance) {{
                        minDistance = distance;
                        text1Element = candidate;
                    }}
                }});
            }} else if (text1Candidates.length > 1) {{
                text1Element = text1Candidates[0];
            }}
            
            if (text2Candidates.length === 1) {{
                text2Element = text2Candidates[0];
            }} else if (text2Candidates.length > 1 && breakPoint.yOffset > 0) {{
                let minDistance = Infinity;
                text2Candidates.forEach(candidate => {{
                    const distance = Math.abs(getElementYPosition(candidate) - breakPoint.yOffset);
                    if (distance < minDistance) {{
                        minDistance = distance;
                        text2Element = candidate;
                    }}
                }});
            }} else if (text2Candidates.length > 1) {{
                text2Element = text2Candidates[0];
            }}

            if (!text1Element || !text2Element) {{
                console.warn('Missing text elements for break ' + breakPoint.breakId + ': text1=' + !!text1Element + ', text2=' + !!text2Element);
                
                // Fallback: try to find elements by partial content match
                if (!text1Element && breakPoint.text1Content) {{
                    const partialMatch1 = currentTextElements.find(el => 
                        el.textContent.trim().includes(breakPoint.text1Content.trim()) ||
                        breakPoint.text1Content.trim().includes(el.textContent.trim())
                    );
                    if (partialMatch1) {{
                        text1Element = partialMatch1;
                    }}
                }}
                
                if (!text2Element && breakPoint.text2Content) {{
                    const partialMatch2 = currentTextElements.find(el => 
                        el.textContent.trim().includes(breakPoint.text2Content.trim()) ||
                        breakPoint.text2Content.trim().includes(el.textContent.trim())
                    );
                    if (partialMatch2) {{
                        text2Element = partialMatch2;
                    }}
                }}
                
                // If still missing elements, try position-based fallback
                if (!text1Element || !text2Element) {{
                    console.log('Trying position-based fallback for missing elements...');
                    const elementsNearPosition = currentTextElements.filter(el => {{
                        const elY = getElementYPosition(el);
                        return Math.abs(elY - breakPoint.yOffset) < 50; // Within 50px of saved position
                    }}).sort((a, b) => {{
                        const aDistance = Math.abs(getElementYPosition(a) - breakPoint.yOffset);
                        const bDistance = Math.abs(getElementYPosition(b) - breakPoint.yOffset);
                        return aDistance - bDistance;
                    }});
                    
                    if (elementsNearPosition.length >= 2) {{
                        if (!text1Element) {{
                            text1Element = elementsNearPosition[0];
                        }}
                        if (!text2Element) {{
                            text2Element = elementsNearPosition[1];
                        }}
                    }}
                }}
                
                // If still no elements found, skip this break
                if (!text1Element || !text2Element) {{
                    console.warn('Could not find suitable elements for break ' + breakPoint.breakId + ' even with fallbacks, skipping');
                    return await restoreBreakSequentially(breakIndex + 1);
                }}
            }}

            // Find which element comes last in DOM order (insert break after the later one)
            const allTexts = Array.from(editor.querySelectorAll('text'));
            const text1Index = allTexts.indexOf(text1Element);
            const text2Index = allTexts.indexOf(text2Element);
            const anchorEl = text1Index > text2Index ? text1Element : text2Element;
            
            console.log('Using element at index ' + Math.max(text1Index, text2Index) + ' as anchor for break insertion');
            console.log('Expected page index: ' + breakPoint.pageIndex);

            const targetPage = anchorEl.closest('.page');
            if (!targetPage) {{
                console.warn('Anchor not in a page, skipping');
                return await restoreBreakSequentially(breakIndex + 1);
            }}

            // Check if a break already exists after this anchor (avoid duplicates)
            let nextElement = anchorEl.parentElement;
            while (nextElement && nextElement !== targetPage) {{
                nextElement = nextElement.nextElementSibling;
                if (nextElement && nextElement.classList && nextElement.classList.contains('page-break')) {{
                    console.log('Break already exists after anchor, skipping');
                    return await restoreBreakSequentially(breakIndex + 1);
                }}
            }}

            // Create selection range after anchor element
            const selection = window.getSelection();
            selection.removeAllRanges();
            const range = document.createRange();
            
            try {{
                // Position range right after the anchor element
                if (anchorEl.nextSibling) {{
                    range.setStartBefore(anchorEl.nextSibling);
                    range.setEndBefore(anchorEl.nextSibling);
                }} else {{
                    range.setStartAfter(anchorEl);
                    range.setEndAfter(anchorEl);
                }}
                selection.addRange(range);
                
                console.log('Set selection after anchor element for break insertion');
                
                // Use the existing TogglePageBreak logic
                const toggleScript = `
                (function() {{
                    const editor = document.getElementById('editor');
                    if (!editor || !window.getSelection) {{
                        console.error('Editor or selection not available for break insertion');
                        return false;
                    }}

                    const sel = window.getSelection();
                    if (!sel.rangeCount) {{
                        console.error('No selection range for break insertion');
                        return false;
                    }}
                    
                    // Execute the same logic as TogglePageBreak
                    ` + getTogglePageBreakLogic() + `
                    
                    return true;
                }})();`;
                
                const toggleResult = eval(toggleScript);
                if (toggleResult) {{
                    console.log('Successfully inserted break for ' + breakPoint.breakId);
                }} else {{
                    console.warn('Failed to insert break for ' + breakPoint.breakId);
                }}
                
                // Continue with next break after a short delay to let DOM settle
                setTimeout(() => restoreBreakSequentially(breakIndex + 1), 200);
                
            }} catch (error) {{
                console.error('Error setting range for break ' + breakPoint.breakId + ':', error);
                return await restoreBreakSequentially(breakIndex + 1);
            }}
        }}
        
        // Simple hash function for text content (must match the one in savePageBreakPositions)
        function computeSimpleTextHash(text) {{
            let hash = 0;
            if (!text || text.length === 0) return 'empty_text';
            for (let i = 0; i < text.length; i++) {{
                const chr = text.charCodeAt(i);
                hash = ((hash << 5) - hash) + chr;
                hash |= 0; // Convert to 32-bit integer
            }}
            return 'txt_' + Math.abs(hash).toString(36);
        }}
        
        // Helper function to get GLOBAL Y position of a text element from document top
        function getElementYPosition(textEl) {{
            return getGlobalYPosition(textEl);
        }}
        
        // Helper function to calculate global Y position from document top
        function getGlobalYPosition(textEl) {{
            try {{
                let globalY = 0;
                
                // Find the page containing this element
                let currentPage = textEl.closest('.page');
                if (!currentPage) {{
                    // Fallback to bounding box if not in a page
                    const bbox = textEl.getBoundingClientRect();
                    const editorBbox = editor.getBoundingClientRect();
                    return bbox.top - editorBbox.top;
                }}
                
                // Calculate cumulative height of all pages before this one
                const allPages = Array.from(editor.querySelectorAll('.page'));
                const currentPageIndex = allPages.indexOf(currentPage);
                
                for (let i = 0; i < currentPageIndex; i++) {{
                    const pageHeight = allPages[i].offsetHeight || 0;
                    globalY += pageHeight;
                }}
                
                // Add the local Y position within the current page
                let localY = 0;
                if (textEl.hasAttribute('y')) {{
                    localY = parseFloat(textEl.getAttribute('y')) || 0;
                }} else {{
                    // Fallback to bounding box relative to page
                    const bbox = textEl.getBoundingClientRect();
                    const pageBox = currentPage.getBoundingClientRect();
                    localY = bbox.top - pageBox.top;
                }}
                
                globalY += localY;
                return globalY;
            }} catch (e) {{
                console.warn('Error calculating global Y position:', e);
                return 0;
            }}
        }}
        
        // Helper function for content similarity calculation
        function calculateContentSimilarity(str1, str2) {{
            if (!str1 || !str2) return 0;
            const longer = str1.length > str2.length ? str1 : str2;
            const shorter = str1.length > str2.length ? str2 : str1;
            if (longer.length === 0) return 1.0;
            return (longer.length - editDistance(longer, shorter)) / parseFloat(longer.length);
        }}
        
        function editDistance(str1, str2) {{
            const matrix = [];
            for (let i = 0; i <= str2.length; i++) {{
                matrix[i] = [i];
            }}
            for (let j = 0; j <= str1.length; j++) {{
                matrix[0][j] = j;
            }}
            for (let i = 1; i <= str2.length; i++) {{
                for (let j = 1; j <= str1.length; j++) {{
                    if (str2.charAt(i - 1) === str1.charAt(j - 1)) {{
                        matrix[i][j] = matrix[i - 1][j - 1];
                    }} else {{
                        matrix[i][j] = Math.min(
                            matrix[i - 1][j - 1] + 1,
                            matrix[i][j - 1] + 1,
                            matrix[i - 1][j] + 1
                        );
                    }}
                }}
            }}
            return matrix[str2.length][str1.length];
        }}
        
        function getTogglePageBreakLogic() {{
            return `
            const range = sel.getRangeAt(0);
            const allPages = Array.from(editor.querySelectorAll('.page'));
            if (!allPages.length) {{
                console.error('No pages found');
                return false;
            }}

            let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
            if (pageIndex < 0) pageIndex = 0;
            const targetPage = allPages[pageIndex];

            function normalizeRangeToElementBoundary(range) {{
                let startContainer = range.startContainer;
                let startOffset = range.startOffset;
                if (startContainer.nodeType === Node.TEXT_NODE && startOffset > 0 && startOffset <= startContainer.textContent.length) {{
                    const text = startContainer.textContent;
                    let splitPoint = startOffset;
                    for (let i = Math.max(0, startOffset - 10); i <= Math.min(text.length, startOffset + 10); i++) {{
                        if (text[i] === ' ' || text[i] === '\\n' || text[i] === '\\t') {{
                            splitPoint = i;
                            break;
                        }}
                    }}
                    if (splitPoint !== startOffset) {{
                        range.setStart(startContainer, splitPoint);
                    }}
                }}
            }}

            normalizeRangeToElementBoundary(range);

            const beforeRange = document.createRange();
            beforeRange.setStart(targetPage, 0);
            beforeRange.setEnd(range.startContainer, range.startOffset);
            const beforeFrag = beforeRange.extractContents();

            const afterRange = document.createRange();
            afterRange.setStart(range.endContainer, range.endOffset);
            afterRange.setEnd(targetPage, targetPage.childNodes.length);
            const afterFrag = afterRange.extractContents();
            
            const beforePages = splitIntoPages(beforeFrag);
            const afterPages = splitIntoPages(afterFrag);
            
            const beforeWithBreaks = insertBreaks(beforePages, false);
            const afterWithBreaks = insertBreaks(afterPages, false);

            const userBreak = createBreakDiv(true);
            
            let allElements = [];
            
            if (beforePages.length > 0) {{
                allElements = beforeWithBreaks.slice();
                allElements.push(userBreak);
            }} else {{
                allElements.push(userBreak);
            }}
            
            if (afterPages.length > 0) {{
                allElements = allElements.concat(afterWithBreaks);
            }}

            if (targetPage.parentNode && allElements.length > 0) {{
                // Replace spread operator with forEach insertion
                const parent = targetPage.parentNode;
                const nextSibling = targetPage.nextSibling;
                parent.removeChild(targetPage);
                allElements.forEach(function(element) {{
                    parent.insertBefore(element, nextSibling);
                }});
                adjustAllSvgs();
                cleanupEmptyOrRectOnlyPages();
                return true;
            }}
            return false;
            `;
        }}
        
        // Helper functions from TogglePageBreak
        function parseAttr(el, attr) {{
            const val = el.getAttribute(attr);
            return val ? parseFloat(val) : 0;
        }}

        function getMinY(el) {{
            let minY = Infinity;
            if (el.tagName.toLowerCase() === 'text') {{
                const y = parseAttr(el, 'y');
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    tspans.forEach(tspan => {{
                        const tspanY = parseAttr(tspan, 'y') || y;
                        minY = Math.min(minY, tspanY);
                    }});
                }} else {{
                    minY = y;
                }}
            }} else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {{
                minY = parseAttr(el, 'y');
            }} else if (el.tagName.toLowerCase() === 'line') {{
                minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
            }}
            return minY === Infinity ? 0 : minY;
        }}

        function getMaxBottom(el) {{
            let maxBottom = 0;
            let fontSize = window.getComputedStyle(el).fontSize;
            fontSize = parseFloat(fontSize) || 16;
            const lineHeight = fontSize * 1.2;

            if (el.tagName.toLowerCase() === 'text') {{
                const y = parseAttr(el, 'y');
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    let maxTspanY = 0;
                    tspans.forEach(tspan => {{
                        const tspanY = parseAttr(tspan, 'y') || y;
                        maxTspanY = Math.max(maxTspanY, tspanY);
                    }});
                    maxBottom = maxTspanY + lineHeight;
                }} else {{
                    const lines = (el.textContent || '').split('\\n').length;
                    maxBottom = y + lines * lineHeight;
                }}
            }} else if (el.tagName.toLowerCase() === 'rect') {{
                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
            }} else if (el.tagName.toLowerCase() === 'image') {{
                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
            }} else if (el.tagName.toLowerCase() === 'line') {{
                maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
            }}
            return maxBottom;
        }}

        function adjustElementY(el, delta) {{
            ['y', 'y1', 'y2'].forEach(attr => {{
                if (el.hasAttribute(attr)) {{
                    el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
                }}
            }});
            if (el.tagName.toLowerCase() === 'text') {{
                el.querySelectorAll('tspan').forEach(tspan => {{
                    const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
                    tspan.setAttribute('y', y + delta);
                }});
            }}
        }}

        function adjustSvg(svg, minY) {{
            const elements = svg.querySelectorAll('rect, line, text, image');
            const deltaY = -minY;
            elements.forEach(el => adjustElementY(el, deltaY));
        }}

        function isPageEmptyOrRectOnly(page) {{
            const svg = page.querySelector('svg');
            if (!svg) {{
                return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
            }}
            const elements = svg.querySelectorAll('text, line, image');
            return !elements.length;
        }}

        function createPageFromGroup(group, groupMinY, groupHeight) {{
            const newPage = document.createElement('div');
            newPage.className = 'page';
            newPage.style.minHeight = '1120px';
            const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

            group.forEach(el => {{
                const cloned = el.cloneNode(true);
                adjustElementY(cloned, -groupMinY);
                newSvg.appendChild(cloned);
            }});
            adjustSvg(newSvg, groupMinY);
            newSvg.setAttribute('viewBox', '0 0 ' + width + ' ' + groupHeight);
            newSvg.style.height = groupHeight + 'px';
            newSvg.style.width = '100%';
            newSvg.style.display = 'block';
            newSvg.style.margin = '0';
            newSvg.style.padding = '0';
            newPage.appendChild(newSvg);
            return newPage;
        }}

        function createBreakDiv(isUser) {{
            const breakDiv = document.createElement('div');
            breakDiv.className = isUser ? 'page-break' : 'page-break auto-page-break';
            breakDiv.style.position = 'relative';
            breakDiv.style.height = '20px';
            breakDiv.style.margin = '10px 0';
            breakDiv.style.backgroundColor = isUser ? '#ff6b6b' : '#4ecdc4';
            breakDiv.style.borderRadius = '4px';
            breakDiv.style.display = 'flex';
            breakDiv.style.alignItems = 'center';
            breakDiv.style.justifyContent = 'center';
            breakDiv.style.color = 'white';
            breakDiv.style.fontSize = '12px';
            breakDiv.style.fontWeight = 'bold';
            breakDiv.style.cursor = 'pointer';
            breakDiv.textContent = isUser ? 'Manual Page Break' : 'Auto Page Break';
            return breakDiv;
        }}

        function insertBreaks(pages, isAuto) {{
            if (pages.length <= 1) return pages;
            const result = [pages[0]];
            for (let i = 1; i < pages.length; i++) {{
                result.push(createBreakDiv(!isAuto), pages[i]);
            }}
            return result;
        }}

        function splitIntoPages(frag) {{
            if (!frag.hasChildNodes()) {{
                console.warn('Fragment is empty, returning no pages');
                return [];
            }}

            const tempPage = document.createElement('div');
            tempPage.className = 'page';
            tempPage.appendChild(frag);
            const maxPageHeight = 1120;

            const svgs = tempPage.querySelectorAll('svg');
            if (svgs.length === 0) {{
                editor.appendChild(tempPage);
                let height = tempPage.offsetHeight;
                editor.removeChild(tempPage);
                if (height > maxPageHeight) {{
                    tempPage.style.maxHeight = maxPageHeight + 'px';
                    tempPage.style.overflow = 'hidden';
                    height = maxPageHeight;
                }}
                tempPage.style.minHeight = maxPageHeight + 'px';
                return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
            }}

            if (svgs.length > 1) {{
                return [tempPage]; // Simplified for multiple SVGs
            }}

            const svg = svgs[0];
            const {{ height, minY }} = estimateSvgHeight(svg);
            
            adjustSvg(svg, minY);

            if (height <= maxPageHeight) {{
                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
                svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                tempPage.style.minHeight = maxPageHeight + 'px';
                return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
            }}

            const pages = [];
            const elements = Array.from(svg.children).sort((a, b) => getMinY(a) - getMinY(b));
            let currentGroup = [];
            let currentMinY = Infinity;
            let currentMaxBottom = -Infinity;

            elements.forEach(el => {{
                const elMinY = getMinY(el);
                const elMaxBottom = getMaxBottom(el);
                
                const potentialMinY = currentGroup.length === 0 ? elMinY : Math.min(currentMinY, elMinY);
                const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
                const potentialHeight = potentialMaxBottom - potentialMinY;

                if (currentGroup.length > 0 && potentialHeight > maxPageHeight) {{
                    const groupHeight = currentMaxBottom - currentMinY;
                    const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
                    if (!isPageEmptyOrRectOnly(newPage)) {{
                        pages.push(newPage);
                    }}
                    currentGroup = [el];
                    currentMinY = elMinY;
                    currentMaxBottom = elMaxBottom;
                }} else {{
                    currentGroup.push(el);
                    currentMinY = potentialMinY;
                    currentMaxBottom = potentialMaxBottom;
                }}
            }});

            if (currentGroup.length > 0) {{
                const groupHeight = currentMaxBottom - currentMinY;
                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
                if (!isPageEmptyOrRectOnly(newPage)) {{
                    pages.push(newPage);
                }}
            }}

            return pages;
        }}

        function adjustAllSvgs() {{
            const allSvgs = document.querySelectorAll('svg');
            allSvgs.forEach(svg => {{
                svg.removeAttribute('height');
                svg.removeAttribute('style');
                const heightData = estimateSvgHeight(svg);
                const height = heightData.height;
                const minY = heightData.minY;
                adjustSvg(svg, minY);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                const width = svg.viewBox?.baseVal?.width || 816;
                svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            }});
        }}

        function cleanupEmptyOrRectOnlyPages() {{
            const pages = Array.from(editor.querySelectorAll('.page'));
            pages.forEach(page => {{
                if (isPageEmptyOrRectOnly(page)) {{
                    const prevSibling = page.previousElementSibling;
                    if (prevSibling && prevSibling.classList.contains('page-break')) {{
                        prevSibling.remove();
                    }}
                    page.remove();
                }}
            }});
        }}

        // Start sequential restoration  
        await restoreBreakSequentially(0);

        // Estimate SVG height
        function estimateSvgHeight(svg, withPadding = true) {{
            const textElements = svg.querySelectorAll('text');
            const rectElements = svg.querySelectorAll('rect');
            const lineElements = svg.querySelectorAll('line');
            const imageElements = svg.querySelectorAll('image');
            let maxBottom = 0;
            let minY = Infinity;
            const parseAttr = (el, attr) => {{
                const val = el.getAttribute(attr);
                return val ? parseFloat(val) : 0;
            }};

            textElements.forEach(textEl => {{
                let fontSize = window.getComputedStyle(textEl).fontSize;
                fontSize = fontSize ? parseFloat(fontSize) : 16;
                const lineHeight = fontSize * 1.2;
                let lines = 1;
                const tspans = textEl.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    let maxTspanY = 0;
                    let minTspanY = Infinity;
                    tspans.forEach(tspan => {{
                        const tspanY = parseAttr(tspan, 'y') || parseAttr(textEl, 'y');
                        if (tspanY > maxTspanY) maxTspanY = tspanY;
                        if (tspanY < minTspanY) minTspanY = tspanY;
                    }});
                    if (maxTspanY > 0) {{
                        const bottom = maxTspanY + lineHeight;
                        if (bottom > maxBottom) maxBottom = bottom;
                    }}
                    if (minTspanY < minY) minY = minTspanY;
                    lines = tspans.length;
                }} else {{
                    lines = textEl.textContent.split('\n').length;
                    const y = parseAttr(textEl, 'y');
                    if (y < minY) minY = y;
                    const bottom = y + lines * lineHeight;
                    if (bottom > maxBottom) maxBottom = bottom;
                }}
            }});

            rectElements.forEach(rectEl => {{
                const y = parseAttr(rectEl, 'y');
                const height = parseAttr(rectEl, 'height');
                if (y < minY) minY = y;
                const bottom = y + height;
                if (bottom > maxBottom) maxBottom = bottom;
            }});

            lineElements.forEach(lineEl => {{
                const y1 = parseAttr(lineEl, 'y1');
                const y2 = parseAttr(lineEl, 'y2');
                const minLineY = Math.min(y1, y2);
                const maxLineY = Math.max(y1, y2);
                if (minLineY < minY) minY = minLineY;
                if (maxLineY > maxBottom) maxBottom = maxLineY;
                const bottom = Math.max(y1, y2);
                const min = Math.min(y1, y2);
                if (min < minY) minY = min;
                if (bottom > maxBottom) maxBottom = bottom;
            }});

            imageElements.forEach(imageEl => {{
                const y = parseAttr(imageEl, 'y');
                const height = parseAttr(imageEl, 'height');
                if (y < minY) minY = y;
                const bottom = y + height;
                if (bottom > maxBottom) maxBottom = bottom;
            }});

            if (maxBottom <= 0 || minY === Infinity) {{
                const viewBox = svg.getAttribute('viewBox');
                if (viewBox) {{
                    const parts = viewBox.split(/\s+/);
                    if (parts.length >= 4) {{
                        const height = parseFloat(parts[3]);
                        minY = parseFloat(parts[1]);
                        if (height > 0) maxBottom = minY + height;
                    }}
                }} else {{
                    minY = 0;
                    maxBottom = svg.clientHeight || 1000;
                }}
            }}

            return {{ height: Math.max(maxBottom - minY + (withPadding ? 20 : 0), 50), minY: minY === Infinity ? 0 : minY }};
        }}

        // Adjust SVG viewBox and shift elements
        function adjustSvg(svg, minY) {{
            const elements = svg.querySelectorAll('rect, line, text, image');
            const deltaY = -minY;
            elements.forEach(el => {{
                ['y', 'y1', 'y2'].forEach(attr => {{
                    if (el.hasAttribute(attr)) {{
                        const val = parseFloat(el.getAttribute(attr));
                        el.setAttribute(attr, val + deltaY);
                    }}
                }});
                if (el.tagName.toLowerCase() === 'text') {{
                    el.querySelectorAll('tspan').forEach(tspan => {{
                        const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
                        tspan.setAttribute('y', y + deltaY);
                    }});
                }}
            }});
        }}

        // Apply adjustments to SVGs
        function adjustAllSvgs() {{
            const allSvgs = document.querySelectorAll('svg');
            allSvgs.forEach(svg => {{
                svg.removeAttribute('height');
                svg.removeAttribute('style');
                const {{ height, minY }} = estimateSvgHeight(svg, true);
                adjustSvg(svg, minY);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                const width = svg.viewBox?.baseVal?.width || 816;
                svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            }});
        }}

        // Helper: Split text at index
        function splitTextAtIndex(textEl, splitIndex) {{
            const tspans = textEl.querySelectorAll('tspan');
            let lines = [];
            let hasTspans = false;
            if (tspans.length > 0) {{
                hasTspans = true;
                lines = Array.from(tspans).map(tspan => ({{element: tspan, text: tspan.textContent}}));
            }} else {{
                lines = textEl.textContent.split('\n').map(text => ({{text}}));
            }}
            if (splitIndex <= 0 || splitIndex >= lines.length) {{
                console.log('No split needed within text element');
                return null;
            }}
            const newText = textEl.cloneNode(false);
            if (hasTspans) {{
                for (let i = splitIndex; i < lines.length; i++) {{
                    newText.appendChild(lines[i].element);
                }}
            }} else {{
                const beforeLines = lines.slice(0, splitIndex).map(l => l.text);
                const afterLines = lines.slice(splitIndex).map(l => l.text);
                textEl.textContent = beforeLines.join('\n');
                newText.textContent = afterLines.join('\n');
                const fontSize = parseFloat(window.getComputedStyle(textEl).fontSize) || 16;
                const lineHeight = fontSize * 1.2;
                const textY = parseFloat(textEl.getAttribute('y'));
                newText.setAttribute('y', textY + splitIndex * lineHeight);
                for (let attr of textEl.attributes) {{
                    if (attr.name !== 'y') {{
                        newText.setAttribute(attr.name, attr.value);
                    }}
                }}
            }}
            textEl.parentNode.insertBefore(newText, textEl.nextSibling);
            const range = document.createRange();
            range.setStartAfter(textEl);
            range.setEndBefore(newText);
            console.log('Split text at index ' + splitIndex);
            return range;
        }}

        // Helper: Create page from fragment
        function createPageFromFrag(frag) {{
            if (!frag.hasChildNodes()) {{
                console.log('Fragment is empty, skipping page creation');
                return null;
            }}
            const newPage = document.createElement('div');
            newPage.className = 'page';
            newPage.style.minHeight = '1120px';
            const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            while (frag.firstChild) {{
                newSvg.appendChild(frag.firstChild);
            }}
            const {{ height, minY }} = estimateSvgHeight(newSvg, true);
            adjustSvg(newSvg, minY);
            const width = newSvg.viewBox?.baseVal?.width || newSvg.clientWidth || 816;
            newSvg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            newSvg.style.height = height + 'px';
            newSvg.style.width = '100%';
            newSvg.style.display = 'block';
            newSvg.style.margin = '0';
            newSvg.style.padding = '0';
            newPage.appendChild(newSvg);
            if (isPageEmptyOrRectOnly(newPage)) {{
                console.log('Page is empty or rect-only, discarding');
                return null;
            }}
            console.log('Created new page with height:', height);
            return newPage;
        }}

        // Helper: Check if page is empty or rect-only
        function isPageEmptyOrRectOnly(page) {{
            const svg = page.querySelector('svg');
            if (!svg) {{
                return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
            }}
            const elements = svg.querySelectorAll('text, line, image');
            const isEmpty = !elements.length;
            console.log('Page empty check:', isEmpty);
            return isEmpty;
        }}

        // Helper: Create break div
        function createBreakDiv() {{
            const breakDiv = document.createElement('div');
            breakDiv.className = 'page-break';
            breakDiv.style.position = 'relative';
            breakDiv.style.height = '20px';
            breakDiv.style.margin = '10px 0';
            breakDiv.style.backgroundColor = '#e0e0e0';
            breakDiv.style.borderTop = '1px dashed #ccc';
            breakDiv.style.borderBottom = '1px dashed #ccc';
            breakDiv.style.pageBreakBefore = 'always';
            const removeBtn = document.createElement('div');
            removeBtn.className = 'page-break-remove';
            removeBtn.innerHTML = '✂️';
            removeBtn.style.position = 'absolute';
            removeBtn.style.left = '50%';
            removeBtn.style.top = '50%';
            removeBtn.style.transform = 'translate(-50%, -50%)';
            removeBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.9)';
            removeBtn.style.color = '#333';
            removeBtn.style.borderRadius = '4px';
            removeBtn.style.padding = '3px 8px';
            removeBtn.style.display = 'none';
            removeBtn.style.cursor = 'pointer';
            breakDiv.appendChild(removeBtn);
            removeBtn.addEventListener('click', function(e) {{
                e.stopPropagation();
                removePageBreak(breakDiv);
            }});
            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
            return breakDiv;
        }}

        // Fallback yOffset function with context matching
        function createRangeAtGlobalYOffset(globalYOffset, targetXOffset, context) {{
            console.log(`Creating range at yOffset: ` + globalYOffset + `, xOffset: ` + targetXOffset + `, context:`, context);
            const allPages = Array.from(editor.children).filter(p => p.classList.contains('page'));
            let cumulativeHeight = 0;
            const positionCorrectionFactor = 0;
            const correctedYOffset = globalYOffset - positionCorrectionFactor;

            for (let i = 0; i < allPages.length; i++) {{
                const page = allPages[i];
                const svg = page.querySelector('svg');
                if (!svg) {{
                    console.warn('No SVG found in page', i);
                    continue;
                }}
                const {{ height: pageHeight }} = estimateSvgHeight(svg, true);
                console.log('Page ' + i + ' height: ' + pageHeight + ', cumulativeHeight: ' + cumulativeHeight);

                if (correctedYOffset >= cumulativeHeight && correctedYOffset <= cumulativeHeight + pageHeight) {{
                    const localYOffset = correctedYOffset - cumulativeHeight;
                    const svgElements = Array.from(svg.querySelectorAll('text, rect, line, image')).map(el => {{
                        const signature = getElementSignature(el);
                        return {{ element: el, signature, y: getMinY(el), bottom: getMaxBottom(el), x: getX(el) }};
                    }}).sort((a, b) => {{
                        const yDiff = a.y - b.y;
                        if (yDiff === 0) {{
                            return a.x - b.x;
                        }}
                        return yDiff;
                    }});

                    console.log('Elements in page ' + i + ' for yOffset ' + localYOffset + ':', svgElements.map(e => ({{content: e.signature.content,
                        y: e.y,
                        x: e.x,
                        bottom: e.bottom
                    }})));

                    // Check for straddling element (text element that spans the yOffset)
                    let straddlingElement = null;
                    svgElements.forEach(item => {{
                        if (item.y <= localYOffset && localYOffset < item.bottom) {{
                            straddlingElement = item.element;
                        }}
                    }});

                    if (straddlingElement && straddlingElement.tagName.toLowerCase() === 'text') {{
                        const splitRange = splitTextAtYOffset(straddlingElement, localYOffset, targetXOffset);
                        if (splitRange) {{
                            console.log('Split text element at yOffset:', localYOffset);
                            return {{ range: splitRange, page: page, pageIndex: i }};
                        }}
                    }}

                    // Find the closest element by yOffset and xOffset
                    let bestElement = null;
                    let minDistance = Infinity;
                    let contextMatches = 0;

                    svgElements.forEach(item => {{
                        const yDistance = Math.abs(localYOffset - item.bottom);
                        const xDistance = Math.abs(targetXOffset - item.x);
                        const combinedDistance = yDistance + xDistance * 0.1; // Weight x less heavily
                        let contextScore = 0;

                        // Check context for additional matching
                        if (context && Array.isArray(context)) {{
                            context.forEach(ctx => {{
                                if (item.signature.hash === ctx.hash) {{
                                    contextScore += 50;
                                }}
                            }});
                        }}

                        if (item.bottom <= localYOffset && combinedDistance < minDistance) {{
                            minDistance = combinedDistance;
                            bestElement = item.element;
                            contextMatches = contextScore;
                        }}
                    }});

                    // Use context elements to refine selection if no direct match
                    if (!bestElement && context && Array.isArray(context)) {{
                        let bestContextElement = null;
                        let minContextDistance = Infinity;
                        svgElements.forEach(item => {{
                            context.forEach(ctx => {{
                                if (item.signature.hash === ctx.hash) {{
                                    const yDistance = Math.abs(parseFloat(ctx.attributes.y || 0) - localYOffset);
                                    const xDistance = Math.abs(parseFloat(ctx.attributes.x || 0) - targetXOffset);
                                    const combinedDistance = yDistance + xDistance * 0.1;
                                    if (combinedDistance < minContextDistance) {{
                                        minContextDistance = combinedDistance;
                                        bestContextElement = item.element;
                                    }}
                                }}
                            }});
                        }});
                        if (bestContextElement) {{
                            bestElement = bestContextElement;
                            console.log('Selected element based on context match at y:', getMinY(bestContextElement));
                        }}
                    }}

                    const range = document.createRange();
                    if (bestElement) {{
                        range.setStartAfter(bestElement);
                        range.setEndAfter(bestElement);
                        console.log('Range set after element at y:', getMinY(bestElement), 'content:', bestElement.textContent?.substring(0, 50));
                    }} else {{
                        // Fallback to the last element in the page if no suitable element is found
                        const lastElement = svgElements[svgElements.length - 1]?.element;
                        if (lastElement) {{
                            range.setStartAfter(lastElement);
                            range.setEndAfter(lastElement);
                            console.log('Range set after last element in page at y:', getMinY(lastElement));
                        }} else {{
                            range.setStart(svg, 0);
                            range.setEnd(svg, 0);
                            console.log('No suitable element found, range set at start of SVG');
                        }}
                    }}
                    return {{ range: range, page: page, pageIndex: i }};
                }}
                cumulativeHeight += pageHeight;
            }}

            // If yOffset is beyond all pages, use the last page
            const lastPage = allPages[allPages.length - 1];
            if (lastPage) {{
                const range = document.createRange();
                const svg = lastPage.querySelector('svg');
                const childCount = svg ? svg.childNodes.length : lastPage.childNodes.length;
                range.setStart(svg || lastPage, childCount);
                range.setEnd(svg || lastPage, childCount);
                console.log('Range set at end of last page');
                return {{ range: range, page: lastPage, pageIndex: allPages.length - 1 }};
            }}
            console.warn('No suitable page found for yOffset:', globalYOffset);
            return null;
        }}

        function parseAttr(el, attr) {{
            const val = el.getAttribute(attr);
            return val ? parseFloat(val) : 0;
        }}

        function getMinY(el) {{
            let minY = Infinity;
            if (el.tagName.toLowerCase() === 'text') {{
                const y = parseAttr(el, 'y');
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    tspans.forEach(tspan => {{
                        const tspanY = parseAttr(tspan, 'y') || y;
                        minY = Math.min(minY, tspanY);
                    }});
                }} else {{
                    minY = y;
                }}
            }} else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {{
                minY = parseAttr(el, 'y');
            }} else if (el.tagName.toLowerCase() === 'line') {{
                minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
            }}
            return minY === Infinity ? 0 : minY;
        }}

        function getMaxBottom(el) {{
            let maxBottom = 0;
            let fontSize = window.getComputedStyle(el).fontSize;
            fontSize = parseFloat(fontSize) || 16;
            const lineHeight = fontSize * 1.2;
            if (el.tagName.toLowerCase() === 'text') {{
                const y = parseAttr(el, 'y');
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    let maxTspanY = 0;
                    tspans.forEach(tspan => {{
                        const tspanY = parseAttr(tspan, 'y') || y;
                        maxTspanY = Math.max(maxTspanY, tspanY);
                    }});
                    maxBottom = maxTspanY + lineHeight;
                }} else {{
                    const lines = (el.textContent || '').split('\n').length;
                    maxBottom = y + lines * lineHeight;
                }}
            }} else if (el.tagName.toLowerCase() === 'rect') {{
                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
            }} else if (el.tagName.toLowerCase() === 'image') {{
                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
            }} else if (el.tagName.toLowerCase() === 'line') {{
                maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
            }}
            return maxBottom;
        }}

        function splitTextAtYOffset(textEl, splitYOffset, targetXOffset) {{
            const fontSize = parseFloat(window.getComputedStyle(textEl).fontSize) || 16;
            const lineHeight = fontSize * 1.2;
            const textY = parseAttr(textEl, 'y');
            const lines = [];
            let hasTspans = false;
            const tspans = textEl.querySelectorAll('tspan');
            if (tspans.length > 0) {{
                hasTspans = true;
                tspans.forEach(tspan => {{
                    const y = parseAttr(tspan, 'y') || textY;
                    const x = parseAttr(tspan, 'x') || parseAttr(textEl, 'x');
                    lines.push({{ y, x, bottom: y + lineHeight, element: tspan }});
                }});
            }} else {{
                const textLines = (textEl.textContent || '').split('\n').map((lineText, idx) => {{
                    const y = textY + idx * lineHeight;
                    const x = parseAttr(textEl, 'x');
                    return {{ y, x, bottom: y + lineHeight, text: lineText }};
                }});
                lines = lines.concat(textLines);
            }}
            lines.sort((a, b) => {{
                const yDiff = a.y - b.y;
                if (yDiff === 0) {{
                    return a.x - b.x;
                }}
                return yDiff;
            }});
            let splitIndex = 0;
            let minXDistance = Infinity;
            for (let i = 0; i < lines.length; i++) {{
                if (lines[i].bottom > splitYOffset) {{
                    const xDistance = Math.abs(lines[i].x - targetXOffset);
                    if (xDistance < minXDistance) {{
                        minXDistance = xDistance;
                        splitIndex = i;
                    }}
                }}
            }}
            if (splitIndex === 0 || splitIndex === lines.length) {{
                console.log('No split needed within text element');
                return null;
            }}
            const newText = textEl.cloneNode(false);
            if (hasTspans) {{
                for (let i = splitIndex; i < lines.length; i++) {{
                    newText.appendChild(lines[i].element);
                }}
            }} else {{
                const beforeLines = lines.slice(0, splitIndex).map(l => l.text);
                const afterLines = lines.slice(splitIndex).map(l => l.text);
                textEl.textContent = beforeLines.join('\n');
                newText.textContent = afterLines.join('\n');
                const newY = lines[splitIndex].y;
                newText.setAttribute('y', newY);
                for (let attr of textEl.attributes) {{
                    if (attr.name !== 'y') {{
                        newText.setAttribute(attr.name, attr.value);
                    }}
                }}
            }}
            textEl.parentNode.insertBefore(newText, textEl.nextSibling);
            const range = document.createRange();
            range.setStartAfter(textEl);
            range.setEndBefore(newText);
            console.log('Split text at yOffset ' + splitYOffset + ', xOffset ' + targetXOffset);
            return range;
        }}

        function getX(el) {{
            let x = parseAttr(el, 'x') || parseAttr(el, 'x1') || 0;
            if (el.tagName.toLowerCase() === 'text') {{
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {{
                    x = parseAttr(tspans[0], 'x') || parseAttr(el, 'x') || 0;
                }}
            }}
            return x;
        }}

        // Remove page break
        function removePageBreak(pageBreakElement) {{
            const prevPage = pageBreakElement.previousElementSibling;
            const nextPage = pageBreakElement.nextElementSibling;
            if (!prevPage || !nextPage || !prevPage.classList.contains('page') || !nextPage.classList.contains('page')) {{
                console.error('Cannot find adjacent pages to merge');
                return false;
            }}
            while (nextPage.firstChild) {{
                prevPage.appendChild(nextPage.firstChild);
            }}
            pageBreakElement.remove();
            nextPage.remove();
            prevPage.querySelectorAll('svg').forEach(svg => {{
                svg.removeAttribute('height');
                svg.style.removeProperty('margin-top');
                svg.style.removeProperty('margin-bottom');
                svg.style.removeProperty('padding-top');
                svg.style.removeProperty('padding-bottom');
                let parent = svg.parentElement;
                while (parent && parent !== prevPage) {{
                    parent.style.removeProperty('margin-top');
                    parent.style.removeProperty('margin-bottom');
                    parent.style.removeProperty('padding-top');
                    parent.style.removeProperty('padding-bottom');
                    parent = parent.parentElement;
                }}
            }});
            const svgList = prevPage.querySelectorAll('svg');
            svgList.forEach(svg => {{
                const {{ height, minY }} = estimateSvgHeight(svg, true);
                adjustSvg(svg, minY);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
                svg.setAttribute('viewBox', `0 0 ${{width}} ${{height}}`);
            }});
            prevPage.style.display = 'none';
            void prevPage.offsetHeight;
            prevPage.style.display = '';
            savePageBreakPositions();
            return true;
        }}

        adjustAllSvgs();

        // Cleanup empty #content divs
        const contentDivs = document.querySelectorAll('div#content');
        contentDivs.forEach(div => {{
            const hasMeaningfulContent = Array.from(div.querySelectorAll('*')).some(el => {{
                if (el.tagName === 'SVG') {{
                    return el.textContent.trim() !== '' || el.querySelector('rect, text, line');
                }}
                if (el.tagName === 'TEXT') {{
                    return el.textContent.trim() !== '';
                }}
                return el.textContent.trim() !== '';
            }});
            if (!hasMeaningfulContent) {{
                console.log('Removing empty content div');
                div.remove();
            }}
        }});
    }} catch (error) {{
        console.error('Error in RestorePageBreaks:', error);
        if (typeof window.notifyError === 'function') {{
            window.notifyError(error.message);
        }}
    }}
}})();
";

                await WaitForBrowserReady();
                var result = await browser.EvaluateScriptAsync(script);
                if (result.Success)
                {
                    System.Diagnostics.Debug.WriteLine("RestorePageBreaks script executed successfully");
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

        //public async Task RestorePageBreaks()
        //{
        //    try
        //    {
        //        if (string.IsNullOrEmpty(_currentReportPath) || string.IsNullOrEmpty(_currentReportContent))
        //        {
        //            System.Diagnostics.Debug.WriteLine("No report path or content provided, skipping restore.");
        //            return;
        //        }

        //        PageBreakData pageBreakData = _pageBreakManager.GetPageBreakData(_currentReportPath, _currentReportContent);
        //        if (pageBreakData?.PageBreakPositions == null || pageBreakData.PageBreakPositions.Count == 0)
        //        {
        //            System.Diagnostics.Debug.WriteLine("No page break data found, skipping restore.");
        //            return;
        //        }

        //        string positionsJson = JsonSerializer.Serialize(pageBreakData.PageBreakPositions);
        //        System.Diagnostics.Debug.WriteLine($"Restoring page breaks with positions: {positionsJson}");

        //        string script = $@"
        //(function() {{
        //    try {{
        //        console.log('RestorePageBreaks script started');

        //        // Get the editor element
        //        const editor = document.getElementById('editor');
        //        if (!editor) {{
        //            console.error('Editor element not found');
        //            return;
        //        }}

        //        // Flatten nested .page containers (keep existing splits intact)
        //        document.querySelectorAll('.page .page').forEach(nested => {{
        //            const outer = nested.closest('.page');
        //            if (outer) {{
        //                while (nested.firstChild) {{
        //                    outer.parentNode.insertBefore(nested.firstChild, outer);
        //                }}
        //                nested.remove();
        //            }}
        //        }});

        //        // Parse page break positions
        //        const pageBreakPositions = {positionsJson};
        //        let breakPositions = pageBreakPositions.map(pos => {{
        //            let yOffset = parseFloat(pos.YOffset || pos.yOffset || 0);
        //            let xOffset = parseFloat(pos.XOffset || pos.xOffset || 0);
        //            return {{ yOffset: isNaN(yOffset) ? 0 : yOffset, xOffset: isNaN(xOffset) ? 0 : xOffset }};
        //        }}).filter(pos => pos.yOffset > 0).sort((a, b) => a.yOffset - b.yOffset);

        //        // Filter closely spaced breaks
        //        const MIN_OFFSET_DIFFERENCE = 10;
        //        const filteredPositions = [];
        //        let lastOffset = -MIN_OFFSET_DIFFERENCE * 2;
        //        breakPositions.forEach(pos => {{
        //            if (pos.yOffset - lastOffset >= MIN_OFFSET_DIFFERENCE) {{
        //                filteredPositions.push(pos);
        //                lastOffset = pos.yOffset;
        //                console.log('Including break at yOffset: ' + pos.yOffset + ', xOffset: ' + pos.xOffset);
        //            }} else {{
        //                console.log('Skipping break at yOffset: ' + pos.yOffset + ', too close to previous: ' + lastOffset);
        //            }}
        //        }});
        //        breakPositions = filteredPositions;

        //        // Calculate SVG height and minimum y coordinate
        //        function estimateSvgHeight(svg, withPadding = true) {{
        //            const textElements = svg.querySelectorAll('text');
        //            const rectElements = svg.querySelectorAll('rect');
        //            const lineElements = svg.querySelectorAll('line');
        //            const imageElements = svg.querySelectorAll('image');

        //            let maxBottom = 0;
        //            let minY = Infinity;

        //            const parseAttr = (el, attr) => {{
        //                const val = el.getAttribute(attr);
        //                return val ? parseFloat(val) : 0;
        //            }};

        //            textElements.forEach(textEl => {{
        //                let fontSize = window.getComputedStyle(textEl).fontSize;
        //                fontSize = fontSize ? parseFloat(fontSize) : 16;
        //                const lineHeight = fontSize * 1.2;

        //                let lines = 1;
        //                const tspans = textEl.querySelectorAll('tspan');
        //                if (tspans.length > 0) {{
        //                    let maxTspanY = 0;
        //                    let minTspanY = Infinity;
        //                    tspans.forEach(tspan => {{
        //                        const tspanY = parseAttr(tspan, 'y') || parseAttr(textEl, 'y');
        //                        if (tspanY > maxTspanY) maxTspanY = tspanY;
        //                        if (tspanY < minTspanY) minTspanY = tspanY;
        //                    }});
        //                    if (maxTspanY > 0) {{
        //                        const bottom = maxTspanY + lineHeight;
        //                        if (bottom > maxBottom) maxBottom = bottom;
        //                    }}
        //                    if (minTspanY < minY) minY = minTspanY;
        //                    lines = tspans.length;
        //                }} else {{
        //                    lines = textEl.textContent.split('\\n').length;
        //                    const y = parseAttr(textEl, 'y');
        //                    if (y < minY) minY = y;
        //                    const bottom = y + lines * lineHeight;
        //                    if (bottom > maxBottom) maxBottom = bottom;
        //                }}
        //            }});

        //            rectElements.forEach(rectEl => {{
        //                const y = parseAttr(rectEl, 'y');
        //                const height = parseAttr(rectEl, 'height');
        //                if (y < minY) minY = y;
        //                const bottom = y + height;
        //                if (bottom > maxBottom) maxBottom = bottom;
        //            }});

        //            lineElements.forEach(lineEl => {{
        //                const y1 = parseAttr(lineEl, 'y1');
        //                const y2 = parseAttr(lineEl, 'y2');
        //                const bottom = Math.max(y1, y2);
        //                const min = Math.min(y1, y2);
        //                if (min < minY) minY = min;
        //                if (bottom > maxBottom) maxBottom = bottom;
        //            }});

        //            imageElements.forEach(imageEl => {{
        //                const y = parseAttr(imageEl, 'y');
        //                const height = parseAttr(imageEl, 'height');
        //                if (y < minY) minY = y;
        //                const bottom = y + height;
        //                if (bottom > maxBottom) maxBottom = bottom;
        //            }});

        //            if (maxBottom <= 0 || minY === Infinity) {{
        //                const viewBox = svg.getAttribute('viewBox');
        //                if (viewBox) {{
        //                    const parts = viewBox.split(/[\\s]+/);
        //                    if (parts.length >= 4) {{
        //                        const height = parseFloat(parts[3]);
        //                        minY = parseFloat(parts[1]);
        //                        if (height > 0) maxBottom = minY + height;
        //                    }}
        //                }} else {{
        //                    minY = 0;
        //                    maxBottom = svg.clientHeight || 1000;
        //                }}
        //            }}

        //            return {{
        //                height: Math.max(maxBottom - minY + (withPadding ? 20 : 0), 50),
        //                minY: minY === Infinity ? 0 : minY
        //            }};
        //        }}

        //        // Adjust SVG viewBox and shift elements
        //        function adjustSvg(svg, minY) {{
        //            const elements = svg.querySelectorAll('rect, line, text, image');
        //            const deltaY = -minY;

        //            elements.forEach(el => {{
        //                ['y', 'y1', 'y2'].forEach(attr => {{
        //                    if (el.hasAttribute(attr)) {{
        //                        const val = parseFloat(el.getAttribute(attr));
        //                        el.setAttribute(attr, val + deltaY);
        //                    }}
        //                }});
        //                if (el.tagName.toLowerCase() === 'text') {{
        //                    el.querySelectorAll('tspan').forEach(tspan => {{
        //                        const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
        //                        tspan.setAttribute('y', y + deltaY);
        //                    }});
        //                }}
        //            }});
        //        }}

        //        // Apply adjustments to SVGs
        //        function adjustAllSvgs() {{
        //            const allSvgs = document.querySelectorAll('svg');
        //            allSvgs.forEach(svg => {{
        //                svg.removeAttribute('height');
        //                svg.removeAttribute('style');
        //                const {{ height, minY }} = estimateSvgHeight(svg, true);
        //                adjustSvg(svg, minY);
        //                svg.style.height = height + 'px';
        //                svg.style.width = '100%';
        //                svg.style.display = 'block';
        //                svg.style.margin = '0';
        //                svg.style.padding = '0';
        //                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
        //                svg.setAttribute('viewBox', `0 0 ${{width}} ${{height}}`);
        //            }});
        //        }}

        //        // Helper function to split a text element at a specific yOffset and xOffset
        //        function splitTextAtYOffset(textEl, splitYOffset, targetXOffset) {{
        //            const parseAttr = (el, attr) => {{
        //                const val = el.getAttribute(attr);
        //                return val ? parseFloat(val) : 0;
        //            }};

        //            const fontSize = parseFloat(window.getComputedStyle(textEl).fontSize) || 16;
        //            const lineHeight = fontSize * 1.2;
        //            const textY = parseAttr(textEl, 'y');
        //            const lines = [];
        //            let hasTspans = false;

        //            const tspans = textEl.querySelectorAll('tspan');
        //            if (tspans.length > 0) {{
        //                hasTspans = true;
        //                tspans.forEach(tspan => {{
        //                    const y = parseAttr(tspan, 'y') || textY;
        //                    const x = parseAttr(tspan, 'x') || parseAttr(textEl, 'x');
        //                    lines.push({{ y, x, bottom: y + lineHeight, element: tspan }});
        //                }});
        //            }} else {{
        //                const textLines = (textEl.textContent || '').split('\\n');
        //                textLines.forEach((lineText, idx) => {{
        //                    const y = textY + idx * lineHeight;
        //                    const x = parseAttr(textEl, 'x');
        //                    lines.push({{ y, x, bottom: y + lineHeight, text: lineText }});
        //                }});
        //            }}

        //            // Sort lines by y, then x
        //            lines.sort((a, b) => {{
        //                const yDiff = a.y - b.y;
        //                if (yDiff === 0) {{
        //                    return a.x - b.x;
        //                }}
        //                return yDiff;
        //            }});

        //            // Find the split index: first line where bottom > splitYOffset and x is closest to targetXOffset
        //            let splitIndex = 0;
        //            let minXDistance = Infinity;
        //            for (let i = 0; i < lines.length; i++) {{
        //                if (lines[i].bottom > splitYOffset) {{
        //                    const xDistance = Math.abs(lines[i].x - targetXOffset);
        //                    if (xDistance < minXDistance) {{
        //                        minXDistance = xDistance;
        //                        splitIndex = i;
        //                    }}
        //                }}
        //            }}

        //            if (splitIndex === 0 || splitIndex === lines.length) {{
        //                console.log('No split needed within text element');
        //                return null;
        //            }}

        //            // Create new text element
        //            const newText = textEl.cloneNode(false); // Shallow clone for attributes

        //            if (hasTspans) {{
        //                // Move tspans from splitIndex onward to newText
        //                for (let i = splitIndex; i < lines.length; i++) {{
        //                    newText.appendChild(lines[i].element);
        //                }}
        //            }} else {{
        //                // Split text content
        //                const beforeLines = lines.slice(0, splitIndex).map(l => l.text);
        //                const afterLines = lines.slice(splitIndex).map(l => l.text);
        //                textEl.textContent = beforeLines.join('\\n');
        //                newText.textContent = afterLines.join('\\n');

        //                // Set y for newText
        //                const newY = lines[splitIndex].y;
        //                newText.setAttribute('y', newY);

        //                // Copy all other attributes
        //                for (let attr of textEl.attributes) {{
        //                    if (attr.name !== 'y') {{
        //                        newText.setAttribute(attr.name, attr.value);
        //                    }}
        //                }}
        //            }}

        //            // Insert newText after original textEl
        //            textEl.parentNode.insertBefore(newText, textEl.nextSibling);

        //            // Create range between the two parts
        //            const range = document.createRange();
        //            range.setStartAfter(textEl);
        //            range.setEndBefore(newText);
        //            console.log(`Split text at yOffset ${{splitYOffset}}, xOffset ${{targetXOffset}}, new text starts at y=${{hasTspans ? lines[splitIndex].y : newY}}, x=${{lines[splitIndex].x}}`);
        //            return range;
        //        }}

        //        // Helper function to get x position of an element
        //        function getX(el) {{
        //            let x = parseAttr(el, 'x') || parseAttr(el, 'x1') || 0;
        //            if (el.tagName.toLowerCase() === 'text') {{
        //                const tspans = el.querySelectorAll('tspan');
        //                if (tspans.length > 0) {{
        //                    x = parseAttr(tspans[0], 'x') || parseAttr(el, 'x') || 0;
        //                }}
        //            }}
        //            return x;
        //        }}

        //        // Helper function to create a range at a specific global yOffset and xOffset
        //        function createRangeAtGlobalYOffset(globalYOffset, targetXOffset) {{
        //            const allPages = Array.from(editor.children).filter(p => p.classList.contains('page'));
        //            let cumulativeHeight = 0;

        //            const positionCorrectionFactor = 0;
        //            const correctedYOffset = globalYOffset - positionCorrectionFactor;

        //            console.log(`Creating range at global yOffset: ${{globalYOffset}}, xOffset: ${{targetXOffset}} (corrected y: ${{correctedYOffset}})`);

        //            for (let i = 0; i < allPages.length; i++) {{
        //                const page = allPages[i];
        //                const svg = page.querySelector('svg');
        //                if (!svg) continue;

        //                const {{ height: pageHeight }} = estimateSvgHeight(svg, true);
        //                console.log(`Page ${{i}}: cumulative=${{cumulativeHeight}}, height=${{pageHeight}}, range=[${{cumulativeHeight}}, ${{cumulativeHeight + pageHeight}}], target=${{correctedYOffset}}`);

        //                if (correctedYOffset >= cumulativeHeight && correctedYOffset < cumulativeHeight + pageHeight) {{
        //                    const localYOffset = correctedYOffset - cumulativeHeight;
        //                    console.log(`Found target page ${{i}}, localYOffset: ${{localYOffset}}`);

        //                    const range = document.createRange();
        //                    const svgElements = Array.from(svg.querySelectorAll('text, rect, line, image')).sort((a, b) => {{
        //                        const yDiff = getMinY(a) - getMinY(b);
        //                        if (yDiff === 0) {{
        //                            return getX(a) - getX(b);
        //                        }}
        //                        return yDiff;
        //                    }});

        //                    // First, check for straddling element (where minY <= localYOffset < maxBottom)
        //                    let straddlingElement = null;
        //                    svgElements.forEach(el => {{
        //                        const elMinY = getMinY(el);
        //                        const elMaxBottom = getMaxBottom(el);
        //                        if (elMinY <= localYOffset && localYOffset < elMaxBottom) {{
        //                            straddlingElement = el;
        //                        }}
        //                    }});

        //                    if (straddlingElement && straddlingElement.tagName.toLowerCase() === 'text') {{
        //                        const splitRange = splitTextAtYOffset(straddlingElement, localYOffset, targetXOffset);
        //                        if (splitRange) {{
        //                            console.log('Successfully split text element for precise positioning');
        //                            return {{ range: splitRange, page: page, pageIndex: i }};
        //                        }}
        //                    }}

        //                    // Fallback to original logic if no split or not text, prioritizing xOffset
        //                    let bestElement = null;
        //                    let largestBottom = -Infinity;
        //                    let minXDistance = Infinity;

        //                    svgElements.forEach(element => {{
        //                        const elementBottom = getMaxBottom(element);
        //                        if (elementBottom <= localYOffset && elementBottom > largestBottom) {{
        //                            const elementX = getX(element);
        //                            const xDistance = Math.abs(elementX - targetXOffset);
        //                            if (xDistance < minXDistance) {{
        //                                largestBottom = elementBottom;
        //                                bestElement = element;
        //                                minXDistance = xDistance;
        //                            }}
        //                        }}
        //                    }});

        //                    if (bestElement) {{
        //                        console.log(`Found last element with bottom <= yOffset: ${{bestElement.tagName}}, bottom: ${{largestBottom}}, x: ${{getX(bestElement)}}, insertAfter: true`);
        //                        range.setStartAfter(bestElement);
        //                        range.setEndAfter(bestElement);
        //                    }} else {{
        //                        range.setStart(svg, 0);
        //                        range.setEnd(svg, 0);
        //                    }}

        //                    return {{ range: range, page: page, pageIndex: i }};
        //                }}

        //                cumulativeHeight += pageHeight;
        //            }}

        //            console.log(`yOffset ${{globalYOffset}} is beyond all pages (total height: ${{cumulativeHeight}}), using last page`);
        //            const lastPage = allPages[allPages.length - 1];
        //            if (lastPage) {{
        //                const range = document.createRange();
        //                const svg = lastPage.querySelector('svg');
        //                const childCount = svg ? svg.childNodes.length : lastPage.childNodes.length;
        //                range.setStart(svg || lastPage, childCount);
        //                range.setEnd(svg || lastPage, childCount);
        //                return {{ range: range, page: lastPage, pageIndex: allPages.length - 1 }};
        //            }}

        //            return null;
        //        }}

        //        // Helper functions from TogglePageBreak for consistent behavior
        //        const maxPageHeight = 1120;

        //        function parseAttr(el, attr) {{
        //            const val = el.getAttribute(attr);
        //            return val ? parseFloat(val) : 0;
        //        }}

        //        function getMinY(el) {{
        //            let minY = Infinity;
        //            if (el.tagName.toLowerCase() === 'text') {{
        //                const y = parseAttr(el, 'y');
        //                const tspans = el.querySelectorAll('tspan');
        //                if (tspans.length > 0) {{
        //                    tspans.forEach(tspan => {{
        //                        const tspanY = parseAttr(tspan, 'y') || y;
        //                        minY = Math.min(minY, tspanY);
        //                    }});
        //                }} else {{
        //                    minY = y;
        //                }}
        //            }} else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {{
        //                minY = parseAttr(el, 'y');
        //            }} else if (el.tagName.toLowerCase() === 'line') {{
        //                minY = Math.min(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        //            }}
        //            return minY === Infinity ? 0 : minY;
        //        }}

        //        function getMaxBottom(el) {{
        //            let maxBottom = 0;
        //            let fontSize = window.getComputedStyle(el).fontSize;
        //            fontSize = parseFloat(fontSize) || 16;
        //            const lineHeight = fontSize * 1.2;

        //            if (el.tagName.toLowerCase() === 'text') {{
        //                const y = parseAttr(el, 'y');
        //                const tspans = el.querySelectorAll('tspan');
        //                if (tspans.length > 0) {{
        //                    let maxTspanY = 0;
        //                    tspans.forEach(tspan => {{
        //                        const tspanY = parseAttr(tspan, 'y') || y;
        //                        maxTspanY = Math.max(maxTspanY, tspanY);
        //                    }});
        //                    maxBottom = maxTspanY + lineHeight;
        //                }} else {{
        //                    const lines = (el.textContent || '').split('\\n').length;
        //                    maxBottom = y + lines * lineHeight;
        //                }}
        //            }} else if (el.tagName.toLowerCase() === 'rect') {{
        //                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        //            }} else if (el.tagName.toLowerCase() === 'image') {{
        //                maxBottom = parseAttr(el, 'y') + parseAttr(el, 'height');
        //            }} else if (el.tagName.toLowerCase() === 'line') {{
        //                maxBottom = Math.max(parseAttr(el, 'y1'), parseAttr(el, 'y2'));
        //            }}
        //            return maxBottom;
        //        }}

        //        function adjustElementY(el, delta) {{
        //            ['y', 'y1', 'y2'].forEach(attr => {{
        //                if (el.hasAttribute(attr)) {{
        //                    el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
        //                }}
        //            }});
        //            if (el.tagName.toLowerCase() === 'text') {{
        //                el.querySelectorAll('tspan').forEach(tspan => {{
        //                    const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
        //                    tspan.setAttribute('y', y + delta);
        //                }});
        //            }}
        //        }}

        //        function isPageEmptyOrRectOnly(page) {{
        //            const svg = page.querySelector('svg');
        //            if (!svg) {{
        //                return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
        //            }}
        //            const elements = svg.querySelectorAll('text, line, image');
        //            return !elements.length;
        //        }}

        //        function splitIntoPages(frag) {{
        //            if (!frag.hasChildNodes()) {{
        //                console.warn('Fragment is empty, returning no pages');
        //                return [];
        //            }}

        //            const tempPage = document.createElement('div');
        //            tempPage.className = 'page';
        //            tempPage.appendChild(frag);

        //            let svgs = tempPage.querySelectorAll('svg');
        //            if (svgs.length === 0) {{
        //                const svgElements = tempPage.querySelectorAll('text, rect, line, image');
        //                if (svgElements.length > 0) {{
        //                    const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //                    while (tempPage.firstChild) {{
        //                        newSvg.appendChild(tempPage.firstChild);
        //                    }}
        //                    tempPage.appendChild(newSvg);
        //                    svgs = tempPage.querySelectorAll('svg');
        //                }}
        //            }}

        //            console.log(`splitIntoPages: Found ${{svgs.length}} SVG elements in fragment`);

        //            // Handle case with no SVGs
        //            if (svgs.length === 0) {{
        //                editor.appendChild(tempPage);
        //                let height = tempPage.offsetHeight;
        //                editor.removeChild(tempPage);
        //                if (height > maxPageHeight) {{
        //                    tempPage.style.maxHeight = maxPageHeight + 'px';
        //                    tempPage.style.overflow = 'hidden';
        //                    height = maxPageHeight;
        //                }}
        //                tempPage.style.minHeight = maxPageHeight + 'px';
        //                return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        //            }}

        //            // Handle multiple SVGs case - we need to process each SVG separately
        //            if (svgs.length > 1) {{
        //                const pages = [];

        //                // Extract each SVG into its own fragment and process it
        //                Array.from(svgs).forEach((svg, index) => {{
        //                    const svgContainer = document.createElement('div');
        //                    svgContainer.className = 'page';
        //                    svgContainer.appendChild(svg.cloneNode(true));

        //                    // Process this individual SVG
        //                    const svgPages = processSingleSvg(svgContainer.querySelector('svg'), svgContainer);
        //                    pages.push(...svgPages);
        //                }});

        //                return pages;
        //            }}

        //            // Handle single SVG case
        //            const svg = svgs[0];
        //            return processSingleSvg(svg, tempPage);
        //        }}

        //        function processSingleSvg(svg, container) {{
        //            const {{height, minY}} = estimateSvgHeight(svg, true);
        //            adjustSvg(svg, minY);

        //            if (height <= maxPageHeight) {{
        //                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
        //                svg.setAttribute('viewBox', `0 0 ${{width}} ${{height}}`);
        //                svg.style.height = height + 'px';
        //                svg.style.width = '100%';
        //                svg.style.display = 'block';
        //                svg.style.margin = '0';
        //                svg.style.padding = '0';
        //                container.style.minHeight = maxPageHeight + 'px';
        //                return isPageEmptyOrRectOnly(container) ? [] : [container];
        //            }}

        //            // Split into pages
        //            const pages = [];
        //            const elements = Array.from(svg.children).sort((a, b) => getMinY(a) - getMinY(b));

        //            let currentGroup = [];
        //            let currentMinY = Infinity;
        //            let currentMaxBottom = -Infinity;

        //            elements.forEach(el => {{
        //                const elMinY = getMinY(el);
        //                const elMaxBottom = getMaxBottom(el);

        //                const potentialMinY = currentGroup.length === 0 ? elMinY : Math.min(currentMinY, elMinY);
        //                const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
        //                const potentialHeight = potentialMaxBottom - potentialMinY + 20;

        //                if (currentGroup.length > 0 && potentialHeight > maxPageHeight) {{
        //                    const groupHeight = currentMaxBottom - currentMinY + 20;
        //                    const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
        //                    if (!isPageEmptyOrRectOnly(newPage)) {{
        //                        pages.push(newPage);
        //                    }}
        //                    currentGroup = [el];
        //                    currentMinY = elMinY;
        //                    currentMaxBottom = elMaxBottom;
        //                }} else {{
        //                    currentGroup.push(el);
        //                    currentMinY = potentialMinY;
        //                    currentMaxBottom = potentialMaxBottom;
        //                }}
        //            }});

        //            if (currentGroup.length > 0) {{
        //                const groupHeight = currentMaxBottom - currentMinY + 20;
        //                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
        //                if (!isPageEmptyOrRectOnly(newPage)) {{
        //                    pages.push(newPage);
        //                }}
        //            }}

        //            return pages;
        //        }}

        //        function createPageFromGroup(group, groupMinY, groupHeight) {{
        //            const newPage = document.createElement('div');
        //            newPage.className = 'page';
        //            newPage.style.minHeight = maxPageHeight + 'px';
        //            const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //            const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

        //            group.forEach(el => {{
        //                const cloned = el.cloneNode(true);
        //                adjustElementY(cloned, -groupMinY);
        //                newSvg.appendChild(cloned);
        //            }});

        //            newSvg.setAttribute('viewBox', `0 0 ${{width}} ${{groupHeight}}`);
        //            newSvg.style.height = groupHeight + 'px';
        //            newSvg.style.width = '100%';
        //            newSvg.style.display = 'block';
        //            newSvg.style.margin = '0';
        //            newSvg.style.padding = '0';
        //            newPage.appendChild(newSvg);
        //            return newPage;
        //        }}

        //        function createBreakDiv(isUser) {{
        //            const breakDiv = document.createElement('div');
        //            breakDiv.className = isUser ? 'page-break' : 'page-break auto-page-break';
        //            breakDiv.style.position = 'relative';
        //            breakDiv.style.height = '20px';
        //            breakDiv.style.margin = '10px 0';
        //            breakDiv.style.backgroundColor = isUser ? '#e0e0e0' : 'gray';
        //            breakDiv.style.borderTop = '1px dashed #ccc';
        //            breakDiv.style.borderBottom = '1px dashed #ccc';
        //            breakDiv.style.pageBreakBefore = 'always';

        //            const removeBtn = document.createElement('div');
        //            removeBtn.className = 'page-break-remove';
        //            removeBtn.innerHTML = '✂️';
        //            removeBtn.style.position = 'absolute';
        //            removeBtn.style.left = '50%';
        //            removeBtn.style.top = '50%';
        //            removeBtn.style.transform = 'translate(-50%, -50%)';
        //            removeBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.9)';
        //            removeBtn.style.color = '#333';
        //            removeBtn.style.borderRadius = '4px';
        //            removeBtn.style.padding = '3px 8px';
        //            removeBtn.style.display = 'none';
        //            removeBtn.style.cursor = 'pointer';
        //            breakDiv.appendChild(removeBtn);

        //            if (isUser) {{
        //                removeBtn.addEventListener('click', function(e) {{
        //                    e.stopPropagation();
        //                    removePageBreak(breakDiv);
        //                }});
        //                breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
        //                breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
        //            }}

        //            return breakDiv;
        //        }}

        //        function insertBreaks(pages, isAuto) {{
        //            if (pages.length <= 1) return pages;
        //            const result = [pages[0]];
        //            for (let i = 1; i < pages.length; i++) {{
        //                result.push(createBreakDiv(!isAuto), pages[i]);
        //            }}
        //            return result;
        //        }}

        //        // Process each break position using the same logic as TogglePageBreak
        //        breakPositions.forEach((breakPos, index) => {{
        //            if (breakPos.yOffset < 10) {{
        //                console.log(`Skipping invalid y-offset: ${{breakPos.yOffset}}`);
        //                return;
        //            }}

        //            console.log(`Processing break ${{index + 1}} at global yOffset: ${{breakPos.yOffset}}, xOffset: ${{breakPos.xOffset}}`);

        //            const {{ range, page: targetPage, pageIndex }} = createRangeAtGlobalYOffset(breakPos.yOffset, breakPos.xOffset);
        //            if (!range || !targetPage) {{
        //                console.log(`Could not create range for global yOffset: ${{breakPos.yOffset}}, xOffset: ${{breakPos.xOffset}}`);
        //                return;
        //            }}

        //            console.log(`Using page ${{pageIndex}} for break at yOffset: ${{breakPos.yOffset}}, xOffset: ${{breakPos.xOffset}}`);

        //            const svg = targetPage.querySelector('svg');

        //            const beforeRange = document.createRange();
        //            beforeRange.setStart(svg, 0);
        //            beforeRange.setEnd(range.startContainer, range.startOffset);
        //            let beforeFrag = beforeRange.extractContents();

        //            const afterRange = document.createRange();
        //            afterRange.setStart(range.endContainer, range.endOffset);
        //            afterRange.setEnd(svg, svg.childNodes.length);
        //            let afterFrag = afterRange.extractContents();

        //            // Wrap beforeFrag in new SVG if has content
        //            let beforeSvgFrag = document.createDocumentFragment();
        //            if (beforeFrag.hasChildNodes()) {{
        //                const newBeforeSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //                while (beforeFrag.firstChild) {{
        //                    newBeforeSvg.appendChild(beforeFrag.firstChild);
        //                }}
        //                beforeSvgFrag.appendChild(newBeforeSvg);
        //            }}

        //            // Combine afterFrag with ALL subsequent content for proper reflow
        //            const combinedFrag = document.createDocumentFragment();
        //            if (afterFrag.hasChildNodes()) {{
        //                const newAfterSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        //                while (afterFrag.firstChild) {{
        //                    newAfterSvg.appendChild(afterFrag.firstChild);
        //                }}
        //                combinedFrag.appendChild(newAfterSvg);
        //            }}

        //            // Collect ALL subsequent content until end of document (same as TogglePageBreak)
        //            const subsequentContent = document.createDocumentFragment();
        //            let current = targetPage.nextElementSibling;
        //            const elementsToRemove = [];

        //            while (current) {{
        //                if (current.classList.contains('page')) {{
        //                    Array.from(current.childNodes).forEach(child => {{
        //                        if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.TEXT_NODE) {{
        //                            subsequentContent.appendChild(child.cloneNode(true));
        //                        }}
        //                    }});
        //                    elementsToRemove.push(current);
        //                }} else if (current.classList.contains('page-break')) {{
        //                    elementsToRemove.push(current);
        //                }}
        //                current = current.nextElementSibling;
        //            }}

        //            while (subsequentContent.firstChild) {{
        //                combinedFrag.appendChild(subsequentContent.firstChild);
        //            }}

        //            // Force deep clone of the combined fragment to ensure we don't lose content
        //            const combinedFragClone = document.createDocumentFragment();
        //            const tempDiv = document.createElement('div');
        //            tempDiv.appendChild(combinedFrag);
        //            combinedFragClone.appendChild(tempDiv.cloneNode(true));

        //            // Extract the content back from the clone
        //            const finalCombinedFrag = document.createDocumentFragment();
        //            const tempDivContent = combinedFragClone.firstChild;
        //            while (tempDivContent && tempDivContent.firstChild) {{
        //                finalCombinedFrag.appendChild(tempDivContent.firstChild);
        //            }}

        //            // Split content into pages with proper auto page break calculation
        //            const beforePages = splitIntoPages(beforeSvgFrag);

        //            const afterPages = splitIntoPages(finalCombinedFrag);

        //            // Ensure auto-splitting worked correctly for all after pages (same as TogglePageBreak)
        //            let pagesNeedingReSplit = [];

        //            // First identify all pages that need re-splitting
        //            afterPages.forEach((page, index) => {{
        //                const svg = page.querySelector('svg');
        //                if (svg) {{
        //                    const {{ height }} = estimateSvgHeight(svg, true);
        //                    if (height > maxPageHeight) {{
        //                        pagesNeedingReSplit.push({{ index, page }});
        //                    }}
        //                }}
        //            }});

        //            // Force re-split if we have any oversized pages or if we only have one page with significant content
        //            if (pagesNeedingReSplit.length > 0 || (afterPages.length === 1 && afterPages[0].querySelector('svg')?.querySelectorAll('text, line, image').length > 50)) {{
        //                console.warn(`Forcing complete re-split of all content due to oversized pages or complex single page`);

        //                // Re-extract ALL content into a single fragment
        //                const allContentFrag = document.createDocumentFragment();
        //                const tempAllContent = document.createElement('div');

        //                // Collect all content from all pages
        //                afterPages.forEach(page => {{
        //                    const svg = page.querySelector('svg');
        //                    if (svg) {{
        //                        while (svg.firstChild) {{
        //                            tempAllContent.appendChild(svg.firstChild);
        //                        }}
        //                    }}
        //                }});

        //                // Force a complete re-split with all content
        //                while (tempAllContent.firstChild) {{
        //                    allContentFrag.appendChild(tempAllContent.firstChild);
        //                }}

        //                // Replace all pages with newly split pages
        //                const newAfterPages = splitIntoPages(allContentFrag);

        //                // Replace the entire afterPages array
        //                afterPages.length = 0;
        //                newAfterPages.forEach(p => afterPages.push(p));
        //            }} else if (pagesNeedingReSplit.length > 0) {{
        //                // If we still need to re-split individual pages, do it in reverse order
        //                pagesNeedingReSplit.sort((a, b) => b.index - a.index).forEach(({{ index, page }}) => {{
        //                    // Re-extract content and force split
        //                    const contentFrag = document.createDocumentFragment();
        //                    while (page.firstChild) {{
        //                        contentFrag.appendChild(page.firstChild);
        //                    }}
        //                    const reSplitPages = splitIntoPages(contentFrag);
        //                    afterPages.splice(index, 1, ...reSplitPages);
        //                }});
        //            }}


        //            // Insert auto breaks between pages
        //            const beforeWithBreaks = insertBreaks(beforePages, true);
        //            const afterWithBreaks = insertBreaks(afterPages, true);

        //            // Create the manual page break
        //            const userBreak = createBreakDiv(true);

        //            let allElements = [];
        //            if (beforePages.length > 0) {{
        //                allElements = [...beforeWithBreaks];
        //                allElements.push(userBreak);
        //            }} else {{
        //                allElements.push(userBreak);
        //            }}

        //            if (afterPages.length > 0) {{
        //                allElements.push(...afterWithBreaks);
        //            }}

        //            // Remove ALL old elements
        //            elementsToRemove.forEach(el => {{
        //                if (el && el.parentNode) {{
        //                    el.remove();
        //                }}
        //            }});

        //            // Replace the target page with the new structure
        //            if (targetPage.parentNode) {{
        //                targetPage.replaceWith(...allElements);
        //                console.log('Replaced target page with', allElements.length, 'elements (pages + breaks)');
        //            }}
        //        }});

        //        adjustAllSvgs();

        //        // Cleanup empty #content divs
        //        const contentDivs = document.querySelectorAll('div#content');
        //        contentDivs.forEach(div => {{
        //            const hasMeaningfulContent = Array.from(div.querySelectorAll('*')).some(el => {{
        //                if (el.tagName === 'SVG') {{
        //                    return el.textContent.trim() !== '' || el.querySelector('rect, text, line');
        //                }}
        //                if (el.tagName === 'TEXT') {{
        //                    return el.textContent.trim() !== '';
        //                }}
        //                return el.textContent.trim() !== '';
        //            }});
        //            if (!hasMeaningfulContent) {{
        //                div.remove();
        //            }}
        //        }});

        //        // Function to remove page break
        //        function removePageBreak(pageBreakElement) {{
        //            const prevPage = pageBreakElement.previousElementSibling;
        //            const nextPage = pageBreakElement.nextElementSibling;

        //            if (!prevPage || !nextPage || 
        //                !prevPage.classList.contains('page') || 
        //                !nextPage.classList.contains('page')) {{
        //                console.error('Cannot find adjacent pages to merge');
        //                return false;
        //            }}

        //            while (nextPage.firstChild) {{
        //                prevPage.appendChild(nextPage.firstChild);
        //            }}

        //            pageBreakElement.remove();
        //            nextPage.remove();

        //            prevPage.querySelectorAll('svg').forEach(svg => {{
        //                svg.removeAttribute('height');
        //                svg.style.removeProperty('margin-top');
        //                svg.style.removeProperty('margin-bottom');
        //                svg.style.removeProperty('padding-top');
        //                svg.style.removeProperty('padding-bottom');

        //                let parent = svg.parentElement;
        //                while (parent && parent !== prevPage) {{
        //                    parent.style.removeProperty('margin-top');
        //                    parent.style.removeProperty('margin-bottom');
        //                    parent.style.removeProperty('padding-top');
        //                    parent.style.removeProperty('padding-bottom');
        //                    parent = parent.parentElement;
        //                }}
        //            }});

        //            const svgList = prevPage.querySelectorAll('svg');
        //            svgList.forEach(svg => {{
        //                const {{ height, minY }} = estimateSvgHeight(svg, true);
        //                adjustSvg(svg, minY);
        //                svg.style.height = height + 'px';
        //                svg.style.width = '100%';
        //                svg.style.display = 'block';
        //                svg.style.margin = '0';
        //                svg.style.padding = '0';
        //                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
        //                svg.setAttribute('viewBox', `0 0 ${{width}} ${{height}}`);
        //            }});

        //            prevPage.style.display = 'none';
        //            void prevPage.offsetHeight;
        //            prevPage.style.display = '';

        //            savePageBreakPositions();
        //            return true;
        //        }}

        //    }} catch (error) {{
        //        console.error('Error in RestorePageBreaks:', error);
        //        if (typeof window.notifyError === 'function') {{
        //            window.notifyError(error.message);
        //        }}
        //    }}
        //}})();
        //";

        //        await WaitForBrowserReady();
        //        var result = await browser.EvaluateScriptAsync(script);

        //        if (result.Success)
        //        {
        //            System.Diagnostics.Debug.WriteLine("RestorePageBreaks script executed successfully");
        //        }
        //        else
        //        {
        //            System.Diagnostics.Debug.WriteLine($"RestorePageBreaks script failed: {result.Message}");
        //            await Task.Delay(1000);
        //            System.Diagnostics.Debug.WriteLine("Retrying RestorePageBreaks script execution...");
        //            result = await browser.EvaluateScriptAsync(script);
        //            System.Diagnostics.Debug.WriteLine(result.Success ? "Retry succeeded" : $"Retry failed: {result.Message}");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"Error restoring page breaks: {ex.Message}");
        //    }
        //}

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

        // Setup the notification function in JavaScript to handle page break position updates
        private async Task SetupPageBreakNotificationFunction()
        {
            string script = @"
                (function() {
                    console.log('Setting up page break notification function');
                    
                    // Define the notification function that will be called when page breaks change
                    window.notifyPageBreakPositionsReady = function() {
                        console.log('Page break positions are ready to be retrieved');
                        // This function is called by savePageBreakPositions when positions are updated
                        // The actual positions are stored in window.pageBreakPositionsData
                    };
                    
                    console.log('Notification function setup complete');
                })();
                ";

            await browser.EvaluateScriptAsync(script);

            // Set up a timer to periodically check for page break position updates
            System.Windows.Forms.Timer positionCheckTimer = new System.Windows.Forms.Timer();
            positionCheckTimer.Interval = 1000; // Check every second
            positionCheckTimer.Tick += async (sender, e) => {
                await CheckForPageBreakPositionUpdates();
            };
            positionCheckTimer.Start();
        }

        // Check if page break positions have been updated in JavaScript
        private async Task CheckForPageBreakPositionUpdates()
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
                    // Clear the data in JavaScript to avoid processing it multiple times
                    await browser.EvaluateScriptAsync("window.pageBreakPositionsData = null;");
                    // Process the positions
                    SavePageBreakPositions(positionsJson);
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

        // Toggle between single page view and multiple page view
        public async Task ToggleMultiplePageView()
        {
            try
            {
                // Use current state before toggling
                bool showMultipleView = !_isMultiplePageView;
                _isMultiplePageView = showMultipleView;
                pageViewToggleButton.Text = _isMultiplePageView ? "Single Page View" : "Multiple Page View";
                
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
    
    // JavaScript bridge class to handle callbacks from JavaScript
    public class JavaScriptBridge
    {
        private readonly Form1 _form;
        
        public JavaScriptBridge(Form1 form)
        {
            _form = form;
            System.Diagnostics.Debug.WriteLine("JavaScriptBridge created and initialized");
        }
        public void SavePageBreakPositions(string positionsJson)
        {
            _form.SavePageBreakPositions(positionsJson);
        }
    }
}


