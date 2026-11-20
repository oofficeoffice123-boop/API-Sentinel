import type { FormEvent } from 'react'
import type { QueryParameter, Run } from '../types'
import QueryParameterEditor from './QueryParameterEditor'

export default function RequestBuilder({ url, setUrl, method, setMethod, body, setBody, contentType, setContentType, headers, setHeaders, queryParameters, setQueryParameters, expected, setExpected, timeoutSeconds, setTimeoutSeconds, authType, setAuthType, credential, setCredential, authHeaderName, setAuthHeaderName, onRun, result, busy }: {
  url: string; setUrl: (v: string) => void; method: string; setMethod: (v: string) => void; body: string; setBody: (v: string) => void; headers: string; setHeaders: (v: string) => void;
  contentType: string; setContentType: (v: string) => void;
  queryParameters: QueryParameter[]; setQueryParameters: (v: QueryParameter[]) => void;
  expected: string; setExpected: (v: string) => void; timeoutSeconds: number; setTimeoutSeconds: (v: number) => void; authType: string; setAuthType: (v: string) => void; credential: string; setCredential: (v: string) => void; authHeaderName: string; setAuthHeaderName: (v: string) => void;
  onRun: (e: FormEvent) => void; result: Run | null; busy: boolean
}) {
  return <section className="panel runner"><div className="panelhead"><div><h2>Quick request</h2><p>Send an HTTP request and check its response.</p></div><span className="tag">HTTP CLIENT</span></div>
    <form onSubmit={onRun}><div className="requestline"><select value={method} onChange={e => setMethod(e.target.value)}>{['GET','POST','PUT','PATCH','DELETE'].map(x => <option key={x}>{x}</option>)}</select><input aria-label="Request URL" type="text" required placeholder="https://api.example.com or {{API_BASE}}/path" value={url} onChange={e => setUrl(e.target.value)} /><button className="primary" disabled={busy}>{busy ? 'Sending…' : 'Send request'}</button></div>
      <div className="formgrid"><label>Expected status<input type="number" value={expected} onChange={e => setExpected(e.target.value)} /></label><label>Timeout seconds<input aria-label="Request timeout seconds" type="number" min="1" max="120" value={timeoutSeconds} onChange={e => setTimeoutSeconds(Number(e.target.value))} /></label><label>Request body<textarea rows={3} placeholder="Optional JSON request body" value={body} onChange={e => setBody(e.target.value)} /></label></div>
      <label className="contenttype">Content type<select value={contentType} onChange={e => setContentType(e.target.value)}><option>application/json</option><option>text/plain</option><option>application/x-www-form-urlencoded</option></select></label>
      <QueryParameterEditor parameters={queryParameters} onChange={setQueryParameters} />
      <label className="headersinput">Headers (JSON)<textarea rows={3} placeholder={'{\n  "Accept": "application/json"\n}'} value={headers} onChange={e => setHeaders(e.target.value)} /></label>
      <div className="authfields"><label>Authentication<select value={authType} onChange={e => setAuthType(e.target.value)}>{['None','Bearer','Basic','ApiKey'].map(x => <option key={x} value={x}>{x === 'ApiKey' ? 'API key' : x}</option>)}</select></label>{authType === 'ApiKey' && <label>Header name<input placeholder="X-API-Key" value={authHeaderName} onChange={e => setAuthHeaderName(e.target.value)} /></label>}{authType !== 'None' && <label>{authType === 'Basic' ? 'Username:password' : 'Credential'}<input type="password" autoComplete="off" value={credential} onChange={e => setCredential(e.target.value)} /></label>}</div>
    </form>
    {result && <div className="response"><div className="responsehead"><b>Response · {new Blob([result.body]).size} bytes</b><span className={result.passed ? 'ok' : 'bad'}>{result.status} · {result.durationMs} ms · {result.passed ? 'PASS' : 'FAIL'}</span></div><pre>{pretty(result.body)}</pre>{result.headers && <details><summary>Response headers</summary><pre>{JSON.stringify(result.headers, null, 2)}</pre></details>}</div>}
  </section>
}
function pretty(body: string) { try { return JSON.stringify(JSON.parse(body), null, 2) } catch { return body || '(empty response)' } }