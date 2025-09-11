using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HTMLEditor
{
    public static class JavaScriptLoader
    {
        private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

        public static string LoadScript(string scriptName)
        {
            try
            {
                string resourceName = $"HTMLEditor.js.{scriptName}";
                using (Stream stream = Assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new FileNotFoundException($"JavaScript resource '{resourceName}' not found.");
                    }
                    
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading JavaScript '{scriptName}': {ex.Message}");
                return string.Empty;
            }
        }

        public static string LoadAllScripts()
        {
            var scripts = new StringBuilder();
            
            // Load scripts in dependency order - CommunicationBridge first for reliable C# communication
            scripts.AppendLine(LoadScript("CommunicationBridge.js"));
            scripts.AppendLine(LoadScript("CommonUtils.js"));
            scripts.AppendLine(LoadScript("PageSplitter.js"));
            scripts.AppendLine(LoadScript("PageBreakManager.js"));
            scripts.AppendLine(LoadScript("AutoSplitContent.js"));
            scripts.AppendLine(LoadScript("EditorTemplate.js"));
            scripts.AppendLine(LoadScript("RestorePageBreaks.js"));
            
            return scripts.ToString();
        }

        public static string GetTogglePageBreakScript()
        {
            return @"
(function() {
    if (typeof window.pageBreakManager === 'undefined') {
        window.pageBreakManager = new PageBreakManager();
    }
    window.pageBreakManager.togglePageBreak();
})();";
        }

        public static string GetAutoSplitScript()
        {
            return @"
(function() {
    if (typeof window.autoSplitContent === 'undefined') {
        window.autoSplitContent = new AutoSplitContent();
    }
    window.autoSplitContent.execute();
})();";
        }

        public static string GetPageBreakNotificationScript()
        {
            return @"
window.notifyPageBreakPositionsReady = async function() {
    console.log('Page break positions ready');
    if (window.pageBreakPositionsData) {
        try {
            // Use event-driven communication bridge
            if (window.communicationBridge) {
                await window.communicationBridge.savePageBreakPositions(window.pageBreakPositionsData);
                console.log('Successfully saved page break positions via communication bridge');
            } else {
                console.error('Communication bridge not available');
            }
            
            // Clear the data after processing
            window.pageBreakPositionsData = null;
            window.pageBreakPositionsProcessed = false;
        } catch (error) {
            console.error('Error saving page break positions:', error);
        }
    }
};";
        }

        public static string GetSavePageBreakPositionsScript()
        {
            return @"
(async function() {
    if (typeof window.pageBreakManager === 'undefined') {
        window.pageBreakManager = new PageBreakManager();
    }
    try {
        await window.pageBreakManager.savePageBreakPositions();
    } catch (error) {
        console.error('Error in GetSavePageBreakPositionsScript:', error);
    }
})();";
        }

        public static string GetRestorePageBreaksScript(string breakDataJson)
        {
            // Escape the JSON string for JavaScript
            string escapedJson = breakDataJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
            
            // Call the function from RestorePageBreaks.js
            return $@"
(function() {{
    if (typeof getRestorePageBreaksScript === 'function') {{
        const script = getRestorePageBreaksScript('{escapedJson}');
        eval(script);
    }} else {{
        console.error('getRestorePageBreaksScript function not found. Make sure RestorePageBreaks.js is loaded.');
    }}
}})();";
        }
    }
}
