import { useState } from 'react'
import type { SavedApi, TestCase } from '../types'
import Panel from './Panel'

export default function TestCasePanel({ apis, tests, busy, onCreate, onRun, onDelete }: {
  apis: SavedApi[]; tests: TestCase[]; busy: boolean; onCreate: (test: { apiId: number; name: string; expectedStatus: number | null; maxDurationMs: number | null; schemaJson: string | null; assertions: { jsonPath: string; operator: string; expected?: unknown }[] }) => Promise<void>;
  onRun: (id: number) => void; onDelete: (id: number) => void
}) {
  const [apiId, setApiId] = useState(''); const [name, setName] = useState(''); const [status, setStatus] = useState('200'); const [maxDuration, setMaxDuration] = useState(''); const [path, setPath] = useState(''); const [source, setSource] = useState<'body' | 'header'>('body'); const [operator, setOperator] = useState('Exists'); const [expected, setExpected] = useState(''); const [schema, setSchema] = useState('')
  async function create() {
    if (!apiId || !name.trim()) return
    let value: unknown
    if (expected) { try { value = JSON.parse(expected) } catch { value = expected } }
    const assertionPath = source === 'header' ? `header:${path.trim()}` : path.trim()
    const assertions = path.trim() ? [{ jsonPath: assertionPath, operator, ...(['Exists','DoesNotExist','IsNull','IsNotNull'].includes(operator) ? {} : { expected: value }) }] : []
    await onCreate({ apiId: Number(apiId), name: name.trim(), expectedStatus: status ? Number(status) : null, maxDurationMs: maxDuration ? Number(maxDuration) : null, schemaJson: schema.trim() || null, assertions })
    setName(''); setSchema('')
  }
  return <Panel title="Test cases" description="Save status and JSON path checks against an API." tag={`${tests.length} TESTS`}>
    <div className="testform"><select aria-label="API for test" value={apiId} onChange={e => setApiId(e.target.value)}><option value="">Choose saved API</option>{apis.map(a => <option value={a.id} key={a.id}>{a.name}</option>)}</select><input placeholder="Test name" value={name} onChange={e => setName(e.target.value)} />
      <div className="testassert"><select aria-label="Assertion source" value={source} onChange={e => setSource(e.target.value as 'body' | 'header')}><option value="body">Response body JSON</option><option value="header">Response header</option></select><input aria-label={source === 'header' ? 'Response header name' : 'JSON path'} placeholder={source === 'header' ? 'Header name, e.g. Content-Type' : 'JSON path, e.g. $.id'} value={path} onChange={e => setPath(e.target.value)} /><select aria-label="Assertion operator" value={operator} onChange={e => setOperator(e.target.value)}>{['Exists','DoesNotExist','IsNull','IsNotNull','Equals','NotEquals','Contains','DoesNotContain','GreaterThan','LessThan'].map(x => <option key={x}>{x}</option>)}</select>{!['Exists','DoesNotExist','IsNull','IsNotNull'].includes(operator) && <input aria-label="Expected value" placeholder="JSON value, e.g. 7" value={expected} onChange={e => setExpected(e.target.value)} />}</div>
      <details><summary>Response JSON schema (optional)</summary><textarea aria-label="Response JSON schema" rows={4} placeholder={'{"type":"object","required":["id"],"properties":{"id":{"type":"integer"}}}'} value={schema} onChange={e => setSchema(e.target.value)} /></details>
      <div className="testcreate"><label>Expected status<input type="number" value={status} onChange={e => setStatus(e.target.value)} /></label><label>Max duration ms<input type="number" min="1" value={maxDuration} onChange={e => setMaxDuration(e.target.value)} /></label><button className="secondary" disabled={busy || !apiId || !name.trim()} onClick={create}>Save test case</button></div>
    </div>
    <div className="list">{tests.map(t => <div className="listrow" key={t.id}><span className="method">{t.expectedStatus ?? 'ANY'}</span><div><b>{t.name}</b><small>{apis.find(a => a.id === t.apiId)?.name ?? 'API'} · {t.assertions.length} assertions</small></div><button className="secondary smallbutton" disabled={busy} onClick={() => onRun(t.id)}>Run</button><button className="icon" title={`Delete ${t.name}`} onClick={() => onDelete(t.id)}>×</button></div>)}{tests.length === 0 && <p className="empty">Create a test case to save repeatable assertions.</p>}</div>
  </Panel>
}