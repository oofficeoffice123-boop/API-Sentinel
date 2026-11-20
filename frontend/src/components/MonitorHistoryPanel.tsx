import type { MonitorHistory } from '../types'
import Panel from './Panel'

export default function MonitorHistoryPanel({ items }: { items: MonitorHistory[] }) {
  return <Panel title="Monitoring history" description="Persisted scheduled and manual check results."><div className="table">{items.map((x, i) => <div className="row" key={`${x.createdAt}-${i}`}><span><b>{x.monitorName}</b><small>{x.url}</small></span><span>{x.status || 'Error'}</span><span>{x.durationMs} ms</span><span className={x.passed ? 'ok' : 'bad'}>{x.passed ? 'Passed' : x.error || 'Failed'}</span></div>)}{items.length === 0 && <p className="empty">No monitoring history yet.</p>}</div></Panel>
}