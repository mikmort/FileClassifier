# FileClassifier

A simple Windows Forms application to help organize PDF files using Azure OpenAI for classification.

## Building

Open the `FileClassifierApp.csproj` project in Visual Studio 2022 or later and build. It targets **.NET 6** with Windows Forms.

## Configuration

Create a file named `env.js` in the repository root with your Azure OpenAI details:

```json
{
  "Endpoint": "https://YOUR_AZURE_OPENAI_ENDPOINT",
  "ApiKey": "YOUR_API_KEY",
  "Deployment": "YOUR_DEPLOYMENT_NAME",
  "ApiVersion": "2023-05-15"
}
```

This file is read by the application at runtime to call Azure OpenAI.

## Usage

1. Select the input directory containing PDFs.
2. Select the output directory with your organized folder structure.
3. Set the limit of files to classify (default is 5).
4. Click **Classify** to preview proposed names and locations.
5. Click **Rename and Move** to apply the changes.
