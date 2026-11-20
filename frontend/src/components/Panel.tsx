import type { ReactNode } from 'react'

export default function Panel({ title, description, tag, children, className = '' }: { title: string; description: string; tag?: string; children: ReactNode; className?: string }) {
  return <section className={`panel ${className}`}><div className="panelhead"><div><h2>{title}</h2><p>{description}</p></div>{tag && <span className="tag">{tag}</span>}</div>{children}</section>
}