using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace HTMLEditor
{
    public class ElementSignature
    {
        public string Hash { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
    }

    public class BreakPoint
    {
        // Simplified data structure - only essential fields
        public string Text1Hash { get; set; }
        public string Text2Hash { get; set; }
        public string Text1Content { get; set; } // For debugging/verification
        public string Text2Content { get; set; } // For debugging/verification
        public string BreakId { get; set; }
        public double YOffset { get; set; }
        public double XOffset { get; set; }
        public int PageIndex { get; set; }
    }

    public class PageBreakData
    {
        public string FileHash { get; set; }
        public string FilePath { get; set; }
        public List<BreakPoint> BreakData { get; set; } = new List<BreakPoint>();
        public DateTime LastModified { get; set; }

        [JsonIgnore]
        public List<PageBreakPosition> PageBreakPositions
        {
            get => BreakData.ConvertAll(bp => new PageBreakPosition { YOffset = bp.YOffset, XOffset = bp.XOffset });
            set => BreakData = value.ConvertAll(p => new BreakPoint { YOffset = p.YOffset, XOffset = p.XOffset });
        }
    }

    public class PageBreakPosition
    {
        public double YOffset { get; set; }
        public double XOffset { get; set; }
    }

    public class PageBreakStorage
    {
        public List<PageBreakData> Files { get; set; } = new List<PageBreakData>();
    }

    public class PageBreakManager
    {
        private const string StorageFileName = "pageBreakData.json";
        private string StorageFilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            StorageFileName);

        private PageBreakStorage _storage;

        public PageBreakManager()
        {
            string directory = Path.GetDirectoryName(StorageFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            LoadStorage();
        }

        private void LoadStorage()
        {
            try
            {
                if (File.Exists(StorageFilePath))
                {
                    string json = File.ReadAllText(StorageFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new BreakPointConverter() }
                    };
                    _storage = JsonSerializer.Deserialize<PageBreakStorage>(json, options) ?? new PageBreakStorage();
                }
                else
                {
                    _storage = new PageBreakStorage();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading page break storage: {ex.Message}");
                _storage = new PageBreakStorage();
            }
        }

        public void SaveStorage()
        {
            try
            {
                string directory = Path.GetDirectoryName(StorageFilePath);
                if (!Directory.Exists(directory))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new BreakPointConverter() }
                };
                string json = JsonSerializer.Serialize(_storage, options);
                File.WriteAllText(StorageFilePath, "");
                File.WriteAllText(StorageFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving page break storage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                try
                {
                    string directory = Path.GetDirectoryName(StorageFilePath);
                    if (Directory.Exists(directory))
                    {
                        System.Diagnostics.Debug.WriteLine($"Directory exists: {directory}");
                        try
                        {
                            string testFile = Path.Combine(directory, "test_write_permission.txt");
                            File.WriteAllText(testFile, "Test");
                            File.Delete(testFile);
                            System.Diagnostics.Debug.WriteLine("Write permission test passed");
                        }
                        catch (Exception permEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Write permission test failed: {permEx.Message}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Directory does not exist: {directory}");
                    }
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking directory: {innerEx.Message}");
                }
            }
        }

        public string CalculateFileHash(string content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content ?? "");
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        public PageBreakData GetPageBreakData(string filePath, string fileContent)
        {
            string fileHash = CalculateFileHash(fileContent);
            PageBreakData data = _storage.Files.Find(f => f.FileHash == fileHash);

            if (data == null)
            {
                data = _storage.Files.Find(f => f.FilePath == filePath);
            }

            if (data == null)
            {
                data = new PageBreakData
                {
                    FileHash = fileHash,
                    FilePath = filePath,
                    LastModified = DateTime.Now
                };
                _storage.Files.Add(data);
            }

            return data;
        }

        public void SavePageBreakData(string filePath, string fileContent, string breakDataJson)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new BreakPointConverter() }
                };

                List<BreakPoint> newBreaks = JsonSerializer.Deserialize<List<BreakPoint>>(breakDataJson, options) ?? new List<BreakPoint>();

                // Validate - only keep breaks with required hash data
                newBreaks = newBreaks
                    .Where(bp => !string.IsNullOrEmpty(bp.Text1Hash) && !string.IsNullOrEmpty(bp.Text2Hash))
                    .ToList();

                // Remove duplicates by breakId first (most important)
                var uniqueBreaks = new List<BreakPoint>();
                foreach (var bp in newBreaks)
                {
                    bool isDuplicate = false;

                    // Primary check: duplicate breakId
                    if (!string.IsNullOrEmpty(bp.BreakId))
                    {
                        isDuplicate = uniqueBreaks.Any(existing => existing.BreakId == bp.BreakId);
                    }

                    if (!isDuplicate)
                    {
                        uniqueBreaks.Add(bp);
                    }
                }
                newBreaks = uniqueBreaks;

                PageBreakData data = GetPageBreakData(filePath, fileContent);

                // Replace all existing breaks with the new list instead of just adding new ones
                // This ensures removed page breaks are properly removed from storage
                data.BreakData = newBreaks;
                data.LastModified = DateTime.Now;
                SaveStorage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving page break data: {ex.Message}");
            }
        }

        public void UpdatePageBreakPositions(string filePath, string fileContent, List<BreakPoint> breakData)
        {
            PageBreakData data = GetPageBreakData(filePath, fileContent);
            data.BreakData = breakData ?? new List<BreakPoint>();
            data.LastModified = DateTime.Now;
            SaveStorage();
        }

        public void UpdatePageBreakPositions(string filePath, string fileContent, List<PageBreakPosition> positions)
        {
            List<BreakPoint> breakData = positions?.ConvertAll(p => new BreakPoint
            {
                YOffset = p.YOffset,
                XOffset = p.XOffset
            }) ?? new List<BreakPoint>();
            UpdatePageBreakPositions(filePath, fileContent, breakData);
        }

        public void UpdatePageBreakPositions(string filePath, string fileContent, List<string> legacyPositions)
        {
            List<BreakPoint> breakData = new List<BreakPoint>();
            if (legacyPositions != null)
            {
                foreach (string pos in legacyPositions)
                {
                    if (double.TryParse(pos, out double yOffset))
                    {
                        breakData.Add(new BreakPoint { YOffset = yOffset });
                    }
                    else
                    {
                        breakData.Add(new BreakPoint { YOffset = 0 });
                    }
                }
            }
            UpdatePageBreakPositions(filePath, fileContent, breakData);
        }
    }

    public class BreakPointConverter : JsonConverter<BreakPoint>
    {
        public override BreakPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            var breakPoint = new BreakPoint();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return breakPoint;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName.ToLower())
                    {
                        // Only read simplified essential fields
                        case "text1hash":
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                breakPoint.Text1Hash = reader.GetString();
                            }
                            break;
                        case "text2hash":
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                breakPoint.Text2Hash = reader.GetString();
                            }
                            break;
                        case "text1content":
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                breakPoint.Text1Content = reader.GetString();
                            }
                            break;
                        case "text2content":
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                breakPoint.Text2Content = reader.GetString();
                            }
                            break;
                        case "breakid":
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                breakPoint.BreakId = reader.GetString();
                            }
                            break;
                        case "yoffset":
                            breakPoint.YOffset = reader.GetDouble();
                            break;
                        case "xoffset":
                            breakPoint.XOffset = reader.GetDouble();
                            break;
                        case "pageindex":
                            breakPoint.PageIndex = reader.GetInt32();
                            break;
                    }
                }
            }
            throw new JsonException("Unexpected end of JSON");
        }

        public override void Write(Utf8JsonWriter writer, BreakPoint breakPoint, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            
            // Write only simplified essential fields
            writer.WriteString("text1Hash", breakPoint.Text1Hash);
            writer.WriteString("text2Hash", breakPoint.Text2Hash);
            writer.WriteString("text1Content", breakPoint.Text1Content);
            writer.WriteString("text2Content", breakPoint.Text2Content);
            writer.WriteString("breakId", breakPoint.BreakId);
            writer.WriteNumber("yOffset", breakPoint.YOffset);
            writer.WriteNumber("xOffset", breakPoint.XOffset);
            writer.WriteNumber("pageIndex", breakPoint.PageIndex);
            
            writer.WriteEndObject();
        }
    }
}
//namespace HTMLEditor
//{
//    // Data structure for storing page breaks for a single file
//    public class PageBreakData
//    {
//        public string FileHash { get; set; }
//        public string FilePath { get; set; }
//        public List<PageBreakPosition> PageBreakPositions { get; set; } = new List<PageBreakPosition>();
//        public DateTime LastModified { get; set; }
//    }

