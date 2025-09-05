using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace HTMLEditor
{
    /// <summary>
    /// Improved JavaScript bridge with async support and better error handling
    /// </summary>
    public class JavaScriptBridge
    {
        private readonly Form1 _form;
        private readonly object _lockObject = new object();

        public JavaScriptBridge(Form1 form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            Debug.WriteLine("ImprovedJavaScriptBridge initialized");
        }

        public bool SavePageBreakPositions(string breakDataJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(breakDataJson))
                {
                    Debug.WriteLine("SavePageBreakPositions: Empty or null data received");
                    return false;
                }

                lock (_lockObject)
                {
                    Debug.WriteLine($"SavePageBreakPositions called with data length: {breakDataJson.Length}");
                    
                    // Validate JSON format before processing
                    if (!IsValidJson(breakDataJson))
                    {
                        Debug.WriteLine("SavePageBreakPositions: Invalid JSON format");
                        return false;
                    }

                    // Call the form's method to handle the actual saving
                    var result = _form.SavePageBreakPositionsFromJS(breakDataJson);
                    
                    Debug.WriteLine($"SavePageBreakPositions completed with result: {result}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SavePageBreakPositions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Async version for future use if needed
        /// </summary>
        public async Task<bool> SavePageBreakPositionsAsync(string breakDataJson)
        {
            return await Task.Run(() => SavePageBreakPositions(breakDataJson));
        }

        /// <summary>
        /// Simple JSON validation
        /// </summary>
        private bool IsValidJson(string jsonString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jsonString))
                    return false;

                jsonString = jsonString.Trim();
                return (jsonString.StartsWith("{") && jsonString.EndsWith("}")) ||
                       (jsonString.StartsWith("[") && jsonString.EndsWith("]"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Method to notify JavaScript that the bridge is ready
        /// </summary>
        public void NotifyBridgeReady()
        {
            Debug.WriteLine("JavaScript bridge is ready for communication");
        }
    }
}
