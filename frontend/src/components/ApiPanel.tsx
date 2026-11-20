import { useState } from 'react'
import type { ApiCollection, SavedApi } from '../types'
import Panel from './Panel'

type Props = {
  items: SavedApi[]; collections: ApiCollection[]; name: string; setName: (v: string) => void
  collectionId: string; setCollectionId: (v: string) => void; editing: boolean
  search: string; onSearch: (v: string) => void; page: number; total: number; onPage: (v: number) => void
  onSave: () => void; onEdit: (api: SavedApi | null) => void; onDelete: (id: number) => void
  onExtraction: (api: SavedApi, path: string, variable: string) => Promise<void>
}

export default function ApiPanel({ items, collections, name, setName, collectionId, setCollectionId, editing, search, onSearch, page, total, onPage, onSave, onEdit, onDelete, onExtraction }: Props) {
  return <Panel title="Saved APIs" description="Keep frequently used endpoints close.">
    <div className="saveform"><input aria-label="API name" placeholder="API name" value={name} onChange={e => setName(e.target.value)} /><select aria-label="Collection" value={collectionId} onChange={e => setCollectionId(e.target.value)}><option value="">No collection</option>{collections.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select><button className="secondary" disabled={!name.trim()} onClick={onSave}>{editing ? 'Update request' : 'Save request'}</button>{editing && <button className="icon" title="Cancel editing" onClick={() => onEdit(null)}>×</button>}</div>
    <div className="saveform"><input aria-label="Search APIs" placeholder="Search by name or URL" value={search} onChange={e => onSearch(e.target.value)} /></div>
    <div className="list">{items.map(api => <div className="listrow" key={api.id}><span className="method">{api.method}</span><div><b>{api.name}</b><small>{api.url} · {api.authenticationType === 'None' ? 'No auth' : `${api.authenticationType} credential ${api.hasCredential ? 'saved' : 'missing'}`}</small><ExtractionEditor api={api} onSave={onExtraction} /></div><button className="smallbutton secondary" onClick={() => onEdit(api)}>Edit</button><button className="icon" title={`Delete ${api.name}`} onClick={() => onDelete(api.id)}>×</button></div>)}{items.length === 0 && <p className="empty">{search ? 'No APIs match this search.' : 'Save a request to start a reusable test.'}</p>}</div>
    {total > 20 && <div className="saveform" aria-label="API pagination"><button className="secondary smallbutton" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button><span>Page {page} of {Math.ceil(total / 20)} · {total} APIs</span><button className="secondary smallbutton" disabled={page >= Math.ceil(total / 20)} onClick={() => onPage(page + 1)}>Next</button></div>}
  </Panel>
}

function ExtractionEditor({ api, onSave }: { api: SavedApi; onSave: (api: SavedApi, path: string, variable: string) => Promise<void> }) {
  const [path, setPath] = useState(api.extractionJsonPath ?? '')
  const [variable, setVariable] = useState(api.extractionVariableName ?? '')
  return <details className="extraction"><summary>{api.extractionJsonPath ? `Extract ${api.extractionVariableName}` : 'Collection response extraction'}</summary><div className="saveform"><input aria-label="Extraction JSON path" placeholder="$.token" value={path} onChange={e => setPath(e.target.value)} /><input aria-label="Runtime variable name" placeholder="AUTH_TOKEN" value={variable} onChange={e => setVariable(e.target.value)} /><button className="secondary smallbutton" disabled={!path.trim() || !variable.trim()} onClick={() => void onSave(api, path.trim(), variable.trim())}>Save</button><button className="icon" title="Clear extraction" onClick={() => { setPath(''); setVariable(''); void onSave(api, '', '') }}>×</button></div></details>
}