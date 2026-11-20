import type { Alert } from '../types'
import Panel from './Panel'

export default function AlertsPanel({ items, unread, onRead }: { items: Alert[]; unread: number; onRead: (id: number, isRead: boolean) => void }) {
  return <Panel title="Alerts" description="Monitoring failure and recovery notifications." tag={`${unread} UNREAD`}>
    <div className="list">{items.map(a => <div className="listrow" key={a.id}><span className={a.isRead ? 'method' : 'bad'}>{a.isRead ? 'READ' : 'NEW'}</span><div><b>{a.title}</b><small>{a.message} · {new Date(a.createdAt).toLocaleString()}</small></div>{!a.isRead && <button className="secondary smallbutton" onClick={() => onRead(a.id, true)}>Mark read</button>}</div>)}{items.length === 0 && <p className="empty">No monitoring alerts yet.</p>}</div>
  </Panel>
}