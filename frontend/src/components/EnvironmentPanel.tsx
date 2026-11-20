import { useState } from 'react'
import type { ApiEnvironment } from '../types'
import Panel from './Panel'

export default function EnvironmentPanel({ items, busy, onCreate, onActivate, onDelete, onSaveVariable, onDeleteVariable }: {
  items: ApiEnvironment[]; busy: boolean
  onCreate: (name: string, active: boolean) => Promise<void>
  onActivate: (environment: ApiEnvironment) => Promise<void>
  onDelete: (id: number) => Promise<void>
  onSaveVariable: (id: number, key: string, value: string, isSecret: boolean) => Promise<void>
  onDeleteVariable: (id: number, variableId: number) => Promise<void>
}) {
  const [name, setName] = useState(''); const [active, setActive] = useState(true)
  return <Panel title="Environments" description="Switch base URLs and variables for requests, collections, and monitors.">
    <form className="saveform" onSubmit={e => { e.preventDefault(); void onCreate(name, active).then(() => setName('')) }}><input aria-label="Environment name" placeholder="Environment name" value={name} onChange={e => setName(e.target.value)} /><label className="checklabel"><input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} /> Activate now</label><button className="secondary" disabled={busy || !name.trim()}>Create environment</button></form>
    <div className="environmentlist">{items.map(item => <EnvironmentCard key={item.id} item={item} busy={busy} onActivate={onActivate} onDelete={onDelete} onSaveVariable={onSaveVariable} onDeleteVariable={onDeleteVariable} />)}{items.length === 0 && <p className="empty">No environments yet. Add variables like API_BASE and use {'{{API_BASE}}'} in a request URL.</p>}</div>
  </Panel>
}

function EnvironmentCard({ item, busy, onActivate, onDelete, onSaveVariable, onDeleteVariable }: {
  item: ApiEnvironment; busy: boolean; onActivate: (environment: ApiEnvironment) => Promise<void>; onDelete: (id: number) => Promise<void>
  onSaveVariable: (id: number, key: string, value: string, isSecret: boolean) => Promise<void>; onDeleteVariable: (id: number, variableId: number) => Promise<void>
}) {
  const [key, setKey] = useState(''); const [value, setValue] = useState(''); const [secret, setSecret] = useState(false)
  return <article className="environmentcard"><header><b>{item.name}</b>{item.isActive ? <span className="ok">Active</span> : <button className="smallbutton secondary" disabled={busy} onClick={() => void onActivate(item)}>Activate</button>}<button className="smallbutton danger" disabled={busy} onClick={() => void onDelete(item.id)}>Delete</button></header>
    <div className="environmentvars">{item.variables.map(variable => <EnvironmentVariableRow key={variable.id} environmentId={item.id} variable={variable} busy={busy} onSave={onSaveVariable} onDelete={onDeleteVariable} />)}</div>
    <div className="environmentvar addvar"><input aria-label="New variable key" placeholder="VARIABLE_NAME" value={key} onChange={e => setKey(e.target.value)} /><input aria-label="New variable value" type={secret ? 'password' : 'text'} placeholder="Variable value" value={value} onChange={e => setValue(e.target.value)} /><label className="checklabel"><input type="checkbox" checked={secret} onChange={e => setSecret(e.target.checked)} /> Secret</label><button className="smallbutton secondary" disabled={busy || !key.trim() || !value} onClick={() => void onSaveVariable(item.id, key, value, secret).then(() => { setKey(''); setValue('') })}>Add variable</button></div>
  </article>
}

function EnvironmentVariableRow({ environmentId, variable, busy, onSave, onDelete }: {
  environmentId: number; variable: ApiEnvironment['variables'][number]; busy: boolean
  onSave: (id: number, key: string, value: string, isSecret: boolean) => Promise<void>
  onDelete: (id: number, variableId: number) => Promise<void>
}) {
  const [value, setValue] = useState(variable.value ?? '')
  return <div className="environmentvar"><span>{variable.key}{variable.isSecret ? ' · secret' : ''}</span><input aria-label={`Value for ${variable.key}`} type={variable.isSecret ? 'password' : 'text'} placeholder={variable.isSecret ? 'Enter replacement secret' : 'Value'} value={value} onChange={e => setValue(e.target.value)} /><button className="smallbutton secondary" disabled={busy || !value} onClick={() => void onSave(environmentId, variable.key, value, variable.isSecret).then(() => setValue(''))}>Update</button><button className="smallbutton danger" disabled={busy} onClick={() => void onDelete(environmentId, variable.id)}>Remove</button></div>
}