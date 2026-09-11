import { createLowlight } from 'lowlight'
import bash from 'highlight.js/lib/languages/bash'
import css from 'highlight.js/lib/languages/css'
import csharp from 'highlight.js/lib/languages/csharp'
import dockerfile from 'highlight.js/lib/languages/dockerfile'
import go from 'highlight.js/lib/languages/go'
import html from 'highlight.js/lib/languages/xml'
import java from 'highlight.js/lib/languages/java'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import markdown from 'highlight.js/lib/languages/markdown'
import plaintext from 'highlight.js/lib/languages/plaintext'
import python from 'highlight.js/lib/languages/python'
import rust from 'highlight.js/lib/languages/rust'
import sql from 'highlight.js/lib/languages/sql'
import typescript from 'highlight.js/lib/languages/typescript'
import yaml from 'highlight.js/lib/languages/yaml'

// A curated dev-docs language set, not highlight.js's full ~190-language
// bundle, to keep the shipped bundle size sane.
export const lowlight = createLowlight()
lowlight.register({
  javascript, typescript, python, csharp, bash, json, yaml, sql,
  html, css, go, rust, java, dockerfile, markdown, plaintext,
})

/** Language picker options: TipTap's stored `language` attr value → display label. */
export const codeLanguages: { value: string; label: string }[] = [
  { value: 'plaintext', label: 'Plain text' },
  { value: 'javascript', label: 'JavaScript' },
  { value: 'typescript', label: 'TypeScript' },
  { value: 'python', label: 'Python' },
  { value: 'csharp', label: 'C#' },
  { value: 'bash', label: 'Bash / Shell' },
  { value: 'json', label: 'JSON' },
  { value: 'yaml', label: 'YAML' },
  { value: 'sql', label: 'SQL' },
  { value: 'html', label: 'HTML' },
  { value: 'css', label: 'CSS' },
  { value: 'go', label: 'Go' },
  { value: 'rust', label: 'Rust' },
  { value: 'java', label: 'Java' },
  { value: 'dockerfile', label: 'Dockerfile' },
  { value: 'markdown', label: 'Markdown' },
  // Not a highlighting language: choosing it turns the code block into a
  // rendered diagram (CodeBlockView / MermaidView, dev-plan Phase 7 Wave F).
  // Kept in this list because "what is in this code block" is one decision,
  // and Confluence's diagram macro is likewise just a fenced block.
  { value: 'mermaid', label: 'Mermaid diagram' },
]

/** The one language that renders rather than highlights. */
export const MERMAID_LANGUAGE = 'mermaid'
