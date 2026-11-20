import { useState } from 'react'
import type { ApiCollection } from '../types'
import Panel from './Panel'

export default function CollectionPanel({ items, busy, onCreate, onRename, onRun, onDelete }: {
  items: ApiCollection[]; busy: boolean; onCreate: (name: string) => Promise<void>; onRename: (id: number, name: string) => Promise<void>; onRun: (id: number) => void; onDelete: (id: number) => void
}) {
  const [name, setName] = useState('')
  async function create() { if (!name.trim()) return; await onCreate(name.trim()); setName('') }
  return <Panel title="Collections" description="Group saved API requests and run them in order." tag={`${items.length} COLLECTIONS`}>
    <div className="saveform"><input aria-label="Collection name" placeholder="New collection name" value={name} onChange={e => setName(e.target.value)} /><button className="secondary" disabled={busy || !name.trim()} onClick={create}>Create</button></div>
    <div className="list">{items.map(item => <CollectionRow key={item.id} item={item} busy={busy} onRename={onRename} onRun={onRun} onDelete={onDelete} />)}{items.length === 0 && <p className="empty">Create a collection, then assign saved APIs to it.</p>}</div>
  </Panel>
}

function CollectionRow({ item, busy, onRename, onRun, onDelete }: {
  item: ApiCollection; busy: boolean; onRename: (id: number, name: string) => Promise<void>; onRun: (id: number) => void; onDelete: (id: number) => void
}) {
  const [name, setName] = useState(item.name)
  return <div className="listrow"><span className="method">{item.requestCount} REQ</span><div><b>{item.name}</b><small>Requests run sequentially by sort order.</small><details><summary>Rename</summary><div className="saveform"><input aria-label={`New name for ${item.name}`} value={name} onChange={e => setName(e.target.value)} /><button className="secondary smallbutton" disabled={busy || !name.trim() || name === item.name} onClick={() => void onRename(item.id, name.trim())}>Save</button></div></details></div><button className="secondary smallbutton" disabled={busy || item.requestCount === 0} onClick={() => onRun(item.id)}>Run all</button><button className="icon" title={`Delete ${item.name}`} onClick={() => onDelete(item.id)}>×</button></div>
}