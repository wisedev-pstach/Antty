# 📚 Antty - Semantic Book Search Engine

A beautiful .NET console application that uses OpenAI embeddings to perform semantic search on PDF books. Find relevant content based on meaning, not just keywords!

## ✨ Features

- 🎨 **Beautiful Console UI** powered by Spectre.Console
- 🔍 **Semantic Search** using OpenAI's text-embedding-3-small model
- 📊 **Progress Indicators** for long-running operations
- 💾 **In-Memory Vector Store** for blazing-fast searches
- 💰 **Low Cost** - approximately $0.02 for a 400-page book ingestion
- ⚙️ **Configuration Management** - API key and book preferences stored automatically
- 📚 **Multiple Books Support** - Switch between different books at runtime
- 🗂️ **Books Folder** - Organized storage for your PDF library

## 🚀 Quick Start

### Prerequisites

- .NET 8.0 or higher
- OpenAI API Key
- A PDF book to search

### Installation

1. **Clone or navigate to the project directory:**
   ```bash
   cd Antty
   ```

2. **Build the solution:**
   ```bash
   dotnet build
   ```

3. **Run the application:**
   ```bash
   dotnet run --project src/Antty.csproj
   ```

4. **First-time setup:**
   - The app will prompt you for your OpenAI API key (stored securely in your AppData folder)
   - Add your PDF files to the `books/` folder, or specify a custom path when prompted



### Running the Application

```bash
dotnet run --project src/Antty.csproj
```

## 📖 Usage

The application provides an interactive menu with the following options:

### 📚 Select/Load Book

1. Choose from PDFs in the `books/` folder
2. Or specify a custom path to any PDF file
3. Your selection is saved for future sessions

### 🔨 Build Knowledge Base

1. Select a book first (if you haven't already)
2. Choose this option to process the PDF and generate embeddings
3. A knowledge base file (`{bookname}_knowledge.json`) will be created in the same folder as your PDF
4. This only needs to be done once per book

### 🔍 Search Your Book

1. Ensure a book is selected and its knowledge base is built
2. Enter your question naturally (e.g., "What does the author say about machine learning?")
3. View the results in a beautiful table with relevance scores
4. Continue asking questions or type 'exit' to return to the main menu

### ⚙️ Settings

- Update your OpenAI API key
- View information about the currently selected book


## 🎯 How It Works

### Ingestion Phase (`IngestionBuilder.cs`)

1. **PDF Extraction**: Reads all pages from your PDF
2. **Paragraph Splitting**: Breaks content into meaningful chunks
3. **Noise Filtering**: Removes headers, footers, and page numbers
4. **Embedding Generation**: Creates 512-dimensional vectors for each chunk
5. **Persistence**: Saves everything to `knowledge.json`

### Search Phase (`SearchEngine.cs`)

The entire search process is handled by a single method: `SearchBookAsync()`

1. **Question Embedding**: Converts your question into a 512-dimensional vector
2. **Similarity Calculation**: Computes cosine similarity with all chunks
3. **Threshold Filtering**: Filters results with similarity > 0.45
4. **Top Results**: Returns the top 5 most relevant passages

## 🛠️ Configuration

### Adjusting Search Sensitivity

In `SearchEngine.cs`, modify the threshold:

```csharp
if (similarity > 0.45)  // Increase to 0.55 for stricter results
```

### Changing Noise Filtering

In `IngestionBuilder.cs`, adjust minimum text length:

```csharp
if (cleanText.Length < 30) continue;  // Increase to filter more aggressively
```

### Batch Size for Embeddings

In `IngestionBuilder.cs`, modify the batch size:

```csharp
int batchSize = 10;  // Increase for faster processing (but watch rate limits)
```

## 📦 Dependencies

- **UglyToad.PdfPig** - PDF text extraction
- **Azure.AI.OpenAI** - OpenAI API client
- **System.Numerics.Tensors** - Fast cosine similarity calculations
- **Spectre.Console** - Beautiful console UI

## 💡 Tips

1. **First run**: Always build the knowledge base before searching
2. **Cost optimization**: The 512-dimension setting saves 66% compared to full embeddings
3. **Large books**: Processing may take a few minutes for very large PDFs
4. **Better results**: Ask specific questions about concepts, not just keywords

## 🎨 Console Features

- Colorful headers and titles
- Real-time progress bars
- Spinner animations
- Formatted tables for results
- Color-coded relevance scores:
  - 🟢 Green: > 80% relevant
  - 🟡 Yellow: 60-80% relevant  
  - 🟠 Orange: 45-60% relevant

## 📝 Project Structure

```
Antty/
├── Antty.sln                 # Solution file
├── books/                    # Place your PDF files here
│   └── *.pdf
├── src/                      # Source code
│   ├── Antty.csproj         # Project file
│   ├── AppConfig.cs         # Configuration management
│   ├── Models.cs            # Data models for chunks and results
│   ├── IngestionBuilder.cs  # PDF processing and embedding generation
│   ├── SearchEngine.cs      # Search logic (SearchBookAsync method)
│   └── Program.cs           # Main application entry point
└── implementation_guide.md   # Original implementation guide
```

## 🔒 Configuration Storage

Your API key and preferences are stored in:
- **Windows:** `%APPDATA%\Antty\config.json`
- **macOS:** `~/Library/Application Support/Antty/config.json`
- **Linux:** `~/.config/Antty/config.json`

The configuration is **never** stored in the project directory, keeping your API key secure.

Knowledge base files are stored alongside their source PDFs with the naming pattern: `{bookname}_knowledge.json`

## 🐛 Troubleshooting

**Error: "Database not found!"**
- You need to run the ingestion step first to create `knowledge.json`

**Error: "No relevant data found"**
- Try lowering the similarity threshold in `SearchEngine.cs`
- Ensure your question relates to content actually in the book

**Slow embedding generation**
- This is normal for large books; progress bars show current status
- Consider increasing the batch size (watch for API rate limits)

## 📄 License

This project is based on the implementation guide for semantic search.

---

Made with ❤️ using .NET and Spectre.Console
