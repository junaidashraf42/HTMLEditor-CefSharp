// Auto-split content functionality for initial page setup
class AutoSplitContent {
    constructor() {
        this.maxPageHeight = EditorConfig.MAX_PAGE_HEIGHT;
        this.pageSplitter = new PageSplitter();
        this.pageBreakManager = new PageBreakManager();
    }

    async execute() {
        const editor = document.getElementById('editor');
        if (!editor) {
            return;
        }

        const initialPage = editor.querySelector('.page');
        if (!initialPage) {
            return;
        }

        const frag = document.createDocumentFragment();
        while (initialPage.firstChild) {
            frag.appendChild(initialPage.firstChild);
        }

        if (!frag.hasChildNodes()) {
            initialPage.remove();
            return;
        }

        const initialPages = this.pageSplitter.splitIntoPages(frag);
        if (initialPages.length === 0) {
            initialPage.appendChild(frag);
            return;
        }

        const pagesWithBreaks = this.pageBreakManager.insertBreaks(initialPages, true);
        try {
            const parent = initialPage.parentNode;
            const nextSibling = initialPage.nextSibling;
            parent.removeChild(initialPage);
            pagesWithBreaks.forEach(element => {
                parent.insertBefore(element, nextSibling);
            });
        } catch (e) {
            initialPage.appendChild(frag);
        }

        this.pageBreakManager.adjustAllSvgs();
        this.pageBreakManager.cleanupEmptyOrRectOnlyPages();
        // Removed savePageBreakPositions() call - page breaks should only be saved on explicit user actions
        // this.pageBreakManager.savePageBreakPositions();
    }
}
