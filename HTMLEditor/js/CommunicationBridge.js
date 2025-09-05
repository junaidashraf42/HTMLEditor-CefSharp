// Polling-based communication bridge between JavaScript and C#
class CommunicationBridge {
    constructor() {
        this.isReady = false;
        this.readyPromise = null;
        this.pendingCalls = [];
        this.pollInterval = null;
        this.initializeBridge();
    }

    initializeBridge() {
        this.readyPromise = new Promise((resolve, reject) => {
            // Check if bridge is already available
            if (this.checkBridgeAvailability()) {
                console.log('JavaScript bridge found immediately');
                this.isReady = true;
                this.processPendingCalls();
                resolve();
                return;
            }

            console.log('JavaScript bridge not found, starting polling...');
            
            // Start polling every 100ms to check for bridge availability (faster polling)
            this.pollInterval = setInterval(() => {
                if (this.checkBridgeAvailability()) {
                    console.log('JavaScript bridge found after polling');
                    this.isReady = true;
                    clearInterval(this.pollInterval);
                    this.processPendingCalls();
                    resolve();
                }
            }, 100);

            // Timeout after 30 seconds
            setTimeout(() => {
                if (!this.isReady) {
                    clearInterval(this.pollInterval);
                    console.error('JavaScript bridge initialization timeout after 30 seconds');
                    reject(new Error('JavaScript bridge initialization timeout'));
                }
            }, 30000);
        });
    }

    // Check if the bridge is available using multiple methods
    checkBridgeAvailability() {
        // Check multiple possible ways the bridge might be available
        return (typeof window.javaScriptBridge !== 'undefined' && window.javaScriptBridge !== null) ||
               (typeof javaScriptBridge !== 'undefined' && javaScriptBridge !== null) ||
               (window.chrome && window.chrome.webview && typeof window.chrome.webview.hostObjects !== 'undefined');
    }

    // Queue calls until bridge is ready, then execute immediately
    async callCSharp(methodName, ...args) {
        if (!this.isReady) {
            return new Promise((resolve, reject) => {
                this.pendingCalls.push({ methodName, args, resolve, reject });
            });
        }

        try {
            // Try different ways to access the bridge
            let bridge = window.javaScriptBridge || javaScriptBridge;
            if (!bridge) {
                throw new Error('JavaScript bridge not available');
            }
            
            // Check if method exists
            if (typeof bridge[methodName] !== 'function') {
                // Try common CefSharp method name variations
                const possibleNames = [
                    methodName,
                    methodName.toLowerCase(),
                    methodName.charAt(0).toLowerCase() + methodName.slice(1), // camelCase
                    methodName.charAt(0).toUpperCase() + methodName.slice(1)  // PascalCase
                ];
                
                let foundMethod = null;
                for (const name of possibleNames) {
                    if (typeof bridge[name] === 'function') {
                        foundMethod = name;
                        break;
                    }
                }
                
                if (foundMethod) {
                    const result = bridge[foundMethod](...args);
                    console.log(`Successfully called C# method: ${foundMethod}`);
                    return result;
                } else {
                    throw new Error(`Method '${methodName}' not found on bridge. Available methods: ${Object.getOwnPropertyNames(bridge).join(', ')}`);
                }
            }
            
            const result = bridge[methodName](...args);
            console.log(`Successfully called C# method: ${methodName}`);
            return result;
        } catch (error) {
            console.error(`Error calling C# method ${methodName}:`, error);
            throw error;
        }
    }

    processPendingCalls() {
        while (this.pendingCalls.length > 0) {
            const { methodName, args, resolve, reject } = this.pendingCalls.shift();
            try {
                // Try different ways to access the bridge
                let bridge = window.javaScriptBridge || javaScriptBridge;
                if (!bridge) {
                    reject(new Error('JavaScript bridge not available'));
                    continue;
                }
                
                const result = bridge[methodName](...args);
                resolve(result);
            } catch (error) {
                reject(error);
            }
        }
    }

    // Specific method for saving page break positions
    async savePageBreakPositions(breakData) {
        return this.callCSharp('SavePageBreakPositions', breakData);
    }

    // Wait for bridge to be ready
    async waitForReady() {
        return this.readyPromise;
    }
}

// Create global instance immediately and ensure it's available
(function() {
    if (typeof window.communicationBridge === 'undefined') {
        console.log('Initializing CommunicationBridge...');
        window.communicationBridge = new CommunicationBridge();
        
        // Add additional debugging
        window.communicationBridge.waitForReady().then(() => {
            console.log('CommunicationBridge is ready and connected to C# bridge');
        }).catch((error) => {
            console.error('CommunicationBridge failed to initialize:', error);
        });
        
        console.log('CommunicationBridge instance created');
    }
})();
