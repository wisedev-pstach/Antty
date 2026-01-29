# 📚 Antty - Semantic Document Search CLI

A powerful .NET CLI tool that uses OpenAI embeddings to perform semantic search across multiple documents. Find relevant content based on meaning, not just keywords!

## ✨ Features

- 🎨 **Beautiful Console UI** powered by Spectre.Console
- 🔍 **Semantic Search** using OpenAI's text-embedding-3-small model
- 📊 **Multi-Document Support** - Search across multiple documents simultaneously
- 📄 **Multiple Formats** - Supports PDF, TXT, MD, and JSON files
- 💾 **In-Memory Vector Store** for blazing-fast searches
- 💰 **Low Cost** - approximately $0.02 for a 400-page book ingestion
- ⚙️ **Auto-Configuration** - API key saved securely
- 🚀 **CLI Tool** - Use `antty` from anywhere in your terminal

## 🚀 Installation

### Windows

```powershell
cd C:\WiseDev\Antty
.\install.ps1
```

### Linux/macOS

```bash
cd /path/to/Antty
chmod +x install.sh
./install.sh
```

After installation, you can use `antty` from any directory! You may need to restart your terminal.

## 📖 Usage

### Quick Start

1. **Navigate to a directory with documents:**
   ```bash
   cd ~/Documents/MyBooks
   ```

2. **Run Antty:**
   ```bash
   antty
   ```

3. **Follow the interactive prompts:**
   - Select documents to analyze (PDF, TXT, MD, JSON)
   - Knowledge bases are built automatically if needed
   - Start asking questions!

### Example Workflow

```bash
$ cd ~/Documents/TechDocs
$ antty

Found 5 document(s) in: /Users/you/Documents/TechDocs

Select documents to load:
❯ ◉ architecture-guide.pdf
  ◉ api-documentation.md
  ◯ notes.txt
  ◉ design-patterns.pdf
  ◯ changelog.json

Building knowledge base for: architecture-guide.pdf
✓ Extracted 245 valid paragraphs
✓ Database saved to architecture-guide_knowledge.json

✓ Loaded 3 document(s) for searching

🔍 SEARCH MODE
Ask a question (or 'exit' to quit): What design patterns are recommended for microservices?

┌─────────┬──────┬───────────┬─────────────────────────────────────┐
│ Source  │ Page │ Relevance │ Content                             │
├─────────┼──────┼───────────┼─────────────────────────────────────┤
│ design… │ 42   │ 87.3%     │ For microservices, we recommend...  │
│ archit… │ 15   │ 79.1%     │ The API Gateway pattern is...       │
└─────────┴──────┴───────────┴─────────────────────────────────────┘
```

## 🎯 How It Works

### Supported File Formats

| Format | Extension | Processing |
|--------|-----------|------------|
| PDF    | `.pdf`    | Text extraction via PdfPig |
| Text   | `.txt`    | Direct reading |
| Markdown | `.md`   | Direct reading (formatting preserved) |
| JSON   | `.json`   | Direct reading (structure preserved) |

### Ingestion Phase

1. **File Detection**: Scans current directory for supported formats
2. **Text Extraction**: Extracts content based on file type
3. **Paragraph Splitting**: Breaks content into meaningful chunks
4. **Noise Filtering**: Removes headers, footers, and page numbers
5. **Embedding Generation**: Creates 512-dimensional vectors for each chunk
6. **Persistence**: Saves to `{filename}_knowledge.json` in the same directory

### Search Phase

1. **Question Embedding**: Converts your question into a 512-dimensional vector
2. **Similarity Calculation**: Computes cosine similarity with all chunks across all documents
3. **Threshold Filtering**: Filters results with similarity > 0.45
4. **Top Results**: Returns the top 10 most relevant passages from all documents

## 🛠️ Configuration

### API Key

First run will prompt for your OpenAI API Key, which is stored securely in:
- **Windows:** `%APPDATA%\Antty\config.json`
- **macOS:** `~/Library/Application Support/Antty/config.json`
- **Linux:** `~/.config/Antty/config.json`

### Knowledge Base Files

Generated and stored in a centralized cache directory:
- **Windows:** `%APPDATA%\Antty\cache\`
- **macOS:** `~/Library/Application Support/Antty/cache/`
- **Linux:** `~/.config/Antty/cache/`

Files are named: `{documentname}_{hash}_knowledge.json`

The hash ensures files with the same name in different locations don't conflict.

### Adjusting Search Parameters

Edit `src/SearchEngine.cs`:

```csharp
if (similarity > 0.45)  // Increase to 0.55 for stricter results
```

Edit `src/IngestionBuilder.cs`:

```csharp
if (cleanText.Length < 30) continue;  // Minimum text length
```

## 📦 Dependencies

- **.NET 10.0**
- **Azure.AI.OpenAI** (v2.1.0) - OpenAI API client
- **Spectre.Console** (v0.54.0) - Beautiful console UI
- **System.Numerics.Tensors** (v10.0.2) - Fast cosine similarity
- **UglyToad.PdfPig** (v1.7.0) - PDF text extraction

## 💡 Tips

1. **Organize documents**: Place related documents in the same directory
2. **Cost optimization**: The 512-dimension setting saves 66% compared to full embeddings
3. **Large files**: Processing may take a few minutes for very large documents
4. **Better results**: Ask specific questions about concepts, not just keywords
5. **Reuse knowledge bases**: Once built, knowledge bases are reused automatically

## 🎨 Console Features

- Colorful headers and titles
- Real-time progress bars
- Spinner animations
- Formatted tables for results
- Color-coded relevance scores:
  - 🟢 Green: >80% relevant
  - 🟡 Yellow: 60-80% relevant
  - 🟠 Orange: 45-60% relevant

## 🗑️ Uninstallation

### Windows
```powershell
cd C:\WiseDev\Antty
.\uninstall.ps1
```

### Linux/macOS
```bash
cd /path/to/Antty
./uninstall.sh
```

## 📝 Project Structure

```
Antty/
├── install.ps1               # Windows installation script
├── install.sh                # Linux/macOS installation script
├── uninstall.ps1             # Windows uninstall script
├── uninstall.sh              # Linux/macOS uninstall script
├── Antty.sln                 # Solution file
├── src/                      # Source code
│   ├── Antty.csproj         # Project file
│   ├── Program.cs           # Main CLI entry point
│   ├── AppConfig.cs         # Configuration management
│   ├── Models.cs            # Data models
│   ├── IngestionBuilder.cs  # Multi-format file processing
│   ├── SearchEngine.cs      # Single document search
│   ├── MultiBookSearchEngine.cs  # Multi-document search
│   └── README.md            # This file
└── publish/                  # Published executables (after install)
```

## 🐛 Troubleshooting

**Error: "No supported documents found"**
- Make sure you have PDF, TXT, MD, or JSON files in the current directory

**Error: "Knowledge base not found"**
- Knowledge bases are created automatically on first selection

**Error: "No relevant data found"**
- Try lowering the similarity threshold in `SearchEngine.cs`
- Ensure your question relates to content actually in the documents

**Slow embedding generation**
- Normal for large documents; progress bars show current status
- Knowledge bases are cached and reused

## 📄 License

Open source project for semantic document search.

---

Made with ❤️ using .NET and Spectre.Console
