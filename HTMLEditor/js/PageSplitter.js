// Page splitting functionality for content management
class PageSplitter {
    constructor() {
        this.maxPageHeight = EditorConfig.MAX_PAGE_HEIGHT;
    }

    splitIntoPages(frag) {
        if (!frag.hasChildNodes()) {
            return [];
        }

        const tempPage = document.createElement('div');
        tempPage.className = 'page';
        tempPage.appendChild(frag);

        const svgs = tempPage.querySelectorAll('svg');
        if (svgs.length === 0) {
            const editor = document.getElementById('editor');
            editor.appendChild(tempPage);
            let height = tempPage.offsetHeight;
            editor.removeChild(tempPage);
            if (height > this.maxPageHeight) {
                tempPage.style.maxHeight = this.maxPageHeight + 'px';
                tempPage.style.overflow = 'hidden';
                height = this.maxPageHeight;
            }
            tempPage.style.minHeight = this.maxPageHeight + 'px';
            return EditorUtils.isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        if (svgs.length > 1) {
            return this.splitMultipleSvgsIntoPages(Array.from(svgs));
        }

        const svg = svgs[0];
        const { height, minY } = EditorUtils.estimateSvgHeight(svg);
        
        EditorUtils.adjustSvg(svg, minY);

        if (height <= this.maxPageHeight) {
            const width = svg.viewBox?.baseVal?.width || svg.clientWidth || EditorConfig.DEFAULT_WIDTH;
            svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
            svg.style.height = height + 'px';
            svg.style.width = '100%';
            svg.style.display = 'block';
            svg.style.margin = '0';
            svg.style.padding = '0';
            tempPage.style.minHeight = this.maxPageHeight + 'px';
            return EditorUtils.isPageEmptyOrRectOnly(tempPage) ? [] : [tempPage];
        }

        const pages = [];
        const elements = Array.from(svg.children).sort((a, b) => EditorUtils.getMinY(a) - EditorUtils.getMinY(b));
        let currentGroup = [];
        let currentMinY = Infinity;
        let currentMaxBottom = -Infinity;

        elements.forEach(el => {
            const elMinY = EditorUtils.getMinY(el);
            const elMaxBottom = EditorUtils.getMaxBottom(el);
            
            const potentialMinY = currentGroup.length === 0 ? elMinY : Math.min(currentMinY, elMinY);
            const potentialMaxBottom = Math.max(currentMaxBottom, elMaxBottom);
            const potentialHeight = potentialMaxBottom - potentialMinY;

            if (currentGroup.length > 0 && potentialHeight > this.maxPageHeight) {
                const groupHeight = currentMaxBottom - currentMinY;
                const newPage = this.createPageFromGroup(currentGroup, currentMinY, groupHeight);
                if (!EditorUtils.isPageEmptyOrRectOnly(newPage)) {
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
            const newPage = this.createPageFromGroup(currentGroup, currentMinY, groupHeight);
            if (!EditorUtils.isPageEmptyOrRectOnly(newPage)) {
                pages.push(newPage);
            }
        }

        return pages;
    }

    splitMultipleSvgsIntoPages(svgs) {
        let totalHeight = 0;
        let cumulativeY = 0;
        const svgData = [];
        
        svgs.forEach(svg => {
            const { height, minY } = EditorUtils.estimateSvgHeight(svg);
            
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
        
        if (totalHeight <= this.maxPageHeight) {
            const combinedPage = document.createElement('div');
            combinedPage.className = 'page';
            combinedPage.style.minHeight = this.maxPageHeight + 'px';
            
            const combinedSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            const width = svgs[0].viewBox?.baseVal?.width || EditorConfig.DEFAULT_WIDTH;
            
            let currentY = 0;
            svgData.forEach(data => {
                EditorUtils.adjustSvg(data.svg, data.minY);
                data.elements.forEach(el => {
                    const cloned = el.cloneNode(true);
                    EditorUtils.adjustElementY(cloned, currentY);
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
            
            return [combinedPage];
        }
        
        const pages = [];
        let currentPageElements = [];
        let currentPageHeight = 0;
        let currentPageMinY = 0;
        
        let globalYOffset = 0;

        svgData.forEach(data => {
            EditorUtils.adjustSvg(data.svg, data.minY);
            
            data.elements.forEach(el => {
                const elMinY = EditorUtils.getMinY(el);
                const elMaxBottom = EditorUtils.getMaxBottom(el);
                
                EditorUtils.adjustElementY(el, globalYOffset);
                
                const adjustedMinY = elMinY + globalYOffset;
                const adjustedMaxBottom = elMaxBottom + globalYOffset;
                
                if (currentPageElements.length > 0 && (adjustedMaxBottom - currentPageMinY) > this.maxPageHeight) {
                    const page = this.createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
                    if (!EditorUtils.isPageEmptyOrRectOnly(page)) {
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
            const page = this.createPageFromElements(currentPageElements, currentPageMinY, currentPageHeight);
            if (!EditorUtils.isPageEmptyOrRectOnly(page)) {
                pages.push(page);
            }
        }
        
        return pages;
    }

    createPageFromElements(elements, pageMinY, pageHeight) {
        const newPage = document.createElement('div');
        newPage.className = 'page';
        newPage.style.minHeight = this.maxPageHeight + 'px';
        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const width = elements[0].ownerSVGElement?.viewBox?.baseVal?.width || EditorConfig.DEFAULT_WIDTH;

        elements.forEach(el => {
            const cloned = el.cloneNode(true);
            EditorUtils.adjustElementY(cloned, -pageMinY);
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

    createPageFromGroup(group, groupMinY, groupHeight) {
        const newPage = document.createElement('div');
        newPage.className = 'page';
        newPage.style.minHeight = this.maxPageHeight + 'px';
        const newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const width = group[0].ownerSVGElement?.viewBox?.baseVal?.width || EditorConfig.DEFAULT_WIDTH;

        group.forEach(el => {
            const cloned = el.cloneNode(true);
            EditorUtils.adjustElementY(cloned, -groupMinY);
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
}
