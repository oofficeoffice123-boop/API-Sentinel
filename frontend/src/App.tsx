import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import axios from 'axios'
import './App.css'
import { api, renewAccessToken, setAccessToken } from './services/api'
import type { Alert, ApiCollection, ApiEnvironment, DashboardReport, Monitor, MonitorHistory, QueryParameter, Run, SavedApi, TestCase } from './types'
import AuthForm from './components/AuthForm'
import StatGrid from './components/StatGrid'
import RequestBuilder from './components/RequestBuilder'
import ApiPanel from './components/ApiPanel'
import TestCasePanel from './components/TestCasePanel'
import ExecutionHistory from './components/ExecutionHistory'
import AiAssistant from './components/AiAssistant'
import CollectionPanel from './components/CollectionPanel'
import MonitoringPanel from './components/MonitoringPanel'
import MonitorHistoryPanel from './components/MonitorHistoryPanel'
import AlertsPanel from './components/AlertsPanel'
import OpenApiImportPanel from './components/OpenApiImportPanel'
import EnvironmentPanel from './components/EnvironmentPanel'
import AccountPanel from './components/AccountPanel'
import AdminPanel from './components/AdminPanel'
import { isSensitiveHeaderName } from './utils/headerSecurity'

const ReportPanel = lazy(() => import('./components/ReportPanel'))
const dashboardDate = new Intl.DateTimeFormat(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }).format(new Date()).toUpperCase()

