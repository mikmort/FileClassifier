using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileClassifierApp
{
    public class ClassificationResult
    {
        public string OriginalFile { get; set; } = "";
        public string ProposedFile { get; set; } = "";
        public string ProposedPath { get; set; } = "";
    }

    public class MainForm : Form
    {
        private TextBox txtInputPath = new();
        private Button btnBrowseInput = new();
        private TextBox txtOutputPath = new();
        private Button btnBrowseOutput = new();
        private NumericUpDown numLimit = new();
        private Button btnClassify = new();
        private Button btnRenameMove = new();
        private DataGridView gridResults = new();
        private readonly List<ClassificationResult> results = new();

        public MainForm()
        {
            Text = "File Classifier";
            Width = 800;
            Height = 600;
            InitializeComponents();
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

        private async Task ClassifyAsync()
        {
            gridResults.Rows.Clear();
            results.Clear();

            string inputPath = txtInputPath.Text;
            string outputPath = txtOutputPath.Text;

            if (!Directory.Exists(inputPath) || !Directory.Exists(outputPath))
            {
                MessageBox.Show("Please select valid input and output paths.");
                return;
            }

            string[] folders = Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories);
            for (int i = 0; i < folders.Length; i++)
                folders[i] = folders[i].Substring(outputPath.Length).TrimStart(Path.DirectorySeparatorChar);

            string folderHierarchy = string.Join("\n", folders);

            var files = Directory.GetFiles(inputPath, "*.pdf");
            int limit = (int)numLimit.Value;
            var httpClient = CreateOpenAIClient();

            foreach (var file in files)
            {
                if (results.Count >= limit) break;

                string contentPreview = ReadFilePreview(file);
                string prompt = $"Available folders:\n{folderHierarchy}\nSuggest best folder and file name for:\n{contentPreview}";

                string response = await CallOpenAIAsync(httpClient, prompt);
                var (name, path) = ParseResponse(response);
                var result = new ClassificationResult
                {
                    OriginalFile = Path.GetFileName(file),
                    ProposedFile = name,
                    ProposedPath = path
                };
                results.Add(result);
                gridResults.Rows.Add(result.OriginalFile, result.ProposedFile, result.ProposedPath);
            }
        }

        private void RenameAndMove(object? sender, EventArgs e)
        {
            string inputPath = txtInputPath.Text;
            string outputPath = txtOutputPath.Text;
            foreach (var result in results)
            {
                string source = Path.Combine(inputPath, result.OriginalFile);
                string targetDir = Path.Combine(outputPath, result.ProposedPath);
                Directory.CreateDirectory(targetDir);
                string target = Path.Combine(targetDir, result.ProposedFile);
                if (File.Exists(source))
                {
                    File.Move(source, target, true);
                }
            }
            MessageBox.Show("Files moved.");
        }

        private static string ReadFilePreview(string path)
        {
            // For simplicity we just read first 2000 characters if text
            try
            {
                using var reader = new StreamReader(path);
                char[] buffer = new char[2000];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, read);
            }
            catch
            {
                return Path.GetFileName(path);
            }
        }

        private static HttpClient CreateOpenAIClient()
        {
            var config = LoadConfig();
            var client = new HttpClient();
            client.BaseAddress = new Uri(config.Endpoint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
            return client;
        }

        private static async Task<string> CallOpenAIAsync(HttpClient client, string prompt)
        {
            var config = LoadConfig();
            var request = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You classify documents."},
                    new { role = "user", content = prompt }
                },
                max_tokens = 100,
                temperature = 0.2
            };

            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"/openai/deployments/{config.Deployment}/chat/completions?api-version={config.ApiVersion}", content);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            return responseString;
        }

        private static (string name, string path) ParseResponse(string response)
        {
            // Simplified parser expecting JSON {"fileName":"...","path":"..."}
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                string name = root.GetProperty("fileName").GetString() ?? "";
                string path = root.GetProperty("path").GetString() ?? "";
                return (name, path);
            }
            catch
            {
                return ("", "");
            }
        }

        private class OpenAIConfig
        {
            public string Endpoint { get; set; } = "";
            public string ApiKey { get; set; } = "";
            public string Deployment { get; set; } = "";
            public string ApiVersion { get; set; } = "2023-03-15-preview";
        }

        private static OpenAIConfig LoadConfig()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "..", "env.js");
            if (!File.Exists(path))
            {
                return new OpenAIConfig();
            }
            string text = File.ReadAllText(path);
            try
            {
                var config = JsonSerializer.Deserialize<OpenAIConfig>(text);
                return config ?? new OpenAIConfig();
            }
            catch
            {
                return new OpenAIConfig();
            }
        }
    }
}
