export type QueryParameter = { name: string; value: string; isSecret: boolean; isEnabled: boolean }

export default function QueryParameterEditor({ parameters: items, onChange }: { parameters: QueryParameter[]; onChange: (items: QueryParameter[]) => void }) {
  return <details className="queryparameters"><summary>Query parameters{items.length ? ` · ${items.filter(item => item.isEnabled).length} enabled` : ''}</summary>
    <div className="list">{items.map((item, index) => <div className="saveform queryparameter" key={index}>
      <input aria-label={`Query parameter ${index + 1} name`} placeholder="Parameter name" value={item.name} onChange={event => onChange(items.map((current, row) => row === index ? { ...current, name: event.target.value } : current))} />
      <input aria-label={`Query parameter ${index + 1} value`} type={item.isSecret ? 'password' : 'text'} autoComplete="off" placeholder="Value or {{VARIABLE}}" value={item.value} onChange={event => onChange(items.map((current, row) => row === index ? { ...current, value: event.target.value } : current))} />
      <label className="checklabel"><input aria-label={`Enable query parameter ${index + 1}`} type="checkbox" checked={item.isEnabled} onChange={event => onChange(items.map((current, row) => row === index ? { ...current, isEnabled: event.target.checked } : current))} />Enabled</label>
      <label className="checklabel"><input aria-label={`Secret query parameter ${index + 1}`} type="checkbox" checked={item.isSecret} onChange={event => onChange(items.map((current, row) => row === index ? { ...current, isSecret: event.target.checked } : current))} />Secret</label>
      <button type="button" className="icon" aria-label={`Remove query parameter ${index + 1}`} onClick={() => onChange(items.filter((_, row) => row !== index))}>×</button>
    </div>)}
      <button type="button" className="secondary smallbutton" onClick={() => onChange([...items, { name: '', value: '', isSecret: false, isEnabled: true }])}>Add query parameter</button>
    </div>
  </details>
}