export default function App() {
  const navigate = useNavigate()
  const location = useLocation()
  const [token, setToken] = useState('')
  const [authReady, setAuthReady] = useState(false)
  const [email, setEmail] = useState('')
  const [isAdmin, setIsAdmin] = useState(false)
  const [url, setUrl] = useState('https://jsonplaceholder.typicode.com/todos/1')
  const [method, setMethod] = useState('GET')
  const [body, setBody] = useState('')
  const [contentType, setContentType] = useState('application/json')
  const [headersText, setHeadersText] = useState('')
  const [headersDirty, setHeadersDirty] = useState(false)
  const [queryParameters, setQueryParameters] = useState<QueryParameter[]>([])
  const [expected, setExpected] = useState('200')
  const [timeoutSeconds, setTimeoutSeconds] = useState(20)
  const [authType, setAuthType] = useState('None')
  const [credential, setCredential] = useState('')
  const [authHeaderName, setAuthHeaderName] = useState('X-API-Key')
  const [apis, setApis] = useState<SavedApi[]>([])
  const [apiSearch, setApiSearch] = useState('')
  const [apiPage, setApiPage] = useState(1)
  const [apiTotal, setApiTotal] = useState(0)
  const apiRequestSequence = useRef(0)
  const [collections, setCollections] = useState<ApiCollection[]>([])
  const [environments, setEnvironments] = useState<ApiEnvironment[]>([])
  const [monitors, setMonitors] = useState<Monitor[]>([])
  const [alerts, setAlerts] = useState<Alert[]>([])
  const [history, setHistory] = useState<MonitorHistory[]>([])
  const [unreadAlerts, setUnreadAlerts] = useState(0)
  const [report, setReport] = useState<DashboardReport | null>(null)
  const [tests, setTests] = useState<TestCase[]>([])
  const [runs, setRuns] = useState<Run[]>([])
  const [result, setResult] = useState<Run | null>(null)
  const [apiName, setApiName] = useState('')
  const [editingApiId, setEditingApiId] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const logout = useCallback(async () => {
    try { if (token) await api.post('/account/logout') } catch { /* Expired/revoked tokens are cleared locally as well. */ }
    setAccessToken(null); setToken(''); setEmail(''); setIsAdmin(false); setAuthReady(true); navigate('/login', { replace: true })
  }, [navigate, token])

  useEffect(() => {
    let active = true
    const expire = () => { setAccessToken(null); setToken(''); setEmail(''); setIsAdmin(false); setAuthReady(true) }
    window.addEventListener('sentinel:auth-expired', expire)
    void renewAccessToken().then(token => {
      if (!active) return
      setToken(token)
    }).catch(() => { if (active) setAccessToken(null) }).finally(() => { if (active) setAuthReady(true) })
    return () => { active = false; window.removeEventListener('sentinel:auth-expired', expire) }
  }, [])

  const refresh = useCallback(async () => {
    try {
      const requestSequence = ++apiRequestSequence.current
      const [a, r, t, c, env, m, al, metrics, profile] = await Promise.all([api.get('/apis', { params: { page: apiPage, search: apiSearch || undefined } }), api.get('/runs'), api.get('/test-cases'), api.get('/collections'), api.get('/environments'), api.get('/monitors'), api.get('/alerts'), api.get('/reports/dashboard'), api.get('/account/profile')])
      if (requestSequence === apiRequestSequence.current) { setApis(a.data.items); setApiTotal(a.data.total) }
      setRuns(r.data.items); setTests(t.data.items); setCollections(c.data); setEnvironments(env.data); setMonitors(m.data); setAlerts(al.data.items); setUnreadAlerts(al.data.unread); setReport(metrics.data); setEmail(profile.data.email); setIsAdmin((profile.data.roles ?? []).includes('Admin'))
    } catch (e) {
      if (axios.isAxiosError(e) && e.response?.status === 401) logout()
      else setError('Could not load workspace data.')
    }
  }, [logout, apiPage, apiSearch])

  async function searchApis(value: string) {
    setApiSearch(value); setApiPage(1)
    const requestSequence = ++apiRequestSequence.current
    try { const response = await api.get('/apis', { params: { page: 1, search: value || undefined } }); if (requestSequence === apiRequestSequence.current) { setApis(response.data.items); setApiTotal(response.data.total) } }
    catch { setError('Could not search saved APIs.') }
  }

  async function changeApiPage(page: number) {
    setApiPage(page)
    const requestSequence = ++apiRequestSequence.current
    try { const response = await api.get('/apis', { params: { page, search: apiSearch || undefined } }); if (requestSequence === apiRequestSequence.current) { setApis(response.data.items); setApiTotal(response.data.total) } }
    catch { setError('Could not load this API page.') }
  }

  useEffect(() => {
    if (!token) return
    const request = window.setTimeout(() => { void refresh() }, 0)
    return () => window.clearTimeout(request)
  }, [token, refresh])

  async function authenticate(mode: 'login' | 'register', userEmail: string, password: string) {
    setError('')
    try {
      const response = await api.post(`/auth/${mode}`, { email: userEmail, password })
      setAccessToken(response.data.token); setEmail(userEmail); setToken(response.data.token); setAuthReady(true)
    } catch { setError('Sign in failed. Check your email and password.') }
  }

  async function execute(e: React.FormEvent) {
    e.preventDefault(); setBusy(true); setError(''); setResult(null)
    let headers: Record<string, string>
    try {
      const parsed: unknown = JSON.parse(headersText || '{}')
      if (parsed === null || Array.isArray(parsed) || typeof parsed !== 'object' || Object.values(parsed).some(value => typeof value !== 'string')) throw new Error('invalid headers')
      headers = parsed as Record<string, string>
    } catch { setBusy(false); setError('Request headers must be a JSON object with string values.'); return }
    try { const response = await api.post('/execute', { url, method, body: body || null, contentType, headers, queryParameters, expectedStatus: expected ? Number(expected) : null, timeoutSeconds, authenticationType: authType, credential: credential || null, authHeaderName: authType === 'ApiKey' ? authHeaderName : null }); setResult(response.data); await refresh() }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'Request failed.' : 'Request failed.'); await refresh() }
    finally { setBusy(false) }
  }

  async function saveApi() {
    try {
      const requestHeaders = JSON.parse(headersText || '{}') as Record<string, unknown>
      if (requestHeaders === null || Array.isArray(requestHeaders) || typeof requestHeaders !== 'object' || Object.values(requestHeaders).some(value => typeof value !== 'string')) throw new Error('Invalid headers')
      const headers = Object.entries(requestHeaders).map(([headerName, value]) => ({ name: headerName, value: value as string, isSecret: isSensitiveHeaderName(headerName) }))
      const payload = { name: apiName, url, method, requestBody: body || null, contentType, timeoutSeconds, headers: editingApiId !== null && !headersDirty ? undefined : headers, queryParameters, collectionId: collectionId ? Number(collectionId) : null, authenticationType: authType, credential: credential || null, authHeaderName: authType === 'ApiKey' ? authHeaderName : null }
      if (editingApiId === null) await api.post('/apis', payload)
      else await api.put(`/apis/${editingApiId}`, payload)
      setEditingApiId(null); setApiName(''); setCredential(''); setHeadersDirty(false); await refresh()
    }
    catch { setError('Could not save this API definition.') }
  }

  function editApi(savedApi: SavedApi) {
    setEditingApiId(savedApi.id); setApiName(savedApi.name); setUrl(savedApi.url); setMethod(savedApi.method); setBody(savedApi.requestBody ?? ''); setContentType(savedApi.contentType || 'application/json'); setTimeoutSeconds(savedApi.timeoutSeconds || 20); setHeadersText(JSON.stringify((savedApi.headers ?? []).filter(header => !header.isSecret).reduce<Record<string, string>>((values, header) => ({ ...values, [header.name]: header.value ?? '' }), {}), null, 2)); setHeadersDirty(false); setQueryParameters((savedApi.queryParameters ?? []).map(parameter => ({ name: parameter.name, value: parameter.isSecret ? '' : parameter.value ?? '', isSecret: parameter.isSecret, isEnabled: parameter.isEnabled }))); setCollectionId(savedApi.collectionId ? String(savedApi.collectionId) : ''); setAuthType(savedApi.authenticationType); setAuthHeaderName(savedApi.authHeaderName || 'X-API-Key'); setCredential('')
    document.getElementById('request-runner')?.scrollIntoView({ behavior: 'smooth' })
  }

  function cancelEdit() { setEditingApiId(null); setApiName(''); setCredential(''); setQueryParameters([]) }

  const [collectionId, setCollectionId] = useState('')

  async function createCollection(name: string) {
    try { await api.post('/collections', { name }); await refresh() } catch { setError('Could not create collection.') }
  }

  async function renameCollection(id: number, name: string) {
    try { await api.put(`/collections/${id}`, { name }); await refresh() } catch { setError('Could not rename collection.') }
  }

  async function createEnvironment(name: string, isActive: boolean) {
    try { await api.post('/environments', { name, isActive }); await refresh() } catch { setError('Could not create environment.') }
  }

  async function activateEnvironment(environment: ApiEnvironment) {
    try { await api.put(`/environments/${environment.id}`, { name: environment.name, isActive: true }); await refresh() } catch { setError('Could not activate environment.') }
  }

  async function deleteEnvironment(id: number) {
    try { await api.delete(`/environments/${id}`); await refresh() } catch { setError('Could not delete environment.') }
  }

  async function saveEnvironmentVariable(id: number, key: string, value: string, isSecret: boolean) {
    try { await api.put(`/environments/${id}/variables`, { key, value, isSecret }); await refresh() } catch { setError('Could not save environment variable.') }
  }

  async function deleteEnvironmentVariable(id: number, variableId: number) {
    try { await api.delete(`/environments/${id}/variables/${variableId}`); await refresh() } catch { setError('Could not remove environment variable.') }
  }

  async function deleteCollection(id: number) {
    try { await api.delete(`/collections/${id}`); if (collectionId === String(id)) setCollectionId(''); await refresh() } catch { setError('Could not delete collection.') }
  }

  async function runCollection(id: number) {
    setBusy(true); setError('')
    try { const response = await api.post(`/collections/${id}/run`); setError(`Collection finished: ${response.data.passed} of ${response.data.count} requests passed.`); await refresh() }
    catch { setError('Collection execution failed.') }
    finally { setBusy(false) }
  }

  async function createMonitor(testCaseId: number, frequencyMinutes: number) {
    setBusy(true)
    try { await api.post('/monitors', { testCaseId, frequencyMinutes, enabled: true }); await refresh() }
    catch { setError('Could not schedule monitoring for this test case.') }
    finally { setBusy(false) }
  }

  async function toggleMonitor(monitor: Monitor) {
    try { await api.put(`/monitors/${monitor.id}`, { testCaseId: monitor.testCaseId, frequencyMinutes: monitor.frequencyMinutes, enabled: !monitor.enabled }); await refresh() }
    catch { setError('Could not update monitoring schedule.') }
  }

  async function deleteMonitor(id: number) {
    try { await api.delete(`/monitors/${id}`); await refresh() } catch { setError('Could not delete monitor.') }
  }

  async function showMonitorHistory(id: number) {
    try { const response = await api.get(`/monitors/${id}/history`); setHistory(response.data.items) }
    catch { setError('Could not load monitoring history.') }
  }

  async function runMonitorNow(id: number) {
    setBusy(true)
    try { await api.post(`/monitors/${id}/run-now`); setError('Monitoring check queued. Refresh shortly to see its result.') }
    catch { setError('Could not queue monitoring check.') }
    finally { setBusy(false) }
  }

  async function markAlertRead(id: number, isRead: boolean) {
    try { await api.put(`/alerts/${id}/read`, { isRead }); await refresh() } catch { setError('Could not update alert.') }
  }

  async function changePassword(currentPassword: string, newPassword: string) {
    setBusy(true)
    try { await api.put('/account/password', { currentPassword, newPassword }); await logout(); return true }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'Could not change password.' : 'Could not change password.'); return false }
    finally { setBusy(false) }
  }

  async function changeEmail(nextEmail: string, currentPassword: string) {
    setBusy(true)
    try { await api.put('/account/profile', { email: nextEmail, currentPassword }); await logout(); return true }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'Could not update email.' : 'Could not update email.'); return false }
    finally { setBusy(false) }
  }

  async function changeReportRange(days: number) {
    try { const response = await api.get(`/reports/dashboard?days=${days}`); setReport(response.data) } catch { setError('Could not load report data.') }
  }

  async function deleteApi(id: number) {
    try { await api.delete(`/apis/${id}`); await refresh() } catch { setError('Could not delete this API.') }
  }

  async function saveApiExtraction(savedApi: SavedApi, path: string, variable: string) {
    try {
      await api.patch(`/apis/${savedApi.id}/extraction`, { jsonPath: path || null, variableName: variable || null })
      await refresh()
    } catch { setError('Could not save collection extraction settings.') }
  }

  async function importOpenApi(specification: string, collectionId: number | null) {
    setBusy(true)
    try { const response = await api.post('/apis/import-openapi', { specification, collectionId }); await refresh(); return response.data.imported as number }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'OpenAPI import failed.' : 'OpenAPI import failed.'); throw e }
    finally { setBusy(false) }
  }

  async function createTest(test: Parameters<React.ComponentProps<typeof TestCasePanel>['onCreate']>[0]) {
    setBusy(true)
    try { await api.post('/test-cases', test); await refresh() }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'Could not save test case.' : 'Could not save test case.') }
    finally { setBusy(false) }
  }

  async function runTest(id: number) {
    setBusy(true); setError(''); setResult(null)
    try { const response = await api.post('/execute', { url: '', method: 'GET', testCaseId: id }); setResult(response.data); await refresh() }
    catch (e) { setError(axios.isAxiosError(e) ? e.response?.data?.error || 'Test execution failed.' : 'Test execution failed.'); await refresh() }
    finally { setBusy(false) }
  }

  async function deleteTest(id: number) {
    try { await api.delete(`/test-cases/${id}`); await refresh() } catch { setError('Could not delete test case.') }
  }

  async function generate(prompt: string) {
    setBusy(true); setError('')
    try { const response = await api.post('/ai/generate', { description: prompt }); return response.data }
    catch { const message = 'AI is unavailable. Check the backend GROQ_API_KEY configuration.'; setError(message); return message }
    finally { setBusy(false) }
  }

  async function analyzeRun(runId: number) {
    try { const response = await api.post(`/ai/analyze/${runId}`); return response.data }
    catch { const message = 'Could not analyze this execution.'; setError(message); return message }
  }

  async function recommendTests(apiId: number) {
    try { const response = await api.post(`/ai/recommendations/${apiId}`); return response.data }
    catch { const message = 'Could not generate API test recommendations.'; setError(message); return message }
  }

  async function summarizeRuns() {
    try { const response = await api.post('/ai/summary'); return response.data }
    catch { const message = 'Could not summarize recent executions. Check the backend GROQ_API_KEY configuration.'; setError(message); return message }
  }

  async function saveAiSuggestion(apiId: number, prompt: string, suggestion: { expectedStatus: number | null; assertions: { jsonPath: string; operator: string; expected: unknown }[] }) {
    setBusy(true)
    try {
      await api.post('/test-cases', { apiId, name: prompt.trim().slice(0, 120) || 'AI generated test', expectedStatus: suggestion.expectedStatus, assertions: suggestion.assertions })
      await refresh()
    } catch { setError('Could not save this AI test suggestion.') }
    finally { setBusy(false) }
  }

  const section = location.pathname.split('/').filter(Boolean).at(-1) || 'overview'
  const sections: Record<string, { title: string; subtitle: string }> = {
    overview: { title: 'API overview', subtitle: 'A current view of your API tests and recent execution history.' },
    apis: { title: 'API workspace', subtitle: 'Build requests, save API definitions, and import OpenAPI JSON.' },
    environments: { title: 'Environments', subtitle: 'Manage environment values and encrypted secrets.' },
    tests: { title: 'Tests and collections', subtitle: 'Organize requests and run deterministic response checks.' },
    monitoring: { title: 'Monitoring and alerts', subtitle: 'Schedule checks and review real monitoring history.' },
    ai: { title: 'AI assistant', subtitle: 'Use Groq for reviewed test suggestions and execution analysis.' },
    reports: { title: 'Reports', subtitle: 'Review metrics calculated from saved execution data.' },
    account: { title: 'Account and security', subtitle: 'Manage your profile, password, and active session.' },
    admin: { title: 'Administration', subtitle: 'Manage API Sentinel user roles.' },
  }
  const page = sections[section]
  const workspace = !authReady ? <main className="content" role="status">Restoring your session…</main> : !token ? <Navigate to="/login" replace /> : !page ? <main className="content"><h1>Page not found</h1><p className="sub">That workspace route does not exist.</p><Link to="/workspace/overview">Return to overview</Link></main> : <div className="shell">
    <aside><div className="brand"><span className="mark">S</span><span>API <b>Sentinel</b></span></div><div className="navtitle">WORKSPACE</div><Link className={`nav ${section === 'overview' ? 'active' : ''}`} to="/workspace/overview">Overview</Link><div className="navtitle">TOOLS</div>{(['apis', 'environments', 'tests', 'monitoring', 'ai', 'reports'] as const).map(key => <Link className={`nav ${section === key ? 'active' : ''}`} key={key} to={`/workspace/${key}`}>{({ apis: 'API workspace', environments: 'Environments', tests: 'Tests and collections', monitoring: 'Monitoring and alerts', ai: 'AI assistant', reports: 'Reports' })[key]}</Link>)}<div className="navtitle">ACCOUNT</div><Link className={`nav ${section === 'account' ? 'active' : ''}`} to="/workspace/account">Profile and security</Link>{isAdmin && <><div className="navtitle">ADMIN</div><Link className={`nav ${section === 'admin' ? 'active' : ''}`} to="/workspace/admin">User administration</Link></>}<div className="aside-bottom"><span className="avatar">{(email || 'U').slice(0, 1).toUpperCase()}</span><span>{email || 'Workspace'}</span><button className="logout" onClick={logout}>Sign out</button></div></aside>
    <main className="content"><header><div><p className="eyebrow">{dashboardDate}</p><h1>{page.title}</h1><p className="sub">{page.subtitle}</p></div><span className="live"><i /> Workspace ready</span></header>
      {section === 'overview' && <><StatGrid items={[{ label: 'Saved APIs', value: apis.length, note: 'Your workspace' }, { label: 'Test cases', value: tests.length, note: 'Reusable checks' }, { label: 'Passed', value: runs.filter(r => r.passed).length, note: 'Latest 20 runs' }, { label: 'Failed', value: runs.filter(r => !r.passed).length, note: 'Latest 20 runs' }]} /><div className="columns"><ExecutionHistory items={runs} /></div></>}
      {section === 'apis' && <><div className="columns"><RequestBuilder url={url} setUrl={setUrl} method={method} setMethod={setMethod} body={body} setBody={setBody} contentType={contentType} setContentType={setContentType} headers={headersText} setHeaders={value => { setHeadersText(value); setHeadersDirty(true) }} queryParameters={queryParameters} setQueryParameters={setQueryParameters} expected={expected} setExpected={setExpected} timeoutSeconds={timeoutSeconds} setTimeoutSeconds={setTimeoutSeconds} authType={authType} setAuthType={v => { setAuthType(v); if (v === 'None') setCredential('') }} credential={credential} setCredential={setCredential} authHeaderName={authHeaderName} setAuthHeaderName={setAuthHeaderName} onRun={execute} result={result} busy={busy} /><ApiPanel items={apis} collections={collections} name={apiName} setName={setApiName} collectionId={collectionId} setCollectionId={setCollectionId} editing={editingApiId !== null} search={apiSearch} onSearch={searchApis} page={apiPage} total={apiTotal} onPage={changeApiPage} onSave={saveApi} onEdit={savedApi => savedApi ? editApi(savedApi) : cancelEdit()} onDelete={deleteApi} onExtraction={saveApiExtraction} /></div><div className="columns"><OpenApiImportPanel collections={collections} busy={busy} onImport={importOpenApi} /></div></>}
      {section === 'environments' && <div className="columns"><EnvironmentPanel items={environments} busy={busy} onCreate={createEnvironment} onActivate={activateEnvironment} onDelete={deleteEnvironment} onSaveVariable={saveEnvironmentVariable} onDeleteVariable={deleteEnvironmentVariable} /></div>}
      {section === 'tests' && <><div className="columns"><CollectionPanel items={collections} busy={busy} onCreate={createCollection} onRename={renameCollection} onRun={runCollection} onDelete={deleteCollection} /><TestCasePanel apis={apis} tests={tests} busy={busy} onCreate={createTest} onRun={runTest} onDelete={deleteTest} /></div><div className="columns"><ExecutionHistory items={runs} /></div></>}
      {section === 'monitoring' && <><div className="columns"><MonitoringPanel tests={tests} items={monitors} busy={busy} onCreate={createMonitor} onToggle={toggleMonitor} onDelete={deleteMonitor} onHistory={showMonitorHistory} onRunNow={runMonitorNow} /><AlertsPanel items={alerts} unread={unreadAlerts} onRead={markAlertRead} /></div>{history.length > 0 && <div className="columns"><MonitorHistoryPanel items={history} /></div>}</>}
      {section === 'ai' && <div className="columns"><AiAssistant busy={busy} apis={apis} runs={runs} onGenerate={generate} onAnalyze={analyzeRun} onRecommend={recommendTests} onSummary={summarizeRuns} onSaveSuggestion={saveAiSuggestion} /></div>}
      {section === 'reports' && <div className="columns"><Suspense fallback={<div className="panel" role="status">Loading reports…</div>}><ReportPanel report={report} onRangeChange={changeReportRange} /></Suspense></div>}
      {section === 'account' && <div className="columns"><AccountPanel email={email} busy={busy} onChangePassword={changePassword} onChangeEmail={changeEmail} /></div>}
      {section === 'admin' && (isAdmin ? <div className="columns"><AdminPanel /></div> : <div className="panel"><h2>Administrator access required</h2><p className="sub">This account does not have the Admin role.</p></div>)}
      {error && <div className="toast" role="alert" onClick={() => setError('')}>{error}<button aria-label="Dismiss">×</button></div>}
    </main>
  </div>
  return <Routes>
    <Route path="/login" element={!authReady ? <main className="content" role="status">Restoring your session…</main> : token ? <Navigate to="/workspace/overview" replace /> : <AuthForm onSubmit={authenticate} error={error} />} />
    <Route path="/" element={!authReady ? <main className="content" role="status">Restoring your session…</main> : <Navigate to={token ? '/workspace/overview' : '/login'} replace />} />
    <Route path="/workspace/:section" element={workspace} />
    <Route path="*" element={<main className="content"><h1>Page not found</h1><p className="sub">That route does not exist.</p><Link to={token ? '/workspace/overview' : '/login'}>Return to API Sentinel</Link></main>} />
  </Routes>
}