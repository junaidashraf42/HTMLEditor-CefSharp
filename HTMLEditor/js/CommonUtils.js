// Common utility functions used across the HTML Editor
class EditorUtils {
    static parseAttr(el, attr) {
        const val = el.getAttribute(attr);
        return val ? parseFloat(val) : 0;
    }

    static getMinY(el) {
        let minY = Infinity;
        if (el.tagName.toLowerCase() === 'text') {
            const y = this.parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                tspans.forEach(tspan => {
                    const tspanY = this.parseAttr(tspan, 'y') || y;
                    minY = Math.min(minY, tspanY);
                });
            } else {
                minY = y;
            }
        } else if (['rect', 'image'].includes(el.tagName.toLowerCase())) {
            minY = this.parseAttr(el, 'y');
        } else if (el.tagName.toLowerCase() === 'line') {
            minY = Math.min(this.parseAttr(el, 'y1'), this.parseAttr(el, 'y2'));
        }
        return minY === Infinity ? 0 : minY;
    }

    static getMaxBottom(el) {
        let maxBottom = 0;
        let fontSize = window.getComputedStyle(el).fontSize;
        fontSize = parseFloat(fontSize) || 16;
        const lineHeight = fontSize * 1.2;

        if (el.tagName.toLowerCase() === 'text') {
            const y = this.parseAttr(el, 'y');
            const tspans = el.querySelectorAll('tspan');
            if (tspans.length > 0) {
                let maxTspanY = 0;
                tspans.forEach(tspan => {
                    const tspanY = this.parseAttr(tspan, 'y') || y;
                    maxTspanY = Math.max(maxTspanY, tspanY);
                });
                maxBottom = maxTspanY + lineHeight;
            } else {
                const lines = (el.textContent || '').split('\n').length;
                maxBottom = y + lines * lineHeight;
            }
        } else if (el.tagName.toLowerCase() === 'rect') {
            maxBottom = this.parseAttr(el, 'y') + this.parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'image') {
            maxBottom = this.parseAttr(el, 'y') + this.parseAttr(el, 'height');
        } else if (el.tagName.toLowerCase() === 'line') {
            maxBottom = Math.max(this.parseAttr(el, 'y1'), this.parseAttr(el, 'y2'));
        }
        return maxBottom;
    }

    static estimateSvgHeight(svg) {
        const elements = svg.querySelectorAll('text, rect, line, image');
        let maxBottom = 0;
        let minY = Infinity;

        elements.forEach(el => {
            const y = this.getMinY(el);
            const bottom = this.getMaxBottom(el);
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

    static adjustElementY(el, delta) {
        ['y', 'y1', 'y2'].forEach(attr => {
            if (el.hasAttribute(attr)) {
                el.setAttribute(attr, parseFloat(el.getAttribute(attr)) + delta);
            }
        });
        if (el.tagName.toLowerCase() === 'text') {
            el.querySelectorAll('tspan').forEach(tspan => {
                const y = this.parseAttr(tspan, 'y') || this.parseAttr(el, 'y');
                tspan.setAttribute('y', y + delta);
            });
        }
    }

    static adjustSvg(svg, minY) {
        const elements = svg.querySelectorAll('rect, line, text, image');
        const deltaY = -minY;
        elements.forEach(el => this.adjustElementY(el, deltaY));
    }

    static isPageEmptyOrRectOnly(page) {
        const svg = page.querySelector('svg');
        if (!svg) {
            return !Array.from(page.childNodes).some(node => 
                node.nodeType === Node.ELEMENT_NODE && node.textContent.trim()
            );
        }
        const elements = svg.querySelectorAll('text, line, image');
        return !elements.length;
    }

    static computeSimpleTextHash(text) {
        let hash = 0;
        if (!text || text.length === 0) return 'empty_text';
        for (let i = 0; i < text.length; i++) {
            const chr = text.charCodeAt(i);
            hash = ((hash << 5) - hash) + chr;
            hash |= 0; // Convert to 32-bit integer
        }
        return 'txt_' + Math.abs(hash).toString(36);
    }

    static getGlobalYPosition(textEl) {
        try {
            let globalY = 0;
            const editor = document.getElementById('editor');
            
            let currentPage = textEl.closest('.page');
            if (!currentPage) {
                const bbox = textEl.getBoundingClientRect();
                const editorBbox = editor.getBoundingClientRect();
                return bbox.top - editorBbox.top;
            }
            
            const allPages = Array.from(editor.querySelectorAll('.page'));
            const currentPageIndex = allPages.indexOf(currentPage);
            
            for (let i = 0; i < currentPageIndex; i++) {
                const pageHeight = allPages[i].offsetHeight || 0;
                globalY += pageHeight;
            }
            
            let localY = 0;
            if (textEl.hasAttribute('y')) {
                localY = parseFloat(textEl.getAttribute('y')) || 0;
            } else {
                const bbox = textEl.getBoundingClientRect();
                const pageBox = currentPage.getBoundingClientRect();
                localY = bbox.top - pageBox.top;
            }
            
            globalY += localY;
            return globalY;
        } catch (e) {
            return 0;
        }
    }
}

// Configuration constants
const EditorConfig = {
    MAX_PAGE_HEIGHT: 1120,
    DEFAULT_WIDTH: 816
};