//    // Structure to store a single page break position with just y-offset
//    public class PageBreakPosition
//    {
//        public double YOffset { get; set; } // Y-offset from the top of the document (relative to original SVG)
//        public double XOffset { get; set; }
//    }

//    // Collection of page break data for all files
//    public class PageBreakStorage
//    {
//        public List<PageBreakData> Files { get; set; } = new List<PageBreakData>();
//    }

//    // Manager class to handle saving and loading page break data
//    public class PageBreakManager
//    {
//        private const string StorageFileName = "pageBreakData.json";
//        private string StorageFilePath => Path.Combine(
//            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
//            "HTMLEditor",
//            StorageFileName);

//        private PageBreakStorage _storage;

//        public PageBreakManager()
//        {
//            // Ensure directory exists
//            string directory = Path.GetDirectoryName(StorageFilePath);
//            if (!Directory.Exists(directory))
//            {
//                Directory.CreateDirectory(directory);
//            }

//            // Load existing data or create new storage
//            LoadStorage();
//        }

//        // Load storage from file or create new if not exists
//        private void LoadStorage()
//        {
//            try
//            {
//                if (File.Exists(StorageFilePath))
//                {
//                    string json = File.ReadAllText(StorageFilePath);
//                    _storage = JsonSerializer.Deserialize<PageBreakStorage>(json) ?? new PageBreakStorage();
//                }
//                else
//                {
//                    _storage = new PageBreakStorage();
//                }
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"Error loading page break storage: {ex.Message}");
//                _storage = new PageBreakStorage();
//            }
//        }

