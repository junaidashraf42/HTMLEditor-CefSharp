// HTML Editor Template functionality
class EditorTemplate {
    constructor() {
        this.insertBreakMode = false;
        this.init();
    }

    init() {
        document.addEventListener('DOMContentLoaded', () => {
            this.setupEventListeners();
        });
    }

    enableInsertBreakMode() {
        this.insertBreakMode = !this.insertBreakMode;
        document.getElementById('editor').style.cursor = this.insertBreakMode ? 'crosshair' : 'text';
        return this.insertBreakMode;
    }

    getHtmlContent() {
        return document.getElementById('editor').innerHTML;
    }

    setHtmlContent(html) {
        document.getElementById('editor').innerHTML = html;
    }

    trimPageWhitespace(page) {
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

    setupEventListeners() {
        const editor = document.getElementById('editor');
        if (!editor) return;

        editor.addEventListener('click', (e) => {
            if (!this.insertBreakMode) return;

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
            
            this.insertBreakMode = false;
            editor.style.cursor = 'text';
            e.preventDefault();
        });
    }
}

// Initialize the editor template
window.editorTemplate = new EditorTemplate();

// Global functions for backward compatibility
window.enableInsertBreakMode = () => window.editorTemplate.enableInsertBreakMode();
window.getHtmlContent = () => window.editorTemplate.getHtmlContent();
window.setHtmlContent = (html) => window.editorTemplate.setHtmlContent(html);
window.trimPageWhitespace = (page) => window.editorTemplate.trimPageWhitespace(page);
