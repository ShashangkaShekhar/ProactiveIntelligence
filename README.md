# Proactive Intelligence

Everything about the project in one file: overview, architecture, setup and tools used.

---

## 1. Overview

ProactiveIntelligence is a multi-service agentic AI system that:

- Accepts natural-language instructions over HTTP
- Uses an LLM (Ollama + qwen2.5:7b) to decide which tools to call
- Retrieves data from PostgreSQL through a read-only MCP server
- Analyzes data and produces structured JSON insights
- Returns both the analysis AND raw data (`tool_results`)

**Stack:** .NET 8 + PostgreSQL + MCP (HTTP) + Ollama (qwen2.5:7b)
**Method:** Spec-Driven Development (SDD) + Agentic AI

---

## 2. Architecture

```
     User 
      │  POST /api/agent/run
      ▼
┌──────────────────────────┐
│  001-core-agent  :5042   │   ← The Orchestrator
└────────┬─────────────────┘
         │
         ├── HTTP MCP ──▶  ┌──────────────────────────┐
         │                 │  002-mcp-server  :8080   │  ← The Hands
         │                 └────────┬─────────────────┘
         │                          │ Npgsql (read-only)
         │                          ▼
         │                 ┌──────────────────────────┐
         │                 │  PostgreSQL    DB :5432  │
         │                 │  1 table,   read-only    │
         │                 └──────────────────────────┘
         │
         └── HTTP ──▶  ┌──────────────────────────┐
                       │  Ollama  :11434          │  ← The Brain
                       │  qwen2.5:7b (CUDA)       │
                       └──────────────────────────┘
```

## 3. Output

<img width="1483" height="803" alt="image" src="https://github.com/user-attachments/assets/8848d0c8-fc6e-4ad0-a3ff-ab4acb8a99e8" />


