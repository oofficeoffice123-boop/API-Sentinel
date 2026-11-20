import { useState } from 'react'
import type { Run, SavedApi } from '../types'
import Panel from './Panel'

type Suggestion = { expectedStatus: number | null; rationale: string; assertions: { jsonPath: string; operator: string; expected: unknown }[] }
type Analysis = { summary: string; likelyCause: string; hypotheses: string[]; debuggingSteps: string[]; recommendedTests: string[] }
type ExecutionSummary = { runsAnalyzed: number; summary: string; commonErrors: string[]; regressions: string[]; performanceObservations: string[]; suggestedActions: string[] }

export default function AiAssistant({ busy, apis, runs, onGenerate, onAnalyze, onRecommend, onSummary, onSaveSuggestion }: {
  busy: boolean; apis: SavedApi[]; runs: Run[]
  onGenerate: (prompt: string) => Promise<Suggestion | string>
  onAnalyze: (runId: number) => Promise<Analysis | string>
  onRecommend: (apiId: number) => Promise<unknown>
  onSummary: () => Promise<ExecutionSummary | string>
  onSaveSuggestion: (apiId: number, prompt: string, suggestion: Suggestion) => Promise<void>
}) {
  const [prompt, setPrompt] = useState(''); const [suggestion, setSuggestion] = useState<Suggestion | string>('')
  const [analysis, setAnalysis] = useState<Analysis | string>(''); const [recommendations, setRecommendations] = useState<unknown>(); const [summary, setSummary] = useState<ExecutionSummary | string>('')
  const [saveApiId, setSaveApiId] = useState(''); const [saved, setSaved] = useState(false)
  const failedRuns = runs.filter(run => !run.passed)
  async function generate() { setSaved(false); setSuggestion(await onGenerate(prompt)) }
  async function save() { if (typeof suggestion !== 'string' && saveApiId) { await onSaveSuggestion(Number(saveApiId), prompt, suggestion); setSaved(true) } }
  return <Panel className="ai" title="AI test assistant" description="Generate checks, inspect failures, and find useful test cases." tag="GROQ">
    <label>Test idea<textarea rows={3} placeholder="e.g. Check login returns 200 and includes a token" value={prompt} onChange={e => setPrompt(e.target.value)} /></label>
    <button className="secondary" disabled={busy || !prompt.trim()} onClick={generate}>Generate test suggestion</button>
    {suggestion && <pre className="suggestion">{typeof suggestion === 'string' ? suggestion : JSON.stringify(suggestion, null, 2)}</pre>}
    {typeof suggestion !== 'string' && suggestion && <div className="saveform"><select aria-label="API to save generated test for" value={saveApiId} onChange={e => setSaveApiId(e.target.value)}><option value="">Choose API for this test</option>{apis.map(api => <option key={api.id} value={api.id}>{api.name}</option>)}</select><button className="secondary" disabled={busy || !saveApiId} onClick={save}>Save as test case</button>{saved && <span className="ok">Saved</span>}</div>}
    <div className="ai-actions">
      <label>Failed execution<select defaultValue="" onChange={async e => { setAnalysis(e.target.value ? await onAnalyze(Number(e.target.value)) : '') }}><option value="">Choose failed run</option>{failedRuns.map(run => <option key={run.id} value={run.id}>#{run.id} · {run.method} {run.url} · {run.status}</option>)}</select></label>
      <label>Saved API<select defaultValue="" onChange={async e => { setRecommendations(e.target.value ? await onRecommend(Number(e.target.value)) : undefined) }}><option value="">Choose API</option>{apis.map(api => <option key={api.id} value={api.id}>{api.method} {api.name}</option>)}</select></label>
    </div>
    {analysis && <pre className="suggestion">{typeof analysis === 'string' ? analysis : JSON.stringify(analysis, null, 2)}</pre>}
    {recommendations !== undefined && recommendations !== null && <pre className="suggestion">{JSON.stringify(recommendations, null, 2)}</pre>}
    <div className="saveform"><button className="secondary" disabled={busy || runs.length === 0} onClick={async () => setSummary(await onSummary())}>Summarize recent executions</button><small>Analyzes up to 100 recent status and timing records.</small></div>
    {summary && <pre className="suggestion">{typeof summary === 'string' ? summary : JSON.stringify(summary, null, 2)}</pre>}
    <p className="hint">AI output is a draft. Review suggestions before saving or running them.</p>
  </Panel>
}