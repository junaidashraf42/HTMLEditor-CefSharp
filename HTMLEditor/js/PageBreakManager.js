// Page Break Management functionality
class PageBreakManager {
    constructor() {
        this.maxPageHeight = EditorConfig.MAX_PAGE_HEIGHT;
    }

    createBreakDiv(isUser = true) {
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
            removeBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.removePageBreak(breakDiv);
            });
            breakDiv.addEventListener('mouseenter', () => removeBtn.style.display = 'flex');
            breakDiv.addEventListener('mouseleave', () => removeBtn.style.display = 'none');
        }

        return breakDiv;
    }

    insertBreaks(pages, isAuto = false) {
        if (pages.length <= 1) return pages;
        const result = [pages[0]];
        for (let i = 1; i < pages.length; i++) {
            result.push(this.createBreakDiv(!isAuto), pages[i]);
        }
        return result;
    }

    removePageBreak(pageBreakElement) {
        if (pageBreakElement.classList.contains('auto-page-break')) {
            return false;
        }
        
        const prevPage = pageBreakElement.previousElementSibling;
        const nextPage = pageBreakElement.nextElementSibling;

        if (!prevPage || !nextPage || !prevPage.classList.contains('page') || !nextPage.classList.contains('page')) {
            return false;
        }

        const prevSvg = prevPage.querySelector('svg');
        const nextSvg = nextPage.querySelector('svg');

        if (prevSvg && nextSvg) {
            const { height: prevHeight } = EditorUtils.estimateSvgHeight(prevSvg);
            const nextElements = Array.from(nextSvg.children);
            nextElements.forEach(el => {
                EditorUtils.adjustElementY(el, prevHeight);
                prevSvg.appendChild(el);
            });
            const newHeight = prevHeight + EditorUtils.estimateSvgHeight(nextSvg).height;
            const width = prevSvg.viewBox?.baseVal?.width || EditorConfig.DEFAULT_WIDTH;
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
        while (prevPage.firstChild) {
            frag.appendChild(prevPage.firstChild);
        }
        
        const pageSplitter = new PageSplitter();
        const newPages = pageSplitter.splitIntoPages(frag);
        const pagesWithBreaks = this.insertBreaks(newPages, true);
        
        const parent = prevPage.parentNode;
        const nextSibling = prevPage.nextSibling;
        parent.removeChild(prevPage);
        pagesWithBreaks.forEach(element => {
            parent.insertBefore(element, nextSibling);
        });

        this.adjustAllSvgs();
        this.cleanupEmptyOrRectOnlyPages();
        this.savePageBreakPositions().catch(error => {
            console.error('Error saving page break positions:', error);
        });
        return true;
    }

    adjustAllSvgs() {
        const allSvgs = document.querySelectorAll('svg');
        allSvgs.forEach(svg => {
            svg.removeAttribute('height');
            svg.removeAttribute('style');
            const { height, minY } = EditorUtils.estimateSvgHeight(svg);
            EditorUtils.adjustSvg(svg, minY);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            const width = svg.viewBox?.baseVal?.width || EditorConfig.DEFAULT_WIDTH;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        });
    }

    cleanupEmptyOrRectOnlyPages() {
        const editor = document.getElementById('editor');
        const pages = Array.from(editor.querySelectorAll('.page'));
        pages.forEach(page => {
            if (EditorUtils.isPageEmptyOrRectOnly(page)) {
                const prevSibling = page.previousElementSibling;
                if (prevSibling && prevSibling.classList.contains('page-break')) {
                    prevSibling.remove();
                }
                page.remove();
            }
        });
    }

    async savePageBreakPositions() {
        const editor = document.getElementById('editor');
        const pageBreaks = Array.from(editor.querySelectorAll('.page-break:not(.auto-page-break)'));
        const breakData = [];
        
        pageBreaks.forEach((pageBreak, index) => {
            const allTextElements = Array.from(editor.querySelectorAll('text'));
            const breakPosition = Array.from(editor.children).indexOf(pageBreak);
            
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
            
            const lastTwoTexts = precedingTexts.slice(-2);
            
            if (lastTwoTexts.length >= 2) {
                const text1Content = lastTwoTexts[0].textContent.trim();
                const text2Content = lastTwoTexts[1].textContent.trim();
                
                const text2Element = lastTwoTexts[1];
                let yOffset = 0;
                let xOffset = 0;
                
                try {
                    yOffset = EditorUtils.getGlobalYPosition(text2Element);
                    
                    if (text2Element.hasAttribute('x')) {
                        xOffset = parseFloat(text2Element.getAttribute('x')) || 0;
                    } else {
                        const bbox = text2Element.getBoundingClientRect();
                        const editorBbox = editor.getBoundingClientRect();
                        xOffset = bbox.left - editorBbox.left;
                    }
                } catch (e) {
                    console.warn('Error calculating offset for text element:', e);
                }
                
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
                    text1Hash: EditorUtils.computeSimpleTextHash(text1Content),
                    text1Content: text1Content.substring(0, 50),
                    text2Hash: EditorUtils.computeSimpleTextHash(text2Content), 
                    text2Content: text2Content.substring(0, 50),
                    breakId: `break_${Date.now()}_${index}`,
                    yOffset: yOffset,
                    xOffset: xOffset,
                    pageIndex: pageIndex
                };
                
                breakData.push(breakInfo);
            }
        });
        
        // Use event-driven communication bridge
        try {
            if (window.communicationBridge) {
                await window.communicationBridge.savePageBreakPositions(JSON.stringify(breakData));
                console.log('Page break positions saved via communication bridge');
            } else {
                console.error('Communication bridge not available');
            }
        } catch (error) {
            console.error('Failed to save page break positions:', error);
        }
    }

    async togglePageBreak() {
        const editor = document.getElementById('editor');
        if (!editor || !window.getSelection) {
            return;
        }

        const sel = window.getSelection();
        if (!sel.rangeCount) {
            return;
        }
        const range = sel.getRangeAt(0);

        const allPages = Array.from(editor.querySelectorAll('.page'));
        if (!allPages.length) {
            return;
        }

        let pageIndex = allPages.findIndex(p => p.contains(range.startContainer));
        if (pageIndex < 0) pageIndex = 0;
        const targetPage = allPages[pageIndex];

        this.normalizeRangeToElementBoundary(range);

        const beforeRange = document.createRange();
        beforeRange.setStart(targetPage, 0);
        beforeRange.setEnd(range.startContainer, range.startOffset);
        const beforeFrag = beforeRange.extractContents();

        const afterRange = document.createRange();
        afterRange.setStart(range.endContainer, range.endOffset);
        afterRange.setEnd(targetPage, targetPage.childNodes.length);
        const afterFrag = afterRange.extractContents();

        // Process subsequent content
        const subsequentContent = document.createDocumentFragment();
        const elementsToRemove = [];
        
        let current = targetPage.nextElementSibling;
        
        while (current) {
            if (current.classList.contains('page')) {
                Array.from(current.childNodes).forEach(child => {
                    if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.TEXT_NODE) {
                        subsequentContent.appendChild(child.cloneNode(true));
                    }
                });
                elementsToRemove.push(current);
            } else if (current.classList.contains('page-break')) {
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

        const pageSplitter = new PageSplitter();
        const beforePages = pageSplitter.splitIntoPages(beforeFrag);
        const afterPages = pageSplitter.splitIntoPages(combinedFrag);

        const beforeWithBreaks = this.insertBreaks(beforePages, true);
        const afterWithBreaks = this.insertBreaks(afterPages, true);
        
        const userBreak = this.createBreakDiv(true);

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
                const parent = targetPage.parentNode;
                const nextSibling = targetPage.nextSibling;
                parent.removeChild(targetPage);
                allElements.forEach(element => {
                    parent.insertBefore(element, nextSibling);
                });
            }
        } catch (e) {
            // Error handled silently for production
        }

        this.adjustAllSvgs();
        this.cleanupEmptyOrRectOnlyPages();
        this.savePageBreakPositions().catch(error => {
            console.error('Error saving page break positions:', error);
        });
    }

    normalizeRangeToElementBoundary(range) {
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


    getGlobalYPosition(textEl) {
        try {
            let globalY = 0;
            const editor = document.getElementById('editor');
            
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

    computeSimpleTextHash(text) {
        let hash = 0;
        if (!text || text.length === 0) return 'empty_text';
        for (let i = 0; i < text.length; i++) {
            const chr = text.charCodeAt(i);
            hash = ((hash << 5) - hash) + chr;
            hash |= 0; // Convert to 32-bit integer
        }
        return 'txt_' + Math.abs(hash).toString(36);
    }
}