//        // Save current storage to file
//        public void SaveStorage()
//        {
//            try
//            {                
//                // Ensure directory exists
//                string directory = Path.GetDirectoryName(StorageFilePath);
//                if (!Directory.Exists(directory))
//                {
//                    System.Diagnostics.Debug.WriteLine($"Creating directory: {directory}");
//                    Directory.CreateDirectory(directory);
//                }

//                // Serialize the data
//                string json = JsonSerializer.Serialize(_storage, new JsonSerializerOptions
//                {
//                    WriteIndented = true
//                });


//                // Write to file
//                File.WriteAllText(StorageFilePath, json);

//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"Error saving page break storage: {ex.Message}");
//                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

//                // Try to get more information about the directory
//                try
//                {
//                    string directory = Path.GetDirectoryName(StorageFilePath);
//                    if (Directory.Exists(directory))
//                    {
//                        System.Diagnostics.Debug.WriteLine($"Directory exists: {directory}");

//                        // Check if we have write permissions
//                        try
//                        {
//                            string testFile = Path.Combine(directory, "test_write_permission.txt");
//                            File.WriteAllText(testFile, "Test");
//                            File.Delete(testFile);
//                            System.Diagnostics.Debug.WriteLine("Write permission test passed");
//                        }
//                        catch (Exception permEx)
//                        {
//                            System.Diagnostics.Debug.WriteLine($"Write permission test failed: {permEx.Message}");
//                        }
//                    }
//                    else
//                    {
//                        System.Diagnostics.Debug.WriteLine($"Directory does not exist: {directory}");
//                    }
//                }
//                catch (Exception innerEx)
//                {
//                    System.Diagnostics.Debug.WriteLine($"Error checking directory: {innerEx.Message}");
//                }
//            }
//        }

//        // Calculate hash for a file content
//        public string CalculateFileHash(string content)
//        {
//            using (SHA256 sha256 = SHA256.Create())
//            {
//                byte[] bytes = Encoding.UTF8.GetBytes(content);
//                byte[] hash = sha256.ComputeHash(bytes);
//                return Convert.ToBase64String(hash);
//            }
//        }

//        // Get page break data for a specific file
//        public PageBreakData GetPageBreakData(string filePath, string fileContent)
//        {
//            string fileHash = CalculateFileHash(fileContent);

//            // Try to find by hash first (most reliable)
//            PageBreakData data = _storage.Files.Find(f => f.FileHash == fileHash);

//            // If not found by hash, try by path
//            if (data == null)
//            {
//                data = _storage.Files.Find(f => f.FilePath == filePath);
//            }

//            // If still not found, create new entry
//            if (data == null)
//            {
//                data = new PageBreakData
//                {
//                    FileHash = fileHash,
//                    FilePath = filePath,
//                    LastModified = DateTime.Now
//                };
//                _storage.Files.Add(data);
//            }

//            return data;
//        }

//        // Update page break positions for a file
//        public void UpdatePageBreakPositions(string filePath, string fileContent, List<PageBreakPosition> positions)
//        {
//            PageBreakData data = GetPageBreakData(filePath, fileContent);
//            data.PageBreakPositions = positions;
//            data.LastModified = DateTime.Now;
//            SaveStorage();
//        }

//        // Legacy method for backward compatibility
//        public void UpdatePageBreakPositions(string filePath, string fileContent, List<string> legacyPositions)
//        {
//            // Convert legacy positions (page indices) to new format
//            // This is a temporary method for backward compatibility
//            List<PageBreakPosition> newPositions = new List<PageBreakPosition>();

//            // If we have legacy positions, convert them to placeholder values
//            // These will be replaced with proper values when page breaks are inserted
//            foreach (string pos in legacyPositions)
//            {
//                // Try to parse the position as a double if it's already a y-offset
//                if (double.TryParse(pos, out double yOffset))
//                {
//                    newPositions.Add(new PageBreakPosition
//                    {
//                        YOffset = yOffset
//                    });
//                }
//                else
//                {
//                    // Legacy page index format - use 0 as placeholder
//                    newPositions.Add(new PageBreakPosition
//                    {
//                        YOffset = 0 // Placeholder value that will be replaced later
//                    });
//                }
//            }

//            UpdatePageBreakPositions(filePath, fileContent, newPositions);
//        }
//    }
//}
