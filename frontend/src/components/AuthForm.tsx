import { useState, type FormEvent } from 'react'

export default function AuthForm({ onSubmit, error }: { onSubmit: (mode: 'login' | 'register', email: string, password: string) => Promise<void>; error: string }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mode, setMode] = useState<'login' | 'register'>('login')
  async function submit(e: FormEvent) { e.preventDefault(); await onSubmit(mode, email, password) }
  return <main className="auth"><form className="authbox" onSubmit={submit}>
    <div className="brand"><span className="mark">S</span><span>API <b>Sentinel</b></span></div>
    <p className="eyebrow">API RELIABILITY WORKSPACE</p><h1>{mode === 'login' ? 'Welcome back' : 'Create your account'}</h1>
    <label>Email<input type="email" required maxLength={254} value={email} onChange={e => setEmail(e.target.value)} /></label>
    <label>Password<input type="password" minLength={10} maxLength={200} required value={password} onChange={e => setPassword(e.target.value)} /></label>
    {error && <p className="error">{error}</p>}
    <button className="primary full">{mode === 'login' ? 'Sign in' : 'Create account'}</button>
    <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>{mode === 'login' ? 'New here? Create an account' : 'Already registered? Sign in'}</button>
  </form></main>
}