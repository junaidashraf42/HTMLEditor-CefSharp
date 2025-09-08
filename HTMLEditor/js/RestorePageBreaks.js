function getRestorePageBreaksScript(breakDataJson) {
    return `
(async function() {
    try {
        console.log('RestorePageBreaks script started');

        // Get the editor element
        const editor = document.getElementById('editor');
        if (!editor) {
            console.error('Editor element not found');
            return;
        }

        // Flatten nested .page containers
        document.querySelectorAll('.page .page').forEach(nested => {
            const outer = nested.closest('.page');
            if (outer) {
                while (nested.firstChild) {
                    outer.parentNode.insertBefore(nested.firstChild, outer);
                }
                nested.remove();
            }
        });

        // Helper: Simple string hash function
        function computeHash(str) {
            //console.log('Computing hash for:', str);
            let hash = 0;
            if (str.length === 0) return 'hash_0';
            for (let i = 0; i < str.length; i++) {
                const chr = str.charCodeAt(i);
                hash = ((hash << 5) - hash) + chr;
                hash |= 0; // Convert to 32-bit integer
            }
            return 'hash_' + Math.abs(hash).toString(36);
        }

        // Helper: Get element signature
        async function getElementSignature(el) {
            const type = el.tagName.toLowerCase();
            let content = '';
            let attributes = {};
            if (type === 'text') {
                content = el.textContent.trim();
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {
                    content = Array.from(tspans).map(t => t.textContent.trim()).join('|');
                }
                attributes = {
                    x: el.getAttribute('x'),
                    y: el.getAttribute('y')
                };
            } else if (['rect', 'image'].includes(type)) {
                attributes = {
                    x: el.getAttribute('x'),
                    y: el.getAttribute('y'),
                    width: el.getAttribute('width'),
                    height: el.getAttribute('height')
                };
            } else if (type === 'line') {
                attributes = {
                    x1: el.getAttribute('x1'),
                    y1: el.getAttribute('y1'),
                    x2: el.getAttribute('x2'),
                    y2: el.getAttribute('y2')
                };
            }
            const attrStr = JSON.stringify(attributes, Object.keys(attributes).sort());
            const hash = computeHash(content + attrStr);
            //console.log('Element signature:', { hash, type, content: content.substring(0, 50), attributes });
            return { hash, type, content: content.substring(0, 50), attributes };
        }

        // Parse break data
        const breakDataJson = '${breakDataJson}';
        let breakData;
        try {
            console.log('Attempting to parse breakDataJson:', breakDataJson);
            breakData = JSON.parse(breakDataJson);
            console.log('Parsed simplified breakData:', breakData);
        } catch (e) {
            console.error('Failed to parse breakDataJson:', e, 'Raw JSON:', breakDataJson);
            return;
        }

        if (!Array.isArray(breakData)) {
            console.error('breakData is not an array:', breakData);
            return;
        }

        console.log('breakData length:', breakData.length);

        // Get all text elements for simplified matching
        const allTextElements = Array.from(editor.querySelectorAll('text'));

        if (allTextElements.length === 0) {
            console.warn('No text elements found in editor to match against break data.');
            return;
        }

        // Process each break with simplified matching
        async function restoreBreakSequentially(breakIndex) {
            if (breakIndex >= breakData.length) {
                console.log('All breaks restored successfully');
                return;
            }

            const breakPoint = breakData[breakIndex];
            console.log('Processing break ' + (breakIndex + 1) + '/' + breakData.length + ': ' + breakPoint.breakId);
            

            // Skip invalid breaks
            if (!breakPoint || !breakPoint.text1Hash || !breakPoint.text2Hash) {
                console.warn('Invalid break at index ' + breakIndex + ':', breakPoint);
                return await restoreBreakSequentially(breakIndex + 1);
            }

            // Find the two text elements by their hashes
            let text1Element = null;
            let text2Element = null;
            
            // Re-scan text elements for fresh DOM state
            const currentTextElements = Array.from(editor.querySelectorAll('text'));
            let text1Candidates = [];
            let text2Candidates = [];
            
            currentTextElements.forEach((textEl, index) => {
                const textContent = textEl.textContent.trim();
                const textHash = computeSimpleTextHash(textContent);
                
                // Debug logging for hash matching
                if (index < 10) { // Log first 10 elements for debugging
                }
                
                if (textHash === breakPoint.text1Hash) {
                    text1Candidates.push(textEl);
                }
                if (textHash === breakPoint.text2Hash) {
                    text2Candidates.push(textEl);
                } else if (false) {
                    // If multiple elements with same hash, prefer one closest to the yOffset
                    if (!text2Element || (breakPoint.yOffset > 0 && 
                        Math.abs(getElementYPosition(textEl) - breakPoint.yOffset) < 
                        Math.abs(getElementYPosition(text2Element) - breakPoint.yOffset))) {
                        text2Element = textEl;
                    }
                }
            });
            
            // Select closest elements to yOffset
            if (text1Candidates.length === 1) {
                text1Element = text1Candidates[0];
            } else if (text1Candidates.length > 1 && breakPoint.yOffset > 0) {
                let minDistance = Infinity;
                text1Candidates.forEach(candidate => {
                    const distance = Math.abs(getElementYPosition(candidate) - breakPoint.yOffset);
                    if (distance < minDistance) {
                        minDistance = distance;
                        text1Element = candidate;
                    }
                });
            } else if (text1Candidates.length > 1) {
                text1Element = text1Candidates[0];
            }
            
            if (text2Candidates.length === 1) {
                text2Element = text2Candidates[0];
            } else if (text2Candidates.length > 1 && breakPoint.yOffset > 0) {
                let minDistance = Infinity;
                text2Candidates.forEach(candidate => {
                    const distance = Math.abs(getElementYPosition(candidate) - breakPoint.yOffset);
                    if (distance < minDistance) {
                        minDistance = distance;
                        text2Element = candidate;
                    }
                });
            } else if (text2Candidates.length > 1) {
                text2Element = text2Candidates[0];
            }

            if (!text1Element || !text2Element) {
                console.warn('Missing text elements for break ' + breakPoint.breakId + ': text1=' + !!text1Element + ', text2=' + !!text2Element);
                
                // Fallback: try to find elements by partial content match
                if (!text1Element && breakPoint.text1Content) {
                    const partialMatch1 = currentTextElements.find(el => 
                        el.textContent.trim().includes(breakPoint.text1Content.trim()) ||
                        breakPoint.text1Content.trim().includes(el.textContent.trim())
                    );
                    if (partialMatch1) {
                        text1Element = partialMatch1;
                    }
                }
                
                if (!text2Element && breakPoint.text2Content) {
                    const partialMatch2 = currentTextElements.find(el => 
                        el.textContent.trim().includes(breakPoint.text2Content.trim()) ||
                        breakPoint.text2Content.trim().includes(el.textContent.trim())
                    );
                    if (partialMatch2) {
                        text2Element = partialMatch2;
                    }
                }
                
                // If still missing elements, try position-based fallback
                if (!text1Element || !text2Element) {
                    console.log('Trying position-based fallback for missing elements...');
                    const elementsNearPosition = currentTextElements.filter(el => {
                        const elY = getElementYPosition(el);
                        return Math.abs(elY - breakPoint.yOffset) < 50; // Within 50px of saved position
                    }).sort((a, b) => {
                        const aDistance = Math.abs(getElementYPosition(a) - breakPoint.yOffset);
                        const bDistance = Math.abs(getElementYPosition(b) - breakPoint.yOffset);
                        return aDistance - bDistance;
                    });
                    
                    if (elementsNearPosition.length >= 2) {
                        if (!text1Element) {
                            text1Element = elementsNearPosition[0];
                        }
                        if (!text2Element) {
                            text2Element = elementsNearPosition[1];
                        }
                    }
                }
                
                // If still no elements found, skip this break
                if (!text1Element || !text2Element) {
                    console.warn('Could not find suitable elements for break ' + breakPoint.breakId + ' even with fallbacks, skipping');
                    return await restoreBreakSequentially(breakIndex + 1);
                }
            }

            // Find which element comes last in DOM order (insert break after the later one)
            const allTexts = Array.from(editor.querySelectorAll('text'));
            const text1Index = allTexts.indexOf(text1Element);
            const text2Index = allTexts.indexOf(text2Element);
            const anchorEl = text1Index > text2Index ? text1Element : text2Element;
            
            const targetPage = anchorEl.closest('.page');
            if (!targetPage) {
                console.warn('Anchor not in a page, skipping');
                return await restoreBreakSequentially(breakIndex + 1);
            }

            // Check if a break already exists after this anchor (avoid duplicates)
            let nextElement = anchorEl.parentElement;
            while (nextElement && nextElement !== targetPage) {
                nextElement = nextElement.nextElementSibling;
                if (nextElement && nextElement.classList && nextElement.classList.contains('page-break')) {
                    console.log('Break already exists after anchor, skipping');
                    return await restoreBreakSequentially(breakIndex + 1);
                }
            }

            // Create selection range after anchor element
            const selection = window.getSelection();
            selection.removeAllRanges();
            const range = document.createRange();
            
            try {
                // Position range right after the anchor element
                if (anchorEl.nextSibling) {
                    range.setStartBefore(anchorEl.nextSibling);
                    range.setEndBefore(anchorEl.nextSibling);
                } else {
                    range.setStartAfter(anchorEl);
                    range.setEndAfter(anchorEl);
                }
                selection.addRange(range);
                
                
                // Use the existing TogglePageBreak logic
                const toggleScript = \`
                (function() {
                    const editor = document.getElementById('editor');
                    if (!editor || !window.getSelection) {
                        console.error('Editor or selection not available for break insertion');
                        return false;
                    }

                    const sel = window.getSelection();
                    if (!sel.rangeCount) {
                        console.error('No selection range for break insertion');
                        return false;
                    }
                    
                    // Execute the same logic as TogglePageBreak
                    \` + getTogglePageBreakLogic() + \`
                    
                    return true;
                })();\`;
                
                const toggleResult = eval(toggleScript);
                if (toggleResult) {
                    console.log('Successfully inserted break for ' + breakPoint.breakId);
                } else {
                    console.warn('Failed to insert break for ' + breakPoint.breakId);
                }
                
                // Continue with next break after a short delay to let DOM settle
                setTimeout(() => restoreBreakSequentially(breakIndex + 1), 200);
                
            } catch (error) {
                console.error('Error setting range for break ' + breakPoint.breakId + ':', error);
                return await restoreBreakSequentially(breakIndex + 1);
            }
        }
        
        // Simple hash function for text content (must match the one in savePageBreakPositions)
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
        
        // Helper function to get GLOBAL Y position of a text element from document top
        function getElementYPosition(textEl) {
            return getGlobalYPosition(textEl);
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
        
        // Helper function for content similarity calculation
        function calculateContentSimilarity(str1, str2) {
            if (!str1 || !str2) return 0;
            const longer = str1.length > str2.length ? str1 : str2;
            const shorter = str1.length > str2.length ? str2 : str1;
            if (longer.length === 0) return 1.0;
            return (longer.length - editDistance(longer, shorter)) / parseFloat(longer.length);
        }
        
        function editDistance(str1, str2) {
            const matrix = [];
            for (let i = 0; i <= str2.length; i++) {
                matrix[i] = [i];
            }
            for (let j = 0; j <= str1.length; j++) {
                matrix[0][j] = j;
            }
            for (let i = 1; i <= str2.length; i++) {
                for (let j = 1; j <= str1.length; j++) {
                    if (str2.charAt(i - 1) === str1.charAt(j - 1)) {
                        matrix[i][j] = matrix[i - 1][j - 1];
                    } else {
                        matrix[i][j] = Math.min(
                            matrix[i - 1][j - 1] + 1,
                            matrix[i][j - 1] + 1,
                            matrix[i - 1][j] + 1
                        );
                    }
                }
            }
            return matrix[str2.length][str1.length];
        }
        
        function getTogglePageBreakLogic() {
            return \`
            const range = sel.getRangeAt(0);
            const allPages = Array.from(editor.querySelectorAll('.page'));
            if (!allPages.length) {
                console.error('No pages found');
                return false;
            }

            let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
            if (pageIndex < 0) pageIndex = 0;
            const targetPage = allPages[pageIndex];

            function normalizeRangeToElementBoundary(range) {
                let startContainer = range.startContainer;
                let startOffset = range.startOffset;
                if (startContainer.nodeType === Node.TEXT_NODE && startOffset > 0 && startOffset <= startContainer.textContent.length) {
                    const text = startContainer.textContent;
                    let splitPoint = startOffset;
                    for (let i = Math.max(0, startOffset - 10); i <= Math.min(text.length, startOffset + 10); i++) {
                        if (text[i] === ' ' || text[i] === '\\\\n' || text[i] === '\\\\t') {
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
            
            const beforePages = splitIntoPages(beforeFrag);
            const afterPages = splitIntoPages(afterFrag);
            
            const beforeWithBreaks = insertBreaks(beforePages, false);
            const afterWithBreaks = insertBreaks(afterPages, false);

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

            if (targetPage.parentNode && allElements.length > 0) {
                // Replace spread operator with forEach insertion
                const parent = targetPage.parentNode;
                const nextSibling = targetPage.nextSibling;
                parent.removeChild(targetPage);
                allElements.forEach(function(element) {
                    parent.insertBefore(element, nextSibling);
                });
                adjustAllSvgs();
                cleanupEmptyOrRectOnlyPages();
                return true;
            }
            return false;
            \`;
        }
        
        // Helper functions from TogglePageBreak
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
                    const lines = (el.textContent || '').split('\\\\n').length;
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

        function createPageFromGroup(group, groupMinY, groupHeight) {
            const newPage = document.createElement('div');
            newPage.className = 'page';
            newPage.style.minHeight = '1120px';
            const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || 816;

            group.forEach(el => {
                const cloned = el.cloneNode(true);
                adjustElementY(cloned, -groupMinY);
                newSvg.appendChild(cloned);
            });
            adjustSvg(newSvg, groupMinY);
            newSvg.setAttribute('viewBox', '0 0 ' + width + ' ' + groupHeight);
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
        }

        function insertBreaks(pages, isAuto) {
            if (pages.length <= 1) return pages;
            const result = [pages[0]];
            for (let i = 1; i < pages.length; i++) {
                result.push(createBreakDiv(!isAuto), pages[i]);
            }
            return result;
        }

        function splitIntoPages(frag) {
            if (!frag.hasChildNodes()) {
                console.warn('Fragment is empty, returning no pages');
                return [];
            }

            const tempPage = document.createElement('div');
            tempPage.className = 'page';
            tempPage.appendChild(frag);
            const maxPageHeight = 1120;

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
                return isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
            }

            if (svgs.length > 1) {
                return [tempPage]; // Simplified for multiple SVGs
            }

            const svg = svgs[0];
            const { height, minY } = estimateSvgHeight(svg);
            
            adjustSvg(svg, minY);

            if (height <= maxPageHeight) {
                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
                svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
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
            });

            if (currentGroup.length > 0) {
                const groupHeight = currentMaxBottom - currentMinY;
                const newPage = createPageFromGroup(currentGroup, currentMinY, groupHeight);
                if (!isPageEmptyOrRectOnly(newPage)) {
                    pages.push(newPage);
                }
            }

            return pages;
        }

        function adjustAllSvgs() {
            const allSvgs = document.querySelectorAll('svg');
            allSvgs.forEach(svg => {
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

        // Start sequential restoration  
        await restoreBreakSequentially(0);

        // Estimate SVG height
        function estimateSvgHeight(svg, withPadding = true) {
            const textElements = svg.querySelectorAll('text');
            const rectElements = svg.querySelectorAll('rect');
            const lineElements = svg.querySelectorAll('line');
            const imageElements = svg.querySelectorAll('image');
            let maxBottom = 0;
            let minY = Infinity;
            const parseAttr = (el, attr) => {
                const val = el.getAttribute(attr);
                return val ? parseFloat(val) : 0;
            };

            textElements.forEach(textEl => {
                let fontSize = window.getComputedStyle(textEl).fontSize;
                fontSize = fontSize ? parseFloat(fontSize) : 16;
                const lineHeight = fontSize * 1.2;
                let lines = 1;
                const tspans = textEl.querySelectorAll('tspan');
                if (tspans.length > 0) {
                    let maxTspanY = 0;
                    let minTspanY = Infinity;
                    tspans.forEach(tspan => {
                        const tspanY = parseAttr(tspan, 'y') || parseAttr(textEl, 'y');
                        if (tspanY > maxTspanY) maxTspanY = tspanY;
                        if (tspanY < minTspanY) minTspanY = tspanY;
                    });
                    if (maxTspanY > 0) {
                        const bottom = maxTspanY + lineHeight;
                        if (bottom > maxBottom) maxBottom = bottom;
                    }
                    if (minTspanY < minY) minY = minTspanY;
                    lines = tspans.length;
                } else {
                    lines = textEl.textContent.split('\\\\n').length;
                    const y = parseAttr(textEl, 'y');
                    if (y < minY) minY = y;
                    const bottom = y + lines * lineHeight;
                    if (bottom > maxBottom) maxBottom = bottom;
                }
            });

            rectElements.forEach(rectEl => {
                const y = parseAttr(rectEl, 'y');
                const height = parseAttr(rectEl, 'height');
                if (y < minY) minY = y;
                const bottom = y + height;
                if (bottom > maxBottom) maxBottom = bottom;
            });

            lineElements.forEach(lineEl => {
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
            });

            imageElements.forEach(imageEl => {
                const y = parseAttr(imageEl, 'y');
                const height = parseAttr(imageEl, 'height');
                if (y < minY) minY = y;
                const bottom = y + height;
                if (bottom > maxBottom) maxBottom = bottom;
            });

            if (maxBottom <= 0 || minY === Infinity) {
                const viewBox = svg.getAttribute('viewBox');
                if (viewBox) {
                    const parts = viewBox.split(/\\s+/);
                    if (parts.length >= 4) {
                        const height = parseFloat(parts[3]);
                        minY = parseFloat(parts[1]);
                        if (height > 0) maxBottom = minY + height;
                    }
                } else {
                    minY = 0;
                    maxBottom = svg.clientHeight || 1000;
                }
            }

            return { height: Math.max(maxBottom - minY + (withPadding ? 20 : 0), 50), minY: minY === Infinity ? 0 : minY };
        }

        // Adjust SVG viewBox and shift elements
        function adjustSvg(svg, minY) {
            const elements = svg.querySelectorAll('rect, line, text, image');
            const deltaY = -minY;
            elements.forEach(el => {
                ['y', 'y1', 'y2'].forEach(attr => {
                    if (el.hasAttribute(attr)) {
                        const val = parseFloat(el.getAttribute(attr));
                        el.setAttribute(attr, val + deltaY);
                    }
                });
                if (el.tagName.toLowerCase() === 'text') {
                    el.querySelectorAll('tspan').forEach(tspan => {
                        const y = parseAttr(tspan, 'y') || parseAttr(el, 'y');
                        tspan.setAttribute('y', y + deltaY);
                    });
                }
            });
        }

        // Apply adjustments to SVGs
        function adjustAllSvgs() {
            const allSvgs = document.querySelectorAll('svg');
            allSvgs.forEach(svg => {
                svg.removeAttribute('height');
                svg.removeAttribute('style');
                const { height, minY } = estimateSvgHeight(svg, true);
                adjustSvg(svg, minY);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                const width = svg.viewBox?.baseVal?.width || 816;
                svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            });
        }

        // Helper: Split text at index
        function splitTextAtIndex(textEl, splitIndex) {
            const tspans = textEl.querySelectorAll('tspan');
            let lines = [];
            let hasTspans = false;
            if (tspans.length > 0) {
                hasTspans = true;
                lines = Array.from(tspans).map(tspan => ({element: tspan, text: tspan.textContent}));
            } else {
                lines = textEl.textContent.split('\\\\n').map(text => ({text}));
            }
            if (splitIndex <= 0 || splitIndex >= lines.length) {
                console.log('No split needed within text element');
                return null;
            }
            const newText = textEl.cloneNode(false);
            if (hasTspans) {
                for (let i = splitIndex; i < lines.length; i++) {
                    newText.appendChild(lines[i].element);
                }
            } else {
                const beforeLines = lines.slice(0, splitIndex).map(l => l.text);
                const afterLines = lines.slice(splitIndex).map(l => l.text);
                textEl.textContent = beforeLines.join('\\\\n');
                newText.textContent = afterLines.join('\\\\n');
                const fontSize = parseFloat(window.getComputedStyle(textEl).fontSize) || 16;
                const lineHeight = fontSize * 1.2;
                const textY = parseFloat(textEl.getAttribute('y'));
                newText.setAttribute('y', textY + splitIndex * lineHeight);
                for (let attr of textEl.attributes) {
                    if (attr.name !== 'y') {
                        newText.setAttribute(attr.name, attr.value);
                    }
                }
            }
            textEl.parentNode.insertBefore(newText, textEl.nextSibling);
            const range = document.createRange();
            range.setStartAfter(textEl);
            range.setEndBefore(newText);
            console.log('Split text at index ' + splitIndex);
            return range;
        }

        // Helper: Create page from fragment
        function createPageFromFrag(frag) {
            if (!frag.hasChildNodes()) {
                console.log('Fragment is empty, skipping page creation');
                return null;
            }
            const newPage = document.createElement('div');
            newPage.className = 'page';
            newPage.style.minHeight = '1120px';
            const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            while (frag.firstChild) {
                newSvg.appendChild(frag.firstChild);
            }
            const { height, minY } = estimateSvgHeight(newSvg, true);
            adjustSvg(newSvg, minY);
            const width = newSvg.viewBox?.baseVal?.width || newSvg.clientWidth || 816;
            newSvg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            newSvg.style.height = height + 'px';
            newSvg.style.width = '100%';
            newSvg.style.display = 'block';
            newSvg.style.margin = '0';
            newSvg.style.padding = '0';
            newPage.appendChild(newSvg);
            if (isPageEmptyOrRectOnly(newPage)) {
                console.log('Page is empty or rect-only, discarding');
                return null;
            }
            console.log('Created new page with height:', height);
            return newPage;
        }

        // Helper: Check if page is empty or rect-only
        function isPageEmptyOrRectOnly(page) {
            const svg = page.querySelector('svg');
            if (!svg) {
                return !Array.from(page.childNodes).some(node => node.nodeType === Node.ELEMENT_NODE && node.textContent.trim());
            }
            const elements = svg.querySelectorAll('text, line, image');
            const isEmpty = !elements.length;
            return isEmpty;
        }

        // Helper: Create break div
        function createBreakDiv() {
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
            removeBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                removePageBreak(breakDiv);
            });
            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
            return breakDiv;
        }

        // Fallback yOffset function with context matching
        function createRangeAtGlobalYOffset(globalYOffset, targetXOffset, context) {
            console.log(\`Creating range at yOffset: \` + globalYOffset + \`, xOffset: \` + targetXOffset + \`, context:\`, context);
            const allPages = Array.from(editor.children).filter(p => p.classList.contains('page'));
            let cumulativeHeight = 0;
            const positionCorrectionFactor = 0;
            const correctedYOffset = globalYOffset - positionCorrectionFactor;

            for (let i = 0; i < allPages.length; i++) {
                const page = allPages[i];
                const svg = page.querySelector('svg');
                if (!svg) {
                    console.warn('No SVG found in page', i);
                    continue;
                }
                const { height: pageHeight } = estimateSvgHeight(svg, true);
                console.log('Page ' + i + ' height: ' + pageHeight + ', cumulativeHeight: ' + cumulativeHeight);

                if (correctedYOffset >= cumulativeHeight && correctedYOffset <= cumulativeHeight + pageHeight) {
                    const localYOffset = correctedYOffset - cumulativeHeight;
                    const svgElements = Array.from(svg.querySelectorAll('text, rect, line, image')).map(el => {
                        const signature = getElementSignature(el);
                        return { element: el, signature, y: getMinY(el), bottom: getMaxBottom(el), x: getX(el) };
                    }).sort((a, b) => {
                        const yDiff = a.y - b.y;
                        if (yDiff === 0) {
                            return a.x - b.x;
                        }
                        return yDiff;
                    });

                    console.log('Elements in page ' + i + ' for yOffset ' + localYOffset + ':', svgElements.map(e => ({content: e.signature.content,
                        y: e.y,
                        x: e.x,
                        bottom: e.bottom
                    })));

                    // Check for straddling element (text element that spans the yOffset)
                    let straddlingElement = null;
                    svgElements.forEach(item => {
                        if (item.y <= localYOffset && localYOffset < item.bottom) {
                            straddlingElement = item.element;
                        }
                    });

                    if (straddlingElement && straddlingElement.tagName.toLowerCase() === 'text') {
                        const splitRange = splitTextAtYOffset(straddlingElement, localYOffset, targetXOffset);
                        if (splitRange) {
                            console.log('Split text element at yOffset:', localYOffset);
                            return { range: splitRange, page: page, pageIndex: i };
                        }
                    }

                    // Find the closest element by yOffset and xOffset
                    let bestElement = null;
                    let minDistance = Infinity;
                    let contextMatches = 0;

                    svgElements.forEach(item => {
                        const yDistance = Math.abs(localYOffset - item.bottom);
                        const xDistance = Math.abs(targetXOffset - item.x);
                        const combinedDistance = yDistance + xDistance * 0.1; // Weight x less heavily
                        let contextScore = 0;

                        // Check context for additional matching
                        if (context && Array.isArray(context)) {
                            context.forEach(ctx => {
                                if (item.signature.hash === ctx.hash) {
                                    contextScore += 50;
                                }
                            });
                        }

                        if (item.bottom <= localYOffset && combinedDistance < minDistance) {
                            minDistance = combinedDistance;
                            bestElement = item.element;
                            contextMatches = contextScore;
                        }
                    });

                    // Use context elements to refine selection if no direct match
                    if (!bestElement && context && Array.isArray(context)) {
                        let bestContextElement = null;
                        let minContextDistance = Infinity;
                        svgElements.forEach(item => {
                            context.forEach(ctx => {
                                if (item.signature.hash === ctx.hash) {
                                    const yDistance = Math.abs(parseFloat(ctx.attributes.y || 0) - localYOffset);
                                    const xDistance = Math.abs(parseFloat(ctx.attributes.x || 0) - targetXOffset);
                                    const combinedDistance = yDistance + xDistance * 0.1;
                                    if (combinedDistance < minContextDistance) {
                                        minContextDistance = combinedDistance;
                                        bestContextElement = item.element;
                                    }
                                }
                            });
                        });
                        if (bestContextElement) {
                            bestElement = bestContextElement;
                            console.log('Selected element based on context match at y:', getMinY(bestContextElement));
                        }
                    }

                    const range = document.createRange();
                    if (bestElement) {
                        range.setStartAfter(bestElement);
                        range.setEndAfter(bestElement);
                        console.log('Range set after element at y:', getMinY(bestElement), 'content:', bestElement.textContent?.substring(0, 50));
                    } else {
                        // Fallback to the last element in the page if no suitable element is found
                        const lastElement = svgElements[svgElements.length - 1]?.element;
                        if (lastElement) {
                            range.setStartAfter(lastElement);
                            range.setEndAfter(lastElement);
                            console.log('Range set after last element in page at y:', getMinY(lastElement));
                        } else {
                            range.setStart(svg, 0);
                            range.setEnd(svg, 0);
                            console.log('No suitable element found, range set at start of SVG');
                        }
                    }
                    return { range: range, page: page, pageIndex: i };
                }
                cumulativeHeight += pageHeight;
            }

            // If yOffset is beyond all pages, use the last page
            const lastPage = allPages[allPages.length - 1];
            if (lastPage) {
                const range = document.createRange();
                const svg = lastPage.querySelector('svg');
                const childCount = svg ? svg.childNodes.length : lastPage.childNodes.length;
                range.setStart(svg || lastPage, childCount);
                range.setEnd(svg || lastPage, childCount);
                console.log('Range set at end of last page');
                return { range: range, page: lastPage, pageIndex: allPages.length - 1 };
            }
            console.warn('No suitable page found for yOffset:', globalYOffset);
            return null;
        }

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
                    const lines = (el.textContent || '').split('\\\\n').length;
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

        function splitTextAtYOffset(textEl, splitYOffset, targetXOffset) {
            const fontSize = parseFloat(window.getComputedStyle(textEl).fontSize) || 16;
            const lineHeight = fontSize * 1.2;
            const textY = parseAttr(textEl, 'y');
            const lines = [];
            let hasTspans = false;
            const tspans = textEl.querySelectorAll('tspan');
            if (tspans.length > 0) {
                hasTspans = true;
                tspans.forEach(tspan => {
                    const y = parseAttr(tspan, 'y') || textY;
                    const x = parseAttr(tspan, 'x') || parseAttr(textEl, 'x');
                    lines.push({ y, x, bottom: y + lineHeight, element: tspan });
                });
            } else {
                const textLines = (textEl.textContent || '').split('\\\\n').map((lineText, idx) => {
                    const y = textY + idx * lineHeight;
                    const x = parseAttr(textEl, 'x');
                    return { y, x, bottom: y + lineHeight, text: lineText };
                });
                lines = lines.concat(textLines);
            }
            lines.sort((a, b) => {
                const yDiff = a.y - b.y;
                if (yDiff === 0) {
                    return a.x - b.x;
                }
                return yDiff;
            });
            let splitIndex = 0;
            let minXDistance = Infinity;
            for (let i = 0; i < lines.length; i++) {
                if (lines[i].bottom > splitYOffset) {
                    const xDistance = Math.abs(lines[i].x - targetXOffset);
                    if (xDistance < minXDistance) {
                        minXDistance = xDistance;
                        splitIndex = i;
                    }
                }
            }
            if (splitIndex === 0 || splitIndex === lines.length) {
                console.log('No split needed within text element');
                return null;
            }
            const newText = textEl.cloneNode(false);
            if (hasTspans) {
                for (let i = splitIndex; i < lines.length; i++) {
                    newText.appendChild(lines[i].element);
                }
            } else {
                const beforeLines = lines.slice(0, splitIndex).map(l => l.text);
                const afterLines = lines.slice(splitIndex).map(l => l.text);
                textEl.textContent = beforeLines.join('\\\\n');
                newText.textContent = afterLines.join('\\\\n');
                const newY = lines[splitIndex].y;
                newText.setAttribute('y', newY);
                for (let attr of textEl.attributes) {
                    if (attr.name !== 'y') {
                        newText.setAttribute(attr.name, attr.value);
                    }
                }
            }
            textEl.parentNode.insertBefore(newText, textEl.nextSibling);
            const range = document.createRange();
            range.setStartAfter(textEl);
            range.setEndBefore(newText);
            console.log('Split text at yOffset ' + splitYOffset + ', xOffset ' + targetXOffset);
            return range;
        }

        function getX(el) {
            let x = parseAttr(el, 'x') || parseAttr(el, 'x1') || 0;
            if (el.tagName.toLowerCase() === 'text') {
                const tspans = el.querySelectorAll('tspan');
                if (tspans.length > 0) {
                    x = parseAttr(tspans[0], 'x') || parseAttr(el, 'x') || 0;
                }
            }
            return x;
        }

        function savePageBreakPositions() {
        debugger;
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
                    breakId: 'break_' + Date.now() + '_' + index,
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

        // Remove page break
        function removePageBreak(pageBreakElement) {
            const prevPage = pageBreakElement.previousElementSibling;
            const nextPage = pageBreakElement.nextElementSibling;
            if (!prevPage || !nextPage || !prevPage.classList.contains('page') || !nextPage.classList.contains('page')) {
                console.error('Cannot find adjacent pages to merge');
                return false;
            }
            while (nextPage.firstChild) {
                prevPage.appendChild(nextPage.firstChild);
            }
            pageBreakElement.remove();
            nextPage.remove();
            prevPage.querySelectorAll('svg').forEach(svg => {
                svg.removeAttribute('height');
                svg.style.removeProperty('margin-top');
                svg.style.removeProperty('margin-bottom');
                svg.style.removeProperty('padding-top');
                svg.style.removeProperty('padding-bottom');
                let parent = svg.parentElement;
                while (parent && parent !== prevPage) {
                    parent.style.removeProperty('margin-top');
                    parent.style.removeProperty('margin-bottom');
                    parent.style.removeProperty('padding-top');
                    parent.style.removeProperty('padding-bottom');
                    parent = parent.parentElement;
                }
            });
            const svgList = prevPage.querySelectorAll('svg');
            svgList.forEach(svg => {
                const { height, minY } = estimateSvgHeight(svg, true);
                adjustSvg(svg, minY);
                svg.style.height = height + 'px';
                svg.style.width = '100%';
                svg.style.display = 'block';
                svg.style.margin = '0';
                svg.style.padding = '0';
                const width = svg.viewBox?.baseVal?.width || svg.clientWidth || 816;
                svg.setAttribute('viewBox', \`0 0 \${width} \${height}\`);
            });
            prevPage.style.display = 'none';
            void prevPage.offsetHeight;
            prevPage.style.display = '';
            savePageBreakPositions();
            return true;
        }

        adjustAllSvgs();

        // Cleanup empty #content divs
        const contentDivs = document.querySelectorAll('div#content');
        contentDivs.forEach(div => {
            const hasMeaningfulContent = Array.from(div.querySelectorAll('*')).some(el => {
                if (el.tagName === 'SVG') {
                    return el.textContent.trim() !== '' || el.querySelector('rect, text, line');
                }
                if (el.tagName === 'TEXT') {
                    return el.textContent.trim() !== '';
                }
                return el.textContent.trim() !== '';
            });
            if (!hasMeaningfulContent) {
                console.log('Removing empty content div');
                div.remove();
            }
        });
    } catch (error) {
        console.error('Error in RestorePageBreaks:', error);
        if (typeof window.notifyError === 'function') {
            window.notifyError(error.message);
        }
    }
})();
`;
}