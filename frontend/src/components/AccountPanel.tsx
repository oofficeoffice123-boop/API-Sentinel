import { useState } from 'react'
import Panel from './Panel'

export default function AccountPanel({ email, busy, onChangePassword, onChangeEmail }: { email: string; busy: boolean; onChangePassword: (currentPassword: string, newPassword: string) => Promise<boolean>; onChangeEmail: (email: string, currentPassword: string) => Promise<boolean> }) {
  const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState(''); const [newEmail, setNewEmail] = useState(email); const [message, setMessage] = useState('')
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setMessage('')
    if (await onChangePassword(currentPassword, newPassword)) { setCurrentPassword(''); setNewPassword(''); setMessage('Password changed. Sign in again with the new password.') }
  }
  async function updateEmail(e: React.FormEvent) {
    e.preventDefault(); setMessage('')
    if (await onChangeEmail(newEmail, currentPassword)) { setCurrentPassword(''); setMessage('Email updated. Sign in again with the new address.') }
  }
  return <Panel title="Account and security" description="Review your profile and update account credentials.">
    <p className="account-email"><b>Signed in as</b><span>{email || 'Loading profile…'}</span></p>
    <form className="account-password" onSubmit={updateEmail}><label>New email<input type="email" required maxLength={254} value={newEmail} onChange={e => setNewEmail(e.target.value)} /></label><label>Confirm with password<input type="password" autoComplete="current-password" required value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label><button className="secondary" disabled={busy || !newEmail.trim()}>Update email</button></form>
    <form className="account-password" onSubmit={submit}><label>Current password<input type="password" autoComplete="current-password" required value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label><label>New password<input type="password" autoComplete="new-password" minLength={10} maxLength={200} required value={newPassword} onChange={e => setNewPassword(e.target.value)} /></label><button className="secondary" disabled={busy || newPassword.length < 10}>Change password</button>{message && <span className="ok" role="status">{message}</span>}</form>
  </Panel>
}