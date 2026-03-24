# AI Regulation & Policy Interpreter

A distributed microservices system built on **Azure Service Fabric** that enables intelligent interpretation of laws, regulations, and internal policies using Large Language Models.

## Overview

Users upload legal documents as plain text, ask questions in natural language, and receive:
- Clear interpretation of the regulation
- Reasoning behind the answer
- References to specific document sections (Article/Paragraph with IDs)
- Confidence score (0–100%)

> The system does **not** make legal decisions — it only assists in understanding regulatory text.

---

## Architecture

```
[Browser / Frontend]
        |
        | HTTP (REST) — port 9066
        v
[QueryService]          ← Stateless Web (ASP.NET Core + Kestrel)
        |
        |--- SF Remoting ---> [DocumentService]   ← Stateful (Reliable Dictionary + Azure Table)
        |--- SF Remoting ---> [LLMService]         ← Stateless (OpenRouter API)
```

### Microservices

| Service | Type | Responsibility |
|---|---|---|
| **QueryService** | Stateless Web | REST API, HTML frontend, orchestration |
| **DocumentService** | Stateful | Document storage, parsing, section search |
| **LLMService** | Stateless | LLM API calls, answer generation |

---

## Tech Stack

- **Platform:** Azure Service Fabric (local 5-node cluster)
- **Language:** C# / .NET 8
- **LLM:** OpenRouter API (`stepfun/step-3.5-flash:free`)
- **Storage:** Azurite emulator (Azure Table Storage)
- **Communication:** Service Fabric Remoting (inter-service), Kestrel (web)
- **Frontend:** Vanilla HTML/CSS/JavaScript

---

## Project Structure

```
AIRegulationInterpreter.sln
├── AIRegulationInterpreter/        ← SF Application project
│   ├── ApplicationPackageRoot/
│   │   └── ApplicationManifest.xml
│   └── ApplicationParameters/
│       ├── Local.1Node.xml         ← Dev parameters (API key, not in repo)
│       └── Local.5Node.xml         ← 5-node parameters (API key, not in repo)
├── Common/                         ← Shared interfaces and models
│   ├── Contracts/
│   │   ├── IDocumentService.cs
│   │   └── ILLMService.cs
│   └── Models/
│       ├── DocumentModels.cs
│       └── QueryModels.cs
├── DocumentService/                ← Stateful microservice
│   └── DocumentService.cs
├── LLMService/                     ← Stateless microservice
│   └── LLMService.cs
└── QueryService/                   ← Stateless Web microservice
    ├── Controllers/
    │   └── DocumentController.cs
    └── wwwroot/
        └── index.html
```

---

## Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) with Azure development workload
- [Azure Service Fabric SDK](https://learn.microsoft.com/en-us/azure/service-fabric/service-fabric-get-started)
- [Node.js / Azurite](https://github.com/Azure/Azurite) — local Azure Storage emulator
- [OpenRouter account](https://openrouter.ai/) — free API key

---

## Getting Started

### 1. Clone the repository
```bash
git clone https://github.com/Soviljko/AIRegulationInterpreter.git
cd AIRegulationInterpreter
```

### 2. Configure API key
Create `AIRegulationInterpreter/ApplicationParameters/Local.1Node.xml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Application xmlns:xsd="http://www.w3.org/2001/XMLSchema"
             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             Name="fabric:/AIRegulationInterpreter"
             xmlns="http://schemas.microsoft.com/2011/01/fabric">
  <Parameters>
    <Parameter Name="DocumentService_MinReplicaSetSize" Value="1" />
    <Parameter Name="DocumentService_PartitionCount" Value="1" />
    <Parameter Name="DocumentService_TargetReplicaSetSize" Value="1" />
    <Parameter Name="LLMService_InstanceCount" Value="1" />
    <Parameter Name="LLMService_OpenAI_ApiKey" Value="YOUR_OPENROUTER_API_KEY" />
    <Parameter Name="QueryService_InstanceCount" Value="1" />
  </Parameters>
</Application>
```
Replace `YOUR_OPENROUTER_API_KEY` with your key from [openrouter.ai](https://openrouter.ai/).

### 3. Start Azurite
```bash
azurite --silent --location c:\azurite --debug c:\azurite\debug.log
```

### 4. Run the application
Open `AIRegulationInterpreter.sln` in Visual Studio and press **F5**.

### 5. Open the frontend
Navigate to [http://localhost:9066](http://localhost:9066)

---

## Usage

### Upload a document
1. Go to the **Upload Document** tab
2. Click **Load from .txt file** and select a plain text law/regulation file
3. Fill in the document title, type, and valid-from date
4. Click **Upload Document**

Document format — use `Član` and `Stav` keywords for automatic parsing:
```
Član 1
This article defines...

Član 5
Stav 1
First paragraph content...
Stav 2
Second paragraph content...
```

### Ask a question
1. Go to the **Ask a Question** tab
2. Type your question in natural language
3. Click **Submit Question**
4. View the answer, confidence score, and document references

---

## Confidence Score

| Range | Meaning |
|---|---|
| 90–100% | Direct and complete answer found in document |
| 70–89% | Main answer present, some details missing |
| 40–69% | Partial answer, some inference required |
| 10–39% | Loosely related, does not clearly answer |
| 0–9% | No relevant information found |

---

## REST API

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/documents` | Upload a document |
| `GET` | `/api/documents` | List all documents |
| `GET` | `/api/documents/{id}/history` | Get version history |
| `POST` | `/api/query` | Ask a question |

### Example — Upload document
```json
POST /api/documents
{
  "title": "Labor Law",
  "content": "Član 1\nThis law regulates...",
  "documentType": "Zakon",
  "validFrom": "2024-01-01"
}
```

### Example — Ask a question
```json
POST /api/query
{
  "question": "What is the minimum wage?",
  "contextDate": "2024-06-01"
}
```

### Example — Response
```json
{
  "explanation": "The minimum wage is 700 dinars net per month.",
  "citations": [
    {
      "sectionId": "DOC20260308-CL25-ST1",
      "sectionTitle": "Član 25, Stav 1",
      "relevantText": "Minimalna plata iznosi 700 dinara neto mesečno."
    }
  ],
  "confidence": 95,
  "hasSufficientInfo": true
}
```

---

## Storage

The system uses two storage layers:

| Layer | Technology | Purpose |
|---|---|---|
| Primary | Reliable Dictionary (SF) | Fast in-memory read/write, replicated across nodes |
| Backup | Azure Table Storage (Azurite) | Persistent backup, version history |

---

## Security Note

The `ApplicationParameters/Local.1Node.xml` and `Local.5Node.xml` files contain the API key and are excluded from this repository via `.gitignore`. Never commit API keys to source control.
