import { useCallback, useEffect, useState } from 'react'
import { api } from '../services/api'
import Panel from './Panel'

type AdminUser = { id: number; email: string; roles: string[] }

export default function AdminPanel() {
  const [items, setItems] = useState<AdminUser[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      const response = await api.get('/admin/users', { params: { page, search: search || undefined } })
      setItems(response.data.items); setTotal(response.data.total); setError('')
    } catch { setError('Could not load administrator user records.') }
  }, [page, search])

  useEffect(() => { const request = window.setTimeout(() => { void load() }, 0); return () => window.clearTimeout(request) }, [load])

  async function changeRole(user: AdminUser, role: 'User' | 'Admin') {
    try { await api.put(`/admin/users/${user.id}/role`, { role }); await load() }
    catch { setError('Could not change this user role. The final administrator cannot be removed.') }
  }

  return <Panel title="User administration" description="Review registered accounts and assign workspace roles.">
    <div className="saveform"><input aria-label="Search users" placeholder="Search by email" value={search} onChange={event => { setSearch(event.target.value); setPage(1) }} /></div>
    {error && <p className="error" role="alert">{error}</p>}
    <div className="list">{items.map(user => <div className="listrow" key={user.id}><div><b>{user.email}</b><small>Account {user.id} · {user.roles.join(', ')}</small></div><select aria-label={`Role for ${user.email}`} value={user.roles.includes('Admin') ? 'Admin' : 'User'} onChange={event => void changeRole(user, event.target.value as 'User' | 'Admin')}><option value="User">User</option><option value="Admin">Admin</option></select></div>)}{items.length === 0 && !error && <p className="empty">No user accounts found.</p>}</div>
    {total > 20 && <div className="saveform"><button className="secondary smallbutton" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page} of {Math.ceil(total / 20)} · {total} users</span><button className="secondary smallbutton" disabled={page >= Math.ceil(total / 20)} onClick={() => setPage(page + 1)}>Next</button></div>}
  </Panel>
}