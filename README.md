# 📚 Antty

**A terminal-based semantic search and chat interface for your documents.** Query PDFs, markdown files, and text documents using natural language. Works entirely offline with local models, or connect to OpenAI, Anthropic, Google, DeepSeek, Groq, or xAI with your own API keys.

Built on [**MaIN.NET**](https://github.com/wisedev-code/MaIN.NET) for multi-provider orchestration.

## ✨ Key Features

- **🔒 Privacy-First** - Runs entirely on your machine with local embeddings and Ollama models. No data leaves your computer.
- **🔌 Flexible Backends** - Bring your own API keys for OpenAI, Anthropic (Claude), Google (Gemini), DeepSeek, Groq, or xAI
- **🔍 Semantic Search** - Find content by meaning, not just keywords
- **📚 Multi-Document** - Query across multiple files simultaneously
- **💾 Smart Caching** - Build knowledge bases once, reuse forever. Switch between providers without re-indexing.
- **📄 Format Support** - PDF, TXT, MD, JSON

## 🚀 Installation

### Windows

```powershell
iwr https://raw.githubusercontent.com/wisedev-pstach/Antty/main/install.cmd -OutFile $env:TEMP\antty-install.cmd; & $env:TEMP\antty-install.cmd
```

### Linux/macOS
```bash
curl -fsSL https://raw.githubusercontent.com/wisedev-pstach/Antty/main/install.sh | bash
```

> **Note:** After installation, restart your terminal, then run `antty` from anywhere.

## 📖 Usage

Navigate to a folder with documents and launch:

```bash
cd ~/Documents/Research
antty
```

The first run will ask you to:
1. Choose embedding provider (local via Nomic Embed, or OpenAI)
2. Select chat backend (Ollama for local, or cloud providers with your API key)
3. Pick documents to load
4. Start conversing

### Example

```
$ antty

Found 3 documents

Select documents:
❯ ◉ paper.pdf
  ◉ notes.md
  ◉ summary.txt

✓ Indexed 3 documents

💬 Assistant

You: What are the main findings?

🔎 Searching...
📖 Reading page 5 from paper.pdf...

Assistant: The research demonstrates that multi-head attention mechanisms...
[continues with detailed answer citing specific pages]
```

## 🧠 Built on MaIN.NET

Antty uses [**MaIN.NET**](https://github.com/wisedev-code/MaIN.NET) to orchestrate conversations across multiple backend providers. This gives you:

- **Unified Interface** - Same API for OpenAI, Anthropic, Google, DeepSeek, Groq, xAI, and Ollama
- **Tool-Calling Agents** - Autonomous document search and page reading
- **Streaming Responses** - See answers appear in real-time
- **Context Management** - Conversation history for natural follow-ups

## 🔌 Supported Backends

### Local (Privacy-First, No API Key)
- **Ollama** - Any model you've pulled (Llama, Mistral, Qwen, etc.)
- **Nomic Embed** - Local embeddings, runs on CPU/GPU

### Cloud (Bring Your Own API Key)
- **OpenAI** - GPT-5.2, o3, GPT-5 Nano, GPT-4o, o1
- **Anthropic** - Claude 4.5 Sonnet, Claude 4.5 Haiku, Claude 4.5 Opus, Claude 3.7 Sonnet
- **Google** - Gemini 3.0 Pro, Gemini 2.5 Pro, Gemini 2.5 Flash
- **DeepSeek** - DeepSeek R1 (Reasoner), DeepSeek V3 (Chat)
- **Groq** - Llama 3.3 70B, Llama 3.1 8B, Mixtral 8x7B
- **xAI** - Grok-3, Grok-2

Knowledge bases are isolated by embedding provider, so switching between local and OpenAI embeddings doesn't require re-indexing if you've used both before.

## 🔧 Configuration

### Config Files
Settings are stored in:
- **Windows:** `%APPDATA%\Antty\config.json`
- **macOS/Linux:** `~/.config/Antty/config.json`

### Knowledge Base Cache
Built indices are saved in:
- **Windows:** `%APPDATA%\Antty\cache\`
- **macOS/Linux:** `~/.config/Antty/cache/`

Named: `{documentname}_{hash}_{provider}_knowledge.json`

## 💡 Tips

- **Privacy:** Use local Ollama + Nomic Embed for completely offline operation
- **Organize by topic:** Keep related documents in one folder for better cross-references
- **Ask specific questions:** "What causes X?" works better than "Tell me about this"
- **Provider switching:** If you've cached indices for multiple providers, switching is instant

## 🗑️ Uninstall

### Windows
```powershell
cd C:\path\to\Antty
.\uninstall.ps1
```

### Linux/macOS
```bash
cd /path/to/Antty
./uninstall.sh
```

This removes `antty` from PATH. Cached knowledge bases remain in your config directory.

## 📦 Tech Stack

- **.NET 10** - Modern C# runtime
- **[MaIN.NET](https://github.com/wisedev-code/MaIN.NET)** - LLM Interface
- **Spectre.Console** - Terminal UI
- **PdfPig** - PDF text extraction

## 🐛 Troubleshooting

**No documents found**  
Place `.pdf`, `.txt`, `.md`, or `.json` files in the current directory before running `antty`.

**API key not configured**  
Run `antty`, choose Settings, and enter your API key for the selected provider.

**Slow indexing on large PDFs**  
Normal for documents with 500+ pages. Progress is shown. Once cached, subsequent loads are instant.

**Ollama not found**  
Install [Ollama](https://ollama.com) if using local models. Antty can auto-detect and start it.

## 📄 License

MIT License

---

**Made by WiseDev** | [GitHub](https://github.com/wisedev-pstach/Antty)
