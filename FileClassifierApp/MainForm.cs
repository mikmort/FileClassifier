using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace FileClassifierApp
{
    public class MainForm : Form
    {
        private TextBox txtInputPath = new();
        private TextBox txtOutputPath = new();
        private Button btnBrowseInput = new();
        private Button btnBrowseOutput = new();
        private Button btnClassify = new();
        private Button btnRenameMove = new();
        private NumericUpDown numLimit = new();
        private DataGridView gridResults = new();
        private List<ClassificationResult> results;

        public MainForm()
        {
            results = new List<ClassificationResult>();
            Text = "File Classifier";
            Width = 800;
            Height = 600;
            InitializeComponents();
            
            // Set up grid resizing
            this.Resize += MainForm_Resize;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            // Resize the grid to fill the available space
            if (gridResults != null)
            {
                // Adjust grid size (leaving some margin)
                gridResults.Width = this.ClientSize.Width - 20;
                gridResults.Height = this.ClientSize.Height - 120; // Account for buttons and margins
                
                // Redistribute column widths proportionally when grid is resized
                if (gridResults.Columns.Count >= 3)
                {
                    int totalWidth = gridResults.ClientSize.Width;
                    int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
                    int availableWidth = totalWidth - scrollBarWidth - 10; // Small margin
                    
                    // Distribute widths: 25% original, 35% proposed name, 40% proposed path
                    gridResults.Columns[0].Width = (int)(availableWidth * 0.25);
                    gridResults.Columns[1].Width = (int)(availableWidth * 0.35);
                    gridResults.Columns[2].Width = (int)(availableWidth * 0.40);
                }
            }
        }

        private void InitializeComponents()
        {
            Label lblInput = new() { Text = "Input Path:", Left = 10, Top = 15, Width = 80 };
            txtInputPath.Left = 100; txtInputPath.Top = 10; txtInputPath.Width = 500;
            btnBrowseInput.Text = "Browse"; btnBrowseInput.Left = 610; btnBrowseInput.Top = 8; btnBrowseInput.Click += BrowseInput;

            Label lblOutput = new() { Text = "Output Path:", Left = 10, Top = 45, Width = 80 };
            txtOutputPath.Left = 100; txtOutputPath.Top = 40; txtOutputPath.Width = 500;
            btnBrowseOutput.Text = "Browse"; btnBrowseOutput.Left = 610; btnBrowseOutput.Top = 38; btnBrowseOutput.Click += BrowseOutput;

            Label lblLimit = new() { Text = "Limit:", Left = 10, Top = 75, Width = 80 };
            numLimit.Left = 100; numLimit.Top = 70; numLimit.Width = 60; numLimit.Minimum = 1; numLimit.Maximum = 1000; numLimit.Value = 5;

            btnClassify.Text = "Classify"; btnClassify.Left = 200; btnClassify.Top = 68; btnClassify.Click += async (s,e) => await ClassifyAsync();
            btnRenameMove.Text = "Rename and Move"; btnRenameMove.Left = 300; btnRenameMove.Top = 68; btnRenameMove.Click += RenameAndMove;

            gridResults.Left = 10; gridResults.Top = 110; gridResults.Width = 760; gridResults.Height = 430;
            gridResults.ReadOnly = true; gridResults.AllowUserToAddRows = false; gridResults.RowHeadersVisible = false;
            gridResults.Columns.Add("original", "Original File");
            gridResults.Columns.Add("newname", "New File Name");
            gridResults.Columns.Add("newpath", "New Path");
            gridResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            Controls.AddRange(new Control[]{lblInput, txtInputPath, btnBrowseInput,
                lblOutput, txtOutputPath, btnBrowseOutput,
                lblLimit, numLimit, btnClassify, btnRenameMove, gridResults});
        }

        private void BrowseInput(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dlg = new();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtInputPath.Text = dlg.SelectedPath;
            }
        }

        private void BrowseOutput(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dlg = new();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtOutputPath.Text = dlg.SelectedPath;
            }
        }

        private (string endpoint, string apiKey, string deployment, string apiVersion) LoadConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "env.js");
                Console.WriteLine($"Looking for config at: {configPath}");
                
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"Configuration file not found at: {configPath}");
                    return ("", "", "", "");
                }

                string json = File.ReadAllText(configPath);
                Console.WriteLine($"Raw config content: {json}");
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var endpoint = root.TryGetProperty("Endpoint", out var endpointProp) ? endpointProp.GetString() ?? "" : "";
                var apiKey = root.TryGetProperty("ApiKey", out var apiKeyProp) ? apiKeyProp.GetString() ?? "" : "";
                var deployment = root.TryGetProperty("Deployment", out var deploymentProp) ? deploymentProp.GetString() ?? "" : "";
                var apiVersion = root.TryGetProperty("ApiVersion", out var apiVersionProp) ? apiVersionProp.GetString() ?? "" : "";
                
                Console.WriteLine($"Parsed - Endpoint: {endpoint}, Deployment: {deployment}, ApiVersion: {apiVersion}");
                return (endpoint, apiKey, deployment, apiVersion);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading config: {ex.Message}");
                return ("", "", "", "");
            }
        }

        private HttpClient CreateOpenAIClient(string apiKey)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private async Task ClassifyAsync()
        {
            if (string.IsNullOrWhiteSpace(txtInputPath.Text) || string.IsNullOrWhiteSpace(txtOutputPath.Text))
            {
                MessageBox.Show("Please specify both input and output paths.");
                return;
            }

            var (endpoint, apiKey, deployment, apiVersion) = LoadConfig();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Invalid configuration. Please check env.js file.");
                return;
            }

            using var httpClient = CreateOpenAIClient(apiKey);
            
            results.Clear();
            gridResults.Rows.Clear();

            var files = Directory.GetFiles(txtInputPath.Text, "*.pdf")
                               .Take((int)numLimit.Value)
                               .ToArray();

            if (!files.Any())
            {
                MessageBox.Show("No PDF files found in the specified directory.");
                return;
            }

            // Create folder hierarchy string for AI context
            string folderHierarchy = CreateFolderHierarchy(txtOutputPath.Text);

            foreach (string file in files)
            {
                try
                {
                    string contentPreview = ReadFilePreview(file);
                    
                    // Check if we need to process in batches based on content size
                    var (name, path) = await ProcessPDFWithBatching(httpClient, file, contentPreview, folderHierarchy, endpoint, deployment, apiVersion);
                    
                    // Debug: Write the parsed results to console
                    Console.WriteLine("=== PARSED RESULTS ===");
                    Console.WriteLine($"Parsed Name: '{name}'");
                    Console.WriteLine($"Parsed Path: '{path}'");
                    Console.WriteLine("=====================");
                    
                    // Ensure we have valid results
                    if (string.IsNullOrEmpty(name))
                        name = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(path))
                        path = "Uncategorized";
                    
                    // Make sure filename has .pdf extension
                    if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        name += ".pdf";

                    var result = new ClassificationResult
                    {
                        OriginalFile = Path.GetFileName(file),
                        ProposedFile = name,
                        ProposedPath = path
                    };
                    results.Add(result);
                    gridResults.Rows.Add(result.OriginalFile, result.ProposedFile, result.ProposedPath);
                    
                    // Debug: Write final results to console
                    Console.WriteLine("=== FINAL RESULT ===");
                    Console.WriteLine($"Original: {result.OriginalFile}");
                    Console.WriteLine($"Proposed File: {result.ProposedFile}");
                    Console.WriteLine($"Proposed Path: {result.ProposedPath}");
                    Console.WriteLine("===================");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    // Add failed file with error info
                    var result = new ClassificationResult
                    {
                        OriginalFile = Path.GetFileName(file),
                        ProposedFile = $"ERROR: {ex.Message}",
                        ProposedPath = "Error"
                    };
                    results.Add(result);
                    gridResults.Rows.Add(result.OriginalFile, result.ProposedFile, result.ProposedPath);
                    Console.WriteLine($"Error processing {file}: {ex.Message}");
                }
            }

            MessageBox.Show($"Classification complete! Processed {files.Length} files.");
        }

        private string CreateFolderHierarchy(string rootPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Root: {Path.GetFileName(rootPath)}");
                
                BuildHierarchy(rootPath, sb, "", 0);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building folder hierarchy: {ex.Message}");
                return $"Root: {Path.GetFileName(rootPath)}";
            }
        }

        private void BuildHierarchy(string path, StringBuilder sb, string prefix, int level)
        {
            if (level > 3) return; // Limit depth
            
            try
            {
                var dirs = Directory.GetDirectories(path)
                                  .Select(Path.GetFileName)
                                  .Where(name => !string.IsNullOrEmpty(name) && !name.StartsWith("."))
                                  .Take(20) // Limit number of folders
                                  .ToArray();
                
                for (int i = 0; i < dirs.Length; i++)
                {
                    bool isLast = i == dirs.Length - 1;
                    sb.AppendLine($"{prefix}{(isLast ? "└── " : "├── ")}{dirs[i]}");
                    
                    string subPath = Path.Combine(path, dirs[i]);
                    string newPrefix = prefix + (isLast ? "    " : "│   ");
                    BuildHierarchy(subPath, sb, newPrefix, level + 1);
                }
            }
            catch { } // Ignore access denied etc.
        }

        private async Task<(string fileName, string path)> ProcessPDFWithBatching(HttpClient httpClient, string file, string contentPreview, string folderHierarchy, string endpoint, string deployment, string apiVersion)
        {
            var basedName = Path.GetFileName(file);
            
            // Check if we have batched content (multiple chunks)
            var chunks = new List<string>();
            if (contentPreview.Contains("CHUNK_"))
            {
                // Extract all chunks
                var chunkLines = contentPreview.Split('\n');
                foreach (var line in chunkLines)
                {
                    if (line.StartsWith("CHUNK_") && line.Contains("PDF_CONTENT_BASE64:"))
                    {
                        var chunkContent = line.Substring(line.IndexOf("PDF_CONTENT_BASE64:") + "PDF_CONTENT_BASE64:".Length);
                        chunks.Add(chunkContent);
                    }
                }
            }
            else
            {
                // Single chunk (small file)
                chunks.Add(contentPreview);
            }

            Console.WriteLine($"=== PDF BATCHING INFO ===");
            Console.WriteLine($"File: {basedName}");
            Console.WriteLine($"Total chunks: {chunks.Count}");
            Console.WriteLine("========================");

            if (chunks.Count == 1)
            {
                // Process single chunk normally
                return await ProcessSingleChunk(httpClient, chunks[0], folderHierarchy, basedName, endpoint, deployment, apiVersion);
            }
            else
            {
                // Process multiple chunks and aggregate results
                return await ProcessMultipleChunks(httpClient, chunks, folderHierarchy, basedName, endpoint, deployment, apiVersion);
            }
        }

        private async Task<(string fileName, string path)> ProcessSingleChunk(HttpClient httpClient, string contentPreview, string folderHierarchy, string basedName, string endpoint, string deployment, string apiVersion)
        {
            string prompt = $@"Available folders:
{folderHierarchy}

Please analyze this PDF document and provide classification suggestions based on the comprehensive analysis data.

{contentPreview}

The document analysis includes extracted metadata, text content, and detected patterns. Please:
1. Review the complete PDF analysis to understand what type of document this is
2. Extract key information like document purpose, subject matter, dates, company names, etc.
3. Suggest a descriptive filename that reflects the document's actual content and purpose
4. Choose the most appropriate folder from the available folders listed above

IMPORTANT: Respond ONLY with valid JSON in this exact format, no other text:
{{""fileName"": ""descriptive_document_name.pdf"", ""path"": ""folder/subfolder""}}

Guidelines:
- Analyze the PDF analysis data to determine document type (report, invoice, manual, contract, policy, etc.)
- fileName should be highly descriptive and reflect the document's actual content (e.g., ""acme_corp_annual_report_2024.pdf"", ""employee_benefits_policy_jan2024.pdf"", ""project_alpha_technical_specification.pdf"")
- Include relevant dates, company names, or project names in the filename when found in the document
- fileName must end with .pdf
- path should be the most appropriate folder from the available list that matches the document type and content
- Consider the document's subject matter, purpose, and any specific topics or categories mentioned in the content";

            // Debug: Write the prompt to console (but truncate the content for readability)
            Console.WriteLine("=== OUTGOING AI PROMPT (SINGLE CHUNK) ===");
            Console.WriteLine($"File: {basedName}");
            var truncatedPrompt = prompt.Length > 1000 ? prompt.Substring(0, 1000) + "... [TRUNCATED - Full PDF analysis included]" : prompt;
            Console.WriteLine($"Prompt: {truncatedPrompt}");
            Console.WriteLine("========================================");

            try
            {
                string response = await CallOpenAIWithRetry(httpClient, prompt, endpoint, deployment, apiVersion, maxRetries: 3);
                
                // Debug: Write the response to console
                Console.WriteLine("=== AI RESPONSE (SINGLE CHUNK) ===");
                Console.WriteLine($"Raw Response: {response}");
                Console.WriteLine("=================================");
                
                return ParseResponse(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== SINGLE CHUNK PROCESSING FAILED ===");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Using fallback filename");
                Console.WriteLine("=====================================");
                
                return (basedName, "Uncategorized");
            }
        }

        private async Task<(string fileName, string path)> ProcessMultipleChunks(HttpClient httpClient, List<string> chunks, string folderHierarchy, string basedName, string endpoint, string deployment, string apiVersion)
        {
            var chunkResults = new List<(string fileName, string path)>();
            
            Console.WriteLine($"=== PROCESSING {chunks.Count} CHUNKS ===");
            Console.WriteLine("Rate limiting: 2-second delay between chunks");

            for (int i = 0; i < chunks.Count; i++)
            {
                string chunkPrompt = $@"Available folders:
{folderHierarchy}

This is CHUNK {i + 1} of {chunks.Count} from a large PDF document analysis. Please analyze this chunk and provide preliminary classification suggestions.

{chunks[i]}

The document analysis data includes extracted metadata, text content, and detected patterns. Please:
1. Review this chunk of PDF analysis data to understand what type of document this might be
2. Extract any key information like document purpose, subject matter, dates, company names, etc.
3. Based on this chunk, suggest what the document might be and provide a preliminary classification

IMPORTANT: Respond ONLY with valid JSON in this exact format, no other text:
{{""fileName"": ""descriptive_document_name.pdf"", ""path"": ""folder/subfolder"", ""confidence"": ""high/medium/low"", ""summary"": ""brief description of what this chunk contains""}}

Note: This is part of a larger document analysis, so focus on what you can determine from this chunk.";

                Console.WriteLine($"=== PROCESSING CHUNK {i + 1}/{chunks.Count} ===");
                Console.WriteLine($"Chunk size: ~{chunks[i].Length} characters");

                try
                {
                    string chunkResponse = await CallOpenAIWithRetry(httpClient, chunkPrompt, endpoint, deployment, apiVersion, maxRetries: 3);
                    
                    Console.WriteLine($"=== CHUNK {i + 1} RESPONSE ===");
                    Console.WriteLine($"Raw Response: {chunkResponse}");
                    Console.WriteLine("============================");

                    var chunkResult = ParseResponse(chunkResponse);
                    chunkResults.Add(chunkResult);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"=== CHUNK {i + 1} FAILED ===");
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.WriteLine("Using fallback result for this chunk");
                    Console.WriteLine("===========================");
                    
                    // Add fallback result for failed chunk
                    chunkResults.Add(("", ""));
                }

                // Longer delay between chunks to avoid rate limiting (2 seconds)
                if (i < chunks.Count - 1) // Don't delay after the last chunk
                {
                    Console.WriteLine($"Waiting 2 seconds before processing chunk {i + 2}...");
                    await Task.Delay(2000);
                }
            }

            // Now aggregate the results from all chunks
            return await AggregateChunkResults(httpClient, chunkResults, folderHierarchy, basedName, endpoint, deployment, apiVersion);
        }

        private async Task<(string fileName, string path)> AggregateChunkResults(HttpClient httpClient, List<(string fileName, string path)> chunkResults, string folderHierarchy, string basedName, string endpoint, string deployment, string apiVersion)
        {
            var summaries = new List<string>();
            var suggestedNames = new List<string>();
            var suggestedPaths = new List<string>();

            foreach (var result in chunkResults)
            {
                if (!string.IsNullOrEmpty(result.fileName))
                    suggestedNames.Add(result.fileName);
                if (!string.IsNullOrEmpty(result.path))
                    suggestedPaths.Add(result.path);
            }

            string aggregationPrompt = $@"Available folders:
{folderHierarchy}

I have analyzed a large PDF document in {chunkResults.Count} chunks. Here are the results from each chunk:

";

            for (int i = 0; i < chunkResults.Count; i++)
            {
                aggregationPrompt += $"Chunk {i + 1}: fileName=\"{chunkResults[i].fileName}\", path=\"{chunkResults[i].path}\"\n";
            }

            aggregationPrompt += $@"

Based on all the chunk analysis results above, please provide the FINAL classification for this PDF document:

IMPORTANT: Respond ONLY with valid JSON in this exact format, no other text:
{{""fileName"": ""final_descriptive_document_name.pdf"", ""path"": ""folder/subfolder""}}

Guidelines:
- Consider all chunk results to determine the most accurate document type and classification
- Choose the most descriptive filename that best represents the entire document
- Select the most appropriate folder path based on the overall document content
- If chunks suggest different classifications, choose the most consistent or likely one
- fileName must end with .pdf";

            Console.WriteLine("=== AGGREGATION PROMPT ===");
            Console.WriteLine($"Aggregating {chunkResults.Count} chunk results");
            Console.WriteLine("=========================");

            try
            {
                string finalResponse = await CallOpenAIWithRetry(httpClient, aggregationPrompt, endpoint, deployment, apiVersion, maxRetries: 3);
                
                Console.WriteLine("=== FINAL AGGREGATED RESPONSE ===");
                Console.WriteLine($"Raw Response: {finalResponse}");
                Console.WriteLine("================================");

                return ParseResponse(finalResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== AGGREGATION FAILED ===");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Using best guess from individual chunks");
                Console.WriteLine("==============================");
                
                // Fallback: use the most common suggestions from chunks
                var mostCommonName = suggestedNames.GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? basedName;
                var mostCommonPath = suggestedPaths.GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "Uncategorized";
                
                return (mostCommonName, mostCommonPath);
            }
        }

        private static async Task<string> CallOpenAIWithRetry(HttpClient client, string prompt, string endpoint, string deployment, string apiVersion, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"API Call attempt {attempt}/{maxRetries}");
                    return await CallOpenAIAsync(client, prompt, endpoint, deployment, apiVersion);
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429"))
                {
                    lastException = ex;
                    Console.WriteLine($"Rate limit hit on attempt {attempt}");
                    
                    if (attempt < maxRetries)
                    {
                        // Extract wait time from error message or use default
                        int waitSeconds = ExtractWaitTimeFromError(ex.Message);
                        Console.WriteLine($"Waiting {waitSeconds} seconds before retry...");
                        await Task.Delay(waitSeconds * 1000);
                    }
                }
                catch (Exception ex)
                {
                    // For non-rate-limit errors, don't retry
                    Console.WriteLine($"Non-retryable error: {ex.Message}");
                    throw;
                }
            }
            
            // If we get here, all retries failed
            throw new Exception($"Failed after {maxRetries} attempts. Last error: {lastException?.Message}");
        }

        private static int ExtractWaitTimeFromError(string errorMessage)
        {
            // Try to extract wait time from error message like "Please retry after 43 seconds"
            var match = Regex.Match(errorMessage, @"retry after (\d+) seconds");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int waitTime))
            {
                return Math.Min(waitTime + 5, 60); // Add 5 seconds buffer, max 60 seconds
            }
            
            // Default wait time if we can't parse the message
            return 45;
        }

        private static string ReadFilePreview(string path)
        {
            try
            {
                var pdfInfo = ExtractPDFInfo(path);
                string analysisText = pdfInfo.ToAnalysisString();
                
                // Check if the analysis text is large and needs batching
                const int maxChunkSize = 8000; // Smaller chunks since we now have structured text
                
                if (analysisText.Length <= maxChunkSize)
                {
                    // Small analysis, return as single chunk
                    return $"PDF_ANALYSIS:{analysisText}";
                }
                else
                {
                    // Large analysis, split into chunks
                    var chunks = new List<string>();
                    int chunkCount = (int)Math.Ceiling((double)analysisText.Length / maxChunkSize);
                    
                    Console.WriteLine($"=== PDF ANALYSIS CHUNKING ===");
                    Console.WriteLine($"File: {Path.GetFileName(path)}");
                    Console.WriteLine($"Analysis size: {analysisText.Length} characters");
                    Console.WriteLine($"Splitting into {chunkCount} chunks of ~{maxChunkSize} characters each");
                    Console.WriteLine("============================");
                    
                    for (int i = 0; i < chunkCount; i++)
                    {
                        int startIndex = i * maxChunkSize;
                        int length = Math.Min(maxChunkSize, analysisText.Length - startIndex);
                        string chunk = analysisText.Substring(startIndex, length);
                        chunks.Add($"CHUNK_{i + 1}_OF_{chunkCount}_PDF_ANALYSIS:{chunk}");
                    }
                    
                    return string.Join("\n", chunks);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing PDF {path}: {ex.Message}");
                return $"Error analyzing PDF: {ex.Message}";
            }
        }

        private static PDFInfo ExtractPDFInfo(string filePath)
        {
            var info = new PDFInfo
            {
                FileName = Path.GetFileName(filePath),
                FileSizeBytes = new FileInfo(filePath).Length
            };
            
            try
            {
                using var pdfReader = new PdfReader(filePath);
                using var pdfDocument = new PdfDocument(pdfReader);
                
                // Extract metadata
                var docInfo = pdfDocument.GetDocumentInfo();
                info.Title = docInfo.GetTitle() ?? "";
                info.Author = docInfo.GetAuthor() ?? "";
                info.Subject = docInfo.GetSubject() ?? "";
                info.Keywords = docInfo.GetKeywords() ?? "";
                info.Creator = docInfo.GetCreator() ?? "";
                info.Producer = docInfo.GetProducer() ?? "";
                
                // Basic date information (if available, add as string representation)
                info.CreationDate = null;
                info.ModificationDate = null;
                
                info.PageCount = pdfDocument.GetNumberOfPages();
                
                // Extract text from all pages
                var textBuilder = new StringBuilder();
                for (int i = 1; i <= info.PageCount; i++)
                {
                    try
                    {
                        var page = pdfDocument.GetPage(i);
                        var text = PdfTextExtractor.GetTextFromPage(page);
                        textBuilder.AppendLine(text);
                        
                        // Limit total text to prevent memory issues
                        if (textBuilder.Length > 50000) // 50KB of text should be plenty
                        {
                            textBuilder.AppendLine($"\n[TEXT TRUNCATED - ANALYZED {i} OF {info.PageCount} PAGES]");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error extracting text from page {i}: {ex.Message}");
                    }
                }
                
                info.ExtractedText = textBuilder.ToString();
                
                // Analyze the extracted text for patterns
                AnalyzeTextPatterns(info);
                
                return info;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing PDF {filePath}: {ex.Message}");
                info.ExtractedText = $"Error processing PDF: {ex.Message}";
                return info;
            }
        }

        private static void AnalyzeTextPatterns(PDFInfo info)
        {
            var text = info.ExtractedText;
            if (string.IsNullOrEmpty(text)) return;
            
            // Detect potential companies/organizations (capitalized words, common suffixes)
            var companiesRegex = new Regex(@"\b[A-Z][a-zA-Z\s&]{2,30}(?:Inc\.?|LLC|Ltd\.?|Corp\.?|Corporation|Company|Co\.?|Group|Solutions|Services|Systems|Technologies)\b", RegexOptions.IgnoreCase);
            var companyMatches = companiesRegex.Matches(text);
            info.PotentialCompanies = companyMatches.Cast<Match>()
                .Select(m => m.Value.Trim())
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            
            // Detect dates in various formats
            var dateRegex = new Regex(@"\b(?:\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4})\b", RegexOptions.IgnoreCase);
            var dateMatches = dateRegex.Matches(text);
            info.PotentialDates = dateMatches.Cast<Match>()
                .Select(m => m.Value.Trim())
                .Distinct()
                .Take(20)
                .ToList();
            
            // Detect email addresses
            var emailRegex = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
            var emailMatches = emailRegex.Matches(text);
            info.PotentialEmails = emailMatches.Cast<Match>()
                .Select(m => m.Value.Trim())
                .Distinct()
                .ToList();
            
            // Detect phone numbers
            var phoneRegex = new Regex(@"\b(?:\+?1[-.\s]?)?\(?([0-9]{3})\)?[-.\s]?([0-9]{3})[-.\s]?([0-9]{4})\b");
            var phoneMatches = phoneRegex.Matches(text);
            info.PotentialPhones = phoneMatches.Cast<Match>()
                .Select(m => m.Value.Trim())
                .Distinct()
                .ToList();
            
            // Detect document types based on keywords
            var docTypeKeywords = new Dictionary<string, string[]>
            {
                ["Invoice"] = new[] { "invoice", "bill", "payment due", "amount due", "subtotal", "tax", "total amount" },
                ["Contract"] = new[] { "agreement", "contract", "terms and conditions", "whereas", "party of the first part", "hereby agrees" },
                ["Report"] = new[] { "executive summary", "analysis", "findings", "conclusion", "methodology", "results", "annual report", "quarterly" },
                ["Policy"] = new[] { "policy", "procedure", "guidelines", "regulations", "compliance", "shall", "must", "required" },
                ["Manual"] = new[] { "manual", "instructions", "how to", "step by step", "procedure", "guide", "tutorial" },
                ["Proposal"] = new[] { "proposal", "bid", "quotation", "estimate", "scope of work", "deliverables", "timeline" },
                ["Financial Statement"] = new[] { "balance sheet", "income statement", "cash flow", "assets", "liabilities", "revenue", "expenses" },
                ["Legal Document"] = new[] { "whereas", "therefore", "jurisdiction", "legal", "court", "plaintiff", "defendant", "attorney" },
                ["Technical Specification"] = new[] { "specifications", "requirements", "technical", "system", "architecture", "design", "implementation" },
                ["Certificate"] = new[] { "certificate", "certification", "certify", "awarded", "completion", "achievement" }
            };
            
            var lowerText = text.ToLower();
            foreach (var docType in docTypeKeywords)
            {
                var keywordCount = docType.Value.Count(keyword => lowerText.Contains(keyword));
                if (keywordCount >= 2) // Require at least 2 matching keywords
                {
                    info.DocumentTypes.Add($"{docType.Key} (confidence: {keywordCount}/{docType.Value.Length} keywords)");
                }
            }
        }

        private static async Task<string> CallOpenAIAsync(HttpClient client, string prompt, string endpoint, string deployment, string apiVersion)
        {
            try
            {
                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 800,
                    temperature = 0.1
                };

                string json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
                
                var response = await client.PostAsync(url, content);
                string responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"HTTP Status: {response.StatusCode}");
                Console.WriteLine($"Response Headers: {response.Headers}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error Response: {responseContent}");
                    throw new HttpRequestException($"Request failed with status {response.StatusCode}: {responseContent}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    return message.GetProperty("content").GetString() ?? "";
                }
                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in CallOpenAIAsync: {ex.Message}");
                throw;
            }
        }

        private static (string fileName, string path) ParseResponse(string response)
        {
            try
            {
                // Clean the response - remove any markdown code blocks or extra text
                string cleaned = response.Trim();
                
                // Remove markdown code blocks if present
                if (cleaned.StartsWith("```json"))
                {
                    cleaned = cleaned.Substring(7);
                }
                if (cleaned.StartsWith("```"))
                {
                    cleaned = cleaned.Substring(3);
                }
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                }
                
                cleaned = cleaned.Trim();
                
                // Find JSON content - look for opening brace
                int jsonStart = cleaned.IndexOf('{');
                int jsonEnd = cleaned.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }
                
                Console.WriteLine($"Attempting to parse JSON: {cleaned}");
                
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;
                
                string fileName = root.TryGetProperty("fileName", out var fileNameProp) ? fileNameProp.GetString() ?? "" : "";
                string path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                
                return (fileName, path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing response: {ex.Message}");
                Console.WriteLine($"Raw response was: {response}");
                return ("", "");
            }
        }

        private void RenameAndMove(object? sender, EventArgs e)
        {
            if (!results.Any())
            {
                MessageBox.Show("No classification results to process.");
                return;
            }

            int moved = 0, errors = 0;
            var errorMessages = new List<string>();

            foreach (var result in results)
            {
                try
                {
                    if (result.ProposedPath == "Error") continue;

                    string sourcePath = Path.Combine(txtInputPath.Text, result.OriginalFile);
                    string destFolderPath = Path.Combine(txtOutputPath.Text, result.ProposedPath);
                    string destFilePath = Path.Combine(destFolderPath, result.ProposedFile);

                    if (!Directory.Exists(destFolderPath))
                        Directory.CreateDirectory(destFolderPath);

                    File.Move(sourcePath, destFilePath);
                    moved++;
                }
                catch (Exception ex)
                {
                    errors++;
                    errorMessages.Add($"{result.OriginalFile}: {ex.Message}");
                }
            }

            string message = $"Successfully moved {moved} files.";
            if (errors > 0)
            {
                message += $"\n{errors} errors occurred:\n" + string.Join("\n", errorMessages.Take(5));
                if (errorMessages.Count > 5)
                    message += $"\n... and {errorMessages.Count - 5} more errors.";
            }

            MessageBox.Show(message);
        }
    }

    public class ClassificationResult
    {
        public string OriginalFile { get; set; } = "";
        public string ProposedFile { get; set; } = "";
        public string ProposedPath { get; set; } = "";
    }

    public class PDFInfo
    {
        public string FileName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Keywords { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Producer { get; set; } = "";
        public DateTime? CreationDate { get; set; }
        public DateTime? ModificationDate { get; set; }
        public int PageCount { get; set; }
        public string ExtractedText { get; set; } = "";
        public List<string> DetectedLanguages { get; set; } = new();
        public List<string> PotentialCompanies { get; set; } = new();
        public List<string> PotentialDates { get; set; } = new();
        public List<string> PotentialEmails { get; set; } = new();
        public List<string> PotentialPhones { get; set; } = new();
        public List<string> DocumentTypes { get; set; } = new();
        public long FileSizeBytes { get; set; }
        
        public string ToAnalysisString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== PDF DOCUMENT ANALYSIS ===");
            sb.AppendLine($"File: {FileName}");
            sb.AppendLine($"File Size: {FileSizeBytes:N0} bytes ({FileSizeBytes / 1024.0:F1} KB)");
            sb.AppendLine($"Pages: {PageCount}");
            sb.AppendLine();
            
            sb.AppendLine("=== METADATA ===");
            if (!string.IsNullOrEmpty(Title)) sb.AppendLine($"Title: {Title}");
            if (!string.IsNullOrEmpty(Author)) sb.AppendLine($"Author: {Author}");
            if (!string.IsNullOrEmpty(Subject)) sb.AppendLine($"Subject: {Subject}");
            if (!string.IsNullOrEmpty(Keywords)) sb.AppendLine($"Keywords: {Keywords}");
            if (!string.IsNullOrEmpty(Creator)) sb.AppendLine($"Creator: {Creator}");
            if (!string.IsNullOrEmpty(Producer)) sb.AppendLine($"Producer: {Producer}");
            if (CreationDate.HasValue) sb.AppendLine($"Created: {CreationDate.Value:yyyy-MM-dd HH:mm:ss}");
            if (ModificationDate.HasValue) sb.AppendLine($"Modified: {ModificationDate.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            if (PotentialCompanies.Any())
            {
                sb.AppendLine("=== DETECTED COMPANIES/ORGANIZATIONS ===");
                foreach (var company in PotentialCompanies.Take(10))
                    sb.AppendLine($"- {company}");
                sb.AppendLine();
            }
            
            if (PotentialDates.Any())
            {
                sb.AppendLine("=== DETECTED DATES ===");
                foreach (var date in PotentialDates.Take(10))
                    sb.AppendLine($"- {date}");
                sb.AppendLine();
            }
            
            if (PotentialEmails.Any())
            {
                sb.AppendLine("=== DETECTED EMAILS ===");
                foreach (var email in PotentialEmails.Take(5))
                    sb.AppendLine($"- {email}");
                sb.AppendLine();
            }
            
            if (DocumentTypes.Any())
            {
                sb.AppendLine("=== POTENTIAL DOCUMENT TYPES ===");
                foreach (var type in DocumentTypes)
                    sb.AppendLine($"- {type}");
                sb.AppendLine();
            }
            
            sb.AppendLine("=== EXTRACTED TEXT (SAMPLE) ===");
            if (ExtractedText.Length > 2000)
            {
                sb.AppendLine(ExtractedText.Substring(0, 1000));
                sb.AppendLine("\n[... MIDDLE CONTENT TRUNCATED ...]");
                sb.AppendLine(ExtractedText.Substring(ExtractedText.Length - 1000));
            }
            else
            {
                sb.AppendLine(ExtractedText);
            }
            
            return sb.ToString();
        }
    }
}
