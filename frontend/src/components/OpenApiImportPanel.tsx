import { useState } from 'react'
import type { ApiCollection } from '../types'
import Panel from './Panel'

export default function OpenApiImportPanel({ collections, busy, onImport }: { collections: ApiCollection[]; busy: boolean; onImport: (specification: string, collectionId: number | null) => Promise<number> }) {
  const [specification, setSpecification] = useState('')
  const [collectionId, setCollectionId] = useState('')
  const [message, setMessage] = useState('')
  async function submit() {
    setMessage('')
    try { const count = await onImport(specification, collectionId ? Number(collectionId) : null); setMessage(`Imported ${count} endpoints.`); setSpecification('') }
    catch { setMessage('Import failed. Check the JSON document and its server URL.') }
  }
  return <Panel title="Import OpenAPI" description="Create saved requests from an OpenAPI JSON document.">
    <textarea rows={5} aria-label="OpenAPI JSON" placeholder="Paste an OpenAPI JSON document" value={specification} onChange={e => setSpecification(e.target.value)} />
    <div className="saveform"><select aria-label="Collection for imported endpoints" value={collectionId} onChange={e => setCollectionId(e.target.value)}><option value="">No collection</option>{collections.map(collection => <option key={collection.id} value={collection.id}>{collection.name}</option>)}</select><button className="secondary" disabled={busy || !specification.trim()} onClick={submit}>Import endpoints</button></div>
    {message && <p className="hint" role="status">{message}</p>}
  </Panel>
}