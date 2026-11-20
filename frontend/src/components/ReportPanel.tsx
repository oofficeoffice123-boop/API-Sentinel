import { useState } from 'react'
import { CategoryScale, Chart as ChartJS, Legend, LinearScale, LineElement, PointElement, Tooltip } from 'chart.js'
import { Line } from 'react-chartjs-2'
import type { DashboardReport } from '../types'
import Panel from './Panel'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend)

export default function ReportPanel({ report, onRangeChange }: { report: DashboardReport | null; onRangeChange: (days: number) => void }) {
  const [days, setDays] = useState(14)
  function change(value: number) { setDays(value); onRangeChange(value) }
  if (!report) return <Panel title="Reports" description="Execution and reliability metrics."><p className="empty">Loading report data…</p></Panel>
  return <Panel title="Reports and analytics" description={`${report.executions} real executions in the selected period.`}>
    <div className="saveform"><select aria-label="Report date range" value={days} onChange={e => change(Number(e.target.value))}><option value={7}>Last 7 days</option><option value={14}>Last 14 days</option><option value={30}>Last 30 days</option><option value={90}>Last 90 days</option></select><span className="hint">Pass rate {report.passRate}% · Availability {report.monitorAvailability}% · Avg. {Math.round(report.averageResponseMs)} ms</span></div>
    <div className="chart"><Line data={{ labels: report.daily.map(x => new Date(x.date).toLocaleDateString()), datasets: [{ label: 'Runs', data: report.daily.map(x => x.count), borderColor: '#315ee7', backgroundColor: '#315ee71a', tension: .25 }, { label: 'Passed', data: report.daily.map(x => x.passed), borderColor: '#22a575', tension: .25 }] }} options={{ responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } }, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } }} /></div>
    <div className="columns reportlists"><div><b className="reportlabel">Slowest endpoints</b>{report.slowest.map(x => <p className="reportline" key={x.url}>{x.url}<span>{Math.round(x.averageMs)} ms · {x.failures} failed</span></p>)}{report.slowest.length === 0 && <p className="empty">No response data for this date range.</p>}</div><div><b className="reportlabel">Recent failures</b>{report.recentFailures.map(x => <p className="reportline" key={x.id}>{x.status || 'Error'} · {x.url}<span>{x.durationMs} ms</span></p>)}{report.recentFailures.length === 0 && <p className="empty">No failed executions.</p>}</div></div>
  </Panel>
}