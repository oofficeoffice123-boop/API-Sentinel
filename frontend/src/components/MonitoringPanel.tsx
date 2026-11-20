import { useState } from 'react'
import type { Monitor, TestCase } from '../types'
import Panel from './Panel'

export default function MonitoringPanel({ tests, items, busy, onCreate, onToggle, onDelete, onHistory, onRunNow }: {
  tests: TestCase[]; items: Monitor[]; busy: boolean; onCreate: (testCaseId: number, frequencyMinutes: number) => Promise<void>;
  onToggle: (monitor: Monitor) => void; onDelete: (id: number) => void; onHistory: (id: number) => void; onRunNow: (id: number) => void
}) {
  const [testId, setTestId] = useState(''); const [frequency, setFrequency] = useState(15)
  async function create() { if (testId) await onCreate(Number(testId), frequency) }
  return <Panel title="Monitoring" description="Schedule saved test cases and review their latest check." tag={`${items.filter(m => m.enabled).length} ACTIVE`}>
    <div className="saveform"><select aria-label="Test case to monitor" value={testId} onChange={e => setTestId(e.target.value)}><option value="">Select test case</option>{tests.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select><select aria-label="Monitoring frequency" value={frequency} onChange={e => setFrequency(Number(e.target.value))}>{[[5,'Every 5 minutes'],[15,'Every 15 minutes'],[30,'Every 30 minutes'],[60,'Hourly'],[1440,'Daily']].map(([v,label]) => <option value={v} key={v}>{label}</option>)}</select><button className="secondary" disabled={busy || !testId} onClick={create}>Schedule</button></div>
    <div className="list">{items.map(m => <div className="listrow" key={m.id}><span className="method">{m.lastCheck ? m.lastCheck.passed ? 'UP' : 'FAIL' : 'NEW'}</span><div><b>{m.testName}</b><small>{m.frequencyMinutes} min · {m.lastCheck ? `${m.lastCheck.status} · ${m.lastCheck.durationMs} ms` : 'Waiting for first scheduled check'}</small></div><button className="secondary smallbutton" onClick={() => onHistory(m.id)}>History</button><button className="secondary smallbutton" disabled={busy} onClick={() => onRunNow(m.id)}>Check now</button><button className="secondary smallbutton" disabled={busy} onClick={() => onToggle(m)}>{m.enabled ? 'Pause' : 'Resume'}</button><button className="icon" title="Delete monitor" onClick={() => onDelete(m.id)}>×</button></div>)}{items.length === 0 && <p className="empty">Schedule a saved test to start API monitoring.</p>}</div>
  </Panel>
}